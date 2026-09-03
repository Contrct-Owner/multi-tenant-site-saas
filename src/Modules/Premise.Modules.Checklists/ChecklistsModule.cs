using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Premise.Modules.Checklists.Data;
using Premise.Platform.Data;
using Premise.Platform.Kernel;
using Wolverine.EntityFrameworkCore;

namespace Premise.Modules.Checklists;

public static class ChecklistsModule
{
    public static IServiceCollection AddChecklistsModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<ChecklistsDbContext>("checklists");
        services.AddScoped<Premise.Contracts.IOrgDataExporter, ChecklistsExporter>();

        return services;
    }
}
