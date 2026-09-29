using System;

using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Datastores
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();

// Clinical Kernel Modular Monolith: single relational database with schema partitioning (ADR-2244, ADR-2247)
var cadenceDb = postgres.AddDatabase("cadence");

// Credentials are Aspire parameters, never literals: set them with `dotnet user-secrets` on this project
// (Parameters:minio-root-user, Parameters:minio-root-password, Parameters:keycloak-admin,
// Parameters:keycloak-admin-password, Parameters:gateway-secret, Parameters:audit-log-secret-key) or as
// Parameters__<name> environment variables. Aspire prompts for any
// missing value in the dashboard instead of starting the container with a well-known default.
var minioRootUser = builder.AddParameter("minio-root-user");
var minioRootPassword = builder.AddParameter("minio-root-password", secret: true);
var keycloakAdmin = builder.AddParameter("keycloak-admin");
var keycloakAdminPassword = builder.AddParameter("keycloak-admin-password", secret: true);

// Shared service secrets (Parameters:gateway-secret must be at least 32 characters).
var gatewaySecret = builder.AddParameter("gateway-secret", secret: true);
var auditLogSecretKey = builder.AddParameter("audit-log-secret-key", secret: true);

// Images are pinned to explicit release tags (same MinIO release as docker/docker-compose.yml).
var minio = builder.AddContainer("minio", "minio/minio", "RELEASE.2024-05-28T17-19-04Z")
    .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "api")
    .WithHttpEndpoint(port: 9001, targetPort: 9001, name: "console")
    .WithEnvironment("MINIO_ROOT_USER", minioRootUser)
    .WithEnvironment("MINIO_ROOT_PASSWORD", minioRootPassword)
    .WithArgs("server", "/data", "--console-address", ":9001");

var keycloak = builder.AddContainer("keycloak", "quay.io/keycloak/keycloak", "26.0")
    .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "http")
    .WithEnvironment("KEYCLOAK_ADMIN", keycloakAdmin)
    .WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", keycloakAdminPassword)
    .WithEnvironment("KC_DB", "dev-file")
    .WithEnvironment("KC_HEALTH_ENABLED", "true")
    .WithArgs("start-dev");

// Product Services. Every service verifies gateway-signed identity with GATEWAY_SECRET and refuses to start
// without it; services backed by PostgreSQL get ConnectionStrings__cadence from the database reference.
builder.AddProject<Projects.Cadence_Apps_Gateway>("gateway")
    .WithEnvironment("GATEWAY_SECRET", gatewaySecret)
    .WithEnvironment("OIDC_AUTHORITY", ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/cadence"))
    .WithEnvironment("OIDC_REQUIRE_HTTPS_METADATA", "false");

builder.AddProject<Projects.Cadence_Apps_Execution>("execution")
    .WithEnvironment("GATEWAY_SECRET", gatewaySecret)
    .WithEnvironment("AUDIT_LOG_SECRET_KEY", auditLogSecretKey)
    .WithReference(postgres)
    .WithReference(cadenceDb);

builder.AddProject<Projects.Cadence_Apps_Designer>("designer")
    .WithEnvironment("GATEWAY_SECRET", gatewaySecret);

builder.AddProject<Projects.Cadence_Apps_Ctms>("ctms")
    .WithEnvironment("GATEWAY_SECRET", gatewaySecret)
    .WithEnvironment("AUDIT_LOG_SECRET_KEY", auditLogSecretKey)
    .WithReference(postgres)
    .WithReference(cadenceDb);

builder.AddProject<Projects.Cadence_Apps_Etmf>("etmf")
    .WithEnvironment("GATEWAY_SECRET", gatewaySecret)
    .WithEnvironment("AUDIT_LOG_SECRET_KEY", auditLogSecretKey)
    .WithReference(postgres)
    .WithReference(cadenceDb);

builder.AddProject<Projects.Cadence_Apps_Quality>("quality")
    .WithEnvironment("GATEWAY_SECRET", gatewaySecret)
    .WithEnvironment("AUDIT_LOG_SECRET_KEY", auditLogSecretKey)
    .WithReference(postgres)
    .WithReference(cadenceDb);

builder.AddProject<Projects.Cadence_Apps_Fileshare>("fileshare")
    .WithEnvironment("GATEWAY_SECRET", gatewaySecret)
    .WithEnvironment("AUDIT_LOG_SECRET_KEY", auditLogSecretKey)
    .WithEnvironment("STORAGE_ENDPOINT_URL", minio.GetEndpoint("api"))
    .WithReference(cadenceDb);

await builder.Build().RunAsync();
