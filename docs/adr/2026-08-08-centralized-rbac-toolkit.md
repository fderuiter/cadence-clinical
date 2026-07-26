# ADR-059: Centralized RBAC Toolkit, Permissions, Principals, and Field Masking

* **Status:** Accepted
* **Date:** 2026-08-08
* **Authors:** @jules
* **Deciders:** @fderuiter, @jules

---

## 1. Context & Problem Statement
The Cadence Clinical platform consists of multiple services (eTMF, Execution, Interop, Metadata Designer, etc.) that need to validate user privileges, restrict access based on trial site assignments, and obfuscate sensitive fields to enforce clinical trial blinding plans.
Previously, role verification was handled locally by individual services, leading to duplicated code, inconsistent role mappings, and high risk of audit compliance failures. We need a centralized, authoritative, and declarative role-based access control (RBAC) and data-masking framework under `packages/security` that all services can seamlessly consume.

## 2. Decision Drivers & Constraints
* **Separation of duties (FDA 21 CFR Part 11 / GxP):** Strictly enforce separation between clinical and administrative personas.
* **Blinding Plan Integrity (ICH E6(R2) / GCP):** Automatically obfuscate sensitive/unblinded data for blinded roles.
* **Single Authoritative Source:** Standardize role definitions, permission matrix, and masking rules in a single shared package.
* **Compatibility:** Maintain seamless backwards compatibility with existing FastAPI dependencies and tests.

## 3. Options Considered
### Option 1: Decentralized RBAC in each Microservice
* **Overview:** Each service parses roles locally and implements its own permissions and masking.
* **Pros:** No shared dependencies.
* **Cons:** Duplicate code, inconsistent mappings, high risk of compliance leakage, difficult to maintain.

### Option 2: Shared Declarative RBAC Toolkit in `packages/security` [Selected]
* **Overview:** Centralize all canonical roles, a `ROLE_ALIASES` normalizer mapping, a declarative `ROLE_PERMISSIONS` matrix, and recursive `mask_payload` routines inside `packages/security/rbac.py`.
* **Pros:**
  * ✅ One single authoritative implementation for access control and data blinding.
  * ✅ Robust normalization maps variations (e.g., PI, Principal Investigator) to a set of 9 canonical roles.
  * ✅ FastAPI dependency helpers (`get_principal`, `require_permission`) simplify endpoint gating.
  * ✅ Schema-agnostic, recursive masking recursively processes dicts, lists, and Pydantic models.
* **Cons:**
  * ❌ Shared package updates require redeploying dependent microservices.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Centralizing identity mapping and blinding rules guarantees mathematical and programmatic consistency across all API paths, completely mitigating the risk of accidental unblinding or unauthorized data mutation.

## 5. Consequences & Trade-offs
* **Positive Impact:** Services can enforce site-isolation boundaries, action permissions, and data blinding using clean declarative dependencies without duplicating code.
* **Negative Impact / Technical Debt:** Microservices depend on the `packages/security` library; any changes in the matrix or masking rules require version bumping and redeployment.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/security/rbac.py`, `packages/security/__init__.py`.
* **Verification Plan:** Verified locally via `pytest tests/test_rbac.py` with 20 newly introduced high-fidelity test cases, as well as full CI/CD run.
