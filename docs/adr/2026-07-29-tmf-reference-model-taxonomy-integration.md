# ADR-049: TMF Reference Model Taxonomy Integration

- **Status:** Accepted
- **Date:** 2026-07-29
- **Authors:** @fderuiter
- **Deciders:** @fderuiter, @jules

---

## 1. Context & Problem Statement

The eTMF microservice requires a robust, standardized mechanism to validate document ingestion against the DIA TMF Reference Model. We need to decide how to implement and model the catalog itself, how to manage named catalog versions, how to enforce validation on incoming artifacts, and how to maintain the corresponding requirement-to-test traceability.

This decision implements requirements under Trace-5.

## 2. Decision Drivers & Constraints

- **Driver 1 (Compliance & Traceability):** Regulatory standards (21 CFR Part 11, EU Annex 11, GAMP 5) require complete traceability from software requirements to automated verification.
- **Driver 2 (Immutability & Config Mutability):** The TMF Reference Model is an industry standard; taxonomy definitions must remain immutable at runtime to ensure validation consistency. However, milestone-to-artifact requirements mapping configuration (`MILESTONE_MANDATORY_ARTIFACTS`) must allow mutability to accommodate data-driven expected document list adjustments and custom study setups.
- **Driver 3 (Validation Performance):** High-throughput document ingestion must not be slowed down by heavy database-backed lookups or remote API queries.

## 3. Options Considered

### Option 1: Static Typed Catalog in Code (Selected)

Define the DIA TMF Reference Model catalog in-memory as an immutable, database-free Pydantic v2 package (`packages/core-models/tmf_reference_model`).

- **Pros:**
  - ✅ Extremely high lookup performance (no DB queries or disk IO).
  - ✅ Enforces strict runtime immutability (Pydantic frozen models) for catalog data (zones, sections, and artifacts).
  - ✅ Thread-safe and easily testable without external database fixtures.
- **Cons:**
  - ❌ Updating or adding new versions requires code changes and a new release cycle (though acceptable since official TMF Reference Model versions change very infrequently).

### Option 2: JSON/YAML Configuration Files

Store catalog definitions in JSON or YAML configuration files loaded dynamically at runtime.

- **Pros:**
  - ✅ Separates configuration from codebase.
- **Cons:**
  - ❌ Risk of accidental modification or corrupted file formats.
  - ❌ Missing native static typing or schema-level constraints on load.

### Option 3: Database-Backed Taxonomy Catalog

Store all zones, sections, and artifacts in relational tables (PostgreSQL or Neo4j).

- **Pros:**
  - ✅ Allows database queries for taxonomy relationships.
- **Cons:**
  - ❌ Adds significant latency and overhead to document ingestion.
  - ❌ Unnecessary schema and migration complexity for a standardized, mostly static dataset.

---

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Implementing a static, Pydantic-typed catalog satisfies all decision drivers. Performance is optimal because resolution occurs entirely in memory. It prevents configuration drift, guarantees absolute runtime immutability for reference taxonomy models, and simplifies validation because the entire catalog structure participates in strict static analysis.

### Named Catalog Versions & Active Default

We register three real catalog versions in a thread-safe registry:

- `v3.2.0`: Standard representative catalog retained strictly for historical reproducibility of pre-cutover records.
- `v3.2.0-complete`: The active default catalog representing the pure, complete standard DIA Reference Model v3.2.0. All new lookups, document ingestions, and validations resolve against this version by default.
- `v3.2.0-extended`: Layers Cadence-specific `is_extension=True` custom extensions on top of the complete catalog.

- Cross-reference the Standard-vs-Extension policy in [packages/core-models/tmf_reference_model/README.md](../../packages/core-models/tmf_reference_model/README.md) §3.
- Version isolation is enforced; once a version is registered, it cannot be mutated or overridden.

### Authoritative-Inventory Scope

To satisfy GxP completeness and DIA Reference Model alignment:

- The standard catalog version `v3.2.0-complete` is expanded to act as the authoritative standard inventory, covering full DIA standard artifacts, including restored standard-status artifacts such as Investigator CV (`05.02.03`) and Delegation of Authority Log (`05.02.04`).
- The representative catalog `v3.2.0` (with 18 artifacts) is kept frozen and immutable to interpret pre-cutover records reproducibly.

### Standard-versus-Extension Policy

To avoid taxonomy drift while supporting platform and sponsor-specific requirements, the system enforces a clear Standard-versus-Extension Policy:

- **Standard DIA Content:** Registered under `v3.2.0-complete` with `is_extension=False` for all standard artifacts.
- **Cadence-Specific Extensions:** Registered under `v3.2.0-extended` with `is_extension=True`. All extensions utilize distinct codes or non-standard suffixes (e.g., `05.02.99`, `10.01.99`) to prevent collision.
- **eISF-to-eTMF Propagation Mapping:** Mappings in `apps/eisf/adapters/adapter.py` map standard documents (Investigator CV, Delegation of Authority Log, and Financial Disclosure) to the authoritative complete catalog, while proprietary extensions (such as Medical License) are mapped to extension-only codes (`05.02.98`) to allow deterministic synchronization via `map_eisf_to_etmf` without standard catalog drift.

### Active-Version Cutover Policy

- **Immediate/Controlled Cutover:** The in-code registry sets `"v3.2.0-complete"` as the active default catalog version on startup (`set_active_version("v3.2.0-complete")`).
- Default document lookups, ingestions, and completeness checks resolve against `"v3.2.0-complete"` unless an explicit alternative version (like `"v3.2.0-extended"` or legacy `"v3.2.0"`) is requested.
- Switching/confirming the active default pointer is formally qualified against programmatic verification suites before release.

### Strict Ingestion Validation vs Heuristic Fallback

- Ingested documents must provide either an `artifact_type` (for name-based resolution) or an exact numeric `artifact_code` (for code-based resolution), or both, falling back to the active default when `taxonomy_version` is omitted. Resolution and precedence are handled deterministically via `resolve_artifact`.
- If optional `zone` and `section` values are supplied, they must be validated against the resolved catalog entry.
- If a hierarchy combination (zone, section, artifact) is mismatched or invalid, the ingestion is aborted and rejected with HTTP 422.

### Traceability Implications

- Full traceability is achieved by tagging test cases with the `@req:` convention.
- Test executions automatically populate the Requirements Traceability Matrix (RTM) and GxP Qualification Reports.

## 5. Consequences & Trade-offs

- **Positive Impact:** Strong validation guarantees that all documents in the eTMF conform exactly to the official DIA reference model taxonomy.
- **Negative Impact / Technical Debt / Hypothetical Scenario:** In a hypothetical future scenario where a major new DIA TMF Reference Model version is released (e.g., a potential future v4.0.0, which is not registered today), a developer would need to define its dictionary structure and register it in `tmf_reference_model` to make it available.
- **Mitigation Strategy:** The static-catalog approach leverages a unified model registry allowing multi-version registration and run-time selection, enabling older trials to remain bound to `v3.2.0` or `v3.2.0-complete` without disruption during any future cutover.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/core-models/tmf_reference_model/`, `apps/etmf/`
- **Verification Plan:** Verify using unit and integration tests covering explicit version selection, hierarchy resolution, invalid catalog combinations, and milestone alignment. Ensure that these are tracked by automated RTM and IQ/OQ/PQ scripts.

---

## 7. Epic #101 Completion Review & Lifecycle Closure

This section records the formal GxP-compliant closure and completion review for **Epic #101: DIA TMF Reference Model Taxonomy**. All child issues have been fully closed and verified:

### 1. Child Issues Resolution Matrix

- **Issue #745 (Standard Reference Taxonomy Catalog Scaffold):** Fully delivered. The Pure-Python, memory-isolated taxonomy catalog registry was implemented under `packages/core-models/tmf_reference_model/` and strict ingestion hierarchy validations were integrated in `apps/etmf/`.
- **Issue #688 (eISF-to-eTMF Document Propagation & De-Duplication):** Fully delivered. Bidirectional cross-walk mappings and deterministic correlation keys were implemented in `apps/eisf/adapters/adapter.py`. Standard artifacts (CV, DOA log, Financial Disclosure) map to the authoritative catalog, and the Medical License maps cleanly as a Cadence extension (`05.02.98`) to prevent taxonomy drift.
- **Issue #746 (Authoritative Active-Version Cutover Qualification):** Fully delivered. The default taxonomy version has successfully cut over to `"v3.2.0-complete"`. Robust regression tests confirm standard and custom propagation pathways.

### 2. Qualification Evidence

The platform's qualification suite provides cryptographic and programmatic assurance of TMF taxonomy integrity:

- **Taxonomy & Policy Tests (`tests/test_tmf_reference_model.py`):**
  - Verifies that `"v3.2.0-complete"` covers all 11 canonical Zones.
  - Verifies that standard and extension classifications adhere strictly to the **Standard-versus-Extension Policy** (with `is_extension` boolean attributes set appropriately).
  - Asserts complete thread-safe immutability of the catalog models at runtime, preventing configuration drift.
  - Asserts that legacy `"v3.2.0"` remains frozen and unaffected by newer registrations, ensuring reproducibility of pre-cutover documents.
- **Ingestion, Propagation, & Cutover Validation (`tests/test_etmf.py`, `tests/test_eisf_adapter.py`, `tests/test_eisf_sync.py`):**
  - Asserts that `apps/etmf/` strict ingestion validations correctly return HTTP 422 for unknown artifacts or mismatched zone/section hierarchies.
  - Verifies that successful ingestions persist the correct `taxonomy_version` and canonical `artifact_code` on `TMFDocument`.
  - Confirms seamless propagation of standard and extension artifacts from eISF to eTMF.
- **Task 2.4 Derived-Version Preservation Checkpoint:**
  - Standard GxP audit fields (`created_at`, `created_by`, `reason_for_change`, and `version_index`) are properly maintained on all mutated records.
  - Version-specific lookups and backward compatibility are maintained for historical/legacy `"v3.2.0"` records.

### 3. Conclusion & Sign-Off

All acceptance criteria of Epic #101 and associated child issues are met. The TMF Reference Model Taxonomy Integration is formally closed and accepted into the baseline.
