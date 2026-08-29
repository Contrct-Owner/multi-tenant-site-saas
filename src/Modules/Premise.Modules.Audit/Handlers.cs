using Microsoft.EntityFrameworkCore;
using Premise.Contracts;
using Premise.Modules.Audit.Data;
using Premise.Platform.Kernel;
using Wolverine;
using Wolverine.Attributes;

namespace Premise.Modules.Audit;

/// <summary>
/// The async halves of ADR 13: domain/authz entries arrive via the durable
/// outbox (transactional with their cause when published inside one); access
/// entries ride the same durable queue but are pure fire-and-forget volume.
/// Tenant rides the envelope (ADR 24) and is read lazily.
/// </summary>
public static class RecordDomainAuditHandler
{
    [Transactional]
    public static async Task Handle(
        RecordDomainAudit message,
        Envelope envelope,
        ITenantContext tenant,
        AuditDbContext db,
        CancellationToken ct
    )
    {
        db.DomainEvents.Add(
            new DomainLogEntry
            {
                Id = Guid.CreateVersion7(),
                OrgId = RequireOrg(tenant, envelope),
                ActorTier = envelope.Headers.GetValueOrDefault("premise-actor-tier") ?? "system",
                ActorId = ParseActor(envelope),
                EventName = message.EventName,
                Payload = message.PayloadJson,
                OccurredAt = envelope.SentAt,
            }
        );
        await db.SaveChangesAsync(ct);
    }

    private static Guid RequireOrg(ITenantContext tenant, Envelope envelope) =>
        tenant.OrgId?.Value
        ?? throw new InvalidOperationException(
            $"audit message arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
        );

    private static Guid? ParseActor(Envelope envelope) =>
        Guid.TryParse(envelope.Headers.GetValueOrDefault("premise-actor-id"), out var id)
            ? id
            : null;
}

public static class RecordAuthzAuditHandler
{
    [Transactional]
    public static async Task Handle(
        RecordAuthzAudit message,
        Envelope envelope,
        ITenantContext tenant,
        AuditDbContext db,
        CancellationToken ct
    )
    {
        db.AuthzDecisions.Add(
            new AuthzLogEntry
            {
                Id = Guid.CreateVersion7(),
                OrgId = RequireOrg(tenant, envelope),
                ActorTier = envelope.Headers.GetValueOrDefault("premise-actor-tier") ?? "system",
                ActorId = ParseActor(envelope),
                Action = message.Action,
                Outcome = message.Outcome,
                ScopeSummary = message.ScopeSummary,
                OccurredAt = envelope.SentAt,
            }
        );
        await db.SaveChangesAsync(ct);
    }

    private static Guid RequireOrg(ITenantContext tenant, Envelope envelope) =>
        tenant.OrgId?.Value
        ?? throw new InvalidOperationException(
            $"audit message arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
        );

    private static Guid? ParseActor(Envelope envelope) =>
        Guid.TryParse(envelope.Headers.GetValueOrDefault("premise-actor-id"), out var id)
            ? id
            : null;
}

public static class RecordAccessAuditHandler
{
    [Transactional]
    public static async Task Handle(
        RecordAccessAudit message,
        Envelope envelope,
        ITenantContext tenant,
        AuditDbContext db,
        CancellationToken ct
    )
    {
        db.Accesses.Add(
            new AccessLogEntry
            {
                Id = Guid.CreateVersion7(),
                OrgId = RequireOrg(tenant, envelope),
                ActorTier = envelope.Headers.GetValueOrDefault("premise-actor-tier") ?? "system",
                ActorId = ParseActor(envelope),
                Method = message.Method,
                Path = message.Path,
                StatusCode = message.StatusCode,
                OccurredAt = envelope.SentAt,
            }
        );
        await db.SaveChangesAsync(ct);
    }

    private static Guid RequireOrg(ITenantContext tenant, Envelope envelope) =>
        tenant.OrgId?.Value
        ?? throw new InvalidOperationException(
            $"audit message arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
        );

    private static Guid? ParseActor(Envelope envelope) =>
        Guid.TryParse(envelope.Headers.GetValueOrDefault("premise-actor-id"), out var id)
            ? id
            : null;
}
