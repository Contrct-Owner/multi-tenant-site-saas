using Microsoft.EntityFrameworkCore;
using Premise.Modules.Identity.Data;
using Premise.Platform.Kernel;

namespace Premise.Modules.Identity.Access;

/// <summary>
/// The gate-2/gate-3 evaluator (ADR 6), replacing step 3's
/// MembershipScopeResolver at the SAME port - no call site changed. Grants
/// come from role assignments (at org or subtree scope) plus active
/// time-boxed exceptions; evaluation is monotonic - more grants only ever
/// widen scope. Memoized per request scope.
/// </summary>
public sealed class GrantScopeResolver(IdentityDbContext db) : IScopeResolver
{
    private readonly Dictionary<(Guid, OrgId, string), NodeScope> _memo = [];

    public async ValueTask<bool> CanAsync(
        Principal principal,
        string action,
        CancellationToken ct = default
    ) => await ScopeForAsync(principal, action, ct) is not NodeScope.None;

    public async ValueTask<NodeScope> ScopeForAsync(
        Principal principal,
        string action,
        CancellationToken ct = default
    )
    {
        // the guest/contact tiers ARE principals of their org (ADR 7): they
        // hold exactly public:read over it, nothing else
        switch (principal)
        {
            case Principal.Guest { Org: { } guestOrg } when action == Capabilities.PublicRead:
                return new NodeScope.EntireOrg(guestOrg);
            case Principal.Contact contact when action == Capabilities.PublicRead:
                // the contact RECORD is the authority: a revoked contact's
                // still-valid cookie holds nothing
                return await db.Contacts.AnyAsync(
                    c => c.Id == contact.ContactId && c.RevokedAt == null,
                    ct
                )
                    ? new NodeScope.EntireOrg(contact.Org)
                    : NodeScope.Nothing;
        }
        if (principal is not Principal.User { ActiveOrg: { } org, UserId: var userId })
            return NodeScope.Nothing;

        if (_memo.TryGetValue((userId, org, action), out var cached))
            return cached;

        var (domain, verb) = Split(action);

        var roleScopes = await (
            from membership in db.Memberships
            where membership.UserId == userId && membership.OrgId == org
            join assignment in db.MembershipRoles on membership.Id equals assignment.MembershipId
            join grant in db.RoleGrants on assignment.RoleId equals grant.RoleId
            where
                (grant.Domain == domain || grant.Domain == "*")
                && (grant.Action == verb || grant.Action == "*")
            select assignment.ScopePath
        )
            .Distinct()
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var exceptionScopes = await db
            .GrantExceptions.Where(e =>
                e.UserId == userId
                && e.OrgId == org
                && e.ExpiresAt > now
                && (e.Domain == domain || e.Domain == "*")
                && (e.Action == verb || e.Action == "*")
            )
            .Select(e => e.ScopePath)
            .Distinct()
            .ToListAsync(ct);

        var paths = roleScopes.Concat(exceptionScopes).ToList();
        NodeScope scope =
            paths.Count == 0 ? NodeScope.Nothing
            : paths.Contains(null) ? new NodeScope.EntireOrg(org)
            : new NodeScope.Subtrees(org, [.. paths.Cast<string>().Distinct()]);
        _memo[(userId, org, action)] = scope;
        return scope;
    }

    private static (string domain, string action) Split(string action)
    {
        var index = action.IndexOf(':');
        return index < 0 ? (action, "*") : (action[..index], action[(index + 1)..]);
    }
}
