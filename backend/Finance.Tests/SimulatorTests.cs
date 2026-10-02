using Finance.Api.Services;
using Finance.Engine;

namespace Finance.Tests;

public class SimulatorTests
{
    /// <summary>Flat history: every month has the same nominal returns and inflation.</summary>
    static List<MarketMonth> Flat(int months, double equity = 0, double bond = 0, double cash = 0, double inflation = 0) =>
        Enumerable.Range(0, months)
            .Select(i => new MarketMonth(1900 + i / 12, i % 12 + 1, equity, bond, cash, inflation))
            .ToList();

    static SimulationInput Input(Action<SimulationInput>? configure = null)
    {
        var input = new SimulationInput
        {
            StartingBalance = 100_000,
            RetirementAge = 60,
            DeathAge = 70,
            FeeRate = 0,
            InflationRate = 0.02,
            Allocation = new Allocation(1, 0, 0),
        };
        configure?.Invoke(input);
        return input;
    }

    [Fact]
    public void Annuity_payment_exhausts_balance_exactly_at_death()
    {
        // T-02: constant real return, withdraw the level annuity-due payment → balance ends at ~£0
        const double annualReal = 0.03;
        var g = Math.Pow(1 + annualReal, 1.0 / 12) - 1;
        const int n = 120;
        var monthly = 100_000 * g / ((1 - Math.Pow(1 + g, -n)) * (1 + g));

        var input = Input(i => i.Spending.InitialRate = monthly * 12 / 100_000 * 0.999999);
        var path = Simulator.RunPath(input, RealReturns.From(Flat(n, equity: g)), 0, "x");

        Assert.False(path.Failed);
        Assert.InRange(path.EndBalance, 0, 1);
    }

    [Fact]
    public void Overspending_fails_and_records_age()
    {
        var input = Input(i => i.Spending.InitialRate = 0.2); // 5 years of money with zero returns
        var path = Simulator.RunPath(input, RealReturns.From(Flat(120)), 0, "x");

        Assert.True(path.Failed);
        Assert.Equal(65, path.FailAge);
        Assert.Equal(0, path.Balances[^1]);
        Assert.False(path.Succeeded);
    }

    [Fact]
    public void Returns_are_deflated_by_historical_inflation()
    {
        // 1% nominal return with 1% inflation = 0% real → no growth in today's money
        var input = Input(i => i.Spending.InitialRate = 0);
        var path = Simulator.RunPath(input, RealReturns.From(Flat(120, equity: 0.01, inflation: 0.01)), 0, "x");
        Assert.Equal(100_000, path.EndBalance, 6);
    }

    [Fact]
    public void Fees_reduce_balance_by_annual_rate()
    {
        var input = Input(i => { i.Spending.InitialRate = 0; i.FeeRate = 0.01; i.DeathAge = 61; });
        var path = Simulator.RunPath(input, RealReturns.From(Flat(12)), 0, "x");
        Assert.Equal(99_000, path.EndBalance, 6);
    }

    [Fact]
    public void Recent_start_months_are_partial_and_excluded_from_success_rate()
    {
        var input = Input(i => { i.Spending.InitialRate = 0.2; i.StartFrequency = StartFrequency.Monthly; }); // fails after 5 years
        var result = Simulator.Run(input, Flat(150));             // 12.5 years of data, 10-year retirement

        Assert.Equal(31, result.CompleteCount);                   // starts 0..30 have 120 months
        Assert.Equal(result.Paths.Count - 31, result.PartialCount);
        Assert.Equal(0, result.SuccessRate);
        // Partial paths with at least 5 years of data have already failed
        Assert.True(result.PartialFailedCount > 0);
        Assert.All(result.Paths.Where(p => p.Partial), p => Assert.False(p.Succeeded));
    }

    [Fact]
    public void Yearly_starts_use_the_first_month_of_each_year()
    {
        // History starting in March: first start is March 1900, then January of each later year
        var history = Enumerable.Range(2, 400).Select(i => new MarketMonth(1900 + i / 12, i % 12 + 1, 0, 0, 0, 0)).ToList();
        var result = Simulator.Run(Input(), history);
        Assert.Equal("1900-03", result.Paths[0].Start);
        Assert.All(result.Paths.Skip(1), p => Assert.EndsWith("-01", p.Start));
        Assert.Equal(result.Paths.Count, result.Paths.Select(p => p.Start[..4]).Distinct().Count());
    }

    [Fact]
    public void Legacy_target_must_be_met_to_succeed()
    {
        var input = Input(i => { i.Spending.InitialRate = 0.05; i.LegacyTarget = 60_000; });
        var result = Simulator.Run(input, Flat(120)); // ends with £50k
        Assert.Equal(0, result.SuccessRate);
        input.LegacyTarget = 40_000;
        Assert.Equal(100, Simulator.Run(input, Flat(120)).SuccessRate);
    }

    [Fact]
    public void One_offs_are_withdrawn_or_deposited_at_the_given_age()
    {
        var input = Input(i =>
        {
            i.Spending.InitialRate = 0;
            i.OneOffs = [new OneOff(62, 10_000, "Car"), new OneOff(65, -5_000, "Inheritance")];
        });
        var path = Simulator.RunPath(input, RealReturns.From(Flat(120)), 0, "x");
        Assert.Equal(100_000, path.Balances[2]);
        Assert.Equal(90_000, path.Balances[3]);
        Assert.Equal(95_000, path.Balances[6]);
    }

    [Fact]
    public void Annual_in_advance_withdraws_whole_year_at_start()
    {
        var input = Input(i => { i.WithdrawalTiming = WithdrawalTiming.AnnualInAdvance; i.DeathAge = 62; });
        var path = Simulator.RunPath(input, RealReturns.From(Flat(24)), 0, "x");
        Assert.Equal(96_000, path.Balances[1], 6);
        Assert.Equal(92_000, path.Balances[2], 6);
    }

    [Fact]
    public void Spending_floor_is_reported_but_not_a_failure()
    {
        var input = Input(i =>
        {
            i.Spending.Type = SpendingStrategyType.ConstantPercentage;
            i.SpendingFloor = 3_900;
        });
        var result = Simulator.Run(input, Flat(120, equity: -0.002));
        Assert.Equal(100, result.SuccessRate);
        Assert.Equal(100, result.BelowFloorRate);
    }

    [Fact]
    public void Higher_withdrawal_never_improves_success_on_real_history()
    {
        // T-05 on the real seed data
        var history = MarketDataStore.ReadCsv(SeedPath());
        double? last = 100;
        foreach (var rate in new[] { 0.03, 0.035, 0.04, 0.05, 0.06 })
        {
            var input = new SimulationInput { Spending = { InitialRate = rate } };
            var rateResult = Simulator.Run(input, history).SuccessRate;
            Assert.True(rateResult <= last, $"{rate:P1} gave {rateResult} > {last}");
            last = rateResult;
        }
    }

    [Fact]
    public void Baseline_on_seed_history_is_in_a_plausible_range()
    {
        // T-03: 60/40, 4%, 34 years, 0.5% fees — UK-based research puts this around 75–90%
        var history = MarketDataStore.ReadCsv(SeedPath());
        var result = Simulator.Run(new SimulationInput(), history);
        Assert.InRange(result.SuccessRate!.Value, 70, 95);
        Assert.StartsWith("196", result.Paths[result.WorstIndex!.Value].Start);
    }

    [Fact]
    public void Validation_rejects_bad_allocation_and_ages()
    {
        var input = new SimulationInput { DeathAge = 50, Allocation = new Allocation(0.5, 0.4, 0) };
        var errors = Simulator.Validate(input);
        Assert.Contains(errors, e => e.Contains("Age of death"));
        Assert.Contains(errors, e => e.Contains("100%"));
    }

    [Fact]
    public void Inflation_linked_pension_reduces_withdrawals_from_its_start_age()
    {
        // £10k spending, zero returns; £6k pension from 65 → pot pays £10k × 5 + £4k × 5 = £70k
        var input = Input(i =>
        {
            i.Spending.InitialRate = 0.1;
            i.Flows = [new RecurringFlow("State Pension", FlowKind.Income, 65, null, 6_000)];
        });
        var path = Simulator.RunPath(input, RealReturns.From(Flat(120)), 0, "x");

        Assert.False(path.Failed);
        Assert.Equal(30_000, path.EndBalance, 6);
        Assert.Equal(10_000, path.Withdrawals[4], 6);
        Assert.Equal(4_000, path.Withdrawals[5], 6);
        Assert.All(path.Spending, s => Assert.Equal(10_000, s, 6));
    }

    [Fact]
    public void Pension_can_rescue_a_plan_that_would_otherwise_fail()
    {
        var input = Input(i => i.Spending.InitialRate = 0.12); // runs out in year 9 without income
        var history = Flat(120);
        Assert.Equal(0, Simulator.Run(input, history).SuccessRate);
        input.Flows = [new RecurringFlow("Pension", FlowKind.Income, 63, null, 5_000)];
        Assert.Equal(100, Simulator.Run(input, history).SuccessRate);
    }

    [Fact]
    public void Income_above_spending_is_paid_into_the_pot_and_spent_at_the_initial_rate()
    {
        var input = Input(i =>
        {
            i.Spending.InitialRate = 0.04;
            i.Flows = [new RecurringFlow("Pension", FlowKind.Income, 60, null, 10_000)];
        });
        var path = Simulator.RunPath(input, RealReturns.From(Flat(120)), 0, "x");
        // Each year's surplus is new capital, so spending rises by 4% of it: 4,000 + 4% of 6,000, and so on
        Assert.Equal(4_240, path.Spending[0], 6);
        Assert.Equal(4_240 + 0.04 * 5_760, path.Spending[1], 6);
        Assert.Equal(-5_760, path.Withdrawals[0], 6);
        Assert.Equal(100_000 + path.Spending.Sum(sp => 10_000 - sp), path.EndBalance, 6);
    }

    [Fact]
    public void Deposit_raises_constant_spending_as_if_it_had_been_in_the_pot()
    {
        var input = Input(i =>
        {
            i.Spending.InitialRate = 0.04;
            i.OneOffs = [new OneOff(61, -500_000, "Inheritance")];
        });
        var path = Simulator.RunPath(input, RealReturns.From(Flat(120)), 0, "x");
        Assert.Equal(4_000, path.Spending[0], 6);
        Assert.Equal(24_000, path.Spending[1], 6);
        Assert.Equal(24_000, path.Spending[9], 6);
    }

    [Fact]
    public void Rate_change_restarts_spending_on_the_pot_including_new_money()
    {
        var input = Input(i =>
        {
            i.Spending.InitialRate = 0.07;
            i.Spending.RateChanges = [new RateChange(62, 0.03)];
            i.OneOffs = [new OneOff(62, -500_000, "Inheritance")];
        });
        var path = Simulator.RunPath(input, RealReturns.From(Flat(120)), 0, "x");
        Assert.Equal(7_000, path.Spending[1], 6);
        // 100,000 - 2 x 7,000 left, plus the inheritance, at 3%
        Assert.Equal(0.03 * 586_000, path.Spending[2], 6);
        Assert.Equal(0.03 * 586_000, path.Spending[9], 6);
    }

    [Fact]
    public void Rate_change_counts_income_arriving_that_year()
    {
        var input = Input(i =>
        {
            i.Spending.InitialRate = 0.07;
            i.Spending.UseGuytonKlinger = true;
            i.Spending.RateChanges = [new RateChange(62, 0.03)];
            i.Flows = [new RecurringFlow("Inheritance", FlowKind.Income, 62, 63, 500_000)];
        });
        var path = Simulator.RunPath(input, RealReturns.From(Flat(120)), 0, "x");
        Assert.Equal(0.03 * 586_000, path.Spending[2], 6);
        // Flat markets: the guardrails leave the new level alone
        Assert.Equal(0.03 * 586_000, path.Spending[5], 6);
    }

    [Fact]
    public void Cash_buffer_stays_a_sum_of_money_when_a_deposit_arrives()
    {
        var input = Input(i =>
        {
            i.Spending.InitialRate = 0.04;
            i.Investment.UseCashBuffer = true;
            i.OneOffs = [new OneOff(61, -900_000, "Inheritance")];
        });
        // Shares grow 1% a month, cash earns nothing
        var path = Simulator.RunPath(input, RealReturns.From(Flat(120, equity: 0.01)), 0, "x");
        // Two years of the new £40k withdrawal is £80k of cash; the other ~£900k is in shares and grows ~12.7%.
        // Sizing the buffer from the pre-deposit pot would have held over 80% of the pot in cash.
        var growth = path.Balances[2] - path.Balances[1] - 900_000 + 40_000;
        Assert.InRange(growth, 100_000, 125_000);
    }

    [Theory]
    [InlineData(SpendingStrategyType.ConstantPercentage)]
    [InlineData(SpendingStrategyType.ConstantInflationAdjusted)]
    [InlineData(SpendingStrategyType.RemainingLife)]
    public void Income_paid_into_the_pot_is_invested_not_spent(SpendingStrategyType type)
    {
        var input = Input(i =>
        {
            i.Spending.Type = type;
            i.Spending.InitialRate = 0.04;
            i.Flows = [new RecurringFlow("Inheritance", FlowKind.Income, 62, 63, 500_000, IntoPot: true)];
        });
        var path = Simulator.RunPath(input, RealReturns.From(Flat(120)), 0, "x");
        Assert.True(path.Spending[2] < 50_000, $"spent {path.Spending[2]:N0} in the year it arrived");
        Assert.True(path.Balances[3] > 500_000);
    }

    [Fact]
    public void Fixed_amounts_take_each_amount_until_the_next_with_income_on_top()
    {
        var input = Input(i =>
        {
            i.Spending.Type = SpendingStrategyType.FixedAmounts;
            i.Spending.FixedAmount = 10_000;
            i.Spending.AmountSteps = [new SpendingStep(63, 6_000), new SpendingStep(66, 4_000)];
            i.Flows = [new RecurringFlow("Pension", FlowKind.Income, 65, null, 3_000)];
        });
        var path = Simulator.RunPath(input, RealReturns.From(Flat(120)), 0, "x");
        // Ages 60-62, 63-65, 66-69; the pension from 65 doesn't reduce what the pot pays
        double[] expected = [10_000, 10_000, 10_000, 6_000, 6_000, 6_000, 4_000, 4_000, 4_000, 4_000];
        Assert.Equal(expected, path.Withdrawals.Select(w => Math.Round(w, 6)));
        Assert.Equal(9_000, path.Spending[5], 6); // ...it is spent on top
        Assert.Equal(100_000 - expected.Sum(), path.EndBalance, 6);
    }

    [Fact]
    public void Fixed_amounts_ignore_adjustments_that_have_nothing_to_act_on()
    {
        var input = Input(i =>
        {
            i.Spending.Type = SpendingStrategyType.FixedAmounts;
            i.Spending.FixedAmount = 5_000;
            i.Spending.UseInflationSkip = true;
            i.Spending.UseRatchet = true;
            i.Spending.RatchetTrigger = 0;
            i.Spending.UseFloorCeiling = true;
            i.Spending.Floor = 0.5;
        });
        // Falling market: inflation skip would cut, the zero-trigger ratchet would raise, the floor would lift to 7,500
        var path = Simulator.RunPath(input, RealReturns.From(Flat(120, equity: -0.01)), 0, "x");
        Assert.All(path.Spending, sp => Assert.Equal(5_000, sp, 6));
    }

    [Fact]
    public void Cuts_count_unplanned_falls_but_not_scheduled_steps()
    {
        var input = Input(i =>
        {
            i.Spending.Type = SpendingStrategyType.FixedAmounts;
            i.Spending.FixedAmount = 10_000;
            i.Spending.AmountSteps = [new SpendingStep(63, 5_000)]; // planned 50% step down
            i.Spending.UseGuytonKlinger = true;
            i.SpendingFloor = 6_000;
        });
        // Shares fall 3% a month from year 5, so the guardrails start cutting
        var months = Enumerable.Range(0, 120).Select(m => new MarketMonth(1900 + m / 12, m % 12 + 1, m >= 60 ? -0.03 : 0, 0, 0, 0)).ToList();
        var r = Simulator.Run(input, months);
        var p = r.Paths[0];
        bool IsCut(int y) => p.Spending[y] <= p.Spending[y - 1] * 0.9 + 1e-6;
        Assert.True(IsCut(3), "the step at 63 is a big fall...");
        var unplanned = Enumerable.Range(1, p.Spending.Length - 1).Where(y => y != 3).Count(IsCut);
        Assert.True(unplanned > 0, "guardrail cuts after the market fall should count");
        Assert.Equal(unplanned, p.Cuts); // ...but only the unplanned ones are counted
        Assert.Equal(p.Spending.Count(s => s < 6_000 - 1e-6), p.YearsBelowFloor);
        Assert.Equal(100, r.CutRate);
    }

    [Fact]
    public void Running_out_records_years_on_other_income()
    {
        var input = Input(i => i.Spending.InitialRate = 0.2); // runs out after 5 years of a 10-year retirement
        var r = Simulator.Run(input, Flat(120));
        Assert.Equal(5, r.Paths[0].YearsWithoutPot, 1);
        Assert.Equal(5, r.MaxYearsWithoutPot!.Value, 1);
    }

    [Fact]
    public void Share_returns_adjustment_lowers_every_year_by_that_much()
    {
        var input = Input(i => { i.Spending.InitialRate = 0; i.EquityReturnAdjustment = -0.02; i.DeathAge = 62; });
        var path = Simulator.RunPath(input, RealReturns.From(Flat(24), input.EquityReturnAdjustment), 0, "x");
        Assert.Equal(100_000 * 0.98 * 0.98, path.EndBalance, 6);
    }

    [Fact]
    public void Deposit_does_not_trigger_the_ratchet()
    {
        var input = Input(i =>
        {
            i.Spending.InitialRate = 0.04;
            i.Spending.UseRatchet = true;
            i.OneOffs = [new OneOff(61, -500_000, "Inheritance")];
        });
        var path = Simulator.RunPath(input, RealReturns.From(Flat(120)), 0, "x");
        Assert.All(path.Spending.Skip(1), sp => Assert.Equal(24_000, sp, 6));
    }

    [Fact]
    public void Fixed_income_loses_value_with_planned_inflation_from_its_start()
    {
        var input = Input(i => i.Flows = [new RecurringFlow("Annuity", FlowKind.Income, 62, null, 1_000, InflationLinked: false)]);
        var (income, _, _) = Simulator.FlowsByYear(input);
        Assert.Equal(0, income[1]);
        Assert.Equal(1_000, income[2], 6);
        Assert.Equal(1_000 / 1.02, income[3], 6);
        Assert.Equal(1_000 / Math.Pow(1.02, 7), income[9], 6);
    }

    [Fact]
    public void Regular_outgoing_adds_to_withdrawals_until_its_end_age()
    {
        var input = Input(i =>
        {
            i.Spending.InitialRate = 0;
            i.Flows = [new RecurringFlow("Mortgage", FlowKind.Expense, 60, 63, 2_000)];
        });
        var path = Simulator.RunPath(input, RealReturns.From(Flat(120)), 0, "x");
        Assert.Equal(94_000, path.EndBalance, 6);
        Assert.Equal(0, path.Withdrawals[3], 6);
    }

    [Fact]
    public void After_running_out_you_live_on_other_income()
    {
        var input = Input(i =>
        {
            i.Spending.InitialRate = 0.3;
            i.Flows = [new RecurringFlow("Pension", FlowKind.Income, 60, null, 3_000)];
        });
        var path = Simulator.RunPath(input, RealReturns.From(Flat(120)), 0, "x");
        Assert.True(path.Failed);
        Assert.Equal(3_000, path.Spending[^1], 6);
    }

    [Fact]
    public void Disabled_flow_is_left_out()
    {
        var input = Input(i =>
        {
            i.Spending.InitialRate = 0;
            i.Flows = [new RecurringFlow("Mortgage", FlowKind.Expense, 60, 63, 2_000, Disabled: true)];
        });
        var path = Simulator.RunPath(input, RealReturns.From(Flat(120)), 0, "x");
        Assert.Equal(100_000, path.EndBalance, 6);
        var off = new SimulationInput { Flows = [new RecurringFlow("Pension", FlowKind.Income, 70, 65, 1_000, Disabled: true)] };
        Assert.DoesNotContain(Simulator.Validate(off), e => e.Contains("end age"));
    }

    [Fact]
    public void Validation_rejects_bad_flows()
    {
        var input = new SimulationInput { Flows = [new RecurringFlow("Pension", FlowKind.Income, 70, 65, 1_000)] };
        Assert.Contains(Simulator.Validate(input), e => e.Contains("end age"));
    }

    static string SeedPath() => Path.Combine(AppContext.BaseDirectory, "SeedData", "history.csv");
}
