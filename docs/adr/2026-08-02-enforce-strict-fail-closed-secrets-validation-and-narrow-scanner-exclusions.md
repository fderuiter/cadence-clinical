# ADR-[NUMBER]: Enforce Strict Fail-Closed Secrets Validation and Narrow Scanner Exclusions

* **Status:** Accepted
* **Date:** 2026-08-02
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
The Cadence Clinical platform requires absolute security for environment configurations, cryptographic operations, and digital signatures to maintain strict GxP and FDA 21 CFR Part 11 compliance. Previously, the static security scanner bypassed any lines of code containing environment variable lookups, introducing a critical blind spot for fallback credentials.

This decision implements requirements under PRD-SYS-001.

## 2. Decision Drivers & Constraints
* **Driver 1:** Security & GxP Compliance — Eliminate global scanner bypasses for credentials/secrets fallback lookups.
* **Driver 2:** Fail-Closed Architecture — Block API Gateway startup and cryptographic modules instantly if vital environment secrets are missing.
* **Driver 3:** Developer Workstations — Preserve developer ergonomics via local mocks and structured allowlists for intentional bypass.

## 3. Options Considered
### Option 1: Maintain broad scanner bypasses
* **Overview:** Keep broad exclusions in security scripts.
* **Pros:** Less configuration effort.
* **Cons:** Security vulnerability (hardcoded fallbacks slip through).

### Option 2: Full Scanner Tightening & Strict Initialization Assertions (Selected)
* **Overview:** Remove broad exclusions, scan for hardcoded environment lookups, permit explicit annotations (e.g., `# pragma: allowlist secret`), and assert `GATEWAY_SECRET` presence on API Gateway boot.
* **Pros:** Absolute security, fail-closed design, and no runtime fallback vulnerabilities.
* **Cons:** Requires explicit configuration in all targets and test files.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Implementing Option 2 resolves the critical security gap and strictly satisfies PRD-SYS-001 by introducing fail-closed startup validation while allowing inline audit override comments.

## 5. Consequences & Trade-offs
* **Positive Impact:** Safer production configurations, zero risk of fallback credential leaks, and complete test audit visibility.
* **Negative Impact / Technical Debt:** Requires mocking the required secrets across test environments.
* **Mitigation Strategy:** Configured default mock environments inside `tests/conftest.py` for standard local verification.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/gateway/main.py`, `packages/security/`, `scripts/audit_security.py`
* **Verification Plan:** Validated using unit and integration tests under `tests/test_compliance_security.py`.
