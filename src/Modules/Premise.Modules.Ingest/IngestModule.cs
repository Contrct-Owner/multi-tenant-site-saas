using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Premise.Modules.Ingest.Data;
using Premise.Platform.Data;
using Premise.Platform.Http;
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
        services
            .AddHttpClient("ingest-connector", client => client.Timeout = TimeSpan.FromSeconds(15))
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
