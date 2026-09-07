using Premise.Platform.Kernel;

namespace Premise.Contracts;

/// <summary>Tenancy-owned report data. Explicit org and scope are mandatory on every read.</summary>
public interface IReportSiteSource
{
    /// <summary>Returns at most limit+1 entries so callers can reject, never truncate, an oversized selection.</summary>
    Task<IReadOnlyList<Site>> SelectAsync(
        OrgId org,
        NodeScope scope,
        Guid[]? ids,
        int limit,
        CancellationToken ct = default
    );

    Task<IReadOnlyList<Hours>> HoursAsync(
        OrgId org,
        NodeScope scope,
        Guid[] ids,
        DateOnly from,
        DateOnly through,
        CancellationToken ct = default
    );

    public sealed record Site(
        Guid Id,
        string Name,
        string Path,
        string Hierarchy,
        string Status,
        string TimeZone,
        string? Address,
        string? City,
        string? PostalCode,
        string? Country,
        double? Latitude,
        double? Longitude,
        string AttributesJson
    );

    public sealed record Hours(
        Guid SiteId,
        DateOnly LocalDate,
        DateTimeOffset StartsAtUtc,
        DateTimeOffset EndsAtUtc
    );
}
