using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Finance.Api.Services;

public sealed class MarketDataOptions
{
    /// <summary>First month taken from live sources; earlier months come from the seed file.</summary>
    public string LiveStartMonth { get; set; } = "2010-01";
    public double UpdateIntervalHours { get; set; } = 24;
    public bool UpdateOnStartup { get; set; } = true;

    /// <summary>File the fetched live months are kept in between restarts. Defaults to the local application data folder.</summary>
    public string? SnapshotPath { get; set; }

    /// <summary>iShares Core MSCI World (accumulating), London listing, priced in GBp.</summary>
    public string EquityTicker { get; set; } = "SWDA.L";

    /// <summary>iShares Core UK Gilts, London listing, priced in GBP.</summary>
    public string BondTicker { get; set; } = "IGLT.L";

    public string YahooChartUrl { get; set; } = "https://query1.finance.yahoo.com/v8/finance/chart/{0}?period1={1}&period2={2}&interval=1mo&events=div,split";
    public string OnsCpiUrl { get; set; } = "https://www.ons.gov.uk/generator?format=csv&uri=/economy/inflationandpriceindices/timeseries/d7bt/mm23";
    public string BoeBankRateUrl { get; set; } = "https://www.bankofengland.co.uk/boeapps/database/_iadb-fromshowcolumns.asp?csv.x=yes&Datefrom=01/Jan/2005&Dateto=now&SeriesCodes=IUMABEDR&CSVF=TN&UsingCodes=Y&VPD=Y&VFD=N";
}

/// <summary>Fetches and parses the free monthly sources used after the seed history ends.</summary>
public sealed class LiveMarketDataSource(HttpClient http, Microsoft.Extensions.Options.IOptions<MarketDataOptions> options)
{
    readonly MarketDataOptions _o = options.Value;

    public async Task<LiveSeries> FetchAsync(DateTime nowUtc, CancellationToken ct)
    {
        var from = new DateTimeOffset(2005, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var to = new DateTimeOffset(nowUtc).ToUnixTimeSeconds();
        var equityTask = http.GetStringAsync(string.Format(CultureInfo.InvariantCulture, _o.YahooChartUrl, Uri.EscapeDataString(_o.EquityTicker), from, to), ct);
        var bondTask = http.GetStringAsync(string.Format(CultureInfo.InvariantCulture, _o.YahooChartUrl, Uri.EscapeDataString(_o.BondTicker), from, to), ct);
        var cpiTask = http.GetStringAsync(_o.OnsCpiUrl, ct);
        var rateTask = http.GetStringAsync(_o.BoeBankRateUrl, ct);
        await Task.WhenAll(equityTask, bondTask, cpiTask, rateTask);

        return new LiveSeries(
            ParseYahooMonthly(equityTask.Result, nowUtc),
            ParseYahooMonthly(bondTask.Result, nowUtc),
            ParseOnsMonthly(cpiTask.Result),
            ParseBoeMonthly(rateTask.Result));
    }

    /// <summary>
    /// Month-end adjusted closes (dividends reinvested) keyed by month. The current, unfinished month is excluded.
    /// </summary>
    public static SortedDictionary<DateOnly, double> ParseYahooMonthly(string json, DateTime nowUtc)
    {
        using var doc = JsonDocument.Parse(json);
        var result = doc.RootElement.GetProperty("chart").GetProperty("result")[0];
        var timestamps = result.GetProperty("timestamp").EnumerateArray().Select(t => t.GetInt64()).ToArray();
        var indicators = result.GetProperty("indicators");
        JsonElement values;
        if (indicators.TryGetProperty("adjclose", out var adj) && adj.GetArrayLength() > 0)
            values = adj[0].GetProperty("adjclose");
        else
            values = indicators.GetProperty("quote")[0].GetProperty("close");

        var current = new DateOnly(nowUtc.Year, nowUtc.Month, 1);
        var map = new SortedDictionary<DateOnly, double>();
        var i = 0;
        foreach (var v in values.EnumerateArray())
        {
            var ts = timestamps[i++];
            if (v.ValueKind != JsonValueKind.Number) continue;
            // Bars are stamped at local midnight on the 1st; +12h keeps them in the right month whatever the time zone
            var d = DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime.AddHours(12);
            var month = new DateOnly(d.Year, d.Month, 1);
            if (month >= current) continue;
            map[month] = v.GetDouble();
        }
        return map;
    }

    static readonly Regex OnsMonthRow = new("^\"(\\d{4}) ([A-Z]{3})\",\"([0-9.]+)\"", RegexOptions.Compiled);

    /// <summary>ONS time series CSV: rows like "2026 AUG","143.6". Annual and quarterly rows are ignored.</summary>
    public static SortedDictionary<DateOnly, double> ParseOnsMonthly(string csv)
    {
        var map = new SortedDictionary<DateOnly, double>();
        foreach (var line in csv.Split('\n'))
        {
            var m = OnsMonthRow.Match(line.Trim());
            if (!m.Success) continue;
            var month = DateTime.ParseExact(m.Groups[2].Value, "MMM", CultureInfo.InvariantCulture).Month;
            map[new DateOnly(int.Parse(m.Groups[1].Value), month, 1)] =
                double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        }
        return map;
    }

    /// <summary>Bank of England IADB CSV: rows like "30 Jun 2026,3.75" (monthly average Bank Rate, % p.a.).</summary>
    public static SortedDictionary<DateOnly, double> ParseBoeMonthly(string csv)
    {
        var map = new SortedDictionary<DateOnly, double>();
        foreach (var line in csv.Split('\n'))
        {
            var parts = line.Trim().Split(',');
            if (parts.Length < 2) continue;
            if (!DateTime.TryParseExact(parts[0], "dd MMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var rate)) continue;
            map[new DateOnly(date.Year, date.Month, 1)] = rate;
        }
        return map;
    }
}

/// <summary>Raw live levels: equity and bond total-return prices, CPI index, Bank Rate (% p.a.).</summary>
public sealed record LiveSeries(
    SortedDictionary<DateOnly, double> Equity,
    SortedDictionary<DateOnly, double> Bond,
    SortedDictionary<DateOnly, double> Cpi,
    SortedDictionary<DateOnly, double> BankRate)
{
    /// <summary>
    /// Converts levels to monthly returns from <paramref name="start"/> until the first month any series is missing.
    /// </summary>
    public List<(DateOnly Month, double Equity, double Bond, double Cash, double Inflation)> ToReturns(DateOnly start)
    {
        var list = new List<(DateOnly, double, double, double, double)>();
        for (var m = start; ; m = m.AddMonths(1))
        {
            var prev = m.AddMonths(-1);
            if (!Equity.TryGetValue(m, out var e1) || !Equity.TryGetValue(prev, out var e0)) break;
            if (!Bond.TryGetValue(m, out var b1) || !Bond.TryGetValue(prev, out var b0)) break;
            if (!Cpi.TryGetValue(m, out var c1) || !Cpi.TryGetValue(prev, out var c0)) break;
            if (!BankRate.TryGetValue(prev, out var rate)) break;
            list.Add((m, e1 / e0 - 1, b1 / b0 - 1, rate / 100 / 12, c1 / c0 - 1));
        }
        return list;
    }

    /// <summary>Sanity checks on fetched returns; returns a list of problems (empty if OK).</summary>
    public static List<string> Validate(IEnumerable<(DateOnly Month, double Equity, double Bond, double Cash, double Inflation)> rows)
    {
        var problems = new List<string>();
        foreach (var r in rows)
        {
            if (Math.Abs(r.Equity) > 0.4) problems.Add($"{r.Month:yyyy-MM}: equity return {r.Equity:P1} outside ±40%");
            if (Math.Abs(r.Bond) > 0.2) problems.Add($"{r.Month:yyyy-MM}: bond return {r.Bond:P1} outside ±20%");
            if (r.Cash is < -0.01 or > 0.02) problems.Add($"{r.Month:yyyy-MM}: cash return {r.Cash:P2} out of range");
            if (Math.Abs(r.Inflation) > 0.05) problems.Add($"{r.Month:yyyy-MM}: inflation {r.Inflation:P1} outside ±5%");
        }
        return problems;
    }
}
