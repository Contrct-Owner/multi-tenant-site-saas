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
    /// <summary>
    /// Whose page is this? The org's public identity - name for the title and
    /// header, brand color as the fork-ready theming hook (the seeded
    /// brand.color org setting finally has a reader). A shell enhancer: 404
    /// when the host resolves to nothing, and the page renders unbranded.
    /// </summary>
    [Transactional(typeof(TenancyDbContext))]
    [WolverineGet("/public/org")]
    public static async Task<IResult> OrgIdentity(
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.PublicRead, ct))
            return Results.NotFound();
        OrgId? principalOrg = accessor.Current switch
        {
            Principal.Guest { Org: { } guestOrg } => guestOrg,
            Principal.Contact contact => contact.Org,
            Principal.User { ActiveOrg: { } activeOrg } => activeOrg,
            _ => null,
        };
        if (principalOrg is not { } org)
            return Results.NotFound();
        var organization = await db
            .Organizations.Where(o => o.Id == org)
            .Select(o => new { o.Name, o.Slug })
            .FirstOrDefaultAsync(ct);
        if (organization is null)
            return Results.NotFound();
        var brandColor = await db
            .OrganizationSettings.Where(s => s.Key == "brand.color")
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return Results.Ok(
            new
            {
                organization.Name,
                organization.Slug,
                brandColor,
            }
        );
    }

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
        // upcoming holiday closures (next 30 site-local days), so the page
        // can say "Closed Dec 25" instead of silently skipping the day
        var localToday = DateOnly.FromDateTime(
            TimeZoneInfo
                .ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(site.TimeZone))
                .DateTime
        );
        var closureHorizon = localToday.AddDays(30);
        var closures = (
            await db
                .SiteSchedules.Where(s => s.SiteId == siteId)
                .Select(s => s.ExDates)
                .ToListAsync(ct)
        )
            .SelectMany(dates => dates)
            .Where(date => date >= localToday && date <= closureHorizon)
            .Distinct()
            .OrderBy(date => date)
            .Select(date => date.ToString("yyyy-MM-dd"))
            .ToList();
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
                closures,
            }
        );
    }
}
