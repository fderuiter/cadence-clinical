# ADR-2238: Universal PostgreSQL Persistence Standard and Hybrid JSONB Record Projection

- **Status:** Accepted
- **Date:** 2026-09-03
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In the legacy Python platform topology, database persistence was fragmented across two tiers:

1. Core services (`execution`, `etmf`, `ctms`, `quality`, `org`) persisted state in PostgreSQL instances.
2. Seven operational microservices (`safety`, `interop`, `tickets`, `notifications`, `econsent`, `eisf`, `fileshare`) persisted state in isolated SQLite database files (`*.db`).

Furthermore, clinical observation models (`ClinicalObservation.additional_properties`) and eCRF submissions (`FormSubmission.form_data`) relied on untyped Python dictionaries (`dict[str, Any]`), bypassing compile-time safety and creating runtime schema fragility.

Under 21 CFR Part 11 and ICH GCP E6(R2) (`PRD-SYS-053`), every entity mutation requires immutable cryptographic audit logging (`GxPAuditInterceptor`) and atomic event publication (`MassTransit` Transactional Outbox). SQLite does not support PostgreSQL session context variables (`set_config('cadence.current_user_id', ...)`), database-level trigger functions, or concurrent outbox locking under high concurrency.

## 2. Decision Drivers & Constraints

- **21 CFR Part 11 Traceability (`PRD-SYS-053`):** Universal enforcement of non-repudiation, SHA-256 Merkle block sealing, and tamper-evident audit ledgers across all services.
- **Compile-Time Type Safety & Native AOT:** Elimination of runtime attribute errors and reflection-based dynamic dictionary access in C# 14 / .NET 10.
- **Dual-Write Elimination:** Guaranteed atomic persistence of domain entity state changes and outbound MassTransit domain events within a single PostgreSQL database transaction.
- **Schema Evolution & Flexibility:** Retaining the ability to support variable laboratory parameters, site-specific analyte units, and custom protocol properties without requiring physical database column migrations for every protocol amendment.

## 3. Options Considered

### Option 1: Universal PostgreSQL Persistence + Hybrid Strongly-Typed JSONB Record Projection (Selected)
- Consolidate all 18 microservices on PostgreSQL 14+ with logical database or schema-level multi-tenant isolation.
- Standardize on `Npgsql.EntityFrameworkCore.PostgreSQL` and the shared `GxPAuditInterceptor` from `Cadence.Database`.
- Model dynamic clinical attributes as strongly-typed C# 14 record hierarchies mapped to PostgreSQL `jsonb` via `System.Text.Json` source generator converters, paired with an unmapped fallback dictionary (`Dictionary<string, JsonElement>`) for forward compatibility.
- Adopt `MassTransit` with PostgreSQL Transactional Outbox for all cross-service asynchronous domain event dispatches.
- ***Pros:***
  - ✅ Addresses requirements
- ***Cons:***
  - ❌ Trade-offs accepted


### Option 2: Retain SQLite for Edge Services + Dynamic JsonDocument
- Keep SQLite for the 7 edge services via `Microsoft.EntityFrameworkCore.Sqlite`.
- Use unconstrained `JsonDocument` properties for observation and form data.
- Cons: Fractures persistence abstractions, prevents uniform database triggers and Merkle outbox integration, and forces dual-driver test configurations.
- ***Pros:***
  - ✅ Addresses requirements


## 4. Decision Outcome

**Chosen Option:** **Option 1 (Universal PostgreSQL Persistence + Hybrid Strongly-Typed JSONB Record Projection)**.

### Architectural Standards Adopted:

1. **Universal Persistence:** All services deploy `Npgsql.EntityFrameworkCore.PostgreSQL` targeting PostgreSQL 14+. The legacy SQLite database files are retired and migrated.
2. **Hybrid JSONB Mapping:** `ClinicalObservation.AdditionalProperties` is backed by PostgreSQL `jsonb`, strongly typed to polymorphic records (`LaboratoryObservationProperties`, `VitalSignProperties`) with Native AOT source generation and fallback to unmapped property dictionaries.
3. **Transactional Outbox Event Backbone:** All mutating domain commands write outbound events to `IntegrationOutbox` in the same EF Core transaction, dispatched asynchronously via `MassTransit`.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Standardizes the entire persistence layer on a single, production-grade RDBMS engine.
  - Enables uniform 21 CFR Part 11 `GxPAuditInterceptor` Merkle block sealing across all 18 microservices.
  - Completely eliminates dual-write failures between database commits and message dispatches.
  - Combines compile-time safety for known clinical properties with zero-downtime extensibility for unanticipated protocol fields.
- **Negative Impact / Liabilities:**
  - Local development environment requires Docker Compose PostgreSQL container rather than zero-dependency SQLite files.
  - Requires EF Core PostgreSQL migrations for previously schema-less SQLite tables.

## 6. Implementation & Verification

- **Documentation & Vocabulary:** Updated `CONTEXT.md` with `Universal PostgreSQL Persistence Standard` and `Hybrid Strongly-Typed JSONB Record Projection`.
- **Database Layer:** Standardized on `packages/database/src/Cadence.Database.csproj` with PostgreSQL provider.
- **Test Verification:** Verified via `tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj` and `tests/Cadence.UnitTests/Cadence.UnitTests.csproj` under `@req:PRD-SYS-053`.
