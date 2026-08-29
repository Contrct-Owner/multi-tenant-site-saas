using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Premise.Modules.Identity.Data;
using Premise.Platform.Kernel;
using Premise.Platform.Notifications;
using Wolverine;

namespace Premise.Modules.Identity.Auth;

/// <summary>
/// The identified-contact tier (ADR 7): known via a signed, expiring token -
/// no account. Token issuance publishes SendContactLink through the Wolverine
/// outbox (ADR 32: the email is transactional with its cause); the handler
/// renders and hands to the transport. Tokens are short-lived (30 min);
/// a revocation store arrives with the first domain slice that keeps
/// long-lived contact links.
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

                var contactId = Guid.CreateVersion7();
                var expires = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds();
                var token = dp.CreateProtector(TokenPurpose)
                    .Protect($"{contactId}|{org.Value}|{expires}");
                var url =
                    $"{http.Request.Scheme}://{http.Request.Host}/contact/redeem?token={Uri.EscapeDataString(token)}";

                await bus.PublishAsync(new SendContactLink(request.Email, url));
                return Results.Ok(new { contactId });
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

                var claims = new List<Claim>
                {
                    new(PremiseClaims.Tier, "contact"),
                    new("premise:contact_id", parts[0]),
                    new(PremiseClaims.ActiveOrg, parts[1]),
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
                return Results.Redirect("/me");
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
