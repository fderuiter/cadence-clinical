# ADR-250: Extend Centralized Permission Matrix for eCOA and ePRO

- **Status:** Accepted
- **Date:** 2026-09-05
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Clinical eCOA (electronic Clinical Outcome Assessment) and ePRO (electronic Patient-Reported Outcome) schedules, diaries, and submissions require granular, role-based access control (RBAC) governance. Prior to this change, the centralized `ROLE_PERMISSIONS` matrix in `packages/security/rbac.py` did not explicitly define resource capabilities for `ecoa_schedule`, `ecoa_diary`, and `ecoa_submission`.

Requirements: PRD-SYS-001

## 2. Decision Drivers & Constraints

- PRD-SYS-001 (21 CFR Part 11 & GxP Role Authorization Controls)
- Least-privilege access: Subject role must not be allowed to create schedules or diaries, while clinical/staff roles (sysadmin, sponsor_dm, investigator, crc, cra) must be permitted to manage/review them.

## 3. Options Considered

1. Option A (Selected): Extend `ROLE_PERMISSIONS` in `packages/security/rbac.py` with `ecoa_schedule`, `ecoa_diary`, and `ecoa_submission` resource permissions (`create`, `read`) for authorized staff roles, and restrict Subject to `read` on schedules/diaries and `create`, `read` on submissions.
2. Option B (Alternative): Ad-hoc inline role checks in FastAPI endpoint handlers.

## 4. Decision Outcome

Chosen option: Option A because extending `ROLE_PERMISSIONS` centralizes auditability and satisfies PRD-SYS-001 without fragmenting authorization rules.

## 5. Consequences & Trade-offs

- Positive: Centralized, auditable permission map for all eCOA and ePRO operations.
- Negative: Matrix needs to be maintained if new roles are introduced.

## 6. Implementation & Verification

- Modified `packages/security/rbac.py` to map `ecoa_schedule`, `ecoa_diary`, and `ecoa_submission` capabilities across staff roles and subject roles.
- Added comprehensive automated verification tests in `tests/test_interop.py` and `tests/test_ecoa_coverage.py`.
