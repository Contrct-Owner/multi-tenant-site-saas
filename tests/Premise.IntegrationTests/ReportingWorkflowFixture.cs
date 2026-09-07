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

public sealed class ReportingWorkflowFixture : ApiFixture
{
    protected override void ConfigureHost(IWebHostBuilder builder)
    {
        foreach (var id in new[] { "fixture", "unavailable", "redirect", "corrupt" })
        {
            builder.UseSetting(
                $"Reports:Basemaps:{id}:Url",
                $"https://report-tiles.example.invalid/{id}/{{z}}/{{x}}/{{y}}.png"
            );
            builder.UseSetting(
                $"Reports:Basemaps:{id}:Attribution",
                "Synthetic report tile fixture"
            );
        }
        builder.ConfigureServices(services =>
        {
            services
                .AddSingleton<ConcurrentDictionary<Guid, int>>()
                .AddScoped<IReportDefinition, WorkflowReportDefinition>();
            services.AddTransient<ReportTileFixtureHandler>();
            services
                .AddHttpClient(Premise.Modules.Reporting.Rendering.ReportBasemaps.ClientName)
                .ConfigurePrimaryHttpMessageHandler<ReportTileFixtureHandler>();
        });
    }
}
