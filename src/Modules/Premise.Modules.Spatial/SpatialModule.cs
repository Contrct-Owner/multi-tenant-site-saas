using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Premise.Modules.Spatial.Data;
using Premise.Platform.Data;
using Premise.Platform.Kernel;
using Wolverine.EntityFrameworkCore;

namespace Premise.Modules.Spatial;

public static class SpatialModule
{
    public static IServiceCollection AddSpatialModule(this IServiceCollection services)
    {
        // persistence is built in ONE place (ModulePersistence, ADR 35): the
        // schema is the only fact a module supplies
        services.AddModuleDbContext<SpatialDbContext>("spatial");
        services.AddScoped<Premise.Contracts.IOrgDataExporter, SpatialExporter>();
        return services;
    }
}
