# ADR-251: Extend Centralized Permission Matrix for eCOA Diary Alert Actions

* **Status:** Accepted
* **Date:** 2026-09-07
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
Clinical scan and push-alert dispatch routines need to evaluate and trigger alerts on `ecoa_diary` records. To authorize these scheduled tasks and API operations, the centralized role-based access control (RBAC) permission matrix must grant the `"alert"` action on `"ecoa_diary"` to relevant administrative and clinical investigator roles, while strictly denying it from routine subjects (who only need read-only access to their diaries).

Requirements: PRD-SYS-001

## 2. Decision Drivers & Constraints
* **PRD-SYS-001 (21 CFR Part 11 & GxP Role Authorization Controls):** Ensure roles can only execute authorized operations.
* **Least-Privilege Enforcement:** Subjects must not be allowed to invoke alert dispatching actions on eCOA diaries, while administrative and investigative staff must be fully authorized.

## 3. Options Considered
### Option 1: Inline Endpoint Gating
* **Overview:** Check for alert capabilities dynamically in API or background task handlers.
* **Pros:**
  * ✅ Quick to implement inside individual routes.
* **Cons:**
  * ❌ Fragments role validation and bypasses the centralized RBAC design pattern, violating PRD-SYS-001 centralization guidelines.

### Option 2: Centralized RBAC Matrix Extension [Selected]
* **Overview:** Add `"alert"` action to `"ecoa_diary"` resource permissions across `ROLE_SYSADMIN`, `ROLE_SPONSOR_DM`, `ROLE_INVESTIGATOR`, `ROLE_CRC`, `ROLE_CRA_CANONICAL`, `"monitor"`, `"admin"`, and `"system"` roles directly in `packages/security/rbac.py`.
* **Pros:**
  * ✅ Preserves centralized, auditable, and unified role-to-permission mapping.
  * ✅ Automatically propagates to derived roles (`ROLE_PRINCIPAL_INVESTIGATOR`, etc.) before the base class instantiation block.
* **Cons:**
  * ❌ Slightly increases the static footprint of the role permissions dictionary.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Centralizing permissions in `packages/security/rbac.py` provides absolute compliance alignment with GxP/Part 11 security audit requirements, avoids code duplication, and ensures clean derivative role inheritance.

## 5. Consequences & Trade-offs
* **Positive Impact:** Staff and investigator personas can safely monitor and execute push alert triggers on diary submissions.
* **Negative Impact / Technical Debt:** Future role integrations must carefully review resource matrix boundaries.
* **Mitigation Strategy:** Covered by strict automated regression test suites verifying positive and negative scoping.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/security/rbac.py`
* **Verification Plan:** Verified locally via `tests/test_rbac.py` regression tests.
