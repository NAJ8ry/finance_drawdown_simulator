using System.Text.Json.Serialization;

namespace Finance.Engine;

/// <summary>One month of market history. All values are nominal GBP monthly returns (0.01 = +1%).</summary>
public readonly record struct MarketMonth(int Year, int Month, double Equity, double Bond, double Cash, double Inflation)
{
    public string Label => $"{Year:D4}-{Month:D2}";
}

public enum SpendingStrategyType
{
    ConstantInflationAdjusted,
    ConstantPercentage,
    SimpleGuardrails,
    GuytonKlinger,
    FloorAndCeiling,
    InflationSkipAfterLoss,
    Ratchet,
    RemainingLife,
    CustomRules,
    FixedAmounts,
}

public enum InvestmentStrategyType
{
    FixedRebalance,
    ThresholdRebalance,
    BuyAndHold,
    DecliningGlidePath,
    RisingGlidePath,
    CashBuffer,
    WithdrawFromWinner,
}

public enum RebalanceFrequency { Monthly, Quarterly, Annually }

public enum WithdrawalTiming { Monthly, AnnualInAdvance }

/// <summary>Which historical months are used as retirement start dates.</summary>
public enum StartFrequency { Yearly, Monthly }

public enum RuleMetric { TrailingReturn, DrawdownFromPeak, WithdrawalRate, BalanceVsInitial, Age }

public enum RuleComparison { GreaterThan, LessThan }

public enum RuleAction { AdjustPercent, SetPercentOfBalance, FreezeInflation, ClampMin, ClampMax }

/// <summary>Asset mix as fractions summing to 1.</summary>
public sealed record Allocation(double Equity, double Bond, double Cash)
{
    public double[] ToArray() => [Equity, Bond, Cash];
}

/// <summary>A lump sum at a given age, in today's money. Positive = extra spending, negative = deposit.</summary>
public sealed record OneOff(int Age, double Amount, string? Label = null);

public enum FlowKind { Income, Expense }

/// <summary>
/// A regular yearly income (e.g. State Pension, workplace pension, rent) or outgoing (e.g. mortgage, care costs).
/// Amounts are per year in today's money. Inflation-linked flows keep their value; fixed ones stay the same in
/// pounds from their start age, so they lose value at the planned inflation rate.
/// Income with <see cref="IntoPot"/> (an inheritance, a house sale) is invested rather than spent: it never counts
/// as income on top of spending, whatever the spending strategy.
/// A <see cref="Disabled"/> flow is kept in the plan but left out of the results, so it can be switched back on.
/// </summary>
public sealed record RecurringFlow(
    string? Label,
    FlowKind Kind,
    int StartAge,
    int? EndAge,
    double AnnualAmount,
    bool InflationLinked = true,
    bool IntoPot = false,
    bool Disabled = false);

/// <summary>From <see cref="Age"/> on, take <see cref="Amount"/> a year from the pot (today's money).</summary>
public sealed record SpendingStep(int Age, double Amount);

/// <summary>From <see cref="Age"/> on, the pot is drawn at <see cref="Rate"/> instead of the initial rate (0.03 = 3%).</summary>
public sealed record RateChange(int Age, double Rate);

/// <summary>"IF metric comparison threshold THEN action value" — used by the custom rule strategy.</summary>
public sealed record SpendingRule(
    RuleMetric Metric,
    RuleComparison Comparison,
    double Threshold,
    RuleAction Action,
    double Value);

/// <summary>
/// Spending = a base strategy (<see cref="Type"/>: constant inflation-adjusted, constant percentage or remaining life)
/// plus any number of adjustments switched on with the Use… flags, applied each year in this order:
/// skip inflation after a loss → Guyton-Klinger → ratchet → custom rules → floor and ceiling.
/// Rates are fractions (0.04 = 4%). Spending amounts are annual and in today's money.
/// Older single-strategy inputs (e.g. Type = GuytonKlinger) are converted by <see cref="Normalise"/>.
/// </summary>
public sealed class SpendingParameters
{
    /// <summary>Base strategy. Only ConstantInflationAdjusted, ConstantPercentage, RemainingLife and FixedAmounts are bases.</summary>
    public SpendingStrategyType Type { get; set; } = SpendingStrategyType.ConstantInflationAdjusted;

    /// <summary>Year-one spending (or pot share, for the percentage base) as a fraction of the starting balance.</summary>
    public double InitialRate { get; set; } = 0.04;

    /// <summary>
    /// Later changes to the withdrawal rate, e.g. dropping to 3% once an inheritance arrives. At each change age a
    /// constant base restarts at the new rate on the pot (including that year's deposits and surplus income), and the
    /// guardrails, ratchet and year-one limits are measured from there. The percentage base simply uses the new rate.
    /// </summary>
    public List<RateChange> RateChanges { get; set; } = [];

    /// <summary>The withdrawal rate in force at an age.</summary>
    public double RateAt(double age) =>
        RateChanges.Where(c => c.Age <= age).OrderBy(c => c.Age).Select(c => c.Rate).LastOrDefault(InitialRate);

    // Fixed amounts base: take FixedAmount a year from the pot from retirement, then each step's amount from its age
    public double FixedAmount { get; set; } = 20_000;
    public List<SpendingStep> AmountSteps { get; set; } = [];

    /// <summary>
    /// With fixed amounts your schedule sets every year, so adjustments that build on last year's spending or year one
    /// (skip inflation, ratchet, floor and ceiling) have nothing to act on and are ignored.
    /// </summary>
    public bool Fixed => Type == SpendingStrategyType.FixedAmounts;

    /// <summary>What the fixed amounts base takes from the pot at an age.</summary>
    public double AmountAt(double age) =>
        AmountSteps.Where(c => c.Age <= age).OrderBy(c => c.Age).Select(c => c.Amount).LastOrDefault(FixedAmount);

    // Remaining life base
    public double AssumedRealReturn { get; set; } = 0.02;

    // Legacy "good year / bad year" adjustment, removed: Normalise turns it into custom rules plus floor and ceiling
    public bool UseGoodBadYear { get; set; }
    public double GoodThreshold { get; set; } = 0.10;
    public double BadThreshold { get; set; } = -0.10;
    public double RaiseStep { get; set; } = 0.10;
    public double CutStep { get; set; } = 0.10;
    public double MaxRaise { get; set; } = 0.50;
    public double MaxCut { get; set; } = 0.30;

    // Guyton-Klinger guardrails
    public bool UseGuytonKlinger { get; set; }
    public double UpperGuardrail { get; set; } = 0.20;
    public double LowerGuardrail { get; set; } = 0.20;

    /// <summary>Raise spending by this when the withdrawal rate falls below the lower guardrail (0 = never raise).</summary>
    public double GuardrailRaise { get; set; } = 0.10;

    /// <summary>Cut spending by this when the withdrawal rate rises above the upper guardrail (0 = never cut).</summary>
    public double GuardrailCut { get; set; } = 0.10;
    public bool FreezeAfterLoss { get; set; } = true;

    // Skip inflation rise after a losing year
    public bool UseInflationSkip { get; set; }

    // Ratchet
    public bool UseRatchet { get; set; }
    public double RatchetTrigger { get; set; } = 0.50;
    public double RatchetIncrease { get; set; } = 0.10;

    // Floor & ceiling, relative to year-one spending (-0.15 = 15% below)
    public bool UseFloorCeiling { get; set; }
    public double Floor { get; set; } = -0.15;
    public double Ceiling { get; set; } = 0.25;

    // Custom rules
    public bool UseCustomRules { get; set; }
    public List<SpendingRule> Rules { get; set; } = [];
    public bool ApplyAllMatches { get; set; }

    /// <summary>Legacy: base for the old CustomRules strategy.</summary>
    public SpendingStrategyType? BaseType { get; set; }

    /// <summary>
    /// Converts old inputs: a single-strategy selection into base + adjustment flags, and the removed good year /
    /// bad year adjustment into custom rules plus floor and ceiling. Safe to call repeatedly.
    /// </summary>
    public SpendingParameters Normalise()
    {
        switch (Type)
        {
            case SpendingStrategyType.SimpleGuardrails:
                UseGoodBadYear = true; Type = SpendingStrategyType.ConstantInflationAdjusted; break;
            case SpendingStrategyType.GuytonKlinger:
                UseGuytonKlinger = true; Type = SpendingStrategyType.ConstantInflationAdjusted; break;
            case SpendingStrategyType.InflationSkipAfterLoss:
                UseInflationSkip = true; Type = SpendingStrategyType.ConstantInflationAdjusted; break;
            case SpendingStrategyType.Ratchet:
                UseRatchet = true; Type = SpendingStrategyType.ConstantInflationAdjusted; break;
            case SpendingStrategyType.FloorAndCeiling:
                UseFloorCeiling = true; Type = SpendingStrategyType.ConstantPercentage; break;
            case SpendingStrategyType.CustomRules:
                UseCustomRules = true;
                Type = BaseType == SpendingStrategyType.ConstantPercentage
                    ? SpendingStrategyType.ConstantPercentage
                    : SpendingStrategyType.ConstantInflationAdjusted;
                break;
        }
        if (UseGoodBadYear) ConvertGoodBadYear();
        return this;
    }

    /// <summary>
    /// Good year / bad year raised or cut spending on last year's return, within limits against year one. The same
    /// behaviour is a pair of trailing-return rules, with its limits as the floor and ceiling.
    /// </summary>
    void ConvertGoodBadYear()
    {
        List<SpendingRule> rules = [];
        if (RaiseStep > 0)
            rules.Add(new(RuleMetric.TrailingReturn, RuleComparison.GreaterThan, GoodThreshold, RuleAction.AdjustPercent, RaiseStep));
        if (CutStep > 0)
            rules.Add(new(RuleMetric.TrailingReturn, RuleComparison.LessThan, BadThreshold, RuleAction.AdjustPercent, -CutStep));
        if (UseCustomRules)
        {
            // Keep the user's own rules; the converted pair must apply alongside them, not instead of them
            Rules = [.. rules, .. Rules];
            ApplyAllMatches = true;
        }
        else
        {
            Rules = rules;
            UseCustomRules = true;
        }
        Floor = UseFloorCeiling ? Math.Max(Floor, -MaxCut) : -MaxCut;
        Ceiling = UseFloorCeiling ? Math.Min(Ceiling, MaxRaise) : MaxRaise;
        UseFloorCeiling = true;
        UseGoodBadYear = false;
    }
}

/// <summary>
/// Investment = a base strategy (<see cref="Type"/>) plus an optional cash buffer that works with any of them.
/// Older inputs with Type = CashBuffer are converted by <see cref="Normalise"/>.
/// </summary>
public sealed class InvestmentParameters
{
    public InvestmentStrategyType Type { get; set; } = InvestmentStrategyType.FixedRebalance;
    public RebalanceFrequency Frequency { get; set; } = RebalanceFrequency.Annually;

    /// <summary>Threshold rebalance band in percentage points of weight (0.05 = 5pp).</summary>
    public double Band { get; set; } = 0.05;

    // Glide paths
    public double StartEquity { get; set; } = 0.60;
    public double EndEquity { get; set; } = 0.40;
    public int GlideYears { get; set; } = 20;

    // Cash buffer (optional, on top of the base)
    public bool UseCashBuffer { get; set; }
    public double BufferYears { get; set; } = 2;

    public InvestmentParameters Normalise()
    {
        if (Type == InvestmentStrategyType.CashBuffer)
        {
            UseCashBuffer = true;
            Type = InvestmentStrategyType.FixedRebalance;
            Frequency = RebalanceFrequency.Annually;
        }
        return this;
    }
}

public sealed class SimulationInput
{
    public double StartingBalance { get; set; } = 100_000;
    public int RetirementAge { get; set; } = 60;

    /// <summary>
    /// Your age today; null = retiring now. "Today's money" means today, so future (nominal) pounds grow with planned
    /// inflation from this age. The simulation itself is unaffected: it runs in today's money from retirement.
    /// </summary>
    public int? CurrentAge { get; set; }

    /// <summary>
    /// Judge the plan against UK life tables as well: the chance of running out while still alive. The age of death
    /// then acts as the planning horizon.
    /// </summary>
    public LifeTable LifeTable { get; set; } = LifeTable.None;
    public int DeathAge { get; set; } = 94;

    /// <summary>Planned constant inflation (0.025 = 2.5% p.a.). Used for the nominal view and inflation-skip rules.</summary>
    public double InflationRate { get; set; } = 0.025;

    /// <summary>
    /// Change to every year's share return, as a fraction a year (-0.01 = 1% a year lower). Share history before 2010 is
    /// US shares, among the best performers anywhere; lowering it tests how much a plan depends on that.
    /// </summary>
    public double EquityReturnAdjustment { get; set; }

    /// <summary>Annual fees as a fraction of the balance.</summary>
    public double FeeRate { get; set; } = 0.005;

    public Allocation Allocation { get; set; } = new(0.6, 0.4, 0);
    public List<OneOff> OneOffs { get; set; } = [];

    /// <summary>Regular incomes (reduce what is drawn from the pot) and outgoings (add to it).</summary>
    public List<RecurringFlow> Flows { get; set; } = [];

    /// <summary>The flows that count towards the results (switched-off ones are left out).</summary>
    [JsonIgnore]
    public IEnumerable<RecurringFlow> ActiveFlows => Flows.Where(f => !f.Disabled);

    /// <summary>
    /// Optional minimum acceptable annual spending (today's money, including other income). Dropping below it is
    /// reported, not a failure.
    /// </summary>
    public double? SpendingFloor { get; set; }

    /// <summary>A path only succeeds if its final real balance is at least this amount.</summary>
    public double LegacyTarget { get; set; }

    public WithdrawalTiming WithdrawalTiming { get; set; } = WithdrawalTiming.Monthly;

    /// <summary>Yearly: one start per calendar year (the first month available, normally January). Monthly: every month.</summary>
    public StartFrequency StartFrequency { get; set; } = StartFrequency.Yearly;
    public SpendingParameters Spending { get; set; } = new();
    public InvestmentParameters Investment { get; set; } = new();

    public int HorizonMonths => (DeathAge - RetirementAge) * 12;
}
