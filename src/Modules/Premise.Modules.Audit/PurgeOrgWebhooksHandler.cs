using Microsoft.EntityFrameworkCore;
using Premise.Contracts;
using Premise.Modules.Audit.Data;
using Premise.Platform.Kernel;
using Wolverine.Attributes;

namespace Premise.Modules.Audit;

/// <summary>
/// The audit module deliberately keeps the org's TRAIL after offboarding -
/// but webhook endpoints and their delivery log are configuration, and
/// configuration goes with the org.
/// </summary>
public static class PurgeOrgWebhooksHandler
{
    [Transactional]
    public static async Task Handle(
        PurgeOrgWebhooks _,
        AuditDbContext db,
        ITenantContext tenant,
        CancellationToken ct
    )
    {
        var org =
            tenant.OrgId
            ?? throw new InvalidOperationException("purge arrived with no tenant on the envelope");
        await db
            .WebhookDeliveries.IgnoreQueryFilters()
            .Where(d => d.OrgId == org)
            .ExecuteDeleteAsync(ct);
        await db
            .WebhookEndpoints.IgnoreQueryFilters()
            .Where(e => e.OrgId == org)
            .ExecuteDeleteAsync(ct);
    }
}
