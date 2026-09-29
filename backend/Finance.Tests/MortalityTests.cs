using Finance.Engine;

namespace Finance.Tests;

public class MortalityTests
{
    /// <summary>Every year, 10% of men and 20% of women die: easy to check by hand.</summary>
    static Mortality Simple() => new(
        from year in Enumerable.Range(2000, 100)
        from age in Enumerable.Range(16, 85)
        select (year, age, 0.1, 0.2));

    [Fact]
    public void Survival_compounds_the_yearly_rates()
    {
        var s = Simple().Survival(LifeTable.Male, 60, 2026, 60, 63);
        Assert.Equal([1, 0.9, 0.81, 0.729], s.Select(v => Math.Round(v, 9)));
    }

    [Fact]
    public void Couple_counts_while_either_is_alive()
    {
        var s = Simple().Survival(LifeTable.Couple, 60, 2026, 60, 61);
        Assert.Equal(1 - 0.1 * 0.2, s[1], 9);
    }

    [Fact]
    public void Rates_rise_beyond_the_last_age_up_to_a_ceiling()
    {
        var m = Simple();
        Assert.Equal(0.1 * 1.05, m.Qx(true, 2026, 101), 9);
        Assert.Equal(0.5, m.Qx(true, 2026, 150), 9);
    }

    [Fact]
    public void Lifetime_ruin_weights_each_run_out_by_the_chance_of_being_alive()
    {
        // 20% withdrawals with no returns run out at 65 (after 5 years) on every path
        var input = new SimulationInput
        {
            StartingBalance = 100_000, RetirementAge = 60, DeathAge = 70, FeeRate = 0, Allocation = new(1, 0, 0),
            Spending = { InitialRate = 0.2 }, LifeTable = LifeTable.Male,
        };
        var history = Enumerable.Range(0, 120).Select(i => new MarketMonth(1900 + i / 12, i % 12 + 1, 0, 0, 0, 0)).ToList();
        var r = Simulator.Run(input, history, Simple(), 2026);
        Assert.Equal(0, r.SuccessRate);
        Assert.Equal(100 * Math.Pow(0.9, 5), r.LifetimeRuinRate!.Value, 6);
        Assert.Equal(100 * Math.Pow(0.9, 10), r.OutliveHorizonRate!.Value, 6);
    }

    [Fact]
    public void Ons_table_loads_and_gives_plausible_uk_lifespans()
    {
        var m = Mortality.Load(Path.Combine(AppContext.BaseDirectory, "SeedData", "mortality_uk.csv"));
        var man = m.Survival(LifeTable.Male, 57, 2026, 57, 90)[^1];
        var woman = m.Survival(LifeTable.Female, 57, 2026, 57, 90)[^1];
        Assert.InRange(man, 0.25, 0.45);   // about 1 in 3 men aged 57 reach 90
        Assert.True(woman > man);
    }

    [Fact]
    public void Fitter_can_target_the_chance_of_running_out_while_alive()
    {
        var input = new SimulationInput
        {
            StartingBalance = 100_000, RetirementAge = 60, DeathAge = 70, FeeRate = 0, Allocation = new(1, 0, 0),
            LifeTable = LifeTable.Male,
        };
        var history = Enumerable.Range(0, 120).Select(i => new MarketMonth(1900 + i / 12, i % 12 + 1, 0, 0, 0, 0)).ToList();
        var strict = SpendingFitter.Fit(input, history, _ => 1, 0, Simple(), 2026, maxLifetimeRuin: 0);
        var loose = SpendingFitter.Fit(input, history, _ => 1, 0, Simple(), 2026, maxLifetimeRuin: 50);
        Assert.Equal(10_000, strict.Plan.Spending.FixedAmount); // must last all 10 years
        // Accepting a 50% chance of running out while alive allows more: 0.9^n <= 0.5 once n >= 7 years
        Assert.True(loose.Plan.Spending.FixedAmount > strict.Plan.Spending.FixedAmount);
        Assert.True(loose.Result.LifetimeRuinRate <= 50);
    }
}
