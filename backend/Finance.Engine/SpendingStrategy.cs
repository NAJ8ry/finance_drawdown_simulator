namespace Finance.Engine;

/// <summary>What a spending strategy can see at each yearly decision point. Money is in today's money (real).</summary>
public sealed class PathState
{
    public double Balance { get; set; }
    public double InitialBalance { get; init; }
    public double PeakBalance { get; set; }
    public double InitialSpending { get; set; }
    public double PreviousSpending { get; set; }

    /// <summary>Portfolio return over the last 12 months, expressed in nominal terms using the planned inflation.</summary>
    public double TrailingReturn { get; set; }

    public double Age { get; set; }
    public double YearsRemaining { get; set; }
    public double InflationRate { get; init; }

    /// <summary>Balance at the last ratchet step-up (ratchet strategy only).</summary>
    public double RatchetBase { get; set; }

    /// <summary>Other income (pensions etc.) and regular outgoings for the coming year.</summary>
    public double Income { get; set; }
    public double Expenses { get; set; }

    /// <summary>Net withdrawal from the pot as a fraction of the balance in year one.</summary>
    public double InitialWithdrawalRate { get; set; }

    public double DrawdownFromPeak => PeakBalance > 0 ? 1 - Balance / PeakBalance : 0;

    /// <summary>What must come out of the pot this year for a given level of spending.</summary>
    public double NetWithdrawal(double spending) => spending + Expenses - Income;

    /// <summary>Current net withdrawal from the pot as a fraction of the balance.</summary>
    public double WithdrawalRate => Balance > 0 ? NetWithdrawal(PreviousSpending) / Balance : double.PositiveInfinity;
    public double BalanceVsInitial => InitialBalance > 0 ? Balance / InitialBalance - 1 : 0;
}

/// <summary>
/// Computes annual spending (today's money) at each retirement anniversary.
/// The simulation runs in real terms, so "keeping pace with inflation" means holding spending constant,
/// and "skipping an inflation rise" means dividing by (1 + planned inflation).
///
/// Spending is what you live on. Other income pays for part of it, so the pot provides
/// spending + regular outgoings - other income. The percentage and remaining-life bases set the pot's
/// contribution, and other income comes on top.
/// </summary>
public static class SpendingStrategy
{
    public static double Initial(SpendingParameters p, PathState s)
    {
        p.Normalise();
        return p.Type switch
        {
            SpendingStrategyType.RemainingLife => RemainingLife(s.Balance, s.YearsRemaining, p.AssumedRealReturn) + s.Income,
            SpendingStrategyType.ConstantPercentage => p.InitialRate * s.Balance + s.Income,
            _ => p.InitialRate * s.Balance,
        };
    }

    /// <summary>Next year's spending: the base strategy, then each switched-on adjustment in turn.</summary>
    public static double Next(SpendingParameters p, PathState s)
    {
        p.Normalise();
        var spend = p.Type switch
        {
            SpendingStrategyType.ConstantPercentage => p.InitialRate * s.Balance + s.Income,
            SpendingStrategyType.RemainingLife => RemainingLife(s.Balance, s.YearsRemaining, p.AssumedRealReturn) + s.Income,
            _ => s.PreviousSpending,
        };

        if (p.UseInflationSkip && s.TrailingReturn < 0) spend /= 1 + s.InflationRate;
        if (p.UseGoodBadYear) spend = GoodBadYear(p, s, spend);
        if (p.UseGuytonKlinger) spend = GuytonKlinger(p, s, spend);
        if (p.UseRatchet) spend = Ratchet(p, s, spend);
        if (p.UseCustomRules) spend = CustomRules(p, s, spend);
        if (p.UseFloorCeiling)
        {
            var s0 = s.InitialSpending;
            spend = Math.Clamp(spend, s0 * (1 + p.Floor), s0 * (1 + Math.Max(p.Floor, p.Ceiling)));
        }
        return Math.Max(0, spend);
    }

    static double GoodBadYear(SpendingParameters p, PathState s, double spend)
    {
        if (s.TrailingReturn >= p.GoodThreshold) spend *= 1 + p.RaiseStep;
        else if (s.TrailingReturn <= p.BadThreshold) spend *= 1 - p.CutStep;
        var min = s.InitialSpending * (1 - p.MaxCut);
        var max = s.InitialSpending * (1 + p.MaxRaise);
        return Math.Clamp(spend, min, Math.Max(min, max));
    }

    static double GuytonKlinger(SpendingParameters p, PathState s, double spend)
    {
        // Guardrails compare what is drawn from the pot (net of other income) with the year-one rate
        var initial = s.InitialWithdrawalRate > 0 ? s.InitialWithdrawalRate : p.InitialRate;
        double Rate(double sp) => s.Balance > 0 ? s.NetWithdrawal(sp) / s.Balance : double.PositiveInfinity;
        // Freeze rule: no inflation rise after a losing year when the withdrawal rate is above the initial rate
        if (p.FreezeAfterLoss && s.TrailingReturn < 0 && Rate(spend) > initial)
            spend /= 1 + s.InflationRate;
        var rate = Rate(spend);
        if (rate > initial * (1 + p.UpperGuardrail)) spend *= 1 - p.GuardrailCut;         // capital preservation
        else if (rate < initial * (1 - p.LowerGuardrail)) spend *= 1 + p.GuardrailRaise;  // prosperity
        return spend;
    }

    static double Ratchet(SpendingParameters p, PathState s, double spend)
    {
        if (s.Balance >= s.RatchetBase * (1 + p.RatchetTrigger))
        {
            spend *= 1 + p.RatchetIncrease;
            s.RatchetBase = s.Balance;
        }
        return spend;
    }

    /// <summary>Level annual payment that would exhaust the balance over the remaining years at the assumed real return.</summary>
    public static double RemainingLife(double balance, double years, double r)
    {
        if (years <= 1) return balance;
        if (Math.Abs(r) < 1e-9) return balance / years;
        // Annuity-due: first payment taken immediately
        return balance * r / ((1 - Math.Pow(1 + r, -years)) * (1 + r));
    }

    static double CustomRules(SpendingParameters p, PathState s, double spend)
    {
        foreach (var rule in p.Rules)
        {
            var metric = rule.Metric switch
            {
                RuleMetric.TrailingReturn => s.TrailingReturn,
                RuleMetric.DrawdownFromPeak => s.DrawdownFromPeak,
                RuleMetric.WithdrawalRate => s.WithdrawalRate,
                RuleMetric.BalanceVsInitial => s.BalanceVsInitial,
                RuleMetric.Age => s.Age,
                _ => 0,
            };
            var matches = rule.Comparison == RuleComparison.GreaterThan ? metric > rule.Threshold : metric < rule.Threshold;
            if (!matches) continue;

            spend = rule.Action switch
            {
                RuleAction.AdjustPercent => spend * (1 + rule.Value),
                RuleAction.SetPercentOfBalance => rule.Value * s.Balance + s.Income,
                RuleAction.FreezeInflation => spend / (1 + s.InflationRate),
                RuleAction.ClampMin => Math.Max(spend, s.InitialSpending * rule.Value),
                RuleAction.ClampMax => Math.Min(spend, s.InitialSpending * rule.Value),
                _ => spend,
            };
            if (!p.ApplyAllMatches) break;
        }
        return spend;
    }
}
