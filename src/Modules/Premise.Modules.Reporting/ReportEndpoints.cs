using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Premise.Contracts;
using Premise.Modules.Reporting.Data;
using Premise.Platform.Data;
using Premise.Platform.Entitlements;
using Premise.Platform.Kernel;
using Premise.Platform.Messaging;
using Premise.Platform.Storage;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace Premise.Modules.Reporting;

public static class ReportEndpoints
{
    public sealed record SubmitRequest(
        string ReportType,
        string Mode,
        string Selection,
        Guid[] SiteIds,
        JsonElement Options
    );

    public sealed record AcceptedResponse(Guid Id, string State);

    public sealed record TypeResponse(string Id, string Name, int Version, bool Aggregate);

    public sealed record ItemResponse(
        Guid Id,
        string State,
        string? ErrorCode,
        DateTimeOffset? GeneratedAt,
        int Attempt,
        Guid[] SiteIds,
        string[] Warnings
    );

    public sealed record ArtifactResponse(
        Guid Id,
        Guid? ItemId,
        string Name,
        string ContentType,
        long Bytes,
        Guid? FileId
    );

    public sealed record JobResponse(
        Guid Id,
        string ReportType,
        string Mode,
        string Selection,
        string State,
        string? ErrorCode,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ExpiresAt,
        ItemResponse[] Items,
        ArtifactResponse[] Artifacts,
        bool CanModify
    );

    public sealed record DownloadResponse(string Url, int ExpiresInSeconds);

    public sealed record BasemapResponse(string Id, string Attribution);

    [WolverineGet("/api/reports/quota")]
    [Transactional(
        typeof(ReportingDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [ProducesResponseType(typeof(ReportQuota.Status), 200)]
    public static async Task<IResult> Quota(
        ReportingDbContext db,
        [FromServices] ReportQuota quota,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.ReportsGenerate, ct);
        return gate is GateOutcome.Allowed { Org: var org }
            ? Results.Ok(await quota.StatusAsync(org, ct))
            : gate.ToResult();
    }

    [WolverineGet("/api/reports/basemaps")]
    [Transactional(
        typeof(ReportingDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [ProducesResponseType(typeof(BasemapResponse[]), 200)]
    public static async Task<IResult> Basemaps(
        ReportingDbContext db,
        [FromServices] Rendering.ReportBasemaps basemaps,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.ReportsGenerate, ct);
        return gate is GateOutcome.Allowed
            ? Results.Ok(
                basemaps.All.Select(x => new BasemapResponse(x.Id, x.Attribution)).ToArray()
            )
            : gate.ToResult();
    }

    [WolverineGet("/api/reports/types")]
    [Transactional(
        typeof(ReportingDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [ProducesResponseType(typeof(TypeResponse[]), 200)]
    public static async Task<IResult> Types(
        ReportingDbContext db,
        [FromServices] ReportRegistry registry,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.ReportsGenerate, ct);
        return gate is GateOutcome.Allowed
            ? Results.Ok(
                registry
                    .All.Select(x => new TypeResponse(x.Id, x.Name, x.Version, x.Aggregate))
                    .ToArray()
            )
            : gate.ToResult();
    }

    [Transactional(typeof(ReportingDbContext))]
    [WolverinePost("/api/reports")]
    [ProducesResponseType(typeof(AcceptedResponse), 200)]
    public static async Task<IResult> Submit(
        SubmitRequest request,
        ReportingDbContext db,
        [FromServices] ReportRegistry registry,
        [FromServices] ReportLimits limits,
        [FromServices] ReportAccess access,
        IReportSiteSource sites,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        [FromServices] ReportQuota quota,
        IEntitlements entitlements,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.ReportsGenerate, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User user, Org: var org })
            return gate.ToResult();
        if (!await entitlements.HasAsync(org, EntitlementCatalog.ReportsEnabled, ct))
            return GateResults.FeatureOff(EntitlementCatalog.ReportsEnabled);
        var definition = registry.Find(request.ReportType);
        if (
            definition is null
            || request.Mode is not ("single" or "bulk" or "aggregate")
            || definition.Aggregate != (request.Mode == "aggregate")
            || request.Selection is not ("selected" or "accessible" or "organization")
        )
            return ApiErrors.BadRequest("Unknown report type, mode or selection.");
        if (
            request.Options.ValueKind != JsonValueKind.Object
            || System.Text.Encoding.UTF8.GetByteCount(request.Options.GetRawText()) > 16384
        )
            return ApiErrors.BadRequest("Report options must be an object of at most 16 KiB.");
        if (definition.ValidateOptions(request.Options) is { } error)
            return ApiErrors.BadRequest(error);
        if (!await access.CanGenerateAsync(org, user.UserId, ct))
            return Results.Forbid();
        var limit =
            request.Mode == "single" ? 1
            : request.Mode == "aggregate" ? limits.MaxAggregateSites
            : limits.MaxBatchItems;
        if (
            request.SiteIds is null
            || request.SiteIds.Length > limit
            || request.SiteIds.Any(x => x == Guid.Empty)
            || request.SiteIds.Distinct().Count() != request.SiteIds.Length
        )
            return ApiErrors.BadRequest($"Choose at most {limit} distinct sites.");
        var siteScope = ReportAccess.Intersect(
            await scopes.ScopeForAsync(user, Capabilities.SitesRead, ct),
            await scopes.ScopeForAsync(user, Capabilities.ReportsGenerate, ct)
        );
        if (request.Selection == "organization" && siteScope is not NodeScope.EntireOrg)
            return Results.Forbid();
        var selected = await sites.SelectAsync(
            org,
            siteScope,
            request.Selection == "selected" ? request.SiteIds : null,
            limit,
            ct
        );
        if (selected.Count == 0 || selected.Count > limit)
            return ApiErrors.BadRequest(
                $"The selection must contain 1..{limit} sites; it has not been truncated."
            );
        if (request.Selection == "selected" && selected.Count != request.SiteIds.Length)
            return Results.NotFound();
        var ids = selected.Select(x => x.Id).ToArray();
        var input = new IReportDefinition.Request(
            org,
            user.UserId,
            ids,
            request.Selection,
            request.Options
        );
        if (!await definition.AuthorizeAsync(input, null, ct))
            return Results.Forbid();
        if (!await db.TryTakeAsync(org.Value, ct))
            return Results.Problem("Report admission is busy; retry.", statusCode: 503);
        if (
            await db.Jobs.CountAsync(
                x => x.OrgId == org && (x.State == "Queued" || x.State == "Running"),
                ct
            ) >= limits.MaxQueuedJobsPerOrg
        )
            return Results.Problem(
                "The organization already has too many queued reports.",
                statusCode: 429
            );
        var job = new ReportJob
        {
            OrgId = org,
            RequestedBy = user.UserId,
            ReportType = definition.Id,
            DefinitionVersion = definition.Version,
            Mode = request.Mode,
            Selection = request.Selection,
            SiteIds = ids,
            OptionsJson = request.Options.GetRawText(),
        };
        foreach (
            var group in request.Mode == "aggregate" ? new[] { ids } : ids.Select(x => new[] { x })
        )
            job.Items.Add(
                new ReportItem
                {
                    OrgId = org,
                    JobId = job.Id,
                    SiteIds = group,
                }
            );
        EntitlementDecision quotaDecision;
        try
        {
            quotaDecision = await quota.ReserveAsync(
                org,
                job.Id,
                job.Items.Select(x => x.Id).ToArray(),
                ct
            );
        }
        catch (CapacityBusyException)
        {
            return Results.Problem("Report quota admission is busy; retry.", statusCode: 503);
        }
        if (!quotaDecision.IsAllowed)
            return GateResults.LimitReached(quotaDecision);
        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);
        await bus.PublishForOrgAsync(org, new GenerateReport(job.Id));
        await bus.AuditAsync(
            org,
            AuditActor.User(user.UserId),
            "report.requested",
            new
            {
                job.Id,
                job.ReportType,
                job.Mode,
                count = ids.Length,
            }
        );
        return Results.Ok(new AcceptedResponse(job.Id, job.State));
    }

    [Transactional(
        typeof(ReportingDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/reports")]
    [ProducesResponseType(typeof(JobResponse[]), 200)]
    public static async Task<IResult> List(
        ReportingDbContext db,
        [FromServices] ReportAccess access,
        [FromServices] ReportRegistry registry,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.FilesRead, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User user, Org: var org })
            return gate.ToResult();
        var jobs = await db
            .Jobs.AsNoTracking()
            .Include(x => x.Items)
            .Where(x => x.OrgId == org)
            .OrderByDescending(x => x.CreatedAt)
            .Take(50)
            .ToListAsync(ct);
        var visible = new List<JobResponse>();
        foreach (var job in jobs)
            if (await access.CanReadJobAsync(job, user.UserId, registry, ct))
                visible.Add(View(job, user.UserId));
        return Results.Ok(visible.ToArray());
    }

    [Transactional(
        typeof(ReportingDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/reports/{id}")]
    [ProducesResponseType(typeof(JobResponse), 200)]
    public static async Task<IResult> Get(
        Guid id,
        ReportingDbContext db,
        [FromServices] ReportAccess access,
        [FromServices] ReportRegistry registry,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.FilesRead, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User user, Org: var org })
            return gate.ToResult();
        var job = await db
            .Jobs.AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Artifacts)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.OrgId == org && x.Id == id, ct);
        return job is null || !await access.CanReadJobAsync(job, user.UserId, registry, ct)
            ? Results.NotFound()
            : Results.Ok(View(job, user.UserId));
    }

    [Transactional(typeof(ReportingDbContext))]
    [WolverinePost("/api/reports/{id}/cancel")]
    [ProducesResponseType(typeof(AcceptedResponse), 200)]
    public static async Task<IResult> Cancel(
        Guid id,
        ReportingDbContext db,
        [FromServices] ReportLimits limits,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.ReportsGenerate, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User user, Org: var org })
            return gate.ToResult();
        if (!await db.TryTakeAsync(id, ct))
            return ApiErrors.Conflict("Report is being modified; retry.");
        var job = await db
            .Jobs.Include(x => x.Items)
            .FirstOrDefaultAsync(
                x => x.OrgId == org && x.Id == id && x.RequestedBy == user.UserId,
                ct
            );
        if (job is null)
            return Results.NotFound();
        if (job.State is "Queued" or "Running")
        {
            job.Revision++;
            Finish(job, "Canceled", limits);
            await ReportQuota.ReleaseAsync(db, org, job.Id, ct);
            foreach (var item in job.Items.Where(x => x.State is "Queued" or "Running"))
                item.State = "Canceled";
            await db.SaveChangesAsync(ct);
        }
        return Results.Ok(new AcceptedResponse(job.Id, job.State));
    }

    [Transactional(typeof(ReportingDbContext))]
    [WolverinePost("/api/reports/{id}/retry")]
    [ProducesResponseType(typeof(AcceptedResponse), 200)]
    public static async Task<IResult> Retry(
        Guid id,
        ReportingDbContext db,
        [FromServices] ReportRegistry registry,
        [FromServices] ReportLimits limits,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        [FromServices] ReportQuota quota,
        IEntitlements entitlements,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.ReportsGenerate, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User user, Org: var org })
            return gate.ToResult();
        if (!await entitlements.HasAsync(org, EntitlementCatalog.ReportsEnabled, ct))
            return GateResults.FeatureOff(EntitlementCatalog.ReportsEnabled);
        if (!await db.TryTakeAsync(id, ct))
            return ApiErrors.Conflict("Report is being modified; retry.");
        var job = await db
            .Jobs.Include(x => x.Items)
            .FirstOrDefaultAsync(
                x => x.OrgId == org && x.Id == id && x.RequestedBy == user.UserId,
                ct
            );
        if (job is null)
            return Results.NotFound();
        if (
            job.State is not ("Failed" or "CompletedWithErrors")
            || job.ExpiresAt <= DateTimeOffset.UtcNow
        )
            return ApiErrors.Conflict(
                "Only unexpired failed work can be retried. Generate again for a fresh batch."
            );
        if (registry.Find(job.ReportType, job.DefinitionVersion) is null)
            return ApiErrors.Conflict("Report definition version is unavailable.");
        if (!await db.TryTakeAsync(org.Value, ct))
            return Results.Problem("Report admission is busy; retry.", statusCode: 503);
        if (
            await db.Jobs.CountAsync(
                x => x.OrgId == org && (x.State == "Queued" || x.State == "Running"),
                ct
            ) >= limits.MaxQueuedJobsPerOrg
        )
            return Results.Problem(
                "Too many queued reports for this organization.",
                statusCode: 429
            );
        EntitlementDecision quotaDecision;
        try
        {
            quotaDecision = await quota.ReserveAsync(
                org,
                job.Id,
                job.Items.Where(x => x.State == "Failed").Select(x => x.Id).ToArray(),
                ct
            );
        }
        catch (CapacityBusyException)
        {
            return Results.Problem("Report quota admission is busy; retry.", statusCode: 503);
        }
        if (!quotaDecision.IsAllowed)
            return GateResults.LimitReached(quotaDecision);
        foreach (var item in job.Items.Where(x => x.State == "Failed"))
        {
            item.State = "Queued";
            item.ErrorCode = null;
        }
        job.State = "Queued";
        job.ErrorCode = null;
        job.Revision++;
        job.LeaseOwner = null;
        job.LeaseUntil = null;
        await db.SaveChangesAsync(ct);
        await bus.PublishForOrgAsync(org, new GenerateReport(id));
        return Results.Ok(new AcceptedResponse(id, job.State));
    }

    [Transactional(
        typeof(ReportingDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/reports/{id}/artifacts/{artifactId}/download")]
    [ProducesResponseType(typeof(DownloadResponse), 200)]
    public static async Task<IResult> Download(
        Guid id,
        Guid artifactId,
        ReportingDbContext db,
        [FromServices] ReportRegistry registry,
        [FromServices] ReportAccess access,
        IObjectStore store,
        IReportPublishedFiles files,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.FilesRead, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User user, Org: var org })
            return gate.ToResult();
        var job = await db
            .Jobs.AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Artifacts)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.OrgId == org && x.Id == id, ct);
        var artifact = job?.Artifacts.SingleOrDefault(x =>
            x.Id == artifactId && x.Ready && (x.ItemId != null || x.Revision == job.Revision)
        );
        if (job is null || artifact is null || job.State == "Purging")
            return Results.NotFound();
        if (artifact.ItemId is not null)
            return artifact.FilePublished
                ? Results.Redirect($"/api/files/{artifact.Id}/download")
                : Results.NotFound();
        if (
            job.ExpiresAt <= DateTimeOffset.UtcNow
            || job.ExpiresAt is null && job.State is not ("Queued" or "Running")
        )
            return Results.NotFound();
        var definition = registry.Find(job.ReportType, job.DefinitionVersion);
        if (definition is null)
            return Results.Forbid();
        var items = (
            artifact.ItemId is { } itemId
                ? job.Items.Where(x => x.Id == itemId && x.State == "Succeeded")
                : job.Items.Where(x => x.State == "Succeeded")
        ).ToArray();
        if (items.Length == 0)
            return Results.NotFound();
        foreach (var item in items)
            if (
                !await access.CanReadSitesAsync(org, user.UserId, item.SiteIds, ct)
                || !await definition.AuthorizeAsync(
                    ReportAccess.Request(job, item.SiteIds) with
                    {
                        UserId = user.UserId,
                    },
                    JsonSerializer.Deserialize<JsonElement>(item.DependenciesJson),
                    ct
                )
            )
                return Results.Forbid();
        // A cached ZIP cannot bypass trash, erasure or changed source permissions.
        var pdfs = job
            .Artifacts.Where(x => x.Ready && x.ItemId != null)
            .Select(x => x.Id)
            .ToArray();
        if (!await files.AreCleanAsync(org, pdfs, ct))
            return Results.NotFound();
        if (!await access.CanReadSitesAsync(org, user.UserId, job.SiteIds, ct))
            return Results.Forbid();
        var ttl = job.ExpiresAt is { } expires
            ? Math.Min(60, (int)(expires - DateTimeOffset.UtcNow).TotalSeconds)
            : 60;
        if (ttl < 1)
            return Results.NotFound();
        return Results.Ok(
            new DownloadResponse(
                (
                    await store.GetDownloadUrlAsync(artifact.Key, TimeSpan.FromSeconds(ttl), ct)
                ).ToString(),
                ttl
            )
        );
    }

    internal static void Finish(ReportJob job, string state, ReportLimits limits)
    {
        job.State = state;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.ExpiresAt ??= job.CompletedAt.Value.AddDays(limits.ArtifactDays);
        job.MetadataExpiresAt ??= job.CompletedAt.Value.AddDays(
            Math.Max(limits.ArtifactDays, limits.MetadataDays)
        );
    }

    private static JobResponse View(ReportJob job, Guid userId) =>
        new(
            job.Id,
            job.ReportType,
            job.Mode,
            job.Selection,
            job.State,
            job.ErrorCode,
            job.CreatedAt,
            job.ExpiresAt,
            job.Items.Select(x => new ItemResponse(
                    x.Id,
                    x.State,
                    x.ErrorCode,
                    x.GeneratedAt,
                    x.Attempt,
                    x.SiteIds,
                    JsonSerializer.Deserialize<string[]>(x.WarningsJson) ?? []
                ))
                .ToArray(),
            job.Artifacts.Where(x => x.Ready && (x.ItemId != null || x.Revision == job.Revision))
                .Select(x => new ArtifactResponse(
                    x.Id,
                    x.ItemId,
                    x.Name,
                    x.ContentType,
                    x.Bytes,
                    x.FilePublished ? x.Id : null
                ))
                .ToArray(),
            job.RequestedBy == userId
        );
}
