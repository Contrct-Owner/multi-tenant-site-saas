using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Premise.Modules.Identity.Data;
using Premise.Modules.Identity.Users;
using Premise.Platform.Auth;
using Premise.Platform.Kernel;

namespace Premise.Modules.Identity.Auth;

/// <summary>
/// Cookie-session auth flow (ADR 21): HttpOnly encrypted cookie issued by the
/// API after the code exchange; no token ever reaches JavaScript. Minimal APIs
/// rather than Wolverine endpoints because SignIn/SignOut are HttpContext
/// operations - Wolverine handlers stay for domain work.
/// </summary>
public static class AuthEndpoints
{
    private const string StatePurpose = "premise.auth.state";

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/auth/login",
            (
                HttpContext http,
                IAuthProvider provider,
                IDataProtectionProvider dp,
                string? returnUrl,
                string? hint,
                string? org
            ) =>
            {
                var state = dp.CreateProtector(StatePurpose)
                    .Protect(
                        $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}|{SafeReturnUrl(returnUrl)}"
                    );
                var redirectUri = CallbackUri(http);
                return Results.Redirect(
                    provider.BuildAuthorizationUrl(redirectUri, state, hint, org)
                );
            }
        );

        app.MapGet(
            "/auth/callback",
            async (
                HttpContext http,
                IAuthProvider provider,
                IDataProtectionProvider dp,
                IdentityDbContext db,
                string code,
                string state,
                CancellationToken ct
            ) =>
            {
                string returnUrl;
                try
                {
                    var payload = dp.CreateProtector(StatePurpose).Unprotect(state).Split('|', 2);
                    if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - long.Parse(payload[0]) > 600)
                        return Results.BadRequest(
                            new { error = "auth state expired, retry login" }
                        );
                    returnUrl = payload[1];
                }
                catch (Exception)
                {
                    return Results.BadRequest(new { error = "invalid auth state" });
                }

                var identity = await provider.ExchangeCodeAsync(code, CallbackUri(http), ct);

                var user = await db.Users.FirstOrDefaultAsync(
                    u => u.Provider == identity.Provider && u.Subject == identity.Subject,
                    ct
                );
                if (user is null)
                {
                    user = AppUser.Create(
                        identity.Provider,
                        identity.Subject,
                        identity.Email,
                        identity.Name
                    );
                    db.Users.Add(user);
                }
                else
                {
                    user.Email = identity.Email;
                    user.Name = identity.Name ?? user.Name;
                }

                // Provider-org mapping: an SSO login through an org connection joins
                // that org (JIT membership). Otherwise the user keeps existing memberships.
                if (
                    identity.ExternalOrgId is { } externalOrgId
                    && await db.OrgDirectory.FirstOrDefaultAsync(
                        d => d.ExternalId == externalOrgId,
                        ct
                    )
                        is { } mapped
                    && !await db.Memberships.AnyAsync(
                        m => m.UserId == user.Id && m.OrgId == mapped.OrgId,
                        ct
                    )
                )
                {
                    var membership = Membership.Create(user.Id, mapped.OrgId);
                    db.Memberships.Add(membership);
                    // Bootstrap: the FIRST member of an org with no roles
                    // becomes Owner (*:*, org-wide) - someone must be able to
                    // assign roles (ADR 6).
                    if (
                        !await db
                            .Roles.IgnoreQueryFilters()
                            .AnyAsync(r => r.OrgId == mapped.OrgId, ct)
                    )
                    {
                        var owner = Access.Role.Create(mapped.OrgId, "Owner");
                        db.Roles.Add(owner);
                        db.RoleGrants.Add(
                            new Access.RoleGrant
                            {
                                Id = Guid.CreateVersion7(),
                                OrgId = mapped.OrgId,
                                RoleId = owner.Id,
                                Domain = "*",
                                Action = "*",
                            }
                        );
                        db.MembershipRoles.Add(
                            new Access.MembershipRole
                            {
                                Id = Guid.CreateVersion7(),
                                OrgId = mapped.OrgId,
                                MembershipId = membership.Id,
                                RoleId = owner.Id,
                                ScopePath = null,
                            }
                        );
                    }
                }
                await db.SaveChangesAsync(ct);

                var activeOrg = await db
                    .Memberships.Where(m => m.UserId == user.Id)
                    .OrderBy(m => m.CreatedAt)
                    .Select(m => (OrgId?)m.OrgId)
                    .FirstOrDefaultAsync(ct);

                await http.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    BuildClaimsPrincipal(user, activeOrg)
                );
                return Results.Redirect(returnUrl);
            }
        );

        app.MapPost(
            "/auth/logout",
            async (HttpContext http) =>
            {
                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.NoContent();
            }
        );

        app.MapPost(
            "/auth/switch-org",
            async (
                HttpContext http,
                IdentityDbContext db,
                SwitchOrgRequest request,
                CancellationToken ct
            ) =>
            {
                if (GetUserId(http.User) is not { } userId)
                    return Results.Unauthorized();

                var target = new OrgId(request.OrgId);
                // 404, not 403: same rule as tenant isolation - do not confirm the org exists.
                if (
                    !await db.Memberships.AnyAsync(m => m.UserId == userId && m.OrgId == target, ct)
                )
                    return Results.NotFound();

                var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
                await http.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    BuildClaimsPrincipal(user, target)
                );
                return Results.NoContent();
            }
        );

        app.MapGet(
            "/me",
            async (IPrincipalAccessor accessor, IdentityDbContext db, CancellationToken ct) =>
            {
                switch (accessor.Current)
                {
                    case Principal.User u:
                    {
                        var orgIds = await db
                            .Memberships.Where(m => m.UserId == u.UserId)
                            .Select(m => m.OrgId)
                            .ToListAsync(ct);
                        // local read model (org_directory), never Tenancy's tables
                        var summaries = await db
                            .OrgDirectory.Where(d => orgIds.Contains(d.OrgId))
                            .Select(d => new
                            {
                                id = d.OrgId.Value,
                                name = d.Name,
                                slug = d.Slug,
                            })
                            .ToListAsync(ct);
                        return Results.Ok(
                            new
                            {
                                tier = "user",
                                userId = u.UserId,
                                email = u.Email,
                                name = u.Name,
                                activeOrg = u.ActiveOrg?.Value,
                                organizations = summaries,
                            }
                        );
                    }
                    case Principal.Contact c:
                        return Results.Ok(
                            new
                            {
                                tier = "contact",
                                contactId = c.ContactId,
                                org = c.Org.Value,
                            }
                        );
                    case Principal.Guest g:
                        return Results.Ok(new { tier = "guest", org = g.Org?.Value });
                    default:
                        return Results.Unauthorized();
                }
            }
        );

        return app;
    }

    public static ClaimsPrincipal BuildClaimsPrincipal(AppUser user, OrgId? activeOrg)
    {
        var claims = new List<Claim>
        {
            new(PremiseClaims.UserId, user.Id.ToString()),
            new(PremiseClaims.Email, user.Email),
            new(PremiseClaims.Tier, "user"),
        };
        if (user.Name is { } name)
            claims.Add(new Claim(PremiseClaims.DisplayName, name));
        if (activeOrg is { } org)
            claims.Add(new Claim(PremiseClaims.ActiveOrg, org.Value.ToString()));
        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)
        );
    }

    private static Guid? GetUserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(PremiseClaims.UserId), out var id) ? id : null;

    private static string CallbackUri(HttpContext http) =>
        $"{http.Request.Scheme}://{http.Request.Host}/auth/callback";

    /// <summary>Open-redirect guard: relative paths only.</summary>
    private static string SafeReturnUrl(string? returnUrl) =>
        returnUrl is ['/', ..] && !returnUrl.StartsWith("//") ? returnUrl : "/me";
}

public sealed record SwitchOrgRequest(Guid OrgId);
