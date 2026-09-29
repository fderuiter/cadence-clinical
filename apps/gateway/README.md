# API gateway

Single entry point for browser and API clients. It validates the caller's Keycloak (OIDC) bearer token, strips any client-supplied identity headers, and forwards requests to the backend services with YARP, adding identity headers signed with `GATEWAY_SECRET`.

**Status:** a standalone ASP.NET Core 10 service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap) the backend services become application modules over the clinical kernel; the disposition of the scaffold services is tracked in issue #4787.

## Project

- Project: `apps/gateway/src/Cadence.Apps.Gateway.csproj` (assembly `Cadence.Apps.Gateway`).
- `Program.cs`: JWT bearer authentication, fallback authorization policy, YARP routes and clusters.
- `GatewayIdentityForwarding.cs`: maps Keycloak realm roles to claims and registers the proxy transform that removes client identity headers and the `Authorization` header, then attaches freshly signed `X-User-Id`, `X-User-Roles`, `X-Tenant-Id` and related headers.

## HTTP surface

- `GET /healthz` (anonymous, mapped in `Program.cs`); `GET /healthz/live` and `GET /healthz/ready` (anonymous, mapped by `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)).
- `GET /api/v1/status` (anonymous).
- Everything else requires a valid bearer token and is proxied by path prefix under `/api/v1/`:

| Prefixes                                                                                                                  | Upstream (env var, default port)                                        |
| ------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------- |
| `studies`                                                                                                                 | designer (`DESIGNER_SERVICE_URL`, 8001)                                 |
| `subjects`, `observations`, `forms`, `queries`, `compliance`, `execution`                                                 | execution (`EXECUTION_SERVICE_URL`, 8002)                               |
| `etmf`, `documents`                                                                                                       | etmf (`ETMF_SERVICE_URL`, 8003)                                         |
| `ctms`, `sites`                                                                                                           | ctms (`CTMS_SERVICE_URL`, 8007)                                         |
| `files`, `fileshare`                                                                                                      | fileshare (`FILESHARE_SERVICE_URL`, 8013)                               |
| `interop`, `quality`, `notifications`, `safety`, `tickets`, `eisf`, `econsent`, `org`, `imaging`, `knowledge`, `payments` | the service of the same name (`<NAME>_SERVICE_URL`, ports 8004 to 8016) |
| `ai`                                                                                                                      | ai_gateway (`AI_GATEWAY_SERVICE_URL`, 8017)                             |

Defaults are `http://localhost:<port>`. The designer's `/api/v1/protocols` route has no gateway route. Compose publishes port 8000.

## Security

- `OIDC_AUTHORITY` (the Keycloak realm URL, for example `http://localhost:8080/realms/cadence`) is required; the host throws at startup without it. Optional: `OIDC_METADATA_ADDRESS` (internal discovery URL), `OIDC_AUDIENCE` (validated when set), `OIDC_REQUIRE_HTTPS_METADATA` (default `true`).
- `GATEWAY_SECRET` (at least 32 characters) is required at startup and must match the downstream services; see [ADR-2250](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).
- The gateway uses its own JWT authentication rather than `AddCadenceSecurity()`; it uses `GatewayAuthenticationOptions`, header names and claim types from [packages/security](../../packages/security/README.md).

## Persistence

None.

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Kernel.Abstractions`, `Cadence.Security`, `Cadence.Telemetry`, `Cadence.ToolingCore`.
- Packages: `Yarp.ReverseProxy`, `Microsoft.AspNetCore.Authentication.JwtBearer`, OpenTelemetry, `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`.

## Tests

- `tests/Gateway/GatewayAuthenticationTests.cs` runs the real gateway in front of a header-echoing upstream: `dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Gateway"`.
- Architecture rules: `dotnet test tests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export OIDC_AUTHORITY="http://localhost:8080/realms/cadence"
export OIDC_REQUIRE_HTTPS_METADATA=false
export GATEWAY_SECRET="<at least 32 characters, same value as the services>"
dotnet run --project apps/gateway/src/Cadence.Apps.Gateway.csproj
```

Keycloak and the `cadence` realm come from `docker/docker-compose.yml` (`docker/cadence-realm.json`). The Aspire AppHost ([apps/apphost](../apphost/README.md)) also starts the gateway but does not set `OIDC_AUTHORITY` or `GATEWAY_SECRET` for it.
