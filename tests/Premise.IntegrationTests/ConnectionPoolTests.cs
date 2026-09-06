using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Premise.Platform.Data;
using Premise.Platform.Kernel;

namespace Premise.IntegrationTests;

public class ConnectionPoolTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Theory]
    [InlineData("", 20)]
    [InlineData(";Maximum Pool Size=7", 7)]
    [InlineData(";Minimum Pool Size=25", 25)]
    public async Task Runtime_bounds_default_pools_and_preserves_explicit_budgets(
        string suffix,
        int expected
    )
    {
        await using var host = fixture.Factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:premise", fixture.AppConnectionString + suffix)
        );
        var configuration = host.Services.GetRequiredService<IConfiguration>();
        var regions = host.Services.GetRequiredService<IRegionDataSources>();
        Assert.Equal(
            expected,
            new NpgsqlConnectionStringBuilder(
                configuration.GetConnectionString("premise")
            ).MaxPoolSize
        );
        Assert.Equal(
            expected,
            new NpgsqlConnectionStringBuilder(
                regions.For(RegionId.Default).ConnectionString
            ).MaxPoolSize
        );
    }
}
