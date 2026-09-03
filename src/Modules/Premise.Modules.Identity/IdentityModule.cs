using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Premise.Modules.Identity.Data;
using Premise.Platform.Data;
using Premise.Platform.Kernel;
using Wolverine.EntityFrameworkCore;

namespace Premise.Modules.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<IdentityDbContext>("identity");
        services.AddScoped<IOperatorContext, Premise.Modules.Identity.Access.OperatorContext>();
        services.AddScoped<Premise.Contracts.IOrgDataExporter, Users.IdentityExporter>();
        services.AddScoped<Premise.Contracts.IActorDirectory, Users.ActorDirectory>();
        return services;
    }
}
