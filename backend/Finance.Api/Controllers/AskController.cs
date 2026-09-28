using System.Text.Json;
using Finance.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Finance.Api.Controllers;

[ApiController]
[Route("api/ask")]
public class AskController(AskService ask, IOptions<JsonOptions> json, ILogger<AskController> logger) : ControllerBase
{
    [HttpGet("status")]
    public object Status() => new { serverKey = ask.Configured, model = ask.Model };

    /// <summary>Streams server-sent events: status, tool, answer, error, done.</summary>
    [HttpPost]
    public async Task Ask(AskRequest request, CancellationToken ct)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        async Task Emit(string name, object payload)
        {
            await Response.WriteAsync($"event: {name}\ndata: {JsonSerializer.Serialize(payload, json.Value.JsonSerializerOptions)}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            await Emit("error", new { message = "Please type a question." });
            return;
        }

        try
        {
            // The visitor's own key, if they supplied one; used for this call only and never logged
            var userKey = Request.Headers["X-Anthropic-Key"].FirstOrDefault();
            await ask.AskAsync(request, userKey, Emit, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Anthropic.Exceptions.AnthropicUnauthorizedException)
        {
            await Emit("error", new { message = "The API key was rejected. Check it and try again.", badKey = true });
        }
        catch (Anthropic.Exceptions.AnthropicRateLimitException)
        {
            await Emit("error", new { message = "Rate limited by the Claude API. Wait a moment and try again." });
        }
        catch (Anthropic.Exceptions.AnthropicApiException ex) when (ex.Message.Contains("credit balance", StringComparison.OrdinalIgnoreCase))
        {
            await Emit("error", new
            {
                message = "Your Anthropic API account has no credit. API usage is billed separately from a Claude subscription: add credit under Settings → Billing at console.anthropic.com, then ask again.",
            });
        }
        catch (Anthropic.Exceptions.AnthropicApiException ex)
        {
            logger.LogWarning("Claude API error {Type}", ex.GetType().Name);
            await Emit("error", new { message = $"Claude API error: {ex.Message}" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ask failed");
            await Emit("error", new { message = "Something went wrong answering that question." });
        }
        await Emit("done", new { });
    }
}
