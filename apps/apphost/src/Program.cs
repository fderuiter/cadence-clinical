using System;
using System.Collections.Generic;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.JavaScript;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Profiles (#4991). `full` (the default) runs every service. `core` runs what the Clinical Canvas calls today: the
// kernel API host, execution, the gateway and the web app. Choose one with `-- --AppHost:Profile=core` or
// AppHost__Profile=core.
string profile = (builder.Configuration["AppHost:Profile"] ?? "full").Trim().ToLowerInvariant();
if (profile is not ("full" or "core"))
{
    throw new InvalidOperationException($"AppHost:Profile must be 'full' or 'core' (was '{profile}').");
}

bool full = profile == "full";

// Credentials are Aspire parameters, never literals: set them with `dotnet user-secrets` on this project
// (Parameters:<name>) or as Parameters__<name> environment variables. Aspire prompts for any missing value in the
// dashboard instead of starting with a well-known default. Only the parameters the chosen resources use are
// declared, so a core run never asks for the MinIO or field-protection keys.

// Shared service secrets (Parameters:gateway-secret must be at least 32 characters).
var gatewaySecret = builder.AddParameter("gateway-secret", secret: true);
var auditLogSecretKey = builder.AddParameter("audit-log-secret-key", secret: true);

// Execution refuses to start without a base64 AES key of 16, 24 or 32 bytes (`openssl rand -base64 32`).
var demographicsEncryptionKey = builder.AddParameter("demographics-encryption-key", secret: true);

// Clinical Kernel Modular Monolith: single relational database with schema partitioning (ADR-2244, ADR-2247).
// When a cadence connection string is configured, the services use that existing PostgreSQL database and no
// container starts. Otherwise the AppHost runs PostgreSQL, with pgAdmin, in a container.
Func<IResourceBuilder<ProjectResource>, IResourceBuilder<ProjectResource>> useDatabase;
if (builder.Configuration.GetConnectionString("cadence") is { Length: > 0 })
{
    var existingDatabase = builder.AddConnectionString("cadence");
    useDatabase = project => project.WithReference(existingDatabase);
}
else
{
    var cadenceDb = builder.AddPostgres("postgres")
        .WithDataVolume()
        .WithPgAdmin()
        .AddDatabase("cadence");
    useDatabase = project => project.WithReference(cadenceDb).WaitFor(cadenceDb);
}

// Identity. With AppHost:OidcAuthority set to an existing Keycloak realm URL (http://host:port/realms/<realm>), the
// gateway and the web app use it and no container starts; otherwise the AppHost runs Keycloak with the same realm
// docker compose imports: clients, roles and local test users (docs/LOCAL_DEV_ENVIRONMENT.md). The gateway accepts
// only tokens with the API audience the realm gives cadence-web tokens (#5037); AppHost:OidcAudience overrides it for
// an existing realm.
const string ApiAudience = "cadence-api";
Action<IResourceBuilder<ProjectResource>> useOidcOnGateway;
Action<IResourceBuilder<ViteAppResource>> useOidcOnWeb;
if (builder.Configuration["AppHost:OidcAuthority"] is { Length: > 0 } authority)
{
    int realmStart = authority.IndexOf("/realms/", StringComparison.Ordinal);
    string realm = realmStart < 0 ? string.Empty : authority[(realmStart + "/realms/".Length)..].TrimEnd('/');
    if (!Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri)
        || authorityUri.Scheme is not ("http" or "https")
        || realm.Length == 0
        || realm.Contains('/', StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"AppHost:OidcAuthority must be a Keycloak realm URL such as http://localhost:8080/realms/cadence (was '{authority}').");
    }

    string keycloakUrl = authority[..realmStart];
    useOidcOnGateway = gateway => gateway
        .WithEnvironment("OIDC_AUTHORITY", authority.TrimEnd('/'))
        .WithEnvironment("OIDC_AUDIENCE", builder.Configuration["AppHost:OidcAudience"] is { Length: > 0 } audience ? audience : ApiAudience)
        .WithEnvironment("OIDC_REQUIRE_HTTPS_METADATA", (authorityUri.Scheme == "https").ToString().ToLowerInvariant());
    useOidcOnWeb = web => web
        .WithEnvironment("VITE_KEYCLOAK_URL", keycloakUrl)
        .WithEnvironment("VITE_KEYCLOAK_REALM", realm);
}
else
{
    var keycloakAdmin = builder.AddParameter("keycloak-admin");
    var keycloakAdminPassword = builder.AddParameter("keycloak-admin-password", secret: true);
    var keycloak = builder.AddContainer("keycloak", "quay.io/keycloak/keycloak", "26.0")
        .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "http")
        .WithEnvironment("KEYCLOAK_ADMIN", keycloakAdmin)
        .WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", keycloakAdminPassword)
        .WithEnvironment("KC_DB", "dev-file")
        .WithEnvironment("KC_HEALTH_ENABLED", "true")
        .WithBindMount("../../../docker/cadence-realm.json", "/opt/keycloak/data/import/realm.json", isReadOnly: true)
        .WithArgs("start-dev", "--import-realm");
    var keycloakHttp = keycloak.GetEndpoint("http");
    useOidcOnGateway = gateway => gateway
        .WithEnvironment("OIDC_AUTHORITY", ReferenceExpression.Create($"{keycloakHttp}/realms/cadence"))
        .WithEnvironment("OIDC_AUDIENCE", ApiAudience)
        .WithEnvironment("OIDC_REQUIRE_HTTPS_METADATA", "false");
    useOidcOnWeb = web => web
        .WithEnvironment("VITE_KEYCLOAK_URL", keycloakHttp)
        .WithEnvironment("VITE_KEYCLOAK_REALM", "cadence");
}

// Product services. Each one listens on the host port docker compose publishes it on (8001-8018), so the
// gateway's Development defaults, the web apps and docs/LOCAL_DEV_ENVIRONMENT.md hold for both. Every service
// verifies gateway-signed identity with GATEWAY_SECRET and refuses to start without it. Services backed by
// PostgreSQL get ConnectionStrings__cadence from the database reference and apply their own migrations on start,
// as in docker compose.
var routes = new List<(string Variable, IResourceBuilder<ProjectResource> Service)>();

IResourceBuilder<ProjectResource> Service(string variable, IResourceBuilder<ProjectResource> project, int port, bool database = true)
{
    project
        .WithHttpEndpoint(port: port, name: "http")
        .WithHttpHealthCheck("/healthz/ready")
        .WithEnvironment("GATEWAY_SECRET", gatewaySecret);

    if (database)
    {
        useDatabase(project
            .WithEnvironment("AUDIT_LOG_SECRET_KEY", auditLogSecretKey)
            .WithEnvironment("Database__MigrateOnStartup", "true"));
    }

    routes.Add((variable, project));
    return project;
}

Service("EXECUTION_SERVICE_URL", builder.AddProject<Projects.Cadence_Apps_Execution>("execution"), 8002)
    .WithEnvironment("DemographicsEncryption__KeyBase64", demographicsEncryptionKey);
// The local realm's users sign in with tenant code cadence-local (ADR-2514); the kernel host provisions that tenant on
// start, in Development only (ADR-2542). A project resource does not inherit the AppHost's environment, so the kernel
// is given it, and the tenant setting only when that environment is Development.
var kernel = Service("KERNEL_SERVICE_URL", builder.AddProject<Projects.Cadence_Kernel_Host>("kernel"), 8018)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName);
if (builder.Environment.IsDevelopment())
{
    kernel.WithEnvironment("Kernel__DevelopmentTenant__Code", "cadence-local");
}

if (full)
{
    // Secrets at rest, per ADR-2494. Econsent and payments encrypt fields with the field encryption key, and
    // interop hashes device-token lookups with the lookup hash key. Both are base64 of 32 random bytes.
    var fieldEncryptionKey = builder.AddParameter("field-encryption-key", secret: true);
    var fieldLookupHashKey = builder.AddParameter("field-lookup-hash-key", secret: true);
    var sceSigningKey = builder.AddParameter("sce-signing-key", secret: true);
    // Imaging signs de-identification certificates with one key and pseudonymizes DICOM UIDs with another (#5399).
    var imagingDeidSigningKey = builder.AddParameter("imaging-deid-signing-key", secret: true);
    var imagingDeidPseudonymizationKey = builder.AddParameter("imaging-deid-pseudonymization-key", secret: true);

    // Images are pinned to explicit release tags (same MinIO release as docker/docker-compose.yml). No service
    // reads MinIO yet; it runs for storage work in progress, as in docker compose's storage profile.
    var minioRootUser = builder.AddParameter("minio-root-user");
    var minioRootPassword = builder.AddParameter("minio-root-password", secret: true);
    builder.AddContainer("minio", "minio/minio", "RELEASE.2024-05-28T17-19-04Z")
        .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "api")
        .WithHttpEndpoint(port: 9001, targetPort: 9001, name: "console")
        .WithEnvironment("MINIO_ROOT_USER", minioRootUser)
        .WithEnvironment("MINIO_ROOT_PASSWORD", minioRootPassword)
        .WithArgs("server", "/data", "--console-address", ":9001");

    Service("DESIGNER_SERVICE_URL", builder.AddProject<Projects.Cadence_Apps_Designer>("designer"), 8001, database: false);
    Service("ETMF_SERVICE_URL", builder.AddProject<Projects.Cadence_Apps_Etmf>("etmf"), 8003);
    Service("INTEROP_SERVICE_URL", builder.AddProject<Projects.Cadence_Apps_Interop>("interop"), 8004)
        .WithEnvironment("FIELD_LOOKUP_HASH_KEY", fieldLookupHashKey)
        .WithEnvironment("CADENCE_SCE_SIGNING_KEY", sceSigningKey);
    Service("QUALITY_SERVICE_URL", builder.AddProject<Projects.Cadence_Apps_Quality>("quality"), 8005);
    Service("NOTIFICATIONS_SERVICE_URL", builder.AddProject<Projects.Cadence_Apps_Notifications>("notifications"), 8006);
    Service("CTMS_SERVICE_URL", builder.AddProject<Projects.Cadence_Apps_Ctms>("ctms"), 8007);
    Service("SAFETY_SERVICE_URL", builder.AddProject<Projects.Cadence_Apps_Safety>("safety"), 8008);
    Service("TICKETS_SERVICE_URL", builder.AddProject<Projects.Cadence_Apps_Tickets>("tickets"), 8009);
    Service("EISF_SERVICE_URL", builder.AddProject<Projects.Cadence_Apps_Eisf>("eisf"), 8010);
    Service("ECONSENT_SERVICE_URL", builder.AddProject<Projects.Cadence_Apps_Econsent>("econsent"), 8011)
        .WithEnvironment("FIELD_ENCRYPTION_KEY", fieldEncryptionKey);
    Service("ORG_SERVICE_URL", builder.AddProject<Projects.Cadence_Apps_Org>("org"), 8012);
    Service("FILESHARE_SERVICE_URL", builder.AddProject<Projects.Cadence_Apps_Fileshare>("fileshare"), 8013);
    Service("IMAGING_SERVICE_URL", builder.AddProject<Projects.Cadence_Apps_Imaging>("imaging"), 8014)
        .WithEnvironment("CADENCE_IMAGING_DEID_SIGNING_KEY", imagingDeidSigningKey)
        .WithEnvironment("CADENCE_IMAGING_DEID_PSEUDONYMIZATION_KEY", imagingDeidPseudonymizationKey);
    Service("KNOWLEDGE_SERVICE_URL", builder.AddProject<Projects.Cadence_Apps_Knowledge>("knowledge"), 8015);
    Service("PAYMENTS_SERVICE_URL", builder.AddProject<Projects.Cadence_Apps_Payments>("payments"), 8016)
        .WithEnvironment("FIELD_ENCRYPTION_KEY", fieldEncryptionKey);
    Service("AI_GATEWAY_SERVICE_URL", builder.AddProject<Projects.Cadence_Apps_AiGateway>("ai-gateway"), 8017, database: false);
}

// The gateway is the API entry point the web apps call, through the web dev server's /api proxy. Outside
// Development it refuses to start without every *_SERVICE_URL, so it is told where each running service is, and a
// service the profile leaves out gets its usual host port, where nothing listens: its routes answer as that
// service being down.
var gateway = builder.AddProject<Projects.Cadence_Apps_Gateway>("gateway")
    .WithHttpEndpoint(port: 8000, name: "http")
    .WithHttpHealthCheck("/healthz/live")
    .WithEnvironment("GATEWAY_SECRET", gatewaySecret);
useOidcOnGateway(gateway);
var routed = new HashSet<string>(StringComparer.Ordinal);
foreach (var (variable, service) in routes)
{
    gateway.WithEnvironment(variable, service.GetEndpoint("http"));
    routed.Add(variable);
}

foreach (var (variable, port) in new[]
{
    ("DESIGNER_SERVICE_URL", 8001), ("EXECUTION_SERVICE_URL", 8002), ("ETMF_SERVICE_URL", 8003),
    ("INTEROP_SERVICE_URL", 8004), ("QUALITY_SERVICE_URL", 8005), ("NOTIFICATIONS_SERVICE_URL", 8006),
    ("CTMS_SERVICE_URL", 8007), ("SAFETY_SERVICE_URL", 8008), ("TICKETS_SERVICE_URL", 8009),
    ("EISF_SERVICE_URL", 8010), ("ECONSENT_SERVICE_URL", 8011), ("ORG_SERVICE_URL", 8012),
    ("FILESHARE_SERVICE_URL", 8013), ("IMAGING_SERVICE_URL", 8014), ("KNOWLEDGE_SERVICE_URL", 8015),
    ("PAYMENTS_SERVICE_URL", 8016), ("AI_GATEWAY_SERVICE_URL", 8017), ("KERNEL_SERVICE_URL", 8018),
})
{
    if (!routed.Contains(variable))
    {
        string url = $"http://localhost:{port}";
        gateway.WithEnvironment(variable, url);
    }
}

// The Clinical Canvas web app (Vite dev server) on port 3000, the redirect URI the cadence-web Keycloak client
// allows. Run `pnpm install` at the repository root first; the AppHost does not install packages.
// The browser calls the web app's own origin and Vite's /api proxy forwards to the gateway (CADENCE_GATEWAY_URL):
// the gateway sends no CORS headers, so a page on :3000 calling :8000 directly is blocked by the browser (#5429).
var web = builder.AddViteApp("web", "../../web")
    .WithPnpm(install: false)
    .WithEndpoint("http", endpoint => endpoint.Port = 3000)
    .WithEnvironment("CADENCE_GATEWAY_URL", gateway.GetEndpoint("http"))
    .WithEnvironment("VITE_KEYCLOAK_CLIENT_ID", "cadence-web");
web.WithEnvironment("VITE_API_BASE_URL", web.GetEndpoint("http"));
useOidcOnWeb(web);

// The participant-facing subject portal (Vite dev server) on port 5174, as in docker compose, in the full profile only:
// it calls interop and payments, which the core profile does not run (#5335). Like the web app, it calls its own origin
// and its /api proxy forwards to the gateway (#5451).
if (full)
{
    var subjectPortal = builder.AddViteApp("subject-portal", "../../subject-portal")
        .WithPnpm(install: false)
        .WithEndpoint("http", endpoint => endpoint.Port = 5174)
        .WithEnvironment("CADENCE_GATEWAY_URL", gateway.GetEndpoint("http"))
        .WithEnvironment("VITE_KEYCLOAK_CLIENT_ID", "cadence-web");
    subjectPortal.WithEnvironment("VITE_API_BASE_URL", subjectPortal.GetEndpoint("http"));
    useOidcOnWeb(subjectPortal);
}

await builder.Build().RunAsync();
