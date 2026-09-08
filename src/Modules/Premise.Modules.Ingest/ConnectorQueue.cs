using Microsoft.EntityFrameworkCore;
using Premise.Modules.Ingest.Data;
using Premise.Platform.Entitlements;
using Premise.Platform.Kernel;
using Wolverine;

namespace Premise.Modules.Ingest;

/// <summary>One durable outstanding sync per connector and at most five per organization.</summary>
public static class ConnectorQueue
{
    public const string Code = "ingest.sync";

    public static async Task<bool> EnqueueAsync(
        IngestDbContext db,
        OrgId org,
        Guid connectorId,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (!await CapacityReservations.TryLockAsync(db, org, Code, ct))
            return false;
        if (
            await db
                .Database.SqlQuery<bool>(
                    $"SELECT EXISTS(SELECT 1 FROM platform.capacity_reservations WHERE org_id = {org.Value} AND code = {Code} AND id = {connectorId}) AS \"Value\""
                )
                .SingleAsync(ct)
            || await CapacityReservations.PendingAsync(db, org, Code, ct) >= 5
        )
            return false;
        var admission = Guid.CreateVersion7();
        await CapacityReservations.ReserveAsync(db, org, Code, admission, [connectorId], ct);
        await bus.PublishAsync(
            new SyncSiteConnector(connectorId, admission),
            new DeliveryOptions { TenantId = org.Value.ToString() }
        );
        return true;
    }
}
