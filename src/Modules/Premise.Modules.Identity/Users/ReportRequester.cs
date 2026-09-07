using Microsoft.EntityFrameworkCore;
using Premise.Contracts;
using Premise.Modules.Identity.Data;
using Premise.Platform.Kernel;

namespace Premise.Modules.Identity.Users;

public sealed class ReportRequester(IdentityDbContext db) : IReportRequester
{
    public async Task<Principal.User?> GetActiveAsync(
        OrgId org,
        Guid userId,
        CancellationToken ct = default
    )
    {
        var user = await (
            from membership in db.Memberships
            where membership.OrgId == org && membership.UserId == userId
            join entry in db.OrgDirectory on membership.OrgId equals entry.OrgId
            where entry.Status == "Active"
            join account in db.Users on membership.UserId equals account.Id
            select account
        )
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);
        return user is null ? null : new Principal.User(user.Id, user.Email, user.Name, org);
    }
}
