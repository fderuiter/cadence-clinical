# ADR-2157: Enforce Fail-Fast Environment Validation for GxP Compliance

- **Status:** Accepted
- **Date:** 2026-08-04
- **Authors:** @google-labs-jules[bot]
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To ensure absolute compliance with **21 CFR Part 11 data integrity standards**, we must guarantee that no service can initialize using insecure, hardcoded default fallback keys. Previously, downstream GxP audit systems and email validation utilities relied on hardcoded fallback keys (`gxp-audit-secret-key-cadence-2026` and `dev-default-secret-inbound-email-hmac`). This allowed microservices to boot up and run in an insecure state if environment variables were missing, exposing signature and access logs to tampering risks.

## 2. Decision Drivers & Constraints

- **Compliance & Security:** Zero tolerance for fallback secrets in non-development environments to satisfy GxP requirements (`PRD-SYS-003`).
- **Fail-Fast Boot:** Raise exceptions at the earliest import or initialization phase to prevent misconfigured containers from accepting requests.
- **Sensitive-Value-Free Error Logs:** Missing keys must be reported clearly in startup error logs without revealing any secrets.
- **Developer Experience:** Provide safe mock overrides in standard test environments (`tests/conftest.py`) so tests run smoothly without requiring production secrets.

## 3. Options Considered

### Option 1: Hardcoded fallback keys with warning logs

- **Overview:** Keep fallback keys but log warnings when they are used in production.
- **Pros:**
  - ✅ Simple to implement.
  - ✅ No container startup crashes.
- **Cons:**
  - ❌ Non-compliant with FDA 21 CFR Part 11 and GCP data integrity guidelines.
  - ❌ Misconfigured services are hard to detect early.

### Option 2: Fail-Fast Startup Validation (Selected)

- **Overview:** Check for environment variables directly upon module loading or service initialization. Raise a `RuntimeError` if missing, preventing startup.
- **Pros:**
  - ✅ 100% GxP and regulatory compliant.
  - ✅ Misconfigured environments are caught instantly during local or deployment boot phases.
- **Cons:**
  - ❌ Requires test environment setup to explicitly supply safe fallback test values.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Hardcoded fallback keys are incompatible with clinical system audit trail integrity under 21 CFR Part 11. Enforcing fail-fast behavior is the only secure way to guarantee cryptographic signatures cannot be forged or tampered with due to fallback defaults.

## 5. Consequences & Trade-offs

- **Positive Impact:** Secure-by-default container bootstrapping. Absolute regulatory compliance.
- **Negative Impact / Technical Debt:** Local environments and test runners must have these variables mock-initialized.
- **Mitigation Strategy:** Safe mock secrets are pre-configured in `tests/conftest.py` for testing, and standard local running configurations have their environment defaults configured.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/security/` (including `audit_logger.py`, `signing.py`, and `crypto_verifier.py`)
- **Verification Plan:** Verify with automated unit tests:
  - `test_audit_logger_raises_runtime_error_if_secret_missing`
  - `test_signing_raises_runtime_error_if_email_secret_missing`
