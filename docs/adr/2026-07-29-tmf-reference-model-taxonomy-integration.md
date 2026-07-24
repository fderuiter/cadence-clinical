# ADR-049: TMF Reference Model Taxonomy Integration

* **Status:** Accepted
* **Date:** 2026-07-29
* **Authors:** @jules
* **Deciders:** @fderuiter, @jules

---

## 1. Context & Problem Statement
The eTMF microservice requires a robust, standardized mechanism to validate document ingestion against the DIA TMF Reference Model. We need to decide how to implement and model the catalog itself, how to manage named catalog versions, how to enforce validation on incoming artifacts, and how to maintain the corresponding requirement-to-test traceability.

## 2. Decision Drivers & Constraints
* **Driver 1 (Compliance & Traceability):** Regulatory standards (21 CFR Part 11, EU Annex 11, GAMP 5) require complete traceability from software requirements to automated verification.
* **Driver 2 (Immutability):** The TMF Reference Model is an industry standard; taxonomy definitions must remain immutable at runtime to ensure validation consistency.
* **Driver 3 (Validation Performance):** High-throughput document ingestion must not be slowed down by heavy database-backed lookups or remote API queries.

## 3. Options Considered

### Option 1: Static Typed Catalog in Code (Selected)
Define the DIA TMF Reference Model catalog in-memory as an immutable, database-free Pydantic v2 package (`packages/core-models/tmf_reference_model`).
* **Pros:**
  * ✅ Extremely high lookup performance (no DB queries or disk IO).
  * ✅ Enforces strict runtime immutability (Pydantic frozen models).
  * ✅ Thread-safe and easily testable without external database fixtures.
* **Cons:**
  * ❌ Updating or adding new versions requires code changes and a new release cycle (though acceptable since official TMF Reference Model versions change very infrequently).

### Option 2: JSON/YAML Configuration Files
Store catalog definitions in JSON or YAML configuration files loaded dynamically at runtime.
* **Pros:**
  * ✅ Separates configuration from codebase.
* **Cons:**
  * ❌ Risk of accidental modification or corrupted file formats.
  * ❌ Missing native static typing or schema-level constraints on load.

### Option 3: Database-Backed Taxonomy Catalog
Store all zones, sections, and artifacts in relational tables (PostgreSQL or Neo4j).
* **Pros:**
  * ✅ Allows database queries for taxonomy relationships.
* **Cons:**
  * ❌ Adds significant latency and overhead to document ingestion.
  * ❌ Unnecessary schema and migration complexity for a standardized, mostly static dataset.

---

## 4. Decision Outcome
* **Chosen Option:** Option 1
* **Justification:** Implementing a static, Pydantic-typed catalog satisfies all decision drivers. Performance is optimal because resolution occurs entirely in memory. It prevents configuration drift, guarantees absolute runtime immutability, and simplifies validation because the entire catalog structure participates in strict static analysis.

### Named Catalog Versions & Active Default
* We register named catalog versions (`v3.2.0`, `v4.0.0`) in a thread-safe registry.
* `v3.2.0` is designated as the active default version.
* Version isolation is enforced; once a version is registered, it cannot be mutated or overridden.

### Strict Ingestion Validation vs Heuristic Fallback
* Rather than using a loose heuristic fallback (which might permit incorrectly categorized or typo-prone files), the system enforces strict hierarchical checks.
* Ingested documents must supply a valid `artifact_type`, which is resolved exactly (or via case-insensitive matching) to a canonical code.
* If a hierarchy combination (zone, section, artifact) is supplied, they must match the catalog definition exactly; otherwise, the request is rejected with HTTP 422.

### Traceability Implications
* Full traceability is achieved by tagging test cases with the `@req:` convention.
* Test executions automatically populate the Requirements Traceability Matrix (RTM) and GxP Qualification Reports.

## 5. Consequences & Trade-offs
* **Positive Impact:** Strong validation guarantees that all documents in the eTMF conform exactly to the official DIA reference model taxonomy.
* **Negative Impact / Technical Debt:** To support future major DIA TMF Reference Model versions (e.g., v4.0), a developer must code the new dictionary structure and register it in `tmf_reference_model`.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/core-models/tmf_reference_model/`, `apps/etmf/`
* **Verification Plan:** Verify using unit and integration tests covering explicit version selection, hierarchy resolution, invalid catalog combinations, and milestone alignment. Ensure that these are tracked by automated RTM and IQ/OQ/PQ scripts.
