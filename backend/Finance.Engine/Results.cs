namespace Finance.Engine;

/// <summary>One historical start month replayed through the whole retirement. Money is in today's money (real).</summary>
public sealed class PathResult
{
    public required string Start { get; init; }
    public int Months { get; set; }
    public bool Partial { get; set; }
    public bool Failed { get; set; }
    public double? FailAge { get; set; }
    public bool Succeeded { get; set; }
    public bool BelowFloor { get; set; }
    public double EndBalance { get; set; }
    public double MinBalance { get; set; }
    public double MaxDrawdown { get; set; }

    /// <summary>Years with an unplanned spending cut of 10% or more (see <see cref="Simulator.PlannedChangeAges"/>).</summary>
    public int Cuts { get; set; }

    /// <summary>Largest unplanned one-year fall in spending, as a fraction (0.25 = 25% less than the year before).</summary>
    public double WorstCut { get; set; }

    /// <summary>Years of retirement with spending below the minimum you set.</summary>
    public int YearsBelowFloor { get; set; }

    /// <summary>Years lived on other income alone after the pot ran out (0 if it lasted).</summary>
    public double YearsWithoutPot { get; set; }

    /// <summary>Balance at each birthday: index 0 = retirement age.</summary>
    public required double[] Balances { get; init; }

    /// <summary>Annual spending (what you live on, including other income) for each year of retirement.</summary>
    public required double[] Spending { get; init; }

    /// <summary>
    /// Net amount taken from the pot each year: negative when other income exceeded spending and the surplus was
    /// invested. Capital paid in (income marked as into the pot, one-off deposits) is not counted.
    /// </summary>
    public required double[] Withdrawals { get; init; }
}

public sealed record PercentileBand(int Age, double P5, double P10, double P25, double P50, double P75, double P90, double P95);

public sealed class SimulationResult
{
    /// <summary>Percentage (0-100) of complete paths that succeeded, or null if no path covers the whole retirement.</summary>
    public double? SuccessRate { get; set; }
    public int CompleteCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public int PartialCount { get; set; }
    public int PartialFailedCount { get; set; }

    public string? FirstStart { get; set; }
    public string? LastCompleteStart { get; set; }
    public string? DataLastMonth { get; set; }

    public double? MedianEndBalance { get; set; }
    public double? P10EndBalance { get; set; }
    public double? P90EndBalance { get; set; }
    public double? WorstDepletionAge { get; set; }
    public double? BelowFloorRate { get; set; }
    public double? MedianAverageSpending { get; set; }
    public double? MinimumSpending { get; set; }
    public double? MedianMaxDrawdown { get; set; }

    // How the plan feels to live through, beyond whether the money lasts (complete paths)

    /// <summary>Percentage (0-100) of paths with at least one unplanned spending cut of 10% or more.</summary>
    public double? CutRate { get; set; }

    /// <summary>Largest unplanned one-year spending cut in any path, as a fraction.</summary>
    public double? WorstCut { get; set; }

    /// <summary>Of the paths that ran out, the median and longest time lived on other income alone.</summary>
    public double? MedianYearsWithoutPot { get; set; }
    public double? MaxYearsWithoutPot { get; set; }

    /// <summary>Years below the spending minimum in the worst 1 in 10 paths (null without a minimum).</summary>
    public double? P90YearsBelowFloor { get; set; }

    // With life tables (null otherwise)

    /// <summary>
    /// Percentage (0-100) chance of running out of money while still alive (for a couple: while either is alive),
    /// averaged over complete paths; given alive at retirement.
    /// </summary>
    public double? LifetimeRuinRate { get; set; }

    /// <summary>Percentage chance of being alive at the age of death, i.e. of outliving the plan's horizon.</summary>
    public double? OutliveHorizonRate { get; set; }

    /// <summary>Chance (0-1) of being alive at each birthday from retirement to the age of death, given alive at retirement.</summary>
    public double[]? Survival { get; set; }

    public int? BestIndex { get; set; }
    public int? MedianIndex { get; set; }
    public int? WorstIndex { get; set; }

    public List<PercentileBand> Bands { get; set; } = [];
    public List<PercentileBand> AgeTable { get; set; } = [];

    /// <summary>Indices into <see cref="Paths"/> of the worst start months (failures first, earliest first).</summary>
    public List<int> WorstStarts { get; set; } = [];

    public List<PathResult> Paths { get; set; } = [];
}
