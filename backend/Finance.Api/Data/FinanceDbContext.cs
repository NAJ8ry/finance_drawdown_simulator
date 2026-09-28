using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Data;

/// <summary>One month of market data. Returns are nominal GBP monthly returns.</summary>
public class MarketMonthRow
{
    public DateOnly Month { get; set; }
    public double Equity { get; set; }
    public double Bond { get; set; }
    public double Cash { get; set; }
    public double Inflation { get; set; }

    /// <summary>"history" (seed file) or "live" (fetched by the updater).</summary>
    public string Source { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}

public class DataUpdateLog
{
    public long Id { get; set; }
    public DateTime RunAt { get; set; }
    public bool Success { get; set; }
    public int MonthsWritten { get; set; }
    public string? LastMonth { get; set; }
    public string Message { get; set; } = "";
}

public class Scenario
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Serialised <see cref="Finance.Engine.SimulationInput"/>.</summary>
    public string InputJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class FinanceDbContext(DbContextOptions<FinanceDbContext> options) : DbContext(options)
{
    public DbSet<MarketMonthRow> MarketMonths => Set<MarketMonthRow>();
    public DbSet<DataUpdateLog> DataUpdateLogs => Set<DataUpdateLog>();
    public DbSet<Scenario> Scenarios => Set<Scenario>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<MarketMonthRow>(e =>
        {
            e.ToTable("market_months");
            e.HasKey(x => x.Month);
            e.Property(x => x.Source).HasMaxLength(16);
        });
        b.Entity<DataUpdateLog>(e =>
        {
            e.ToTable("data_update_logs");
            e.Property(x => x.LastMonth).HasMaxLength(7);
            e.HasIndex(x => x.RunAt);
        });
        b.Entity<Scenario>(e =>
        {
            e.ToTable("scenarios");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.InputJson).HasColumnType("jsonb");
        });
    }
}
