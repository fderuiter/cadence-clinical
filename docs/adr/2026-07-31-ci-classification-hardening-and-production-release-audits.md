# ADR-130: CI/CD Classification Hardening and Production Release Security Audits

* **Status:** Accepted
* **Date:** 2026-07-31
* **Authors:** @google-labs-jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
Currently, developers can self-approve pull requests that modify the vulnerability exclusions ledger. This occurs because changes to the ledger are incorrectly classified as safe documentation updates, which bypass manual review and trigger an automatic merge. This loophole creates a significant security risk and threatens our GxP compliance. Furthermore, the production pipeline lacks an automated security audit gate, allowing unverified dependencies to deploy directly to production. This decision implements targeted classification hardening and gates production releases based on security audit verification, satisfying requirement PRD-SYS-001.

## 2. Decision Drivers & Constraints
* Security & GxP Compliance (PRD-SYS-001)
* Automation and Developer Velocity
* Self-Approval Bypass Mitigation

## 3. Options Considered
### Option 1: Manual review for all changes
* **Overview:** Require manual review on every PR regardless of directory or content.
* **Pros:**
  * ✅ High security confidence.
* **Cons:**
  * ❌ Severe negative impact on developer velocity and automation.

### Option 2: Hardened CI Classification and Production Release Audits (Selected)
* **Overview:** Harden CI classification logic to flag ledger JSON and validation script modifications as unsafe, and integrate `validate_vulnerabilities.py` into the production release workflow as a strict gate.
* **Pros:**
  * ✅ Minimizes friction for standard markdown documentation.
  * ✅ Specifically blocks self-approval on vulnerability list changes.
  * ✅ Guarantees no unapproved vulnerabilities reach production.
* **Cons:**
  * ❌ Slight increase in CI pipeline complexity.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Chosen because it specifically addresses the self-approval vulnerability bypass without introducing friction for standard documentation or pre-commit hooks, aligning with the compliance requirements of PRD-SYS-001.

## 5. Consequences & Trade-offs
* **Positive Impact:** Safer PR merges and reliable release gates.
* **Negative Impact / Technical Debt:** Requires maintenance of the CI files classification list.
* **Mitigation Strategy:** Covered by robust unit tests (`tests/test_ci_classification.py`).

## 6. Implementation & Verification
* **Affected Repositories / Services:** CI workflow configurations (`.github/workflows/`), security validation scripts.
* **Verification Plan:** Validated via automated test suite running `pytest` and verified by checking classification on custom PR inputs.
