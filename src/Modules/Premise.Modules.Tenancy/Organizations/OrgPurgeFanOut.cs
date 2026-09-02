using Premise.Contracts;
using Premise.Platform.Kernel;
using Premise.Platform.Messaging;
using Wolverine;

namespace Premise.Modules.Tenancy.Organizations;

/// <summary>
/// The org-deletion fan-out, in ONE place. It used to be copied into the
/// operator offboarding endpoint and the closure sweep, and the copies were
/// already drifting risk: a module added after both was purged by neither.
/// One command per owning module keeps each Wolverine chain single-DbContext.
/// </summary>
public static class OrgPurgeFanOut
{
    public static async Task PublishAsync(IMessageBus bus, OrgId org, string? externalId)
    {
        await bus.PublishForOrgAsync(org, new PurgeOrgSites());
        await bus.PublishForOrgAsync(org, new PurgeOrgFiles());
        await bus.PublishForOrgAsync(org, new PurgeOrgEntitlements());
        await bus.PublishForOrgAsync(org, new PurgeOrgIngest());
        await bus.PublishForOrgAsync(org, new PurgeOrgWebhooks());
        await bus.PublishForOrgAsync(org, new PurgeOrgChecklists());
        await bus.PublishForOrgAsync(org, new OrganizationDeleted(org, externalId));
    }
}
