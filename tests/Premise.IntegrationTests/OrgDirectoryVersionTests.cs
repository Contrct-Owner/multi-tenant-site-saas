using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Premise.Api;
using Premise.Contracts;
using Premise.Modules.Identity.Data;
using Premise.Modules.Identity.Users;
using Premise.Platform.Kernel;
using Wolverine;

namespace Premise.IntegrationTests;

/// <summary>
/// The third projection rule (docs/cross-tenant-sharing.md), through the
/// real org_directory handler: an older event delivered after a newer one,
/// or the newer one delivered twice, never overwrites the row. The events
/// are INVOKED rather than published so the arrival order is the test's,
/// not the queue's, and each handling is awaited - a published stale event
/// that changes nothing is indistinguishable from one not yet handled.
/// Invoke runs the same handler chain, transaction and lock included.
/// </summary>
public class OrgDirectoryVersionTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private IdentityDbContext Identity() =>
        (IdentityDbContext)
            ApiFixture.CreateCatalogContext(
                ModuleCatalog.All.Single(m => m.Name == "identity"),
                fixture.PostgresConnectionString
            );

    private static OrganizationUpserted Event(OrgId org, string name, long version) =>
        new(org, name, $"v-{org.Value:N}", RegionId.Default, null, version);

    [Fact]
    public async Task An_older_event_arriving_after_a_newer_one_is_ignored()
    {
        var org = OrgId.New();
        using var scope = fixture.Factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        await bus.InvokeAsync(Event(org, "Newer", 7));
        await bus.InvokeAsync(Event(org, "Older", 3)); // late on a busy outbox

        var row = await RowAsync(org);
        Assert.Equal("Newer", row.Name);
        Assert.Equal(7, row.SourceVersion);
    }

    [Fact]
    public async Task A_redelivered_event_changes_nothing()
    {
        var org = OrgId.New();
        using var scope = fixture.Factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        await bus.InvokeAsync(Event(org, "Once", 4));
        var first = await RowAsync(org);
        await bus.InvokeAsync(Event(org, "Twice", 4)); // same version, different payload

        var again = await RowAsync(org);
        Assert.Equal("Once", again.Name);
        Assert.Equal(first.SyncedAt, again.SyncedAt); // not even touched
    }

    [Fact]
    public async Task A_newer_event_still_applies()
    {
        var org = OrgId.New();
        using var scope = fixture.Factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        await bus.InvokeAsync(Event(org, "First", 1));
        await bus.InvokeAsync(Event(org, "Second", 2));

        Assert.Equal("Second", (await RowAsync(org)).Name);
    }

    private async Task<OrgDirectoryEntry> RowAsync(OrgId org)
    {
        await using var db = Identity();
        return await db.OrgDirectory.SingleAsync(d => d.OrgId == org);
    }
}
