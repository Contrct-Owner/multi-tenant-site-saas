using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Premise.Platform.Kernel;

namespace Premise.Modules.Storage.Data;

/// <summary>Design-time only (dotnet ef). Never used at runtime.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<StorageDbContext>
{
    public StorageDbContext CreateDbContext(string[] args) =>
        new(
            new DbContextOptionsBuilder<StorageDbContext>()
                .UseNpgsql(
                    "Host=localhost;Database=design_time_only",
                    npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "storage")
                )
                .Options,
            new TenantContext()
        );
}
