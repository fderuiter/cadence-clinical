# ADR-122: Decouple Shared Test Signing RBAC Helpers from Gateway

- **Status:** Accepted
- **Date:** 2026-08-11
- **Authors:** @jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To satisfy strict service boundary isolation rules and modularity within our FastAPI applications, we must prevent direct compile-time package imports across the `apps/` microservices packages (e.g., packages cannot import from `apps/`).

Previously, several clinical test suites required Gateway V2 signed headers for role-specific authentication, which were constructed using helper functions inside `tests/rbac_helpers.py`. However, these helpers depended on the `generate_signature` function inside `apps/gateway/main.py`. This created a static dependency boundary violation as package-level test utilities were forced to import application-level gateway code, triggering AST linter errors.

We need a clean, decoupled solution that isolates the shared test signing RBAC helpers into a package-level directory without violating static architectural boundaries.

## 2. Decision Drivers & Constraints

- **Strict Service & Package Boundaries:** Prevent packages or shared utilities from importing application-level code (`apps/gateway`).
- **Auditability and Compliance (PRD-SYS-001):** Ensure all electronic signatures generated during integration/unit testing continue to adhere to proper signature specifications and secrets management without compromising system integrity.
- **Maintainability & Backward Compatibility:** Keep existing test suites functional without rewriting persona headers across thousands of tests.

## 3. Options Considered

### Option 1: Inline Signature Generation in All Tests

- **Overview:** Duplicate the header signing logic directly within every test suite or test file.
- **Pros:**
  - Avoids package-level dependency issues entirely.
- **Cons:**
  - ❌ Massively violates DRY principles, introducing huge code duplication and maintaining/updating it would be highly error-prone.

### Option 2: Move RBAC Persona Helpers to `packages/security` (Selected)

- **Overview:** Extract the signature construction and persona helpers into a new package module `packages/security/rbac_helpers.py` using the underlying `packages.security.signing` library directly. Refactor `tests/rbac_helpers.py` to re-export from this package-level helper.
- **Pros:**
  - ✅ Preserves absolute clean boundaries; packages import only from other packages.
  - ✅ Retains full backward compatibility with all existing test suites via re-export.
  - ✅ Resolves all import-boundary AST validation failures.
- **Cons:**
  - ❌ Slightly increases the surface area of the shared security package.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Option 2 successfully decouples test signature generation from the application gateway service, cleanly solving package boundary violations while ensuring that all existing test personas remain perfectly intact.

## 5. Consequences & Trade-offs

- **Positive Impact:** All existing test files can continue using the persona builders seamlessly via `tests/rbac_helpers.py` re-exports, with zero import-boundary errors.
- **Negative Impact / Technical Debt:** Requires maintaining test-centric helper functions inside `packages/security/rbac_helpers.py`.
- **Mitigation Strategy:** These helpers are strictly scoped to mock/test credentials generation and utilize safely managed gateway environment secrets.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/security/rbac_helpers.py`, `tests/rbac_helpers.py`
- **Verification Plan:** Verified via `pnpm run lint`, `uv run python scripts/validate_imports.py`, and `uv run pytest` to ensure both compile-time validation and runtime test execution pass successfully.
