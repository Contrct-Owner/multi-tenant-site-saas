using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Premise.Modules.Ingest.Data;
using Premise.Platform.Data;
using Premise.Platform.Kernel;
using Wolverine.EntityFrameworkCore;

namespace Premise.Modules.Ingest;

public static class IngestModule
{
    public static IServiceCollection AddIngestModule(this IServiceCollection services)
    {
        services.AddDbContextWithWolverineIntegration<IngestDbContext>(
            (sp, options) =>
            {
                var regions = sp.GetRequiredService<IRegionDataSources>();
                var tenant = sp.GetRequiredService<ITenantContext>();
                options
                    .UseNpgsql(
                        regions.For(tenant.Region),
                        npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "ingest")
                    )
                    .AddInterceptors(
                        TenantSessionInterceptor.Instance,
                        sp.GetRequiredService<Premise.Platform.Audit.AuditSaveChangesInterceptor>()
                    );
            }
        );
        services.AddScoped<StagingService>();
        services.AddHttpClient("ingest-connector");
        return services;
    }
}
