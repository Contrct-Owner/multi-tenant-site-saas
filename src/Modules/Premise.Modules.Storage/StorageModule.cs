using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Premise.Modules.Storage.Data;
using Premise.Platform.Data;
using Premise.Platform.Kernel;
using Wolverine.EntityFrameworkCore;

namespace Premise.Modules.Storage;

public static class StorageModule
{
    public static IServiceCollection AddStorageModule(this IServiceCollection services)
    {
        services.AddDbContextWithWolverineIntegration<StorageDbContext>(
            (sp, options) =>
            {
                var regions = sp.GetRequiredService<IRegionDataSources>();
                var tenant = sp.GetRequiredService<ITenantContext>();
                options
                    .UseNpgsql(
                        regions.For(tenant.Region),
                        npgsql =>
                            npgsql.MigrationsHistoryTable("__ef_migrations_history", "storage")
                    )
                    .AddInterceptors(
                        TenantSessionInterceptor.Instance,
                        sp.GetRequiredService<Premise.Platform.Audit.AuditSaveChangesInterceptor>()
                    );
            }
        );
        services.AddScoped<Premise.Contracts.IStoredFileLookup, StoredFileLookup>();
        return services;
    }
}
