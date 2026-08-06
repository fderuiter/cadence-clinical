# ADR-120: Enforce Strict Fail-Closed Environment Rules and Narrow Scanner Exclusions

- **Status:** Accepted
- **Date:** 2026-09-06
- **Authors:** @jules, @engineering-team
- **Deciders:** @engineering-lead, @quality-officer

---

## 1. Context & Problem Statement

Previously, our automated security scanner globally bypassed any lines of code containing environment variable lookups (like `os.getenv` or `os.environ`). This introduced a critical security blind spot, potentially allowing hardcoded fallback secrets to bypass detection and slip into production environments unnoticed.
To achieve GxP and 21 CFR Part 11 compliance, we must guarantee that all cryptographic signatures and service integrations rely strictly on secure, environment-specific secrets rather than silent static fallbacks.

This addresses the security and compliance requirements outlined in PRD-SYS-001.

## 2. Decision Drivers & Constraints

- **Driver 1:** Enforce robust security practices.
- **Driver 2:** Achieve GxP and 21 CFR Part 11 compliance.
- **Driver 3:** Eliminate security blind spots in static scanning pipelines.

## 3. Options Considered

### Option 1: Global Bypass of Environment Lookups

- **Overview:** Maintain the legacy global skip logic in security scans.
- **Pros:**
  - ✅ Simplifies setup for local development.
- **Cons:**
  - ❌ Major security risk of hardcoded fallbacks slipping through.

### Option 2: Fail-Closed Architecture and Narrow Scanner Exclusions

- **Overview:** Remove global scan skip, implement a "Hardcoded Environment Fallback" regex rule, and enforce startup-level RuntimeError validation for missing variables.
- **Pros:**
  - ✅ Eliminates security blind spots.
  - ✅ Guarantees production environments cannot start with default hardcoded secrets.
- **Cons:**
  - ❌ Requires explicit bypass annotations for test scenarios.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Choosing Option 2 aligns directly with the mandatory security standards and ensures strict adherence to PRD-SYS-001.

## 5. Consequences & Trade-offs

- **Positive Impact:** Secure, robust secrets management and improved scanner accuracy.
- **Negative Impact / Technical Debt:** Requires using `# pragma: allowlist secret` for legitimate fallback tests or mock configuration setups.
- **Mitigation Strategy:** Document the allowed bypasses in development guides.

## 6. Implementation & Verification

- **Affected Repositories / Services:** Gateway Service, Study Designer, Security packages.
- **Verification Plan:** Verified through automated tests inside `tests/test_compliance_security.py` and pre-commit hooks.
