# ADR-185: Dynamic Dependency Matching and Clinical De-identification Consolidation

- **Status:** Accepted
- **Date:** 2026-08-10
- **Authors:** @jules
- **Deciders:** @engineering-lead, @compliance-officer
- **Requirement Reference:** PRD-SYS-001

---

## 1. Context & Problem Statement

To maintain strict GxP boundary verification and system integrity across shared python packages, we must ensure all package-to-package import statements (e.g., `packages.deid` importing from `packages.security`) are explicitly declared as local dependencies within their respective package `pyproject.toml` manifests.

Additionally, we must consolidate all clinical text scrubbing and named entity recognition capabilities (such as `PHINameEntityScrubber`) and its associated unit tests under a single dedicated clinical de-identification package (`packages/deid`). Core security package files should contain no references to clinical de-identification or entity scrubbing utilities.

This decision outlines how we enforce these modular boundaries statically in our CI/CD pipelines and developer workspaces.

## 2. Decision Drivers & Constraints

- **Driver 1:** Enforce strict dependency isolation across workspace packages statically in under 5 seconds (PRD-SYS-001).
- **Driver 2:** Consolidate all clinical text scrubbing and named entity recognition capabilities inside the de-identification package (`packages/deid`) to keep core security packages lightweight.
- **Driver 3:** Ensure the clinical de-identification package initializes cleanly without requiring secrets or parameters from other services (such as `AUDIT_LOG_SECRET_KEY` or `INBOUND_EMAIL_HMAC_SECRET`).

## 3. Options Considered

### Option 1: Manual review of dependencies and keeping clinical scrubbing in security

- **Overview:** Developers manually maintain `pyproject.toml` files and perform peer review on cross-package imports. Clinical text scrubbing remains coupled inside `packages/security`.
- **Pros:**
  - ✅ No tooling overhead.
- **Cons:**
  - ❌ Fragmented boundaries, human error in review, and runtime environment secret requirement leakage to lightweight utilities.

### Option 2: Automated AST validation with package-to-package manifest alignment

- **Overview:** Consolidate all clinical de-identification and text scrubbing utilities into `packages/deid` and extend `/app/scripts/validate_imports.py` to statically verify all workspace package-to-package imports against local package `pyproject.toml` configurations.
- **Pros:**
  - ✅ High reliability and robust compliance with PRD-SYS-001.
  - ✅ Fast, static check with zero runtime execution or side effects.
  - ✅ Eradicates clinical/security coupled logic.
- **Cons:**
  - ❌ Requires parsing and caching manifest files during the validation script execution.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Implementing Option 2 resolves coupled logic boundaries completely, enforces modular package dependency declarations statically, and prevents future architectural drift without introducing runtime complexity or dependencies.

## 5. Consequences & Trade-offs

- **Positive Impact:** Completely clean separation of clinical de-identification logic from core security logic, zero secret leakage during lightweight deid module imports, and a fast blocking gate in pre-commit and CI to prevent undeclared dependencies.
- **Negative Impact / Technical Debt:** Added dependency cache and parsing code in the python validation script.
- **Mitigation Strategy:** Cached `pyproject.toml` parsed results to keep the execution time well under the 5-second constraint (running in < 0.1 seconds).

## 6. Implementation & Verification

- **Affected Repositories / Services:** All shared workspace packages under `packages/` (specifically `deid`, `security`, `database`, `compliance`), the AST import validator script (`scripts/validate_imports.py`), and corresponding tests.
- **Verification Plan:** Verified using pytest unit tests on the validator, as well as executing the validation script over the codebase to verify zero compliance/boundary errors.
