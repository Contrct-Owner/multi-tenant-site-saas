using Premise.Modules.Audit.Data;
using Premise.Platform.Entitlements;
using Premise.Platform.Kernel;
using Wolverine;

namespace Premise.Modules.Audit;

public static class WebhookDispatch
{
    public const string Code = "webhook.delivery";
    public const int MaxEndpoints = 20;

    public static async Task<bool> EnqueueAsync(
        AuditDbContext db,
        OrgId org,
        DeliverWebhook message,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (
            !await CapacityReservations.TryLockAsync(db, org, Code, ct)
            || await CapacityReservations.PendingAsync(db, org, Code, ct) >= 100
        )
            return false;
        var admission = Guid.CreateVersion7();
        await CapacityReservations.ReserveAsync(db, org, Code, message.EndpointId, [admission], ct);
        await bus.PublishAsync(
            message with
            {
                AdmissionId = admission,
            },
            new DeliveryOptions { TenantId = org.Value.ToString() }
        );
        return true;
    }
}
