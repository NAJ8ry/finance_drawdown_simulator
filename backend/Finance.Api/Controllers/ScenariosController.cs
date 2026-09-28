using System.Text.Json;
using Finance.Api.Data;
using Finance.Engine;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Finance.Api.Controllers;

[ApiController]
[Route("api/scenarios")]
public class ScenariosController(FinanceDbContext db, IOptions<JsonOptions> json) : ControllerBase
{
    public sealed record ScenarioDto(Guid Id, string Name, SimulationInput Input, DateTime CreatedAt, DateTime UpdatedAt);

    public sealed record SaveScenario(string Name, SimulationInput Input);

    JsonSerializerOptions Options => json.Value.JsonSerializerOptions;

    ScenarioDto ToDto(Scenario s) => new(s.Id, s.Name,
        JsonSerializer.Deserialize<SimulationInput>(s.InputJson, Options) ?? new SimulationInput(), s.CreatedAt, s.UpdatedAt);

    [HttpGet]
    public async Task<List<ScenarioDto>> List(CancellationToken ct) =>
        (await db.Scenarios.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct)).Select(ToDto).ToList();

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ScenarioDto>> Get(Guid id, CancellationToken ct) =>
        await db.Scenarios.FindAsync([id], ct) is { } s ? ToDto(s) : NotFound();

    [HttpPost]
    public async Task<ActionResult<ScenarioDto>> Create(SaveScenario body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Name)) return ValidationProblem(new ValidationProblemDetails { Detail = "Name is required." });
        var now = DateTime.UtcNow;
        var s = new Scenario
        {
            Id = Guid.NewGuid(),
            Name = body.Name.Trim(),
            InputJson = JsonSerializer.Serialize(body.Input, Options),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Scenarios.Add(s);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = s.Id }, ToDto(s));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ScenarioDto>> Update(Guid id, SaveScenario body, CancellationToken ct)
    {
        if (await db.Scenarios.FindAsync([id], ct) is not { } s) return NotFound();
        if (string.IsNullOrWhiteSpace(body.Name)) return ValidationProblem(new ValidationProblemDetails { Detail = "Name is required." });
        s.Name = body.Name.Trim();
        s.InputJson = JsonSerializer.Serialize(body.Input, Options);
        s.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToDto(s);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await db.Scenarios.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? NotFound() : NoContent();
    }
}
