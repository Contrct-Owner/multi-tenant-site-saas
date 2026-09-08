using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Premise.Modules.Reporting.Data;
using Premise.Platform.Kernel;
using Premise.Platform.Storage;

namespace Premise.Modules.Reporting;

public sealed class ReportFileAccess(
    ReportingDbContext db,
    ReportRegistry registry,
    ReportAccess access
) : IFileOriginAccess
{
    public string Origin => "report";

    public async Task<bool> CanReadAsync(OrgId org, Guid fileId, Guid userId, CancellationToken ct)
    {
        var artifact = await db
            .Artifacts.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OrgId == org && x.Id == fileId && x.Ready && x.ItemId != null,
                ct
            );
        if (artifact is null)
            return false;
        var item = await db
            .Items.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OrgId == org && x.Id == artifact.ItemId && x.State == ReportItemState.Succeeded,
                ct
            );
        var job = await db
            .Jobs.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrgId == org && x.Id == artifact.JobId, ct);
        if (
            item is null
            || job is null
            || job.State == ReportJobState.Purging
            || !await access.CanReadSitesAsync(org, userId, item.SiteIds, ct)
        )
            return false;
        var definition = registry.Find(job.ReportType, job.DefinitionVersion);
        return definition is not null
            && await definition.AuthorizeAsync(
                ReportAccess.Request(job, item.SiteIds) with
                {
                    UserId = userId,
                },
                JsonSerializer.Deserialize<JsonElement>(item.DependenciesJson),
                ct
            );
    }
}
