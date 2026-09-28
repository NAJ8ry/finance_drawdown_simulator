using System.Globalization;
using Finance.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Finance.Api.Services;

/// <summary>
/// Keeps market data current: on startup and then every <see cref="MarketDataOptions.UpdateIntervalHours"/>,
/// fetches the live sources, validates them and replaces the "live" months in the database.
/// </summary>
public sealed class MarketDataUpdater(
    IServiceScopeFactory scopes,
    MarketDataStore store,
    IOptions<MarketDataOptions> options,
    ILogger<MarketDataUpdater> logger) : BackgroundService
{
    readonly SemaphoreSlim _running = new(1, 1);

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
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var source = scope.ServiceProvider.GetRequiredService<LiveMarketDataSource>();
            var log = await UpdateAsync(db, source, ct);
            db.DataUpdateLogs.Add(log);
            await db.SaveChangesAsync(ct);
            if (log.Success) store.Invalidate();
            logger.Log(log.Success ? LogLevel.Information : LogLevel.Warning, "Market data update: {Message}", log.Message);
            return log;
        }
        finally
        {
            _running.Release();
        }
    }

    async Task<DataUpdateLog> UpdateAsync(FinanceDbContext db, LiveMarketDataSource source, CancellationToken ct)
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

        var existing = await db.MarketMonths.CountAsync(r => r.Source == "live", ct);
        if (rows.Count < existing)
        {
            log.Message = $"Live sources returned {rows.Count} months but {existing} are stored; keeping stored data.";
            return log;
        }

        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.MarketMonths.Where(r => r.Month >= start).ExecuteDeleteAsync(ct);
        db.MarketMonths.AddRange(rows.Select(r => new MarketMonthRow
        {
            Month = r.Month,
            Equity = r.Equity,
            Bond = r.Bond,
            Cash = r.Cash,
            Inflation = r.Inflation,
            Source = "live",
            UpdatedAt = now,
        }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        log.Success = true;
        log.MonthsWritten = rows.Count;
        log.LastMonth = rows[^1].Month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        log.Message = rows.Count > existing
            ? $"Added {rows.Count - existing} new month(s); data now runs to {log.LastMonth}."
            : $"Up to date; data runs to {log.LastMonth}.";
        return log;
    }
}
