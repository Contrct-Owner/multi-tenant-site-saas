// Local orchestration (ADR 34): one command boots Postgres, the WorkOS
// emulator, the API, and a worker instance of the same project (the role
// flag), with the Aspire dashboard as the OTLP sink (ADR 33). Local dev runs
// the REAL WorkOS adapter (ADR 14) against @workos/emulate - the local
// provider remains for bare `dotnet run` and the test suites.
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder
    .AddPostgres("postgres")
    .WithDataVolume("premise-pgdata")
    .AddDatabase("premise");

var workos = builder
    .AddContainer("workos", "ghcr.io/workos/emulate", "latest")
    .WithHttpEndpoint(port: 4100, targetPort: 4100)
    .WithBindMount(
        "../../workos-emulate.config.yaml",
        "/app/workos-emulate.config.yaml",
        isReadOnly: true
    )
    .WithArgs("--host", "0.0.0.0", "--interactive"); // serve real login pages

var workosEndpoint = workos.GetEndpoint("http");

var api = builder
    .AddProject<Projects.Premise_Api>("api")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WaitFor(workos)
    .WithEnvironment("ROLE", "api")
    .WithEnvironment("Auth__Provider", "workos")
    .WithEnvironment("Auth__WorkOS__ApiKey", "sk_test_default")
    .WithEnvironment("Auth__WorkOS__ClientId", "client_premise_dev")
    .WithEnvironment("Auth__WorkOS__ApiBaseUrl", workosEndpoint);

builder
    .AddNpmApp("console", "../../web/apps/console", "dev")
    // unproxied: executables cannot be proxied onto their own target port;
    // vite binds 5173 directly and reads it from PORT
    .WithHttpEndpoint(env: "PORT", port: 5173, isProxied: false)
    .WithEnvironment("PREMISE_API", api.GetEndpoint("http"))
    .WaitFor(api);

builder
    .AddProject<Projects.Premise_Api>("worker")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithEnvironment("ROLE", "worker");

builder.Build().Run();
