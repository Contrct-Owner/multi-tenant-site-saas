using Microsoft.EntityFrameworkCore;
using Premise.Contracts;
using Premise.Modules.Tenancy.Data;
using Premise.Platform.Kernel;

namespace Premise.Modules.Tenancy.Sites;

public sealed class SiteDirectory(TenancyDbContext db) : ISiteDirectory
{
    public async Task<SiteInfo?> FindAsync(Guid siteId, CancellationToken ct = default)
    {
        var id = new SiteId(siteId);
        return await db
            .Sites.Where(s => s.Id == id)
            .Select(s => new SiteInfo(s.Id.Value, s.Name, s.Path.ToString(), s.TimeZone))
            .FirstOrDefaultAsync(ct);
    }
}
