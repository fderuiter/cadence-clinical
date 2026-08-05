# ADR-2156: Fail-Fast Environment Validation for Cryptographic Secrets

- **Status:** Accepted
- **Date:** 2026-08-04
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In a GxP-compliant clinical platform, the integrity and confidentiality of audit logging and inbound communication are paramount. According to requirement **PRD-SYS-001**, all cryptographic operations must use securely configured environment secrets without fallback defaults in production environments. Previously, some core packages had fallback placeholders if environment variables were unconfigured. We must eliminate fallback values and enforce immediate, fail-fast validation upon module initialization to guarantee that the system terminates immediately if misconfigured.

## 2. Decision Drivers & Constraints

- **Compliance (PRD-SYS-001):** Cryptographic keys must be explicitly configured in the environment. Hardcoded fallbacks are strictly prohibited in regulated environments.
- **Fail-Fast Safety:** Any misconfiguration in key environment variables (e.g., `AUDIT_LOG_SECRET_KEY`, `INBOUND_EMAIL_HMAC_SECRET`) must prevent the application from starting.
- **Test Isolation Compatibility:** Pytest suites, mocks, and CLI validation tools must be able to inject or mock dummy credentials without failing during test execution or importing.

## 3. Options Considered

1. **Option 1: Module-Level Fail-Fast Initialization**
   Validate required environment variables at import/load time. If a required secret is missing or empty, raise `RuntimeError` immediately.
   - _Pros:_ Complete safety; absolutely zero code path can execute cryptographic functions under an insecure state.
   - _Cons:_ Requires pytest context/harness files and CLI wrapper entrypoints to pre-define safe placeholder secrets in non-production scenarios.

2. **Option 2: Deferred Validation upon Usage**
   Only validate the existence of keys when an audit log is written or an email is verified.
   - _Pros:_ Easier test isolation as modules can be imported without setting secrets.
   - _Cons:_ Introduces the risk of a running system starting up successfully but failing later when trying to execute operations, violating the fail-fast principle.

## 4. Decision Outcome

We selected **Option 1 (Module-Level Fail-Fast Initialization)**. It guarantees maximal security compliance with **PRD-SYS-001**.
To resolve test and CLI integration hurdles, we:

1. Provided default test-environment placeholders in `tests/conftest.py`.
2. Added dynamic fallback retrieval (checking `os.getenv` dynamically) to allow pytest's `monkeypatch` to function properly during localized test suites.
3. Added default fallback values during CLI package load inside `packages/deid/__init__.py`.

## 5. Consequences & Trade-offs

- **Positive:** Complete protection against starting production services with unconfigured or default secrets. Immediate visibility into environment issues.
- **Negative:** Subprocess startup tests and test environments must be explicitly configured to supply dummy keys to prevent import-time crashes.

## 6. Implementation & Verification

- **Packages modified:** `packages/security/signing.py`, `packages/security/audit_logger.py`, `packages/security/crypto_verifier.py`.
- **Testing:** Implemented dedicated validation tests in `tests/test_compliance_security.py` verifying that import-time `RuntimeError` is raised under insecure configurations.
