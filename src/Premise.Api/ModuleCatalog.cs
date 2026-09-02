using Premise.Platform.Modules;

namespace Premise.Api;

/// <summary>
/// The single list of modules. The composition root is the one place allowed
/// to know every module (Platform must not reference them - that would invert
/// the dependency the architecture tests enforce), so the catalog lives here
/// and everything that needs to enumerate modules reads it: MigrationRunner
/// migrates and grants from it, and the test suites derive their module lists
/// from it rather than keeping their own copies.
///
/// Adding a module means adding ONE line here. An architecture test asserts
/// every Premise.Modules.* assembly appears, so a half-registered module
/// fails the build instead of silently skipping migrations and exports.
/// </summary>
public static class ModuleCatalog
{
    public static readonly IReadOnlyList<ModuleDescriptor> All =
    [
        new("tenancy", "tenancy", typeof(Premise.Modules.Tenancy.Data.TenancyDbContext)),
        new("identity", "identity", typeof(Premise.Modules.Identity.Data.IdentityDbContext)),
        new(
            "entitlements",
            "entitlements",
            typeof(Premise.Modules.Entitlements.Data.EntitlementsDbContext)
        ),
        new("audit", "audit", typeof(Premise.Modules.Audit.Data.AuditDbContext)),
        new("storage", "storage", typeof(Premise.Modules.Storage.Data.StorageDbContext)),
        new("ingest", "ingest", typeof(Premise.Modules.Ingest.Data.IngestDbContext)),
        new(
            "checklists",
            "checklists",
            typeof(Premise.Modules.Checklists.Data.ChecklistsDbContext)
        ),
    ];

    /// <summary>
    /// Platform's own schema rides alongside the modules wherever schemas are
    /// enumerated (grants, RLS coverage, round-trips) but is not a module.
    /// </summary>
    public static readonly ModuleDescriptor Platform = new(
        "platform",
        "platform",
        typeof(Premise.Platform.Infra.PlatformDbContext)
    );

    /// <summary>Every migratable context, modules plus Platform.</summary>
    public static IEnumerable<ModuleDescriptor> AllWithPlatform => All.Append(Platform);

    public static IEnumerable<string> Schemas => AllWithPlatform.Select(m => m.Schema);
}
