using System.Net;
using System.Net.Http.Json;

namespace Premise.IntegrationTests;

/// <summary>
/// The golden suite (ADR 1/17 + CLAUDE.md): every id-addressed endpoint is
/// replayed as tenant B against tenant A's ids and must 404 - never 200,
/// never 403 (a 403 confirms the resource exists). Every new id-addressed
/// endpoint gets a row in the replay table below.
/// </summary>
public class TenantIsolationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Own_setting_is_readable()
    {
        var id = await fixture.SettingIdOf(fixture.OrgA, "brand.color");
        var response = await fixture.ClientFor(fixture.OrgA).GetAsync($"/api/settings/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Other_tenants_setting_by_id_is_404_not_403()
    {
        var orgAsSettingId = await fixture.SettingIdOf(fixture.OrgA, "brand.color");
        var response = await fixture
            .ClientFor(fixture.OrgB)
            .GetAsync($"/api/settings/{orgAsSettingId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_never_contains_another_tenants_rows()
    {
        var settings = await fixture
            .ClientFor(fixture.OrgB)
            .GetFromJsonAsync<List<SettingDto>>("/api/settings");
        Assert.NotNull(settings);
        var setting = Assert.Single(settings!, s => s.Key == "brand.color");
        Assert.Equal("#0A6E8A", setting.Value); // org B's value, never org A's
    }

    [Fact]
    public async Task Write_lands_in_own_tenant_only()
    {
        var put = await fixture
            .ClientFor(fixture.OrgB)
            .PutAsJsonAsync("/api/settings/onboarding.step", new { value = "2" });
        put.EnsureSuccessStatusCode();

        var orgAList = await fixture
            .ClientFor(fixture.OrgA)
            .GetFromJsonAsync<List<SettingDto>>("/api/settings");
        Assert.DoesNotContain(orgAList!, s => s.Key == "onboarding.step");
    }

    [Fact]
    public async Task No_principal_means_no_rows_fail_closed()
    {
        var client = fixture.Factory.CreateClient(); // no X-Org-Id header
        var settings = await client.GetFromJsonAsync<List<SettingDto>>("/api/settings");
        Assert.Empty(settings!);
    }

    private sealed record SettingDto(Guid Id, string Key, string Value);
}
