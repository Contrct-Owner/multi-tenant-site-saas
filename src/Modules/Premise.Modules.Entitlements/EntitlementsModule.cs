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
        services.AddModuleDbContext<EntitlementsDbContext>("entitlements");
        // both by TYPE: Wolverine codegen inlines type registrations (no service location)
        services.AddScoped<EntitlementsService>();
        services.AddScoped<IEntitlements, EntitlementsService>();
        services.AddScoped<Premise.Contracts.IOrgDataExporter, EntitlementsExporter>();
        if (runBackgroundWork)
            services.AddHostedService<MeterCompactionService>();
        return services;
    }
}
