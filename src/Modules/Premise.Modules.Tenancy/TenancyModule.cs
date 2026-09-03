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
        {
            services.AddHostedService<HorizonRollService>();
            services.AddHostedService<Organizations.OrgClosureService>();
        }

        services.AddModuleDbContext<TenancyDbContext>("tenancy");
        services.AddScoped<IOrganizationLookup, OrganizationLookup>();
        // the narrow port PerOrgSweepService resolves: registering the derived
        // interface alone does NOT satisfy the base one, and the sweeps only
        // run in the worker role, so a miss here fails nowhere a test looks
        services.AddScoped<Premise.Platform.Messaging.IOrganizationEnumerator>(sp =>
            sp.GetRequiredService<IOrganizationLookup>()
        );
        services.AddScoped<ISiteLookup, SiteLookup>();
        services.AddScoped<ISiteDirectory, Sites.SiteDirectory>();
        services.AddScoped<IEntitlementUsageProbe, MaxSitesProbe>();
        services.AddScoped<IEntitlementUsageProbe, HierarchyDepthProbe>();
        services.AddScoped<IOrgDataExporter, Organizations.TenancyExporter>();
        return services;
    }
}
