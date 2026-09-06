using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Premise.Contracts;
using Premise.Modules.Tenancy.Data;
using Premise.Modules.Tenancy.Hierarchy;
using Premise.Platform.Data;
using Premise.Platform.Entitlements;
using Premise.Platform.Kernel;
using Premise.Platform.Messaging;
using Premise.Platform.Spatial;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace Premise.Modules.Tenancy.Sites;

public sealed record CreateSiteRequest(
    Guid NodeId,
    string Name,
    string TimeZone,
    string? AddressLine1 = null,
    string? City = null,
    string? PostalCode = null,
    string? CountryCode = null,
    double? Latitude = null,
    double? Longitude = null
);

/// <summary>
/// Patch semantics: null = unchanged. Address fields accept "" to CLEAR -
/// an address typo must be fixable, and so must an address that never
/// existed (finding 2 of the competitive review).
/// </summary>
public sealed record UpdateSiteRequest(
    string? Name,
    string? TimeZone,
    SiteStatus? Status,
    string? AddressLine1 = null,
    string? City = null,
    string? PostalCode = null,
    string? CountryCode = null,
    double? Latitude = null,
    double? Longitude = null,
    System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>? Attributes = null,
    uint? Version = null
);

public sealed record CreateScheduleRequest(
    string Name,
    string RRule,
    DateOnly AnchorDate,
    TimeOnly Opens,
    TimeOnly Closes,
    DateOnly[]? ExDates = null
);

public sealed record SiteResponse(
    Guid Id,
    Guid NodeId,
    string Name,
    string TimeZone,
    string Status,
    string Path,
    uint Version,
    string? AddressLine1,
    string? City,
    string? PostalCode,
    string? CountryCode,
    double? Latitude,
    double? Longitude,
    System.Text.Json.JsonElement Attributes
);

/// <summary>
/// Site queries take a REQUIRED NodeScope (the third gate): resolved once per
/// request from the principal, applied as an ltree predicate. No endpoint
/// hand-writes an org or location filter.
/// </summary>
public static class SiteEndpoints
{
    [Transactional(typeof(TenancyDbContext))]
    [WolverinePost("/api/sites")]
    [ProducesResponseType(typeof(SiteResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Create(
        CreateSiteRequest request,
        TenancyDbContext db,
        IMessageBus bus,
        IEntitlements entitlements,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (!BusinessDate.IsValidTimeZone(request.TimeZone))
            return Results.BadRequest(
                new { error = $"'{request.TimeZone}' is not an IANA time zone" }
            );
        var node = await db.HierarchyNodes.FirstOrDefaultAsync(n => n.Id == request.NodeId, ct);
        if (node is null)
            return Results.NotFound();

        // Gates 2+3 on the write side: the grant must COVER the target node.
        var writeScope = await scopes.ScopeForAsync(accessor.Current, Capabilities.SitesManage, ct);
        if (!writeScope.Covers(node.Path.ToString()))
            return Results.Forbid();

        // Gate 1 (ADR 8/9): a limit failure is 402-and-upsell, never an error.
        var siteCount = await db.Sites.LongCountAsync(ct);
        var decision = await entitlements.CheckLimitAsync(
            node.OrgId,
            EntitlementCatalog.MaxSites,
            siteCount,
            1,
            ct
        );
        if (!decision.IsAllowed)
            return GateResults.LimitReached(decision);

        var id = SiteId.New();
        var site = new Site
        {
            Id = id,
            OrgId = node.OrgId,
            NodeId = node.Id,
            Name = request.Name,
            TimeZone = request.TimeZone,
            Path = new Microsoft.EntityFrameworkCore.LTree($"{node.Path}.{Site.Label(id)}"),
            AddressLine1 = request.AddressLine1,
            City = request.City,
            PostalCode = request.PostalCode,
            CountryCode = request.CountryCode,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
        };
        db.Sites.Add(site);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(site));
    }

    /// <summary>
    /// Fleet-scale list: filtered by scope FIRST, then searched, then paged.
    /// Every predicate is a leakproof key range (ADR 51), so the app role's
    /// plan is an index range under row security however many sites the org
    /// has: scope and <c>under</c> are <c>path_text</c> ranges, the map's
    /// <c>bbox</c> (ADR 49) is a set of <c>cell</c> ranges made exact by the
    /// coordinates, search is "a word of the name or city starts with each
    /// word typed" over the term index, and a page is a keyset on (name, id):
    /// <c>after</c> is the <c>next</c> cursor the previous page returned.
    /// <c>zoom</c> stays in the contract for clustering (ADR 50 §4); today it
    /// is validated and unused.
    /// </summary>
    [Transactional(typeof(TenancyDbContext))]
    [WolverineGet("/api/sites")]
    [ProducesResponseType(typeof(SiteListResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        Guid? under,
        string? q,
        string? status,
        string? bbox,
        int? zoom,
        int? limit,
        string? after,
        CancellationToken ct
    )
    {
        // a comma-separated set of lifecycle statuses (the console's filter bar)
        SiteStatus[]? statuses = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            var wanted = new List<SiteStatus>();
            foreach (var part in status.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Enum.TryParse<SiteStatus>(part.Trim(), ignoreCase: true, out var one))
                    return Results.BadRequest(new { error = $"unknown status '{part.Trim()}'" });
                wanted.Add(one);
            }
            statuses = wanted.ToArray();
        }
        BoundingBox? box = null;
        if (bbox is not null)
        {
            if (!BoundingBox.TryParse(bbox, out var parsed, out var error))
                return Results.BadRequest(new { error });
            box = parsed;
        }
        if (zoom is < 0 or > 22)
            return Results.BadRequest(new { error = "zoom must be between 0 and 22" });
        SiteCursor? cursor = null;
        if (after is not null)
        {
            if (!SiteCursor.TryParse(after, out var parsedCursor))
                return Results.BadRequest(new { error = "after is not a cursor this list issued" });
            cursor = parsedCursor;
        }

        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.SitesRead, ct);
        var query = db.Sites.InScope(scope);
        if (under is { } nodeId)
        {
            var node = await db.HierarchyNodes.FirstOrDefaultAsync(n => n.Id == nodeId, ct);
            if (node is null)
                return Results.Ok(new SiteListResponse([], 0, 0, null));
            query = query.Where(PathKeys.UnderAny<Site>([node.Path.ToString()]));
        }
        if (statuses is not null)
            query = query.Where(s => statuses.Contains(s.Status));
        int? withoutCoordinates = null;
        if (box is { } viewport)
        {
            // Counted before the box so the map's list can name what it can
            // never show; a site without coordinates is inside no box.
            withoutCoordinates = await query.CountAsync(s => s.Cell == null, ct);
            query = query.Where(SpatialPredicates.InViewport<Site>(viewport));
        }
        var take = Math.Clamp(limit ?? 50, 1, 200);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var words = SiteSearchTerm.Words(q);
            var org = scope switch
            {
                NodeScope.EntireOrg entire => entire.Org,
                NodeScope.Subtrees subtrees => subtrees.Org,
                _ => (OrgId?)null,
            };
            if (words.Count == 0 || org is null)
                return Results.Ok(new SiteListResponse([], 0, 0, null, withoutCoordinates));
            return Results.Ok(
                await SearchAsync(db, query, org.Value, words, cursor, take, withoutCoordinates, ct)
            );
        }

        // one pass over the (org_id, status) index counts the whole result; a
        // viewport's count is capped like a search's - a continent-wide box at a
        // million sites is the whole org, and "10,000+ in view" is the answer
        var counted = box is null ? query : query.Take(SearchCountCap);
        var counts = await counted
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var total = counts.Sum(c => c.Count);
        var openCount = counts.Where(c => c.Status == SiteStatus.Open).Sum(c => c.Count);

        var page = query;
        if (cursor is { Name: var afterName, Id: var afterId })
            // "name >= n" is the index range; the rest skips the rows a
            // previous page already showed under the same name
            page = page.Where(s =>
                s.Name.CompareTo(afterName) >= 0
                && (s.Name.CompareTo(afterName) > 0 || s.Id.CompareTo(afterId) > 0)
            );
        var sites = await page.OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .Take(take + 1)
            .ToListAsync(ct);
        var more = sites.Count > take;
        if (more)
            sites.RemoveAt(take);
        return Results.Ok(
            new SiteListResponse(
                sites.Select(ToResponse).ToList(),
                total,
                openCount,
                more ? SiteCursor.Encode(sites[^1]) : null,
                withoutCoordinates,
                TotalIsLowerBound: box is not null && total >= SearchCountCap
            )
        );
    }

    /// <summary>
    /// Past this many matches a search or a viewport reports a lower bound: counting every
    /// hit of a common word is the one search cost that grows with the org.
    /// </summary>
    public const int SearchCountCap = 10_000;

    /// <summary>
    /// Search (ADR 51): the first word's term range IS the page - the
    /// <c>(org_id, term, name, site_id)</c> index walked in its own order,
    /// joined to the filtered sites, further words probed per row - so a page
    /// costs the same however the matches are spread through the org.
    /// Results therefore come back by matched term, then name; the cursor
    /// carries the term.
    /// </summary>
    private static async Task<SiteListResponse> SearchAsync(
        TenancyDbContext db,
        IQueryable<Site> sites,
        OrgId org,
        IReadOnlyList<string> words,
        SiteCursor? cursor,
        int take,
        int? withoutCoordinates,
        CancellationToken ct
    )
    {
        foreach (var word in words.Skip(1))
        {
            var (lo, hi) = SiteSearchTerm.PrefixRange(word);
            sites = sites.Where(s =>
                db.SiteSearchTerms.Any(t =>
                    t.SiteId == s.Id && t.Term.CompareTo(lo) >= 0 && t.Term.CompareTo(hi) < 0
                )
            );
        }
        var (first, last) = SiteSearchTerm.PrefixRange(words[0]);
        var hits = db
            .SiteSearchTerms.Where(t =>
                t.OrgId == org && t.Term.CompareTo(first) >= 0 && t.Term.CompareTo(last) < 0
            )
            .Join(sites, t => t.SiteId, s => s.Id, (t, s) => new { t, s });

        var counts = await hits.Take(SearchCountCap)
            .GroupBy(x => x.s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var total = counts.Sum(c => c.Count);
        var openCount = counts.Where(c => c.Status == SiteStatus.Open).Sum(c => c.Count);

        if (cursor is { Term: { } afterTerm, Name: var afterName, Id: var afterId })
            hits = hits.Where(x =>
                x.t.Term.CompareTo(afterTerm) >= 0
                && (
                    x.t.Term.CompareTo(afterTerm) > 0
                    || x.t.Name.CompareTo(afterName) > 0
                    || (x.t.Name == afterName && x.t.SiteId.CompareTo(afterId) > 0)
                )
            );
        var rows = await hits.OrderBy(x => x.t.Term)
            .ThenBy(x => x.t.Name)
            .ThenBy(x => x.t.SiteId)
            .Take(take + 1)
            .Select(x => new { x.s, x.t.Term })
            .ToListAsync(ct);
        var more = rows.Count > take;
        if (more)
            rows.RemoveAt(take);
        return new SiteListResponse(
            rows.Select(r => ToResponse(r.s)).ToList(),
            total,
            openCount,
            more ? SiteCursor.Encode(rows[^1].s, rows[^1].Term) : null,
            withoutCoordinates,
            TotalIsLowerBound: total >= SearchCountCap
        );
    }

    [Transactional(typeof(TenancyDbContext))]
    [WolverineGet("/api/sites/open-now")]
    public static async Task<IReadOnlyList<SiteResponse>> OpenNow(
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.SitesRead, ct);
        var now = time.GetUtcNow();
        // The projection (ADR 28) makes this an indexed range query, not an
        // in-process RRULE expansion over every site.
        return
            await db
                .Sites.InScope(scope)
                .Where(s =>
                    db.SiteOpenWindows.Any(w =>
                        w.SiteId == s.Id && w.StartsAtUtc <= now && now < w.EndsAtUtc
                    )
                )
                .OrderBy(s => s.Name)
                .ToListAsync(ct)
                is { } open
            ? open.Select(ToResponse).ToList()
            : [];
    }

    [Transactional(typeof(TenancyDbContext))]
    [WolverineGet("/api/sites/{id}")]
    [ProducesResponseType(typeof(SiteResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Get(
        Guid id,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var siteId = new SiteId(id);
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct);
        // the scope gate applies to id-addressed reads too: outside the
        // grant's subtree is 404, same as outside the tenant
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.SitesRead, ct);
        return site is null || !scope.Covers(site.Path.ToString())
            ? Results.NotFound()
            : Results.Ok(ToResponse(site));
    }

    [Transactional(typeof(TenancyDbContext))]
    [WolverinePost("/api/sites/{id}")]
    [ProducesResponseType(typeof(SiteResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Update(
        Guid id,
        UpdateSiteRequest request,
        TenancyDbContext db,
        IMessageBus bus,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var siteId = new SiteId(id);
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct);
        if (site is null)
            return Results.NotFound();
        var updateScope = await scopes.ScopeForAsync(
            accessor.Current,
            Capabilities.SitesManage,
            ct
        );
        if (!updateScope.Covers(site.Path.ToString()))
            return Results.Forbid();
        // optimistic concurrency: the client echoes the version it edited;
        // a mismatch means someone else saved first - 409, never a clobber
        if (request.Version is { } version && version != site.Version)
            return Results.Conflict(
                new { error = "this site was changed by someone else; reload and retry" }
            );

        var timeZoneChanged = false;
        if (request.TimeZone is { } zone && zone != site.TimeZone)
        {
            if (!BusinessDate.IsValidTimeZone(zone))
                return Results.BadRequest(new { error = $"'{zone}' is not an IANA time zone" });
            site.TimeZone = zone;
            timeZoneChanged = true;
        }
        if (request.Name is { } name)
            site.Name = name;
        if (request.Status is { } status)
            site.Status = status;
        if (request.AddressLine1 is { } line1)
            site.AddressLine1 = line1.Length == 0 ? null : line1;
        if (request.City is { } city)
            site.City = city.Length == 0 ? null : city;
        if (request.PostalCode is { } postal)
            site.PostalCode = postal.Length == 0 ? null : postal;
        if (request.CountryCode is { } country)
            site.CountryCode = country.Length == 0 ? null : country.ToUpperInvariant();
        if (request is { Latitude: { } latitude, Longitude: { } longitude })
        {
            if (Math.Abs(latitude) > 90 || Math.Abs(longitude) > 180)
                return Results.BadRequest(new { error = "coordinates out of range" });
            site.Latitude = latitude;
            site.Longitude = longitude;
        }
        if (request.Attributes is { Count: > 0 } incoming)
        {
            var (error, merged) = await SiteAttributeEndpoints.MergeAttributesAsync(
                db,
                site.AttributesJson,
                incoming,
                ct
            );
            if (error is not null)
                return Results.BadRequest(new { error });
            site.AttributesJson = merged!;
        }
        await db.SaveChangesAsync(ct);

        // The rebuild trigger everyone forgets (ADR 28): a timezone change
        // shifts every published open window.
        if (timeZoneChanged)
            await bus.PublishForOrgAsync(site.OrgId, new RebuildSiteOccurrences(site.Id.Value));
        return Results.Ok(ToResponse(site));
    }

    [Transactional(typeof(TenancyDbContext))]
    [WolverinePost("/api/sites/{id}/schedules")]
    [ProducesResponseType(typeof(ScheduleCreatedResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> CreateSchedule(
        Guid id,
        CreateScheduleRequest request,
        TenancyDbContext db,
        IMessageBus bus,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var siteId = new SiteId(id);
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct);
        if (site is null)
            return Results.NotFound();
        var scheduleScope = await scopes.ScopeForAsync(
            accessor.Current,
            Capabilities.SitesManage,
            ct
        );
        if (!scheduleScope.Covers(site.Path.ToString()))
            return Results.Forbid();
        if (!Premise.Platform.Scheduling.RecurrenceExpander.IsValidRule(request.RRule))
            return Results.BadRequest(new { error = "invalid RRULE" });

        var schedule = SiteSchedule.Create(
            site.OrgId,
            site.Id,
            request.Name,
            request.RRule,
            request.AnchorDate,
            request.Opens,
            request.Closes
        );
        schedule.ExDates = request.ExDates ?? [];
        db.SiteSchedules.Add(schedule);
        await db.SaveChangesAsync(ct);

        await bus.PublishForOrgAsync(site.OrgId, new RebuildSiteOccurrences(site.Id.Value));
        return Results.Ok(new ScheduleCreatedResponse(schedule.Id));
    }

    /// <summary>
    /// WithoutCoordinates is set only for a bbox query (ADR 49): sites the
    /// scope (and search) hold that can never be inside a box, so the map's
    /// list can name them instead of letting them vanish.
    /// </summary>
    /// <summary>
    /// A page of the list. <c>Next</c> is the opaque cursor for the page after
    /// this one (null on the last), passed back as <c>after</c>. A search
    /// stops counting at <see cref="SearchCountCap"/> and says so with
    /// <c>TotalIsLowerBound</c>.
    /// </summary>
    public sealed record SiteListResponse(
        IReadOnlyList<SiteResponse> Items,
        int Total,
        int OpenCount,
        string? Next,
        int? WithoutCoordinates = null,
        bool TotalIsLowerBound = false
    );

    private static SiteResponse ToResponse(Site s) =>
        new(
            s.Id.Value,
            s.NodeId,
            s.Name,
            s.TimeZone,
            s.Status.ToString(),
            s.Path.ToString(),
            s.Version,
            s.AddressLine1,
            s.City,
            s.PostalCode,
            s.CountryCode,
            s.Latitude,
            s.Longitude,
            System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                s.AttributesJson
            )
        );
}

public sealed record ScheduleResponse(
    Guid Id,
    string Name,
    string RRule,
    DateOnly AnchorDate,
    TimeOnly Opens,
    TimeOnly Closes,
    DateOnly[] ExDates
);

public sealed record ScheduleCreatedResponse(Guid Id);

public sealed record SiteWindowResponse(
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    DateOnly LocalDate
);

/// <summary>Schedule listing/removal and the projection preview - the hours editor's backend.</summary>
public static class ScheduleEndpoints
{
    [Transactional(typeof(TenancyDbContext))]
    [WolverineGet("/api/sites/{id}/schedules")]
    [ProducesResponseType(typeof(List<ScheduleResponse>), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        Guid id,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var siteId = new SiteId(id);
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct);
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.SitesRead, ct);
        if (site is null || !scope.Covers(site.Path.ToString()))
            return Results.NotFound();
        var schedules = await db
            .SiteSchedules.Where(s => s.SiteId == siteId)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);
        return Results.Ok(
            schedules
                .Select(s => new ScheduleResponse(
                    s.Id,
                    s.Name,
                    s.RRule,
                    s.AnchorDate,
                    s.OpensLocal,
                    s.ClosesLocal,
                    s.ExDates
                ))
                .ToList()
        );
    }

    [Transactional(typeof(TenancyDbContext))]
    [WolverineDelete("/api/sites/{id}/schedules/{scheduleId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public static async Task<IResult> Delete(
        Guid id,
        Guid scheduleId,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var siteId = new SiteId(id);
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct);
        if (site is null)
            return Results.NotFound();
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.SitesManage, ct);
        if (!scope.Covers(site.Path.ToString()))
            return Results.Forbid();
        var schedule = await db.SiteSchedules.FirstOrDefaultAsync(
            s => s.Id == scheduleId && s.SiteId == siteId,
            ct
        );
        if (schedule is null)
            return Results.NotFound();

        db.SiteSchedules.Remove(schedule);
        await db.SaveChangesAsync(ct);
        // a removed rule invalidates its windows (ADR 28 rebuild trigger)
        await bus.PublishForOrgAsync(site.OrgId, new RebuildSiteOccurrences(site.Id.Value));
        return Results.NoContent();
    }

    /// <summary>Upcoming open windows from the projection - "what these rules actually mean".</summary>
    [Transactional(typeof(TenancyDbContext))]
    [WolverineGet("/api/sites/{id}/windows")]
    [ProducesResponseType(typeof(List<SiteWindowResponse>), StatusCodes.Status200OK)]
    public static async Task<IResult> Windows(
        Guid id,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        TimeProvider time,
        int? days,
        CancellationToken ct
    )
    {
        var siteId = new SiteId(id);
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct);
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.SitesRead, ct);
        if (site is null || !scope.Covers(site.Path.ToString()))
            return Results.NotFound();
        var now = time.GetUtcNow();
        var horizon = now.AddDays(Math.Clamp(days ?? 7, 1, 60));
        var windows = await db
            .SiteOpenWindows.Where(w =>
                w.SiteId == siteId && w.EndsAtUtc > now && w.StartsAtUtc < horizon
            )
            .OrderBy(w => w.StartsAtUtc)
            .Select(w => new SiteWindowResponse(w.StartsAtUtc, w.EndsAtUtc, w.LocalDate))
            .ToListAsync(ct);
        return Results.Ok(windows);
    }
}
