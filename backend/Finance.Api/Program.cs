using System.Text.Json.Serialization;
using Finance.Api.Data;
using Finance.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// REMEODY_CONNECTION_STRING takes precedence over appsettings
var connectionString = Environment.GetEnvironmentVariable("REMEODY_CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("Finance")
    ?? throw new InvalidOperationException("Set REMEODY_CONNECTION_STRING or ConnectionStrings:Finance.");

builder.Services.AddDbContext<FinanceDbContext>(o => o.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
});
builder.Services.AddProblemDetails();
builder.Services.AddResponseCompression(o => o.EnableForHttps = true);

builder.Services.Configure<MarketDataOptions>(builder.Configuration.GetSection("MarketData"));
builder.Services.AddSingleton<MarketDataStore>();
builder.Services.AddHttpClient<LiveMarketDataSource>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(60);
    // Yahoo and the Bank of England reject requests without a browser-like user agent
    c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (X11; Linux x86_64) FinanceSimulator/1.0");
});
builder.Services.AddSingleton<MarketDataUpdater>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MarketDataUpdater>());

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
        .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
    await db.Database.MigrateAsync();
    await HistorySeeder.SeedAsync(db, Path.Combine(AppContext.BaseDirectory, "SeedData", "history.csv"), app.Logger);
}

app.UseExceptionHandler();
app.UseResponseCompression();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
