using Premise.Platform.Kernel;

namespace Premise.Contracts;

/// <summary>Cross-module read contract implemented by the Tenancy module.</summary>
public interface IOrganizationLookup
{
    Task<OrgSummary?> FindBySlugAsync(string slug, CancellationToken ct = default);
    Task<OrgSummary?> FindByExternalIdAsync(string externalId, CancellationToken ct = default);
    Task<OrgSummary?> GetAsync(OrgId id, CancellationToken ct = default);
}

public sealed record OrgSummary(
    OrgId Id,
    string Name,
    string Slug,
    RegionId Region,
    string? ExternalId
);
