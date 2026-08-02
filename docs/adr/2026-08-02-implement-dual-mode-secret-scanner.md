# ADR-1847: Implement Dual-Mode Secret Scanner for Pre-commit and CI Validation

* **Status:** Accepted
* **Date:** 2026-08-02
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
Core application services are vulnerable to accidental credential exposure. To resolve this security gap without degrading developer velocity, we need a solution that operates both as a fast local pre-commit hook targeting only staged files, and as a comprehensive, blocking CI job that audits 100% of the codebase on every pull request.

## 2. Decision Drivers & Constraints
* **Driver 1:** Security & Compliance: Ensure hardcoded credentials never enter the codebase (FDA 21 CFR Part 11 / PRD-SYS-001).
* **Driver 2:** Developer Velocity: Pre-commit checks must run in under 2 seconds.
* **Driver 3:** Robustness: Ensure false positives are minimized by consistently excluding third-party, generated, or mock test assets.

## 3. Options Considered
### Option 1: One-Size-Fits-All Full Scan
* **Overview:** Always run a full recursive scan of the entire codebase for both local commits and CI pipelines.
* **Pros:**
  * ✅ Easy to implement and maintain.
* **Cons:**
  * ❌ Too slow for local git hooks, creating friction for developer velocity.

### Option 2: Dual-Mode Validation Utility (Selected)
* **Overview:** An optimized security script that scans targeted files passed as arguments during local pre-commit checks, but falls back to full recursive sweeps in CI.
* **Pros:**
  * ✅ Blazing fast for local commits (under 1-2 seconds).
  * ✅ Comprehensive and blocking during PR builds on GitHub.
  * ✅ Supports clean path normalization and standard exclusion paths (e.g., `.venv`, `node_modules`).
* **Cons:**
  * ❌ Higher maintenance overhead compared to a single-line command.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 meets both the speed constraint of local developer workflows and the strict safety gate requirement of the continuous integration pipeline, with robust bypass exclusions (`# pragma: allowlist`) to handle intentional mock configurations safely.

## 5. Consequences & Trade-offs
* **Positive Impact:** Codebase remains compliant and clean, preventing secrets leakage without delaying local workflow velocity.
* **Negative Impact / Technical Debt:** Added complexity in managing the dual execution modes.
* **Mitigation Strategy:** Automated tests ensure both the targeted scanning and path exclusions work reliably.

## 6. Implementation & Verification
* **Affected Repositories / Services:** All codebase directories, `.github/workflows/ci.yml`, and `.pre-commit-config.yaml`.
* **Verification Plan:** Verified via automated tests in `tests/test_compliance_security.py` and local/CI pipeline execution checks.
