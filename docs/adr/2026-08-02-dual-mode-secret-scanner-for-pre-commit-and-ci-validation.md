# ADR-251: Dual-Mode Secret Scanner for Pre-Commit and CI Validation

- **Status:** Accepted
- **Date:** 2026-08-02
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To protect the clinical trial platform's code integrity and ensure compliance with security and privacy requirements (including PRD-SYS-001), we must prevent secret keys, credentials, and high-risk sensitive configurations from being accidentally committed to the source repository.

A standard security check must be integrated into both local developer environments and our continuous integration (CI) pipelines. However, running a full codebase secrets check can slow down local Git commits, reducing developer velocity. Conversely, scanning only changed files in CI can leave older/unmodified files vulnerable to missed leaks if hooks were bypassed. We require a dual-mode scanning solution that behaves as a lightning-fast staged-file scan locally and as a comprehensive recursive-sweep fallback in CI.

## 2. Decision Drivers & Constraints

- **Compliance:** Prevent exposure of credentials and GxP configuration keys, satisfying the secure systems and audit standard defined in PRD-SYS-001.
- **Developer Velocity:** Staged-file scans in local pre-commit hooks must complete in under 2 seconds.
- **Continuous Integration:** CI pipelines must run a comprehensive, blocking recursive-sweep scan of the entire repository to guarantee zero credential leakage.
- **Exclusion Optimization:** The utility must reliably ignore false positives (e.g., test mocks, vendor files) via consistent exclusions.

## 3. Options Considered

### Option 1: Separate Local and CI Scan Utilities

- **Overview:** Maintain different scanning tools or scripts for pre-commit checks and CI environments.
- **Pros:**
  - ✅ Allows fine-tuned local scripts.
- **Cons:**
  - ❌ Increases maintenance overhead across different environments.
  - ❌ Discrepancies between local scans and CI checks create developer friction.

### Option 2: Unified Dual-Mode Script (Selected)

- **Overview:** Enhance the main security auditing script (`scripts/audit_security.py`) to handle both modes. Locally, pre-commit passes specific changed file paths to run a targeted sweep. In CI, with no positional arguments, it defaults to a full recursive walk of the repository while consistently respecting path exclusions.
- **Pros:**
  - ✅ Single source of truth for both local hooks and CI pipelines.
  - ✅ Ensures perfect parity in scanner logic, exclusion rules, and bypass comments (`# pragma: allowlist`).
  - ✅ Meets both developer velocity requirements and CI blocking sweep guarantees.
- **Cons:**
  - ❌ Marginal complexity in script parameters to handle CLI positional arguments.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Implementing a single dual-mode scanning script guarantees consistent, reliable, and high-performance credential enforcement across both developer workspaces and pipelines, fully satisfying compliance with PRD-SYS-001.

## 5. Consequences & Trade-offs

- **Positive Impact:** Staged-file local pre-commit scans complete instantly. CI pipelines run a guaranteed recursive check on 100% of the repository.
- **Negative Impact / Technical Debt:** Requires careful configuration of local hooks to pass file lists correctly and maintain ignore list consistency.
- **Mitigation Strategy:** Solidified with robust integration tests verifying targeted sweeps, recursive fallbacks, and exclusion behaviors.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `scripts/audit_security.py`, `.pre-commit-config.yaml`, `.github/workflows/ci.yml`, `tests/test_compliance_security.py`
- **Verification Plan:** Verified locally and in CI with unit/integration tests running under pytest (`tests/test_compliance_security.py`).
