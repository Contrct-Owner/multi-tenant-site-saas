using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Premise.Modules.Identity.Auth;
using Premise.Platform.Auth;
using Premise.Platform.Data;
using Premise.Platform.Kernel;

namespace Premise.Api;

internal static class HttpPolicyHosting
{
    public static void AddRequestPolicies(this WebApplicationBuilder builder)
    {
        // Rate limiting (ADR 30): partitioned by principal tier. Guests limit on
        // their session cookie (fallback: IP), users on user id. The per-org quota
        // reading metered entitlements attaches in step 4.
        var guestLimit = builder.Configuration.GetValue("RateLimits:GuestPerMinute", 60);
        var userLimit = builder.Configuration.GetValue("RateLimits:UserPerMinute", 300);
        // ADR 52: a quota is one number across the fleet, so the org, user and
        // key partitions count in Postgres; guests and IPs stay per instance
        // (abuse control, not a sold quota). Off for the in-process test host,
        // which has one instance and no reason to pay a round trip per request.
        var shared = builder.Configuration.GetValue("RateLimits:Shared", true);
        var window = TimeSpan.FromMinutes(1);
        RateLimiter Counter(IServiceProvider services, string partition, int permits) =>
            shared
                ? new SharedRateLimiter(
                    partition,
                    permits,
                    window,
                    services.GetRequiredService<IRegionDataSources>(),
                    services.GetRequiredService<TimeProvider>(),
                    services.GetRequiredService<ILoggerFactory>().CreateLogger<SharedRateLimiter>()
                )
                : new FixedWindowRateLimiter(
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permits,
                        Window = window,
                        QueueLimit = 0,
                    }
                );
        builder.Services.AddSingleton<OrgRateLimitCache>();
        builder.Services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // consumers deserve to know when to come back: fixed one-minute windows,
            // so the limiter's own retry hint (when present) or the window size
            limiter.OnRejected = (context, _) =>
            {
                var seconds = context.Lease.TryGetMetadata(
                    System.Threading.RateLimiting.MetadataName.RetryAfter,
                    out var retryAfter
                )
                    ? Math.Max(1, (int)retryAfter.TotalSeconds)
                    : 60;
                context.HttpContext.Response.Headers.RetryAfter = seconds.ToString();
                return ValueTask.CompletedTask;
            };
            limiter.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                // ADR 30: org-level quota from the metered entitlement, over the per-principal limiter
                PartitionedRateLimiter.Create<HttpContext, string>(http =>
                {
                    // ONE resolver for "who is this request": the same Principal the
                    // endpoints see. This lambda used to re-parse claims and Items
                    // itself, and the two readings drifted - API keys fell into the
                    // per-IP guest bucket and skipped the org quota entirely.
                    var principal = http
                        .RequestServices.GetRequiredService<IPrincipalAccessor>()
                        .Current;
                    OrgId? org = principal switch
                    {
                        Principal.User { ActiveOrg: { } active } => active,
                        Principal.Service service => service.Org,
                        Principal.Contact contact => contact.Org,
                        _ => null,
                    };
                    if (org is { } quotaOrg)
                    {
                        var orgGuid = quotaOrg.Value;
                        var orgLimit = http
                            .RequestServices.GetRequiredService<OrgRateLimitCache>()
                            .LimitFor(quotaOrg);
                        // the limit is part of the KEY: partition limiters are
                        // created once and cached, so a quota change must roll to a
                        // fresh partition or a hot org keeps its old limit forever
                        // (found by the load baseline)
                        var partition = $"org:{orgGuid}:{orgLimit}";
                        return RateLimitPartition.Get(
                            partition,
                            _ => Counter(http.RequestServices, partition, orgLimit)
                        );
                    }
                    return RateLimitPartition.GetNoLimiter("org:none");
                }),
                PartitionedRateLimiter.Create<HttpContext, string>(http =>
                {
                    var (key, permits, fleetWide) = http
                        .RequestServices.GetRequiredService<IPrincipalAccessor>()
                        .Current switch
                    {
                        // an API key is a first-class principal (ADR 40): its own
                        // bucket at the USER limit, never the per-IP guest bucket
                        Principal.Service service => ($"key:{service.KeyId}", userLimit, true),
                        Principal.User user => ($"user:{user.UserId}", userLimit, true),
                        _ => http.Request.Cookies.TryGetValue(
                            GuestSessionMiddleware.CookieName,
                            out var guest
                        )
                            ? ($"guest:{guest}", guestLimit, false)
                            : ($"ip:{http.Connection.RemoteIpAddress}", guestLimit, false),
                    };
                    if (fleetWide)
                        return RateLimitPartition.Get(
                            key,
                            _ => Counter(http.RequestServices, key, permits)
                        );
                    return RateLimitPartition.GetFixedWindowLimiter(
                        key,
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = permits,
                            Window = window,
                            QueueLimit = 0,
                        }
                    );
                })
            );
        });
    }

    public static void UseRequestPolicies(this WebApplication app)
    {
        // Behind the documented TLS-terminating proxy the request arrives as
        // HTTP: without this, cookies lose the Secure flag and every URL built
        // from Request.Scheme/Host (billing returns, SSO portal returns) comes
        // out http://. Opt-in because trusting these headers from an UNKNOWN
        // peer lets clients spoof scheme/host/ip - only enable it when the
        // immediate proxy strips inbound X-Forwarded-* (reverse proxies do).
        if (app.Configuration.GetValue("Proxy:TrustForwardedHeaders", false))
        {
            var forwarded = new ForwardedHeadersOptions
            {
                ForwardedHeaders =
                    ForwardedHeaders.XForwardedFor
                    | ForwardedHeaders.XForwardedProto
                    | ForwardedHeaders.XForwardedHost,
            };
            forwarded.KnownIPNetworks.Clear(); // trust the immediate peer: the proxy
            forwarded.KnownProxies.Clear();
            app.UseForwardedHeaders(forwarded);
        }
        app.UseMiddleware<UnhandledErrorMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseWhen(
            context => context.Request.Path != "/livez" && context.Request.Path != "/healthz",
            api =>
            {
                api.UseMiddleware<PublicCacheMiddleware>();
                api.UseAuthentication();
                api.UseMiddleware<SessionValidationMiddleware>();
                api.UseMiddleware<ApiKeyAuthenticationMiddleware>();
                api.UseMiddleware<CsrfOriginMiddleware>();
                api.UseMiddleware<GuestSessionMiddleware>();
                api.UseMiddleware<GuestOrgMiddleware>();
                api.UseMiddleware<SessionContextMiddleware>();
                api.UseRateLimiter();
                api.UseMiddleware<SuspensionMiddleware>();
                api.UseMiddleware<IdempotencyMiddleware>();
                api.UseMiddleware<AccessLogMiddleware>();
            }
        );
    }
}
