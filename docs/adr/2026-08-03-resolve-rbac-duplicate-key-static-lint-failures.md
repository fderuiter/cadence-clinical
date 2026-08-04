# ADR-135: Resolve RBAC Duplicate Key Static Lint Failures

* **Status:** Accepted
* **Date:** 2026-08-03
* **Authors:** @jules
* **Deciders:** @jules

---

## 1. Context & Problem Statement
During standard linting and formatting quality checks (specifically when running Ruff on Python 3.14 environments), the security module failed with duplicate dictionary key errors (Ruff F601) in `packages/security/rbac.py`. Specifically, the duplicate key `"soa"` was defined multiple times in `ROLE_PERMISSIONS` dictionary structures. This block of code must be cleaned up and deduplicated to satisfy the static analysis gate and prevent potential runtime permission evaluation bugs under (PRD-CRF-009).

## 2. Decision Drivers & Constraints
* **Driver 1:** 100% compliance with static analysis (Ruff) quality gates.
* **Driver 2:** Correctness of security roles/permissions (RBAC) in clinical modules as mandated by (PRD-CRF-009).

## 3. Options Considered
### Option 1: Ignore the Ruff lint rule (no-qa / skip)
* **Overview:** Add local disable comments to ignore the duplicate key warnings.
* **Pros:**
  * ✅ Quickest to implement.
* **Cons:**
  * ❌ Leaves duplicate keys in the codebase, leading to confusion and potential hidden bugs.
  * ❌ Fails strict static analysis standards.

### Option 2: Clean up and deduplicate key definitions [Selected]
* **Overview:** Resolve the duplicate `"soa"` keys in `rbac.py` by merging permissions correctly.
* **Pros:**
  * ✅ Clear, readable code without duplicate entries.
  * ✅ 100% compliance with static analysis.
* **Cons:**
  * ❌ Requires editing security-sensitive files.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Choosing Option 2 guarantees logical consistency of the security policy, eliminates warnings cleanly without masking potential issues, and fully aligns with role-based authorization requirements defined in (PRD-CRF-009).

## 5. Consequences & Trade-offs
* **Positive Impact:** Cleaner codebase, passing quality gates, and robust security posture under (PRD-CRF-009).
* **Negative Impact / Technical Debt:** None.
* **Mitigation Strategy:** Code formatting/evaluation tests to cover role permissions mapping.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/security`
* **Verification Plan:** Validated via `uv run ruff check packages/security/rbac.py` and running RBAC tests.
