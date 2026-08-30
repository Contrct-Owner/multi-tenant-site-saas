using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Premise.Modules.Tenancy.Data;
using Premise.Platform.Kernel;
using Wolverine.Attributes;
using Wolverine.Http;

namespace Premise.Modules.Tenancy.Sites;

/// <summary>
/// The guest surface (ADR 7): public-safe site data for the host-derived org,
/// behind the SAME gates as everything else - guests hold exactly public:read
/// over their org, and RLS scopes the rows. Closed sites and internal fields
/// (paths, nodes, external ids) never appear here.
/// </summary>
public static class PublicSiteEndpoints
{
    [Transactional(typeof(TenancyDbContext))]
    [WolverineGet("/public/sites")]
    public static async Task<IResult> List(
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.PublicRead, ct))
            return Results.Ok(Array.Empty<object>()); // unknown host: empty, never an error
        var now = time.GetUtcNow();
        var sites = await db
            .Sites.Where(s => s.Status == SiteStatus.Open || s.Status == SiteStatus.ComingSoon)
            .OrderBy(s => s.Name)
            .Select(s => new
            {
                id = s.Id.Value,
                s.Name,
                s.City,
                s.TimeZone,
                status = s.Status.ToString(),
                openNow = db.SiteOpenWindows.Any(w =>
                    w.SiteId == s.Id && w.StartsAtUtc <= now && now < w.EndsAtUtc
                ),
            })
            .ToListAsync(ct);
        return Results.Ok(sites);
    }

    [Transactional(typeof(TenancyDbContext))]
    [WolverineGet("/public/sites/{id}")]
    public static async Task<IResult> Get(
        Guid id,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.PublicRead, ct))
            return Results.NotFound();
        var siteId = new SiteId(id);
        var site = await db.Sites.FirstOrDefaultAsync(
            s =>
                s.Id == siteId
                && (s.Status == SiteStatus.Open || s.Status == SiteStatus.ComingSoon),
            ct
        );
        if (site is null)
            return Results.NotFound();

        var now = time.GetUtcNow();
        var horizon = now.AddDays(7);
        var windows = await db
            .SiteOpenWindows.Where(w =>
                w.SiteId == siteId && w.EndsAtUtc > now && w.StartsAtUtc < horizon
            )
            .OrderBy(w => w.StartsAtUtc)
            .Select(w => new
            {
                w.StartsAtUtc,
                w.EndsAtUtc,
                w.LocalDate,
            })
            .ToListAsync(ct);
        return Results.Ok(
            new
            {
                id = site.Id.Value,
                site.Name,
                site.City,
                site.AddressLine1,
                site.PostalCode,
                site.CountryCode,
                site.TimeZone,
                status = site.Status.ToString(),
                openNow = windows.Any(w => w.StartsAtUtc <= now && now < w.EndsAtUtc),
                windows,
            }
        );
    }
}
