using Npgsql;
using Premise.Platform.Data;

namespace Premise.IntegrationTests;

/// <summary>
/// The two cross-org tenancy shapes, proven against a real Postgres rather
/// than reviewed by eye. A fork hand-wrote these policies four times; a
/// hand-rolled cross-org policy is exactly where a tenant-isolation bug
/// hides, so the shared helpers get adversarial tests: who can read, who
/// can write, and who can forge a row naming orgs they are not.
/// </summary>
public class TenancyShapeRlsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly Guid OrgOwner = Guid.NewGuid();
    private static readonly Guid OrgCounterparty = Guid.NewGuid();
    private static readonly Guid OrgStranger = Guid.NewGuid();

    private async Task<NpgsqlConnection> AsOrg(Guid org)
    {
        var conn = new NpgsqlConnection(fixture.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT set_config('app.org_id', $1, false)", conn);
        cmd.Parameters.AddWithValue(org.ToString());
        await cmd.ExecuteNonQueryAsync();
        return conn;
    }

    private async Task SetupAsync(string table, string columns, string policySql)
    {
        await using var admin = new NpgsqlConnection(fixture.PostgresConnectionString);
        await admin.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"""
            DROP TABLE IF EXISTS platform.{table};
            CREATE TABLE platform.{table} ({columns});
            GRANT SELECT, INSERT, UPDATE, DELETE ON platform.{table} TO app_user;
            {policySql}
            """,
            admin
        );
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountAsync(NpgsqlConnection conn, string table)
    {
        await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM platform.{table}", conn);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Two_party_rows_are_visible_to_both_sides_and_nobody_else()
    {
        await SetupAsync(
            "two_party_probe",
            "id uuid primary key, org_id uuid not null, counterparty_org_id uuid",
            RlsMigrationExtensions.TwoPartySql("platform", "two_party_probe", "counterparty_org_id")
        );

        await using (var owner = await AsOrg(OrgOwner))
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO platform.two_party_probe VALUES (gen_random_uuid(), $1, $2)",
                owner
            );
            insert.Parameters.AddWithValue(OrgOwner);
            insert.Parameters.AddWithValue(OrgCounterparty);
            await insert.ExecuteNonQueryAsync();
            Assert.Equal(1, await CountAsync(owner, "two_party_probe"));
        }

        await using (var counterparty = await AsOrg(OrgCounterparty))
            Assert.Equal(1, await CountAsync(counterparty, "two_party_probe"));

        await using (var stranger = await AsOrg(OrgStranger))
            Assert.Equal(0, await CountAsync(stranger, "two_party_probe"));

        // WITH CHECK: a tenant must not forge a row between two OTHER orgs
        await using (var stranger = await AsOrg(OrgStranger))
        {
            await using var forge = new NpgsqlCommand(
                "INSERT INTO platform.two_party_probe VALUES (gen_random_uuid(), $1, $2)",
                stranger
            );
            forge.Parameters.AddWithValue(OrgOwner);
            forge.Parameters.AddWithValue(OrgCounterparty);
            await Assert.ThrowsAsync<PostgresException>(() => forge.ExecuteNonQueryAsync());
        }
    }

    [Fact]
    public async Task Published_catalog_rows_read_widely_but_write_only_for_the_owner()
    {
        await SetupAsync(
            "catalog_probe",
            "id uuid primary key, org_id uuid not null, published boolean not null",
            RlsMigrationExtensions.CatalogSql("platform", "catalog_probe")
        );

        await using (var owner = await AsOrg(OrgOwner))
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO platform.catalog_probe VALUES (gen_random_uuid(), $1, true), "
                    + "(gen_random_uuid(), $1, false)",
                owner
            );
            insert.Parameters.AddWithValue(OrgOwner);
            await insert.ExecuteNonQueryAsync();
            Assert.Equal(2, await CountAsync(owner, "catalog_probe")); // both, published or not
        }

        await using (var stranger = await AsOrg(OrgStranger))
        {
            // the published one only - the draft stays private
            Assert.Equal(1, await CountAsync(stranger, "catalog_probe"));

            // reading is not writing: a stranger cannot edit a published row
            await using var hijack = new NpgsqlCommand(
                "UPDATE platform.catalog_probe SET published = false",
                stranger
            );
            Assert.Equal(0, await hijack.ExecuteNonQueryAsync());

            // nor plant a row under someone else's org
            await using var forge = new NpgsqlCommand(
                "INSERT INTO platform.catalog_probe VALUES (gen_random_uuid(), $1, true)",
                stranger
            );
            forge.Parameters.AddWithValue(OrgOwner);
            await Assert.ThrowsAsync<PostgresException>(() => forge.ExecuteNonQueryAsync());
        }
    }

    [Fact]
    public async Task Recipients_may_read_the_parent_but_never_write_it()
    {
        // the side table carries its OWN plain policy - never one referencing
        // the parent, which would recurse
        await SetupAsync(
            "rfq_recipients_probe",
            "id uuid primary key, parent_id uuid not null, recipient_org_id uuid not null, org_id uuid not null",
            RlsMigrationExtensions.TwoPartySql(
                "platform",
                "rfq_recipients_probe",
                "recipient_org_id"
            )
        );
        await SetupAsync(
            "rfq_probe",
            "id uuid primary key, org_id uuid not null, counterparty_org_id uuid, awarded boolean not null default false",
            RlsMigrationExtensions.RecipientListSql(
                "platform",
                "rfq_probe",
                "rfq_recipients_probe",
                "parent_id",
                "recipient_org_id",
                "counterparty_org_id"
            )
        );

        var requestId = Guid.NewGuid();
        await using (var owner = await AsOrg(OrgOwner))
        {
            await using var parent = new NpgsqlCommand(
                "INSERT INTO platform.rfq_probe VALUES ($1, $2, NULL, false)",
                owner
            );
            parent.Parameters.AddWithValue(requestId);
            parent.Parameters.AddWithValue(OrgOwner);
            await parent.ExecuteNonQueryAsync();

            await using var listed = new NpgsqlCommand(
                "INSERT INTO platform.rfq_recipients_probe VALUES (gen_random_uuid(), $1, $2, $3)",
                owner
            );
            listed.Parameters.AddWithValue(requestId);
            listed.Parameters.AddWithValue(OrgCounterparty);
            listed.Parameters.AddWithValue(OrgOwner);
            await listed.ExecuteNonQueryAsync();
        }

        // the recipient READS the broadcast - the point of the shape
        await using (var recipient = await AsOrg(OrgCounterparty))
        {
            Assert.Equal(1, await CountAsync(recipient, "rfq_probe"));

            // ...but must not WRITE it: being on the list is not authority to
            // award the request to yourself. USING makes the row visible, so
            // Postgres evaluates WITH CHECK on the new version and REFUSES
            // loudly rather than silently updating nothing - the better
            // failure mode, and worth pinning as the expected behaviour.
            await using var award = new NpgsqlCommand(
                "UPDATE platform.rfq_probe SET awarded = true",
                recipient
            );
            var refused = await Assert.ThrowsAsync<PostgresException>(() =>
                award.ExecuteNonQueryAsync()
            );
            Assert.Equal("42501", refused.SqlState); // insufficient_privilege
        }

        // an org that is not owner, counterparty, or on the list sees nothing
        await using (var stranger = await AsOrg(OrgStranger))
            Assert.Equal(0, await CountAsync(stranger, "rfq_probe"));
    }

    [Fact]
    public async Task The_recipient_list_policy_does_not_recurse()
    {
        // a policy referencing a table whose own policy references back fails
        // at QUERY time, not creation time - so the proof is a real read
        await SetupAsync(
            "loop_recipients_probe",
            "id uuid primary key, parent_id uuid not null, recipient_org_id uuid not null, org_id uuid not null",
            RlsMigrationExtensions.TwoPartySql(
                "platform",
                "loop_recipients_probe",
                "recipient_org_id"
            )
        );
        await SetupAsync(
            "loop_parent_probe",
            "id uuid primary key, org_id uuid not null, counterparty_org_id uuid",
            RlsMigrationExtensions.RecipientListSql(
                "platform",
                "loop_parent_probe",
                "loop_recipients_probe",
                "parent_id",
                "recipient_org_id",
                "counterparty_org_id"
            )
        );

        await using var reader = await AsOrg(OrgOwner);
        // both directions: the parent scans the side table, and the side
        // table is read on its own - neither may blow the stack
        Assert.Equal(0, await CountAsync(reader, "loop_parent_probe"));
        Assert.Equal(0, await CountAsync(reader, "loop_recipients_probe"));
    }

    // ---- item 16: status-gated recipients, and recipient-written rows ----

    /// <summary>
    /// Drop every table whose POLICY references share_access_probe before the
    /// access table itself. Postgres refuses to drop a table a policy still
    /// names - the ordering trap the migration skill warns about for Down(),
    /// and these tests share a fixture in a shuffled order, so leftovers from
    /// any other test must be cleared first.
    /// </summary>
    private async Task DropAccessDependentsAsync()
    {
        await using var admin = new NpgsqlConnection(fixture.PostgresConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand(
            "DROP TABLE IF EXISTS platform.share_probe, platform.share_member_probe",
            admin
        );
        await drop.ExecuteNonQueryAsync();
    }

    private async Task SetupShareAsync(bool writableByRecipient)
    {
        await DropAccessDependentsAsync();

        await SetupAsync(
            "share_access_probe",
            "id uuid primary key, share_id uuid not null, org_id uuid not null, status text not null",
            RlsMigrationExtensions.TwoPartySql("platform", "share_access_probe", "org_id")
        );
        await SetupAsync(
            "share_probe",
            "id uuid primary key, org_id uuid not null, note text",
            RlsMigrationExtensions.RecipientListSql(
                "platform",
                "share_probe",
                "share_access_probe",
                "share_id",
                "org_id",
                counterpartyColumn: null,
                orgColumn: "org_id",
                recipientPredicate: "status <> 'Removed'",
                writableByRecipient: writableByRecipient
            )
        );
    }

    private async Task SeedShareAsync(Guid shareId, Guid member, string status)
    {
        await using (var owner = await AsOrg(OrgOwner))
        {
            await using var share = new NpgsqlCommand(
                "INSERT INTO platform.share_probe VALUES ($1, $2, 'original')",
                owner
            );
            share.Parameters.AddWithValue(shareId);
            share.Parameters.AddWithValue(OrgOwner);
            await share.ExecuteNonQueryAsync();
        }

        // the access row is single-owner: each org owns its own, so it is
        // written as that org (the owner writing it would violate the access
        // table's own policy - which is the point of anchoring visibility on
        // a single-owner table)
        await using var recipient = await AsOrg(member);
        await using var access = new NpgsqlCommand(
            "INSERT INTO platform.share_access_probe VALUES (gen_random_uuid(), $1, $2, $3)",
            recipient
        );
        access.Parameters.AddWithValue(shareId);
        access.Parameters.AddWithValue(member);
        access.Parameters.AddWithValue(status);
        await access.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task A_removed_recipient_keeps_its_row_but_loses_read_access()
    {
        await SetupShareAsync(writableByRecipient: false);
        await SeedShareAsync(Guid.NewGuid(), OrgCounterparty, "Active");
        await SeedShareAsync(Guid.NewGuid(), OrgStranger, "Removed");

        await using (var active = await AsOrg(OrgCounterparty))
            Assert.Equal(1, await CountAsync(active, "share_probe"));

        // the access row still EXISTS for the audit trail - the predicate is
        // what stops it granting anything
        await using (var removed = await AsOrg(OrgStranger))
        {
            Assert.Equal(0, await CountAsync(removed, "share_probe"));
            Assert.Equal(1, await CountAsync(removed, "share_access_probe"));
        }
    }

    [Fact]
    public async Task Recipients_can_write_when_the_policy_says_so_but_removed_ones_cannot()
    {
        await SetupShareAsync(writableByRecipient: true);
        await SeedShareAsync(Guid.NewGuid(), OrgCounterparty, "Active");
        await SeedShareAsync(Guid.NewGuid(), OrgStranger, "Removed");

        // an active member authors the row (a share member editing membership)
        await using (var active = await AsOrg(OrgCounterparty))
        {
            await using var edit = new NpgsqlCommand(
                "UPDATE platform.share_probe SET note = 'by member'",
                active
            );
            Assert.Equal(1, await edit.ExecuteNonQueryAsync());
        }

        // the removed one cannot write either: the SAME gated lookup guards
        // WITH CHECK, so revoking access revokes writes, not just reads
        await using (var removed = await AsOrg(OrgStranger))
        {
            await using var edit = new NpgsqlCommand(
                "UPDATE platform.share_probe SET note = 'by removed member'",
                removed
            );
            Assert.Equal(0, await edit.ExecuteNonQueryAsync()); // invisible, so nothing to update
        }

        await using (var owner = await AsOrg(OrgOwner))
        {
            await using var read = new NpgsqlCommand(
                "SELECT count(*) FROM platform.share_probe WHERE note = 'by removed member'",
                owner
            );
            Assert.Equal(0L, (long)(await read.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task Recipient_writes_stay_refused_when_the_policy_does_not_grant_them()
    {
        // the default: listing grants READ only, even for an active member
        await SetupShareAsync(writableByRecipient: false);
        await SeedShareAsync(Guid.NewGuid(), OrgCounterparty, "Active");

        await using var active = await AsOrg(OrgCounterparty);
        await using var edit = new NpgsqlCommand(
            "UPDATE platform.share_probe SET note = 'should not stick'",
            active
        );
        var refused = await Assert.ThrowsAsync<PostgresException>(() =>
            edit.ExecuteNonQueryAsync()
        );
        Assert.Equal("42501", refused.SqlState);
    }

    // ---- item 18: children keyed on the parent, and parent-owner writes ----

    /// <summary>
    /// Network's real shape: share_members hangs off a SHARE, so the access
    /// lookup matches share_id to share_id rather than to the row's own id,
    /// and the share's owner administers member rows without being on the
    /// access list.
    /// </summary>
    private async Task SetupShareChildAsync(bool withParentOwner)
    {
        await DropAccessDependentsAsync();

        await SetupAsync(
            "share_access_probe",
            "id uuid primary key, share_id uuid not null, org_id uuid not null, status text not null",
            RlsMigrationExtensions.TwoPartySql("platform", "share_access_probe", "org_id")
        );
        // the parent share: single-owner, so its owner column is the anchor
        await SetupAsync(
            "share_parent_probe",
            "id uuid primary key, owner_org_id uuid not null",
            RlsMigrationExtensions.TenantSql("platform", "share_parent_probe", "owner_org_id")
        );
        await SetupAsync(
            "share_member_probe",
            "id uuid primary key, share_id uuid not null, org_id uuid not null, note text",
            RlsMigrationExtensions.RecipientListSql(
                "platform",
                "share_member_probe",
                "share_access_probe",
                "share_id",
                "org_id",
                counterpartyColumn: null,
                orgColumn: "org_id",
                recipientPredicate: "status <> 'Removed'",
                writableByRecipient: true,
                parentKeyColumn: "share_id",
                parentTable: withParentOwner ? "share_parent_probe" : null,
                parentOwnerColumn: "owner_org_id"
            )
        );
    }

    private async Task SeedShareChildAsync(
        Guid shareId,
        Guid member,
        string status,
        bool withChildRow = true,
        bool withParentRow = true
    )
    {
        if (withParentRow)
        {
            await using var owner = await AsOrg(OrgOwner);
            await using var parent = new NpgsqlCommand(
                "INSERT INTO platform.share_parent_probe VALUES ($1, $2)",
                owner
            );
            parent.Parameters.AddWithValue(shareId);
            parent.Parameters.AddWithValue(OrgOwner);
            await parent.ExecuteNonQueryAsync();
        }

        await using (var recipient = await AsOrg(member))
        {
            await using var access = new NpgsqlCommand(
                "INSERT INTO platform.share_access_probe VALUES (gen_random_uuid(), $1, $2, $3)",
                recipient
            );
            access.Parameters.AddWithValue(shareId);
            access.Parameters.AddWithValue(member);
            access.Parameters.AddWithValue(status);
            await access.ExecuteNonQueryAsync();

            if (withChildRow)
            {
                // the member's own row on the child table
                await using var child = new NpgsqlCommand(
                    "INSERT INTO platform.share_member_probe VALUES (gen_random_uuid(), $1, $2, 'original')",
                    recipient
                );
                child.Parameters.AddWithValue(shareId);
                child.Parameters.AddWithValue(member);
                await child.ExecuteNonQueryAsync();
            }
        }
    }

    [Fact]
    public async Task A_child_keyed_on_its_parent_is_visible_through_the_access_list()
    {
        // The probe row is owned by the PUBLISHER, not the reader - modelling
        // shared_bulletins. That matters: if the row were owned by the member,
        // `org_id = current` would satisfy the policy on its own and the test
        // would pass even with the parent key ignored, proving nothing about
        // the join.
        await SetupShareChildAsync(withParentOwner: false);
        var shareId = Guid.NewGuid();
        await SeedShareChildAsync(shareId, OrgCounterparty, "Active", withChildRow: false);

        await using (var publisher = await AsOrg(OrgOwner))
        {
            await using var bulletin = new NpgsqlCommand(
                "INSERT INTO platform.share_member_probe VALUES (gen_random_uuid(), $1, $2, 'posted')",
                publisher
            );
            bulletin.Parameters.AddWithValue(shareId);
            bulletin.Parameters.AddWithValue(OrgOwner);
            await bulletin.ExecuteNonQueryAsync();
        }

        // the member's ONLY path is share_access.share_id = probe.share_id
        await using (var member = await AsOrg(OrgCounterparty))
            Assert.Equal(1, await CountAsync(member, "share_member_probe"));

        await using (var stranger = await AsOrg(OrgStranger))
            Assert.Equal(0, await CountAsync(stranger, "share_member_probe"));
    }

    [Fact]
    public async Task The_parent_owner_may_administer_the_child_without_being_listed()
    {
        await SetupShareChildAsync(withParentOwner: true);
        var shareId = Guid.NewGuid();
        await SeedShareChildAsync(shareId, OrgCounterparty, "Active");

        // the owner is on no access row, yet administers the membership
        await using (var owner = await AsOrg(OrgOwner))
        {
            Assert.Equal(1, await CountAsync(owner, "share_member_probe"));
            await using var evict = new NpgsqlCommand(
                "UPDATE platform.share_member_probe SET note = 'by parent owner'",
                owner
            );
            Assert.Equal(1, await evict.ExecuteNonQueryAsync());
        }

        // a stranger still cannot: owning SOME share is not owning this one
        await using (var stranger = await AsOrg(OrgStranger))
        {
            Assert.Equal(0, await CountAsync(stranger, "share_member_probe"));
            await using var write = new NpgsqlCommand(
                "UPDATE platform.share_member_probe SET note = 'by stranger'",
                stranger
            );
            Assert.Equal(0, await write.ExecuteNonQueryAsync());
        }
    }

    [Fact]
    public async Task A_removed_member_loses_the_child_even_with_a_parent_owner_clause()
    {
        // the two clauses are independent: adding parent-owner writes must not
        // widen what a lapsed member can reach.
        //
        // The probe row belongs to ANOTHER member on purpose. A removed member
        // still sees its OWN row through org_id - that is ownership, not
        // access, and is the correct behaviour; what removal must take away is
        // everything it could only reach through the access list.
        await SetupShareChildAsync(withParentOwner: true);
        var shareId = Guid.NewGuid();
        await SeedShareChildAsync(shareId, OrgCounterparty, "Active");
        await SeedShareChildAsync(
            shareId,
            OrgStranger,
            "Removed",
            withChildRow: false,
            withParentRow: false
        );

        await using var removed = await AsOrg(OrgStranger);
        Assert.Equal(0, await CountAsync(removed, "share_member_probe"));
        await using var write = new NpgsqlCommand(
            "UPDATE platform.share_member_probe SET note = 'by removed'",
            removed
        );
        Assert.Equal(0, await write.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task The_parent_owner_clause_does_not_recurse()
    {
        // it reaches UP to the parent, whose own policy is single-owner and
        // names nothing here; a real read in both directions proves it
        await SetupShareChildAsync(withParentOwner: true);
        await using var reader = await AsOrg(OrgOwner);
        Assert.Equal(0, await CountAsync(reader, "share_member_probe"));
        Assert.Equal(0, await CountAsync(reader, "share_parent_probe"));
    }
}
