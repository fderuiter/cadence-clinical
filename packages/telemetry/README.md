# Cadence.Telemetry

Shared web host defaults for the .NET services: HTTP hardening, JSON console logging, OpenTelemetry tracing and metrics, ProblemDetails, OpenAPI, health probes and a `traceparent` response header.

## Project

- Project: `packages/telemetry/src/Cadence.Telemetry.csproj` (assembly `Cadence.Telemetry`, namespace `Cadence.Telemetry`).
- `CadenceTelemetryExtensions`:
  - `AddCadenceWebDefaults()`: JSON console logging, OpenTelemetry (ASP.NET Core, HttpClient, runtime and Npgsql instrumentation), health checks, ProblemDetails and OpenAPI. OTLP export of traces, metrics and logs is enabled only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (the Aspire AppHost sets it); `OTEL_SERVICE_NAME` overrides the service name.
  - HTTP hardening (`CadenceHttpHardening`, ADR-2491): no `Server` header; `X-Forwarded-For` and `X-Forwarded-Proto` honoured only from the proxies listed in `CADENCE_TRUSTED_PROXIES` (IP addresses or CIDR ranges; loopback is always trusted), walking back through every trusted hop to the real client; `Strict-Transport-Security` (one year) on HTTPS responses outside Development.
  - `UseCadenceWebDefaults()`: forwarded headers, HSTS outside Development, exception handler, `traceparent` response header, OpenAPI and Scalar UI in Development only, and the probe endpoints.
  - `MapCadenceProbeEndpoints()`: `GET /healthz/live` (no checks) and `GET /healthz/ready` (checks tagged `ready`, such as a service's DbContext).
  - `MapCadenceHealthAndStatusEndpoints(serviceName, routePrefix, ...)`: anonymous `GET /healthz` and `GET /api/v1/{routePrefix}/status`.

## Key dependencies

- Framework reference `Microsoft.AspNetCore.App`.
- Packages: `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`, `OpenTelemetry.Instrumentation.Runtime`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`.

## Referenced by

Every .NET service under `apps/` except the AppHost.

## Tests

```bash
dotnet test --project tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.TelemetryUnitTests|FullyQualifiedName~Cadence.UnitTests.Packages.LoggerMessageGeneratorTests|FullyQualifiedName~Cadence.UnitTests.Packages.HttpHardeningTests"
```
