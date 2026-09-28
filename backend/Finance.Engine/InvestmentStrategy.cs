namespace Finance.Engine;

/// <summary>
/// Decides target weights, when to rebalance and which assets withdrawals come from.
/// A base strategy sets the mix; an optional cash buffer can be layered on top of any base.
/// Holdings are indexed 0 = equity, 1 = bonds, 2 = cash.
/// </summary>
public sealed class InvestmentStrategy
{
    public const int Equity = 0, Bond = 1, Cash = 2;

    readonly InvestmentParameters _p;
    readonly double[] _base;

    public InvestmentStrategy(InvestmentParameters p, Allocation baseAllocation)
    {
        _p = p.Normalise();
        _base = Normalise(baseAllocation.ToArray());
    }

    /// <summary>
    /// Target weights for a given month since retirement. The cash buffer needs this year's withdrawal from the pot.
    /// </summary>
    public double[] TargetWeights(int month, double balance, double annualWithdrawal)
    {
        var w = BaseWeights(month);
        if (!_p.UseCashBuffer) return w;
        var cash = balance > 0 ? Math.Clamp(_p.BufferYears * annualWithdrawal / balance, 0, 1) : 0;
        // The buffer is carved out first; the base mix applies to the rest
        return [w[Equity] * (1 - cash), w[Bond] * (1 - cash), w[Cash] * (1 - cash) + cash];
    }

    double[] BaseWeights(int month)
    {
        if (_p.Type is not (InvestmentStrategyType.DecliningGlidePath or InvestmentStrategyType.RisingGlidePath))
            return (double[])_base.Clone();
        var progress = _p.GlideYears <= 0 ? 1 : Math.Min(1, month / 12.0 / _p.GlideYears);
        var equity = Math.Clamp(_p.StartEquity + (_p.EndEquity - _p.StartEquity) * progress, 0, 1);
        var bondShare = _base[Bond] + _base[Cash] > 0 ? _base[Bond] / (_base[Bond] + _base[Cash]) : 1;
        return [equity, (1 - equity) * bondShare, (1 - equity) * (1 - bondShare)];
    }

    /// <summary>Whether to rebalance at the end of this month (month is 0-based since retirement).</summary>
    public bool ShouldRebalance(int month, double[] holdings, double[] targets, bool downMarket)
    {
        // With a cash buffer, never sell shares to rebalance while markets are down
        if (_p.UseCashBuffer && downMarket) return false;
        var endOfYear = month % 12 == 11;
        return _p.Type switch
        {
            InvestmentStrategyType.FixedRebalance => _p.Frequency switch
            {
                RebalanceFrequency.Monthly => true,
                RebalanceFrequency.Quarterly => month % 3 == 2,
                _ => endOfYear,
            },
            InvestmentStrategyType.ThresholdRebalance => Drift(holdings, targets) > _p.Band,
            InvestmentStrategyType.BuyAndHold => false,
            InvestmentStrategyType.WithdrawFromWinner => false,
            _ => endOfYear, // glide paths: annual
        };
    }

    /// <summary>
    /// At the year end in a rising market, tops the cash buffer back up from shares and bonds.
    /// Needed for bases that never rebalance (buy and hold, withdraw from the winner).
    /// </summary>
    public void RefillCash(int month, double[] holdings, double[] targets, bool downMarket)
    {
        if (!_p.UseCashBuffer || downMarket || month % 12 != 11) return;
        var total = holdings.Sum();
        var needed = targets[Cash] * total - holdings[Cash];
        if (needed <= 0) return;
        var shortfall = ProRata(holdings, needed, [Equity, Bond]);
        holdings[Cash] += needed - shortfall;
    }

    /// <summary>Removes <paramref name="amount"/> from holdings. Caller guarantees amount &lt; total.</summary>
    public void Withdraw(double[] holdings, double amount, double[] targets, bool downMarket)
    {
        if (amount <= 0) return;
        if (_p.UseCashBuffer)
        {
            // Falling market: live off the cash so shares aren't sold at a low.
            // Otherwise: spend from shares and bonds and let the year-end refill top the cash back up.
            if (downMarket) amount = TakeFrom(holdings, Cash, amount);
            amount = BaseWithdraw(holdings, amount, targets, [Equity, Bond]);
            TakeFrom(holdings, Cash, amount);
        }
        else
        {
            BaseWithdraw(holdings, amount, targets, [Equity, Bond, Cash]);
        }
    }

    /// <summary>Withdraws from the given assets according to the base strategy; returns any shortfall.</summary>
    double BaseWithdraw(double[] holdings, double amount, double[] targets, int[] assets)
    {
        if (amount <= 0) return 0;
        if (_p.Type != InvestmentStrategyType.WithdrawFromWinner) return ProRata(holdings, amount, assets);

        var total = holdings.Sum();
        foreach (var i in assets.OrderByDescending(i => holdings[i] / total - targets[i]))
        {
            amount = TakeFrom(holdings, i, amount);
            if (amount <= 0) break;
        }
        return amount;
    }

    /// <summary>Adds a deposit according to the target weights.</summary>
    public static void Deposit(double[] holdings, double amount, double[] targets)
    {
        for (var i = 0; i < 3; i++) holdings[i] += amount * targets[i];
    }

    public static void Rebalance(double[] holdings, double[] targets)
    {
        var total = holdings.Sum();
        for (var i = 0; i < 3; i++) holdings[i] = total * targets[i];
    }

    static double Drift(double[] holdings, double[] targets)
    {
        var total = holdings.Sum();
        if (total <= 0) return 0;
        var max = 0.0;
        for (var i = 0; i < 3; i++) max = Math.Max(max, Math.Abs(holdings[i] / total - targets[i]));
        return max;
    }

    static double TakeFrom(double[] holdings, int i, double amount)
    {
        var take = Math.Min(holdings[i], amount);
        holdings[i] -= take;
        return amount - take;
    }

    /// <summary>Takes up to <paramref name="amount"/> from the given assets in proportion to their size; returns any shortfall.</summary>
    static double ProRata(double[] holdings, double amount, int[] assets)
    {
        if (amount <= 0) return 0;
        var total = assets.Sum(i => holdings[i]);
        if (total <= 0) return amount;
        var take = Math.Min(amount, total);
        foreach (var i in assets) holdings[i] -= take * holdings[i] / total;
        return amount - take;
    }

    static double[] Normalise(double[] w)
    {
        var sum = w.Sum();
        return sum > 0 ? w.Select(x => x / sum).ToArray() : [1, 0, 0];
    }
}
