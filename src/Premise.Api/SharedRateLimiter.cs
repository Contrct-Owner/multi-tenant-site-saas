using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Premise.Platform.Data;
using Premise.Platform.Infra;
using Premise.Platform.Kernel;

namespace Premise.Api;

/// <summary>
/// A fixed-window limiter whose counter lives in Postgres (ADR 52), so a
/// partition's quota is one number across every api replica. Each acquire is
/// one upsert on <c>platform.rate_windows</c> returning the window's count;
/// the row is the lock. Fails OPEN: if the counter cannot be reached the
/// request proceeds and the failure is logged once per window - the database
/// being down already fails every request that needs it, and a limiter must
/// never be the reason a healthy request is refused. Per-instance limiters
/// (guests, IPs) stay in memory: abuse control, not a quota anyone is sold.
/// </summary>
public sealed class SharedRateLimiter(
    string partition,
    int permitLimit,
    TimeSpan window,
    IRegionDataSources dataSources,
    TimeProvider time,
    ILogger logger
) : RateLimiter
{
    private DateTimeOffset _lastAcquire = time.GetUtcNow();
    private DateTimeOffset _lastFailureLogged = DateTimeOffset.MinValue;

    public override TimeSpan? IdleDuration => time.GetUtcNow() - _lastAcquire;

    public override RateLimiterStatistics? GetStatistics() => null;

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        // the chained partitioned limiter takes the synchronous path first;
        // the counter is the same either way
        var (windowStart, now) = Window();
        try
        {
            using var connection = dataSources.For(RegionId.Default).OpenConnection();
            using var command = Upsert(connection, windowStart, permitCount);
            return Decide(Convert.ToInt64(command.ExecuteScalar()), windowStart, now);
        }
        catch (Exception exception)
        {
            return Open(exception, now);
        }
    }

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(
        int permitCount,
        CancellationToken cancellationToken
    )
    {
        var (windowStart, now) = Window();
        try
        {
            await using var connection = await dataSources
                .For(RegionId.Default)
                .OpenConnectionAsync(cancellationToken);
            await using var command = Upsert(connection, windowStart, permitCount);
            return Decide(
                Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)),
                windowStart,
                now
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Open(exception, now);
        }
    }

    private (DateTimeOffset windowStart, DateTimeOffset now) Window()
    {
        var now = time.GetUtcNow();
        _lastAcquire = now;
        return (
            new DateTimeOffset(now.UtcTicks - (now.UtcTicks % window.Ticks), TimeSpan.Zero),
            now
        );
    }

    /// <summary>
    /// A window is sixteen rows, not one: every request for an org used to
    /// upsert the same row and wait on the previous writer's lock, and the
    /// bench found that wait to be the database's whole ceiling (15 ms per
    /// request at 32 concurrent). Each request bumps one shard and reads the
    /// window's sum; the CTE's own increment is not in the statement's
    /// snapshot, so it is added back. Fixed windows are approximate by
    /// design; the sum is exact between concurrent bumps.
    /// </summary>
    private const int Shards = 16;

    private static readonly string[] ShardSuffixes = Enumerable
        .Range(0, Shards)
        .Select(i => $"#{i:D2}")
        .ToArray();

    private NpgsqlCommand Upsert(
        NpgsqlConnection connection,
        DateTimeOffset windowStart,
        int permits
    )
    {
        var command = new NpgsqlCommand(
            """
            WITH bump AS (
                INSERT INTO platform.rate_windows (partition, window_start, count)
                VALUES ($1, $2, $3)
                ON CONFLICT (partition, window_start)
                DO UPDATE SET count = platform.rate_windows.count + EXCLUDED.count
                RETURNING 1
            )
            SELECT coalesce(sum(count), 0) + $3
            FROM platform.rate_windows
            WHERE window_start = $2 AND partition = ANY($4)
            """,
            connection
        );
        var shard = Random.Shared.Next(Shards);
        command.Parameters.Add(new NpgsqlParameter { Value = partition + ShardSuffixes[shard] });
        command.Parameters.Add(new NpgsqlParameter { Value = windowStart });
        command.Parameters.Add(new NpgsqlParameter { Value = permits });
        command.Parameters.Add(
            new NpgsqlParameter { Value = ShardSuffixes.Select(x => partition + x).ToArray() }
        );
        return command;
    }

    private RateLimitLease Decide(long count, DateTimeOffset windowStart, DateTimeOffset now) =>
        count <= permitLimit ? new Lease(true, null) : new Lease(false, windowStart + window - now);

    /// <summary>Fail open, and say so once per window.</summary>
    private RateLimitLease Open(Exception exception, DateTimeOffset now)
    {
        if (now - _lastFailureLogged > window)
        {
            _lastFailureLogged = now;
            logger.LogWarning(
                exception,
                "rate counter unreachable for {Partition}; allowing the request",
                partition
            );
        }
        return new Lease(true, null);
    }

    private sealed class Lease(bool acquired, TimeSpan? retryAfter) : RateLimitLease
    {
        public override bool IsAcquired => acquired;

        public override IEnumerable<string> MetadataNames =>
            retryAfter is null ? [] : [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (metadataName == MetadataName.RetryAfter.Name && retryAfter is { } after)
            {
                metadata = after;
                return true;
            }
            metadata = null;
            return false;
        }
    }
}
