# Cadence AppHost (.NET Aspire Orchestration)

Local developer orchestration host for the Cadence Clinical Research Software Platform powered by **.NET Aspire**.

## Capabilities
- **Datastores Orchestration**: Coordinates PostgreSQL (with databases for `cadence-kernel`, `cadence-edc`, `cadence-ctms`, `cadence-etmf`, `cadence-quality`), MinIO S3 object storage, and Keycloak IAM.
- **Service Mesh**: Orchestrates `Cadence.Apps.Gateway`, `Cadence.Apps.Execution`, `Cadence.Apps.Designer`, `Cadence.Apps.Ctms`, `Cadence.Apps.Etmf`, `Cadence.Apps.Quality`, and `Cadence.Apps.Fileshare`.
- **Integrated Telemetry**: Seamlessly exposes OpenTelemetry logs, distributed traces, and metrics on the local Aspire dashboard.

## Running Locally
```bash
dotnet run --project apps/apphost/src/Cadence.AppHost.csproj
```
