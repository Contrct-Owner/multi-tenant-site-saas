using Microsoft.EntityFrameworkCore;
using Premise.Contracts;
using Premise.Platform.Kernel;
using Wolverine;

namespace Premise.Api;

/// <summary>
/// Development-only boot: apply every module's migrations and seed the dev
/// org/user matched to the WorkOS emulator's pinned ids
/// (workos-emulate.config.yaml), so `aspire run` on a fresh clone gives a
/// working login. Never registered outside Development.
/// </summary>
public sealed class DevBootstrap(
    IServiceProvider services,
    ReadinessState readiness,
    ILogger<DevBootstrap> logger
) : BackgroundService
{
    public const string EmulatorUserId = "user_01DEVALICE00000000000000";
    public const string EmulatorOrgId = "org_01DEVACME000000000000000";
    public const string EmulatorOperatorId = "user_01DEVOPERATOR0000000000";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await RunAsync(stoppingToken);
                readiness.MarkReady();
                logger.LogInformation("dev bootstrap complete");
                return;
            }
            catch (Exception e) when (attempt < 30 && !stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("dev bootstrap waiting on dependencies ({Error})", e.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        await sp.GetRequiredService<Premise.Modules.Tenancy.Data.TenancyDbContext>()
            .Database.MigrateAsync(ct);
        await sp.GetRequiredService<Premise.Modules.Identity.Data.IdentityDbContext>()
            .Database.MigrateAsync(ct);
        await sp.GetRequiredService<Premise.Modules.Entitlements.Data.EntitlementsDbContext>()
            .Database.MigrateAsync(ct);
        await sp.GetRequiredService<Premise.Modules.Audit.Data.AuditDbContext>()
            .Database.MigrateAsync(ct);
        await sp.GetRequiredService<Premise.Modules.Storage.Data.StorageDbContext>()
            .Database.MigrateAsync(ct);
        await sp.GetRequiredService<Premise.Modules.Ingest.Data.IngestDbContext>()
            .Database.MigrateAsync(ct);
        await sp.GetRequiredService<Premise.Platform.Infra.PlatformDbContext>()
            .Database.MigrateAsync(ct);

        // seed keyed to the emulator's PINNED ids - login just matches
        var tenancy = sp.GetRequiredService<Premise.Modules.Tenancy.Data.TenancyDbContext>();
        var org = await tenancy.Organizations.FirstOrDefaultAsync(o => o.Slug == "acme-dev", ct);
        if (org is null)
        {
            org = new Premise.Modules.Tenancy.Organizations.Organization
            {
                Id = OrgId.New(),
                Name = "Acme Dev",
                Slug = "acme-dev",
                Region = RegionId.Default,
                ExternalId = EmulatorOrgId,
            };
            tenancy.Organizations.Add(org);
            await tenancy.SaveChangesAsync(ct);
        }

        var identity = sp.GetRequiredService<Premise.Modules.Identity.Data.IdentityDbContext>();
        if (!await identity.Users.AnyAsync(u => u.Subject == EmulatorUserId, ct))
        {
            var user = Premise.Modules.Identity.Users.AppUser.Create(
                "workos",
                EmulatorUserId,
                "alice@acme.test",
                "Alice Dev"
            );
            var membership = Premise.Modules.Identity.Users.Membership.Create(user.Id, org.Id);
            var owner = Premise.Modules.Identity.Access.Role.Create(org.Id, "Owner");
            identity.Users.Add(user);
            identity.Memberships.Add(membership);
            identity.Roles.Add(owner);
            identity.RoleGrants.Add(
                new Premise.Modules.Identity.Access.RoleGrant
                {
                    Id = Guid.CreateVersion7(),
                    OrgId = org.Id,
                    RoleId = owner.Id,
                    Domain = "*",
                    Action = "*",
                }
            );
            identity.MembershipRoles.Add(
                new Premise.Modules.Identity.Access.MembershipRole
                {
                    Id = Guid.CreateVersion7(),
                    OrgId = org.Id,
                    MembershipId = membership.Id,
                    RoleId = owner.Id,
                    ScopePath = null,
                }
            );
            await identity.SaveChangesAsync(ct);
        }

        // the vendor's own org: operators live here (platform:operate)
        var platformOrg = await tenancy.Organizations.FirstOrDefaultAsync(
            o => o.Slug == "premise-ops",
            ct
        );
        if (platformOrg is null)
        {
            platformOrg = new Premise.Modules.Tenancy.Organizations.Organization
            {
                Id = OrgId.New(),
                Name = "Premise Operations",
                Slug = "premise-ops",
                Region = RegionId.Default,
                IsPlatform = true,
            };
            tenancy.Organizations.Add(platformOrg);
            await tenancy.SaveChangesAsync(ct);
        }
        if (!await identity.Users.AnyAsync(u => u.Subject == EmulatorOperatorId, ct))
        {
            var operatorUser = Premise.Modules.Identity.Users.AppUser.Create(
                "workos",
                EmulatorOperatorId,
                "operator@premise.local",
                "Premise Operator"
            );
            var operatorMembership = Premise.Modules.Identity.Users.Membership.Create(
                operatorUser.Id,
                platformOrg.Id
            );
            var operatorRole = Premise.Modules.Identity.Access.Role.Create(
                platformOrg.Id,
                "Operator"
            );
            identity.Users.Add(operatorUser);
            identity.Memberships.Add(operatorMembership);
            identity.Roles.Add(operatorRole);
            identity.RoleGrants.Add(
                new Premise.Modules.Identity.Access.RoleGrant
                {
                    Id = Guid.CreateVersion7(),
                    OrgId = platformOrg.Id,
                    RoleId = operatorRole.Id,
                    Domain = "*",
                    Action = "*",
                }
            );
            identity.MembershipRoles.Add(
                new Premise.Modules.Identity.Access.MembershipRole
                {
                    Id = Guid.CreateVersion7(),
                    OrgId = platformOrg.Id,
                    MembershipId = operatorMembership.Id,
                    RoleId = operatorRole.Id,
                    ScopePath = null,
                }
            );
            await identity.SaveChangesAsync(ct);
        }

        // what every org-writing flow does: publish the event (org_directory)
        var bus = sp.GetRequiredService<IMessageBus>();
        await bus.PublishAsync(
            new OrganizationUpserted(org.Id, org.Name, org.Slug, org.Region, org.ExternalId)
        );
        await bus.PublishAsync(
            new OrganizationUpserted(
                platformOrg.Id,
                platformOrg.Name,
                platformOrg.Slug,
                platformOrg.Region,
                platformOrg.ExternalId,
                "Active",
                IsPlatform: true
            )
        );
    }
}
