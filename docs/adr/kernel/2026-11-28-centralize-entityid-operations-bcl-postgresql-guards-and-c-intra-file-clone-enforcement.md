# ADR-KERNEL-20261128-centralize-entityid-operations-bcl-postgresql-guards-and-c-intra-file-clone-enforcement: Centralize EntityId Operations, BCL PostgreSQL Guards, and C# Intra-File Clone Enforcement

- **Status:** Accepted
- **Date:** 2026-11-28
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001

---

## 1. Context & Problem Statement

Following `ADR-2691` (which enabled intra-file 15-line clone enforcement for JavaScript, TypeScript, and Vue while keeping `.cs` and `.css` on cross-file-only scanning), three structural C# duplication patterns remained across the kernel, persistence layers, and service modules:

1. **Repeated `IEntityId<TSelf>` Boilerplate & Per-File Converter Subclasses:** All 38 strongly-typed UUIDv7 `IEntityId<TSelf>` identifier structs in `Cadence.Kernel.Abstractions` (`ADR-2481`) repeated multi-line `CompareTo`, `Parse`, and `TryParse` implementations alongside 76 empty per-file `*IdTypeConverter` and `*IdJsonConverter` subclasses.
2. **Triplicated PostgreSQL Bitemporal, Insert-Only, RLS, and Session SQL Builders:** Standalone persistence (`packages/database`), module SDK persistence (`Cadence.Kernel.Modules`), and kernel persistence (`Cadence.Kernel.Persistence`, `ExecutionModuleDatabase`, `IntelligenceDatabase`) maintained separate copies of PostgreSQL identifier quoting, `KT001` bitemporal append-only triggers (`ADR-2246`), `KA001` insert-only triggers, tenant Row-Level Security policies and role grants (`ADR-2250`, `ADR-2548`, `ADR-2600`), and `pg_catalog.set_config('cadence.tenant_id', @tenant, false)` `DbCommand` session binding.
3. **Unchecked Intra-File C# Clones:** Because `DuplicationDetectorService.IntraFileDuplicationExtensions` excluded `.cs`, 24 C# files across `apps/` and `packages/` accumulated 15-line intra-file clone windows across endpoints, RBAC grant tables, simulation builders, persistence projections, and CLI tooling services.

## 2. Decision Drivers & Constraints

- **Pure BCL Kernel Abstractions & Standards Isolation (`PRD-SYS-001`):** `Cadence.Kernel.Abstractions` must remain free of `Cadence.*`, EF Core, ASP.NET Core, and Npgsql dependencies (`KernelLayeringTests`) and free of CDISC dataset or variable names (`StandardsBoundaryTests`, `K-PROJ-1`), while preserving `K-XS-1`, `K-XS-2`, and `K-MOD-1` boundaries (`KernelBoundaryTests`, `ADR-2600`).
- **Native AOT Serialization Compatibility:** `System.Text.Json` source generation in `KernelJsonSerializerContext` requires closed generic `[JsonConverter(typeof(EntityIdJsonConverter<TSelf>))]` attributes rather than open-generic reflection converter factories, alongside closed generic `[TypeConverter(typeof(EntityIdTypeConverter<TSelf>))]` attributes for route and query model binding.
- **Exact SQL DDL & Runtime Parity:** Shared SQL and session builders must preserve exact DDL output across strict lower-case `snake_case` kernel/module schemas and mixed-case standalone EF Core schemas (`_version`, `VersionIndex`, `version_index`, `DROP ... IF EXISTS` idempotency, and conditional `DO $$` role grants).
- **Zero-Exemption C# Intra-File Clone Gate (`PRD-SYS-001`):** `DuplicationDetectorService` must enforce 15-line intra-file clone detection on `.cs` files (sorting same-file clone pairs by `StartLine`) with zero `.duplication-baseline.txt` entries while keeping `.css` cross-file-only.

## 3. Options Considered

### Option 1: Closed Generic `IEntityId<TSelf>` Converters + `EntityIdOperations`, Pure BCL `Cadence.Kernel.Sql` Builders, Private Helper Extraction, and `.cs` Intra-File Clone Gate (Selected)

- **Overview:** Make `EntityIdTypeConverter<TId>` concrete alongside `EntityIdJsonConverter<TId>`, decorate all 38 `IEntityId<TSelf>` structs with closed generic converter attributes, delete all 76 per-file converter subclasses, and delegate `CompareTo`, `Parse`, and `TryParse` to `EntityIdOperations` (preserving deterministic `For` factories on `ProtocolElementId` and `ValidationFindingId`). Introduce pure BCL PostgreSQL DDL and `DbCommand` helpers under `Cadence.Kernel.Sql` (`PostgresIdentifier`, `PostgresBitemporalSql`, `PostgresInsertOnlySql`, `PostgresTenantRlsSql`, `PostgresTenantSession`) in `Cadence.Kernel.Abstractions` and delegate standalone, module, and kernel persistence wrappers to them. Refactor all intra-file C# clone windows into module-private helpers and add `.cs` to `DuplicationDetectorService.IntraFileDuplicationExtensions`.
- **Pros:**
  - Eliminates 76 redundant converter subclasses and multi-line identifier parsing copies while preserving Native AOT JSON serialization and ASP.NET Core model binding.
  - Consolidates `KT001`, `KA001`, RLS policy, role grant, and `cadence.tenant_id` session SQL into one pure BCL layer in `Cadence.Kernel.Abstractions` verified by `KernelLayeringTests` and `BitemporalPersistenceTests`.
  - Prevents copy-pasted 15-line C# blocks inside a single file from bypassing `cadence check duplication` without introducing compiler plugins or `.duplication-baseline.txt` debt.
- **Cons:**
  - Parameterizes `Cadence.Kernel.Sql` builders with explicit options (`allowMixedCaseIdentifiers`, `dropIfExists`, `mutableColumns`, `quotePolicyName`) to preserve exact SQL compatibility with existing migrations and schema assertions.

### Option 2: Roslyn Source Generators for Identifiers and `.duplication-baseline.txt` Entries for Intra-File C# Clones

- **Overview:** Add a custom Roslyn incremental generator project for `IEntityId<TSelf>` boilerplate and baseline existing intra-file C# clones in `.duplication-baseline.txt`.
- **Pros:**
  - Reduces one-line forwarding members on `IEntityId<TSelf>` structs.
- **Cons:**
  - Introduces compiler-plugin build complexity and IDE/AOT coupling, leaves triplicated SQL guards unaddressed, and violates shrink-only duplication baseline governance (`ADR-2832`).

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Closed generic `EntityIdTypeConverter<TId>` / `EntityIdJsonConverter<TId>` attributes paired with `EntityIdOperations`, pure BCL `Cadence.Kernel.Sql` builders in `Cadence.Kernel.Abstractions`, targeted private helper extraction across C# services/endpoints/RBAC definitions, and `.cs` intra-file clone enforcement in `DuplicationDetectorService` eliminate duplicated clinical infrastructure and guard against regression with zero baseline debt.

## 5. Consequences & Trade-offs

- **Positive Impact:** Adding a new `IEntityId<TSelf>` struct requires only closed generic converter attributes and one-line `EntityIdOperations` delegations; updates to PostgreSQL `KT001`/`KA001` triggers, tenant RLS policies, or `cadence.tenant_id` session binding flow from `Cadence.Kernel.Sql` across standalone, module, and kernel persistence stacks; and `cadence check duplication` enforces 0 cross-file and 0 intra-file 15-line clones across C#, JavaScript, TypeScript, and Vue.
- **Negative Impact / Technical Debt:** `.css` remains cross-file-only in `DuplicationDetectorService` so light/dark theme token blocks in a single stylesheet do not trigger false positives.
- **Mitigation Strategy:** Cross-file `.css` clone checks remain enforced across `apps/` and `packages/`, and `DomainEngineUnitTests`, `KernelIdentifierTests`, `NodeRefTests`, `BitemporalPersistenceTests`, `KernelBoundaryTests`, and `KernelLayeringTests` verify every contract continuously.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/kernel/src/Cadence.Kernel.Abstractions`, `packages/kernel/src/Cadence.Kernel.Modules`, `packages/kernel/src/Cadence.Kernel.Persistence`, `packages/kernel/src/Cadence.Kernel.Application`, `packages/kernel/src/Cadence.Kernel.Api`, `packages/kernel/src/Cadence.Rules`, `packages/database`, `packages/security`, `packages/tooling_core`, `apps/ctms`, `apps/etmf`, `apps/execution`, `apps/intelligence`, `apps/interop`, `apps/kernel-api`, `tests/Cadence.UnitTests`, `docs/adr/kernel/`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `dotnet build Cadence.sln`, `Cadence.ArchitectureTests` (`KernelBoundaryTests`, `KernelLayeringTests`, `StandardsBoundaryTests`), `Cadence.Kernel.ConsumerTests`, `Cadence.UnitTests` (`KernelIdentifierTests`, `NodeRefTests`, `BitemporalPersistenceTests`, `DomainEngineUnitTests`, and service/endpoint unit tests), and `cadence check duplication adrs permission-matrix openapi doc-index markdown no-emojis file-hygiene`.
