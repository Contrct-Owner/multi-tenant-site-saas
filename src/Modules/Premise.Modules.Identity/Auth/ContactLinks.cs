using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Premise.Contracts;
using Premise.Modules.Identity.Data;
using Premise.Platform.Entitlements;
using Premise.Platform.Kernel;
using Premise.Platform.Notifications;
using Wolverine;
using Wolverine.Http;

namespace Premise.Modules.Identity.Auth;

/// <summary>
/// The identified-contact tier (ADR 7): known via a signed, expiring token -
/// no account. Issuance persists the Contact record (the revocation store)
/// and publishes SendContactLink through the Wolverine outbox (ADR 32: the
/// email is transactional with its cause); the handler renders and hands to
/// the transport. Tokens are short-lived (30 min); the CONTACT is long-lived
/// and revocable. The link points at the org's own public host - a redeemed
/// contact lands on the org's public app, identified.
/// </summary>
public static class ContactLinks
{
    private const string TokenPurpose = "premise.contact.link";

    public sealed record IssueContactLinkRequest(string Email);

    public sealed record SendContactLink(string Email, string Url);

    public static IEndpointRouteBuilder MapContactLinkEndpoints(this IEndpointRouteBuilder app)
    {
        // Issuance: a member of the active org invites a contact into it.
        app.MapPost(
            "/contact-links",
            async (
                HttpContext http,
                IPrincipalAccessor accessor,
                IdentityDbContext db,
                IDataProtectionProvider dp,
                IMessageBus bus,
                IEntitlements entitlements,
                IConfiguration configuration,
                IssueContactLinkRequest request,
                CancellationToken ct
            ) =>
            {
                if (
                    accessor.Current
                    is not Principal.User { ActiveOrg: { } org, UserId: var userId }
                )
                    return Results.Unauthorized();
                if (!await db.Memberships.AnyAsync(m => m.UserId == userId && m.OrgId == org, ct))
                    return Results.NotFound();

                // Gate 1, both shapes: boolean feature switch + monthly meter
                // (Grace absorbs the approximate live count, ADR 9).
                if (!await entitlements.HasAsync(org, EntitlementCatalog.ContactLinksEnabled, ct))
                    return Results.Json(
                        new
                        {
                            error = "contact links are not part of this plan",
                            code = EntitlementCatalog.ContactLinksEnabled,
                        },
                        statusCode: StatusCodes.Status402PaymentRequired
                    );
                var usage = await entitlements.RecordUsageAsync(
                    org,
                    EntitlementCatalog.ContactLinksMonthly,
                    1,
                    ct
                );
                if (!usage.IsAllowed)
                    return Results.Json(
                        new
                        {
                            error = "monthly contact-link allowance exhausted",
                            usage.Code,
                            usage.Limit,
                            usage.Current,
                        },
                        statusCode: StatusCodes.Status402PaymentRequired
                    );

                // the CONTACT is the durable, revocable thing; the token is
                // just a 30-minute key to it. Re-inviting a revoked contact
                // is a deliberate re-grant.
                var email = request.Email.Trim().ToLowerInvariant();
                var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Email == email, ct);
                if (contact is null)
                {
                    contact = new Contact
                    {
                        Id = Guid.CreateVersion7(),
                        OrgId = org,
                        Email = email,
                        CreatedBy = userId,
                    };
                    db.Contacts.Add(contact);
                }
                else
                {
                    contact.RevokedAt = null;
                }
                await db.SaveChangesAsync(ct);

                var expires = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds();
                var token = dp.CreateProtector(TokenPurpose)
                    .Protect($"{contact.Id}|{org.Value}|{expires}");

                // the link lands on the ORG'S public host: the contact's
                // world is the public app, not the console or the API
                var slug = await db
                    .OrgDirectory.Where(d => d.OrgId == org)
                    .Select(d => d.Slug)
                    .FirstAsync(ct);
                var publicHost =
                    configuration["Public:HostTemplate"] ?? "http://{slug}.localhost:5174";
                var url =
                    $"{publicHost.Replace("{slug}", slug)}/contact/redeem?token={Uri.EscapeDataString(token)}";

                await bus.PublishAsync(new SendContactLink(email, url));
                await bus.PublishAsync(
                    new RecordDomainAudit(
                        "contact.invited",
                        System.Text.Json.JsonSerializer.Serialize(new { contactId = contact.Id })
                    ),
                    new DeliveryOptions
                    {
                        TenantId = org.Value.ToString(),
                        Headers =
                        {
                            ["premise-actor-tier"] = "user",
                            ["premise-actor-id"] = userId.ToString(),
                        },
                    }
                );
                return Results.Ok(new { contactId = contact.Id });
            }
        );

        app.MapGet(
            "/contact/redeem",
            async (HttpContext http, IDataProtectionProvider dp, string token) =>
            {
                string[] parts;
                try
                {
                    parts = dp.CreateProtector(TokenPurpose).Unprotect(token).Split('|');
                }
                catch (Exception)
                {
                    return Results.BadRequest(new { error = "invalid or tampered link" });
                }
                if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > long.Parse(parts[2]))
                    return Results.BadRequest(new { error = "link expired, request a new one" });

                // the token is only a key: the CONTACT RECORD decides. This
                // request is anonymous and contacts are RLS-protected, so the
                // read runs in a scope tenanted from the authenticated token.
                var contactId = Guid.Parse(parts[0]);
                var org = new OrgId(Guid.Parse(parts[1]));
                var email = await TenantScope.RunAsAsync(
                    http.RequestServices,
                    org,
                    sp =>
                        sp.GetRequiredService<IdentityDbContext>()
                            .Contacts.Where(c => c.Id == contactId && c.RevokedAt == null)
                            .Select(c => c.Email)
                            .FirstOrDefaultAsync(http.RequestAborted)
                );
                if (email is null)
                    return Results.BadRequest(new { error = "this link has been revoked" });

                var claims = new List<Claim>
                {
                    new(PremiseClaims.Tier, "contact"),
                    new("premise:contact_id", parts[0]),
                    new(PremiseClaims.ActiveOrg, parts[1]),
                    new(PremiseClaims.Email, email),
                };
                await http.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(
                        new ClaimsIdentity(
                            claims,
                            CookieAuthenticationDefaults.AuthenticationScheme
                        )
                    )
                );
                // relative: through the public app's redeem proxy this is the
                // org's public locator; hit directly it is the API root
                return Results.Redirect("/");
            }
        );

        return app;
    }
}

/// <summary>Wolverine handler: durable via the outbox, retried on transport failure.</summary>
public static class SendContactLinkHandler
{
    public static Task Handle(
        ContactLinks.SendContactLink message,
        INotificationTransport transport,
        CancellationToken ct
    ) =>
        transport.SendAsync(
            new EmailMessage(
                message.Email,
                "Your access link",
                $"Follow this link to continue: {message.Url}\nIt expires in 30 minutes."
            ),
            ct
        );
}

/// <summary>
/// Contact custody for members: who was let in, and taking it back. Revocation
/// is a status flip - the row remains the auditable record, and both the
/// scope resolver (live sessions) and redemption (unexpired tokens) consult
/// it, so revoking cuts off every path at once.
/// </summary>
public static class ContactManagementEndpoints
{
    [WolverineGet("/api/contacts")]
    public static async Task<IResult> List(
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.RolesManage, ct))
            return Results.Unauthorized();
        var contacts = await db
            .Contacts.OrderByDescending(c => c.CreatedAt)
            .Select(c => new
            {
                c.Id,
                c.Email,
                c.CreatedAt,
                revoked = c.RevokedAt != null,
            })
            .ToListAsync(ct);
        return Results.Ok(contacts);
    }

    [WolverineDelete("/api/contacts/{id}")]
    public static async Task<IResult> Revoke(
        Guid id,
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (
            accessor.Current
                is not Principal.User { ActiveOrg: { } org, UserId: var userId } principal
            || !await scopes.CanAsync(principal, Capabilities.RolesManage, ct)
        )
            return Results.Unauthorized();
        var contact = await db.Contacts.FirstOrDefaultAsync(
            c => c.Id == id && c.RevokedAt == null,
            ct
        );
        if (contact is null)
            return Results.NotFound();
        contact.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new RecordDomainAudit(
                "contact.revoked",
                System.Text.Json.JsonSerializer.Serialize(new { contactId = id })
            ),
            new DeliveryOptions
            {
                TenantId = org.Value.ToString(),
                Headers =
                {
                    ["premise-actor-tier"] = "user",
                    ["premise-actor-id"] = userId.ToString(),
                },
            }
        );
        return Results.NoContent();
    }
}
