using Microsoft.EntityFrameworkCore;
using Premise.Platform.Infra;

namespace Premise.Api;

/// <summary>
/// Hard-deletes expired idempotency rows (ADR 29; the expired_cleanup RLS
/// policy authorizes exactly this cross-org delete) and prunes sweep leases
/// older than a month - the two platform tables with a TTL.
/// </summary>
public static class CleanupIdempotencyHandler
{
    public static async Task Handle(
        CleanupIdempotency _,
        PlatformDbContext db,
        CancellationToken ct
    )
    {
        var expired = DateTimeOffset.UtcNow.AddHours(-24);
        await db.IdempotencyRecords.Where(r => r.CreatedAt < expired).ExecuteDeleteAsync(ct);
        var stale = DateTimeOffset.UtcNow.AddDays(-30);
        await db.SweepRuns.Where(r => r.Period < stale).ExecuteDeleteAsync(ct);
    }
}
