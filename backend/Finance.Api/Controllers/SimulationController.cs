using Finance.Api.Services;
using Finance.Engine;
using Microsoft.AspNetCore.Mvc;

namespace Finance.Api.Controllers;

[ApiController]
[Route("api/simulate")]
public class SimulationController(MarketDataStore store) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<SimulationResult>> Simulate(SimulationInput input, CancellationToken ct)
    {
        var errors = Simulator.Validate(input);
        if (errors.Count > 0) return ValidationProblem(new ValidationProblemDetails { Detail = string.Join(" ", errors) });

        var history = await store.GetAsync(ct);
        if (history.Count < Simulator.MinimumMonths)
            return Problem("No market data is loaded yet.", statusCode: StatusCodes.Status503ServiceUnavailable);

        var result = Simulator.Run(input, history);
        RoundForTransport(result);
        return result;
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
