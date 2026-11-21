# ADR-2548: Enforce PostgreSQL Forced RLS and Shared Clinical Scope Guards Across Standalone Services

- **Status:** Accepted
- **Date:** 2026-11-20
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-013, PRD-SYS-192

---

## 1. Context & Problem Statement

While `Cadence.Kernel.Persistence` enforced PostgreSQL `FORCE ROW LEVEL SECURITY` (`tenant_isolation` via `kernel.current_tenant_id()`), the 15 standalone clinical `DbContext` schemas (`ctms`, `econsent`, `eisf`, `etmf`, `edc`, `fileshare`, `imaging`, `interop`, `knowledge`, `notifications`, `org`, `payments`, `quality`, `safety`, `tickets`) lacked database-enforced `tenant_id` columns and RLS policies, and `GatewayAuthenticationHandler` did not populate `Principal.TenantId` or fail closed when a signed `X-Tenant-Id` was missing or inactive. Additionally, standalone services duplicated ad-hoc study/site checks instead of sharing a single fail-closed authorization primitive.

## 2. Decision Drivers & Constraints

- **Defense-in-Depth Multi-Tenant Isolation (K-ID-1, K-ID-2, ADR-2250, ADR-2514):** Every tenant-scoped standalone table must carry `tenant_id uuid NOT NULL` and enforce `ENABLE` + `FORCE ROW LEVEL SECURITY` (`USING` and `WITH CHECK` `tenant_id = kernel.current_tenant_id()`) under the non-superuser `cadence_app` (`NOSUPERUSER NOBYPASSRLS`) role.
- **Strict Kernel Persistence Boundary (K-XS-2, ADR-2600):** `packages/database` and standalone `apps/*` must resolve signed tenant codes via `SELECT kernel.tenant_id_by_code(@code)` without referencing `Cadence.Kernel.Persistence` or migrating standalone aggregates into `Cadence.Kernel.Host`.
- **Fail-Closed Study & Site Scope (`PRD-SYS-192`):** Site-bound roles (`crc`, `investigator`, `principal_investigator`, `cra`, `monitor`) and study-scoped callers must be evaluated through a shared `ClinicalScopeGuard` in `packages/security` and verified by route metadata and handler denial probes.

## 3. Options Considered

### Option 1: Application-Only LINQ Tenant Filters Without PostgreSQL RLS

- **Overview:** Filter standalone `DbContext` queries in C# using `CallerScope.TenantId(httpContext)` without adding `tenant_id` columns or PostgreSQL RLS policies.
- **Pros:**
  - Avoids EF Core schema migrations across the 15 standalone services.
- **Cons:**
  - Violates `K-ID-1`, `K-ID-2`, and `ADR-2250`; any raw SQL query or wildcard `*` study scope can read or mutate cross-tenant records.

### Option 2: Database-Enforced `FORCE ROW LEVEL SECURITY`, Session-Bound Connection Interceptors, and Shared `ClinicalScopeGuard` (Selected)

- **Overview:** Add `PostgresTenantCodeResolver`, `StandaloneTenantConnectionInterceptor` (`set_config('cadence.tenant_id', ...)` on open, reset on close), `StandaloneTenantSaveChangesInterceptor` (fail-closed tenant UUID stamping), and non-relational EF query filters (`StandaloneTenantModelExtensions`) in `packages/database`; add `FORCE ROW LEVEL SECURITY` migrations across all 15 standalone `DbContext` schemas; populate `Principal.TenantId` and verify active tenants in `GatewayAuthenticationHandler`; and enforce `RequireTenantScope`, `RequireCallerStudyScope`, `RequireCallerStudyAndSiteScope`, and `ClinicalScopeGuard` across standalone endpoints.
- **Pros:**
  - Enforces tenant isolation in PostgreSQL even under wildcard `*` study scope (`PaymentsCaller`).
  - Preserves `K-XS-2` package boundaries and `ServiceTopology.cs` route contracts.
  - Centralizes study/site admission and eliminates hardcoded `tenant_default` and `TenantId.New` fallbacks.
- **Cons:**
  - Requires coordinated EF Core migrations across all 15 standalone schemas and explicit `ITenantScope` binding in background workers.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Combining PostgreSQL `FORCE ROW LEVEL SECURITY` under `cadence_app` with session-bound connection/save interceptors and `ClinicalScopeGuard` satisfies `K-ID-1`, `K-ID-2`, `ADR-2250`, `ADR-2514`, `PRD-CRF-013`, and `PRD-SYS-192` without violating `K-XS-2` or `ADR-2600`.

## 5. Consequences & Trade-offs

- **Positive Impact:** Unbound, inactive, or missing-tenant requests fail closed with HTTP 403; cross-tenant reads and writes are blocked by PostgreSQL RLS; country greenlight matrices and eSource provenance trails filter unauthorized site/study/tenant data; and `RouteAuthorizationTests` blocks unguarded standalone routes.
- **Negative Impact / Technical Debt:** Background workers iterating across tenants (`EtmfExportWorker`, `DocumentConversionWorker`, `MediaTranscoderWorker`) must enumerate active tenant IDs via `ITenantCodeResolver.ListActiveTenantIdsAsync` and bind each tenant scope explicitly.
- **Mitigation Strategy:** `StandaloneTenantSaveChangesInterceptor` throws `InvalidOperationException` if any code path attempts to persist a tenant-scoped entity without an active verified tenant.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/database`, `packages/security`, `packages/testing`, all 15 standalone `DbContext` services (`ctms`, `econsent`, `eisf`, `etmf`, `execution`, `fileshare`, `imaging`, `interop`, `knowledge`, `notifications`, `org`, `payments`, `quality`, `safety`, `tickets`), and `designer`.
- **Verification Plan:** Verified via `RouteAuthorizationTests`, `Cadence.ArchitectureTests`, `StandaloneTenantIsolationPostgresTests`, `StandaloneServiceScopeAuthorizationTests`, and `ServiceSchemaPostgresTests`.
