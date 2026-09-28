using Finance.Api.Data;
using Finance.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Controllers;

[ApiController]
[Route("api/market")]
public class MarketController(FinanceDbContext db, MarketDataStore store, MarketDataUpdater updater) : ControllerBase
{
    public sealed record SourceInfo(string Series, string Period, string Source);

    public sealed record AssetStats(string Asset, double NominalReturn, double RealReturn, double WorstMonth, double BestMonth);

    public sealed record MarketSummary(
        string? FirstMonth,
        string? LastMonth,
        int Months,
        int HistoryMonths,
        int LiveMonths,
        DataUpdateLog? LastUpdate,
        DataUpdateLog? LastSuccessfulUpdate,
        double AnnualInflation,
        List<AssetStats> Assets,
        List<SourceInfo> Sources);

    static readonly List<SourceInfo> Sources =
    [
        new("Equities", "1871 – 2009", "US S&P Composite total return (Robert Shiller) converted to GBP with the Bank of England $/£ series. Used as a proxy for global equities: no free monthly global index exists before ~1970."),
        new("Equities", "2010 – now", "iShares Core MSCI World ETF (SWDA.L, accumulating, GBP) via Yahoo Finance."),
        new("Bonds", "1871 – 1934", "UK consols, total return derived from Bank of England consol yields."),
        new("Bonds", "1935 – 2009", "UK 10-year gilts, total return derived from Bank of England 10-year yields."),
        new("Bonds", "2010 – now", "iShares Core UK Gilts ETF (IGLT.L) via Yahoo Finance. UK gilts stand in for GBP-hedged global bonds."),
        new("Cash", "1871 – 1922", "Bank of England prime short-term bill rate."),
        new("Cash", "1923 – 2009", "UK Treasury bill rate (Bank of England)."),
        new("Cash", "2010 – now", "Bank of England Bank Rate, monthly average (IUMABEDR)."),
        new("Inflation", "1871 – 1914", "UK CPI, annual (Bank of England millennium dataset), interpolated monthly."),
        new("Inflation", "1914 – 2009", "UK CPI, monthly spliced series (Bank of England millennium dataset)."),
        new("Inflation", "2010 – now", "ONS CPI index (D7BT)."),
    ];

    [HttpGet("summary")]
    public async Task<MarketSummary> Summary(CancellationToken ct)
    {
        var history = await store.GetAsync(ct);
        var counts = await db.MarketMonths.GroupBy(r => r.Source).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var lastUpdate = await db.DataUpdateLogs.OrderByDescending(l => l.RunAt).FirstOrDefaultAsync(ct);
        var lastOk = await db.DataUpdateLogs.Where(l => l.Success).OrderByDescending(l => l.RunAt).FirstOrDefaultAsync(ct);

        double Annualise(IEnumerable<double> monthly)
        {
            var list = monthly.ToList();
            if (list.Count == 0) return 0;
            var logSum = list.Sum(r => Math.Log(1 + r));
            return Math.Exp(logSum * 12 / list.Count) - 1;
        }

        AssetStats Stats(string name, Func<Engine.MarketMonth, double> pick) => new(
            name,
            Annualise(history.Select(pick)),
            Annualise(history.Select(h => (1 + pick(h)) / (1 + h.Inflation) - 1)),
            history.Count > 0 ? history.Min(pick) : 0,
            history.Count > 0 ? history.Max(pick) : 0);

        return new MarketSummary(
            history.Count > 0 ? history[0].Label : null,
            history.Count > 0 ? history[^1].Label : null,
            history.Count,
            counts.FirstOrDefault(c => c.Key == "history")?.Count ?? 0,
            counts.FirstOrDefault(c => c.Key == "live")?.Count ?? 0,
            lastUpdate,
            lastOk,
            Annualise(history.Select(h => h.Inflation)),
            [Stats("Equities", h => h.Equity), Stats("Bonds", h => h.Bond), Stats("Cash", h => h.Cash)],
            Sources);
    }

    /// <summary>Full monthly history (nominal GBP returns and UK inflation).</summary>
    [HttpGet("months")]
    public async Task<IEnumerable<object>> Months(CancellationToken ct)
    {
        var history = await store.GetAsync(ct);
        return history.Select(h => new { month = h.Label, h.Equity, h.Bond, h.Cash, h.Inflation });
    }

    [HttpGet("updates")]
    public async Task<List<DataUpdateLog>> Updates(CancellationToken ct) =>
        await db.DataUpdateLogs.OrderByDescending(l => l.RunAt).Take(20).ToListAsync(ct);

    /// <summary>Fetch the latest month(s) from the live sources now.</summary>
    [HttpPost("refresh")]
    public async Task<DataUpdateLog> Refresh(CancellationToken ct) => await updater.RunOnceAsync(ct);
}
