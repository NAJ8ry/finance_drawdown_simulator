using System.Text.Json;

namespace Finance.Engine;

/// <summary>A spending schedule the fitter proposes, and how it performed.</summary>
public sealed record SpendingFit(
    /// <summary>Spending (today's money, including other income) at the shape's reference level of 1.</summary>
    double Level,
    /// <summary>False when even spending nothing from the pot misses the target (e.g. outgoings exceed income).</summary>
    bool Feasible,
    /// <summary>The user's plan with spending replaced by the fitted fixed amounts.</summary>
    SimulationInput Plan,
    SimulationResult Result);

/// <summary>
/// Finds the highest spending that follows a given shape by age (e.g. the way real spending tends to ease through
/// retirement) and still succeeds in at least a target share of historical start dates. The result is a fixed
/// amounts schedule: what to take from the pot at each age, with pensions and other income on top.
/// </summary>
public static class SpendingFitter
{
    /// <summary>Schedule amounts are rounded to this many pounds so they read cleanly.</summary>
    public const double Rounding = 100;

    /// <summary>A new step starts when the ideal amount has moved this far from the current step (or after this many years).</summary>
    const double StepTolerance = 0.05, MinStepChange = 500;
    const int MaxStepYears = 5;

    /// <param name="shape">Spending at each age relative to the level (1 = the level). Includes other income.</param>
    /// <param name="targetSuccess">Minimum success rate, 0-100.</param>
    /// <param name="maxLifetimeRuin">
    /// Instead of a success rate: the highest acceptable chance (0-100) of running out while alive. Needs
    /// <paramref name="mortality"/> and a life table on the input.
    /// </param>
    public static SpendingFit Fit(SimulationInput input, IReadOnlyList<MarketMonth> history, Func<int, double> shape, double targetSuccess,
        Mortality? mortality = null, int? currentYear = null, double? maxLifetimeRuin = null)
    {
        if (maxLifetimeRuin is not null && (mortality is null || input.LifeTable == LifeTable.None))
            throw new ArgumentException("A lifetime target needs life tables.");
        input.Spending.Normalise();
        var (income, _, _) = Simulator.FlowsByYear(input);

        (SimulationInput Plan, SimulationResult Result) Try(double level)
        {
            var plan = WithSchedule(input, Schedule(input, income, level, shape));
            return (plan, Simulator.Run(plan, history, mortality, currentYear));
        }
        bool Meets(SimulationResult r) => maxLifetimeRuin is { } max
            ? r.LifetimeRuinRate is { } ruin && ruin <= max + 1e-9
            : r.SuccessRate is { } s && s >= targetSuccess - 1e-9;

        var floor = Try(0);
        if (!Meets(floor.Result)) return new SpendingFit(0, false, floor.Plan, floor.Result);

        // Grow an upper bound that fails, then bisect between the last level that met the target and it
        double lo = 0, hi = Math.Max(1_000, input.StartingBalance / Math.Max(1, input.DeathAge - input.RetirementAge) + income.Max());
        var best = floor;
        for (var i = 0; i < 40; i++)
        {
            var t = Try(hi);
            if (!Meets(t.Result)) break;
            lo = hi;
            best = t;
            hi *= 2;
        }
        while (hi - lo > Rounding / 4)
        {
            var mid = (lo + hi) / 2;
            var t = Try(mid);
            if (Meets(t.Result)) { lo = mid; best = t; }
            else hi = mid;
        }
        return new SpendingFit(lo, true, best.Plan, best.Result);
    }

    /// <summary>
    /// What to take from the pot each year: the shaped spending less other income, grouped into readable steps.
    /// Steps break where income starts or stops, so a pension arriving shows as its own step.
    /// </summary>
    public static List<SpendingStep> Schedule(SimulationInput input, double[] income, double level, Func<int, double> shape)
    {
        var years = income.Length;
        var ideal = new double[years];
        for (var y = 0; y < years; y++)
            ideal[y] = Math.Max(0, level * shape(input.RetirementAge + y) - income[y]);

        var breaks = Simulator.PlannedChangeAges(input);
        List<SpendingStep> steps = [];
        var start = 0;
        for (var y = 1; y <= years; y++)
        {
            var end = y == years
                || breaks.Contains(input.RetirementAge + y)
                || y - start >= MaxStepYears
                || Math.Abs(ideal[y] - ideal[start]) > Math.Max(MinStepChange, StepTolerance * ideal[start]);
            if (!end) continue;
            var amount = Math.Round(ideal[start..y].Average() / Rounding) * Rounding;
            if (steps.Count == 0 || steps[^1].Amount != amount) steps.Add(new SpendingStep(input.RetirementAge + start, amount));
            start = y;
        }
        return steps;
    }

    /// <summary>A copy of the plan whose spending is the given fixed amounts schedule, with no adjustments.</summary>
    public static SimulationInput WithSchedule(SimulationInput input, List<SpendingStep> steps)
    {
        var plan = JsonSerializer.Deserialize<SimulationInput>(JsonSerializer.Serialize(input))!;
        plan.Spending = new SpendingParameters
        {
            Type = SpendingStrategyType.FixedAmounts,
            FixedAmount = steps.Count > 0 ? steps[0].Amount : 0,
            AmountSteps = steps.Skip(1).ToList(),
        };
        return plan;
    }
}
