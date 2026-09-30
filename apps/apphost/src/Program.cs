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
// Parameters:keycloak-admin-password, Parameters:gateway-secret, Parameters:audit-log-secret-key,
// Parameters:demographics-encryption-key, Parameters:field-encryption-key, Parameters:field-lookup-hash-key) or as
// Parameters__<name> environment variables. Aspire prompts for any
// missing value in the dashboard instead of starting the container with a well-known default.
var minioRootUser = builder.AddParameter("minio-root-user");
var minioRootPassword = builder.AddParameter("minio-root-password", secret: true);
var keycloakAdmin = builder.AddParameter("keycloak-admin");
var keycloakAdminPassword = builder.AddParameter("keycloak-admin-password", secret: true);

// Shared service secrets (Parameters:gateway-secret must be at least 32 characters).
var gatewaySecret = builder.AddParameter("gateway-secret", secret: true);
var auditLogSecretKey = builder.AddParameter("audit-log-secret-key", secret: true);

// Execution refuses to start without a base64 AES key of 16, 24 or 32 bytes (`openssl rand -base64 32`).
var demographicsEncryptionKey = builder.AddParameter("demographics-encryption-key", secret: true);

// Secrets at rest, per ADR-2494. Econsent and payments encrypt fields with the field encryption key, and interop
// hashes device-token lookups with the lookup hash key. Both are base64 of 32 random bytes (`openssl rand -base64 32`).
var fieldEncryptionKey = builder.AddParameter("field-encryption-key", secret: true);
var fieldLookupHashKey = builder.AddParameter("field-lookup-hash-key", secret: true);

// Images are pinned to explicit release tags (same MinIO release as docker/docker-compose.yml).
// No service reads MinIO yet; it runs for storage work in progress, as in docker compose's storage profile.
builder.AddContainer("minio", "minio/minio", "RELEASE.2024-05-28T17-19-04Z")
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
    // The same realm docker compose imports: clients, roles and local test users (docs/LOCAL_DEV_ENVIRONMENT.md).
    .WithBindMount("../../../docker/cadence-realm.json", "/opt/keycloak/data/import/realm.json", isReadOnly: true)
    .WithArgs("start-dev", "--import-realm");

// Product services. Each one listens on the host port docker compose publishes it on (8001-8018), so the
// gateway's Development defaults, the web apps and docs/LOCAL_DEV_ENVIRONMENT.md hold for both. Every service
// verifies gateway-signed identity with GATEWAY_SECRET and refuses to start without it. Services backed by
// PostgreSQL get ConnectionStrings__cadence from the database reference, wait for PostgreSQL, and apply their
// own migrations on start, as in docker compose.
IResourceBuilder<ProjectResource> Service(IResourceBuilder<ProjectResource> project, int port, bool database = true)
{
    project
        .WithHttpEndpoint(port: port, name: "http")
        .WithHttpHealthCheck("/healthz/ready")
        .WithEnvironment("GATEWAY_SECRET", gatewaySecret);

    if (database)
    {
        project
            .WithEnvironment("AUDIT_LOG_SECRET_KEY", auditLogSecretKey)
            .WithEnvironment("Database__MigrateOnStartup", "true")
            .WithReference(cadenceDb)
            .WaitFor(cadenceDb);
    }

    return project;
}

var designer = Service(builder.AddProject<Projects.Cadence_Apps_Designer>("designer"), 8001, database: false);
var execution = Service(builder.AddProject<Projects.Cadence_Apps_Execution>("execution"), 8002)
    .WithEnvironment("DemographicsEncryption__KeyBase64", demographicsEncryptionKey);
var etmf = Service(builder.AddProject<Projects.Cadence_Apps_Etmf>("etmf"), 8003);
var interop = Service(builder.AddProject<Projects.Cadence_Apps_Interop>("interop"), 8004)
    .WithEnvironment("FIELD_LOOKUP_HASH_KEY", fieldLookupHashKey);
var quality = Service(builder.AddProject<Projects.Cadence_Apps_Quality>("quality"), 8005);
var notifications = Service(builder.AddProject<Projects.Cadence_Apps_Notifications>("notifications"), 8006);
var ctms = Service(builder.AddProject<Projects.Cadence_Apps_Ctms>("ctms"), 8007);
var safety = Service(builder.AddProject<Projects.Cadence_Apps_Safety>("safety"), 8008);
var tickets = Service(builder.AddProject<Projects.Cadence_Apps_Tickets>("tickets"), 8009);
var eisf = Service(builder.AddProject<Projects.Cadence_Apps_Eisf>("eisf"), 8010);
var econsent = Service(builder.AddProject<Projects.Cadence_Apps_Econsent>("econsent"), 8011)
    .WithEnvironment("FIELD_ENCRYPTION_KEY", fieldEncryptionKey);
var org = Service(builder.AddProject<Projects.Cadence_Apps_Org>("org"), 8012);
var fileshare = Service(builder.AddProject<Projects.Cadence_Apps_Fileshare>("fileshare"), 8013);
var imaging = Service(builder.AddProject<Projects.Cadence_Apps_Imaging>("imaging"), 8014);
var knowledge = Service(builder.AddProject<Projects.Cadence_Apps_Knowledge>("knowledge"), 8015);
var payments = Service(builder.AddProject<Projects.Cadence_Apps_Payments>("payments"), 8016)
    .WithEnvironment("FIELD_ENCRYPTION_KEY", fieldEncryptionKey);
var aiGateway = Service(builder.AddProject<Projects.Cadence_Apps_AiGateway>("ai-gateway"), 8017, database: false);
var kernel = Service(builder.AddProject<Projects.Cadence_Kernel_Host>("kernel"), 8018);

// The gateway is the API entry point the web apps call (VITE_API_BASE_URL=http://localhost:8000). It is told
// where every service is rather than relying on its Development localhost defaults.
var gateway = builder.AddProject<Projects.Cadence_Apps_Gateway>("gateway")
    .WithHttpEndpoint(port: 8000, name: "http")
    .WithHttpHealthCheck("/healthz/live")
    .WithEnvironment("GATEWAY_SECRET", gatewaySecret)
    .WithEnvironment("OIDC_AUTHORITY", ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/cadence"))
    .WithEnvironment("OIDC_REQUIRE_HTTPS_METADATA", "false");

foreach (var (variable, service) in new[]
{
    ("DESIGNER_SERVICE_URL", designer), ("EXECUTION_SERVICE_URL", execution), ("ETMF_SERVICE_URL", etmf),
    ("INTEROP_SERVICE_URL", interop), ("QUALITY_SERVICE_URL", quality), ("NOTIFICATIONS_SERVICE_URL", notifications),
    ("CTMS_SERVICE_URL", ctms), ("SAFETY_SERVICE_URL", safety), ("TICKETS_SERVICE_URL", tickets),
    ("EISF_SERVICE_URL", eisf), ("ECONSENT_SERVICE_URL", econsent), ("ORG_SERVICE_URL", org),
    ("FILESHARE_SERVICE_URL", fileshare), ("IMAGING_SERVICE_URL", imaging), ("KNOWLEDGE_SERVICE_URL", knowledge),
    ("PAYMENTS_SERVICE_URL", payments), ("AI_GATEWAY_SERVICE_URL", aiGateway), ("KERNEL_SERVICE_URL", kernel),
})
{
    gateway.WithEnvironment(variable, service.GetEndpoint("http"));
}

await builder.Build().RunAsync();
