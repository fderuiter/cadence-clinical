# Specification: Mathematical Relational & Tensor-Algebraic MDR (Neo4j Decommissioning)

## Problem Statement

Clinical protocol metadata in Cadence Clinical was previously persisted using a Neo4j property graph database. While clinical protocol standards (such as CDISC USDM) refer to study definitions as "protocol graphs," the actual data structure is not an arbitrary-scale graph; it is a **stratified hierarchy (Poset)** coupled with a **bipartite incidence matrix** (Schedule of Activities) and an **acyclic version lineage tree (DAG)**.

Using Neo4j introduced significant architectural, operational, and regulatory friction:
1. **Operational Weight & JVM Overhead:** Running Neo4j alongside PostgreSQL and Redis required a high idle memory footprint (>1.5 GB), added 30–60s startup overhead in local development and CI runners, and complicated multi-database reset and backup/restore scripts.
2. **Dual-Database Split:** Cadence's execution engine (EDC), CTMS, eTMF, eISF, and Quality modules all run on PostgreSQL / SQLite. The Designer alone required Neo4j, preventing transactional atomicity across study authoring and execution deployment without complex distributed transaction orchestration.
3. **Weak Schema & Foreign Key Enforcement:** Property graph databases permit dynamic, untyped properties and lack strict 3NF referential integrity cascades. In a GxP (21 CFR Part 11) validated clinical system, missing foreign key constraints or unvalidated node properties create regulatory audit liabilities.
4. **Suboptimal Matrix Math:** Calculating the Schedule of Activities (SoA) visit-by-procedure matrix required multi-hop Cypher queries (`(Encounter)-[:PERFORMS]->(Activity)`) rather than direct relational tensor lookups and matrix projections.

---

## Solution

Decommission Neo4j entirely and replace it with a **Mathematically Rigorous Relational & Tensor-Algebraic Engine** implemented natively in PostgreSQL (using SQLModel / SQLAlchemy) with SQLite support for sub-second isolated unit testing:

1. **Protocol Structure as a Stratified Poset (Partially Ordered Set):**
   - Study hierarchy ($\text{Study} \to \text{StudyVersion} \to \text{StudyDesign} \to \text{StudyEpoch} \to \text{Encounter}$) is stored in strictly normalized relational tables with monotonic sequence indices and cascading foreign keys.
2. **Schedule of Activities as a 3NF Bipartite Incidence Tensor:**
   - The SoA is modeled as a first-class relational incidence relation:
     $$M \subseteq \text{Arm} \times \text{Encounter} \times \text{Activity} \to \text{CellMetadata}$$
     stored in table `soa_matrix_cells(study_version_id, arm_id, encounter_id, activity_id, is_mandatory, rule_id, config_json)`. This yields $O(1)$ cell lookups, atomic cell toggles, and sub-5ms matrix view compilations via simple SQL projections.
3. **Version Lineage & Amendment Trees as a Directed Acyclic Graph (DAG):**
   - Study version history ($\text{StudyVersion}_i \xrightarrow{\text{parent}} \text{StudyVersion}_j$) is modeled with `parent_version_id` and `base_version_id` foreign keys, queried via PostgreSQL Recursive Common Table Expressions (CTEs).
   - Version snapshotting executes via atomic single-transaction SQL queries (`INSERT ... SELECT`).
   - Immutability for non-`DRAFT` statuses (`APPROVED`, `LOCKED`, `PUBLISHED`) is strictly enforced at the repository layer and backed by HMAC SHA-256 canonical state signatures.
4. **Global MDR & Library Object In-Use Constraints:**
   - Global Library templates are stored in `global_library_objects` with stable root identifiers and linear version indices.
   - References in active recruiting studies are verified via indexed $O(\log N)$ relational joins on `study_library_instances`.
5. **Lossless USDM Serialization:**
   - Ingests and exports CDISC USDM v2.0, v3.0, and v4.0 JSON via strict Pydantic v2 schemas, retaining the canonical raw JSON in `study_versions.raw_usdm_payload` for bit-exact Part 11 provenance.

---

## User Stories

1. As a **Study Designer**, I want to create and edit clinical study arms, epochs, and visits in a normalized relational model, so that study structures are validated against strict schema constraints in real time.
2. As a **Study Designer**, I want to toggle activity assignments across visits in the Schedule of Activities matrix with sub-10ms response times, so that authoring large oncology protocols (100+ procedures × 50+ visits) is instantaneous.
3. As a **Study Designer**, I want to instantiate standardized forms and visit schedules from the Global MDR Library into my study, so that institutional best practices are reused without manual duplication.
4. As a **Study Designer**, I want to create a protocol amendment branch (e.g., v2.0-Draft from v1.0-Published), so that the entire study protocol graph is atomically snapshotted without blocking active patient data capture on v1.0.
5. As a **Study Designer**, I want to view a visual diff between two study versions computed via relational set differences, so that all protocol changes across visits, activities, and SoA cells are highlighted accurately.
6. As a **Data Manager**, I want to export the complete protocol definition into standard CDISC USDM v3.0/v4.0 JSON, so that downstream EDC and biostatistical pipelines receive fully compliant specifications.
7. As a **Data Manager**, I want the system to reject direct mutations to library objects that are currently referenced by active recruiting studies, so that in-flight trials are protected against unapproved breaking changes.
8. As a **Quality & Compliance Auditor**, I want all study versions to have immutable audit fields (`created_at`, `created_by`, `reason_for_change`, `version_index`) and cryptographic HMAC SHA-256 signatures, so that 21 CFR Part 11 tamper-evidence is guaranteed.
9. As a **Quality & Compliance Auditor**, I want database-enforced immutability on `LOCKED` and `PUBLISHED` study versions, so that historical protocol baselines can never be overwritten.
10. As a **Site Investigator / CRC**, I want instant protocol matrix lookups during patient visits without database latency or connection dropouts, so that subject visit schedules are always accurate.
11. As a **Developer / Agent**, I want to run all Designer unit and integration tests against an in-memory SQLite store or local PostgreSQL without starting a Neo4j Docker container, so that test suites execute in seconds.
12. As a **DevOps Engineer**, I want a single unified PostgreSQL database instance across all microservices (`apps/designer`, `apps/execution`, `apps/ctms`), so that operational maintenance, backups, and disaster recovery use standard relational tooling.
13. As a **DevOps Engineer**, I want the multi-database reset CLI (`cadence db reset`) to purge and seed relational tables in under 5 seconds without JVM detachment or Cypher schema dropping, so that CI workflows run efficiently.
14. As an **AI Protocol Digitization Pipeline**, I want to ingest unstructured protocol PDFs and commit extracted USDM entities into relational tables in a single atomic SQL transaction, so that extraction failures roll back cleanly without leaving orphaned nodes.

---

## Implementation Decisions

### A. Mathematical Relational Domain Model

#### 1. Poset Hierarchy & Core Entities
- `studies`: `(id, title, protocol_id, phase, therapeutic_area, status, created_at, created_by)`
- `study_versions`: `(id, study_id, version_tag, version_index, status, parent_version_id, base_version_id, branch_name, signature, raw_usdm_payload, reason_for_change, created_at, created_by)`
- `study_designs`: `(id, study_version_id, name, design_type)`
- `study_epochs`: `(id, study_version_id, name, epoch_type, sequence_index)`
- `study_arms`: `(id, study_version_id, name, arm_type, target_sample_size, description)`
- `encounters`: `(id, study_version_id, epoch_id, name, encounter_type, target_day, window_lower, window_upper, is_mandatory)`
- `activities`: `(id, study_version_id, name, cdash_domain, biomedical_concept_code, description)`
- `biomedical_concepts`: `(id, study_version_id, name, concept_code, display_name, cdash_domain, definition)`
- `biomedical_concept_properties`: `(id, biomedical_concept_id, name, cdash_variable, data_type, mandatory, grid_span, config_json)`
- `eligibility_criteria`: `(id, study_version_id, criterion_type, identifier, text_expression, logical_expression, template_json)`
- `soa_matrix_cells`: `(id, study_version_id, arm_id, encounter_id, activity_id, is_mandatory, rule_id, config_json)`
- `global_library_objects`: `(id, root_id, version_index, object_type, name, payload_json, status, sponsor_id, is_deleted)`
- `study_library_instances`: `(id, study_version_id, library_object_id, instantiated_version, overridden_properties_json)`
- `designer_audit_logs`: `(id, study_id, study_version_id, entity_type, entity_id, action, user_id, timestamp, reason_for_change, previous_state_json, new_state_json)`

### B. Architecture & Codebase Decoupling
1. **Hexagonal Persistence Ports:**
   - `StudyRepositoryPort`, `LibraryRepositoryPort`, and `RulesRepositoryPort` implementations powered by `SQLAlchemy` async sessions.
   - `MockStudyRepository` and `InMemoryRepository[T]` from `packages/testing/fakes` provide in-memory zero-dependency testing.
2. **Database Engine & Environment:**
   - Dedicated PostgreSQL database `cadence_designer` (or dedicated schema) on the shared PostgreSQL instance.
   - Remove `neo4j` Python package from `apps/designer/pyproject.toml` and root requirements.
   - Remove Neo4j service and health checks from `docker/docker-compose.yml` and CI workflows.
3. **Immutability & 21 CFR Part 11 Compliance:**
   - Version cloning uses a single atomic SQL transaction copying epochs, encounters, activities, concepts, and SoA cells into the new `study_version_id`.
   - Repositories reject write operations when `study_version.status IN ('APPROVED', 'SIGNED', 'LOCKED', 'PUBLISHED', 'ARCHIVED')`.
   - Version state is verified on read and signed on write using canonical SHA-256 signatures (`generate_canonical_signature`).

---

## Testing Decisions

1. **Test Philosophy:**
   - Test external REST API behavior and domain invariants, never internal SQL query strings or database implementation details.
2. **Seams to Test:**
   - **Designer REST API Seam:** All endpoints in `apps/designer/presentation/routers/` tested with `AsyncClient` against in-memory/SQLite and live PostgreSQL test databases.
   - **USDM Round-Trip Parity Seam:** Given a standard CDISC USDM JSON payload, `POST /api/v1/designer/digitization/commit` followed by `GET /api/v1/studies/{id}/usdm` must return bit-exact equivalent data structures.
   - **SoA Matrix Compilation Seam:** Verify matrix row/column generation and arm filtering across various protocol structures.
   - **Immutability & In-Use Enforcement:** Verify that mutating locked study versions or amending active library objects raises strict 409 / 403 HTTP errors.

---

## Out of Scope

- Modifying the Execution engine's PostgreSQL schema (`apps/execution/` is already relational).
- Introducing alternative graph query languages (GraphQL, SPARQL) into the Designer.
- Distributed multi-region database sharding (standard PostgreSQL master/replica connection pooling satisfies all SLA requirements).
