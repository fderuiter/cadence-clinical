using System;

using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Datastores
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();

var kernelDb = postgres.AddDatabase("cadence-kernel");
var executionDb = postgres.AddDatabase("cadence-edc");
var etmfDb = postgres.AddDatabase("cadence-etmf");
var ctmsDb = postgres.AddDatabase("cadence-ctms");
var qualityDb = postgres.AddDatabase("cadence-quality");

var minio = builder.AddContainer("minio", "minio/minio", "latest")
    .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "api")
    .WithHttpEndpoint(port: 9001, targetPort: 9001, name: "console")
    .WithEnvironment("MINIO_ROOT_USER", "minio_admin")
    .WithEnvironment("MINIO_ROOT_PASSWORD", "minio_password")
    .WithArgs("server", "/data", "--console-address", ":9001");

builder.AddContainer("keycloak", "quay.io/keycloak/keycloak", "26.0")
    .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "http")
    .WithEnvironment("KEYCLOAK_ADMIN", "admin")
    .WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", "admin_password")
    .WithEnvironment("KC_DB", "dev-file")
    .WithEnvironment("KC_HEALTH_ENABLED", "true")
    .WithArgs("start-dev");

// Product Services
builder.AddProject<Projects.Cadence_Apps_Gateway>("gateway");

builder.AddProject<Projects.Cadence_Apps_Execution>("execution")
    .WithReference(postgres)
    .WithReference(executionDb)
    .WithReference(kernelDb);

builder.AddProject<Projects.Cadence_Apps_Designer>("designer");

builder.AddProject<Projects.Cadence_Apps_Ctms>("ctms")
    .WithReference(postgres)
    .WithReference(ctmsDb)
    .WithReference(kernelDb);

builder.AddProject<Projects.Cadence_Apps_Etmf>("etmf")
    .WithReference(postgres)
    .WithReference(etmfDb)
    .WithReference(kernelDb);

builder.AddProject<Projects.Cadence_Apps_Quality>("quality")
    .WithReference(postgres)
    .WithReference(qualityDb)
    .WithReference(kernelDb);

builder.AddProject<Projects.Cadence_Apps_Fileshare>("fileshare")
    .WithEnvironment("STORAGE_ENDPOINT_URL", minio.GetEndpoint("api"));

await builder.Build().RunAsync();
