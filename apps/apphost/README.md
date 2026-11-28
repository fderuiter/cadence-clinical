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
| `AppHost:Profile`           | `full` (default), `core`                                   | `core` runs the gateway, the kernel API host, execution, etmf, ctms, knowledge and the web app. `full` adds the other 12 services, MinIO and the subject portal.       |
| `ConnectionStrings:cadence` | an Npgsql connection string                                | Use this existing database instead of starting the PostgreSQL container. The services apply their migrations to it.                                                    |
| `AppHost:OidcAuthority`     | a Keycloak realm URL, `http(s)://host:port/realms/<realm>` | Use this issuer for the gateway and the web app instead of starting the Keycloak container. The realm needs the `cadence-web` client from `docker/cadence-realm.json`. |

Pass them on the command line after `--` (`-- --AppHost:Profile=core`) or as environment variables
(`AppHost__Profile=core`). An unknown profile or a malformed authority stops the AppHost with a message. In the
`core` profile the gateway is given `"disabled"` for every omitted service's `*_SERVICE_URL`, so those routes
answer `503 Service Unavailable` directly without probing unused host ports. Outside Development, `Storage:RootPath`
(and `Scratch:RootPath` in the `full` profile) must be set to explicit, fully qualified directory paths; in
Development, AppHost defaults them under `LocalApplicationData/cadence-clinical/` without using ambient OS temp directories.

## Resources

- `postgres` (PostgreSQL 18, the digest-pinned image docker compose runs and the tests qualify per [ADR-2533](../../docs/adr/2026-10-19-tests-ci-and-compose-run-one-pinned-postgresql-major.md), with a data volume and pgAdmin) and one database named `cadence`, shared by all services per [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md). With `ConnectionStrings:cadence` set, `cadence` is that existing database and no container runs.
- `keycloak` container (port 8080, `start-dev`, importing `docker/cadence-realm.json` as docker compose does), unless `AppHost:OidcAuthority` is set.
- `minio` container (ports 9000 and 9001; no service reads it yet), in the `full` profile only.
- `web`: the Clinical Canvas Vite dev server on `http://localhost:3000/cadence-clinical/`, the redirect URI the
  `cadence-web` Keycloak client allows. It gets `VITE_API_BASE_URL` (its own origin), `CADENCE_GATEWAY_URL`
  (the gateway, where its `/api` proxy forwards; the gateway sends no CORS headers, so the browser must not call it
  cross-origin, #5429) and the `VITE_KEYCLOAK_*` settings. Run `pnpm install --frozen-lockfile` at the repository root first; the AppHost does not install packages (`packages/ui` resolves directly from workspace source).
- `subject-portal`: the participant portal's Vite dev server on `http://localhost:5174/subject-portal/`, as in docker
  compose, in the `full` profile only (it calls interop and payments). Like `web`, it gets `VITE_API_BASE_URL` (its own
  origin), `CADENCE_GATEWAY_URL` (where its `/api` proxy forwards, #5451) and the `VITE_KEYCLOAK_*` settings. The dev realm's `cadence-web` client allows its redirect URI,
  `http://localhost:5174/subject-portal/*`.
- Projects, each on the host port docker compose publishes it on, so the gateway's Development defaults and the
  web dev server's `/api` proxy to `http://localhost:8000` work for both (`core` runs 8000, 8002, 8003, 8007, 8015 and 8018). Each
  service's port, address variable, database need and profile come from the service topology manifest
  ([`packages/topology`](../../packages/topology/README.md)); a new service still needs its `AddProject` line:

  | Port | Service   | Port | Service       | Port | Service   |
  | ---- | --------- | ---- | ------------- | ---- | --------- |
  | 8000 | gateway   | 8006 | notifications | 8012 | org       |
  | 8001 | designer  | 8007 | ctms          | 8013 | fileshare |
  | 8002 | execution | 8008 | safety        | 8014 | imaging   |
  | 8003 | etmf      | 8009 | tickets       | 8015 | knowledge |
  | 8004 | interop   | 8010 | eisf          | 8016 | payments  |
  | 8005 | quality   | 8011 | econsent      | 8018 | kernel    |

- Every service except the gateway and designer references the `cadence` database
  (`ConnectionStrings:cadence`), waits for PostgreSQL, and applies its own migrations on start
  (`Database__MigrateOnStartup=true`), as in docker compose. Each project has a health check on
  `/healthz/ready` (the gateway on `/healthz/live`), shown in the dashboard.
- The gateway receives every `*_SERVICE_URL` from the running service endpoints above (or `"disabled"` for services omitted by the `core` profile), `etmf` receives `EXECUTION_SERVICE_URL` from the `execution` endpoint, `eisf`, `etmf`, and `fileshare` receive per-service subdirectories under `Storage:RootPath`, and in the `full` profile `imaging` and `interop` receive isolated `Scratch__RootPath` subdirectories under `Scratch:RootPath`.
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
- `randomization-allocation-key`, passed to execution as `RANDOMIZATION_ALLOCATION_KEY` (base64 of 32 bytes, a
  different key; it encrypts randomisation lists, allocations and kit arm labels, and execution refuses to start
  without one, ADR-2640);
- `cgt-barcode-hmac-key`, passed to execution as `CGT_BARCODE_HMAC_KEY` (base64 of 32 bytes, a different key; it
  verifies CGT chain-of-custody bag barcodes, and execution refuses to start without one,
  [ADR-2666](../../docs/adr/2026-11-24-atomic-dual-signer-step-up-verification-and-startup-validated-cgt-barcode-key.md));
- `field-encryption-key`, passed to econsent and payments as `FIELD_ENCRYPTION_KEY`, and `field-lookup-hash-key`,
  passed to interop as `FIELD_LOOKUP_HASH_KEY` (base64 of 32 bytes each, ADR-2494);
- `sce-signing-key`, passed to interop as `CADENCE_SCE_SIGNING_KEY` (base64 of 32 bytes; signs SCE database-lock
  tokens, ADR-2515);
- `imaging-deid-signing-key` and `imaging-deid-pseudonymization-key`, passed to imaging as
  `CADENCE_IMAGING_DEID_SIGNING_KEY` and `CADENCE_IMAGING_DEID_PSEUDONYMIZATION_KEY` (base64 of 32 bytes each, two
  different keys; they sign de-identification certificates and key the anonymized DICOM UIDs, #5399);
- `biometric-liveness-key`, passed to kernel (both `core` and `full`) and econsent (`full`) as
  `CADENCE_BIOMETRIC_LIVENESS_KEY` (base64 of 32 bytes; signs and verifies 15-minute biometric liveness tokens,
  [ADR-2660](../../docs/adr/2026-11-23-biometric-liveness-hmac-key-and-single-use-replay-protection.md)).

The gateway's `OIDC_AUTHORITY` is the Keycloak `cadence` realm. Set the parameters once with user secrets (choose your own values):

```bash
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:minio-root-user" "<user>"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:minio-root-password" "<password>"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:keycloak-admin" "<user>"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:keycloak-admin-password" "<password>"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:gateway-secret" "<at least 32 characters>"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:audit-log-secret-key" "<secret>"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:demographics-encryption-key" "$(openssl rand -base64 32)"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:randomization-allocation-key" "$(openssl rand -base64 32)"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:cgt-barcode-hmac-key" "$(openssl rand -base64 32)"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:field-encryption-key" "$(openssl rand -base64 32)"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:field-lookup-hash-key" "$(openssl rand -base64 32)"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:sce-signing-key" "$(openssl rand -base64 32)"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:imaging-deid-signing-key" "$(openssl rand -base64 32)"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:imaging-deid-pseudonymization-key" "$(openssl rand -base64 32)"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:biometric-liveness-key" "$(openssl rand -base64 32)"
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
pnpm install --frozen-lockfile                                 # once, for the frontend workspace
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
    "Parameters__randomization-allocation-key=$(openssl rand -base64 32)" \
    "Parameters__cgt-barcode-hmac-key=$(openssl rand -base64 32)" \
    "Parameters__biometric-liveness-key=$(openssl rand -base64 32)" \
    ASPIRE_ALLOW_UNSECURED_TRANSPORT=true \
    dotnet run --project apps/apphost/src/Cadence.AppHost.csproj --launch-profile http
```
