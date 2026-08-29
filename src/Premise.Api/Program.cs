using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Premise.Api;
using Premise.Integrations.WorkOS;
using Premise.Modules.Identity;
using Premise.Modules.Identity.Auth;
using Premise.Modules.Tenancy;
using Premise.Platform.Auth;
using Premise.Platform.Data;
using Premise.Platform.Kernel;
using Premise.Platform.Notifications;
using Wolverine;
using Wolverine.Http;
using Wolverine.Postgresql;

var builder = WebApplication.CreateBuilder(args);

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

// The third gate (scope). Step-4 grants replace this implementation.
builder.Services.AddSingleton<IScopeResolver, MembershipScopeResolver>();

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
builder.Services.AddWolverineHttp();

// Notifications (ADR 32): local catcher unless a fork wires a real transport.
builder.Services.AddSingleton<INotificationTransport, LocalMailCatcher>();

// Rate limiting (ADR 30): partitioned by principal tier. Guests limit on
// their session cookie (fallback: IP), users on user id. The per-org quota
// reading metered entitlements attaches in step 4.
var guestLimit = builder.Configuration.GetValue("RateLimits:GuestPerMinute", 60);
var userLimit = builder.Configuration.GetValue("RateLimits:UserPerMinute", 300);
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
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
    });
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
});

var app = builder.Build();

if (role == "api")
{
    app.UseAuthentication();
    app.UseMiddleware<GuestSessionMiddleware>();
    app.UseMiddleware<GuestOrgMiddleware>();
    app.UseRateLimiter();

    app.MapIdentityEndpoints();
    app.MapContactLinkEndpoints();
    app.MapWolverineEndpoints();
    app.MapGet("/healthz", () => Results.Ok(new { status = "ok", role }));
}

app.Run();

// Exposed for WebApplicationFactory in the integration/isolation suites.
public partial class Program;
