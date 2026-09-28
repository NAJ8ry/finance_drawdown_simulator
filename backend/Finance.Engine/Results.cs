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

    /// <summary>Balance at each birthday: index 0 = retirement age.</summary>
    public required double[] Balances { get; init; }

    /// <summary>Annual spending (what you live on, including other income) for each year of retirement.</summary>
    public required double[] Spending { get; init; }

    /// <summary>Net amount taken from the pot each year (negative when income was paid into it).</summary>
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

    public int? BestIndex { get; set; }
    public int? MedianIndex { get; set; }
    public int? WorstIndex { get; set; }

    public List<PercentileBand> Bands { get; set; } = [];
    public List<PercentileBand> AgeTable { get; set; } = [];

    /// <summary>Indices into <see cref="Paths"/> of the worst start months (failures first, earliest first).</summary>
    public List<int> WorstStarts { get; set; } = [];

    public List<PathResult> Paths { get; set; } = [];
}
