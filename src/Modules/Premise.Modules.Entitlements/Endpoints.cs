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
}
