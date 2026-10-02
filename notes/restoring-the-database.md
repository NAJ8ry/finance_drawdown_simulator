# Restoring the database

The proof of concept runs without a database to keep hosting costs down. PostgreSQL was removed after commit
`8327c4c`; everything listed here can be recovered from that commit (`git show 8327c4c:<path>`).

## What changed

| Before | Now |
|---|---|
| `market_months` table, seeded from `SeedData/history.csv` and updated by `MarketDataUpdater` | `MarketDataStore` holds the months in memory. Live months are kept in a snapshot file between restarts (`MarketData:SnapshotPath`) |
| `data_update_logs` table | The last 20 runs since the API started, in memory in `MarketDataUpdater` |
| `scenarios` table behind `/api/scenarios` | The visitor's browser (`localStorage`), in `frontend/src/api.ts` |

## Removed files and settings

- `backend/Finance.Api/Data/FinanceDbContext.cs`
- `backend/Finance.Api/Migrations/`
- `backend/Finance.Api/Controllers/ScenariosController.cs`
- `backend/Finance.Api/appsettings.Development.example.json` (it held only the connection string)
- Packages `Npgsql.EntityFrameworkCore.PostgreSQL`, `EFCore.NamingConventions`, `Microsoft.EntityFrameworkCore.Design`
- The connection string (`REMEODY_CONNECTION_STRING` or `ConnectionStrings:Finance`), `AddDbContext` and the
  migrate-and-seed block in `Program.cs`

Market data does not need to go back into the database. Saved scenarios do, once they belong to a user.

## Health check

`/api/health` now only reports that the API is running. This database check was never committed, so it is kept here:

```csharp
/// <summary>Healthy when the database answers.</summary>
public sealed class DatabaseHealthCheck(FinanceDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(ct)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Cannot connect to the database.");
        }
        catch (Exception e)
        {
            return HealthCheckResult.Unhealthy("Cannot connect to the database.", e);
        }
    }
}
```

Register it with `builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");`.
