using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Premise.Modules.Reporting;
using Premise.Modules.Reporting.Data;
using Premise.Modules.Tenancy.Data;
using Premise.Modules.Tenancy.Sites;
using Premise.Platform.Kernel;

namespace Premise.IntegrationTests;

/// <summary>A concrete test report exercises the fork registration contract. PDF layout is tested separately.</summary>
public sealed class WorkflowReportDefinition(ConcurrentDictionary<Guid, int> attempts)
    : IReportDefinition
{
    private readonly ConcurrentDictionary<Guid, int> _attempts = attempts;
    public string Id => "workflow-test";
    public string Name => "Workflow test";
    public int Version => 1;
    public bool Aggregate => false;

    public string? ValidateOptions(JsonElement options) => null;

    public Task<bool> AuthorizeAsync(
        IReportDefinition.Request request,
        JsonElement? dependencies,
        CancellationToken ct
    ) => Task.FromResult(true);

    public async Task<IReportDefinition.Output> RenderAsync(
        IReportDefinition.Request request,
        Stream destination,
        CancellationToken ct
    )
    {
        var id = request.SiteIds.Single();
        var attempt = _attempts.AddOrUpdate(id, 1, (_, n) => n + 1);
        if (
            request.Options.TryGetProperty("failFirstSite", out var fail)
            && fail.GetGuid() == id
            && attempt == 1
        )
            throw new IOException("Test first-attempt failure.");
        if (request.Options.TryGetProperty("hold", out var hold) && hold.GetBoolean())
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        if (request.Options.TryGetProperty("oversize", out var oversize) && oversize.GetBoolean())
        {
            var chunk = new byte[1024 * 1024];
            for (var i = 0; i < 21; i++)
                await destination.WriteAsync(chunk, ct);
        }
        await destination.WriteAsync(
            Encoding.UTF8.GetBytes($"%PDF-1.7\nworkflow-fixture-{id}-{attempt}\n%%EOF"),
            ct
        );
        return new(JsonSerializer.SerializeToElement(new { sites = request.SiteIds }), []);
    }
}
