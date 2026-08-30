using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Premise.Contracts;
using Premise.Modules.Identity.Data;
using Premise.Platform.Kernel;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace Premise.Modules.Identity.Access;

public sealed record CreateApiKeyRequest(string Name, Guid RoleId, string? ScopePath = null);

/// <summary>
/// API-key custody (ADR 40): create shows the secret ONCE, the list shows
/// prefixes, revocation is immediate (the resolver consults the row per
/// request). org:manage gated - a key can carry any role, so minting one is
/// an org-level power.
/// </summary>
public static class ApiKeyEndpoints
{
    [Transactional(typeof(IdentityDbContext))]
    [WolverineGet("/api/api-keys")]
    public static async Task<IResult> List(
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (
            accessor.Current is not Principal.User { ActiveOrg: { } org } principal
            || !await scopes.CanAsync(principal, Capabilities.OrgManage, ct)
        )
            return Results.Unauthorized();
        var keys = await (
            from key in db.ApiKeys
            where key.OrgId == org
            join role in db.Roles on key.RoleId equals role.Id
            orderby key.CreatedAt descending
            select new
            {
                key.Id,
                key.Name,
                key.Prefix,
                role = role.Name,
                key.ScopePath,
                key.CreatedAt,
                key.LastUsedAt,
                revoked = key.RevokedAt != null,
            }
        ).ToListAsync(ct);
        return Results.Ok(keys);
    }

    [Transactional(typeof(IdentityDbContext))]
    [WolverinePost("/api/api-keys")]
    public static async Task<IResult> Create(
        CreateApiKeyRequest request,
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
            || !await scopes.CanAsync(principal, Capabilities.OrgManage, ct)
        )
            return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 120)
            return Results.BadRequest(new { error = "name must be 1-120 characters" });
        if (!await db.Roles.AnyAsync(r => r.Id == request.RoleId && r.OrgId == org, ct))
            return Results.NotFound(new { error = "unknown role" });

        var secret =
            "premise_"
            + Convert
                .ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        var key = new ApiKey
        {
            Id = Guid.CreateVersion7(),
            OrgId = org,
            Name = request.Name.Trim(),
            SecretHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))),
            Prefix = secret[..16],
            RoleId = request.RoleId,
            ScopePath = request.ScopePath,
            CreatedBy = userId,
        };
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(ct);
        await PublishAudit(bus, org, userId, "apikey.created", key.Id, key.Name);
        // the one and only time the secret leaves the server
        return Results.Ok(
            new
            {
                key.Id,
                secret,
                key.Prefix,
            }
        );
    }

    [Transactional(typeof(IdentityDbContext))]
    [WolverineDelete("/api/api-keys/{id}")]
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
            || !await scopes.CanAsync(principal, Capabilities.OrgManage, ct)
        )
            return Results.Unauthorized();
        var key = await db.ApiKeys.FirstOrDefaultAsync(
            k => k.Id == id && k.OrgId == org && k.RevokedAt == null,
            ct
        );
        if (key is null)
            return Results.NotFound();
        key.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await PublishAudit(bus, org, userId, "apikey.revoked", key.Id, key.Name);
        return Results.NoContent();
    }

    private static Task PublishAudit(
        IMessageBus bus,
        OrgId org,
        Guid actorId,
        string eventName,
        Guid keyId,
        string name
    ) =>
        bus.PublishAsync(
                new RecordDomainAudit(
                    eventName,
                    System.Text.Json.JsonSerializer.Serialize(new { keyId, name })
                ),
                new DeliveryOptions
                {
                    TenantId = org.Value.ToString(),
                    Headers =
                    {
                        ["premise-actor-tier"] = "user",
                        ["premise-actor-id"] = actorId.ToString(),
                    },
                }
            )
            .AsTask();
}
