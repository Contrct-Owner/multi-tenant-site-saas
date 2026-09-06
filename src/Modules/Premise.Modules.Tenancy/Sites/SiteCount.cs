using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Premise.Modules.Tenancy.Data;
using Premise.Platform.Kernel;

namespace Premise.Modules.Tenancy.Sites;

/// <summary>
/// The org's site count for the plan limit (gate 1 at the creation point).
/// A count over a million rows is an index scan per create; this remembers
/// it for a minute per org, and a create moves the remembered number along
/// so a burst of creates never re-counts. The key names the org (org is
/// never ambient); the dashboard's usage probe still counts exactly.
/// </summary>
public static class SiteCount
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private static string Key(OrgId org) => $"sites.count:{org.Value:N}";

    public static async ValueTask<long> CurrentAsync(
        IMemoryCache cache,
        TenancyDbContext db,
        OrgId org,
        CancellationToken ct
    )
    {
        if (cache.TryGetValue(Key(org), out long remembered))
            return remembered;
        var counted = await db.Sites.LongCountAsync(ct);
        cache.Set(Key(org), counted, Ttl);
        return counted;
    }

    /// <summary>Move the remembered count along after a create; a miss stays a miss.</summary>
    public static void Created(IMemoryCache cache, OrgId org, int by = 1)
    {
        if (cache.TryGetValue(Key(org), out long remembered))
            cache.Set(Key(org), remembered + by, Ttl);
    }
}
