# Aspire AppHost

.NET Aspire orchestration host for local development. It starts PostgreSQL, MinIO and Keycloak containers and a subset of the .NET services, and exposes their logs, traces and metrics on the Aspire dashboard.

## Project

- Project: `apps/apphost/src/Cadence.AppHost.csproj` (assembly `Cadence.AppHost`, `Aspire.AppHost.Sdk`).
- `Program.cs`: the resource graph.
- `Properties/launchSettings.json`: `https` (dashboard on `https://localhost:17000`) and `http` (`http://localhost:15000`) profiles, both in the Development environment.
- `appsettings.json`, `appsettings.Development.json`: logging levels only.

## Resources

- `postgres` (with a data volume and pgAdmin) and one database named `cadence`, shared by all services per [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md).
- `minio` container (ports 9000 and 9001) and `keycloak` container (port 8080, `start-dev`).
- Projects: `gateway`, `designer`, `execution`, `ctms`, `etmf`, `quality` and `fileshare`. `execution`, `ctms`, `etmf`, `quality` and `fileshare` reference the `cadence` database, which supplies `ConnectionStrings:cadence`; `fileshare` also receives `STORAGE_ENDPOINT_URL` for MinIO.
- Not wired here: ai_gateway, econsent, eisf, imaging, interop, knowledge, notifications, org, payments, safety, tickets, and the frontends.

Aspire sets `OTEL_EXPORTER_OTLP_ENDPOINT` on each project; [packages/telemetry](../../packages/telemetry/README.md) enables OTLP export when that variable is present, so service telemetry reaches the dashboard.

## Configuration

Credentials have no default value (ADR-2260). The MinIO root user and password, the Keycloak admin user and
password, the shared `GATEWAY_SECRET`, the `AUDIT_LOG_SECRET_KEY` and execution's demographics encryption key are
Aspire secret parameters. The AppHost passes `GATEWAY_SECRET` and `AUDIT_LOG_SECRET_KEY` to every service, passes
the demographics key to execution as `DemographicsEncryption__KeyBase64` (execution refuses to start without a
base64 AES key of 16, 24 or 32 bytes, ADR-2490), and sets the gateway's `OIDC_AUTHORITY` to the Keycloak `cadence`
realm. Set the parameters once with user secrets (choose your own values):

```bash
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:minio-root-user" "<user>"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:minio-root-password" "<password>"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:keycloak-admin" "<user>"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:keycloak-admin-password" "<password>"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:gateway-secret" "<at least 32 characters>"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:audit-log-secret-key" "<secret>"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:demographics-encryption-key" "$(openssl rand -base64 32)"
```

Or export `Parameters__minio-root-user` (and so on) as environment variables. If a value is missing, the Aspire
dashboard prompts for it before starting the containers. The AppHost does not import the `cadence` Keycloak realm;
import `docker/cadence-realm.json` before signing in.

## Key dependencies

- Packages: `Aspire.Hosting.AppHost`, `Aspire.Hosting.PostgreSQL`.
- Project references: the seven orchestrated service projects above.

## Tests

No tests exist for the AppHost.

## Run locally

```bash
dotnet run --project apps/apphost/src/Cadence.AppHost.csproj
```

Requires a container runtime (Docker or Podman) for PostgreSQL, MinIO and Keycloak.
