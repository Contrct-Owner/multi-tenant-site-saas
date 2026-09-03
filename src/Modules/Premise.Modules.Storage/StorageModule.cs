using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Premise.Modules.Storage.Data;
using Premise.Platform.Data;
using Premise.Platform.Kernel;
using Wolverine.EntityFrameworkCore;

namespace Premise.Modules.Storage;

public static class StorageModule
{
    public static IServiceCollection AddStorageModule(
        this IServiceCollection services,
        bool runBackgroundWork = false
    )
    {
        if (runBackgroundWork)
            services.AddHostedService<FileTrashService>();
        services.AddModuleDbContext<StorageDbContext>("storage");
        services.AddScoped<Premise.Contracts.IStoredFileLookup, StoredFileLookup>();
        services.AddScoped<Premise.Contracts.IOrgDataExporter, StorageExporter>();
        return services;
    }
}
