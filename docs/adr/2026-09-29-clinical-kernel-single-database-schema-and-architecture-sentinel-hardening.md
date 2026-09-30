# ADR-2247: Clinical Kernel Single Database Schema and Architecture Sentinel Hardening

- **Status:** Accepted
- **Date:** 2026-09-29
- **Authors:** @fderuiter, Antigravity AI
- **Deciders:** @fderuiter
- **Supercedes:** ADR-2244 (Extended & Hardened)

---

## 1. Context & Problem Statement

Following the clean-break migration to .NET 10 (ADR-2243) and the establishment of the Clinical Kernel Modular Monolith specification (`docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md`), three critical architectural and operational discrepancies emerged:

1. **Database Topology Discrepancy**: `.NET Aspire AppHost` (`apps/apphost/src/Program.cs`) provisioned 5 separate physical databases (`cadence-kernel`, `cadence-edc`, `cadence-etmf`, `cadence-ctms`, `cadence-quality`). This contradicted the Clinical Kernel specification §9, which requires a **single relational database** with PostgreSQL schema-level isolation (`kernel.*`, `edc.*`, `ctms.*`, `etmf.*`, `quality.*`). Multi-database boundaries prevent atomic cross-domain 21 CFR Part 11 transactions, composite foreign keys `(tenant_id, id)`, and unified bitemporal point-in-time state reconstruction.
2. **Architecture Sentinel Blindspots (Issue #4723)**: ArchUnitNET rules `K-XS-1` and `K-XS-2` in `tests/Cadence.ArchitectureTests/ArchitectureSentinelTests.cs` only registered 6 assemblies, omitting 12 microservices. Furthermore, the check only filtered classes inheriting from `AggregateRootBase`, allowing plain POCO classes (`Site`, `Organization` in `apps/org`) and prefixed types (`CtmsStudy`, `CtmsDelegation` in `apps/ctms`) to define duplicate clinical aggregates with `string` IDs, violating domain unification.
3. **21 CFR Part 11 GxP Traceability Gap (Issue #4717)**: `Cadence.Compliance.RtmGeneratorService` tracks 226 canonical requirements (192 PRD, 34 SRS). Only 8 were mapped in test attributes, leaving 218 unmapped. Additionally, 34 requirements were legacy Python `Trace-1..34` aliases from `docs/SRS.md` Table 7.2 referencing deleted Python test files, and a parser bug in `ScanTests` misattributed docstrings placed inside method bodies.

## 2. Decision Drivers & Constraints

- **Driver 1 (21 CFR Part 11 & GxP Invariants)**: Cross-domain mutations (e.g. subject enrollment triggering initial eCRF schedule generation and audit ledger persistence) must be committed atomically within a single PostgreSQL ACID transaction without distributed dual-write failure windows.
- **Driver 2 (Domain Purity & Modular Monolith)**: A single canonical clinical truth (`Cadence.Kernel.Domain`) must govern core entities (`Tenant`, `Organization`, `Person`, `Study`, `Site`, `Participant`, `Observation`, `Attestation`). Product modules must never define shadow tables or duplicate types.
- **Driver 3 (Deterministic Traceability)**: The Requirements Traceability Matrix (RTM) must reflect real verification status without false qualification or blocking CI on planned future sprint deliverables.

## 3. Options Considered

### Option 1: Single Relational Database with Schema Partitioning & Universal ArchUnit Sentinels (Selected)

- **Overview**: 
  - Standardize Aspire AppHost and microservice EF Core `DbContext` configurations on a single PostgreSQL database (`cadence`) with schema isolation: `kernel`, `edc`, `ctms`, `etmf`, `quality`.
  - Expand ArchUnitNET rules `K-XS-1` and `K-XS-2` across all 18 microservice assemblies with zero tolerance for local entity duplication.
  - Refactor `apps/org` and `apps/ctms` to project directly from `Cadence.Kernel.Domain` UUIDv7 entities.
  - Introduce a Two-Tier RTM verification lifecycle: `Qualified` (xUnit tested) vs `Planned` (milestone scheduled).
- **Pros:**
  - ✅ Preserves transactional ACID atomicity and outbox guarantees under 21 CFR Part 11.
  - ✅ Enables PostgreSQL Row-Level Security (RLS) across composite keys `(tenant_id, id)`.
  - ✅ Mathematically guarantees Clean Architecture boundaries via ArchUnitNET across all 18 assemblies.
  - ✅ Unblocks `dotnet cadence gxp sync` in strict mode.
- **Cons:**
  - ❌ Requires updating `apps/apphost`, EF Core migrations, and DbContext schema bindings.

### Option 2: Retain Multi-Database Isolation & Phased Sentinel Exclusions

- **Overview**: Maintain separate databases per microservice in Aspire, coordinate cross-domain state via MassTransit distributed sagas, and grant temporary exemptions to `apps/org` and `apps/ctms`.
- **Pros:**
  - ✅ Requires minimal initial migration refactoring.
- **Cons:**
  - ❌ Re-introduces distributed eventual consistency windows and two-phase commit overhead prohibited by GxP invariants.
  - ❌ Violates Clinical Kernel Specification §9 and allows architecture erosion to persist.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **Justification:** Conforming Aspire AppHost and microservices to a single relational database with schema isolation directly delivers on the Clinical Kernel mission. Universal ArchUnit sentinels protect the codebase from structural backsliding, while the Two-Tier RTM provides rigorous, honest regulatory accounting.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Single database connection string across Aspire and staging environments.
  - 100% architectural sentinel coverage across all 18 services.
  - Eliminates duplicate domain aggregates and redundant migrations in `apps/org` and `apps/ctms`.
  - Strict GxP synchronization passes deterministically.
- **Negative Impact / Operational Changes:**
  - `apps/org` and `apps/ctms` local duplicate entities (`Organization`, `Site`, `Personnel`, `CtmsStudy`) must be removed and replaced with references to `Cadence.Kernel.Domain`.
  - Aspire AppHost database registrations consolidate from 5 instances to 1 with schema assignments.

## 6. Implementation & Verification Plan

1. **DX & Build Blocker Remediation**:
   - Patch `MarkdownValidatorService.cs` directory exclusions (`.venv`, `bin`, `obj`, `dist`, `.antigravity`) and fenced code block tracking.
   - Decouple `scripts/setup_wizard.sh` (replace Stage 3 with native git pre-commit hook binding) and `scripts/build-docs.js` (remove Python preflight check).
   - Resolve `vitest` dependency in `apps/web`.
2. **Regulatory & Parser Fix**:
   - Fix `ScanTests` inside-method docstring regex in `RtmGeneratorService.cs`.
   - Update `docs/SRS.md` Table 7.2 to deprecate legacy `Trace-1..34` aliases in favor of canonical `PRD-*` requirements.
3. **Architecture Sentinel Expansion**:
   - Expand `ArchitectureSentinelTests.cs` to test all 18 microservice assemblies.
   - Refactor `apps/org` and `apps/ctms` to project from `Cadence.Kernel.Domain`.
4. **AppHost Consolidation**:
   - Update `apps/apphost/src/Program.cs` to provision single database `"cadence"`.
5. **Sprint 2 Core Kernel**:
   - Implement PostgreSQL Native Bitemporality (Issue #4725).
   - Implement ProtocolVersion Snapshots & DDF Compiler (Issue #4724).
