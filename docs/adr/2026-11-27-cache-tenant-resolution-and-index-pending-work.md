# ADR-2668: Cache Tenant Resolution, Scoped DbContext Connection Leasing, and Pending-Work Tenant Indexing

- **Status:** Accepted
- **Date:** 2026-11-27
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001, PRD-SYS-005

---

## 1. Context & Problem Statement

After enforcing PostgreSQL `ENABLE` and `FORCE ROW LEVEL SECURITY` (`ADR-2548`) and default GxP audit and step-up electronic signature interceptors (`ADR-2664`) across all 15 standalone service schemas, four database round-trip multipliers affected HTTP request latency, connection pool contention, and idle background worker load:

1. **Uncached `kernel.resolve_tenant_id` Lookups on Separate Ad-Hoc Pools:** `TenantScopeAuthorizationHandler`, `CurrentTenantAccessor`, `KnowledgeCatalogSeeder`, `StandaloneTenantSaveChangesInterceptor`, and `GxPAuditInterceptor` repeatedly resolved `X-Tenant-Id` (`tenant_code`) to `tenant_id` (`uuid`) within the same request, while `PostgresTenantCodeResolver` opened ad-hoc `new NpgsqlConnection(_connectionString)` connections outside EF Core's `NpgsqlDataSource`.
2. **Duplicate Save-Time Tenant Stamping and Concurrency Token Increments:** `StandaloneTenantSaveChangesInterceptor`, `StandaloneConcurrencyTokenInterceptor`, `ConcurrencyTokenIncrementInterceptor` (`apps/execution`), and `GxPAuditInterceptor` each scanned `DbContext.ChangeTracker` entries independently during `SaveChanges` / `SaveChangesAsync`.
3. **Per-Command `DbConnection` Checkout and `set_config` Overhead:** EF Core checked out, bound `set_config('cadence.tenant_id', $1, false)`, and reset `set_config('cadence.tenant_id', '', false)` on every individual SQL command (`3Q + 6W` statements for `Q` reads and `W` saves), while `PostgresSigTokenUseStore` opened a separate connection outside the ambient `DbContext` lease.
4. **Full-Tenant Worker Fan-Out in `apps/etmf` and `apps/fileshare`:** `EtmfExportWorker` polled all active tenants via `kernel.active_tenant_ids()` every 2 seconds even though `EtmfExportQueue` already had an in-process `Channel<Guid>`, and `MediaTranscoderWorker` plus `DocumentConversionWorker` scanned every active tenant every 5 seconds regardless of whether pending transcode or conversion rows existed.

## 2. Decision Drivers & Constraints

- **Fail-Closed Row-Level Security (`ADR-2514`, `ADR-2548`):** All 15 standalone schemas must preserve `ENABLE` and `FORCE ROW LEVEL SECURITY` with `cadence_app` remaining `NOSUPERUSER NOBYPASSRLS`. Unknown or inactive `tenant_code` values must never be cached and must continue to fail closed (`HTTP 403 Forbidden`), and every pooled connection must reset `cadence.tenant_id` to `''` before returning to `NpgsqlDataSource`.
- **21 CFR Part 11 Audit Chain & Signature Replay Integrity (`ADR-2515`, `ADR-2664`):** Deduplicating save-time stamping and sharing the scoped connection lease must produce byte-identical SHA-256 `AuditLogEntry` Merkle hashes (`PreviousHash` / `CurrentHash`), preserve actor and `X-Change-Reason` attribution, atomically stamp `TransactionalOutboxMessage.TenantId`, and commit single-use `sig_token_uses` markers independently of outer rollbacks unless an explicit transaction is active.
- **Deterministic Clock Discipline & Package Boundaries (`K-TIME-3`, `K-XS-2`):** All TTL expirations must derive exclusively from `IClock.UtcNow`, and `Cadence.Security` / `Cadence.Database` must remain decoupled from kernel persistence internals without introducing external message brokers.

## 3. Options Considered

### Option 1: Request-Scoped Tenant Memoization, Shared `NpgsqlDataSource` 5s Active-Tenant Cache, Coordinated Save Stamping, Scoped `DbContext` Connection Leasing, and Narrow `SECURITY DEFINER` Pending-Work Functions (Selected)

- **Overview:** Memoize verified `(TenantCode, TenantId)` on `HttpContext.Items` via `RequestTenantResolutionCache` while honoring `ITenantScope` overrides; share one keyed/singleton `NpgsqlDataSource` per `AddCadencePostgresDbContext<TDbContext>` between EF Core and `PostgresTenantCodeResolver` with an `IClock`-driven 5-second active-only `ConcurrentDictionary` cache; coordinate `StampTenantIdsAsync` and `IncrementConcurrencyTokens` once per save cycle across standalone interceptors and `GxPAuditInterceptor`; hold a single `StandaloneDbContextLeaseManager` connection lease per scoped `DbContext` (`2 + Q + 2W` wire statements) shared by `PostgresSigTokenUseStore` when unleased; and introduce `SECURITY DEFINER` directory functions (`etmf.queued_export_tenant_ids()`, `fileshare.pending_transcode_tenant_ids()`, `fileshare.pending_conversion_tenant_ids()`) backed by partial `(status, tenant_id)` indexes alongside channel-driven `(JobId, TenantId)` wakeups in `EtmfExportQueue`.
- **Pros:**
  - Collapses warm-cache HTTP tenant resolution to `0` SQL queries (`<= 1` on cold cache) and bounds tenant deactivation propagation to at most 5 seconds via `IClock`.
  - Reduces per-scope SQL wire statements from `3Q + 6W` to `2 + Q + 2W` while guaranteeing `cadence.tenant_id` rebinding on `ITenantScope` transitions and `''` reset on connection close/dispose.
  - Eliminates `O(T)` idle background worker fan-out in `apps/etmf` and `apps/fileshare` without weakening `FORCE ROW LEVEL SECURITY` or introducing external brokers.
- **Cons:**
  - Scoped `DbContext` instances hold one pooled connection from their first command until scope disposal, requiring handlers not to hold idle `DbContext` scopes across long non-database waits.

### Option 2: External Distributed Cache and Broker Queue

- **Overview:** Introduce Redis for tenant code caching and RabbitMQ / Kafka topics for standalone background worker dispatch.
- **Pros:**
  - Supports cross-node cache invalidation pub/sub and out-of-process worker fan-out.
- **Cons:**
  - Adds operational infrastructure dependencies, cross-store consistency failure modes, and broker credentials for workloads already coordinated cleanly within PostgreSQL 18 and in-process channels.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Combining request-scoped `HttpContext.Items` memoization, an `IClock`-governed 5-second active-tenant cache over a shared `NpgsqlDataSource`, coordinated single-pass save stamping, scoped `DbContext` connection leasing, and narrow `SECURITY DEFINER` UUID-only pending-work directory functions removes redundant SQL round-trips and idle worker polling while preserving every PostgreSQL RLS, 21 CFR Part 11 Merkle chain, and step-up signature invariant.

## 5. Consequences & Trade-offs

- **Positive Impact:** Warm HTTP requests execute zero `kernel.resolve_tenant_id` queries and check out one `NpgsqlDataSource` connection per `DbContext` scope; `SaveChanges` stamps tenant IDs and increments concurrency tokens once per save; `KnowledgeCatalogSeeder` reuses the resolved tenant UUID and memoizes seeded state per connection string; `EtmfExportWorker` sleeps with zero SQL polling when idle after startup recovery; and `Fileshare` workers bind `ITenantScope` only for tenants with pending rows.
- **Negative Impact / Technical Debt:** Deactivating a tenant in `kernel.tenants` can remain cached for up to 5 seconds on warm `PostgresTenantCodeResolver` instances before failing closed, and `apps/etmf` plus `apps/fileshare` own one additional EF Core migration each for their partial indexes and `SECURITY DEFINER` directory functions.
- **Mitigation Strategy:** Negative/inactive lookups are never cached, `IClock` controls deterministic TTL expiry in unit and integration tests, `StandaloneDbContextLeaseManager` re-validates `ITenantScope` before every command and resets `cadence.tenant_id` to `''` before pool return, and `cadence migrations verify all` plus `cadence check schema-doc` guard migration and schema parity.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/security`, `packages/database`, `apps/execution`, `apps/knowledge`, `apps/etmf`, `apps/fileshare`, `docs/schema/`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`, `tests/Cadence.UnitTests`, `tests/Cadence.IntegrationTests`.
- **Verification Plan:** Verified via `Cadence.UnitTests` (`StandaloneTenancyCachingAndStampingTests`, `EtmfExportTests`, `MediaTranscoderWorkerTests`, `DocumentConversionWorkerTests`), PostgreSQL 18 `Cadence.IntegrationTests` (`StandaloneTenantIsolationPostgresTests` wire-level `PgStatActivity` pool, `set_config`, RLS, Merkle audit chain, and worker directory function assertions), `Cadence.ArchitectureTests`, `cadence migrations verify all`, and `cadence check`.
