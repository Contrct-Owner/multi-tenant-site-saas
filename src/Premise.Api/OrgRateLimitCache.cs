using System.Collections.Concurrent;
using Premise.Platform.Entitlements;
using Premise.Platform.Kernel;

namespace Premise.Api;

/// <summary>
/// ADR 30: the per-org request quota reads the api.requests_per_minute
/// entitlement - but rate-limit partitioning is synchronous, so values are
/// cached and refreshed in the background. Until the first resolution an org
/// gets the catalog default; a five-minute TTL bounds staleness after a plan
/// change.
/// </summary>
public sealed class OrgRateLimitCache(IServiceScopeFactory scopeFactory)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<OrgId, (int limit, DateTimeOffset at)> _cache = new();
    private readonly ConcurrentDictionary<OrgId, bool> _refreshing = new();

    public int LimitFor(OrgId org)
    {
        if (_cache.TryGetValue(org, out var entry) && DateTimeOffset.UtcNow - entry.at < Ttl)
            return entry.limit;
        if (_refreshing.TryAdd(org, true))
            _ = RefreshAsync(org);
        return entry.limit > 0
            ? entry.limit
            : (int)
                EntitlementCatalog
                    .Definitions[EntitlementCatalog.ApiRequestsPerMinute]
                    .DefaultAsLong;
    }

    private async Task RefreshAsync(OrgId org)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var entitlements = scope.ServiceProvider.GetRequiredService<IEntitlements>();
            var limit = await entitlements.LimitAsync(org, EntitlementCatalog.ApiRequestsPerMinute);
            _cache[org] = ((int)limit, DateTimeOffset.UtcNow);
        }
        catch (Exception)
        {
            // keep the previous/default value; next request retries
        }
        finally
        {
            _refreshing.TryRemove(org, out _);
        }
    }
}
