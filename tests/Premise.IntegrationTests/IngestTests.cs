using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Premise.IntegrationTests;

/// <summary>ADR 18: staging + dry-run diff + idempotent commit, uploads and connectors on one core.</summary>
public class IngestTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private async Task<(HttpClient client, Guid rootId, Guid eastId)> Setup()
    {
        var client = await fixture.LoginAsync(ApiFixture.UserA);
        var get = await client.GetAsync("/api/hierarchy");
        Guid rootId;
        if (get.StatusCode == HttpStatusCode.OK)
        {
            var tree = await get.Content.ReadFromJsonAsync<JsonElement>();
            rootId = tree.GetProperty("nodes")
                .EnumerateArray()
                .First(n => n.GetProperty("depth").GetInt32() == 0)
                .GetProperty("id")
                .GetGuid();
            var east = tree.GetProperty("nodes")
                .EnumerateArray()
                .FirstOrDefault(n => n.GetProperty("name").GetString() == "IngestEast");
            if (east.ValueKind == JsonValueKind.Object)
                return (client, rootId, east.GetProperty("id").GetGuid());
        }
        else
        {
            var created = await client.PostAsJsonAsync(
                "/api/hierarchy",
                new { name = "Org A", levels = new[] { "Region", "Market" } }
            );
            created.EnsureSuccessStatusCode();
            rootId = (await created.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("rootNodeId")
                .GetGuid();
        }
        var node = await client.PostAsJsonAsync(
            "/api/hierarchy/nodes",
            new { parentId = rootId, name = "IngestEast" }
        );
        node.EnsureSuccessStatusCode();
        return (
            client,
            rootId,
            (await node.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid()
        );
    }

    private async Task<Guid> UploadCsv(HttpClient client, string csv)
    {
        var bytes = Encoding.UTF8.GetBytes(csv);
        var created = await client.PostAsJsonAsync(
            "/api/files",
            new
            {
                name = "sites.csv",
                contentType = "text/csv",
                sizeBytes = bytes.Length,
            }
        );
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var fileId = body.GetProperty("fileId").GetGuid();
        var put = new HttpRequestMessage(
            HttpMethod.Put,
            body.GetProperty("ticket").GetProperty("url").GetString()
        )
        {
            Content = new ByteArrayContent(bytes),
        };
        (await client.SendAsync(put)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/files/{fileId}/complete", null)).EnsureSuccessStatusCode();

        // staging requires a Clean verdict
        for (var i = 0; i < 60; i++)
        {
            var files = await client.GetFromJsonAsync<JsonElement>("/api/files");
            if (
                files
                    .EnumerateArray()
                    .First(f => f.GetProperty("id").GetGuid() == fileId)
                    .GetProperty("status")
                    .GetString() == "Clean"
            )
                break;
            await Task.Delay(100);
        }
        return fileId;
    }

    private static async Task<JsonElement> Stage(HttpClient client, Guid fileId)
    {
        var staged = await client.PostAsJsonAsync("/api/ingest/uploads", new { fileId });
        Assert.True(staged.IsSuccessStatusCode, await staged.Content.ReadAsStringAsync());
        return await staged.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement?> PollSite(HttpClient client, string name, bool expect = true)
    {
        for (var i = 0; i < 60; i++)
        {
            var sites = await client.GetFromJsonAsync<JsonElement>("/api/sites");
            var match = sites
                .EnumerateArray()
                .FirstOrDefault(s => s.GetProperty("name").GetString() == name);
            if ((match.ValueKind == JsonValueKind.Object) == expect)
                return match.ValueKind == JsonValueKind.Object ? match : null;
            await Task.Delay(100);
        }
        return null;
    }

    [Fact]
    public async Task Upload_preview_commit_creates_updates_and_closes()
    {
        var (client, _, _) = await Setup();

        // round 1: two creates
        var csv1 = """
            external_id,name,time_zone,node,status
            ing-001,Downtown,America/New_York,IngestEast,open
            ing-002,Uptown,America/Chicago,IngestEast,open
            """;
        var batch1 = await Stage(client, await UploadCsv(client, csv1));
        Assert.Equal(2, batch1.GetProperty("counts").GetProperty("create").GetInt32());
        var commit1 = await client.PostAsync(
            $"/api/ingest/batches/{batch1.GetProperty("batchId").GetGuid()}/commit",
            null
        );
        commit1.EnsureSuccessStatusCode();
        Assert.NotNull(await PollSite(client, "Downtown"));

        // round 2: same file re-run = all unchanged (idempotent by external id)
        var batch2 = await Stage(client, await UploadCsv(client, csv1));
        Assert.Equal(0, batch2.GetProperty("counts").GetProperty("create").GetInt32());
        Assert.Equal(2, batch2.GetProperty("counts").GetProperty("unchanged").GetInt32());

        // round 3: rename one, close the other
        var csv3 = """
            external_id,name,time_zone,node,status
            ing-001,Downtown Flagship,America/New_York,IngestEast,open
            ing-002,Uptown,America/Chicago,IngestEast,closed
            """;
        var batch3 = await Stage(client, await UploadCsv(client, csv3));
        Assert.Equal(1, batch3.GetProperty("counts").GetProperty("update").GetInt32());
        Assert.Equal(1, batch3.GetProperty("counts").GetProperty("close").GetInt32());
        (
            await client.PostAsync(
                $"/api/ingest/batches/{batch3.GetProperty("batchId").GetGuid()}/commit",
                null
            )
        ).EnsureSuccessStatusCode();

        var renamed = await PollSite(client, "Downtown Flagship");
        Assert.NotNull(renamed);
        JsonElement? uptown = null;
        for (var i = 0; i < 60 && uptown?.GetProperty("status").GetString() != "Closed"; i++)
        {
            await Task.Delay(100);
            uptown = await PollSite(client, "Uptown");
        }
        Assert.Equal("Closed", uptown?.GetProperty("status").GetString());

        // closing is a DOMAIN EVENT (ADR 18), never a delete
        List<Premise.Modules.Audit.Data.DomainLogEntry> closures = [];
        for (var i = 0; i < 50 && closures.Count == 0; i++)
        {
            await Task.Delay(100);
            closures = await fixture.QueryAudit(db =>
                Microsoft
                    .EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(
                        db.DomainEvents
                    )
                    .Where(a => a.EventName == "site.closed")
            );
        }
        Assert.NotEmpty(closures);
    }

    [Fact]
    public async Task Invalid_rows_are_reported_not_applied()
    {
        var (client, _, _) = await Setup();
        var csv = """
            external_id,name,time_zone,node,status
            ,Missing Id,America/New_York,IngestEast,open
            ing-bad-tz,Bad Zone,Mars/Olympus,IngestEast,open
            ing-bad-node,Lost,America/New_York,Nowhere/AtAll,open
            """;
        var batch = await Stage(client, await UploadCsv(client, csv));
        Assert.Equal(3, batch.GetProperty("counts").GetProperty("invalid").GetInt32());

        var preview = await client.GetFromJsonAsync<JsonElement>(
            $"/api/ingest/batches/{batch.GetProperty("batchId").GetGuid()}"
        );
        var badZone = preview
            .GetProperty("rows")
            .EnumerateArray()
            .First(r => r.GetProperty("externalId").GetString() == "ing-bad-tz");
        Assert.Contains("IANA", badZone.GetProperty("errors")[0].GetString());
    }

    [Fact]
    public async Task Connector_syncs_through_the_same_staging_core()
    {
        var (client, _, _) = await Setup();

        // stub source: an org's POS system speaking json over http with an api key
        string? seenKey = null;
        var stub = WebApplication.CreateSlimBuilder().Build();
        stub.MapGet(
            "/sites",
            (HttpRequest req) =>
            {
                seenKey = req.Headers["X-Api-Key"].ToString();
                return Results.Json(
                    new[]
                    {
                        new
                        {
                            external_id = "pos-100",
                            name = "Harborside",
                            time_zone = "America/New_York",
                            node = "IngestEast",
                            status = "open",
                        },
                    }
                );
            }
        );
        await stub.StartAsync();
        var stubUrl = stub.Urls.First();

        try
        {
            var created = await client.PostAsJsonAsync(
                "/api/connectors",
                new
                {
                    name = "pos-sync",
                    url = $"{stubUrl}/sites",
                    apiKey = "sk-pos-secret",
                }
            );
            created.EnsureSuccessStatusCode();
            var connectorId = (await created.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("id")
                .GetGuid();

            (
                await client.PostAsync($"/api/connectors/{connectorId}/sync", null)
            ).EnsureSuccessStatusCode();

            // sync lands a STAGED batch (same core as uploads); commit stays explicit
            JsonElement batch = default;
            for (var i = 0; i < 60; i++)
            {
                await Task.Delay(100);
                var found = await fixture.QueryIngestBatch("pos-sync");
                if (found is { } b)
                {
                    batch = JsonSerializer.SerializeToElement(b);
                    break;
                }
            }
            Assert.Equal("sk-pos-secret", seenKey); // decrypted credential reached the source
            var batchId = batch.GetProperty("Id").GetGuid();
            var commit = await client.PostAsync($"/api/ingest/batches/{batchId}/commit", null);
            commit.EnsureSuccessStatusCode();
            var site = await PollSite(client, "Harborside");
            if (site is null)
            {
                var preview = await client.GetStringAsync($"/api/ingest/batches/{batchId}");
                Assert.Fail(
                    $"no site; commit={await commit.Content.ReadAsStringAsync()} preview={preview}"
                );
            }

            // credential access is audited (ADR 31)
            List<Premise.Modules.Audit.Data.DomainLogEntry> accesses = [];
            for (var i = 0; i < 50 && accesses.Count == 0; i++)
            {
                await Task.Delay(100);
                accesses = await fixture.QueryAudit(db =>
                    Microsoft
                        .EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(
                            db.DomainEvents
                        )
                        .Where(a => a.EventName == "connector.credentials_accessed")
                );
            }
            Assert.NotEmpty(accesses);
        }
        finally
        {
            await stub.StopAsync();
        }
    }
}
