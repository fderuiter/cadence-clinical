# ADR-2162: Unified Shared Branding Validation and Fallback Configurations

- **Status:** Accepted
- **Date:** 2026-08-11
- **Authors:** @jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Following the initial decisions in ADR-2161, we parameterized the platform's brand identity and domain configurations across multiple services. However, as the number of microservices grew, the validation logic became duplicated and inconsistent across different services' boot sequences. We need a centralized, robust, and unified validation mechanism that enforces secure configurations across the API Gateway and all backend services, while providing standardized fallback behaviors for local development.

## 2. Decision Drivers & Constraints

- **Single Source of Truth:** Branding validation rules must be defined in a centralized package (`packages/security`) rather than duplicated in each service.
- **Fail-Fast Compliance (PRD-SYS-001):** In non-development environments (production/staging) or automated CI/CD checks, incorrect or legacy defaults must immediately halt startup to ensure regulatory and organizational compliance.
- **Developer Velocity:** Local dev boot sequences must remain non-blocking, degrading to warning logs with safe fallbacks instead of crashing the developer's environment.

## 3. Options Considered

### Option 1: Decentralized Service-Level Validation

- **Overview:** Each microservice maintains its own validation routines within its `main.py` file.
- **Pros:**
  - Simple local coupling.
- **Cons:**
  - Highly repetitive code.
  - Risk of configuration drift when new validation checks (e.g. keycloak parameters) are introduced.

### Option 2: Unified Shared Package-Level Validation (Selected)

- **Overview:** Introduce a single validation function `validate_branding` in `packages/security/branding.py` that handles all domain, name, and optional auth parameters validation.
- **Pros:**
  - ✅ Single location to update/improve validation.
  - ✅ Enforces uniform fail-fast behavior across gateway and all microservices.
  - ✅ Supports optional authentication validation for the gateway.
- **Cons:**
  - ❌ Requires importing the shared security library at module level, which requires proper virtual environment path alignments during testing/linting.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Centralizing the logic in `packages/security/branding.py` ensures perfect compliance with `PRD-SYS-001` and eliminates duplicate code, making maintenance and feature enhancements simple and safe.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Complete alignment on brand parameters across the API Gateway and 14 microservices.
  - Reduced boilerplate code in individual microservice `main.py` boot files.
- **Negative Impact / Technical Debt:**
  - CI environment must be seeded with non-default branding environment variables to pass automated static checkers and test suites.

## 6. Implementation & Verification

- **Affected Services:** `apps/gateway`, `packages/security`, and all backend microservices.
- **Verification Plan:**
  - Verified with extensive unit tests in `packages/security/tests/test_branding.py`.
  - Confirmed via local CI simulation with standard quality gating script runs (`validate_markdown.py`, `validate_adrs.py`, `pytest`).
