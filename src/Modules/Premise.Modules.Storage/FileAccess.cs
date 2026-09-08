using Premise.Contracts;
using Premise.Modules.Storage.Data;
using Premise.Platform.Kernel;
using Premise.Platform.Storage;

namespace Premise.Modules.Storage;

public sealed class FileAccess(
    IScopeResolver scopes,
    IReportSiteSource sites,
    IEnumerable<IFileOriginAccess> origins
)
{
    public async Task<bool> AllowsAsync(
        FileObject file,
        Principal principal,
        string capability,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(principal, capability, ct))
            return false;
        // Legacy archives predate origin metadata. Their reserved names remain protected
        // until a migration/backfill can distinguish them from ordinary uploads.
        var source = file.Origin switch
        {
            "org-export" => Capabilities.OrgManage,
            "audit-export" => Capabilities.AuditRead,
            null when file.Name.StartsWith("org-export-", StringComparison.Ordinal) =>
                Capabilities.OrgManage,
            null when file.Name.StartsWith("audit-export-", StringComparison.Ordinal) =>
                Capabilities.AuditRead,
            _ => null,
        };
        if (source is not null)
            return principal is Principal.User { ActiveOrg: { } org }
                && org == file.OrgId
                && await scopes.ScopeForAsync(principal, source, ct) is NodeScope.EntireOrg;
        if (file.SiteIds.Length > 0)
        {
            var siteScope = await scopes.ScopeForAsync(principal, Capabilities.SitesRead, ct);
            var fileScope = await scopes.ScopeForAsync(principal, capability, ct);
            var readable = await sites.SelectAsync(
                file.OrgId,
                siteScope,
                file.SiteIds,
                file.SiteIds.Length,
                ct
            );
            var allowed = await sites.SelectAsync(
                file.OrgId,
                fileScope,
                file.SiteIds,
                file.SiteIds.Length,
                ct
            );
            if (readable.Count != file.SiteIds.Length || allowed.Count != file.SiteIds.Length)
                return false;
        }
        // Managing the file does not expose its bytes. A removed source resource
        // or retired renderer must not prevent an authorized site/file manager
        // from placing a hold, trashing, or restoring the persistent file.
        if (file.Origin is null || capability == Capabilities.FilesManage)
            return true;
        if (principal is not Principal.User user || user.ActiveOrg != file.OrgId)
            return false;
        var policy = origins.SingleOrDefault(x => x.Origin == file.Origin);
        return policy is not null
            && await policy.CanReadAsync(file.OrgId, file.Id, user.UserId, ct);
    }
}
