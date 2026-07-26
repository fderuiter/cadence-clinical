# ADR-062: Global Library Object Instantiation in Clinical Studies

* **Status:** Accepted
* **Date:** 2026-08-09
* **Authors:** @jules
* **Deciders:** @lead_architect, @gxp_compliance_officer

---

## 1. Context & Problem Statement
The platform manages global library metadata templates (such as reusable Forms, Arms, Visits, and Data Elements) under a tenant/sponsor scope. To support rapid trial design and standardized metadata layouts, clinical studies need a mechanism to instantiate a specific version of a Global Library object as a distinct study-scoped copy.

Crucially, this copy must remain linked to its source for provenance and traceability, while ensuring the original source object remains unmodified. Standard GCP/GxP boundaries require strict tenant/sponsor isolation: a study or library object owned by Sponsor A must not be accessible or instantiable by Sponsor B.

## 2. Decision Drivers & Constraints
* **Compliance & Traceability (21 CFR Part 11 & GxP):** Every instantiation must maintain a clear, durable trace link to the exact source library object ID and version used.
* **Sponsor Isolation & Security:** Access and mutation controls must strictly prevent cross-sponsor authorization leaks.
* **Immutability of Source Templates:** The original Global Library object version must remain completely unmodified and locked during and after instantiation.
* **Database Agnosticity:** The instantiation mechanism must seamlessly support both the Neo4j graph database environment and an in-memory mock fallback system for offline testing.

## 3. Options Considered
### Option 1: Direct Reference to Global Library Nodes
Instead of copying, study version nodes directly reference global library nodes via shared relationships.
* **Pros:**
  * ✅ Reduces node duplication in the database.
* **Cons:**
  * ❌ Violates GxP study-level version freeze constraints. If a study is amended or frozen, modifying the shared global template could retroactively alter the historical metadata definition of a study.
  * ❌ Fails to allow localized study-specific overrides/extensions to the instantiated template.

### Option 2: Copy-on-Instantiation with Trace Linkage (Selected)
When a study designer instantiates a library template, the platform clones the template's properties and payload into a distinct, study-scoped copy, and records an `INSTANTIATED_FROM` relationship pointing to the source library version node with tracing metadata (source ID, version, sponsor, and timestamp).
* **Pros:**
  * ✅ Strictly preserves study-level immutability and GCP version freezing.
  * ✅ Allows study-specific changes to the copy without affecting the global template or other studies.
  * ✅ Provides perfect audit traceability back to the source library metadata version.
* **Cons:**
  * ❌ Moderately increases the number of nodes in the graph database.

---

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 aligns perfectly with GxP study versioning standards and prevents template mutation side effects, while establishing durable trace linkages.

---

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * Independent study design progression.
  * Uncompromised tenant isolation boundaries.
  * Durable provenance links preserved automatically.
* **Negative Impact:**
  * Requires explicit deep-copy logic for template payloads.
* **Mitigation Strategy:** Payloads are serialized/deserialized cleanly via JSON (`payload_json`) when writing to Neo4j.

---

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  - `apps/designer/delta.py` (added `instantiate_library_object_in_study`, `check_library_object_exists_any_sponsor`, and `check_study_exists_any_sponsor`)
  - `apps/designer/main.py` (added `POST /api/v1/studies/{study_id}/library-instances` and Pydantic schemas)
* **Verification Plan:**
  - Unit/integration tests added in `tests/test_global_library_api.py` validating successful instantiation, explicit version selection, and correct rejection of cross-sponsor/unauthorized requests.
