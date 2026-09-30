# ADR-2250: Kernel Tenant Isolation via Row-Level Security in a Shared Schema (K003)

- **Status:** Accepted
- **Date:** 2026-09-29
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The Clinical Kernel (ADR-2244) keeps all shared clinical truth in one PostgreSQL database with a single
`kernel` schema (ADR-2247). A Cadence tenant is a customer: the contractual and isolation boundary. A tenant
contains several organizations (sponsor, CRO, sites, labs), and one Cadence deployment serves many tenants.

Kernel specification §3.3 requires that cross-tenant references are impossible at the schema level (K-ID-1),
that row visibility is enforced by the database and cannot be bypassed by the application (K-ID-2), and that
cross-tenant exchange happens only through an audited `DataSharingAgreement` (K-ID-3). A leak between two
sponsors' trials is a 21 CFR Part 11 §11.10(d) access-control failure (PRD-SYS-053) and a confidentiality
breach under the sponsors' contracts.

ADR-2237 chose a dedicated PostgreSQL schema per tenant with a per-session `search_path`. That decision was
made for the Python services and predates the kernel. It conflicts with ADR-2247 (one `kernel` schema) and
with the kernel's migration model. This ADR decides how tenants are isolated inside the kernel.

## 2. Decision Drivers & Constraints

- **Driver 1 (defence in depth):** isolation must hold even when application code forgets a `WHERE tenant_id = …`.
- **Driver 2 (referential integrity):** a foreign key must not be able to point at another tenant's row.
- **Driver 3 (operability):** one migration run per deployment, not one per tenant; schema drift between tenants must be impossible.
- **Driver 4 (regulated customers):** some sponsors contractually require physical separation, separate backups or data residency.
- **Constraint:** EF Core 10 + Npgsql, pooled connections, PostgreSQL 16+ on managed services (Aurora, Azure Flexible Server).

## 3. Options Considered

### Option 1: Shared schema, `tenant_id` column, composite keys and Row-Level Security (Selected)

- **Overview:** Every kernel table carries `tenant_id uuid NOT NULL`. Primary keys stay `id`, but every table also
  has `UNIQUE (tenant_id, id)` and every foreign key is composite `(tenant_id, x_id) REFERENCES t (tenant_id, id)`.
  RLS is enabled **and forced** on every kernel table with one policy:
  `USING (tenant_id = NULLIF(current_setting('cadence.tenant_id', true), '')::uuid) WITH CHECK (same)`.
- **Pros:**
  - ✅ Isolation enforced by PostgreSQL for every query path, including raw SQL and reporting.
  - ✅ Composite FKs make a cross-tenant reference a constraint violation (K-ID-1).
  - ✅ One schema, one migration history, no drift.
  - ✅ Works with connection pooling when the tenant is set per transaction.
- **Cons:**
  - ❌ Every query pays the policy predicate; indexes must lead with `tenant_id`.
  - ❌ A mistake in role setup (owner role, `BYPASSRLS`) silently disables protection, so it needs a test.

### Option 2: Schema per tenant with `search_path` (ADR-2237)

- **Overview:** Clone the `kernel` schema for each tenant and switch `search_path` per request.
- **Pros:**
  - ✅ Per-tenant `pg_dump` and drop are simple.
- **Cons:**
  - ❌ Migrations fan out to N schemas; a failure part-way leaves tenants on different schema versions.
  - ❌ Catalog bloat (tables × tenants), poor plan caching, and pooled connections that leak `search_path`.
  - ❌ Nothing stops a query that names a schema explicitly; isolation depends on the application.

### Option 3: Database per tenant

- **Overview:** One PostgreSQL database per tenant; connection string chosen by tenant.
- **Pros:**
  - ✅ Strongest isolation; per-tenant backup, restore, encryption key and region.
- **Cons:**
  - ❌ Operationally heavy for many small tenants; fleet-wide migrations and monitoring.
  - ❌ Cross-tenant data sharing (K-ID-3) becomes cross-database.

## 4. Decision Outcome

- **Chosen Option:** Option 1, with Option 3 as a supported **upgrade path**.
- **Justification:** RLS gives database-enforced isolation without per-tenant schema drift, and composite keys make
  cross-tenant references impossible. Because the schema is identical and every row already carries `tenant_id`,
  a regulated customer can be placed in a dedicated database (Option 3) by routing its tenant to its own
  connection string. Nothing in the model changes. Schema-per-tenant is rejected.

Rules that implement the decision:

1. **Roles.** Migrations run as `cadence_owner` (owns tables). The application connects as `cadence_app`, which is
   not the owner, has no `BYPASSRLS`, and has no `UPDATE`/`DELETE` on insert-only tables (spec §5.3). `FORCE ROW LEVEL
   SECURITY` is set anyway so owner mistakes are caught.
2. **One place sets the tenant.** A Kernel.Persistence connection/transaction interceptor runs
   `SELECT set_config('cadence.tenant_id', @tenant, true)` (transaction-local) at the start of every transaction,
   from the authenticated `Actor`'s tenant. No other code sets it. A transaction with no tenant set sees zero rows
   and cannot insert: the policy compares against `NULL`, and `NULLIF` covers the empty value a pooled
   connection holds after a transaction-local setting resets.
3. **System work** (outbox relay, scheduled chain verification) runs as a named ServiceAccount per tenant and sets
   the tenant the same way. There is no "all tenants" application connection. Fleet jobs iterate tenants.
4. **Cross-tenant exchange** uses an explicit `DataSharingAgreement` row in *both* tenants and a dedicated
   command that copies or projects data. Sharing is never implemented by relaxing a policy.
5. **Indexes** lead with `tenant_id`. Observation partitioning is by `(tenant_id, study_id)` (spec §13).
6. **Tenant routing** (Option 3 upgrade) is a `ITenantConnectionResolver` in Kernel.Persistence. The default returns
   the shared database.

## 5. Consequences & Trade-offs

- **Positive Impact:** Cross-tenant reads and FK inserts fail at the database layer (spec §14 test 5). One migration
  history for all tenants. Customers who need physical isolation get it without a fork.
- **Negative Impact / Technical Debt:** ADR-2237 is superseded; its schema-provisioning pipeline is not carried into
  the kernel. Every kernel entity and FK needs a `tenant_id`. Module schemas (`edc`, `ctms`, …) must adopt the same
  policy before they hold tenant data.
- **Mitigation Strategy:** An architecture test asserts every `kernel.*` table has `tenant_id`, RLS enabled and
  forced, and composite FKs. A Testcontainers test connects as `cadence_app` and proves a cross-tenant read returns
  nothing and a cross-tenant FK insert fails.

## 6. Implementation & Verification

- **Affected Assemblies:** `Cadence.Kernel.Persistence` (policies, interceptor, roles in migrations),
  `Cadence.Kernel.Application` (actor → tenant resolution), `Cadence.ArchitectureTests`.
- **Tracked by:** #4780 (RLS and composite FKs), #4731 (spec §14 test 5).
- **Verification Plan:**
  - Migration test: RLS enabled and forced on every `kernel` table; `cadence_app` lacks `BYPASSRLS`.
  - Integration test (PostgreSQL): tenant A cannot read tenant B's rows; FK insert pointing at tenant B's row fails;
    a transaction with no tenant set reads zero rows.

### Implementation notes (#4780, 2026-09-30)

These refine how the rules above are carried out; the decision is unchanged.

- **Rule 2, scope of the setting.** `KernelTenantConnectionInterceptor` sets `cadence.tenant_id` when a kernel
  context opens a connection and clears it before the connection is closed, instead of once per transaction. A command
  reads its aggregate before it opens its write transaction, and EF Core runs those reads as separate statements, so
  a transaction-local value would be gone before the reads. Each connection lease belongs to one context, and each
  context is bound once to one tenant by `KernelTenantDbContextFactory`. A context with no tenant cannot open a
  connection. Npgsql also discards session state when a connection returns to its pool, and the policies compare with
  `NULLIF(…, '')`, so a cleared value sees nothing.
- **Rule 3, listing tenants.** `kernel.tenant_ids()` is a `SECURITY DEFINER` function that returns tenant ids only.
  It turns on `cadence.tenant_directory` for its own execution, and a read-only policy on `tenants` admits rows only
  when that setting is on and the caller is the table owner, which inside the function it is. The application role
  can call the function but cannot reproduce it: it does not own the table. The outbox relay and the verify-and-seal
  job list tenants this way and then work through a connection scoped to each tenant. The relay's batch size is per
  tenant.
- **Persons (K-ID-4).** `people` has no `tenant_id`. Its policy shows a person only when the current tenant has a
  `tenant_memberships` row for them, and any tenant may register a person. Organization memberships and grants arrive
  with the authority model (#4781). The global unique index on `people.email` still reveals whether an address is
  registered somewhere; the IdentityVault (#4782) replaces it with blind indexes.
- **Rule 1, grants.** `KernelDatabase.GrantAppRoleSql(role)` grants the application role read, insert and update on
  the kernel tables, no update on insert-only tables, no delete, nothing on the migration history, and execute on the
  directory function. Role creation stays with deployment.
- **Scope.** The command tables (`provenance_activities`, `domain_events`, `audit_entries`, …) carry `tenant_id`
  and the policy but no foreign keys to each other; they are written only by the pipeline in one transaction.
- **Verified by** `KernelTenantIsolationPostgresTests` (schema-wide RLS check, application-role check, cross-tenant
  read, cross-tenant foreign key, no tenant, persons, tenant directory, pipeline under RLS),
  `KernelDbContextModelTests` (composite foreign keys in the model), and the kernel PostgreSQL fixture, which now runs
  every pipeline test as a non-owner role without `BYPASSRLS`.
