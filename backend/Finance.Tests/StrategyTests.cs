using Finance.Engine;

namespace Finance.Tests;

public class SpendingStrategyTests
{
    static PathState State(double balance = 100_000, double previous = 4_000, double trailing = 0) => new()
    {
        Balance = balance,
        InitialBalance = 100_000,
        PeakBalance = Math.Max(balance, 100_000),
        InitialSpending = 4_000,
        PreviousSpending = previous,
        TrailingReturn = trailing,
        InflationRate = 0.025,
        RatchetBase = 100_000,
        Age = 65,
        YearsRemaining = 25,
    };

    [Fact]
    public void Constant_inflation_adjusted_keeps_real_spending_flat()
    {
        var p = new SpendingParameters { Type = SpendingStrategyType.ConstantInflationAdjusted };
        Assert.Equal(4_000, SpendingStrategy.Next(p, State(balance: 50_000)));
    }

    [Fact]
    public void Constant_percentage_follows_balance()
    {
        var p = new SpendingParameters { Type = SpendingStrategyType.ConstantPercentage, InitialRate = 0.05 };
        Assert.Equal(6_000, SpendingStrategy.Next(p, State(balance: 120_000)));
    }

    [Theory]
    [InlineData(0.15, 4_400)]  // good year → +10%
    [InlineData(-0.12, 3_600)] // bad year → -10%
    [InlineData(0.05, 4_000)]  // in between → unchanged
    public void Simple_guardrails_adjust_by_step(double trailing, double expected)
    {
        var p = new SpendingParameters { Type = SpendingStrategyType.SimpleGuardrails };
        Assert.Equal(expected, SpendingStrategy.Next(p, State(trailing: trailing)), 6);
    }

    [Fact]
    public void Simple_guardrails_can_cut_without_ever_raising()
    {
        var p = new SpendingParameters { Type = SpendingStrategyType.SimpleGuardrails, RaiseStep = 0, CutStep = 0.05 };
        Assert.Equal(4_000, SpendingStrategy.Next(p, State(trailing: 0.3)), 6);
        Assert.Equal(3_800, SpendingStrategy.Next(p, State(trailing: -0.3)), 6);
    }

    [Fact]
    public void Simple_guardrails_respect_cumulative_limits()
    {
        var p = new SpendingParameters { Type = SpendingStrategyType.SimpleGuardrails, MaxCut = 0.1 };
        Assert.Equal(3_600, SpendingStrategy.Next(p, State(previous: 3_700, trailing: -0.3)), 6);
    }

    [Fact]
    public void Guyton_klinger_cuts_when_withdrawal_rate_breaches_upper_guardrail()
    {
        var p = new SpendingParameters { Type = SpendingStrategyType.GuytonKlinger, FreezeAfterLoss = false };
        // 4,000 / 70,000 = 5.7% > 4% × 1.2 → cut 10%
        Assert.Equal(3_600, SpendingStrategy.Next(p, State(balance: 70_000)), 6);
        // 4,000 / 150,000 = 2.7% < 4% × 0.8 → raise 10%
        Assert.Equal(4_400, SpendingStrategy.Next(p, State(balance: 150_000)), 6);
    }

    [Fact]
    public void Guyton_klinger_raise_and_cut_are_separate()
    {
        var p = new SpendingParameters { Type = SpendingStrategyType.GuytonKlinger, FreezeAfterLoss = false, GuardrailRaise = 0, GuardrailCut = 0.05 };
        Assert.Equal(3_800, SpendingStrategy.Next(p, State(balance: 70_000)), 6);
        Assert.Equal(4_000, SpendingStrategy.Next(p, State(balance: 150_000)), 6);
    }

    [Fact]
    public void Guyton_klinger_freezes_inflation_after_loss()
    {
        var p = new SpendingParameters { Type = SpendingStrategyType.GuytonKlinger };
        // 4,000 / 90,000 = 4.4% > 4% (freeze) but < 4.8% (no guardrail)
        Assert.Equal(4_000 / 1.025, SpendingStrategy.Next(p, State(balance: 90_000, trailing: -0.05)), 6);
    }

    [Fact]
    public void Guyton_klinger_uses_withdrawal_net_of_other_income()
    {
        var p = new SpendingParameters { Type = SpendingStrategyType.GuytonKlinger, FreezeAfterLoss = false };
        var s = State(balance: 100_000, previous: 10_000);
        s.InitialWithdrawalRate = 0.04;
        s.Income = 6_000; // pot pays 4,000 → 4%, inside the guardrails
        Assert.Equal(10_000, SpendingStrategy.Next(p, s), 6);
        s.Income = 8_000; // pot pays 2,000 → 2% < 3.2% → raise
        Assert.Equal(11_000, SpendingStrategy.Next(p, s), 6);
    }

    [Fact]
    public void Percentage_of_pot_strategies_add_other_income_on_top()
    {
        var p = new SpendingParameters { Type = SpendingStrategyType.ConstantPercentage, InitialRate = 0.05 };
        var s = State(balance: 100_000);
        s.Income = 12_000;
        Assert.Equal(17_000, SpendingStrategy.Next(p, s), 6);
    }

    [Fact]
    public void Adjustments_can_be_combined()
    {
        // Good/bad year cuts 10% after a bad year, then Guyton-Klinger cuts another 10% because the rate is too high
        var p = new SpendingParameters
        {
            UseGoodBadYear = true,
            UseGuytonKlinger = true,
            FreezeAfterLoss = false,
            BadThreshold = 0,
            MaxCut = 0.5,
        };
        var s = State(balance: 60_000, trailing: -0.05);
        s.InitialWithdrawalRate = 0.04;
        Assert.Equal(4_000 * 0.9 * 0.9, SpendingStrategy.Next(p, s), 6);
    }

    [Fact]
    public void Floor_applies_after_other_adjustments()
    {
        var p = new SpendingParameters { UseGoodBadYear = true, BadThreshold = 0, CutStep = 0.5, MaxCut = 1, UseFloorCeiling = true, Floor = -0.2 };
        Assert.Equal(3_200, SpendingStrategy.Next(p, State(trailing: -0.1)), 6);
    }

    [Fact]
    public void Legacy_single_strategy_is_converted_to_base_plus_adjustment()
    {
        var p = new SpendingParameters { Type = SpendingStrategyType.GuytonKlinger }.Normalise();
        Assert.Equal(SpendingStrategyType.ConstantInflationAdjusted, p.Type);
        Assert.True(p.UseGuytonKlinger);
        var f = new SpendingParameters { Type = SpendingStrategyType.FloorAndCeiling }.Normalise();
        Assert.Equal(SpendingStrategyType.ConstantPercentage, f.Type);
        Assert.True(f.UseFloorCeiling);
        var i = new InvestmentParameters { Type = InvestmentStrategyType.CashBuffer }.Normalise();
        Assert.Equal(InvestmentStrategyType.FixedRebalance, i.Type);
        Assert.True(i.UseCashBuffer);
    }

    [Fact]
    public void Floor_and_ceiling_clamp_percentage_spending()
    {
        var p = new SpendingParameters { Type = SpendingStrategyType.FloorAndCeiling, Floor = -0.15, Ceiling = 0.25 };
        Assert.Equal(3_400, SpendingStrategy.Next(p, State(balance: 50_000)), 6);
        Assert.Equal(5_000, SpendingStrategy.Next(p, State(balance: 200_000)), 6);
        Assert.Equal(4_400, SpendingStrategy.Next(p, State(balance: 110_000)), 6);
    }

    [Fact]
    public void Inflation_skip_only_after_loss()
    {
        var p = new SpendingParameters { Type = SpendingStrategyType.InflationSkipAfterLoss };
        Assert.Equal(4_000 / 1.025, SpendingStrategy.Next(p, State(trailing: -0.01)), 6);
        Assert.Equal(4_000, SpendingStrategy.Next(p, State(trailing: 0.01)), 6);
    }

    [Fact]
    public void Ratchet_steps_up_and_resets_base()
    {
        var p = new SpendingParameters { Type = SpendingStrategyType.Ratchet };
        var s = State(balance: 160_000);
        Assert.Equal(4_400, SpendingStrategy.Next(p, s), 6);
        Assert.Equal(160_000, s.RatchetBase);
        s.PreviousSpending = 4_400;
        s.Balance = 170_000;
        Assert.Equal(4_400, SpendingStrategy.Next(p, s), 6); // not 50% above the new base
    }

    [Fact]
    public void Remaining_life_zero_return_splits_evenly()
    {
        var p = new SpendingParameters { Type = SpendingStrategyType.RemainingLife, AssumedRealReturn = 0 };
        Assert.Equal(4_000, SpendingStrategy.Next(p, State(balance: 100_000)), 6); // 25 years left
    }

    [Fact]
    public void Custom_rules_apply_first_match()
    {
        var p = new SpendingParameters
        {
            Type = SpendingStrategyType.CustomRules,
            Rules =
            [
                new SpendingRule(RuleMetric.DrawdownFromPeak, RuleComparison.GreaterThan, 0.2, RuleAction.AdjustPercent, -0.2),
                new SpendingRule(RuleMetric.TrailingReturn, RuleComparison.LessThan, 0, RuleAction.AdjustPercent, -0.1),
            ],
        };
        // 30% below peak and negative return: only the first rule applies
        Assert.Equal(3_200, SpendingStrategy.Next(p, State(balance: 70_000, trailing: -0.1)), 6);
        p.ApplyAllMatches = true;
        Assert.Equal(2_880, SpendingStrategy.Next(p, State(balance: 70_000, trailing: -0.1)), 6);
    }

    [Fact]
    public void Custom_rules_clamp_to_multiple_of_initial()
    {
        var p = new SpendingParameters
        {
            Type = SpendingStrategyType.CustomRules,
            ApplyAllMatches = true,
            Rules =
            [
                new SpendingRule(RuleMetric.TrailingReturn, RuleComparison.LessThan, 0, RuleAction.AdjustPercent, -0.5),
                new SpendingRule(RuleMetric.Age, RuleComparison.GreaterThan, 0, RuleAction.ClampMin, 0.8),
            ],
        };
        Assert.Equal(3_200, SpendingStrategy.Next(p, State(trailing: -0.1)), 6);
    }
}

public class InvestmentStrategyTests
{
    [Fact]
    public void Glide_path_moves_linearly()
    {
        var s = new InvestmentStrategy(new InvestmentParameters
        {
            Type = InvestmentStrategyType.DecliningGlidePath, StartEquity = 0.8, EndEquity = 0.4, GlideYears = 10,
        }, new Allocation(0.6, 0.4, 0));
        Assert.Equal(0.8, s.TargetWeights(0, 1, 0)[0], 6);
        Assert.Equal(0.6, s.TargetWeights(60, 1, 0)[0], 6);
        Assert.Equal(0.4, s.TargetWeights(240, 1, 0)[0], 6);
        Assert.Equal(0.6, s.TargetWeights(240, 1, 0)[1], 6);
    }

    [Fact]
    public void Cash_buffer_holds_years_of_spending_and_uses_it_in_down_markets()
    {
        var s = new InvestmentStrategy(new InvestmentParameters { Type = InvestmentStrategyType.CashBuffer, BufferYears = 2 },
            new Allocation(0.6, 0.4, 0));
        var targets = s.TargetWeights(0, 100_000, 4_000);
        Assert.Equal(0.08, targets[2], 6);
        Assert.Equal(0.92 * 0.6, targets[0], 6);

        double[] holdings = [55_200, 36_800, 8_000];
        s.Withdraw(holdings, 1_000, targets, downMarket: true);
        Assert.Equal(7_000, holdings[2], 6);
        Assert.Equal(55_200, holdings[0], 6);

        s.Withdraw(holdings, 1_000, targets, downMarket: false);
        Assert.Equal(7_000, holdings[2], 6);
        Assert.Equal(54_600, holdings[0], 6);
    }

    [Fact]
    public void Cash_buffer_combines_with_glide_path()
    {
        var s = new InvestmentStrategy(new InvestmentParameters
        {
            Type = InvestmentStrategyType.DecliningGlidePath, StartEquity = 0.8, EndEquity = 0.4, GlideYears = 10,
            UseCashBuffer = true, BufferYears = 2,
        }, new Allocation(0.6, 0.4, 0));
        var t = s.TargetWeights(0, 100_000, 5_000); // 10% cash, rest 80/20
        Assert.Equal(0.1, t[2], 6);
        Assert.Equal(0.72, t[0], 6);
        Assert.Equal(0.18, t[1], 6);
    }

    [Fact]
    public void Cash_buffer_is_refilled_at_year_end_even_without_rebalancing()
    {
        var s = new InvestmentStrategy(new InvestmentParameters { Type = InvestmentStrategyType.BuyAndHold, UseCashBuffer = true },
            new Allocation(0.6, 0.4, 0));
        double[] holdings = [60, 40, 0];
        double[] targets = [0.54, 0.36, 0.1];
        s.RefillCash(5, holdings, targets, downMarket: false);
        Assert.Equal(0, holdings[2]);                // only at year end
        s.RefillCash(11, holdings, targets, downMarket: true);
        Assert.Equal(0, holdings[2]);                // not in a falling market
        s.RefillCash(11, holdings, targets, downMarket: false);
        Assert.Equal(10, holdings[2], 6);
        Assert.Equal(54, holdings[0], 6);
        Assert.False(s.ShouldRebalance(11, holdings, targets, false));
    }

    [Fact]
    public void Withdraw_from_winner_sells_overweight_asset()
    {
        var s = new InvestmentStrategy(new InvestmentParameters { Type = InvestmentStrategyType.WithdrawFromWinner },
            new Allocation(0.5, 0.5, 0));
        double[] holdings = [70, 30, 0];
        s.Withdraw(holdings, 10, [0.5, 0.5, 0], false);
        Assert.Equal([60.0, 30.0, 0.0], holdings);
    }

    [Fact]
    public void Threshold_rebalance_only_when_drift_exceeds_band()
    {
        var s = new InvestmentStrategy(new InvestmentParameters { Type = InvestmentStrategyType.ThresholdRebalance, Band = 0.05 },
            new Allocation(0.6, 0.4, 0));
        Assert.False(s.ShouldRebalance(3, [63, 37, 0], [0.6, 0.4, 0], false));
        Assert.True(s.ShouldRebalance(3, [66, 34, 0], [0.6, 0.4, 0], false));
    }

    [Theory]
    [InlineData(RebalanceFrequency.Monthly, 4, true)]
    [InlineData(RebalanceFrequency.Quarterly, 4, false)]
    [InlineData(RebalanceFrequency.Quarterly, 5, true)]
    [InlineData(RebalanceFrequency.Annually, 5, false)]
    [InlineData(RebalanceFrequency.Annually, 11, true)]
    public void Fixed_rebalance_follows_frequency(RebalanceFrequency f, int month, bool expected)
    {
        var s = new InvestmentStrategy(new InvestmentParameters { Frequency = f }, new Allocation(0.6, 0.4, 0));
        Assert.Equal(expected, s.ShouldRebalance(month, [70, 30, 0], [0.6, 0.4, 0], false));
    }
}
