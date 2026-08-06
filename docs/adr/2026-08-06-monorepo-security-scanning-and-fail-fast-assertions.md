# ADR-[NUMBER]: Global Monorepo Security Scanning and Fail-Fast Startup Assertions

- **Status:** Accepted
- **Date:** 2026-08-06
- **Authors:** @jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The Cadence Clinical platform consists of multiple active clinical microservices. To ensure that these services are configured securely in staging and production environments, we need a robust validation mechanism that blocks startup if critical environment secrets use insecure fallback values, strictly complying with **PRD-SYS-001**. Previously, static security scans bypassed several microservices, and runtime environment validations were not consistently enforced across all service entrypoints.

## 2. Decision Drivers & Constraints

- **Compliance (PRD-SYS-001):** Cryptographic keys, salts, and secrets must be configured with explicit, secure values in production.
- **Microservice Diversity:** Multiple services (e.g., ctms, designer, econsent, eisf, etmf, execution, interop, notifications, org, quality, safety) must consistently assert secure configuration on boot.
- **Static Scan Coverage:** Security checks must cover all monorepo directories globally while allowing a standard `.scannerignore` mechanism for test resources.

## 3. Options Considered

- **Option 1: Individual Service Custom Startup Checks**
  Write service-specific validation logic in each microservice entrypoint.
  - *Pros:* Custom messages per service.
  - *Cons:* High duplication, difficult to maintain, prone to drift.

- **Option 2: Centralized Security Guardrail and Global Scan Upgrades (Selected)**
  Implement a shared `assert_secure_secrets` function in `packages/security/fail_fast.py`, integrate it into every clinical microservice's `main.py`, configure global scanning by default, and support `.scannerignore`.
  - *Pros:* Single source of truth for fail-fast behavior, zero duplication, global linter enforcement, and robust test coverage.
  - *Cons:* Require test configurations to explicitly mock APP_ENV or secrets.

## 4. Decision Outcome

We chose **Option 2**.
To implement this, we:
1. Created `packages/security/fail_fast.py` to centralized `assert_secure_secrets` behavior.
2. Anchored validation checks in `main.py` across all active clinical microservices.
3. Configured the security scanner to run globally while respecting a `.scannerignore` file.

## 5. Consequences & Trade-offs

- **Positive:** Centralized and unified environment validation, fail-fast safety on misconfiguration, expanded monorepo-wide security coverage.
- **Negative:** Need to update/mock configurations in localized test and local developer environments.
- **Mitigation:** Safe local fallback values are allowed when `APP_ENV` is unset or set to development/test.

## 6. Implementation & Verification

- **Packages modified:** `packages/security/fail_fast.py`, `packages/security/__init__.py`.
- **Services modified:** `apps/ctms/main.py`, `apps/designer/main.py`, `apps/econsent/main.py`, `apps/eisf/main.py`, `apps/etmf/main.py`, `apps/execution/main.py`, `apps/interop/main.py`, `apps/notifications/main.py`, `apps/org/main.py`, `apps/quality/main.py`, `apps/safety/main.py`.
- **Tests Added:** `test_assert_secure_secrets_validation` and `test_global_scanner_with_opt_out` under `tests/test_compliance_security.py`.
