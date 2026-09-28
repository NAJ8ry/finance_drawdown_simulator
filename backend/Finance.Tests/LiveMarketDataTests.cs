using Finance.Api.Services;

namespace Finance.Tests;

public class LiveMarketDataTests
{
    [Fact]
    public void Yahoo_monthly_bars_map_to_months_and_skip_current_month()
    {
        // Bars stamped at London midnight: 2026-06-30T23:00Z is July (BST), 2026-02-01T00:00Z is February (GMT)
        var json = """
        {"chart":{"result":[{"meta":{"currency":"GBp"},
          "timestamp":[1769904000,1782860400,1788217200,1790640000],
          "indicators":{"quote":[{"close":[1,2,3,4]}],"adjclose":[{"adjclose":[100.0,110.0,null,130.0]}]}}]}}
        """;
        var map = LiveMarketDataSource.ParseYahooMonthly(json, new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(100.0, map[new DateOnly(2026, 2, 1)]);
        Assert.Equal(110.0, map[new DateOnly(2026, 7, 1)]);
        Assert.False(map.ContainsKey(new DateOnly(2026, 9, 1))); // null value
        Assert.Equal(2, map.Count);                                // September (current) excluded
    }

    [Fact]
    public void Ons_csv_keeps_only_monthly_rows()
    {
        var csv = "\"Title\",\"CPI INDEX 00: ALL ITEMS 2015=100\"\n\"2025\",\"140.1\"\n\"2025 Q4\",\"141.0\"\n\"2026 JUL\",\"142.9\"\n\"2026 AUG\",\"143.6\"\n";
        var map = LiveMarketDataSource.ParseOnsMonthly(csv);
        Assert.Equal(2, map.Count);
        Assert.Equal(143.6, map[new DateOnly(2026, 8, 1)]);
    }

    [Fact]
    public void Boe_csv_parses_dates_and_rates()
    {
        var csv = "DATE,IUMABEDR\n30 Jun 2026,3.75\n31 Jul 2026,3.5\n";
        var map = LiveMarketDataSource.ParseBoeMonthly(csv);
        Assert.Equal(3.5, map[new DateOnly(2026, 7, 1)]);
        Assert.Equal(2, map.Count);
    }

    [Fact]
    public void Returns_stop_at_first_gap_and_use_previous_month_rate()
    {
        SortedDictionary<DateOnly, double> Series(params (int m, double v)[] points) =>
            new(points.ToDictionary(p => new DateOnly(2010, p.m, 1), p => p.v));

        var series = new LiveSeries(
            Series((1, 100), (2, 110), (3, 121), (4, 130)),
            Series((1, 50), (2, 50), (3, 51)),
            Series((1, 100), (2, 101), (3, 102), (4, 103)),
            Series((1, 6), (2, 12), (3, 12)));

        var rows = series.ToReturns(new DateOnly(2010, 2, 1));
        Assert.Equal(2, rows.Count); // April missing a bond price
        Assert.Equal(0.1, rows[0].Equity, 9);
        Assert.Equal(0.005, rows[0].Cash, 9);   // January's 6% / 12
        Assert.Equal(0.01, rows[1].Cash, 9);    // February's 12% / 12
        Assert.Equal(0.01, rows[0].Inflation, 9);
    }

    [Fact]
    public void Validation_flags_implausible_values()
    {
        var problems = LiveSeries.Validate([(new DateOnly(2020, 3, 1), -0.5, 0, 0.001, 0.001)]);
        Assert.Single(problems);
    }
}
