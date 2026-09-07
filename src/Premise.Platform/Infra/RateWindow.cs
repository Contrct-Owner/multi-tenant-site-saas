namespace Premise.Platform.Infra;

/// <summary>
/// One row per (partition, minute): the request count a rate-limit partition
/// has used in that fixed window, shared by every api replica (ADR 52). A
/// limiter in process memory gives a fleet of N replicas N times the quota;
/// this row is the one counter they all increment. Platform upkeep, not
/// tenant data: no org column (the partition names the principal), no RLS.
/// Deletion tier 3: windows older than ten minutes are hard-deleted by the
/// idempotency cleanup sweep.
/// </summary>
public sealed class RateWindow
{
    /// <summary>"org:{id}:{limit}", "user:{id}", "key:{id}" - the partition the request fell in.</summary>
    public required string Partition { get; init; }

    /// <summary>UTC instant (ADR 26): the start of the one-minute window, aligned to the epoch.</summary>
    public required DateTimeOffset WindowStart { get; init; }

    public required int Count { get; set; }
}
