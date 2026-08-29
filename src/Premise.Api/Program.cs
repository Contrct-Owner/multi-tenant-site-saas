using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Premise.Api;
using Premise.Integrations.WorkOS;
using Premise.Modules.Audit;
using Premise.Modules.Entitlements;
using Premise.Modules.Identity;
using Premise.Modules.Identity.Auth;
using Premise.Modules.Ingest;
using Premise.Modules.Storage;
using Premise.Modules.Tenancy;
using Premise.Platform.Audit;
using Premise.Platform.Auth;
using Premise.Platform.Data;
using Premise.Platform.Infra;
using Premise.Platform.Kernel;
using Premise.Platform.Notifications;
using Premise.Platform.Secrets;
using Premise.Platform.Storage;
using Wolverine;
using Wolverine.Http;
using Wolverine.Postgresql;

var builder = WebApplication.CreateBuilder(args);

// Scope validation ALWAYS (not just Development): singleton captures of
// scoped services are tenant-leak bugs; fail on them in every environment.
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateScopes = true;
    o.ValidateOnBuild = false; // Wolverine registers open generics that trip build validation
});

// Role flag (ADR 34): one image, run as "api" or "worker".
var role = builder.Configuration["ROLE"] ?? "api";

// No ambient connection string (ADR 35): everything resolves through the
// region seam, single-region in v1.
builder.Services.AddSingleton<IRegionDataSources, SingleRegionDataSources>();

// Principals (ADR 7): read-time resolution, usable from any scope.
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IPrincipalAccessor, RequestPrincipalAccessor>();
builder.Services.AddScoped<TenantContext>(); // envelope-tenant holder (ADR 24)
builder.Services.AddScoped<ITenantContext, PrincipalTenantContext>();
builder.Services.AddSingleton(TimeProvider.System);

// Gates 2+3: roles compile to grants; scope evaluated per request (ADR 6),
// decorated with authz-decision audit (ADR 12: denials always).
builder.Services.AddScoped<Premise.Modules.Identity.Access.GrantScopeResolver>();
builder.Services.AddScoped<IScopeResolver, AuditedScopeResolver>();
builder.Services.AddSingleton<AuditPolicyCache>();
builder.Services.AddSingleton<AuditSaveChangesInterceptor>();

// Auth seam (ADR 14): provider selected by config; WorkOS is the built-in
// full-capability implementation, local is the dev/test base implementation.
var authProvider = builder.Configuration["Auth:Provider"] ?? "local";
switch (authProvider)
{
    case "workos":
        builder.Services.Configure<WorkOSOptions>(builder.Configuration.GetSection("Auth:WorkOS"));
        builder.Services.AddSingleton<IAuthProvider, WorkOSAuthProvider>();
        break;
    case "local" when !builder.Environment.IsProduction():
        builder.Services.AddSingleton<IAuthProvider, LocalAuthProvider>();
        break;
    default:
        throw new InvalidOperationException(
            $"Auth:Provider '{authProvider}' is not valid for {builder.Environment.EnvironmentName}. "
                + "Use 'workos' in Production; 'local' is dev/test only (ADR 14)."
        );
}

// Cookie session (ADR 21): HttpOnly, no token ever reachable from JS.
builder
    .Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "premise_session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        // API, not a browser app: never redirect to a login page.
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = 401;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = 403;
            return Task.CompletedTask;
        };
    });

builder.Services.AddTenancyModule(runBackgroundWork: role == "worker");
builder.Services.AddIdentityModule();
builder.Services.AddEntitlementsModule(runBackgroundWork: role == "worker");
builder.Services.AddAuditModule(runBackgroundWork: role == "worker");
builder.Services.AddStorageModule();
builder.Services.AddIngestModule();

// Platform infra context (idempotency, ADR 29)
builder.Services.AddDbContext<PlatformDbContext>(
    (sp, options) =>
    {
        // singleton options: default region only (see module registrations / ADR 35)
        var regions = sp.GetRequiredService<IRegionDataSources>();
        options
            .UseNpgsql(
                regions.For(RegionId.Default),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "platform")
            )
            .AddInterceptors(TenantSessionInterceptor.Instance);
    }
);
if (role == "worker")
    builder.Services.AddHostedService<IdempotencyCleanupService>();

// Object storage (ADR 19): local adapter by default; forks point
// Storage:Adapter at s3 (Premise.Integrations.AmazonS3) or their own.
builder.Services.AddSingleton<IObjectStore, LocalObjectStore>();
builder.Services.AddSingleton<IVirusScanner, EicarScanner>();

// Secrets (ADR 31): local wrapper is DEV/TEST ONLY - Production must boot a KMS.
if (builder.Configuration["Secrets:LocalMasterKey"] is { } localKey)
{
    if (builder.Environment.IsProduction())
        throw new InvalidOperationException(
            "LocalKeyWrapper is dev/test only (ADR 31); configure a cloud KMS adapter."
        );
    builder.Services.AddSingleton<IKeyWrapper>(
        new LocalKeyWrapper(Convert.FromBase64String(localKey))
    );
}
builder.Services.AddWolverineHttp();
builder.Services.AddOpenApi(); // ADR 16: the spec is the contract; TS client + keys generate from it

// Notifications (ADR 32): local catcher unless a fork wires a real transport.
builder.Services.AddSingleton<INotificationTransport, LocalMailCatcher>();

// Rate limiting (ADR 30): partitioned by principal tier. Guests limit on
// their session cookie (fallback: IP), users on user id. The per-org quota
// reading metered entitlements attaches in step 4.
var guestLimit = builder.Configuration.GetValue("RateLimits:GuestPerMinute", 60);
var userLimit = builder.Configuration.GetValue("RateLimits:UserPerMinute", 300);
builder.Services.AddSingleton<OrgRateLimitCache>();
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        // ADR 30: org-level quota from the metered entitlement, over the per-principal limiter
        PartitionedRateLimiter.Create<HttpContext, string>(http =>
        {
            if (
                http.User.FindFirst(Premise.Modules.Identity.Auth.PremiseClaims.ActiveOrg)?.Value
                    is { } activeOrg
                && Guid.TryParse(activeOrg, out var orgGuid)
            )
            {
                var orgLimit = http
                    .RequestServices.GetRequiredService<OrgRateLimitCache>()
                    .LimitFor(new OrgId(orgGuid));
                return RateLimitPartition.GetFixedWindowLimiter(
                    $"org:{orgGuid}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = orgLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }
                );
            }
            return RateLimitPartition.GetNoLimiter("org:none");
        }),
        PartitionedRateLimiter.Create<HttpContext, string>(http =>
        {
            var (key, permits) =
                http.User.FindFirst(Premise.Modules.Identity.Auth.PremiseClaims.UserId)?.Value
                    is { } userId
                    ? ($"user:{userId}", userLimit)
                : http.Request.Cookies.TryGetValue(GuestSessionMiddleware.CookieName, out var guest)
                    ? ($"guest:{guest}", guestLimit)
                : ($"ip:{http.Connection.RemoteIpAddress}", guestLimit);
            return RateLimitPartition.GetFixedWindowLimiter(
                key,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permits,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }
            );
        })
    );
});

// Wolverine (ADR 23): mediation + messaging + durable Postgres outbox.
builder.UseWolverine(opts =>
{
    var cs =
        builder.Configuration.GetConnectionString("premise")
        ?? throw new InvalidOperationException("Missing connection string 'premise'.");
    opts.PersistMessagesWithPostgresql(cs, "wolverine");
    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableLocalQueues();
    opts.Discovery.IncludeAssembly(typeof(TenancyModule).Assembly);
    opts.Discovery.IncludeAssembly(typeof(IdentityModule).Assembly);
    opts.Discovery.IncludeAssembly(typeof(EntitlementsModule).Assembly);
    opts.Discovery.IncludeAssembly(typeof(AuditModule).Assembly);
    opts.Discovery.IncludeAssembly(typeof(StorageModule).Assembly);
    opts.Discovery.IncludeAssembly(typeof(IngestModule).Assembly);
});

var bootstraps = builder.Environment.IsDevelopment() && role == "api";
builder.Services.AddSingleton(new ReadinessState(ready: !bootstraps));
if (bootstraps)
    builder.Services.AddHostedService<DevBootstrap>(); // migrate + seed for `aspire run`

var app = builder.Build();

if (role == "api")
{
    app.UseAuthentication();
    app.UseMiddleware<GuestSessionMiddleware>();
    app.UseMiddleware<GuestOrgMiddleware>();
    app.UseRateLimiter();
    app.UseMiddleware<SuspensionMiddleware>();
    app.UseMiddleware<IdempotencyMiddleware>();
    app.UseMiddleware<AccessLogMiddleware>();
    app.MapLocalObjectStore();

    app.MapOpenApi();
    app.MapIdentityEndpoints();
    app.MapContactLinkEndpoints();
    app.MapWolverineEndpoints();
    app.MapGet(
        "/healthz",
        (ReadinessState readiness) =>
            readiness.Ready
                ? Results.Ok(new { status = "ok", role })
                : Results.Json(
                    new { status = "starting", role },
                    statusCode: StatusCodes.Status503ServiceUnavailable
                )
    );
}

app.Run();

// Exposed for WebApplicationFactory in the integration/isolation suites.
public partial class Program;
