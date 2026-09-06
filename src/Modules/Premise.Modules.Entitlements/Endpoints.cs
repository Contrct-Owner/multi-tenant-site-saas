using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
    [Transactional(typeof(EntitlementsDbContext))]
    [WolverineGet("/api/entitlements")]
    [ProducesResponseType(typeof(Dictionary<string, EntitlementSummary>), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        IPrincipalAccessor accessor,
        EntitlementsService service,
        IEnumerable<IEntitlementUsageProbe> probes,
        CancellationToken ct
    )
    {
        if (accessor.Current is not Principal.User { ActiveOrg: { } org })
            return Results.Unauthorized();
        return Results.Ok(await service.DescribeAllAsync(org, probes, ct));
    }
}
