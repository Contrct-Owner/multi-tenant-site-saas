using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Premise.Platform.Kernel;

namespace Premise.Platform.Infra;

/// <summary>Design-time only (dotnet ef). Never used at runtime.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args) =>
        new(
            new DbContextOptionsBuilder<PlatformDbContext>()
                .UseNpgsql(
                    "Host=localhost;Database=design_time_only",
                    npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "platform")
                )
                .Options,
            new TenantContext()
        );
}
