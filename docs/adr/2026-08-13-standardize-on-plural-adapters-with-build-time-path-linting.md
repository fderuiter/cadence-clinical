# ADR-2172: Standardize on Plural Adapters with Build-time Path Linting

- **Status:** Accepted
- **Date:** 2026-08-13
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The Cadence Clinical platform consists of multiple active microservices and package sub-projects in a unified hexagonal/ports-and-adapters architecture. Previously, there was minor cognitive load and architectural drift where some services used a singular `adapter` package or directory name while others used the plural `adapters` package name. This inconsistency made automated hexagonal architectural verification and boundary testing harder to generalize. To enforce clean, consistent, and standardized hexagonal architecture layers and boundaries across all workspaces as mandated by `PRD-SYS-001`, we need a strict codebase-wide convention on using the plural `adapters` folder layout.

## 2. Decision Drivers & Constraints

- **Architectural Consistency:** Reduce layout drift and establish a predictable pattern across all active services.
- **Ease of Automated Testing (PRD-SYS-001):** Simplify boundary checking and imports verification in pytest assertions (such as pytest-archon or custom hexagonal test engines).
- **Developer Onboarding & Cognitive Load:** Developers should not have to guess or check whether to name a folder `adapter` or `adapters`.
- **Standard Audit Logging and GxP Compliance (PRD-SYS-001):** A strictly validated repository boundary prevents out-of-band modifications and silent structural drift in GxP environments.

## 3. Options Considered

### Option 1: Dual Support (Singular and Plural)

Allow both `adapter` and `adapters` as valid folder names under `apps/` microservices.

- **Pros:**
  - ✅ Requires no immediate refactoring of existing directories.
- **Cons:**
  - ❌ Increases complexity in architectural assertions.
  - ❌ Higher risk of cognitive confusion and inconsistent layout patterns in future services.

### Option 2: Standardize on Plural `adapters` (Selected)

Unify all sub-projects and service directory structures to strictly use the plural `adapters` name and enforce this with build-time verification.

- **Pros:**
  - ✅ Predictable, fully standardized structure across the entire codebase.
  - ✅ Simpler and more rigid architecture assertions in packages like `packages/hexagonal/tests/test_hexagonal_architecture.py`.
  - ✅ Automated build-time validation prevents any regression or accidental introduction of a singular `adapter` package.
- **Cons:**
  - ❌ Requires a one-time migration of any existing files and directories.

## 4. Decision Outcome

Chosen option: Option 2. We standardise on the plural `adapters` layout because it simplifies validation, reduces developer cognitive load, and maintains strict architectural alignment with our hexagonal guidelines under `PRD-SYS-001`.

### Unified Directory Structures & Safeguards:

1. Converted `apps/safety/` adapter file and package structure to use the new `apps/safety/adapters/` directory.
2. Created placeholder `__init__.py` files inside each `adapters` directory across remaining microservices (`econsent`, `gateway`, `interop`, `notifications`, `org`, `tickets`) to establish the standardized unified folder layout.
3. Enhanced `scripts/validate_path_patterns.py` to raise validation failures if a singular `adapter` folder is detected under any service in the repository.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Consistent layout across all 15+ platform services.
  - Rigid and standardized imports and boundary validations.
  - Early build-time failure in local pre-commit checks or CI/CD pipelines if incorrect directories are introduced.
- **Negative Impact / Technical Debt:**
  - Slight churn in moving existing adapters to the plural naming convention.
- **Mitigation Strategy:**
  - Automatic checking is fully integrated into path verification and architectural lint tests.

## 6. Implementation & Verification

### Affected Repositories / Services:

- `apps/safety/` layout converted to `adapters/`.
- Placeholder package `adapters/` folders created in `apps/econsent/`, `apps/gateway/`, `apps/interop/`, `apps/notifications/`, `apps/org/`, `apps/tickets/`.
- `packages/hexagonal/tests/test_hexagonal_architecture.py` updated to strictly check imports against the plural `adapters` package.
- `scripts/validate_path_patterns.py` modified to detect and reject any folder with the singular `adapter` name.

### Verification Plan:

- ADR validation verified offline via `uv run python scripts/validate_adrs.py`.
- Path pattern lint tests executed and verified via `make lint-paths` or `uv run python scripts/validate_path_patterns.py`.
- Architectural boundary tests verified via `pytest packages/hexagonal`.
