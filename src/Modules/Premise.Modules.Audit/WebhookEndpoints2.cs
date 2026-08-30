using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Premise.Modules.Audit.Data;
using Premise.Platform.Kernel;
using Premise.Platform.Secrets;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace Premise.Modules.Audit;

public sealed record CreateWebhookRequest(string Url, string[]? Events = null);

/// <summary>
/// Webhook custody (ADR 40): create shows the signing secret ONCE (verify
/// with the same t=...,v1=HMAC scheme the template uses for inbound billing
/// webhooks), delete is immediate, ping sends a signed test delivery.
/// Production requires https and refuses loopback/private hosts (SSRF).
/// </summary>
public static class WebhookManagementEndpoints
{
    [Transactional(typeof(AuditDbContext))]
    [WolverineGet("/api/webhooks")]
    public static async Task<IResult> List(
        AuditDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (!await Allowed(accessor, scopes, ct))
            return Results.Unauthorized();
        var endpoints = await db
            .WebhookEndpoints.OrderByDescending(e => e.CreatedAt)
            .Select(e => new
            {
                e.Id,
                e.Url,
                e.Events,
                e.Active,
                e.CreatedAt,
                lastDelivery = db
                    .WebhookDeliveries.Where(d => d.EndpointId == e.Id)
                    .OrderByDescending(d => d.OccurredAt)
                    .Select(d => new
                    {
                        d.EventName,
                        d.Ok,
                        d.StatusCode,
                        d.OccurredAt,
                    })
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);
        return Results.Ok(endpoints);
    }

    [Transactional(typeof(AuditDbContext))]
    [WolverinePost("/api/webhooks")]
    public static async Task<IResult> Create(
        CreateWebhookRequest request,
        AuditDbContext db,
        IKeyWrapper kms,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IWebHostEnvironment environment,
        CancellationToken ct
    )
    {
        if (accessor.Current is not Principal.User { ActiveOrg: { } org, UserId: var userId })
            return Results.Unauthorized();
        if (!await Allowed(accessor, scopes, ct))
            return Results.Unauthorized();
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
            return Results.BadRequest(new { error = "url must be absolute" });
        if (environment.IsProduction())
        {
            // SSRF floor: outbound calls originate from OUR network
            if (uri.Scheme != "https")
                return Results.BadRequest(new { error = "webhook urls must be https" });
            if (
                uri.IsLoopback
                || uri.Host is "localhost"
                || uri.HostNameType == UriHostNameType.IPv4
                || uri.HostNameType == UriHostNameType.IPv6
            )
                return Results.BadRequest(
                    new { error = "webhook urls must use a public dns name" }
                );
        }

        var secret =
            "whsec_"
            + Convert
                .ToBase64String(RandomNumberGenerator.GetBytes(24))
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        var endpoint = new WebhookEndpoint
        {
            Id = Guid.CreateVersion7(),
            OrgId = org,
            Url = request.Url,
            EncryptedSecret = await EnvelopeCrypto.EncryptAsync(secret, kms, ct),
            Events = request.Events ?? [],
            CreatedBy = userId,
        };
        db.WebhookEndpoints.Add(endpoint);
        await db.SaveChangesAsync(ct);
        // the one and only time the signing secret leaves the server
        return Results.Ok(new { endpoint.Id, secret });
    }

    [Transactional(typeof(AuditDbContext))]
    [WolverineDelete("/api/webhooks/{id}")]
    public static async Task<IResult> Delete(
        Guid id,
        AuditDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (!await Allowed(accessor, scopes, ct))
            return Results.Unauthorized();
        var endpoint = await db.WebhookEndpoints.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (endpoint is null)
            return Results.NotFound();
        db.WebhookEndpoints.Remove(endpoint);
        await db.WebhookDeliveries.Where(d => d.EndpointId == id).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    [Transactional(typeof(AuditDbContext))]
    [WolverineGet("/api/webhooks/{id}/deliveries")]
    public static async Task<IResult> Deliveries(
        Guid id,
        AuditDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (!await Allowed(accessor, scopes, ct))
            return Results.Unauthorized();
        var rows = await db
            .WebhookDeliveries.Where(d => d.EndpointId == id)
            .OrderByDescending(d => d.OccurredAt)
            .Take(50)
            .Select(d => new
            {
                d.EventName,
                d.Attempt,
                d.StatusCode,
                d.Ok,
                d.OccurredAt,
            })
            .ToListAsync(ct);
        return Results.Ok(rows);
    }

    /// <summary>A signed test delivery, so integrators can verify plumbing before real events.</summary>
    [Transactional(typeof(AuditDbContext))]
    [WolverinePost("/api/webhooks/{id}/ping")]
    public static async Task<IResult> Ping(
        Guid id,
        AuditDbContext db,
        IMessageBus bus,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (accessor.Current is not Principal.User { ActiveOrg: { } org })
            return Results.Unauthorized();
        if (!await Allowed(accessor, scopes, ct))
            return Results.Unauthorized();
        if (!await db.WebhookEndpoints.AnyAsync(e => e.Id == id, ct))
            return Results.NotFound();
        await bus.PublishAsync(
            new DeliverWebhook(
                id,
                Guid.CreateVersion7(),
                "webhook.ping",
                "{}",
                DateTimeOffset.UtcNow,
                Attempt: 1
            ),
            new DeliveryOptions { TenantId = org.Value.ToString() }
        );
        return Results.Accepted();
    }

    private static async ValueTask<bool> Allowed(
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    ) => await scopes.CanAsync(accessor.Current, Capabilities.OrgManage, ct);
}
