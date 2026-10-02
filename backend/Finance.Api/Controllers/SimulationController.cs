using Finance.Api.Services;
using Finance.Engine;
using Microsoft.AspNetCore.Mvc;

namespace Finance.Api.Controllers;

[ApiController]
[Route("api/simulate")]
public class SimulationController(MarketDataStore store, Mortality mortality) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<SimulationResult>> Simulate(SimulationInput input, CancellationToken ct)
    {
        var errors = Simulator.Validate(input);
        if (errors.Count > 0) return ValidationProblem(new ValidationProblemDetails { Detail = string.Join(" ", errors) });

        var history = store.Months;
        if (history.Count < Simulator.MinimumMonths)
            return Problem("No market data is loaded yet.", statusCode: StatusCodes.Status503ServiceUnavailable);

        var result = Simulator.Run(input, history, mortality);
        RoundForTransport(result);
        return result;
    }

    /// <summary>Either a minimum success rate, or (with life tables) a maximum chance of running out while alive.</summary>
    public sealed record FitRequest(SimulationInput Input, string Pattern, double TargetSuccess, double? MaxLifetimeRuin = null);

    public sealed record FitResponse(
        bool Feasible,
        SpendingParameters Spending,
        double? SuccessRate,
        double? MedianEndBalance,
        double? P10EndBalance,
        double? CutRate,
        double? LifetimeRuinRate,
        List<FitYear> Years);

    /// <summary>One year of the fitted plan: what comes from the pot, and total spending with other income.</summary>
    public sealed record FitYear(int Age, double FromPot, double Spending);

    public sealed record PatternInfo(string Id, string Label, string Description, string? Source);

    [HttpGet("patterns")]
    public IEnumerable<PatternInfo> Patterns() => SpendingPatterns.All.Select(p => new PatternInfo(p.Id, p.Label, p.Description, p.Source));

    /// <summary>
    /// Finds the highest fixed-amounts schedule that follows a spending pattern and succeeds in at least
    /// <see cref="FitRequest.TargetSuccess"/>% of historical start dates. The user's plan is not changed.
    /// </summary>
    [HttpPost("fit")]
    public async Task<ActionResult<FitResponse>> Fit(FitRequest request, CancellationToken ct)
    {
        var errors = Simulator.Validate(request.Input);
        if (errors.Count > 0) return ValidationProblem(new ValidationProblemDetails { Detail = string.Join(" ", errors) });
        if (SpendingPatterns.Find(request.Pattern) is not { } pattern) return ValidationProblem(new ValidationProblemDetails { Detail = "Unknown spending pattern." });
        if (request.MaxLifetimeRuin is { } ruin)
        {
            if (ruin is < 0 or > 50) return ValidationProblem(new ValidationProblemDetails { Detail = "The chance of running out must be between 0% and 50%." });
            if (request.Input.LifeTable == LifeTable.None) return ValidationProblem(new ValidationProblemDetails { Detail = "Choose UK life tables to use a lifetime target." });
        }
        else if (request.TargetSuccess is < 50 or > 100) return ValidationProblem(new ValidationProblemDetails { Detail = "Target success must be between 50% and 100%." });

        var history = store.Months;
        if (history.Count < Simulator.MinimumMonths)
            return Problem("No market data is loaded yet.", statusCode: StatusCodes.Status503ServiceUnavailable);

        var input = request.Input;
        var fit = SpendingFitter.Fit(input, history, age => pattern.At(input.RetirementAge, age), request.TargetSuccess,
            mortality, null, request.MaxLifetimeRuin);
        var (income, _, _) = Simulator.FlowsByYear(fit.Plan);
        var years = Enumerable.Range(0, income.Length).Select(y =>
        {
            var fromPot = fit.Plan.Spending.AmountAt(input.RetirementAge + y);
            return new FitYear(input.RetirementAge + y, fromPot, fromPot + income[y]);
        }).ToList();
        var r = fit.Result;
        return new FitResponse(fit.Feasible, fit.Plan.Spending, r.SuccessRate, r.MedianEndBalance, r.P10EndBalance, r.CutRate, r.LifetimeRuinRate, years);
    }

    /// <summary>Whole pounds are plenty for charts and keep the response small.</summary>
    static void RoundForTransport(SimulationResult r)
    {
        foreach (var p in r.Paths)
        {
            for (var i = 0; i < p.Balances.Length; i++) p.Balances[i] = Math.Round(p.Balances[i]);
            for (var i = 0; i < p.Spending.Length; i++) p.Spending[i] = Math.Round(p.Spending[i]);
            for (var i = 0; i < p.Withdrawals.Length; i++) p.Withdrawals[i] = Math.Round(p.Withdrawals[i]);
            p.EndBalance = Math.Round(p.EndBalance);
            p.MinBalance = Math.Round(p.MinBalance);
            p.MaxDrawdown = Math.Round(p.MaxDrawdown, 4);
            if (p.FailAge is { } age) p.FailAge = Math.Round(age, 2);
        }
    }
}
