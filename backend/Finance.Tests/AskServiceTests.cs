using System.Text.Json;
using System.Text.Json.Serialization;
using Finance.Api.Services;
using Finance.Engine;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Finance.Tests;

public class AskServiceTests
{
    static AskService Service()
    {
        var json = new JsonOptions();
        json.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        json.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        var mortality = Mortality.Load(Path.Combine(AppContext.BaseDirectory, "SeedData", "mortality_uk.csv"));
        return new AskService(null!, mortality, Options.Create(new AskOptions()), Options.Create(json), NullLogger<AskService>.Instance);
    }

    static IReadOnlyDictionary<string, JsonElement> ToolInput(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    static List<MarketMonth> History() =>
        Enumerable.Range(0, 900).Select(i => new MarketMonth(1900 + i / 12, i % 12 + 1, 0.005, 0.002, 0.001, 0.002)).ToList();

    [Fact]
    public void Run_simulation_merges_changes_over_the_current_plan()
    {
        var current = new SimulationInput { Spending = { InitialRate = 0.2 } }; // fails quickly
        var (baseline, _, _) = Service().ExecuteTool("run_simulation", ToolInput("""{"label":"base","changes":{}}"""), current, History());
        var (withPension, isError, label) = Service().ExecuteTool("run_simulation", ToolInput("""
            {"label":"+pension","changes":{"flows":[{"label":"Pension","kind":"Income","startAge":60,"endAge":null,"annualAmount":20000,"inflationLinked":true}]}}
            """), current, History());

        Assert.False(isError);
        Assert.Equal("+pension", label);
        Assert.Equal(0, JsonDocument.Parse(baseline).RootElement.GetProperty("successRatePercent").GetDouble());
        Assert.Equal(100, JsonDocument.Parse(withPension).RootElement.GetProperty("successRatePercent").GetDouble());
        Assert.Equal(0.2, current.Spending.InitialRate); // the user's plan is not modified
    }

    [Fact]
    public void Run_simulation_merges_nested_objects_and_reports_invalid_inputs()
    {
        var current = new SimulationInput();
        var (ok, okError, _) = Service().ExecuteTool("run_simulation",
            ToolInput("""{"label":"gk","changes":{"spending":{"useGuytonKlinger":true}}}"""), current, History());
        Assert.False(okError);
        Assert.Contains("successRatePercent", ok);

        var (bad, badError, _) = Service().ExecuteTool("run_simulation",
            ToolInput("""{"label":"bad","changes":{"allocation":{"equity":0.9}}}"""), current, History());
        Assert.True(badError);
        Assert.Contains("100%", bad);
    }
}
