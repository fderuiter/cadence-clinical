# ADR 2026-08-30: Graph Schema Extensions for Arm-Aware Schedule of Activities (SoA)

## Status

Accepted

**Requirement:** PRD-SYS-001

## Context

The Cadence Clinical platform's metadata designer supports authoring complex, multi-arm clinical trials in compliance with GxP and the CDISC Unified Study Design Model (USDM) standards.

The baseline Neo4j graph database schema documented in Technical Design Document (TDD) §3.3 represents study schedules as a simple linear epoch-to-visit sequence (i.e., `StudyVersion` → `Epoch` → `Visit` → `Form` → `BiomedicalConcept`) and is limited to a single flat scalar attribute `visit_window_days` on the `Visit` node.

To represent real-world multi-arm clinical trial schedules of activities, we require schema enhancements that support:

1. Multi-arm applicability (knowing which `Visit` or `Procedure` occurs on which specific `StudyArm`).
2. Richer conditional timing windows that generalize simple `visit_window_days` values and include conditional execution triggers with regulatory justifications.

This document describes the schema extensions that bridge these gaps, cross-referencing `docs/adr/2023-01-01-neo4j-graph-database.md` and TDD §3.3.

## Decision

We decided to extend the Neo4j graph database schema to model Schedule of Activities (SoA) matrices as highly interconnected, arm-aware, and version-safe subgraphs. This is achieved by introducing the following graph schema extensions:

### 1. Arm-to-Entity Applicability Relationships

Instead of a simple flat hierarchy, we introduce direct relationship vectors to express arm applicability:

- `(arm:StudyArm)-[:APPLICABLE_TO]->(v:Visit)`
- `(arm:StudyArm)-[:APPLICABLE_TO]->(p:Procedure)`
- `(arm:StudyArm)-[:APPLICABLE_TO]->(ep:Epoch)`

This permits selective applicability of visits, epochs, and procedures to distinct cohorts or treatment paths, mirroring the CDISC USDM standard.

### 2. Rich Conditional TimingWindow Nodes

The flat integer attribute `visit_window_days` on `Visit` is generalized into a dedicated, highly queryable `TimingWindow` node:

- Relationship: `(source)-[:HAS_TIMING]->(tw:TimingWindow)` (where source can be a `Visit` or `Procedure`).
- Properties on `TimingWindow`:
  - `anchor_reference` (String): e.g., "Visit 1 / Randomization".
  - `target_day` (Integer): The target study day.
  - `min_offset` (Integer): Minimum allowed offset.
  - `max_offset` (Integer): Maximum allowed offset.
  - `conditional` (Boolean): True if applicability is conditional.
  - `reason` (String): Auditable justification reason required if conditional is True.

This structure perfectly mirrors CDISC USDM's `activityIsConditional` and `TimingWindow` models.

### 3. Preserving Diffability and GxP Traceability

All extended SoA elements (`StudyArm`, `Epoch`, `Visit`, `Procedure`, `TimingWindow`) compose the shared `SoAAuditMixin` Pydantic models and are stored in the graph with standard versioning metadata:

- Every mutation creates a new version of the node.
- The previous node state is preserved via a `[:PREVIOUS_VERSION]` relationship.
- This maintains flawless, graph-native version diffing (ensuring compatibility with the protocol amendment diffing and rehydration engine) and satisfies strict 21 CFR Part 11 audit trails.

## Alternatives Considered

- **Embedding JSON in Visit Nodes:** Instead of creating separate `TimingWindow` nodes, timing details could be stored as JSON fields inside `Visit`. However, this violates clean graph traversal paradigms and hinders Cypher-native queries targeting cohort timing.
- **Relational Mapping:** Storing the SoA matrix in Postgres. This would require cross-database joins between the Designer (Neo4j) and Relational (PostgreSQL) databases, increasing serialization overhead and breaking transactional integrity.

## Trade-offs

- **Positive:**
  - Full alignment with CDISC USDM encounter/activity semantics.
  - Extremely fast, graph-native compilation of Schedule of Activities tables for rendering.
  - Preserves perfect diffability and version lineage.
- **Negative:**
  - Slightly more complex Cypher queries for constructing the full matrix projection. This has been resolved by implementing high-performance matrix-assembly logic in `apps/designer/delta.py`.

## References

- **TDD §3.3:** The baseline metadata schemas.
- **ADR 2023-01-01:** Neo4j Graph Database for Clinical Metadata.
- **ADR-057:** Arm-Aware Schedule of Activities (SoA) Matrix Component.
