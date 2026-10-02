using System.Globalization;
using Finance.Engine;
using Microsoft.Extensions.Options;

namespace Finance.Api.Services;

public class DataUpdateLog
{
    public long Id { get; set; }
    public DateTime RunAt { get; set; }
    public bool Success { get; set; }
    public int MonthsWritten { get; set; }
    public string? LastMonth { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>
/// Keeps market data current: on startup and then every <see cref="MarketDataOptions.UpdateIntervalHours"/>,
/// fetches the live sources, validates them and replaces the "live" months in the store.
/// </summary>
public sealed class MarketDataUpdater(
    IServiceScopeFactory scopes,
    MarketDataStore store,
    IOptions<MarketDataOptions> options,
    ILogger<MarketDataUpdater> logger) : BackgroundService
{
    const int LogsKept = 20;

    readonly SemaphoreSlim _running = new(1, 1);
    readonly object _logLock = new();
    readonly List<DataUpdateLog> _logs = [];
    long _lastId;
    DataUpdateLog? _lastSuccessful;

    /// <summary>The most recent runs since the API started, newest first.</summary>
    public List<DataUpdateLog> RecentLogs
    {
        get { lock (_logLock) return [.. _logs]; }
    }

    public DataUpdateLog? LastSuccessful
    {
        get { lock (_logLock) return _lastSuccessful; }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.UpdateOnStartup)
            await Delay(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Market data update failed");
            }
            await Delay(stoppingToken);
        }
    }

    Task Delay(CancellationToken ct) =>
        Task.Delay(TimeSpan.FromHours(Math.Max(0.01, options.Value.UpdateIntervalHours)), ct);

    public async Task<DataUpdateLog> RunOnceAsync(CancellationToken ct)
    {
        await _running.WaitAsync(ct);
        try
        {
            using var scope = scopes.CreateScope();
            var source = scope.ServiceProvider.GetRequiredService<LiveMarketDataSource>();
            var log = await UpdateAsync(source, ct);
            lock (_logLock)
            {
                log.Id = ++_lastId;
                _logs.Insert(0, log);
                if (_logs.Count > LogsKept) _logs.RemoveAt(_logs.Count - 1);
                if (log.Success) _lastSuccessful = log;
            }
            logger.Log(log.Success ? LogLevel.Information : LogLevel.Warning, "Market data update: {Message}", log.Message);
            return log;
        }
        finally
        {
            _running.Release();
        }
    }

    async Task<DataUpdateLog> UpdateAsync(LiveMarketDataSource source, CancellationToken ct)
    {
        var log = new DataUpdateLog { RunAt = DateTime.UtcNow };
        var start = DateOnly.ParseExact(options.Value.LiveStartMonth + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);

        LiveSeries series;
        try
        {
            series = await source.FetchAsync(DateTime.UtcNow, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            log.Message = $"Could not fetch live data: {ex.Message}";
            return log;
        }

        var rows = series.ToReturns(start);
        if (rows.Count == 0)
        {
            log.Message = $"No live months could be built from {start:yyyy-MM} (a source is missing data).";
            return log;
        }

        var problems = LiveSeries.Validate(rows);
        if (problems.Count > 0)
        {
            log.Message = "Validation failed, data not published: " + string.Join("; ", problems.Take(5));
            return log;
        }

        var existing = store.LiveMonths;
        if (rows.Count < existing)
        {
            log.Message = $"Live sources returned {rows.Count} months but {existing} are stored; keeping stored data.";
            return log;
        }

        store.PublishLive(rows.Select(r => new MarketMonth(r.Month.Year, r.Month.Month, r.Equity, r.Bond, r.Cash, r.Inflation)).ToList());

        log.Success = true;
        log.MonthsWritten = rows.Count;
        log.LastMonth = rows[^1].Month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        log.Message = rows.Count > existing
            ? $"Added {rows.Count - existing} new month(s); data now runs to {log.LastMonth}."
            : $"Up to date; data runs to {log.LastMonth}.";
        return log;
    }
}
