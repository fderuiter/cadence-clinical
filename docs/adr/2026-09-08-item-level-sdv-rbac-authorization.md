# ADR-121: Item-Level SDV and Study-Design Reordering Authorization

* **Status:** Accepted
* **Date:** 2026-09-08
* **Authors:** @jules
* **Deciders:** @reviewer1, @reviewer2
* **References:** PRD-QRY-005, PRD-CRF-009

---

## 1. Context & Problem Statement
With the introduction of item-level Source Data Verification (SDV) flags and study-design reordering capabilities (such as reordering arms, epochs, visits, and procedures), the system needs clear, centralized permission boundaries and RBAC mapping. Specifically:
- Item-level SDV actions (flagging, resolving) require specialized permissions on execution assets.
- Study design reordering needs consistent role access so that unauthorized designers or external roles cannot mutate study version layouts.

## 2. Decision Drivers & Constraints
* **Driver 1:** Compliance and 21 CFR Part 11 auditing requirements.
* **Driver 2:** Security and prevention of unauthorized or accidental study design disruptions.
* **Driver 3:** Clean API contract validation alignment.

## 3. Options Considered
### Option 1: Handle checks on individual routers ad-hoc
* **Overview:** Check roles or permissions manually inside each new FastAPI route.
* **Pros:**
  * ✅ Quick to write initially.
* **Cons:**
  * ❌ Prone to omission, hard to verify in a centralized audit.

### Option 2: Integrate into Centralized RBAC Map & Explicit Route Dependency (Selected)
* **Overview:** Declare the new permissions ("sdv:flag", "study_design:reorder") in the centralized RBAC model (`packages/security/rbac.py`) and enforce them via FastAPI route dependencies.
* **Pros:**
  * ✅ Easy to audit and centralize.
  * ✅ Prevents bypasses by enforcing at the router layer.
  * ✅ Clean contract parity.
* **Cons:**
  * ❌ Requires updating the centralized security package, triggering architectural review.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Centralizing RBAC roles and permissions is the standard architectural pattern of Cadence Clinical, ensuring high compliance and easy auditability of 21 CFR Part 11 electronic records.

## 5. Consequences & Trade-offs
* **Positive Impact:** Clear permission gates on item-level SDV and study design mutations.
* **Negative Impact / Technical Debt:** Requires keeping `rbac.py` in sync when new actions are introduced.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/security/rbac.py`, `apps/designer/main.py`, `apps/execution/routers/sdv.py`
* **Verification Plan:** Verified via `test_api_contract_validation.py` and `test_sdv_item_level_rbac.py` test cases.
