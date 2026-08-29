using Premise.Modules.Tenancy;
using Premise.Platform.Data;
using Premise.Platform.Kernel;
using Wolverine;
using Wolverine.Http;
using Wolverine.Postgresql;

var builder = WebApplication.CreateBuilder(args);

// Role flag (ADR 34): one image, run as "api" or "worker".
var role = builder.Configuration["ROLE"] ?? "api";

// No ambient connection string (ADR 35): everything resolves through the
// region seam, single-region in v1.
builder.Services.AddSingleton<IRegionDataSources, SingleRegionDataSources>();

// Plain type registrations: Wolverine's codegen inlines them (no service location).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, Premise.Api.DevHeaderTenantContext>();

builder.Services.AddTenancyModule();
builder.Services.AddWolverineHttp();

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
});

var app = builder.Build();

if (role == "api")
{
    // PLACEHOLDER principal until step 2 (ADR 14): dev/test-only header tenant
    // resolution. Guarded so a production environment cannot boot with it.
    if (app.Environment.IsProduction())
        throw new InvalidOperationException(
            "Header-based tenant resolution is a step-1 placeholder; wire real "
                + "authentication (ADR 14) before running in Production."
        );

    app.MapWolverineEndpoints();
    app.MapGet("/healthz", () => Results.Ok(new { status = "ok", role }));
}

app.Run();

// Exposed for WebApplicationFactory in the integration/isolation suites.
public partial class Program;
