using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Premise.Contracts;
using Premise.Modules.Tenancy.Data;
using Premise.Platform.Auth;
using Premise.Platform.Kernel;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace Premise.Modules.Tenancy.Organizations;

public sealed record CreateOrgRequest(string Name, string Slug);

/// <summary>
/// Day-zero onboarding (the tenant-lifecycle front half): any authenticated
/// user may create an org. The provider directory capability (WorkOS) gets
/// the org created on its side too; Identity provisions the founder via the
/// outbox; OrganizationUpserted feeds every read model.
/// </summary>
public static class OnboardingEndpoints
{
    [Transactional(typeof(TenancyDbContext))]
    [WolverinePost("/api/orgs")]
    public static async Task<IResult> Create(
        CreateOrgRequest request,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IAuthProvider provider,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (accessor.Current is not Principal.User { UserId: var userId })
            return Results.Unauthorized();

        var slug = request.Slug.Trim().ToLowerInvariant();
        if (
            slug.Length is < 3 or > 60
            || !slug.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')
        )
            return Results.BadRequest(
                new { error = "slug must be 3-60 chars of lowercase letters, digits, and dashes" }
            );
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest(new { error = "name is required" });
        if (await db.Organizations.AnyAsync(o => o.Slug == slug, ct))
            return Results.Conflict(new { error = $"slug '{slug}' is taken" });

        // provider org first (WorkOS as much as possible): invitations and
        // SSO hang off it. Absent the capability (bare OIDC), ExternalId
        // stays null and everything else still works.
        string? externalId = null;
        if (provider is IOrganizationDirectory directory)
            externalId = await directory.CreateOrganizationAsync(request.Name, ct);

        var org = new Organization
        {
            Id = OrgId.New(),
            Name = request.Name.Trim(),
            Slug = slug,
            Region = RegionId.Default,
            ExternalId = externalId,
        };
        db.Organizations.Add(org);
        await db.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new OrganizationUpserted(org.Id, org.Name, org.Slug, org.Region, org.ExternalId)
        );
        await bus.PublishAsync(
            new ProvisionFounderMembership(userId, org.Id),
            new DeliveryOptions { TenantId = org.Id.Value.ToString() }
        );
        await bus.PublishAsync(
            new RecordDomainAudit(
                "org.created",
                System.Text.Json.JsonSerializer.Serialize(new { org.Name, org.Slug })
            ),
            new DeliveryOptions
            {
                TenantId = org.Id.Value.ToString(),
                Headers =
                {
                    ["premise-actor-tier"] = "user",
                    ["premise-actor-id"] = userId.ToString(),
                },
            }
        );
        return Results.Ok(new { orgId = org.Id.Value, org.Slug });
    }
}
