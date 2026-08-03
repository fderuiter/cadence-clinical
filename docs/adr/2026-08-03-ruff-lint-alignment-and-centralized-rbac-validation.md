# ADR-255: Ruff Lint Alignment and Centralized RBAC Validation

* **Status:** Accepted
* **Date:** 2026-08-03
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To support robust, secure, and maintainable software standards across the platform, the backend relies on automated static code analysis and linting checks. During a review of the centralized Role-Based Access Control matrix (`ROLE_PERMISSIONS` in `packages/security/rbac.py`), we identified redundant, duplicated dictionary keys for the Schedule of Activities (`"soa"`) capability configuration. Such duplications violate Ruff lint rule `F601` (multi-value-dict-key) and can lead to potential confusion and maintenance overhead.

To satisfy the system requirement **PRD-SYS-001** and ensure strict adherence to GxP static analysis quality gates, we need to consolidate all duplicate keys, enforce alphabetical ordering on module imports, and align the code with strict Ruff guidelines.

## 2. Decision Drivers & Constraints

* **Driver 1 (Quality Gate Compliance):** Ensure all Python modules successfully pass automated CI formatting and linting (Ruff checks).
* **Driver 2 (RBAC Transparency):** Keep permission matrices simple, clean, and free from redundant or conflict-prone entries.
* **Driver 3 (GxP Traceability):** Satisfy **PRD-SYS-001** for centralized user privilege validation and secure system execution.

## 3. Options Considered

### Option 1: Manually ignore the dictionary key duplication and import order violations using inline `# noqa` comments
* **Overview:** Disable specific linter rules on a line-by-line basis to allow duplicates to persist.
* **Pros:**
  * ✅ Leaves legacy code untouched.
* **Cons:**
  * ❌ Increases technical debt and ignores potential configuration bugs.
  * ❌ Violates strict GxP static check enforcements.

### Option 2: Clean up configuration, consolidate keys, and run global formatting alignment (Selected)
* **Overview:** Remove duplicate dictionary keys from `rbac.py` and run automatic import sorting and code format alignment.
* **Pros:**
  * ✅ Completely resolves Ruff rule `F601` violations.
  * ✅ Enhances clarity of role definitions in our security layer.
  * ✅ Satisfies all CI quality gate criteria.
* **Cons:**
  * ❌ Modifying central security files requires verification and architectural check approvals.

## 4. Decision Outcome

**Chosen Option:** Option 2. Consolidating the duplicate `"soa"` keys ensures consistent and safe permission evaluation while complying with GxP static verification guidelines under **PRD-SYS-001**.

## 5. Consequences & Trade-offs

* **Positive Impact:** Safer role evaluations and 100% clean linter status.
* **Negative Impact / Technical Debt:** Requires a new ADR to satisfy branch architectural validation constraints.
* **Mitigation Strategy:** Automated script enforcements in CI/CD prevent regressions.

## 6. Implementation & Verification

* **Affected Repositories / Services:** `packages/security/rbac.py`, `apps/designer/main.py`, `apps/designer/soa_models.py`, `tests/test_rbac.py`, `tests/test_sdv_item_level_rbac.py`.
* **Verification Plan:** Validated via `uv run ruff check .` and unit tests in `tests/test_rbac.py`.

