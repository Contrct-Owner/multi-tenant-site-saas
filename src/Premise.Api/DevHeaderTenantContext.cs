using Premise.Platform.Kernel;

namespace Premise.Api;

/// <summary>
/// STEP-1 PLACEHOLDER (ADR 14): tenant resolved lazily from the X-Org-Id
/// header. Lazy on purpose - Wolverine's transactional middleware opens the
/// database connection before any request middleware could have populated a
/// set-once context, so the org must be answerable whenever the RLS
/// interceptor asks, not at a fixed point in the pipeline. Step 2 replaces
/// this with real principal resolution (session cookie -> principal); the
/// same read-time rule applies there. Production boot is blocked in Program.
/// </summary>
public sealed class DevHeaderTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    public OrgId? OrgId =>
        accessor.HttpContext?.Request.Headers.TryGetValue("X-Org-Id", out var raw) == true
        && Guid.TryParse(raw, out var orgGuid)
            ? new OrgId(orgGuid)
            : null;

    public RegionId Region => RegionId.Default;
}
