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
                var regions = sp.GetRequiredService<IRegionDataSources>();
                var tenant = sp.GetRequiredService<ITenantContext>();
                options
                    .UseNpgsql(
                        regions.For(tenant.Region),
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
        if (runBackgroundWork)
            services.AddHostedService<MeterCompactionService>();
        return services;
    }
}
