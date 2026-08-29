using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Premise.Modules.Audit.Data;
using Premise.Platform.Audit;
using Premise.Platform.Data;
using Premise.Platform.Kernel;
using Wolverine.EntityFrameworkCore;

namespace Premise.Modules.Audit;

public static class AuditModule
{
    public static IServiceCollection AddAuditModule(
        this IServiceCollection services,
        bool runBackgroundWork = false
    )
    {
        services.AddDbContextWithWolverineIntegration<AuditDbContext>(
            (sp, options) =>
            {
                var regions = sp.GetRequiredService<IRegionDataSources>();
                var tenant = sp.GetRequiredService<ITenantContext>();
                options
                    .UseNpgsql(
                        regions.For(tenant.Region),
                        npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "audit")
                    )
                    .AddInterceptors(TenantSessionInterceptor.Instance);
            }
        );
        services.AddScoped<IAuditPolicyProvider, AuditPolicyService>();
        if (runBackgroundWork)
            services.AddHostedService<AuditRetentionService>();
        return services;
    }
}
