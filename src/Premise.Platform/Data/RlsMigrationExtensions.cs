using Microsoft.EntityFrameworkCore.Migrations;

namespace Premise.Platform.Data;

/// <summary>
/// RLS policy helper for migrations. EVERY org-scoped table calls this in the
/// migration that creates it (see the new-migration skill); CI asserts coverage.
/// NULLIF guards a pooled-connection quirk: after DISCARD ALL a custom GUC
/// reads as '' (not NULL), and ''::uuid errors - NULLIF turns it back into
/// NULL so an unset tenant matches nothing instead of erroring.
/// FORCE applies the policy to the table owner too, so even a misconfigured
/// app role cannot bypass it.
/// </summary>
public static class RlsMigrationExtensions
{
    public static void EnableTenantRls(
        this MigrationBuilder migrationBuilder,
        string schema,
        string table,
        string orgColumn = "org_id"
    )
    {
        migrationBuilder.Sql(TenantSql(schema, table, orgColumn));
    }

    /// <summary>
    /// A row two orgs share (<see cref="Kernel.ITwoPartyScoped"/>): either
    /// side sees it. One policy, FOR ALL, so a counterparty can advance the
    /// row it is party to. WITH CHECK repeats the predicate: without it, a
    /// tenant could INSERT a row naming two OTHER orgs.
    /// </summary>
    public static void EnableTwoPartyRls(
        this MigrationBuilder migrationBuilder,
        string schema,
        string table,
        string counterpartyColumn,
        string orgColumn = "org_id"
    )
    {
        migrationBuilder.Sql(TwoPartySql(schema, table, counterpartyColumn, orgColumn));
    }

    /// <summary>
    /// Owner-writes, everyone-reads-once-published
    /// (<see cref="Kernel.IPublishedCatalogScoped"/>). TWO policies on
    /// purpose: a single FOR ALL policy that allowed published reads would
    /// also allow published WRITES by any tenant.
    /// </summary>
    public static void EnablePublishedCatalogRls(
        this MigrationBuilder migrationBuilder,
        string schema,
        string table,
        string publishedColumn = "published",
        string orgColumn = "org_id"
    )
    {
        migrationBuilder.Sql(CatalogSql(schema, table, publishedColumn, orgColumn));
    }

    /// <summary>
    /// A row visible to its owner, an optional counterparty, AND every org
    /// listed in a side table (a broadcast request and its recipients, a
    /// share and its members). Two invariants are load-bearing:
    ///
    /// 1. The RECIPIENTS table gets its own plain policy - never one that
    ///    references the parent. A policy that reaches back into a table
    ///    whose own policy reaches here recurses, and the query fails at
    ///    runtime. Anchor visibility on one single-owner side table.
    /// 2. WITH CHECK deliberately OMITS the recipient clause: being on the
    ///    recipient list grants READ, never write. Without that asymmetry any
    ///    recipient could edit the parent row - awarding a request to itself,
    ///    say - which is the whole point of the shape.
    /// </summary>
    /// <param name="recipientPredicate">
    /// Optional extra condition on the RECIPIENT row, e.g.
    /// <c>"status &lt;&gt; 'Removed'"</c>. Membership rows usually survive
    /// removal for the audit trail, so listing alone cannot mean access. It
    /// is applied to reads AND writes: a predicate that gated only reads
    /// would let a removed member keep writing. Raw SQL, authored in a
    /// migration - never user input, same trust level as the table name.
    /// </param>
    /// <param name="parentKeyColumn">
    /// The column on THIS table that the recipients' foreign key matches.
    /// Defaults to <c>"id"</c> - correct when the protected row IS the parent
    /// (a request and its recipients). A CHILD of the shared thing keys on
    /// the parent instead: share_members and shared_bulletins both hang off a
    /// share, so the lookup is share_access.share_id = share_members.share_id.
    /// </param>
    /// <param name="parentTable">
    /// When set, the owner of the PARENT may read and write this row - a
    /// share's owner administers its member rows without being on the access
    /// list. The clause reaches UP to the parent (matched on the parent's
    /// <c>id</c>) and never back to this table, so it cannot recurse; the
    /// parent's own policy must likewise not name this child.
    /// </param>
    /// <param name="parentOwnerColumn">The owning-org column on that parent table.</param>
    /// <param name="writableByRecipient">
    /// When the row is written by the recipient rather than the parent's
    /// owner (a share member editing its own membership, a publisher posting
    /// into a share), extend WITH CHECK with the same gated lookup. Default
    /// false keeps the safe shape: listing grants read only.
    /// </param>
    public static void EnableRecipientListRls(
        this MigrationBuilder migrationBuilder,
        string schema,
        string table,
        string recipientsTable,
        string foreignKeyColumn,
        string recipientOrgColumn,
        string? counterpartyColumn = null,
        string orgColumn = "org_id",
        string? recipientPredicate = null,
        bool writableByRecipient = false,
        string parentKeyColumn = "id",
        string? parentTable = null,
        string parentOwnerColumn = "org_id"
    )
    {
        migrationBuilder.Sql(
            RecipientListSql(
                schema,
                table,
                recipientsTable,
                foreignKeyColumn,
                recipientOrgColumn,
                counterpartyColumn,
                orgColumn,
                recipientPredicate,
                writableByRecipient,
                parentKeyColumn,
                parentTable,
                parentOwnerColumn
            )
        );
    }

    // SQL as pure functions so the policies can be asserted directly in tests
    // rather than only through a module that happens to use them.
    internal static string TenantSql(string schema, string table, string orgColumn = "org_id") =>
        $"""
            ALTER TABLE "{schema}"."{table}" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "{schema}"."{table}" FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON "{schema}"."{table}"
                USING ("{orgColumn}" = NULLIF(current_setting('app.org_id', true), '')::uuid)
                WITH CHECK ("{orgColumn}" = NULLIF(current_setting('app.org_id', true), '')::uuid);
            """;

    internal static string TwoPartySql(
        string schema,
        string table,
        string counterpartyColumn,
        string orgColumn = "org_id"
    ) =>
        $"""
            ALTER TABLE "{schema}"."{table}" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "{schema}"."{table}" FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON "{schema}"."{table}"
                USING (
                    "{orgColumn}" = NULLIF(current_setting('app.org_id', true), '')::uuid
                    OR "{counterpartyColumn}" = NULLIF(current_setting('app.org_id', true), '')::uuid
                )
                WITH CHECK (
                    "{orgColumn}" = NULLIF(current_setting('app.org_id', true), '')::uuid
                    OR "{counterpartyColumn}" = NULLIF(current_setting('app.org_id', true), '')::uuid
                );
            """;

    internal static string RecipientListSql(
        string schema,
        string table,
        string recipientsTable,
        string foreignKeyColumn,
        string recipientOrgColumn,
        string? counterpartyColumn = null,
        string orgColumn = "org_id",
        string? recipientPredicate = null,
        bool writableByRecipient = false,
        string parentKeyColumn = "id",
        string? parentTable = null,
        string parentOwnerColumn = "org_id"
    )
    {
        const string Current = "NULLIF(current_setting('app.org_id', true), '')::uuid";
        var owner = $"\"{orgColumn}\" = {Current}";
        var party = counterpartyColumn is null
            ? owner
            : $"{owner} OR \"{counterpartyColumn}\" = {Current}";
        // the predicate qualifies the RECIPIENT row (alias r), so a listing
        // that has lapsed - a removed member kept for the audit trail - stops
        // granting anything
        var gate = recipientPredicate is null ? "" : $" AND ({recipientPredicate})";
        // parentKeyColumn is what the recipients' foreign key matches on THIS
        // table. It is the row's own id for a parent (a request and its
        // recipients) but the parent's key for a child (share_members and
        // shared_bulletins both hang off a share, so the lookup is
        // share_access.share_id = share_members.share_id).
        var onTheList =
            $"EXISTS (SELECT 1 FROM \"{schema}\".\"{recipientsTable}\" r "
            + $"WHERE r.\"{foreignKeyColumn}\" = \"{table}\".\"{parentKeyColumn}\" "
            + $"AND r.\"{recipientOrgColumn}\" = {Current}{gate})";

        // The owner of the PARENT may also read and write the child: a share's
        // owner administers its member rows without appearing on the access
        // list. It reaches UP to the parent and never back to this table, so
        // it cannot recurse - the parent's own policy must likewise not name
        // this child.
        var parentOwner = parentTable is null
            ? null
            : $"EXISTS (SELECT 1 FROM \"{schema}\".\"{parentTable}\" p "
                + $"WHERE p.id = \"{table}\".\"{parentKeyColumn}\" "
                + $"AND p.\"{parentOwnerColumn}\" = {Current})";
        var orParentOwner = parentOwner is null ? "" : $" OR {parentOwner}";

        // same gated lookup on the write side when recipients author the row;
        // reusing it (rather than an ungated variant) is what stops a removed
        // member keeping write access it no longer has for reads
        var writable = writableByRecipient ? $"{party} OR {onTheList}" : party;

        return $"""
            ALTER TABLE "{schema}"."{table}" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "{schema}"."{table}" FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON "{schema}"."{table}"
                USING ({party} OR {onTheList}{orParentOwner})
                WITH CHECK ({writable}{orParentOwner});
            """;
    }

    internal static string CatalogSql(
        string schema,
        string table,
        string publishedColumn = "published",
        string orgColumn = "org_id"
    ) =>
        $"""
            ALTER TABLE "{schema}"."{table}" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "{schema}"."{table}" FORCE ROW LEVEL SECURITY;
            CREATE POLICY catalog_read ON "{schema}"."{table}" FOR SELECT
                USING (
                    "{publishedColumn}"
                    OR "{orgColumn}" = NULLIF(current_setting('app.org_id', true), '')::uuid
                );
            CREATE POLICY owner_write ON "{schema}"."{table}" FOR ALL
                USING ("{orgColumn}" = NULLIF(current_setting('app.org_id', true), '')::uuid)
                WITH CHECK ("{orgColumn}" = NULLIF(current_setting('app.org_id', true), '')::uuid);
            """;
}
