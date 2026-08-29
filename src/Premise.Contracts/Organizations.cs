using Premise.Platform.Kernel;

namespace Premise.Contracts;

/// <summary>Cross-module read contract implemented by the Tenancy module.</summary>
public interface IOrganizationLookup
{
    Task<OrgSummary?> FindBySlugAsync(string slug, CancellationToken ct = default);
    Task<OrgSummary?> FindByExternalIdAsync(string externalId, CancellationToken ct = default);
    Task<OrgSummary?> GetAsync(OrgId id, CancellationToken ct = default);

    /// <summary>All org ids - for platform enumerators fanning out per-org work (ADR 24).</summary>
    Task<IReadOnlyList<OrgId>> ListIdsAsync(CancellationToken ct = default);
}

public sealed record OrgSummary(
    OrgId Id,
    string Name,
    string Slug,
    RegionId Region,
    string? ExternalId
);

/// <summary>
/// Cross-module contract: the module OWNING a limited resource reports current
/// usage so the entitlements module can run downgrade preflight (ADR 11)
/// without reaching into another module's tables.
/// </summary>
public interface IEntitlementUsageProbe
{
    /// <summary>The limit-shaped entitlement code this probe measures.</summary>
    string Code { get; }

    ValueTask<long> CurrentUsageAsync(OrgId org, CancellationToken ct = default);
}
