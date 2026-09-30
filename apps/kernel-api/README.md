# Kernel API host

The Clinical Kernel's HTTP host ([ADR-2540](../../docs/adr/2026-09-30-kernel-api-host-and-read-endpoints-for-the-canvas.md)). It serves the kernel's read endpoints for the Clinical Canvas: navigation, dry-run authorization with explanations, the command catalogue, search and protocol versions. It is the only process that registers kernel persistence (K-XS-2); product services never reference it.

**Status:** the endpoints are mapped, documented and tested, and each read runs in the kernel tenant found from the tenant code the gateway signs. Authorization decisions come from the caller's grants, roles and delegations, and so does what the caller may see (ADR-2457): an object is visible when a grant in effect covers it, and masked in place otherwise, so a caller with no grant sees every object masked. Blinding and the IdentityVault (#4782) are not decided yet.

## Project

- Project: `apps/kernel-api/src/Cadence.Kernel.Host.csproj` (assembly `Cadence.Kernel.Host`).
- `Program.cs`: composition root. Registers kernel persistence, the scope graph and navigation, search and authorization, and maps `MapKernelApi().MapKernelReads()` from `Cadence.Kernel.Api`.
- `ClaimsCurrentActor`: the acting person, from the verified `sub` claim. A subject the kernel does not accept as an accountable person (`system`, `admin`, a `svc:` or `agent:` id) gives no actor, so dry runs deny with `actor.resolved`.
- Authorization: the kernel's `AuthorityEvaluator` (`AddKernelAuthorityEvaluator`, ADR-2455), with no platform operators since the host never provisions tenants. Without a grant in effect covering the resource, a decision is a denial whose unmet condition is `grant.active`.
- `DirectoryTenantResolver`: finds the caller's kernel tenant from the signed `tenant_id` claim through `ITenantDirectory.FindActiveByCodeAsync`. A missing code, or one that names no active kernel tenant, is a 403; there is no fallback tenant and no configured mapping.
- `KernelDatabaseHealthCheck`: the readiness probe opens a connection to the kernel database.
- `KernelMigrationHostedService`: applies the kernel migrations on start when `Database:MigrateOnStartup` or `Database:MigrateOnly` is set.

The endpoints, response records and tenant filter live in [`packages/kernel/src/Cadence.Kernel.Api`](../../packages/kernel/README.md).

## HTTP surface

All reads are `GET`, need the gateway-signed caller identity, run in the caller's kernel tenant, and answer RFC 9457 problem details on failure. The wire is snake_case. Objects the caller may not see are masked (ADR-2471): node reads, paths and child lists keep them in place with only `kind` and `mask`; search leaves them out; lineage reads keep them as a `masked:n` handle with their kind and count them like any other.

| Route (`/api/v1/kernel/…`)                                                 | Answers                                                                                                                                                                                 |
| -------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `nodes/{nodeRef}`                                                          | Summary of one object: `ref`, `kind`, `title`, `code`, `status`.                                                                                                                        |
| `nodes/{nodeRef}/path`                                                     | Zoom path from the tenant down to the object (ADR-2481).                                                                                                                                |
| `nodes/{nodeRef}/children`                                                 | Zoom children with their own child counts. A hidden node is refused with 403, since even masked its list would say how many children it has (#5033); a hidden child is masked in place. |
| `nodes/{nodeRef}/commands`                                                 | Kernel commands offered on the object's kind, each with the caller's dry-run decision and explanation (ADR-2483).                                                                       |
| `authorization/can-i?capability=&resource=`                                | The caller's dry-run decision on one capability and object, with its explanation (ADR-2482).                                                                                            |
| `search?q=&kind=&limit=`                                                   | Search hits by code or title (ADR-2484); `kind` repeats, `limit` is 1 to 50.                                                                                                            |
| `studies/{studyRef}/protocol-versions`                                     | The study's protocol versions, newest first, without content. A hidden study is refused with 403; a hidden version is masked in place.                                                  |
| `protocol-versions/{versionRef}`                                           | One protocol version, without content.                                                                                                                                                  |
| `nodes/{nodeRef}/lineage?direction=&depth=&edge_kind=&valid_at=&known_at=` | Trace: the lineage walk, `Backward` (default) or `Forward`, depth 1 to 50 (default 10), `edge_kind` repeats (ADR-2471).                                                                 |
| `nodes/{nodeRef}/dependents?depth=&edge_kind=&valid_at=&known_at=`         | Impact: what depends on the object, grouped by kind with counts.                                                                                                                        |
| `protocol-versions/{versionRef}/impact?depth=&valid_at=&known_at=`         | Amendment impact (K-PROT-2): the dependents of the version it amends. A first version is a 400.                                                                                         |

Also `GET /healthz`, `/healthz/live`, `/healthz/ready` and `GET /api/v1/kernel/status` (anonymous). OpenAPI and the Scalar reference are mapped in Development only; the exported document is [docs/openapi/kernel_openapi.json](../../docs/openapi/kernel_openapi.json).

The gateway forwards `/api/v1/kernel/**` here. Compose publishes host port 8018 (container port 8080); the gateway's default upstream is `http://localhost:8018` (`KERNEL_SERVICE_URL`).

## Security

`AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md): every route not marked anonymous requires the gateway-signed caller identity, and `GATEWAY_SECRET` is required at startup ([ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md)). The tenant and the actor come only from the verified claims. Authorization is the kernel's own `KernelAuthorization` (K-AUTH-7); dry runs go to the authorization access log, not the audit chain (ADR-2483).

## Persistence

`AddKernelPersistence` with the connection string from `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`; startup fails without one (ADR-2260). Every read runs through a context bound to the caller's tenant by Row-Level Security ([ADR-2250](../../docs/adr/2026-09-29-kernel-tenant-isolation-via-row-level-security.md)). In production the host connects as the application role and the migrations and role grant are applied separately with `KernelDatabase`; docker compose and the AppHost connect as the schema owner and set `Database__MigrateOnStartup=true`.

## Key dependencies

- Project references: `Cadence.Kernel.Abstractions`, `Cadence.Kernel.Application`, `Cadence.Kernel.Persistence`, `Cadence.Kernel.Api`, `Cadence.Security`, `Cadence.Database`, `Cadence.Telemetry`.
- Packages: `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Npgsql.EntityFrameworkCore.PostgreSQL` (versions in `Directory.Packages.props`).

## Tests

- `tests/Cadence.UnitTests/Kernel/Api/KernelApiHostTests.cs` boots the real host with fake navigation, search, tenant directory, protocol version reader and authority store: 403 for an unknown or missing tenant code, 401 unsigned, reads in the resolved tenant, masking in place and in search (and the host's grant-backed visibility: everything masked without a grant, visible under a tenant-wide one), protocol versions listed, masked and refused for a hidden study, 400 and 404 problems, CanI denied without a grant and permitted with one, each explained, no actor for a subject that is no accountable person, the command catalogue, and search.
- `tests/Cadence.UnitTests/Gateway/GatewayRouteCoverageTests.cs` checks the gateway route against the endpoints this host maps; `tests/Cadence.UnitTests/AppHost/AppHostModelTests.cs` checks its port.
- The kernel ports themselves are tested against PostgreSQL in `tests/Cadence.IntegrationTests/Kernel`.
- `tests/Cadence.IntegrationTests/Kernel/KernelApiHostPostgresTests.cs` boots the real host on PostgreSQL with nothing faked: a coordinator granted at one study site gets their enrollment, the design elements and the version from Trace, dependents and amendment impact, the other site's enrollment masked in place, and the changed elements; a reader with no grant gets the same counts with everything masked.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=<password>"
export Database__MigrateOnStartup=true
dotnet run --project apps/kernel-api/src/Cadence.Kernel.Host.csproj --urls http://localhost:8018
```

The Aspire AppHost ([apps/apphost](../apphost/README.md)) and docker compose also run it on port 8018.
