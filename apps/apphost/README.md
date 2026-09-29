# Cadence AppHost (.NET Aspire Orchestration)

Local developer orchestration host for the Cadence Clinical Research Software Platform powered by **.NET Aspire**.

## Capabilities

- **Datastores Orchestration**: Coordinates PostgreSQL (with databases for `cadence-kernel`, `cadence-edc`, `cadence-ctms`, `cadence-etmf`, `cadence-quality`), MinIO S3 object storage, and Keycloak IAM.
- **Service Mesh**: Orchestrates `Cadence.Apps.Gateway`, `Cadence.Apps.Execution`, `Cadence.Apps.Designer`, `Cadence.Apps.Ctms`, `Cadence.Apps.Etmf`, `Cadence.Apps.Quality`, and `Cadence.Apps.Fileshare`.
- **Integrated Telemetry**: Seamlessly exposes OpenTelemetry logs, distributed traces, and metrics on the local Aspire dashboard.

## Running Locally

The MinIO and Keycloak credentials, the shared `GATEWAY_SECRET` and the `AUDIT_LOG_SECRET_KEY` are Aspire parameters with no default value. Set them once with user
secrets (choose your own values):

```bash
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:minio-root-user" "<user>"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:minio-root-password" "<password>"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:keycloak-admin" "<user>"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:keycloak-admin-password" "<password>"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:gateway-secret" "<at least 32 characters>"
dotnet user-secrets --project apps/apphost/src/Cadence.AppHost.csproj set "Parameters:audit-log-secret-key" "<secret>"
```

or export `Parameters__minio-root-user` (and so on) as environment variables. If a value is missing, the Aspire
dashboard prompts for it before starting the container.

```bash
dotnet run --project apps/apphost/src/Cadence.AppHost.csproj
```
