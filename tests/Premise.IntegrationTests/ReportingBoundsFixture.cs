using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Premise.Contracts;
using Premise.Modules.Reporting;
using Premise.Modules.Tenancy.Sites;

namespace Premise.IntegrationTests;

/// <summary>
/// A host whose item budget is one second, and whose site resolutions are
/// counted. Both make an executor promise observable: that a render which
/// ignores cancellation is still bounded, and that reading a page of runs
/// resolves sites once rather than once per run.
/// </summary>
public sealed class ReportingBoundsFixture : ApiFixture
{
    protected override void ConfigureHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Reports:ItemTimeoutSeconds", "1");
        builder.ConfigureServices(services =>
        {
            services
                .AddSingleton<ConcurrentDictionary<Guid, int>>()
                .AddScoped<IReportDefinition, WorkflowReportDefinition>();
            services.AddSingleton<SiteSourceCalls>();
            services.AddScoped<SiteSource>();
            services.AddScoped<ISiteSource, CountingSiteSource>();
        });
    }
}
