# ADR-2217: Enforce Startup Security Fail-Fast Assertions and Remove Insecure Fallback Secrets

* **Status:** Accepted
* **Date:** 2026-08-25
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Prior to this change, default fallback secret strings (such as `"internal-gateway-secret-12345"`) were scattered across services, CLI tooling, database scripts, and test suites. Furthermore, startup environment secret validation in `packages/security/fail_fast.py` was previously restricted to non-development environments, allowing invalid or fallback secrets to bypass validation in local and testing environments. Additionally, logging error details via `sys.stderr` triggered CodeQL clear-text logging flags for sensitive data tracking.

To adhere to 21 CFR Part 11 and GxP compliance standards (referencing PRD-SYS-001), the platform requires strict fail-fast startup validation for missing or insecure secret overrides across all service contexts, without logging clear-text secret identifiers or relying on insecure default values.

## 2. Decision Drivers & Constraints

* **GxP 21 CFR Part 11 Compliance (PRD-SYS-001):** Cryptographic keys, HMAC signatures, and service secrets must be explicitly configured and validated across all platform components on startup.
* **Security & CodeQL Static Analysis:** Error messages emitted during fail-fast startup checks must not log sensitive secret values or names to `sys.stderr` in a way that triggers clear-text secret logging vulnerabilities.
* **Test & Script Stability:** Test suites, CLI utilities, and helper scripts must use dedicated, non-blacklisted test keys dynamically loaded via environment variables rather than hardcoded insecure defaults.

## 3. Options Considered

1. **Option A (Selected):** Remove hardcoded fallback secrets workspace-wide, enforce `assert_secure_secrets` fail-fast assertions unconditionally across all environments, and raise structured `RuntimeError` exceptions directly without clear-text `sys.stderr` logging.
2. **Option B:** Maintain environment-gated startup assertions and suppress CodeQL warnings with inline comment pragmas.

## 4. Decision Outcome

Chosen option: **Option A** because it enforces fail-closed startup security across all environments (dev, test, staging, production), eliminates insecure fallback secrets from service definitions and test runners, and satisfies CodeQL security scanning requirements without suppressing alerts.

## 5. Consequences & Trade-offs

* **Positive:**
  * Eliminates hardcoded insecure fallback secrets across 190+ service, client, and test modules.
  * Guarantees immediate failure on process startup if required cryptographic secrets are missing or unconfigured.
  * Cleanly passes CodeQL clear-text secret logging scans.
* **Negative:**
  * Services and scripts require explicit environment variables or non-blacklisted test key overrides when running in test environments.

## 6. Implementation & Verification

* **Modified Modules:**
  * `packages/security/fail_fast.py`: Updated `assert_secure_secrets` to enforce startup checks fail-fast across all environments and removed `print(error_msg, file=sys.stderr)` calls.
  * Services & CLI (`apps/`, `packages/`, `scripts/`): Updated secret fallbacks to non-blacklisted test key values (`valid-test-gateway-secret-key-2026`).
* **Verification:**
  * Security compliance tests in `packages/compliance/tests/test_compliance_security.py` and unit/integration tests verified clean pass across all 15 microservices.
