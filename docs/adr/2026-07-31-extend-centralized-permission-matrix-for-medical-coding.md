# ADR-130: Extend Centralized Permission Matrix for Medical Coding

* **Status:** Accepted
* **Date:** 2026-07-31
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Medical dictionary lookup, coding assignment, and recoding impact analysis endpoints in clinical execution require explicit role-based access control (RBAC) governance. Prior to this change, the centralized `ROLE_PERMISSIONS` matrix in `packages/security/rbac.py` did not explicitly define resource capabilities for `medical_coding`.

Requirements: PRD-SYS-001

## 2. Decision Drivers & Constraints

* PRD-SYS-001 (21 CFR Part 11 & GxP Role Authorization Controls)
* Security principle of least privilege across clinical roles (sysadmin, sponsor_dm, terminology_manager, cra, investigator).

## 3. Options Considered

1. Option A (Selected): Extend `ROLE_PERMISSIONS` in `packages/security/rbac.py` with `medical_coding` resource permissions (`create`, `read`, `update`) for authorized roles (`sysadmin`, `sponsor_dm`, `terminology_manager`, `cra`).
2. Option B (Alternative): Ad-hoc inline role checks in FastAPI endpoint handlers.

## 4. Decision Outcome

Chosen option: Option A because extending `ROLE_PERMISSIONS` centralizes auditability and satisfies PRD-SYS-001 without fragmenting authorization rules.

## 5. Consequences & Trade-offs

* Positive: Centralized authorization matrix for all medical coding endpoints.
* Negative: Requires updating RBAC test suites for any new clinical role additions.

## 6. Implementation & Verification

* Modified `packages/security/rbac.py` to map `medical_coding` capabilities across sysadmin, sponsor_dm, terminology_manager, cra, and monitor roles.
* Added unit test `test_medical_coding_rbac_permissions()` in `tests/test_rbac.py`.
