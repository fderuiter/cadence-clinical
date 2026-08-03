# ADR-120: Refactor Signature Validations and Enforce Ruff S101

* **Status:** Accepted
* **Date:** 2026-08-02
* **Authors:** @google-labs-jules
* **Deciders:** @engineering-lead, @security-architect

---

## 1. Context & Problem Statement
Relying on standard `assert` statements for critical business logic, specifically GxP electronic signature validations, poses a severe risk in Python since assertions can be globally compiled away when running the interpreter with standard optimization flags (`-O` or `-OO`). If optimized out, signature verification would be silently bypassed. To secure our clinical platform and ensure compliance with 21 CFR Part 11 and EU Annex 11, we must replace all assertion-based checks on critical paths with explicit conditional validations and error handling. Furthermore, we need to enforce this prevention programmatically in CI/CD via the Ruff `S101` lint rule while allowing native test assertions.

This decision implements requirements under Trace-13 and PRD-SYS-001.

## 2. Decision Drivers & Constraints
* **GxP & Part 11 Compliance:** Ensuring signature validation cannot be bypassed under any production run/optimization configurations.
* **Proactive Security:** Transitioning from dynamic verification checks to robust, compile-proof compile-time constraints.
* **Developer Velocity & Guardrails:** Automatically gating developer contributions via Ruff linting to block future assertions in production paths without breaking native testing structures.

## 3. Options Considered
### Option 1: Maintain Status Quo with Assertions
Keep using `assert` for electronic signatures on study versions and eTMF clinical documents.
* **Pros:** Minor code changes, standard Python pattern.
* **Cons:** ❌ High vulnerability of silent authorization bypass when optimized in production.

### Option 2: Explicit Code Checks & Programmatic CI Linting (Selected)
Replace `assert` statements on state-changing or critical signature verification paths with explicit conditional blocks raising `HTTPException` with appropriate auditing, while enforcing Ruff `S101` in production files with test exclusions.
* **Pros:** ✅ Absolute safety under all Python optimization levels, standard REST API error delivery, and automated build prevention.
* **Cons:** ❌ Slightly more verbose code patterns.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Implementing explicit conditional checks with appropriate exception handling ensures regulatory GxP non-repudiation and security. Programmatically locking the ruff `S101` rule blocks future regression.

## 5. Consequences & Trade-offs
* **Positive Impact:** Guarantees 100% compliance in production deployments, structured API gateway response schemas on signature rejection, and explicit failure audit logging.
* **Negative Impact / Technical Debt:** Requires developers to use explicit exceptions instead of simple assertions in non-test directories.
* **Mitigation Strategy:** Automated Ruff linting with S101 enabled is added to the PR build pipeline, which fails immediately on assertion imports in production code while excluding `tests/**` files via `per-file-ignores`.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/designer/main.py`, `apps/etmf/main.py`, `apps/gateway/main.py`, `pyproject.toml`
* **Verification Plan:**
  - Automated ruff check verification passes.
  - Verification of signature validations through integration tests (`tests/test_etmf_signing_lifecycle.py` and `tests/test_gateway_ecoa.py`).
