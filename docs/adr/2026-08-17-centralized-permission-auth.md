# ADR-097: Centralized Permission-Based Authorization and Dynamic Mappings

* Status: Accepted
* Date: 2026-08-17
* Authors: @jules
* Deciders: @fderuiter

---

## 1. Context & Problem Statement
The Cadence Clinical platform spans multiple services (such as eTMF, CTMS, and Quality) that require strict access controls to remain GxP-compliant and align with CDISC standards. Previously, authorization was handled using decentralized local role checks and hardcoded split roles. This caused maintenance friction, security review overhead, and made dynamic role mappings impossible to enforce systematically.

## 2. Decision Drivers & Constraints
* **GxP Auditing & Compliance:** All document states and quality transactions must have clear, centrally defined authorization constraints.
* **Maintainability & DRY:** Authorization logic should not be duplicated across microservices.
* **Dynamic Role Support:** Roles (including sponsor roles and admins) should resolve dynamically using a centralized RBAC mechanism.

## 3. Options Considered
### Option 1: Decentralized authorization in each service
* **Overview:** Keep role checks locally in eTMF, CTMS, and Quality routers.
* **Pros:**
  * ✅ High isolation between services.
* **Cons:**
  * ❌ Severe duplication of RBAC tables.
  * ❌ Risk of authorization drifts between components.

### Option 2: Centralized RBAC using dynamic permission-based mappings (Selected)
* **Overview:** Standardize on a unified role-permission schema inside `packages/security/rbac.py` and implement dynamic mappings (such as matching sponsor admin variations or mapping specific transition permissions to actions).
* **Pros:**
  * ✅ Single source of truth for all role permissions.
  * ✅ Simplifies access verification down to `has_permission(principal, permission)`.
  * ✅ Facilitates GxP compliance and audit log tracing.
* **Cons:**
  * ❌ Centralized changes can impact multiple downstream services if improperly coordinated.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Centralizing RBAC and utilizing a dynamic permission-mapping model eliminates hardcoded split-role checks and ensures strict GxP conformance across eTMF, CTMS, and Quality services.

## 5. Consequences & Trade-offs
* **Positive Impact:** Security definitions are completely centralized and easy to audit.
* **Negative Impact / Technical Debt:** Requires all services to depend on the `packages/security` shared package.
* **Mitigation Strategy:** Enforce shared dependencies and run automated test suites continuously.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/security/rbac.py`, `apps/etmf/`, `apps/ctms/`, `apps/quality/`
* **Verification Plan:** Validated via unit/integration test runs in `pytest` verifying that unauthorized requests are blocked and valid roles/permissions map perfectly.
