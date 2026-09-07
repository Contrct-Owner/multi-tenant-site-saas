using System.Text.Json;
using Premise.Contracts;
using Premise.Modules.Reporting.Data;
using Premise.Platform.Kernel;

namespace Premise.Modules.Reporting;

public sealed class ReportAccess(
    IReportRequester requesters,
    IScopeResolver scopes,
    IReportSiteSource sites
)
{
    public async Task<bool> CanReadSitesAsync(
        OrgId org,
        Guid userId,
        Guid[] ids,
        CancellationToken ct
    )
    {
        var user = await requesters.GetActiveAsync(org, userId, ct);
        if (user is null || ids.Length == 0)
            return false;
        var scope = Intersect(
            await scopes.ScopeForAsync(user, Capabilities.SitesRead, ct),
            await scopes.ScopeForAsync(user, Capabilities.FilesRead, ct)
        );
        return (await sites.SelectAsync(org, scope, ids, ids.Length, ct)).Count == ids.Length;
    }

    public async Task<bool> CanReadJobAsync(
        ReportJob job,
        Guid userId,
        ReportRegistry registry,
        CancellationToken ct
    )
    {
        if (job.State == "Purging" || !await CanReadSitesAsync(job.OrgId, userId, job.SiteIds, ct))
            return false;
        var definition = registry.Find(job.ReportType, job.DefinitionVersion);
        if (definition is null)
            return false;
        foreach (var item in job.Items)
            if (
                !await definition.AuthorizeAsync(
                    Request(job, item.SiteIds) with
                    {
                        UserId = userId,
                    },
                    item.State == "Succeeded"
                        ? JsonSerializer.Deserialize<JsonElement>(item.DependenciesJson)
                        : null,
                    ct
                )
            )
                return false;
        return true;
    }

    public async Task<bool> CanGenerateAsync(OrgId org, Guid userId, CancellationToken ct)
    {
        var user = await requesters.GetActiveAsync(org, userId, ct);
        return user is not null && await scopes.CanAsync(user, Capabilities.ReportsGenerate, ct);
    }

    public async Task<bool> CanAccessSitesAsync(
        OrgId org,
        Guid userId,
        Guid[] ids,
        CancellationToken ct
    )
    {
        var user = await requesters.GetActiveAsync(org, userId, ct);
        if (user is null || ids.Length == 0)
            return false;
        var scope = Intersect(
            await scopes.ScopeForAsync(user, Capabilities.SitesRead, ct),
            await scopes.ScopeForAsync(user, Capabilities.ReportsGenerate, ct)
        );
        var found = await sites.SelectAsync(org, scope, ids, ids.Length, ct);
        return found.Count == ids.Length;
    }

    public static NodeScope Intersect(NodeScope left, NodeScope right)
    {
        if (left is NodeScope.None || right is NodeScope.None)
            return NodeScope.Nothing;
        if (left is NodeScope.EntireOrg)
            return right;
        if (right is NodeScope.EntireOrg)
            return left;
        var a = (NodeScope.Subtrees)left;
        var b = (NodeScope.Subtrees)right;
        if (a.Org != b.Org)
            return NodeScope.Nothing;
        return new NodeScope.Subtrees(
            a.Org,
            a.Paths.Concat(b.Paths).Distinct().Where(p => a.Covers(p) && b.Covers(p)).ToArray()
        );
    }

    public static IReportDefinition.Request Request(ReportJob job, Guid[] sites) =>
        new(
            job.OrgId,
            job.RequestedBy,
            sites,
            job.Selection,
            JsonSerializer.Deserialize<JsonElement>(job.OptionsJson)
        );
}
