using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Premise.Contracts;
using Premise.Modules.Entitlements.Data;
using Premise.Platform.Entitlements;
using Premise.Platform.Kernel;
using Wolverine.Attributes;
using Wolverine.Http;

namespace Premise.Modules.Entitlements;

public sealed record SetEntitlementRequest(string Value);

public sealed record AddExceptionRequest(string Value, string Reason, DateTimeOffset ExpiresAt);

public static class EntitlementEndpoints
{
    /// <summary>Effective entitlements for the active org - part of the UI bootstrap.</summary>
    [WolverineGet("/api/entitlements")]
    public static async Task<IResult> List(
        IPrincipalAccessor accessor,
        EntitlementsService service,
        CancellationToken ct
    )
    {
        if (accessor.Current is not Principal.User { ActiveOrg: { } org })
            return Results.Unauthorized();
        var effective = new Dictionary<string, object>();
        foreach (var (code, descriptor) in EntitlementCatalog.Definitions)
            effective[code] = new
            {
                value = await service.EffectiveValueAsync(org, code, ct),
                shape = descriptor.Shape.ToString(),
                policy = descriptor.Policy.ToString(),
            };
        return Results.Ok(effective);
    }

    /// <summary>
    /// Assign a value with downgrade preflight (ADR 11): a limit shrinking
    /// below current usage is refused with the exact overage until an admin
    /// remediates - no degraded states anywhere else in the system.
    /// </summary>
    [Transactional(typeof(EntitlementsDbContext))]
    [WolverinePut("/api/admin/entitlements/{code}")]
    public static async Task<IResult> Set(
        string code,
        SetEntitlementRequest request,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        EntitlementsDbContext db,
        IEnumerable<IEntitlementUsageProbe> probes,
        CancellationToken ct
    )
    {
        if (
            accessor.Current is not Principal.User { ActiveOrg: { } org } principal
            || !await scopes.CanAsync(principal, Capabilities.EntitlementsManage, ct)
        )
            return Results.Unauthorized();
        if (!EntitlementCatalog.Definitions.TryGetValue(code, out var descriptor))
            return Results.NotFound();

        if (
            descriptor.Shape is EntitlementShape.Limit
            && probes.FirstOrDefault(p => p.Code == code) is { } probe
        )
        {
            var newLimit = long.Parse(request.Value);
            var current = await probe.CurrentUsageAsync(org, ct);
            if (current > newLimit)
                return Results.Conflict(
                    new
                    {
                        error = "downgrade blocked until conformant",
                        code,
                        currentUsage = current,
                        requestedLimit = newLimit,
                        over = current - newLimit,
                    }
                );
        }

        var row = await db.OrgEntitlements.FirstOrDefaultAsync(
            e => e.OrgId == org && e.Code == code,
            ct
        );
        if (row is null)
        {
            db.OrgEntitlements.Add(
                new OrgEntitlement
                {
                    Id = Guid.CreateVersion7(),
                    OrgId = org,
                    Code = code,
                    Value = request.Value,
                    Source = "manual",
                }
            );
        }
        else
        {
            row.Value = request.Value;
            row.Source = "manual";
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    [Transactional(typeof(EntitlementsDbContext))]
    [WolverinePost("/api/admin/entitlements/{code}/exceptions")]
    public static async Task<IResult> AddException(
        string code,
        AddExceptionRequest request,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        EntitlementsDbContext db,
        CancellationToken ct
    )
    {
        if (
            accessor.Current
                is not Principal.User { ActiveOrg: { } org, UserId: var userId } principal
            || !await scopes.CanAsync(principal, Capabilities.EntitlementsManage, ct)
        )
            return Results.Unauthorized();
        if (!EntitlementCatalog.Definitions.ContainsKey(code))
            return Results.NotFound();
        if (request.ExpiresAt <= DateTimeOffset.UtcNow)
            return Results.BadRequest(new { error = "exceptions must expire in the future" });

        var exception = new EntitlementException
        {
            Id = Guid.CreateVersion7(),
            OrgId = org,
            Code = code,
            Value = request.Value,
            Reason = request.Reason,
            GrantedBy = userId,
            ExpiresAt = request.ExpiresAt,
        };
        db.Exceptions.Add(exception);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { exception.Id, exception.ExpiresAt });
    }
}
