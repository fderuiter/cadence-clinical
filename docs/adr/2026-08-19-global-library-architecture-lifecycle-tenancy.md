# ADR-099: Global Library Architecture, Lifecycle, and Multi-Tenant Isolation

* **Status:** Accepted
* **Date:** 2026-08-19
* **Authors:** @jules
* **Deciders:** @lead_architect, @gxp_compliance_officer

---

## 1. Context & Problem Statement
The platform provides clinical study metadata design tools in the Metadata Designer (MDR/SDR) service (`apps/designer`). To optimize standardizing and authoring clinical protocols across trials, sponsors need to reuse common, high-quality building blocks like Forms, Data Elements, Arms, and Visits.

These reusable template blocks must reside within a centralized **Global Library**. To adhere to GxP data integrity rules, clinical design safety standards, and multi-tenant security requirements:
1. Every Global Library template must follow a rigid status-driven state machine (governance).
2. Direct edits on published/archived versions must be strictly blocked (immutability).
3. The schema and underlying persistence layer must preserve comprehensive provenance (version history) using graph-native pathways.
4. Tenant separation must strictly prevent any cross-sponsor metadata leaks or unauthorized execution.
5. In-use library objects cannot be directly overwritten but can be formally amended.
6. Target studies must be able to instantiate library templates, keeping the template immutable while tracking source pedigree.

This decision directly implements requirements under **Trace-3**.

## 2. Decision Drivers & Constraints
* **21 CFR Part 11 & GxP Compliance**: Maintain complete chronological audit trails of all library object updates and lifecycle status transitions, including mandatory change reasons and transition details (who, when, what, why).
* **Graph Database Modeling**: Seamlessly leverage a Neo4j graph model (and an in-memory mock fallback) for tracking stable entity roots and linear version histories via `PREVIOUS_VERSION` relationships.
* **Tenant Isolation**: Secure the platform against cross-sponsor data disclosure or template mutation, particularly when parsing custom client headers.
* **Non-Destructive Amendments**: Support clinical protocol progress by allowing in-use template modifications only via explicit cloning/amendment pipelines.

## 3. Options Considered
### Option 1: Direct Mutations on Library Objects
* **Overview**: Permit immediate overwrites of existing templates inside a single relational database.
* **Pros**:
  * Simple schema with fewer records to maintain.
* **Cons**:
  * ❌ Violates GxP reproducibility and 21 CFR Part 11 electronic records tracing. If a trial instantiates a Form, and the Form is directly modified, historical protocols become unreproducible.
  * ❌ No governance controls.

### Option 2: Append-Only Graph-Native Versioning & Scoped Copy-on-Instantiation (Selected)
* **Overview**: Define Global Library objects with a composite primary key / stable identifier concept in Neo4j. Modifying an object inserts a new version node linked via `PREVIOUS_VERSION` to the predecessor. Statuses transition through a strict state machine validated at runtime, and in-use objects are amended via an explicit `/amend` endpoint to clone them into successor draft versions.
* **Pros**:
  * ✅ Full auditability of all historical changes.
  * ✅ High isolation between templates and their study-specific instantiations.
  * ✅ Adheres fully to GxP and 21 CFR Part 11 requirements.
* **Cons**:
  * Slightly higher database node footprint, mitigated by lightweight properties and JSON string serialization.

---

## 4. Decision Outcome
* **Chosen Option**: Option 2.
* **Justification**: This aligns with the overall Cadence Clinical platform's GxP and multi-tenant baseline, ensuring total isolation, perfect traceability, and strict immutability.

---

## 5. Consequences & Trade-offs
* **Positive Impact**:
  * Uncompromising security isolation between competing pharmaceutical sponsors.
  * High-fidelity clinical protocol definitions that are immune to retroactive side-effects.
  * Automatic generation of compliant GxP audit histories.
* **Negative Impact**:
  * Creating a new node for every minor template iteration increases Neo4j graph complexity.
* **Mitigations**:
  * Implement cursor-based pagination and index on `(id, sponsor_id)` in the Neo4j schema to keep query response times flat under extensive load.

---

## 6. Implementation & Verification
* **Affected Components**:
  - `apps/designer/main.py`: Express endpoints for library CRUD, list, amend, transition, and instantiation.
  - `apps/designer/library.py`: Data models, schemas, status enum, and allowed transition matrix.
  - `apps/designer/delta.py`: Logic implementing write lock checks, versioning chains, in-use checks, and cloning.
* **Verification**:
  - Verified via robust test suite under `tests/test_global_library_api.py`.
  - Continuous integration pipeline runs validation scripts `python3 scripts/validate_adrs.py` to ensure all structural format rules are correctly parsed.
