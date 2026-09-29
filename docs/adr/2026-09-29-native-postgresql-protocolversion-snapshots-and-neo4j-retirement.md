# ADR-2245: Native PostgreSQL ProtocolVersion Snapshots and Neo4j Retirement

- **Status:** Accepted
- **Date:** 2026-09-29
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Historically, the Cadence study authoring module (`apps/designer`) relied on Neo4j as its primary datastore to model CDISC USDM study definitions (study arms, epochs, encounters, activities, and biomedical concepts) as a property graph. While graph databases excel at unconstrained multi-hop traversals, clinical study protocols in regulated production environments are governed by strict lifecycle invariants: once an amendment is approved and published, the protocol structure is **permanently immutable**.

Running Neo4j as a separate datastore created distributed transactional boundaries between study design in Neo4j and trial execution (EDC, CTMS, eConsent) in PostgreSQL. Cross-datastore synchronization required outbox queues, two-phase commits, and complex drift-detection routines, creating vulnerability windows under 21 CFR Part 11 (PRD-SYS-053).

## 2. Decision Drivers & Constraints

- **Driver 1 (ACID Consistency & Audit Chains):** Under the Clinical Kernel architecture (ADR-2244), protocol applicability, subject enrollment, and visit schedules must be transactionally bound to protocol versions within PostgreSQL.
- **Driver 2 (Operational Simplification):** Eliminating Neo4j from the mandatory runtime stack reduces infrastructure overhead, local developer friction, and operational disaster recovery complexity.
- **Driver 3 (Deterministic DDF Compilation):** The Digital Data Flow (DDF) compilation engine requires deterministic, snapshot-based input structures, which are best represented as immutable, versioned trees.
- **Constraint:** Must retain full support for CDISC USDM v3.0/v4.0 data structures and high-speed SoA matrix generation.

## 3. Options Considered

### Option 1: Native PostgreSQL ProtocolVersion Snapshot Trees (Selected)

- **Overview:** Model `ProtocolVersion` as an immutable snapshot entity in the Kernel PostgreSQL database. The full USDM hierarchy (arms, epochs, encounters, activities, BCs) is stored as normalized relational entities combined with strongly-typed C# 14 record projections serialized to indexed `jsonb` columns. Protocol amendments create new immutable `ProtocolVersion` rows linked via predecessor pointers (`PredecessorVersionId`). Neo4j is dropped as a mandatory primary datastore; optional graph export endpoints remain for offline visualization.
- **Pros:**
  - ✅ Atomic cross-table transactions between protocol versions, site applicability, and subject visit schedules.
  - ✅ Zero cross-database synchronization lag or dual-write failure modes.
  - ✅ Full EF Core 10 Native AOT compile readiness and sub-millisecond query performance.
  - ✅ Drastically simplifies local development environment and CI test pipelines.
- **Cons:**
  - ❌ Complex arbitrary graph path queries (if ever needed) must be computed in memory or via recursive CTEs.

### Option 2: Retain Neo4j as Primary Authoring Datastore

- **Overview:** Continue authoring protocols in Neo4j and emit publication events to PostgreSQL.
- **Pros:**
  - ✅ Preserves existing Neo4j Cypher queries in `apps/designer`.
- **Cons:**
  - ❌ Maintains dual-datastore operational debt and network failure windows.
  - ❌ Precludes atomic transactional consistency between protocol applicability and clinical data capture.

## 4. Decision Outcome

- **Chosen Option:** Option 1: Native PostgreSQL ProtocolVersion Snapshot Trees.
- **Justification:** Retiring Neo4j for runtime operations aligns with the Clinical Kernel Modular Monolith (ADR-2244), providing bulletproof 21 CFR Part 11 transactional integrity and deterministic DDF compilation.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Unified single-database backup, recovery, and audit verification.
  - Eliminates Neo4j container requirements for developer onboarding and automated CI.
  - Direct relational joins between protocol versions and participant observations.
- **Negative Impact / Technical Debt:** Requires migrating existing USDM graph models in `apps/designer` to relational/JSONB snapshot entities.
- **Mitigation Strategy:** Provide automated migration scripts and keep graph export endpoints in `Cadence.Ddf` for visual rendering.

## 6. Implementation & Verification

- **Affected Assemblies:** `Cadence.Kernel.*`, `Cadence.Apps.Designer`, `Cadence.Ddf`, `Cadence.Apps.Execution`.
- **Verification Plan:**
  - Round-trip USDM v3.0/v4.0 JSON serialization and validation tests.
  - Snapshot immutability assertions: verify that modifying an approved protocol version throws GxP concurrency exceptions.
