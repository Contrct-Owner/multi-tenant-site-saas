using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Premise.Modules.Ingest.Data;
using Premise.Platform.Data;
using Premise.Platform.Kernel;
using Wolverine.EntityFrameworkCore;

namespace Premise.Modules.Ingest;

public static class IngestModule
{
    public static IServiceCollection AddIngestModule(
        this IServiceCollection services,
        bool runBackgroundWork = false
    )
    {
        if (runBackgroundWork)
            services.AddHostedService<ConnectorScheduleService>();

        services.AddModuleDbContext<IngestDbContext>("ingest");
        services.AddScoped<StagingService>();
        services.AddScoped<Premise.Contracts.IOrgDataExporter, IngestExporter>();
        services.AddHttpClient("ingest-connector");
        return services;
    }
}
