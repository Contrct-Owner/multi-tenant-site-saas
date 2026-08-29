using Premise.Modules.Tenancy.Data;
using Premise.Modules.Tenancy.Organizations;
using Premise.Platform.Kernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Premise.IntegrationTests;

/// <summary>
/// Boots real Postgres (Testcontainers), applies every module's migrations
/// (RLS policies included), seeds two orgs, and hosts the API in-process.
/// The app connects as a NON-superuser role: table owners bypass RLS unless
/// FORCEd, and superusers always do - testing as postgres would test nothing.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(
        "postgres:17-alpine"
    ).Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public string AppUserConnectionString { get; private set; } = null!;
    public OrgId OrgA { get; } = OrgId.New();
    public OrgId OrgB { get; } = OrgId.New();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // migrate as owner, then create the restricted app role
        var adminCs = _postgres.GetConnectionString();
        await using (var admin = CreateContext(adminCs))
        {
            await admin.Database.MigrateAsync();
        }
        await _postgres.ExecScriptAsync(
            """
            CREATE ROLE app_user LOGIN PASSWORD 'app_user' NOSUPERUSER;
            -- Wolverine owns its envelope schema; the app creates it at startup
            GRANT CREATE ON DATABASE postgres TO app_user;
            GRANT USAGE ON SCHEMA tenancy TO app_user;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA tenancy TO app_user;
            """
        );
        var appCs = new Npgsql.NpgsqlConnectionStringBuilder(adminCs)
        {
            Username = "app_user",
            Password = "app_user",
        }.ConnectionString;

        // seed two orgs + a setting each (as owner: seeding is platform work)
        await using (var seed = CreateContext(adminCs))
        {
            seed.Organizations.AddRange(
                new Organization
                {
                    Id = OrgA,
                    Name = "Org A",
                    Slug = "org-a",
                    Region = RegionId.Default,
                },
                new Organization
                {
                    Id = OrgB,
                    Name = "Org B",
                    Slug = "org-b",
                    Region = RegionId.Default,
                }
            );
            seed.OrganizationSettings.AddRange(
                OrganizationSetting.Create(OrgA, "brand.color", "#B01458"),
                OrganizationSetting.Create(OrgB, "brand.color", "#0A6E8A")
            );
            await seed.SaveChangesAsync();
        }

        AppUserConnectionString = appCs;
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:premise", appCs);
            builder.UseEnvironment("Testing");
        });
    }

    /// <summary>Client whose requests carry the given org's principal.</summary>
    public HttpClient ClientFor(OrgId org)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Org-Id", org.Value.ToString());
        return client;
    }

    public async Task<Guid> SettingIdOf(OrgId org, string key)
    {
        await using var db = CreateContext(_postgres.GetConnectionString());
        return await db
            .OrganizationSettings.IgnoreQueryFilters()
            .Where(s => s.OrgId == org && s.Key == key)
            .Select(s => s.Id)
            .SingleAsync();
    }

    private static TenancyDbContext CreateContext(string cs)
    {
        var options = new DbContextOptionsBuilder<TenancyDbContext>()
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations_history", "tenancy"))
            .Options;
        return new TenancyDbContext(options, new TenantContext());
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null)
            await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
