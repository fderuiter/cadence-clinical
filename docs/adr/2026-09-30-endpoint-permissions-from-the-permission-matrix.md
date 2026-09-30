# ADR-2510: Endpoint Permissions from the Permission Matrix

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-009

---

## 1. Context & Problem Statement

Every service authenticates the gateway-signed caller, and the fallback policy admits any authenticated caller.
Deciding which roles may call a route has been done in three different ways:

- **Hand-written role predicates** in application services, as in Execution's SDV and subject checks.
- **Nothing at all.** Designer's protocol compile and the Safety, Notifications and CTMS render routes admit any
  signed-in caller (#4958, #4960).
- **`RequirePermission("resource:action")`** from `packages/security`, which is meant to read
  `PermissionMatrix`. It names a policy `Permission:resource:action` that nothing registers, so the first route to
  use it would fail every request with a 500 (#4961).

## 2. Decision Drivers & Constraints

- **One source of role grants per permission:** `PermissionMatrix`, the table the web app and audits already
  describe.
- **Fail closed:** a caller without the permission gets `403`, never the route, and a mistyped permission fails
  loudly rather than admitting everyone.
- **One line on the route**, so that a missing check is visible in review.
- **Replaceable:** Trust-engine grants will replace role tables (#4781) without touching routes.

## 3. Options Considered

### Option 1: Register one named policy per permission at startup

- **Cons:**
  - ❌ Each service has to list every permission it uses, and a forgotten one is the same 500.

### Option 2: Role predicates in each service

- **Cons:**
  - ❌ The role lists drift from the permission matrix and from each other.

### Option 3: A policy provider that builds permission policies on demand (Selected)

- **Overview:**
  - `PermissionPolicyProvider` is registered by `AddCadenceSecurity()`. It answers every policy name starting with
    `Permission:` with a policy on the gateway scheme that:
    - requires an authenticated caller;
    - requires a `PermissionRequirement`, which `PermissionAuthorizationHandler` checks against `PermissionMatrix`
      using the signed principal's normalized roles.
  - Every other policy name, and the default and fallback policies, come from `AuthorizationOptions` as before.
  - A permission policy name that is not `resource:action` throws.
- **Pros:**
  - ✅ `RequirePermission("study_design:create")` works on any route with no per-service registration.
  - ✅ The route declares what it needs; the matrix says who has it.
- **Cons:**
  - ❌ Checks remain role-based until the Trust engine's grants replace the matrix (#4781).

## 4. Decision Outcome

- **Chosen Option:** Option 3.
- **Justification:** It makes the existing convention work and fail closed, and gives the unchecked routes one way to
  be checked.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Routes that need a role use `.RequirePermission("resource:action")` and answer `403` to any other caller.
- **Negative Impact / Technical Debt:**
  - Row-level rules (for example "unblinding needs an unblinding authority only when the target is UNBLINDED") still
    live in application code, as in Execution's `SubjectAuthority`, because they depend on the request.

## 6. Implementation & Verification

- **Affected code:**
  - `packages/security/src/Rbac/PermissionPolicyProvider.cs`
  - `packages/security/src/DependencyInjection/CadenceSecurityServiceCollectionExtensions.cs`
- **Verification:** `tests/Cadence.UnitTests/Packages/PermissionPolicyTests.cs` hosts a route with `RequirePermission`
  and checks:
  - holders of the permission get `200`;
  - other signed callers get `403`;
  - unsigned callers get `401`;
  - routes without a permission keep the authenticated-caller fallback;
  - malformed permission names are refused.
