using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace Premise.IntegrationTests;

/// <summary>
/// The image's worker role, booted from the same configuration as the api
/// against the same database: it serves the two probes the orchestrator
/// needs and none of the API surface. Before this the worker had no probe
/// at all while the production guide told operators to wire /healthz to
/// the deployed process.
/// </summary>
public class WorkerRoleTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task The_worker_serves_liveness_and_readiness_and_no_api()
    {
        using var worker = fixture.Factory.WithWebHostBuilder(b => b.UseSetting("ROLE", "worker"));
        using var client = worker.CreateClient();

        var live = await client.GetFromJsonAsync<JsonElement>("/livez");
        Assert.Equal("worker", live.GetProperty("role").GetString());

        var ready = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(
            "worker",
            (await ready.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("role").GetString()
        );

        // no HTTP surface on the worker: the api role owns it
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/sites")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync("/openapi/v1.json")).StatusCode
        );
    }
}
