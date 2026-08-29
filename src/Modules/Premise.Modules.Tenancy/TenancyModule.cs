using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Premise.Contracts;
using Premise.Modules.Tenancy.Data;
using Premise.Modules.Tenancy.Organizations;
using Premise.Modules.Tenancy.Sites;
using Premise.Platform.Data;
using Premise.Platform.Kernel;
using Wolverine.EntityFrameworkCore;

namespace Premise.Modules.Tenancy;

public static class TenancyModule
{
    /// <summary>
    /// Module registration: DbContext resolves its connection through the
    /// region resolver per scope - no ambient connection string (ADR 35).
    /// </summary>
    public static IServiceCollection AddTenancyModule(
        this IServiceCollection services,
        bool runBackgroundWork = false
    )
    {
        if (runBackgroundWork)
            services.AddHostedService<HorizonRollService>();

        services.AddDbContextWithWolverineIntegration<TenancyDbContext>(
            (sp, options) =>
            {
                var regions = sp.GetRequiredService<IRegionDataSources>();
                var tenant = sp.GetRequiredService<ITenantContext>();
                options
                    .UseNpgsql(
                        regions.For(tenant.Region),
                        npgsql =>
                            npgsql.MigrationsHistoryTable("__ef_migrations_history", "tenancy")
                    )
                    .AddInterceptors(
                        TenantSessionInterceptor.Instance,
                        sp.GetRequiredService<Premise.Platform.Audit.AuditSaveChangesInterceptor>()
                    );
            }
        );
        services.AddScoped<IOrganizationLookup, OrganizationLookup>();
        services.AddScoped<IEntitlementUsageProbe, MaxSitesProbe>();
        services.AddScoped<IEntitlementUsageProbe, HierarchyDepthProbe>();
        return services;
    }
}
