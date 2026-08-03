# ADR-252: Style and Lint Enforcements for Security RBAC

* **Status:** Accepted
* **Date:** 2026-08-03
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
During standard CI/CD linting verification of the `packages/security/rbac.py` module, duplicate dictionary keys (specifically `"soa"`) were detected under rule F601. This caused static code analysis and linting checks to fail, necessitating a cleanup of the duplicate dictionary keys in the central authorization/RBAC system.

Requirements: PRD-SYS-001

## 2. Decision Drivers & Constraints
* **PRD-SYS-001 (21 CFR Part 11 & GxP Role Authorization Controls):** Ensure clean, predictable, and error-free RBAC configuration without redundant or duplicate mappings.
* **Continuous Integration Stability:** Resolve any static analysis or format errors under ruff and ESLint globally.

## 3. Options Considered
### Option 1: Ignore or Suppress Linting Checks
* **Overview:** Add local `# noqa` suppressions to bypass duplicate key detection.
* **Pros:**
  * ✅ Requires no changes to the dictionary.
* **Cons:**
  * ❌ Leaves duplicate configurations intact which can lead to runtime confusion, debugging difficulties, and potential GxP validation concerns.

### Option 2: Clean and Reformat Duplicate Keys [Selected]
* **Overview:** Safely remove the duplicated `"soa"` keys from the RBAC nested dictionary and run formatting to standardise the module.
* **Pros:**
  * ✅ Eliminates redundancy and potential dictionary lookup bugs.
  * ✅ Restores fully green CI/CD build status.
  * ✅ Aligns perfectly with GxP and GAMP-5 engineering best practices.
* **Cons:**
  * ❌ None.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 completely resolves the duplicate key warnings, ensuring configuration clarity and compliance with the GxP-aligned CI lint gates.

## 5. Consequences & Trade-offs
* **Positive Impact:** Cleaner, standard-compliant nested dictionary definitions in our core security package.
* **Negative Impact / Technical Debt:** None.
* **Mitigation Strategy:** Enforced dynamically via local ruff style checks in automated CI pre-commit checks.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/security/rbac.py`
* **Verification Plan:** Verified via `uv run ruff check .` and unit tests in `tests/test_rbac.py`.
