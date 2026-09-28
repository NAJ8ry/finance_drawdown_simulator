using Finance.Api.Data;
using Finance.Engine;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Services;

/// <summary>In-memory cache of the market history held in Postgres. Invalidated whenever the updater writes.</summary>
public sealed class MarketDataStore(IServiceScopeFactory scopes)
{
    readonly SemaphoreSlim _lock = new(1, 1);
    IReadOnlyList<MarketMonth>? _cache;

    public async Task<IReadOnlyList<MarketMonth>> GetAsync(CancellationToken ct = default)
    {
        if (_cache is { } cached) return cached;
        await _lock.WaitAsync(ct);
        try
        {
            if (_cache is { } again) return again;
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var rows = await db.MarketMonths.AsNoTracking().OrderBy(r => r.Month).ToListAsync(ct);
            _cache = rows.Select(r => new MarketMonth(r.Month.Year, r.Month.Month, r.Equity, r.Bond, r.Cash, r.Inflation))
                .ToList();
            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidate() => _cache = null;
}

/// <summary>Loads SeedData/history.csv (built by tools/build_history.py) into the database.</summary>
public static class HistorySeeder
{
    public static async Task SeedAsync(FinanceDbContext db, string csvPath, ILogger logger, CancellationToken ct = default)
    {
        var rows = ReadCsv(csvPath);
        var existing = await db.MarketMonths.CountAsync(r => r.Source == "history", ct);
        if (existing == rows.Count) return;

        logger.LogInformation("Seeding {Count} months of historical market data", rows.Count);
        await db.MarketMonths.Where(r => r.Source == "history").ExecuteDeleteAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var r in rows) r.UpdatedAt = now;
        db.MarketMonths.AddRange(rows);
        await db.SaveChangesAsync(ct);
    }

    public static List<MarketMonthRow> ReadCsv(string path)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return File.ReadLines(path)
            .Skip(1)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Split(','))
            .Select(f => new MarketMonthRow
            {
                Month = DateOnly.ParseExact(f[0] + "-01", "yyyy-MM-dd", inv),
                Equity = double.Parse(f[1], inv),
                Bond = double.Parse(f[2], inv),
                Cash = double.Parse(f[3], inv),
                Inflation = double.Parse(f[4], inv),
                Source = "history",
            })
            .ToList();
    }
}
