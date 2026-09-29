using Finance.Engine;

namespace Finance.Tests;

public class SpendingFitterTests
{
    static List<MarketMonth> Flat(int months) =>
        Enumerable.Range(0, months).Select(i => new MarketMonth(1900 + i / 12, i % 12 + 1, 0, 0, 0, 0)).ToList();

    static SimulationInput Input(Action<SimulationInput>? configure = null)
    {
        var input = new SimulationInput
        {
            StartingBalance = 100_000, RetirementAge = 60, DeathAge = 70, FeeRate = 0, Allocation = new Allocation(1, 0, 0),
        };
        configure?.Invoke(input);
        return input;
    }

    [Fact]
    public void Level_spending_uses_up_the_pot_exactly_with_no_growth()
    {
        var fit = SpendingFitter.Fit(Input(), Flat(120), _ => 1, 100);
        Assert.True(fit.Feasible);
        Assert.Equal(10_000, fit.Plan.Spending.FixedAmount); // the level itself is found to within the £100 rounding
        Assert.Equal(100, fit.Result.SuccessRate);
        Assert.Equal(SpendingStrategyType.FixedAmounts, fit.Plan.Spending.Type);
    }

    [Fact]
    public void Pension_pays_part_of_the_spending_so_the_pot_steps_down_when_it_starts()
    {
        var input = Input(i => i.Flows = [new RecurringFlow("Pension", FlowKind.Income, 65, null, 5_000)]);
        var fit = SpendingFitter.Fit(input, Flat(120), _ => 1, 100);
        // 5 years at L from the pot, then 5 years at L - 5,000: 10L - 25,000 = 100,000
        Assert.Equal(12_500, fit.Plan.Spending.FixedAmount);
        var step = Assert.Single(fit.Plan.Spending.AmountSteps);
        Assert.Equal(65, step.Age);
        Assert.Equal(7_500, step.Amount);
    }

    [Fact]
    public void Shape_lowers_later_spending_so_early_spending_can_be_higher()
    {
        double Easing(int age) => age < 65 ? 1 : 0.8;
        var level = SpendingFitter.Fit(Input(), Flat(120), _ => 1, 100);
        var eased = SpendingFitter.Fit(Input(), Flat(120), Easing, 100);
        Assert.True(eased.Plan.Spending.FixedAmount > level.Plan.Spending.FixedAmount);
    }

    [Fact]
    public void Infeasible_when_outgoings_alone_empty_the_pot()
    {
        var input = Input(i => i.Flows = [new RecurringFlow("Care", FlowKind.Expense, 60, null, 20_000)]);
        var fit = SpendingFitter.Fit(input, Flat(120), _ => 1, 100);
        Assert.False(fit.Feasible);
    }

    [Fact]
    public void Users_plan_is_not_modified()
    {
        var input = Input(i => i.Spending.UseGuytonKlinger = true);
        SpendingFitter.Fit(input, Flat(120), _ => 1, 100);
        Assert.Equal(SpendingStrategyType.ConstantInflationAdjusted, input.Spending.Type);
        Assert.True(input.Spending.UseGuytonKlinger);
    }

    [Theory]
    [InlineData(65, 0.0001)]
    [InlineData(75, -0.0129)]
    [InlineData(80, -0.0134)]
    [InlineData(95, 0.0091)]
    public void Blanchett_formula_matches_published_equation(int age, double expected) =>
        Assert.Equal(expected, SpendingPatterns.BlanchettChange(age), 4);

    [Fact]
    public void Uk_pattern_is_level_to_80_then_eases_one_percent_a_year()
    {
        Assert.Equal(1, SpendingPatterns.UkIfs.At(60, 80));
        Assert.Equal(0.99 * 0.99, SpendingPatterns.UkIfs.At(60, 82), 9);
    }
}
