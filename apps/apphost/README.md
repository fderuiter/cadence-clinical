# Aspire AppHost

.NET Aspire orchestration host for local development. It starts PostgreSQL, MinIO and Keycloak containers and every .NET service, and exposes their logs, traces and metrics on the Aspire dashboard.

## Project

- Project: `apps/apphost/src/Cadence.AppHost.csproj` (assembly `Cadence.AppHost`, `Aspire.AppHost.Sdk`).
- `Program.cs`: the resource graph.
- `Properties/launchSettings.json`: `https` (dashboard on `https://localhost:17000`) and `http` (`http://localhost:15000`) profiles, both in the Development environment.
- `appsettings.json`, `appsettings.Development.json`: logging levels only.

## Resources

- `postgres` (with a data volume and pgAdmin) and one database named `cadence`, shared by all services per [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md).
- `minio` container (ports 9000 and 9001; no service reads it yet) and `keycloak` container (port 8080, `start-dev`, importing `docker/cadence-realm.json` as docker compose does).
- Projects, each on the host port docker compose publishes it on, so the gateway's Development defaults and the
  web apps' `VITE_API_BASE_URL=http://localhost:8000` work for both:

  | Port | Service | Port | Service | Port | Service |
  | --- | --- | --- | --- | --- | --- |
  | 8000 | gateway | 8006 | notifications | 8012 | org |
  | 8001 | designer | 8007 | ctms | 8013 | fileshare |
  | 8002 | execution | 8008 | safety | 8014 | imaging |
  | 8003 | etmf | 8009 | tickets | 8015 | knowledge |
  | 8004 | interop | 8010 | eisf | 8016 | payments |
  | 8005 | quality | 8011 | econsent | 8017 | ai-gateway |
  |  |  |  |  | 8018 | kernel |

- Every service except the gateway, designer and ai-gateway references the `cadence` database
  (`ConnectionStrings:cadence`), waits for PostgreSQL, and applies its own migrations on start
  (`Database__MigrateOnStartup=true`), as in docker compose. Each project has a health check on
  `/healthz/ready` (the gateway on `/healthz/live`), shown in the dashboard.
- The gateway receives every `*_SERVICE_URL` from the service endpoints above.

Aspire sets `OTEL_EXPORTER_OTLP_ENDPOINT` on each project; [packages/telemetry](../../packages/telemetry/README.md) enables OTLP export when that variable is present, so service telemetry reaches the dashboard.

## Configuration

Credentials have no default value (ADR-2260). These are Aspire secret parameters:

- the MinIO root user and password, and the Keycloak admin user and password;
- `gateway-secret`, passed to every service as `GATEWAY_SECRET` (at least 32 characters);
- `audit-log-secret-key`, passed to every database-backed service as `AUDIT_LOG_SECRET_KEY`;
- `demographics-encryption-key`, passed to execution as `DemographicsEncryption__KeyBase64` (base64 AES key of
  16, 24 or 32 bytes; execution refuses to start without one, ADR-2490);
- `field-encryption-key`, passed to econsent and payments as `FIELD_ENCRYPTION_KEY`, and `field-lookup-hash-key`,
  passed to interop as `FIELD_LOOKUP_HASH_KEY` (base64 of 32 bytes each, ADR-2494).

The gateway's `OIDC_AUTHORITY` is the Keycloak `cadence` realm. Set the parameters once with user secrets (choose your own values):

```bash
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:minio-root-user" "<user>"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:minio-root-password" "<password>"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:keycloak-admin" "<user>"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:keycloak-admin-password" "<password>"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:gateway-secret" "<at least 32 characters>"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:audit-log-secret-key" "<secret>"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:demographics-encryption-key" "$(openssl rand -base64 32)"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:field-encryption-key" "$(openssl rand -base64 32)"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:field-lookup-hash-key" "$(openssl rand -base64 32)"
```

Or export `Parameters__minio-root-user` (and so on) as environment variables. If a value is missing, the Aspire
dashboard prompts for it before starting the containers.

## Key dependencies

- Packages: `Aspire.Hosting.AppHost`, `Aspire.Hosting.PostgreSQL`.
- Project references: every service project above.

## Tests

`tests/Cadence.UnitTests/AppHost/AppHostModelTests.cs` builds the resource graph with `Aspire.Hosting.Testing`, without starting anything, and checks that every service is present on its port, gets the settings it refuses to start without, and that the gateway is told every service URL.

## Run locally

```bash
dotnet run --project apps/apphost/src/Cadence.AppHost.csproj
```

Requires a container runtime (Docker or Podman) for PostgreSQL, MinIO and Keycloak.
