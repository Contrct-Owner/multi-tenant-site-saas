// Local orchestration (ADR 34): one command boots Postgres, the API, and a
// worker instance of the same project (ADR 34's role flag), with the Aspire
// dashboard as the OTLP sink (ADR 33). Deployment stays vanilla: one OCI
// image, ROLE=api|worker.
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder
    .AddPostgres("postgres")
    .WithDataVolume("premise-pgdata")
    .AddDatabase("premise");

builder
    .AddProject<Projects.Premise_Api>("api")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithEnvironment("ROLE", "api");

builder
    .AddProject<Projects.Premise_Api>("worker")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithEnvironment("ROLE", "worker");

builder.Build().Run();
