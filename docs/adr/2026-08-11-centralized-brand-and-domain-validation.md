# ADR-189: Centralized Brand and Domain Validation Guardrail

- Status: Accepted
- Date: 2026-08-11
- Authors: @google-labs-jules[bot]
- Deciders: @fderuiter
- References: PRD-SYS-001

---

## 1. Context & Problem Statement

Prior to this decision, duplicate boot-time brand name, Keycloak realms, and domain validation routines were copy-pasted and run individually across all downstream microservices and the API Gateway. This led to maintenance overhead, inconsistent security policies, and high risk of misconfiguration bypasses. If a new legacy domain or brand name was deprecated, the change had to be replicated manually across 13 different code repositories/directories.

## 2. Decision Drivers & Constraints

- **Driver 1 (Maintainability):** Single source of truth for allowed/disallowed domains, brand names, and keycloak realms.
- **Driver 2 (Security & Compliance):** Enforce strict boot-time fail-fast validation in production and staging environments to prevent insecure or legacy bypasses.
- **Driver 3 (Testability):** Validation should raise standard, catchable exceptions (like `RuntimeError`) rather than executing hard `sys.exit(1)` directly to allow clean unit/integration testing.

## 3. Options Considered

### Option 1: Distributed Local Validation (Status Quo)

Keep local boot-time loops inside each `main.py`.
- **Pros:** No shared security package dependency.
- **Cons:** High code duplication and maintenance friction; drift prone.

### Option 2: Centralized Validation Utility in `packages/security` (Selected)

Implement `validate_branding` helper in the security package and import/call it at service startup.
- **Pros:** 
  - ✅ Consolidates duplicate logic into a single testable helper.
  - ✅ Restricts default and legacy domains/branding across all microservices uniformly.
  - ✅ Uses catchable `RuntimeError` allowing easy mock and testing.
- **Cons:**
  - ❌ Downstream microservices gain an explicit dependency on `packages/security` at boot time (already exists for RBAC/identity).

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Centralizing the logic ensures strict security validation parity across the gateway and all 12 microservices, preventing drift and facilitating central updates to the list of prohibited domains or credentials.

## 5. Consequences & Trade-offs

- **Positive Impact:** 100% reduction in branding validation code duplication; extremely easy to deprecate or add domains.
- **Negative Impact / Technical Debt:** No significant technical debt introduced.

## 6. Implementation & Verification

- **Affected Repositories / Services:** All 12 downstream microservices, the API Gateway, and `packages/security`.
- **Verification Plan:** Validated via new automated unit tests in `packages/security/tests/test_fail_fast_branding.py` and local run of `validate_imports.py` and `validate_path_patterns.py`.
