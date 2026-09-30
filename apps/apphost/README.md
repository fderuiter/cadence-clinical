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
- Projects: `gateway`, `designer`, `execution`, `ctms`, `etmf`, `quality` (the last four reference the `cadence` database, which supplies `ConnectionStrings:cadence`), and `fileshare` (receives `STORAGE_ENDPOINT_URL` for MinIO).
- Not wired here: ai_gateway, econsent, eisf, imaging, interop, knowledge, notifications, org, payments, safety, tickets, and the frontends.

Aspire sets `OTEL_EXPORTER_OTLP_ENDPOINT` on each project; [packages/telemetry](../../packages/telemetry/README.md) enables OTLP export when that variable is present, so service telemetry reaches the dashboard.

## Configuration gaps

The AppHost does not set `GATEWAY_SECRET` for any service or `OIDC_AUTHORITY` for the gateway, and it does not import the `cadence` Keycloak realm. Every service fails at startup without `GATEWAY_SECRET`, and the gateway fails without `OIDC_AUTHORITY`; supply them before relying on this host. See [ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).

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
