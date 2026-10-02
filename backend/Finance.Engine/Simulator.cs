namespace Finance.Engine;

/// <summary>
/// Historical backtest: replays the plan from every year (or, optionally, every month) in history and counts how many paths
/// keep money until the age of death.
///
/// Everything runs in real terms (today's money): each month's nominal return is deflated by that
/// month's historical UK inflation. The user's constant planned inflation is only used to express
/// results in nominal terms and by rules that skip an inflation rise.
/// </summary>
public static class Simulator
{
    /// <summary>Start months with less data than this are not simulated at all.</summary>
    public const int MinimumMonths = 12;

    const double Epsilon = 1e-6;

    /// <param name="mortality">UK life tables, needed when <see cref="SimulationInput.LifeTable"/> is set.</param>
    /// <param name="currentYear">This calendar year, to place the person in their cohort.</param>
    public static SimulationResult Run(SimulationInput input, IReadOnlyList<MarketMonth> history, Mortality? mortality = null, int? currentYear = null)
    {
        input.Spending.Normalise();
        input.Investment.Normalise();
        var errors = Validate(input);
        if (errors.Count > 0) throw new ArgumentException(string.Join("; ", errors));

        var real = RealReturns.From(history, input.EquityReturnAdjustment);
        var paths = new List<PathResult>();
        for (var start = 0; start + MinimumMonths <= history.Count; start++)
        {
            // Yearly: only the first month of each calendar year (January, or the first month of data)
            if (input.StartFrequency == StartFrequency.Yearly && start > 0 && history[start].Year == history[start - 1].Year)
                continue;
            paths.Add(RunPath(input, real, start, history[start].Label));
        }

        var result = Summarise(input, paths);
        result.DataLastMonth = history.Count > 0 ? history[^1].Label : null;
        if (input.LifeTable != LifeTable.None && mortality is not null)
            AddLifespan(input, result, mortality, currentYear ?? DateTime.UtcNow.Year);
        return result;
    }

    /// <summary>Weights each run-out by the chance of still being alive then (Milevsky &amp; Robinson's lifetime ruin).</summary>
    static void AddLifespan(SimulationInput input, SimulationResult result, Mortality mortality, int currentYear)
    {
        var survival = mortality.Survival(input.LifeTable, input.CurrentAge ?? input.RetirementAge, currentYear,
            input.RetirementAge, input.DeathAge);
        result.Survival = survival;
        result.OutliveHorizonRate = 100 * survival[^1];
        var complete = result.Paths.Where(p => !p.Partial).ToList();
        if (complete.Count == 0) return;
        result.LifetimeRuinRate = 100 * complete
            .Select(p => p.Failed && p.FailAge is { } age ? Mortality.At(survival, input.RetirementAge, age) : 0)
            .Average();
    }

    public static PathResult RunPath(SimulationInput input, RealReturns real, int start, string label)
    {
        var horizon = input.HorizonMonths;
        var months = Math.Min(horizon, real.Count - start);
        var years = horizon / 12;
        var invest = new InvestmentStrategy(input.Investment, input.Allocation);
        var monthlyFee = 1 - Math.Pow(1 - input.FeeRate, 1.0 / 12);
        var oneOffs = OneOffsByMonth(input);
        var (income, expenses, intoPot) = FlowsByYear(input);
        // New capital each year: one-off deposits plus income paid into the pot
        var deposits = new double[Math.Max(1, years)];
        foreach (var (month, amount) in oneOffs)
            if (amount < 0) deposits[month / 12] -= amount;
        for (var y = 0; y < deposits.Length && y < intoPot.Length; y++) deposits[y] += intoPot[y];

        var path = new PathResult
        {
            Start = label,
            Partial = months < horizon,
            Balances = new double[months / 12 + 1],
            Spending = new double[(months + 11) / 12],
            Withdrawals = new double[(months + 11) / 12],
            MinBalance = input.StartingBalance,
        };
        path.Balances[0] = input.StartingBalance;

        var state = new PathState
        {
            Balance = input.StartingBalance,
            InitialBalance = input.StartingBalance,
            PeakBalance = input.StartingBalance,
            RatchetBase = input.StartingBalance,
            InflationRate = input.InflationRate,
        };

        var firstDraw = input.Spending.Type == SpendingStrategyType.FixedAmounts
            ? input.Spending.AmountAt(input.RetirementAge)
            : input.Spending.InitialRate * input.StartingBalance;
        var holdings = invest.TargetWeights(0, input.StartingBalance, firstDraw)
            .Select(w => w * input.StartingBalance).ToArray();
        var targets = holdings.Select(h => h / input.StartingBalance).ToArray();
        var portfolioTrail = new Trailing12();
        var equityTrail = new Trailing12();
        var spend = 0.0;
        var draw = 0.0; // this year's net withdrawal from the pot, which sizes the cash buffer
        var peak = input.StartingBalance;
        var inflationFactor = 1 + input.InflationRate;

        for (var m = 0; m < months; m++)
        {
            var total = holdings.Sum();
            if (m % 12 == 0)
            {
                var year = m / 12;
                state.Balance = total;
                state.Age = input.RetirementAge + year;
                state.YearsRemaining = years - year;
                state.TrailingReturn = year == 0 ? 0 : portfolioTrail.Product * inflationFactor - 1;
                state.Income = income[year];
                state.Expenses = expenses[year];
                state.Rate = input.Spending.RateAt(state.Age);
                var restart = year > 0 && input.Spending.Type != SpendingStrategyType.FixedAmounts
                    && input.Spending.RateChanges.Any(c => c.Age == state.Age);
                if (year == 0 || restart)
                {
                    // A rate change restarts the plan on everything available this year, new money included
                    var capital = restart ? total + deposits[year] + Math.Max(0, state.Income - state.Expenses) : total;
                    spend = SpendingStrategy.Initial(input.Spending, state, capital);
                    state.InitialSpending = spend;
                    state.InitialWithdrawalRate = capital > 0 ? Math.Max(0, state.NetWithdrawal(spend)) / capital : 0;
                    if (restart)
                    {
                        state.InitialBalance = capital;
                        state.RatchetBase = capital;
                    }
                }
                else
                {
                    state.PreviousSpending = spend;
                    // If other income covered everything so far, the guardrails' reference is the first year the pot is drawn on
                    if (state.InitialWithdrawalRate <= 0 && total > 0 && state.NetWithdrawal(spend) > 0)
                        state.InitialWithdrawalRate = state.NetWithdrawal(spend) / total;
                    spend = SpendingStrategy.Next(input.Spending, state);
                }
                if (!restart)
                {
                    // Deposits and surplus income this year are new capital, not growth
                    var added = deposits[year] + Math.Max(0, -state.NetWithdrawal(spend));
                    spend = SpendingStrategy.AddCapital(input.Spending, state, spend, added);
                }
                state.PreviousSpending = spend;
                path.Spending[year] = spend;
                if (input.SpendingFloor is { } floor && spend < floor - Epsilon) path.BelowFloor = true;
                draw = Math.Max(0, state.NetWithdrawal(spend));
                targets = invest.TargetWeights(m, total, draw);
            }

            var downMarket = m >= 12 && equityTrail.Product * inflationFactor < 1;
            var withdrawal = input.WithdrawalTiming == WithdrawalTiming.Monthly
                ? spend / 12
                : m % 12 == 0 ? spend : 0;
            // Other income and regular outgoings arrive monthly
            withdrawal += (expenses[m / 12] - income[m / 12] - intoPot[m / 12]) / 12;
            if (oneOffs.TryGetValue(m, out var extra)) withdrawal += extra;

            if (withdrawal > 0)
            {
                // Spending that can't be fully funded is a failure; exactly emptying the pot is not (yet)
                if (withdrawal > total + Epsilon || total <= Epsilon)
                {
                    Fail(path, input, m, months, income, expenses);
                    return path;
                }
                invest.Withdraw(holdings, withdrawal, targets, downMarket);
            }
            else if (withdrawal < 0)
            {
                // The cash buffer is a sum of money, not a share, so re-size the weights for the bigger pot
                targets = invest.TargetWeights(m, total - withdrawal, draw);
                InvestmentStrategy.Deposit(holdings, -withdrawal, targets);
                // Paid-in money isn't a market gain, so it moves the peak and ratchet base too
                SpendingStrategy.Deposited(state, -withdrawal);
                peak += -withdrawal;
            }
            // Capital paid in (an inheritance, a one-off deposit) is not money taken out, so it is left out of the record
            path.Withdrawals[m / 12] += withdrawal + intoPot[m / 12] / 12 + Math.Max(0, -extra);

            var before = holdings.Sum();
            var i = start + m;
            holdings[InvestmentStrategy.Equity] *= (1 + real.Equity[i]) * (1 - monthlyFee);
            holdings[InvestmentStrategy.Bond] *= (1 + real.Bond[i]) * (1 - monthlyFee);
            holdings[InvestmentStrategy.Cash] *= (1 + real.Cash[i]) * (1 - monthlyFee);
            var after = holdings.Sum();

            portfolioTrail.Add(before > 0 ? after / before : 1);
            equityTrail.Add(1 + real.Equity[i]);

            if (after > peak) peak = after;
            state.PeakBalance = peak;
            path.MaxDrawdown = Math.Max(path.MaxDrawdown, peak > 0 ? 1 - after / peak : 0);
            path.MinBalance = Math.Min(path.MinBalance, after);

            targets = invest.TargetWeights(m, after, draw);
            if (invest.ShouldRebalance(m, holdings, targets, downMarket))
                InvestmentStrategy.Rebalance(holdings, targets);
            invest.RefillCash(m, holdings, targets, downMarket);

            if (m % 12 == 11) path.Balances[(m + 1) / 12] = after;
        }

        path.Months = months;
        path.EndBalance = holdings.Sum();
        path.Succeeded = !path.Partial && path.EndBalance >= input.LegacyTarget;
        return path;
    }

    static void Fail(PathResult path, SimulationInput input, int month, int months, double[] income, double[] expenses)
    {
        path.Failed = true;
        path.FailAge = input.RetirementAge + month / 12.0;
        path.Months = months;
        path.EndBalance = 0;
        path.MinBalance = 0;
        path.MaxDrawdown = 1;
        if (input.SpendingFloor is > 0) path.BelowFloor = true;
        // Balances after failure stay at 0; from then on you live on other income alone
        for (var y = month / 12 + 1; y < path.Balances.Length; y++) path.Balances[y] = 0;
        for (var y = month / 12 + 1; y < path.Spending.Length; y++)
            path.Spending[y] = Math.Max(0, income[y] - expenses[y]);
    }

    /// <summary>
    /// Annual other income, regular outgoings and money paid into the pot (today's money) for each year of retirement.
    /// </summary>
    public static (double[] Income, double[] Expenses, double[] IntoPot) FlowsByYear(SimulationInput input)
    {
        var years = Math.Max(1, input.HorizonMonths / 12);
        var income = new double[years];
        var expenses = new double[years];
        var intoPot = new double[years];
        foreach (var f in input.ActiveFlows)
        {
            var end = f.EndAge ?? input.DeathAge;
            var from = Math.Max(f.StartAge, input.RetirementAge);
            for (var y = 0; y < years; y++)
            {
                var age = input.RetirementAge + y;
                if (age < f.StartAge || age >= end) continue;
                // Fixed amounts stay the same in pounds, so lose value at the planned inflation rate
                var amount = f.InflationLinked ? f.AnnualAmount : f.AnnualAmount / Math.Pow(1 + input.InflationRate, age - from);
                if (f.Kind == FlowKind.Expense) expenses[y] += amount;
                else if (f.IntoPot) intoPot[y] += amount;
                else income[y] += amount;
            }
        }
        return (income, expenses, intoPot);
    }

    static Dictionary<int, double> OneOffsByMonth(SimulationInput input)
    {
        var map = new Dictionary<int, double>();
        foreach (var o in input.OneOffs)
        {
            var m = (o.Age - input.RetirementAge) * 12;
            if (m < 0 || m >= input.HorizonMonths) continue;
            map[m] = map.GetValueOrDefault(m) + o.Amount;
        }
        return map;
    }

    /// <summary>A fall in spending this big from one year to the next counts as a cut.</summary>
    public const double CutThreshold = 0.10;

    /// <summary>
    /// Ages where the plan itself changes spending: rate changes, fixed-amount steps, and other income starting or
    /// stopping. A fall at these ages is planned, so it is not counted as a cut.
    /// </summary>
    public static HashSet<int> PlannedChangeAges(SimulationInput input)
    {
        var p = input.Spending;
        var ages = new HashSet<int>(p.Fixed ? p.AmountSteps.Select(s => s.Age) : p.RateChanges.Select(c => c.Age));
        foreach (var f in input.ActiveFlows.Where(f => f.Kind == FlowKind.Income && !f.IntoPot))
        {
            ages.Add(f.StartAge);
            if (f.EndAge is { } end) ages.Add(end);
        }
        return ages;
    }

    /// <summary>Fills in the per-path cut, floor and run-out measures.</summary>
    static void MeasureStability(SimulationInput input, PathResult path, HashSet<int> planned)
    {
        var s = path.Spending;
        for (var y = 1; y < s.Length; y++)
        {
            if (planned.Contains(input.RetirementAge + y) || s[y - 1] <= 0) continue;
            var fall = 1 - s[y] / s[y - 1];
            if (fall >= CutThreshold - Epsilon) path.Cuts++;
            path.WorstCut = Math.Max(path.WorstCut, fall);
        }
        if (input.SpendingFloor is { } floor) path.YearsBelowFloor = s.Count(v => v < floor - Epsilon);
        if (path.Failed && path.FailAge is { } age) path.YearsWithoutPot = Math.Max(0, input.DeathAge - age);
    }

    static SimulationResult Summarise(SimulationInput input, List<PathResult> paths)
    {
        var planned = PlannedChangeAges(input);
        foreach (var p in paths) MeasureStability(input, p, planned);

        var complete = paths.Where(p => !p.Partial).ToList();
        var result = new SimulationResult
        {
            Paths = paths,
            CompleteCount = complete.Count,
            SuccessCount = complete.Count(p => p.Succeeded),
            FailedCount = complete.Count(p => p.Failed),
            PartialCount = paths.Count(p => p.Partial),
            PartialFailedCount = paths.Count(p => p.Partial && p.Failed),
            FirstStart = paths.FirstOrDefault()?.Start,
            LastCompleteStart = complete.LastOrDefault()?.Start,
            WorstDepletionAge = paths.Where(p => p.Failed).Select(p => p.FailAge).Min(),
        };

        // Worst first: failures ordered by earliest failure, then by lowest ending balance
        static (int, double) Rank(PathResult p) => p.Failed ? (0, p.FailAge!.Value) : (1, p.EndBalance);
        var ranked = Enumerable.Range(0, paths.Count)
            .Where(i => !paths[i].Partial || paths[i].Failed)
            .OrderBy(i => Rank(paths[i]))
            .ToList();
        result.WorstStarts = ranked.Take(10).ToList();

        if (complete.Count == 0) return result;

        result.SuccessRate = 100.0 * result.SuccessCount / complete.Count;
        var ends = complete.Select(p => p.EndBalance).ToArray();
        result.MedianEndBalance = Percentile(ends, 50);
        result.P10EndBalance = Percentile(ends, 10);
        result.P90EndBalance = Percentile(ends, 90);
        result.BelowFloorRate = input.SpendingFloor is > 0 ? 100.0 * complete.Count(p => p.BelowFloor) / complete.Count : null;
        result.MedianAverageSpending = Percentile(complete.Select(p => p.Spending.Average()).ToArray(), 50);
        result.MinimumSpending = complete.Min(p => p.Spending.Min());
        result.MedianMaxDrawdown = Percentile(complete.Select(p => p.MaxDrawdown).ToArray(), 50);
        result.CutRate = 100.0 * complete.Count(p => p.Cuts > 0) / complete.Count;
        result.WorstCut = complete.Max(p => p.WorstCut);
        var ranOut = complete.Where(p => p.Failed).Select(p => p.YearsWithoutPot).ToArray();
        if (ranOut.Length > 0)
        {
            result.MedianYearsWithoutPot = Percentile(ranOut, 50);
            result.MaxYearsWithoutPot = ranOut.Max();
        }
        if (input.SpendingFloor is > 0)
            result.P90YearsBelowFloor = Percentile(complete.Select(p => (double)p.YearsBelowFloor).ToArray(), 90);

        var completeRanked = Enumerable.Range(0, paths.Count).Where(i => !paths[i].Partial).OrderBy(i => Rank(paths[i])).ToList();
        result.WorstIndex = completeRanked[0];
        result.MedianIndex = completeRanked[completeRanked.Count / 2];
        result.BestIndex = completeRanked[^1];

        var years = input.HorizonMonths / 12;
        for (var y = 0; y <= years; y++)
        {
            var values = complete.Select(p => p.Balances[y]).ToArray();
            result.Bands.Add(new PercentileBand(input.RetirementAge + y,
                Percentile(values, 5), Percentile(values, 10), Percentile(values, 25), Percentile(values, 50),
                Percentile(values, 75), Percentile(values, 90), Percentile(values, 95)));
        }
        var tableAges = new[] { 70, 80, 90 }.Where(a => a > input.RetirementAge && a < input.DeathAge).Append(input.DeathAge);
        result.AgeTable = tableAges.Select(a => result.Bands[a - input.RetirementAge]).ToList();
        return result;
    }

    /// <summary>Linear-interpolated percentile (0-100).</summary>
    public static double Percentile(double[] values, double pct)
    {
        if (values.Length == 0) return double.NaN;
        var sorted = values.OrderBy(v => v).ToArray();
        var rank = pct / 100 * (sorted.Length - 1);
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
    }

    public static List<string> Validate(SimulationInput i)
    {
        var e = new List<string>();
        if (i.StartingBalance <= 0) e.Add("Starting balance must be greater than zero.");
        if (i.RetirementAge is < 30 or > 100) e.Add("Retirement age must be between 30 and 100.");
        if (i.DeathAge <= i.RetirementAge) e.Add("Age of death must be after retirement age.");
        if (i.DeathAge > 120) e.Add("Age of death must be 120 or less.");
        if (i.CurrentAge is { } now && (now < 16 || now > i.RetirementAge)) e.Add("Current age must be between 16 and your retirement age.");
        if (i.InflationRate is < -0.05 or > 0.2) e.Add("Planned inflation must be between -5% and 20%.");
        if (i.FeeRate is < 0 or > 0.05) e.Add("Fees must be between 0% and 5%.");
        if (i.EquityReturnAdjustment is < -0.05 or > 0.05) e.Add("The share returns adjustment must be between -5% and +5% a year.");
        var a = i.Allocation;
        if (a.Equity < 0 || a.Bond < 0 || a.Cash < 0) e.Add("Allocation percentages cannot be negative.");
        if (Math.Abs(a.Equity + a.Bond + a.Cash - 1) > 0.001) e.Add("Allocation must add up to 100%.");
        if (i.Spending.InitialRate is < 0 or > 0.5) e.Add("Initial withdrawal rate must be between 0% and 50%.");
        if (i.Spending.Type == SpendingStrategyType.FixedAmounts)
        {
            if (i.Spending.FixedAmount < 0) e.Add("The amount taken from the pot cannot be negative.");
            foreach (var st in i.Spending.AmountSteps)
            {
                if (st.Amount < 0) e.Add($"The amount from age {st.Age} cannot be negative.");
                if (st.Age <= i.RetirementAge || st.Age >= i.DeathAge) e.Add($"An amount change at age {st.Age} must be after retirement and before the age of death.");
            }
        }
        foreach (var c in i.Spending.RateChanges)
        {
            if (c.Rate is < 0 or > 0.5) e.Add($"The withdrawal rate from age {c.Age} must be between 0% and 50%.");
            if (c.Age <= i.RetirementAge || c.Age >= i.DeathAge) e.Add($"A rate change at age {c.Age} must be after retirement and before the age of death.");
        }
        if (i.Investment.Type is InvestmentStrategyType.DecliningGlidePath or InvestmentStrategyType.RisingGlidePath
            && (i.Investment.StartEquity is < 0 or > 1 || i.Investment.EndEquity is < 0 or > 1))
            e.Add("Glide path equity percentages must be between 0% and 100%.");
        if (i.Investment.BufferYears < 0) e.Add("Cash buffer years cannot be negative.");
        foreach (var f in i.ActiveFlows)
        {
            var name = string.IsNullOrWhiteSpace(f.Label) ? f.Kind.ToString().ToLowerInvariant() : $"\"{f.Label}\"";
            if (f.AnnualAmount < 0) e.Add($"The amount for {name} cannot be negative.");
            if (f.EndAge is { } end && end <= f.StartAge) e.Add($"The end age for {name} must be after its start age.");
        }
        return e;
    }

    /// <summary>Product of the last (up to) 12 monthly growth factors.</summary>
    sealed class Trailing12
    {
        readonly double[] _buf = new double[12];
        int _count, _next;

        public void Add(double factor)
        {
            _buf[_next] = factor;
            _next = (_next + 1) % 12;
            if (_count < 12) _count++;
        }

        public double Product
        {
            get
            {
                var p = 1.0;
                for (var k = 0; k < _count; k++) p *= _buf[k];
                return p;
            }
        }
    }
}

/// <summary>Monthly real (inflation-adjusted) returns derived from nominal history.</summary>
public sealed class RealReturns
{
    public required double[] Equity { get; init; }
    public required double[] Bond { get; init; }
    public required double[] Cash { get; init; }
    public int Count => Equity.Length;

    /// <param name="equityAdjustment">Change to share returns, a fraction a year, spread evenly over the months.</param>
    public static RealReturns From(IReadOnlyList<MarketMonth> history, double equityAdjustment = 0)
    {
        double Real(double nominal, double inflation) => (1 + nominal) / (1 + inflation) - 1;
        var monthly = Math.Pow(1 + equityAdjustment, 1.0 / 12);
        return new RealReturns
        {
            Equity = history.Select(h => (1 + Real(h.Equity, h.Inflation)) * monthly - 1).ToArray(),
            Bond = history.Select(h => Real(h.Bond, h.Inflation)).ToArray(),
            Cash = history.Select(h => Real(h.Cash, h.Inflation)).ToArray(),
        };
    }
}
