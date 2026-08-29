using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Premise.Contracts;
using Premise.Modules.Audit.Data;
using Premise.Platform.Audit;
using Premise.Platform.Kernel;
using Wolverine;

namespace Premise.Modules.Audit;

/// <summary>Per-org purge: audit.retention_days (tiered entitlement) finally gets its consumer.</summary>
public sealed record PurgeAuditData;

public static class PurgeAuditDataHandler
{
    [Wolverine.Attributes.Transactional(typeof(AuditDbContext))]
    public static async Task Handle(
        PurgeAuditData _,
        Envelope envelope,
        ITenantContext tenant,
        AuditDbContext db,
        IAuditPolicyProvider policies,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"PurgeAuditData arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );

        var policy = await policies.GetAsync(org, ct);
        var cutoff = time.GetUtcNow().AddDays(-policy.RetentionDays);
        await db.Changes.Where(a => a.OccurredAt < cutoff).ExecuteDeleteAsync(ct);
        await db.DomainEvents.Where(a => a.OccurredAt < cutoff).ExecuteDeleteAsync(ct);
        await db.AuthzDecisions.Where(a => a.OccurredAt < cutoff).ExecuteDeleteAsync(ct);
        await db.Accesses.Where(a => a.OccurredAt < cutoff).ExecuteDeleteAsync(ct);
    }
}

/// <summary>Daily enumerator (ADR 24 fan-out, same shape as the horizon roll and meter compaction).</summary>
public sealed class AuditRetentionService(IServiceProvider services) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        try
        {
            do
            {
                await using var scope = services.CreateAsyncScope();
                var orgs = scope.ServiceProvider.GetRequiredService<IOrganizationLookup>();
                var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                foreach (var orgId in await orgs.ListIdsAsync(stoppingToken))
                    await bus.PublishAsync(
                        new PurgeAuditData(),
                        new DeliveryOptions { TenantId = orgId.Value.ToString() }
                    );
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { } // shutdown
    }
}
