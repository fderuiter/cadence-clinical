# ADR-097: Frontend Standardization, CSS Grid Layouts, and Centralized UI Utilities

* **Status:** Accepted
* **Date:** 2026-08-17
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
The Cadence Clinical platform consists of multiple web applications and portals, such as `apps/web` (clinical execution dashboard) and `apps/subject-portal` (patient-facing eCOA questionnaire platform). Over time, separate applications implemented parallel utility functions (such as cryptographic block hashing for 21 CFR Part 11 compliant audit ledgers and debounce wrappers for input validation) and redundant layout/style sheets (such as CSS Grid-based clinical form layouts). This divergence increases maintenance overhead, risks validation gaps under GxP audits, and compromises visual consistency across portals.

## 2. Decision Drivers & Constraints
* **Driver 1:** 21 CFR Part 11 and GxP compliance requires cryptographic consistency for signature and ledger block creation across all portals.
* **Driver 2:** Code reusability and reducing duplication to simplify system verification.
* **Driver 3:** Maintenance of uniform frontend design standards, particularly around grid-based clinical forms and debounced asynchronous field validations.

## 3. Options Considered
### Option 1: Fragmented/Bespoke Utility Implementations
* **Overview:** Retain bespoke helper functions and CSS files within each discrete app folder.
* **Pros:**
  * ✅ Allows rapid individual app modifications without cross-service review.
* **Cons:**
  * ❌ Severe code duplication across the repository.
  * ❌ Risk of cryptographic ledger divergence (different hash templates/schemas for patient portals vs. main platform).
  * ❌ High overhead for UI alignment.

### Option 2: Shared `ui` Package Standardization
* **Overview:** Move core layout primitives, ledger-block construction, and debounce utilities into the shared `packages/ui` library.
* **Pros:**
  * ✅ Single source of truth for cryptographic auditing (`buildLedgerBlock`) and input components (`createClinicalInput`).
  * ✅ Guarantees identical styling guidelines, removing redundant CSS declarations.
  * ✅ Drastically reduces lines of duplicate code, directly validated by automated scanners.
* **Cons:**
  * ❌ Requires coordination to avoid breaking changes in downstream consuming apps.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Shared standardization guarantees alignment with Gate 3 verification checks and ensures absolute cryptographic lock-step between patient and clinician portals.

## 5. Consequences & Trade-offs
* **Positive Impact:** Greater developer velocity, simplified pre-commit testing, and robust audit trail generation.
* **Negative Impact / Technical Debt:** Requires any future change to shared UI utilities to undergo rigorous cross-portal review.
* **Mitigation Strategy:** Enforced pre-commit hooks, rigorous Jest/Vitest unit tests, and CI/CD style/lint/duplication check gates.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`
* **Verification Plan:** Verify through `pnpm -r format`, `pnpm -r lint`, and the local ADR/duplication verification scripts (`scripts/validate_adrs.py` and `scripts/detect_duplication.py`).
