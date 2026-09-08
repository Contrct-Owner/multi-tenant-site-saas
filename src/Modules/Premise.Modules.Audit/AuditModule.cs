using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Premise.Modules.Audit.Data;
using Premise.Platform.Audit;
using Premise.Platform.Data;
using Premise.Platform.Http;
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
        services
            .AddHttpClient("webhook-delivery", client => client.Timeout = TimeSpan.FromSeconds(15))
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var environment = sp.GetRequiredService<IHostEnvironment>();
                return PublicHttp.CreateHandler(
                    environment.IsDevelopment() || environment.IsEnvironment("Testing")
                );
            });
        return services;
    }
}
