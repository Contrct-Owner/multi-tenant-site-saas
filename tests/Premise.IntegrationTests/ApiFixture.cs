using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Premise.Modules.Identity.Data;
using Premise.Modules.Identity.Users;
using Premise.Modules.Tenancy.Data;
using Premise.Modules.Tenancy.Organizations;
using Premise.Platform.Kernel;
using Testcontainers.PostgreSql;

namespace Premise.IntegrationTests;

/// <summary>
/// Boots real Postgres (Testcontainers), applies every module's migrations
/// (RLS policies included), seeds two orgs and their users, and hosts the API
/// in-process with the local auth provider (ADR 14). Clients authenticate
/// through the REAL cookie flow (ADR 21) - login redirect, code exchange,
/// session cookie - not a header hack.
/// The app connects as a NON-superuser role: table owners bypass RLS unless
/// FORCEd, and superusers always do - testing as postgres would test nothing.
/// </summary>
public class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(
        "postgres:17-alpine"
    ).Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public OrgId OrgA { get; } = OrgId.New();
    public OrgId OrgB { get; } = OrgId.New();
    public const string UserA = "user-a@premise.local"; // member: OrgA
    public const string UserB = "user-b@premise.local"; // member: OrgB
    public const string UserBoth = "user-ab@premise.local"; // member: OrgA + OrgB

    public virtual async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var adminCs = _postgres.GetConnectionString();
        await using (var tenancy = CreateTenancyContext(adminCs))
            await tenancy.Database.MigrateAsync();
        await using (var identity = CreateIdentityContext(adminCs))
            await identity.Database.MigrateAsync();

        await _postgres.ExecScriptAsync(
            """
            CREATE ROLE app_user LOGIN PASSWORD 'app_user' NOSUPERUSER;
            -- Wolverine owns its envelope schema; the app creates it at startup
            GRANT CREATE ON DATABASE postgres TO app_user;
            GRANT USAGE ON SCHEMA tenancy, identity TO app_user;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA tenancy TO app_user;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA identity TO app_user;
            """
        );
        var appCs = new Npgsql.NpgsqlConnectionStringBuilder(adminCs)
        {
            Username = "app_user",
            Password = "app_user",
        }.ConnectionString;

        await using (var seed = CreateTenancyContext(adminCs))
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
        await using (var seed = CreateIdentityContext(adminCs))
        {
            var a = AppUser.Create("local", UserA, UserA, "User A");
            var b = AppUser.Create("local", UserB, UserB, "User B");
            var both = AppUser.Create("local", UserBoth, UserBoth, "User AB");
            seed.Users.AddRange(a, b, both);
            seed.Memberships.AddRange(
                Membership.Create(a.Id, OrgA),
                Membership.Create(b.Id, OrgB),
                Membership.Create(both.Id, OrgA),
                Membership.Create(both.Id, OrgB)
            );
            await seed.SaveChangesAsync();
        }

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:premise", appCs);
            builder.UseSetting("Auth:Provider", "local");
            builder.UseEnvironment("Testing");
            ConfigureHost(builder);
        });
    }

    /// <summary>Subclass hook (e.g. the WorkOS-emulator fixture overrides auth settings).</summary>
    protected virtual void ConfigureHost(IWebHostBuilder builder) { }

    /// <summary>Fresh client authenticated as the given user via the real login flow.</summary>
    public async Task<HttpClient> LoginAsync(string email)
    {
        var client = Factory.CreateDefaultClient(
            new RedirectHandler(),
            new CookieContainerHandler()
        );
        var response = await client.GetAsync($"/auth/login?hint={Uri.EscapeDataString(email)}");
        response.EnsureSuccessStatusCode(); // followed: login -> provider -> callback -> /me
        return client;
    }

    /// <summary>Unauthenticated client: a Guest principal (ADR 7).</summary>
    public HttpClient GuestClient() =>
        Factory.CreateDefaultClient(new RedirectHandler(), new CookieContainerHandler());

    public async Task<List<(DateTimeOffset start, DateTimeOffset end)>> QueryWindows(Guid siteId)
    {
        await using var db = CreateTenancyContext(_postgres.GetConnectionString());
        var typed = new SiteId(siteId);
        return (
            await db
                .SiteOpenWindows.IgnoreQueryFilters()
                .Where(w => w.SiteId == typed)
                .OrderBy(w => w.StartsAtUtc)
                .Select(w => new { w.StartsAtUtc, w.EndsAtUtc })
                .ToListAsync()
        )
            .Select(w => (w.StartsAtUtc, w.EndsAtUtc))
            .ToList();
    }

    /// <summary>Debug helper: surface Wolverine dead-letter exceptions in test failures.</summary>
    public async Task<string> DeadLetterSummary()
    {
        await using var conn = new Npgsql.NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();
        try
        {
            await using var cmd = new Npgsql.NpgsqlCommand(
                """
                SELECT (SELECT count(*) FROM wolverine.wolverine_dead_letters),
                       (SELECT min(exception_type || ': ' || exception_message) FROM wolverine.wolverine_dead_letters),
                       (SELECT min(status || '/' || owner_id || '/' || message_type || '/' || coalesce(execution_time::text,'now')) FROM wolverine.wolverine_incoming_envelopes),
                       (SELECT count(*) FROM wolverine.wolverine_outgoing_envelopes)
                """,
                conn
            );
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            return $"dead={reader.GetInt64(0)} first={(reader.IsDBNull(1) ? "-" : reader.GetString(1))} incoming={(reader.IsDBNull(2) ? "-" : reader.GetString(2))} outgoing={reader.GetInt64(3)}";
        }
        catch (Npgsql.PostgresException e)
        {
            return "dead-letter query failed: " + e.MessageText;
        }
    }

    public async Task DeleteWindows(Guid siteId)
    {
        await using var db = CreateTenancyContext(_postgres.GetConnectionString());
        var typed = new SiteId(siteId);
        await db
            .SiteOpenWindows.IgnoreQueryFilters()
            .Where(w => w.SiteId == typed)
            .ExecuteDeleteAsync();
    }

    public async Task PublishForOrgA<T>(T message)
        where T : notnull
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<Wolverine.IMessageBus>();
        await Premise.Modules.Tenancy.TenantedMessaging.PublishForOrgAsync(bus, OrgA, message);
    }

    public async Task<Guid> SettingIdOf(OrgId org, string key)
    {
        await using var db = CreateTenancyContext(_postgres.GetConnectionString());
        return await db
            .OrganizationSettings.IgnoreQueryFilters()
            .Where(s => s.OrgId == org && s.Key == key)
            .Select(s => s.Id)
            .SingleAsync();
    }

    private static TenancyDbContext CreateTenancyContext(string cs) =>
        new(
            new DbContextOptionsBuilder<TenancyDbContext>()
                .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations_history", "tenancy"))
                .Options,
            new TenantContext()
        );

    private static IdentityDbContext CreateIdentityContext(string cs) =>
        new(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations_history", "identity"))
                .Options,
            new TenantContext()
        );

    public virtual async Task DisposeAsync()
    {
        if (Factory is not null)
            await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
