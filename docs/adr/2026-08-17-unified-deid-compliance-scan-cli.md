# ADR-097: Unified De-identification Compliance Scan CLI, Pre-commit Hook, and Blocking CI Gate

* Status: Accepted
* Date: 2026-08-17
* Authors: @jules
* Deciders: @fderuiter
* Requirement Reference: PRD-TMF-005

---

## 1. Context & Problem Statement
To ensure GxP and HIPAA compliance, clinical trial platforms must strictly prevent any accidental leakage of Personally Identifiable Information (PII) or Protected Health Information (PHI) in code, test datasets, or documentation committed to version control. Previously, there was no centralized, automated method for scanning files before they were pushed to the remote repository. This increased the risk of inadvertent compliance violations and manual auditing overhead.

## 2. Decision Drivers & Constraints
* **Compliance & Audit Readiness:** Ensure zero leakage of sensitive data (HIPAA / GDPR / GxP).
* **Developer Ergonomics:** Minimize overhead with automated local and pre-commit detection.
* **Continuous Integration Integration:** Block non-compliant code from merging by running identical checks on every PR inside a CI/CD job.
* **Extensibility:** Support custom compliance profiles, regex patterns, and easy updates.

## 3. Options Considered
### Option 1: Manual Review and Code Audits
* **Overview:** Rely on peer reviews and periodic compliance team audits.
* **Pros:**
  * ✅ No technical configuration or maintenance required.
* **Cons:**
  * ❌ Human error is common, and accidental leaks can easily bypass manual inspections.
  * ❌ High audit overhead and late detection of leaks.

### Option 2: Decentralized Package Scanners
* **Overview:** Install standard global PII scanners (like git-secrets or similar) on each individual engineer's machine.
* **Pros:**
  * ✅ Leverages existing tooling.
* **Cons:**
  * ❌ Hard to distribute, maintain, and configure uniformly across all developer environments.
  * ❌ Doesn't integrate natively with our Python/Pydantic-based clinical domain patterns.

### Option 3: Built-in Python-based De-identification Scanner CLI and Hook (Selected)
* **Overview:** Implement a custom package `packages/deid` containing PII/PHI detection rules based on compliance profiles (e.g. HIPAA) with a unified CLI, integrated into local pre-commit hooks and blocking CI jobs.
* **Pros:**
  * ✅ Fully integrated with our existing clinical platform models and Pydantic rules.
  * ✅ Enforces the exact same compliance scan locally via pre-commit and remotely via CI.
  * ✅ Easily configurable via project structures and custom profiles.
* **Cons:**
  * ❌ Requires code maintenance for regex patterns and detectors.

## 4. Decision Outcome
* **Chosen Option:** Option 3
* **Justification:** Implementing a dedicated `packages/deid` utility provides consistent, automated, and deterministic de-identification checks both pre-commit and pre-merge. Integrating this directly as a pre-commit hook and CI gate ensures robust HIPAA compliance without degrading developer velocity.

## 5. Consequences & Trade-offs
* **Positive Impact:** Automatic detection of PII/PHI (e.g., SSN, emails, IPs, ZIP codes) happens instantly prior to commit. The same engine is run in the CI pipelines to block merges of unsafe PRs.
* **Negative Impact / Technical Debt:** Requires adding new patterns and maintaining the `packages/deid` codebase as standards evolve.
* **Mitigation Strategy:** Provide clear exclusion markers and documentation to help developers handle false positives without bypassing core compliance rules.

## 6. Implementation & Verification
* **Affected Repositories / Services:** Added `packages/deid`, configured `.pre-commit-config.yaml`, updated `pyproject.toml`, and integrated the blocking check within `.github/workflows/ci.yml`.
* **Verification Plan:** Verified via local unit and CLI integration tests in `tests/test_deid.py`, running `uv run python -m packages.deid.cli` across the workspace.
