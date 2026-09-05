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
        services.AddModuleDbContext<AuditDbContext>("audit", audited: false);
        services.AddScoped<IAuditPolicyProvider, AuditPolicyService>();
        services.AddScoped<Premise.Contracts.IOrgDataExporter, AuditExporter>();
        services.AddScoped<Premise.Contracts.IAuditTrailExporter, AuditTrailExporter>();
        if (runBackgroundWork)
        {
            services.AddHostedService<AuditRetentionService>();
            services.AddHostedService<AuditPartitionMaintenanceService>();
        }
        return services;
    }
}
