using Premise.Modules.Identity.Auth;
using Premise.Platform.Kernel;

namespace Premise.Api;

/// <summary>
/// Read-time principal resolution (step-1 lesson): singleton over
/// IHttpContextAccessor so the answer is available from any DI scope at any
/// point in the pipeline - including when Wolverine's transactional middleware
/// opens a connection before request middleware has run. Reads claims and
/// request state only; never the database (guest org is materialized into
/// HttpContext.Items by GuestOrgMiddleware, which owns the one DB lookup).
/// </summary>
public sealed class RequestPrincipalAccessor(IHttpContextAccessor accessor) : IPrincipalAccessor
{
    public const string GuestOrgItem = "premise.guest_org";

    public Principal Current
    {
        get
        {
            var http = accessor.HttpContext;
            if (http is null)
                return new Principal.Guest(null);

            var user = http.User;
            if (
                user.Identity?.IsAuthenticated == true
                && user.FindFirst(PremiseClaims.Tier)?.Value == "contact"
                && Guid.TryParse(user.FindFirst("premise:contact_id")?.Value, out var contactId)
                && Guid.TryParse(user.FindFirst(PremiseClaims.ActiveOrg)?.Value, out var contactOrg)
            )
            {
                return new Principal.Contact(contactId, new OrgId(contactOrg));
            }

            if (
                user.Identity?.IsAuthenticated == true
                && Guid.TryParse(user.FindFirst(PremiseClaims.UserId)?.Value, out var userId)
            )
            {
                OrgId? activeOrg = Guid.TryParse(
                    user.FindFirst(PremiseClaims.ActiveOrg)?.Value,
                    out var orgGuid
                )
                    ? new OrgId(orgGuid)
                    : null;
                return new Principal.User(
                    userId,
                    user.FindFirst(PremiseClaims.Email)?.Value ?? "",
                    user.FindFirst(PremiseClaims.DisplayName)?.Value,
                    activeOrg
                );
            }

            return new Principal.Guest(
                http.Items.TryGetValue(GuestOrgItem, out var v) && v is OrgId org ? org : null
            );
        }
    }
}

/// <summary>ITenantContext backed by the resolved principal (ADR 5/7).</summary>
public sealed class PrincipalTenantContext(IPrincipalAccessor accessor) : ITenantContext
{
    public OrgId? OrgId =>
        accessor.Current switch
        {
            Principal.User u => u.ActiveOrg,
            Principal.Contact c => c.Org,
            Principal.Guest g => g.Org,
            _ => null,
        };

    public RegionId Region => RegionId.Default; // org->region routing lands with ADR 35 step two
}
