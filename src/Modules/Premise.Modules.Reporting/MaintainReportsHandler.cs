using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Premise.Contracts;
using Premise.Modules.Reporting.Data;
using Premise.Platform.Data;
using Premise.Platform.Kernel;
using Premise.Platform.Messaging;
using Premise.Platform.Storage;
using Wolverine;
using Wolverine.Attributes;

namespace Premise.Modules.Reporting;

public static class MaintainReportsHandler
{
    [NonTransactional]
    public static async Task Handle(
        MaintainReports _,
        ITenantContext tenant,
        IServiceScopeFactory scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var org =
            tenant.OrgId
            ?? throw new InvalidOperationException("Report maintenance requires an organization.");
        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(org, tenant.Region);
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IObjectStore>();
        var now = DateTimeOffset.UtcNow;
        var jobs = await db
            .Jobs.AsNoTracking()
            .Where(x =>
                x.OrgId == org
                && (
                    x.State == ReportJobState.Queued
                    || x.State == ReportJobState.Running
                    || x.State == ReportJobState.Purging
                    || x.Artifacts.Any(a => a.Ready && a.ItemId != null && !a.FilePublished)
                    || x.ExpiresAt <= now && x.Artifacts.Any(a => !a.Ready || a.ItemId == null)
                    || x.MetadataExpiresAt <= now
                        && !x.Artifacts.Any(a => a.Ready && a.ItemId != null)
                )
            )
            .OrderBy(x => x.CreatedAt)
            .Take(200)
            .ToListAsync(ct);
        foreach (var job in jobs)
        {
            if (job.LeaseUntil > now)
            {
                if (job.State == ReportJobState.Purging)
                    await bus.PublishAsync(
                        new MaintainReports(),
                        new DeliveryOptions
                        {
                            TenantId = org.Value.ToString(),
                            ScheduledTime = job.LeaseUntil.Value.AddSeconds(1),
                        }
                    );
                continue;
            }
            if (job.State != ReportJobState.Purging)
            {
                // Reconciliation also publishes pre-upgrade PDFs and repairs a missing acknowledgement.
                var pending = await db
                    .Artifacts.AsNoTracking()
                    .Where(x =>
                        x.OrgId == org
                        && x.JobId == job.Id
                        && x.Ready
                        && x.ItemId != null
                        && !x.FilePublished
                    )
                    .ToListAsync(ct);
                foreach (var artifact in pending)
                {
                    var item = await db
                        .Items.AsNoTracking()
                        .SingleAsync(x => x.OrgId == org && x.Id == artifact.ItemId, ct);
                    await bus.PublishForOrgAsync(
                        org,
                        new PublishGeneratedFile(
                            artifact.Id,
                            artifact.Key,
                            artifact.Name,
                            artifact.ContentType,
                            artifact.Bytes,
                            job.RequestedBy,
                            item.GeneratedAt ?? artifact.CreatedAt,
                            item.SiteIds,
                            "report",
                            job.Id
                        )
                    );
                }
                if (job.ExpiresAt is null || job.ExpiresAt > now)
                {
                    if (job.State is ReportJobState.Queued or ReportJobState.Running)
                        await bus.PublishForOrgAsync(org, new GenerateReport(job.Id));
                    continue;
                }
            }
            await using (var tx = await db.Database.BeginTransactionAsync(ct))
            {
                await db.TakeAsync(job.Id, ct);
                var current = await db.Jobs.FirstOrDefaultAsync(
                    x => x.Id == job.Id && x.OrgId == org,
                    ct
                );
                if (current is null || current.LeaseUntil > DateTimeOffset.UtcNow)
                    continue;
                await ReportQuota.ReleaseAsync(db, org, job.Id, ct);
                if (current.State is ReportJobState.Running or ReportJobState.Queued)
                    current.State = ReportJobState.Expired;
                current.Revision++;
                current.LeaseOwner = null;
                current.LeaseUntil = null;
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            var artifacts = await db
                .Artifacts.Where(x =>
                    x.OrgId == org
                    && x.JobId == job.Id
                    && (job.State == ReportJobState.Purging || !x.Ready || x.ItemId == null)
                )
                .ToListAsync(ct);
            foreach (var artifact in artifacts)
                await store.DeleteAsync(artifact.Key, ct);
            // Successful PDFs belong to Storage, including its trash and legal-hold lifecycle.
            var ids = artifacts.Select(x => x.Id).ToArray();
            await db
                .Artifacts.Where(x => x.OrgId == org && ids.Contains(x.Id))
                .ExecuteDeleteAsync(ct);
            if (
                job.MetadataExpiresAt <= now
                && !await db.Artifacts.AnyAsync(
                    x => x.OrgId == org && x.JobId == job.Id && x.Ready && x.ItemId != null,
                    ct
                )
            )
                await db.Jobs.Where(x => x.OrgId == org && x.Id == job.Id).ExecuteDeleteAsync(ct);
            db.ChangeTracker.Clear();
        }
    }
}
