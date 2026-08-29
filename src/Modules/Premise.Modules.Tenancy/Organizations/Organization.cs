using Premise.Platform.Kernel;

namespace Premise.Modules.Tenancy.Organizations;

/// <summary>
/// Deletion tier 1 (ADR 25): lifecycle status, never deleted. Platform-global
/// table - it is the org anchor itself, so it carries no RLS (allowlisted in
/// the CI coverage assertion). Region names the silo the org's data lives in
/// (ADR 35); identity stays global, org data stays regional.
/// </summary>
public sealed class Organization
{
    public required OrgId Id { get; init; }
    public required string Name { get; set; }
    public required string Slug { get; init; }
    public required RegionId Region { get; init; }

    /// <summary>Auth-provider org id (e.g. WorkOS org) for SSO mapping (ADR 14).</summary>
    public string? ExternalId { get; set; }

    public OrganizationStatus Status { get; set; } = OrganizationStatus.Active;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public enum OrganizationStatus
{
    Active,
    Suspended,
    Offboarding,
}
