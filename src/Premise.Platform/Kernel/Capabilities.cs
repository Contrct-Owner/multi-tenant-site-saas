namespace Premise.Platform.Kernel;

/// <summary>
/// The permission-action catalog (ADR 16): every (domain, action) string the
/// scope resolver evaluates, as constants - codegen emits these as a TS union
/// so capability strings cannot drift between the two languages. Endpoints
/// reference these, never inline strings.
/// </summary>
public static class Capabilities
{
    public const string SitesRead = "sites:read";
    public const string SitesManage = "sites:manage";
    public const string HierarchyManage = "hierarchy:manage";
    public const string FilesRead = "files:read";
    public const string FilesManage = "files:manage";
    public const string IngestManage = "ingest:manage";
    public const string AuditRead = "audit:read";
    public const string AuditManage = "audit:manage";
    public const string EntitlementsManage = "entitlements:manage";
    public const string RolesManage = "roles:manage";

    /// <summary>Platform-operator reach: held only inside the flagged platform org.</summary>
    public const string PlatformOperate = "platform:operate";

    public static readonly IReadOnlyList<string> All =
    [
        SitesRead,
        SitesManage,
        HierarchyManage,
        FilesRead,
        FilesManage,
        IngestManage,
        AuditRead,
        AuditManage,
        EntitlementsManage,
        RolesManage,
        PlatformOperate,
    ];
}
