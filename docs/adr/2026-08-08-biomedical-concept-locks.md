# ADR-065: Biomedical Concept Locks for Active-Recruiting Studies

* **Status:** Accepted
* **Date:** 2026-08-08
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
Under regulatory compliance and GxP standards (PRD-MDR-002), a Biomedical Concept that is currently referenced by an active, recruiting clinical trial protocol (defined as study status `Active-Recruiting`) must be locked against direct modification, renaming, or deletion. This prevents accidental clinical metadata alterations during ongoing execution. Any required modifications should be directed to the protocol amendment workflow.

## 2. Decision Drivers & Constraints
* **Compliance:** 21 CFR Part 11 and GCP guidelines require complete audit trails and locked structures for active studies.
* **Frictionless Integration:** Allow unreferenced concepts to remain fully editable.
* **Stable API Contracts:** Return a structured `409 Conflict` response with error code `CONCEPT_LOCKED_ACTIVE_STUDY` when locked.

## 3. Options Considered
### Option 1: Lock All Concepts Globally
* **Overview:** Once a study starts, freeze the entire Metadata Repository.
* **Pros:**
  * ✅ Very easy to implement.
* **Cons:**
  * ❌ Blocks trial designers from editing concepts belonging to other draft studies.

### Option 2: Active-Recruiting Reference-Based Locks
* **Overview:** Dynamically scan if a concept is referenced by any study with status `Active-Recruiting` across in-memory data structures and graph databases, blocking direct update/rename/delete mutations.
* **Pros:**
  * ✅ Highly precise; only locks referenced concepts on active-recruiting trials.
  * ✅ Preserves full design velocity for all draft and non-recruiting trials.
* **Cons:**
  * ❌ Requires recursive dictionary traversing for in-memory mocks.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 offers the perfect balance between regulatory compliance/guaranteed trial integrity and design team agility.

## 5. Consequences & Trade-offs
* **Positive Impact:** No accidental concept mutations can break active studies.
* **Negative Impact / Technical Debt:** Small run-time lookup overhead when mutating concepts.
* **Mitigation Strategy:** Fallback to in-memory traversal and fast-path indexing.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/designer/`
* **Verification Plan:** Verified using test cases in `tests/test_concept_locks.py`.
