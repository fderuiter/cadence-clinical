# ADR-054: Clinical Query Workflow and Role Authorization

* **Status:** Accepted
* **Date:** 2026-07-24
* **Authors:** @google-labs-jules[bot]
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
To meet security and regulatory compliance requirements for clinical trials (e.g., FDA 21 CFR Part 11, EU Annex 11), the eClinical platform must implement a highly secure, reliable, and auditable manually-raised clinical query workflow.
The core issues addressed by this decision are:
- Authorizing actions cleanly based on roles propagated from Keycloak tokens.
- Handling potential role claim variances (case sensitivity, whitespace, synonym mapping) safely.
- Centralizing query lifecycle validation to prevent invalid state transitions or data corruption.
- Correctly capturing GxP audit metadata utilizing a session commit-then-reselect pattern.

## 2. Decision Drivers & Constraints
* **Driver 1 (Compliance):** Enforce allow-list-based role gates (e.g., CRAs/Data Managers can raise/close, Site Investigators can answer).
* **Driver 2 (Reliability):** Guard against token claim mismatches across tenant directories.
* **Driver 3 (Traceability):** Ensure all mutations are persisted with robust 21 CFR Part 11 audit fields.

## 3. Options Considered
### Option 1: Ad-hoc Role Gate checks in Endpoint Handlers
* **Overview:** Validate user roles directly inside each endpoint function using local conditions.
* **Pros:**
  * ✅ Quick to implement initially.
* **Cons:**
  * ❌ Easy to miss security gates on new/cloned endpoints.
  * ❌ No role normalization logic, leading to subtle access issues due to claim formatting discrepancies.

### Option 2: Declarative Allow-List Dependency & Centralized Lifecycle Engine
* **Overview:** Implement a generic, reusable FastAPI dependency `require_roles(...)` alongside a dedicated query state transition machine (`QueryService`).
* **Pros:**
  * ✅ Secure-by-default architecture utilizing allow-lists.
  * ✅ Centralized role normalization (normalizes case, trims whitespace, maps synonym roles).
  * ✅ Guarantees state transition validation and transactional consistency across all entrypoints.
* **Cons:**
  * ❌ Introduces a small amount of abstraction and upfront boilerplate.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 provides a standard, secure-by-default framework for eClinical action-level RBAC. Implementing centralized role normalization prevents environment-specific discrepancies from bypassing or breaking authentication gates.

## 5. Consequences & Trade-offs
* **Positive Impact:** Highly robust security verification, completely deterministic query workflows, and clean GxP audit trails.
* **Negative Impact / Technical Debt:** Requires keeping role synonym mappings up-to-date if external OIDC claims change drastically.
* **Mitigation Strategy:** Role configurations are kept as explicit constants inside the security package for clear visibility and maintenance.

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  * `packages/security/` - Shared allow-list validator and normalization patterns.
  * `apps/execution/` - Core clinical query lifecycle service, schema binding, and endpoints.
* **Verification Plan:** Full automated coverage in `tests/test_clinical_queries.py` and `tests/test_rbac.py`.
