# ADR-101: Role Authorization for Granular Data-Lock Actions and Trust Boundary Verification

* **Status:** Accepted
* **Date:** 2026-08-24
* **Authors:** @jules
* **Deciders:** @jules
* **Requirements Reference:** PRD-SYS-001

---

## 1. Context & Problem Statement
To enforce 21 CFR Part 11 and EU Annex 11 electronic data controls, the Cadence Clinical platform supports granular data-locking mechanisms across multiple operational scopes (trial, site, subject, visit, and form levels). Modifying these lock states is a privileged operation that must be limited to authorized personas.

Previously, lock mutation endpoints authorized only `ROLE_DATA_MANAGER` via the standard `require_roles` dependency. However, Sponsor Administrators (`ROLE_SPONSOR_ADMIN`) also require authority to override lock states, and several legacy endpoints relied on decentralized local guard helpers (such as `verify_roles`) instead of the centralized RBAC package. Furthermore, we need to clearly define the trust boundary around role propagation and prevent direct microservice access bypass.

This decision implements requirements under PRD-SYS-001.

Note: A pre-existing numbering collision exists in the codebase where multiple distinct records have been labeled with **ADR-097**. We acknowledge this collision without resolving the historical documents and assign **ADR-101** as a unique identifier for this decision.

## 2. Decision Drivers & Constraints
* **Least Privilege GxP Gating:** Restrict administrative lock modifications strictly to approved administrative roles (`ROLE_DATA_MANAGER` and `ROLE_SPONSOR_ADMIN`).
* **Microservice Trust Boundary Integration:** Enforce signature-valid role propagation to prevent direct endpoint tampering or bypasses.
* **Unified Architectural Pattern:** Consolidate authorization checking around the shared, centralized RBAC package in `packages/security`, eliminating decentralized local guards.

## 3. Options Considered
### Option 1: Inline guard logic inside Execution routers
* **Overview:** Keep the existing local role checks and manually append conditional gates for both Data Manager and Sponsor Admin.
* **Pros:**
  * ✅ Quick to implement inside `apps/execution/main.py`.
* **Cons:**
  * ❌ Violates the centralized RBAC principle.
  * ❌ Decentralized guards are harder to verify, audit, and keep consistent with global compliance requirements.

### Option 2: Consolidation using Centralized `require_roles` Dependency [Selected]
* **Overview:** Authorize both `ROLE_DATA_MANAGER` and `ROLE_SPONSOR_ADMIN` as allowed roles in FastAPI's declarative route `Depends(require_roles(...))` and remove unused decentralized helpers.
* **Pros:**
  * ✅ Leverages the robust, tested `ROLE_EXPANSIONS` mappings inside `packages/security/rbac.py` which normalizes alias inputs (e.g., `Sponsor Admin`, `sponsor_admin`, `dm`, `data_manager`).
  * ✅ Uniform policy application across all lock and unlock endpoints.
  * ✅ Restricts the read-only status endpoint `/api/v1/execution/locks` strictly to read-capable roles (`ROLE_DATA_MANAGER` and `ROLE_CRA`).
  * ✅ Clear trust boundaries: untrusted/absent/malformed roles are rejected at the gateway middleware and dependency injection layer before any lock mutations are executed.
* **Cons:**
  * ❌ Requires updating multiple endpoints in `main.py` and maintaining test mock signatures.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Choosing Option 2 guarantees compliance with FDA regulations, reduces codebase fragmentation by removing dead helper functions (`_is_data_manager`, `_is_investigator`, `verify_roles`), and enforces a unified security trust boundary.

### Trust Boundary & Integration Verification
* **Gateway Authentication:** All incoming client requests must cross the gateway boundary, where JWT claims are converted to signature-injected header context variables. Any direct microservice request omitting or tampering with these signature headers gets rejected by the `GatewayAuthMiddleware` with a `401 Unauthorized` or `403 Forbidden` response. For technical details on the gateway authentication propagation, refer to [ADR 2026-07-22: Centralized API Gateway Authentication and Header Propagation](2026-07-22-gateway-authentication-propagation.md).
* **RBAC Normalization:** Route-level authorization relies entirely on [ADR-059: Centralized RBAC Toolkit](2026-08-08-centralized-rbac-toolkit.md) and [ADR-097: Centralized Permission-Based Authorization](2026-08-17-centralized-permission-auth.md) to parse canonical roles.
* **Service Integrations:** Internal cross-boundary requests (such as automatic locks originating from the eTMF service) are propagated securely as a synthetic `"Data Manager"` principal to allow programmatic execution without sacrificing audit trails.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * Clean, declarative code in the API layer.
  * Robust, signature-validated security context prevents unauthorized lock operations.
  * Unified role policy coverage across site, visit, form, subject, and trial lock/unlock levels.
* **Negative Impact:**
  * Deleting local helpers requires ensuring no other service relies on the removed internal routes (verified via comprehensive repo search).

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  * Microservice: Execution Service (`apps/execution/main.py`)
  * Tests: Integration lock API tests (`tests/test_granular_locks_api.py`)
* **Verification Plan:**
  * Added integration tests asserting:
    * Allowed Data Manager, Sponsor Admin, and their canonical aliases.
    * Forbidden personas (`CRA`, Site Investigator, Auditor, sysadmin) receive 403 on mutations, and status is not altered.
    * Malformed, blank, or whitespace roles are securely rejected before route processing.
    * Gateway-bypass scenarios (missing signature, tampered signature, expired gateway timestamp) fail with 401/403.
