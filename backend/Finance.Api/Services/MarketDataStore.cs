using System.Globalization;
using Finance.Engine;
using Microsoft.Extensions.Options;

namespace Finance.Api.Services;

/// <summary>
/// Market history held in memory: the seed file's months, then the live months the updater publishes.
/// The live months are also written to a snapshot file, so a restart has them before its first fetch finishes.
/// </summary>
public sealed class MarketDataStore
{
    sealed record State(IReadOnlyList<MarketMonth> Months, int HistoryMonths, int LiveMonths);

    readonly IReadOnlyList<MarketMonth> _history;
    readonly string _snapshotPath;
    readonly ILogger<MarketDataStore> _logger;
    volatile State _state;

    public MarketDataStore(IOptions<MarketDataOptions> options, ILogger<MarketDataStore> logger)
    {
        _logger = logger;
        _history = ReadCsv(Path.Combine(AppContext.BaseDirectory, "SeedData", "history.csv"));
        _snapshotPath = options.Value.SnapshotPath is { Length: > 0 } path ? path : DefaultSnapshotPath();
        _state = Combine(ReadSnapshot(options.Value.LiveStartMonth));
    }

    public IReadOnlyList<MarketMonth> Months => _state.Months;
    public int HistoryMonths => _state.HistoryMonths;
    public int LiveMonths => _state.LiveMonths;

    /// <summary>Replaces the live months. Seed months from the first live month onwards are dropped.</summary>
    public void PublishLive(IReadOnlyList<MarketMonth> live)
    {
        _state = Combine(live);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_snapshotPath)!);
            var temp = _snapshotPath + ".tmp";
            WriteCsv(temp, live);
            File.Move(temp, _snapshotPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not write the live market data snapshot to {Path}", _snapshotPath);
        }
    }

    State Combine(IReadOnlyList<MarketMonth> live)
    {
        if (live.Count == 0) return new State(_history, _history.Count, 0);
        var history = _history.TakeWhile(m => Index(m) < Index(live[0])).ToList();
        return new State([.. history, .. live], history.Count, live.Count);
    }

    static int Index(MarketMonth m) => m.Year * 12 + m.Month;

    /// <summary>The snapshot is used only if it starts at the configured first live month.</summary>
    IReadOnlyList<MarketMonth> ReadSnapshot(string liveStartMonth)
    {
        try
        {
            if (!File.Exists(_snapshotPath)) return [];
            var live = ReadCsv(_snapshotPath);
            if (live.Count > 0 && live[0].Label == liveStartMonth)
            {
                _logger.LogInformation("Loaded {Count} live months to {Last} from {Path}", live.Count, live[^1].Label, _snapshotPath);
                return live;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or IndexOutOfRangeException)
        {
            _logger.LogWarning(ex, "Ignoring the unreadable live market data snapshot at {Path}", _snapshotPath);
        }
        return [];
    }

    static string DefaultSnapshotPath()
    {
        // On Azure App Service for Linux this is under /home, which survives restarts and deployments
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root.Length > 0 ? root : Path.GetTempPath(), "finance-simulator", "live-months.csv");
    }

    /// <summary>Reads months in the format of SeedData/history.csv (built by tools/build_history.py).</summary>
    public static List<MarketMonth> ReadCsv(string path)
    {
        var inv = CultureInfo.InvariantCulture;
        return File.ReadLines(path)
            .Skip(1)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Split(','))
            .Select(f =>
            {
                var month = DateOnly.ParseExact(f[0] + "-01", "yyyy-MM-dd", inv);
                return new MarketMonth(month.Year, month.Month, double.Parse(f[1], inv), double.Parse(f[2], inv), double.Parse(f[3], inv), double.Parse(f[4], inv));
            })
            .ToList();
    }

    static void WriteCsv(string path, IEnumerable<MarketMonth> months)
    {
        var inv = CultureInfo.InvariantCulture;
        File.WriteAllLines(path, months
            .Select(m => string.Join(',', m.Label, m.Equity.ToString("R", inv), m.Bond.ToString("R", inv), m.Cash.ToString("R", inv), m.Inflation.ToString("R", inv)))
            .Prepend("month,equity,bond,cash,inflation"));
    }
}
