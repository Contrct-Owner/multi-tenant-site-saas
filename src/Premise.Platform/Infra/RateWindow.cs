namespace Premise.Platform.Infra;

/// <summary>
/// One row per (partition shard, minute): a rate-limit partition's count in
/// that fixed window is the sum of its sixteen shards, shared by every api
/// replica (ADR 52). A limiter in process memory gives a fleet of N
/// replicas N times the quota; these rows are the one counter they all
/// increment. Sharded because one row per partition serialised every
/// request of an org on one row lock - the bench found that lock to be the
/// database's whole ceiling. Platform upkeep, not tenant data: no org
/// column (the partition names the principal), no RLS.
/// Deletion tier 3: windows older than ten minutes are hard-deleted by the
/// idempotency cleanup sweep.
/// </summary>
public sealed class RateWindow
{
    /// <summary>"org:{id}:{limit}#07", "user:{id}#12", "key:{id}#00" - the partition the request fell in, and its shard.</summary>
    public required string Partition { get; init; }

    /// <summary>UTC instant (ADR 26): the start of the one-minute window, aligned to the epoch.</summary>
    public required DateTimeOffset WindowStart { get; init; }

    public required int Count { get; set; }
}
