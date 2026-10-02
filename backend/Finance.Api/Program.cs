using Finance.Engine;
using System.Text.Json.Serialization;
using Finance.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
});
builder.Services.AddProblemDetails();
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    // Server-sent events must not be buffered by compression
    o.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes.Where(m => m != "text/event-stream");
});

builder.Services.Configure<MarketDataOptions>(builder.Configuration.GetSection("MarketData"));
builder.Services.AddSingleton<MarketDataStore>();
// UK life tables (ONS, Open Government Licence) for lifespan-weighted results
builder.Services.AddSingleton(Mortality.Load(Path.Combine(AppContext.BaseDirectory, "SeedData", "mortality_uk.csv")));
builder.Services.AddHttpClient<LiveMarketDataSource>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(60);
    // Yahoo and the Bank of England reject requests without a browser-like user agent
    c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (X11; Linux x86_64) FinanceSimulator/1.0");
});
builder.Services.AddSingleton<MarketDataUpdater>();
builder.Services.Configure<AskOptions>(builder.Configuration.GetSection("Anthropic"));
builder.Services.AddSingleton<AskService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MarketDataUpdater>());
// For Azure App Service's health check (Monitoring > Health check, path /api/health)
builder.Services.AddHealthChecks();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
        .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Load the market history now, so a missing or broken seed file stops the start rather than the first request
app.Services.GetRequiredService<MarketDataStore>();

app.UseExceptionHandler();
app.UseResponseCompression();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapHealthChecks("/api/health");
// Unknown API routes are a 404, not the single-page app
app.Map("/api/{**rest}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
