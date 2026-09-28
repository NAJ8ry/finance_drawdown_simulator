using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using Finance.Engine;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Finance.Api.Services;

public sealed class AskOptions
{
    /// <summary>Anthropic API key. Falls back to the ANTHROPIC_API_KEY environment variable.</summary>
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "claude-opus-5";

    /// <summary>Stop after this many tool-use rounds in one answer.</summary>
    public int MaxToolRounds { get; set; } = 8;
}

public sealed record AskTurn(string Role, string Text);

public sealed record AskRequest(string Question, SimulationInput Input, List<AskTurn>? History);

/// <summary>
/// "Ask Claude" about the current plan. Claude sees the user's inputs and results, and can call
/// <c>run_simulation</c> to test what-if changes against the same market history.
/// </summary>
public sealed class AskService(MarketDataStore store, IOptions<AskOptions> options, IOptions<JsonOptions> json, ILogger<AskService> logger)
{
    readonly AskOptions _o = options.Value;
    JsonSerializerOptions Json => json.Value.JsonSerializerOptions;

    string? ServerKey => string.IsNullOrWhiteSpace(_o.ApiKey) ? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") : _o.ApiKey;

    /// <summary>True when the server has its own key; otherwise each user must supply theirs.</summary>
    public bool Configured => !string.IsNullOrWhiteSpace(ServerKey);
    public string Model => _o.Model;

    /// <summary>
    /// Runs the conversation, reporting progress through <paramref name="emit"/> (event name, payload).
    /// <paramref name="userKey"/> is the visitor's own API key; it is used for this request only and never stored or logged.
    /// </summary>
    public async Task AskAsync(AskRequest request, string? userKey, Func<string, object, Task> emit, CancellationToken ct)
    {
        var key = string.IsNullOrWhiteSpace(userKey) ? ServerKey : userKey.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            await emit("error", new { message = "Add your Anthropic API key to ask Claude." });
            return;
        }

        var history = await store.GetAsync(ct);
        var client = new AnthropicClient { ApiKey = key };

        var baseline = RunSummary(request.Input, "Current plan", history);
        var context = new StringBuilder()
            .AppendLine("<current_plan_inputs>")
            .AppendLine(JsonSerializer.Serialize(request.Input, Json))
            .AppendLine("</current_plan_inputs>")
            .AppendLine("<current_plan_results>")
            .AppendLine(baseline)
            .AppendLine("</current_plan_results>")
            .AppendLine()
            .Append(request.Question.Trim())
            .ToString();

        List<BetaMessageParam> messages = [];
        foreach (var turn in request.History ?? [])
        {
            if (string.IsNullOrWhiteSpace(turn.Text)) continue;
            messages.Add(new BetaMessageParam
            {
                Role = turn.Role == "assistant" ? Role.Assistant : Role.User,
                Content = turn.Text,
            });
        }
        messages.Add(new BetaMessageParam { Role = Role.User, Content = context });

        for (var round = 0; round <= _o.MaxToolRounds; round++)
        {
            await emit("status", new { message = round == 0 ? "Thinking…" : "Reviewing the results…" });

            var response = await client.Beta.Messages.Create(new MessageCreateParams
            {
                Model = _o.Model,
                MaxTokens = 16000,
                Thinking = new BetaThinkingConfigAdaptive(),
                OutputConfig = new BetaOutputConfig { Effort = Effort.Medium },
                // Re-serve on another model if a safety classifier declines (routed by refusal category)
                Betas = ["server-side-fallback-2026-07-01"],
                // This SDK version only types the list form, so pass the "default" scalar as raw JSON
                Fallbacks = new(JsonSerializer.SerializeToElement("default")),
                System = new List<BetaTextBlockParam>
                {
                    new() { Text = SystemPrompt, CacheControl = new BetaCacheControlEphemeral() },
                },
                Tools = [SimulationTool],
                Messages = messages,
            }, ct);

            if (response.StopReason == "refusal")
            {
                await emit("error", new { message = "Claude declined to answer that question." });
                return;
            }

            // After a mid-output fallback, thinking/tool_use blocks before the last fallback block must not be echoed
            var lastFallback = -1;
            for (var i = 0; i < response.Content.Count; i++)
                if (response.Content[i].TryPickFallback(out _)) lastFallback = i;

            var answer = new StringBuilder();
            List<BetaContentBlockParam> assistant = [];
            List<BetaContentBlockParam> results = [];
            for (var i = 0; i < response.Content.Count; i++)
            {
                var block = response.Content[i];
                var beforeFallback = i < lastFallback;
                if (block.TryPickText(out var text))
                {
                    answer.Append(text.Text);
                    assistant.Add(new BetaTextBlockParam { Text = text.Text });
                }
                else if (beforeFallback)
                {
                    continue;
                }
                else if (block.TryPickThinking(out var thinking))
                {
                    assistant.Add(new BetaThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
                }
                else if (block.TryPickRedactedThinking(out var redacted))
                {
                    assistant.Add(new BetaRedactedThinkingBlockParam { Data = redacted.Data });
                }
                else if (block.TryPickToolUse(out var toolUse))
                {
                    assistant.Add(new BetaToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });
                    var (output, isError, label) = ExecuteTool(toolUse.Name, toolUse.Input, request.Input, history);
                    await emit("tool", new { label, error = isError });
                    results.Add(new BetaToolResultBlockParam { ToolUseID = toolUse.ID, Content = output, IsError = isError });
                }
            }

            if (response.StopReason == "tool_use" && results.Count > 0)
            {
                messages.Add(new BetaMessageParam { Role = Role.Assistant, Content = assistant });
                messages.Add(new BetaMessageParam { Role = Role.User, Content = results });
                continue;
            }

            var final = answer.ToString().Trim();
            if (response.StopReason == "max_tokens") final += "\n\n*(The answer was cut short.)*";
            await emit("answer", new { text = final.Length > 0 ? final : "(No answer was returned.)" });
            return;
        }

        await emit("error", new { message = "Stopped after too many simulation rounds. Try a narrower question." });
    }

    internal (string Output, bool IsError, string Label) ExecuteTool(
        string name, IReadOnlyDictionary<string, JsonElement> input, SimulationInput current, IReadOnlyList<MarketMonth> history)
    {
        var label = input.TryGetValue("label", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString()! : "What-if";
        if (name != "run_simulation") return ($"Unknown tool {name}", true, label);
        try
        {
            var merged = JsonNode.Parse(JsonSerializer.Serialize(current, Json))!.AsObject();
            if (input.TryGetValue("changes", out var changes) && changes.ValueKind == JsonValueKind.Object)
                Merge(merged, JsonNode.Parse(changes.GetRawText())!.AsObject());
            var what = merged.Deserialize<SimulationInput>(Json)!;
            var errors = Simulator.Validate(what.Also(x => { x.Spending.Normalise(); x.Investment.Normalise(); }));
            if (errors.Count > 0) return ("Invalid inputs: " + string.Join(" ", errors), true, label);
            return (RunSummary(what, label, history), false, label);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            logger.LogWarning(ex, "run_simulation failed");
            return ($"Could not apply changes: {ex.Message}", true, label);
        }
    }

    /// <summary>Objects merge recursively; arrays and scalars replace.</summary>
    static void Merge(JsonObject target, JsonObject changes)
    {
        foreach (var (key, value) in changes)
        {
            if (value is JsonObject obj && target[key] is JsonObject existing) Merge(existing, obj);
            else target[key] = value?.DeepClone();
        }
    }

    /// <summary>Compact JSON summary of a simulation, all money in today's pounds.</summary>
    string RunSummary(SimulationInput input, string label, IReadOnlyList<MarketMonth> history)
    {
        var r = Simulator.Run(input, history);
        var complete = r.Paths.Where(p => !p.Partial).ToList();
        var years = input.DeathAge - input.RetirementAge;
        var ages = Enumerable.Range(0, years + 1).Select(k => input.RetirementAge + k)
            .Where(a => (a - input.RetirementAge) % 5 == 0 || a == input.DeathAge).ToList();
        var worst = r.Paths.Where(p => p.Failed).OrderBy(p => p.FailAge).FirstOrDefault();

        object Pct(Func<PathResult, double?> pick)
        {
            var v = complete.Select(pick).Where(x => x.HasValue).Select(x => x!.Value).ToArray();
            return v.Length == 0 ? new { } : new
            {
                p10 = Math.Round(Simulator.Percentile(v, 10)),
                median = Math.Round(Simulator.Percentile(v, 50)),
                p90 = Math.Round(Simulator.Percentile(v, 90)),
            };
        }

        var summary = new
        {
            label,
            startDates = input.StartFrequency == StartFrequency.Monthly ? "every month" : "every year (January)",
            dataRange = $"{r.FirstStart} to {r.DataLastMonth}",
            successRatePercent = r.SuccessRate is { } s ? Math.Round(s, 1) : (double?)null,
            completePaths = r.CompleteCount,
            completePathsThatRanOut = r.FailedCount,
            recentPartialPaths = r.PartialCount,
            partialPathsAlreadyRanOut = r.PartialFailedCount,
            earliestRunOut = worst is null ? null : new { age = Math.Round(worst.FailAge ?? 0, 1), retired = worst.Start, partial = worst.Partial },
            balanceAtDeath = Pct(p => p.EndBalance),
            medianAverageSpending = r.MedianAverageSpending is { } m ? Math.Round(m) : (double?)null,
            lowestYearlySpendingAnyPath = r.MinimumSpending is { } lo ? Math.Round(lo) : (double?)null,
            medianWorstFallPercent = r.MedianMaxDrawdown is { } dd ? Math.Round(dd * 100) : (double?)null,
            byAge = ages.Select(a =>
            {
                var k = a - input.RetirementAge;
                return new
                {
                    age = a,
                    balance = Pct(p => k < p.Balances.Length ? p.Balances[k] : null),
                    spending = k < years ? Pct(p => k < p.Spending.Length ? p.Spending[k] : null) : null,
                    takenFromPot = k < years ? Pct(p => k < p.Withdrawals.Length ? p.Withdrawals[k] : null) : null,
                };
            }),
            worstStarts = r.WorstStarts.Take(5).Select(i => r.Paths[i]).Select(p => new
            {
                retired = p.Start,
                ranOutAtAge = p.Failed ? Math.Round(p.FailAge ?? 0, 1) : (double?)null,
                lowestBalance = Math.Round(p.MinBalance),
                balanceAtDeath = p.Partial ? (double?)null : Math.Round(p.EndBalance),
            }),
        };
        return JsonSerializer.Serialize(summary, Json);
    }

    static readonly BetaToolUnion SimulationTool = new BetaTool
    {
        Name = "run_simulation",
        Description = """
            Re-runs the historical backtest with changes applied on top of the user's current plan and returns a summary
            (success rate, earliest run-out, balance and spending percentiles by age, worst start dates). Use it to answer
            any "what if" question with real numbers instead of estimating. Call it several times in parallel to compare options.

            `changes` is a partial SimulationInput that is deep-merged over the current plan: nested objects merge, but arrays
            (flows, oneOffs, spending.rules) REPLACE the current array entirely - so to add a flow, send the full list including
            the existing ones. Rates and percentages are fractions (0.04 = 4%). Money is £ per year in today's money.

            SimulationInput fields: startingBalance, retirementAge, deathAge, inflationRate, feeRate,
            allocation {equity, bond, cash} (fractions summing to 1), spendingFloor (nullable £/yr), legacyTarget (£),
            withdrawalTiming ("Monthly" | "AnnualInAdvance"), startFrequency ("Yearly" | "Monthly"),
            oneOffs [{age, amount (+ spend, - deposit), label}],
            flows [{label, kind ("Income" | "Expense"), startAge, endAge (null = for life), annualAmount, inflationLinked}],
            spending {type ("ConstantInflationAdjusted" | "ConstantPercentage" | "RemainingLife"), initialRate, assumedRealReturn,
              useInflationSkip,
              useGoodBadYear, goodThreshold, badThreshold, raiseStep, cutStep, maxRaise, maxCut,
              useGuytonKlinger, upperGuardrail, lowerGuardrail, guardrailRaise, guardrailCut, freezeAfterLoss,
              useRatchet, ratchetTrigger, ratchetIncrease,
              useFloorCeiling, floor, ceiling,
              useCustomRules, rules [{metric, comparison, threshold, action, value}], applyAllMatches},
            investment {type ("FixedRebalance" | "ThresholdRebalance" | "BuyAndHold" | "DecliningGlidePath" | "RisingGlidePath" | "WithdrawFromWinner"),
              frequency ("Monthly" | "Quarterly" | "Annually"), band, startEquity, endEquity, glideYears, useCashBuffer, bufferYears}.
            """,
        InputSchema = new()
        {
            Properties = new Dictionary<string, JsonElement>
            {
                ["label"] = JsonSerializer.SerializeToElement(new
                {
                    type = "string",
                    description = "Short name for this scenario shown to the user, e.g. \"+£5k/yr from 67\"",
                }),
                ["changes"] = JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    description = "Partial SimulationInput to merge over the current plan. {} re-runs the plan unchanged.",
                }),
            },
            Required = ["label", "changes"],
        },
    };

    const string SystemPrompt = """
        You are the analysis assistant inside a UK retirement drawdown simulator. The user is planning their own
        retirement and asks about their plan and its numbers. Each question arrives with their current inputs and the
        simulator's results for them.

        How the simulator works:
        - It replays the plan from historical start dates (by default every January since 1871; optionally every month)
          through to the age of death, using real monthly market returns in GBP. Success = the pot never runs out
          (and ends at or above the "leave at least" legacy target). Only start dates with enough history to cover the
          whole retirement count towards the success rate; recent "partial" paths are shown but excluded.
        - Everything is in today's money: each month's return is adjusted by that month's actual UK inflation. The
          user's constant planned inflation only affects the nominal display, fixed (non-inflation-linked) incomes, and
          "skip inflation rise" rules.
        - Data: equities are US shares converted to GBP before 2010 (a proxy for global shares) and the MSCI World
          ETF after; bonds are UK gilts; cash is UK T-bills/Bank Rate; inflation is UK CPI. Tax is not modelled.
        - Spending = a base strategy (constant inflation-adjusted, percentage of the pot, or spend-down) plus optional
          adjustments applied each year in order: skip inflation after a loss, good year/bad year raises and cuts
          (based on the last 12 months' portfolio return), Guyton-Klinger guardrails (based on the withdrawal rate
          from the pot), ratchet, custom rules, then floor/ceiling. Pensions and other regular income pay part of
          spending, so the pot provides only the rest; surplus income is reinvested. Regular outgoings and one-offs
          add to what is taken from the pot.
        - Investment = a base (fixed mix with rebalancing, glide paths, etc.) plus an optional cash buffer holding N
          years of withdrawals, spent first when shares are down over the last 12 months.

        Answer with numbers from the simulator. When a question involves a change ("should I spend more at 67",
        "what if I retire later", "is a bigger cash buffer better"), use run_simulation to test sensible alternatives,
        usually several in parallel, and compare them with the current plan. Be concrete about trade-offs: success
        rate, earliest run-out age, spending levels, and what is typically left at death. Point out when a figure is
        driven by a small number of historical start dates.

        Be direct and practical; the user wants a clear view they can act on. Keep answers compact, using a short
        table when comparing options, and write in British English with £. This is a historical backtest, not a
        forecast, and you are not a regulated financial adviser: say so briefly when giving a recommendation, but
        don't let caveats crowd out the answer.
        """;
}

static class ObjectExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
