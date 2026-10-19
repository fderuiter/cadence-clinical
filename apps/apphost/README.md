# Aspire AppHost

.NET Aspire orchestration host for local development. By default it starts PostgreSQL, MinIO and Keycloak containers, every .NET service and the web app, and exposes their logs, traces and metrics on the Aspire dashboard. A `core` profile runs only what the Clinical Canvas calls, and it can use an existing PostgreSQL and Keycloak instead of containers, so it also runs where there is no container runtime (#4991).

## Project

- Project: `apps/apphost/src/Cadence.AppHost.csproj` (assembly `Cadence.AppHost`, `Aspire.AppHost.Sdk`).
- `Program.cs`: the resource graph.
- `Properties/launchSettings.json`: `https` (dashboard on `https://localhost:17000`) and `http` (`http://localhost:15000`) profiles, both in the Development environment.
- `appsettings.json`, `appsettings.Development.json`: logging levels only.

## Profiles and existing infrastructure

| Setting                     | Values                                                     | Effect                                                                                                                                                                 |
| --------------------------- | ---------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `AppHost:Profile`           | `full` (default), `core`                                   | `core` runs the gateway, the kernel API host, execution and the web app. `full` adds the other 15 services, MinIO and the subject portal.                              |
| `ConnectionStrings:cadence` | an Npgsql connection string                                | Use this existing database instead of starting the PostgreSQL container. The services apply their migrations to it.                                                    |
| `AppHost:OidcAuthority`     | a Keycloak realm URL, `http(s)://host:port/realms/<realm>` | Use this issuer for the gateway and the web app instead of starting the Keycloak container. The realm needs the `cadence-web` client from `docker/cadence-realm.json`. |

Pass them on the command line after `--` (`-- --AppHost:Profile=core`) or as environment variables
(`AppHost__Profile=core`). An unknown profile or a malformed authority stops the AppHost with a message. In the
`core` profile the gateway is given every other service's usual host port, where nothing listens, so those
routes answer as the service being down.

## Resources

- `postgres` (with a data volume and pgAdmin) and one database named `cadence`, shared by all services per [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md). With `ConnectionStrings:cadence` set, `cadence` is that existing database and no container runs.
- `keycloak` container (port 8080, `start-dev`, importing `docker/cadence-realm.json` as docker compose does), unless `AppHost:OidcAuthority` is set.
- `minio` container (ports 9000 and 9001; no service reads it yet), in the `full` profile only.
- `web`: the Clinical Canvas Vite dev server on `http://localhost:3000/cadence-clinical/`, the redirect URI the
  `cadence-web` Keycloak client allows. It gets `VITE_API_BASE_URL` (its own origin), `CADENCE_GATEWAY_URL`
  (the gateway, where its `/api` proxy forwards; the gateway sends no CORS headers, so the browser must not call it
  cross-origin, #5429) and the `VITE_KEYCLOAK_*` settings. Run `pnpm install` at the repository root first; the AppHost does not install packages.
- `subject-portal`: the participant portal's Vite dev server on `http://localhost:5174/subject-portal/`, as in docker
  compose, in the `full` profile only (it calls interop and payments). It gets `VITE_API_BASE_URL` (the gateway, called
  cross-origin, which the browser blocks until #5451) and the same `VITE_KEYCLOAK_*` settings as `web`. Keycloak sign-in does not work there yet: the realm's `cadence-web` client
  allows only the web app's redirect URI.
- Projects, each on the host port docker compose publishes it on, so the gateway's Development defaults and the
  web dev server's `/api` proxy to `http://localhost:8000` work for both (`core` runs 8000, 8002 and 8018):

  | Port | Service   | Port | Service       | Port | Service    |
  | ---- | --------- | ---- | ------------- | ---- | ---------- |
  | 8000 | gateway   | 8006 | notifications | 8012 | org        |
  | 8001 | designer  | 8007 | ctms          | 8013 | fileshare  |
  | 8002 | execution | 8008 | safety        | 8014 | imaging    |
  | 8003 | etmf      | 8009 | tickets       | 8015 | knowledge  |
  | 8004 | interop   | 8010 | eisf          | 8016 | payments   |
  | 8005 | quality   | 8011 | econsent      | 8017 | ai-gateway |
  |      |           |      |               | 8018 | kernel     |

- Every service except the gateway, designer and ai-gateway references the `cadence` database
  (`ConnectionStrings:cadence`), waits for PostgreSQL, and applies its own migrations on start
  (`Database__MigrateOnStartup=true`), as in docker compose. Each project has a health check on
  `/healthz/ready` (the gateway on `/healthz/live`), shown in the dashboard.
- The gateway receives every `*_SERVICE_URL` from the service endpoints above.
- The kernel runs in the AppHost's environment (`ASPNETCORE_ENVIRONMENT`; project resources do not inherit it), and in
  Development it gets `Kernel__DevelopmentTenant__Code=cadence-local`, so it provisions the tenant the local realm's
  users sign in with (ADR-2514, ADR-2542) and the Canvas's kernel reads are served on a fresh database.

Aspire sets `OTEL_EXPORTER_OTLP_ENDPOINT` on each project; [packages/telemetry](../../packages/telemetry/README.md) enables OTLP export when that variable is present, so service telemetry reaches the dashboard.

## Configuration

Credentials have no default value (ADR-2260). These are Aspire secret parameters, declared only when a resource
that uses them runs (the `core` profile never asks for the MinIO or field-protection keys, and an existing OIDC
issuer never asks for the Keycloak admin):

- the MinIO root user and password, and the Keycloak admin user and password;
- `gateway-secret`, passed to every service as `GATEWAY_SECRET` (at least 32 characters);
- `audit-log-secret-key`, passed to every database-backed service as `AUDIT_LOG_SECRET_KEY`;
- `demographics-encryption-key`, passed to execution as `DemographicsEncryption__KeyBase64` (base64 AES key of
  16, 24 or 32 bytes; execution refuses to start without one, ADR-2490);
- `field-encryption-key`, passed to econsent and payments as `FIELD_ENCRYPTION_KEY`, and `field-lookup-hash-key`,
  passed to interop as `FIELD_LOOKUP_HASH_KEY` (base64 of 32 bytes each, ADR-2494);
- `sce-signing-key`, passed to interop as `CADENCE_SCE_SIGNING_KEY` (base64 of 32 bytes; signs SCE database-lock
  tokens, ADR-2515);
- `imaging-deid-signing-key` and `imaging-deid-pseudonymization-key`, passed to imaging as
  `CADENCE_IMAGING_DEID_SIGNING_KEY` and `CADENCE_IMAGING_DEID_PSEUDONYMIZATION_KEY` (base64 of 32 bytes each, two
  different keys; they sign de-identification certificates and key the anonymized DICOM UIDs, #5399).

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
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:sce-signing-key" "$(openssl rand -base64 32)"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:imaging-deid-signing-key" "$(openssl rand -base64 32)"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:imaging-deid-pseudonymization-key" "$(openssl rand -base64 32)"
```

Or set `Parameters__minio-root-user` (and so on) as environment variables. The names contain hyphens, which
bash `export` rejects, so pass them with `env` (`env "Parameters__gateway-secret=..." dotnet run ...`). If a value is missing, the Aspire
dashboard prompts for it before starting the containers.

## Key dependencies

- Packages: `Aspire.Hosting.AppHost`, `Aspire.Hosting.PostgreSQL`, `Aspire.Hosting.JavaScript` (the Vite app).
- Project references: every service project above.

## Tests

`tests/Cadence.UnitTests/AppHost/AppHostModelTests.cs` builds the resource graph with `Aspire.Hosting.Testing`, without starting anything, and checks that every service is present on its port, gets the settings it refuses to start without, and that the gateway is told every service URL. It also checks the `core` profile, the existing-PostgreSQL and existing-OIDC switches, and that invalid settings stop the AppHost.

## Run locally

```bash
pnpm install                                                   # once, for the web app
dotnet run --project apps/apphost/src/Cadence.AppHost.csproj   # full profile
dotnet run --project apps/apphost/src/Cadence.AppHost.csproj -- --AppHost:Profile=core
```

Without a container runtime (for example a cloud dev container), point the core profile at an existing PostgreSQL
and Keycloak. Nothing then needs Docker:

```bash
env AppHost__Profile=core \
    ConnectionStrings__cadence="Host=localhost;Username=postgres;Password=<password>;Database=cadence" \
    AppHost__OidcAuthority=http://localhost:8080/realms/cadence \
    "Parameters__gateway-secret=$(openssl rand -hex 24)" \
    "Parameters__audit-log-secret-key=$(openssl rand -hex 24)" \
    "Parameters__demographics-encryption-key=$(openssl rand -base64 32)" \
    ASPIRE_ALLOW_UNSECURED_TRANSPORT=true \
    dotnet run --project apps/apphost/src/Cadence.AppHost.csproj --launch-profile http
```
