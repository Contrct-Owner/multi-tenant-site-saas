using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Premise.Modules.Entitlements.Data;
using Premise.Platform.Data;
using Premise.Platform.Entitlements;
using Premise.Platform.Kernel;
using Wolverine.EntityFrameworkCore;

namespace Premise.Modules.Entitlements;

public static class EntitlementsModule
{
    public static IServiceCollection AddEntitlementsModule(
        this IServiceCollection services,
        bool runBackgroundWork = false
    )
    {
        services.AddDbContextWithWolverineIntegration<EntitlementsDbContext>(
            (sp, options) =>
            {
                // Options are SINGLETON: never resolve scoped services here (dev
                // scope-validation rejects it, and it would freeze the first
                // request's region). v1 is single-region (ADR 35); multi-region
                // moves connection selection to a per-scope interceptor.
                var regions = sp.GetRequiredService<IRegionDataSources>();
                options
                    .UseNpgsql(
                        regions.For(RegionId.Default),
                        npgsql =>
                            npgsql.MigrationsHistoryTable("__ef_migrations_history", "entitlements")
                    )
                    .AddInterceptors(
                        TenantSessionInterceptor.Instance,
                        sp.GetRequiredService<Premise.Platform.Audit.AuditSaveChangesInterceptor>()
                    );
            }
        );
        // both by TYPE: Wolverine codegen inlines type registrations (no service location)
        services.AddScoped<EntitlementsService>();
        services.AddScoped<IEntitlements, EntitlementsService>();
        services.AddScoped<Premise.Contracts.IOrgDataExporter, EntitlementsExporter>();
        if (runBackgroundWork)
            services.AddHostedService<MeterCompactionService>();
        return services;
    }
}
