# ADR-2516: Routes Are Denied Unless They Declare Who May Call Them

- **Status:** Accepted
- **Date:** 2026-10-16
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-192, PRD-SYS-004, PRD-CRF-009
- **Extends:** [ADR-2510](2026-09-30-endpoint-permissions-from-the-permission-matrix.md)

---

## 1. Context & Problem Statement

ADR-2510 gave routes one way to name a permission, `RequirePermission("resource:action")`, but left the fallback
policy admitting any authenticated caller. A route that forgot the line was open to every signed-in user, and nothing
failed when one shipped. The 2026-10-15 assessment counted about 190 such routes across Execution, Payments, Interop,
eTMF, Imaging, Knowledge and eConsent, including writes such as payment disbursement, device quarantine release and
lab-range import (#5361).

Study scope failed open as well. `StudyScopeAuthorizationHandler` succeeded when it found no study id, read the id from
an unsigned header, the query or the body, and `StudyAccessEvaluator` admitted any caller with no assigned studies. No
service ever filled in assigned studies, so the check could not deny anyone.

## 2. Decision Drivers & Constraints

- **Fail closed (21 CFR 11.10(d), (g)):** a route nobody thought about is denied, not open.
- **Visible in review:** a route says who may call it on the route itself, and any route open to every signed-in caller
  is listed, with its reason, in one test.
- **Behaviour, not metadata:** the proof is that an unauthorized caller is refused before the operation runs.
- **One permission source:** `PermissionMatrix` stays the role table until the Trust engine's grants replace it
  (#4781). The kernel stays the authority for its own objects (ADR-2457).
- **Pre-deployment:** coordinated breaking changes to the gateway signature and internal routes are acceptable.

## 3. Options Considered

### Option 1: Keep the authenticated fallback and add an inventory test with a shrink-only baseline

- **Cons:**
  - ❌ Every route on the baseline stays open while the baseline shrinks, and a route can still ship open if it is
    added to the baseline.

### Option 2: Resource-based checks in each handler

- **Cons:**
  - ❌ The pattern ADR-2510 found drifting: each service writes its own role lists, and a missing check is invisible.

### Option 3: Deny by default, with three explicit declarations (Selected)

- **Overview:**
  - The fallback policy requires an authenticated gateway caller **and** an `UndeclaredEndpointRequirement` that never
    succeeds. An undeclared route answers `401` to an unsigned caller and `403` to every signed one.
  - A route declares one of:
    - `RequirePermission("resource:action")`: the caller's signed roles hold the permission in `PermissionMatrix`.
      `PermissionPolicyProvider` refuses a permission that no role holds, so a typo fails at startup or first use.
    - `RequireSignedInCaller(reason)`: any signed-in caller, for a route whose handler authorizes each object itself or
      that reads and writes nothing a signed-in caller may not see. The reason is kept on the endpoint. The kernel host's
      routes use this policy: kernel reads mask what the caller's grants do not cover (ADR-2471) and every command goes
      through the authority evaluator (K-AUTH-1). The simulation route now checks that the caller can see the protocol
      version it reads.
    - `AllowAnonymous()`: health, readiness, the service status probe and development API documents only.
  - Module permissions live in per-module catalogs (`PermissionMatrix.<Module>.cs`), so each module's grants can be
    reviewed in one place.
  - **Study scope fails closed.** `RequireStudyScope(routeParameter)` reads the study id from that route value only and
    admits a caller only when the signed principal is assigned to that study or to `*`. No route value, no principal or
    no assignment is a denial. The gateway signs the caller's assignments as `X-Study-Ids` from the sign-in token's
    `study_ids` claim, inside the same HMAC as the rest of the identity, so a client cannot add one.
  - `RequireSiteScope(routeParameter)` applies `SiteAccessEvaluator` to the site a route names, the same way.
  - **Scope the route cannot name is checked in the handler.** When the study is in a body, a query or a stored
    record, or a list must be narrowed, the handler asks `CallerScope` (`packages/security/src/Rbac/CallerScope.cs`),
    which reads the same signed principal. A write outside the caller's studies returns `403` and writes nothing; a
    read of one record outside them returns `404`; a list returns only the caller's studies. The actor on an audit
    record, certificate or signature is the signed caller, never a body field, an unsigned header or a literal role
    name.
  - Scope narrows a permission and never replaces one: a route with only a scope policy fails the route test.
- **Pros:**
  - ✅ No route is open by omission, from the first commit.
  - ✅ The route test (`tests/Cadence.UnitTests/Security/RouteAuthorizationTests.cs`) boots every service and checks
    both the declarations and the behaviour.
- **Cons:**
  - ❌ Study assignments come from the identity provider until the kernel's grants decide study scope for every module
    (#4781). That is an interim second source, documented here and limited to narrowing routes that already need a
    permission.

## 4. Decision Outcome

- **Chosen Option:** Option 3.
- **Justification:** It closes the gap for every route at once and makes the next route fail its test rather than ship
  open.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - A route that declares nothing is unreachable, so a forgotten check is found in the first test run.
  - Unauthorized callers are refused by the authorization middleware, before model binding or the handler, so nothing
    is read or written.
- **Negative Impact / Technical Debt:**
  - Role permissions remain role-based until #4781; row-level rules that depend on the request (for example a subject's
    site) stay in application code.
  - Every identity provider configuration must issue `study_ids` for users of study-scoped routes. The local realm
    (`docker/cadence-realm.json`) maps the attribute and gives demo users `*`.

## 6. Implementation & Verification

- **Affected code:**
  - `packages/security/src/DependencyInjection/CadenceSecurityServiceCollectionExtensions.cs`
  - `packages/security/src/Rbac/AuthorizationHandlers.cs`, `EndpointAuthorizationMetadata.cs`, `CallerScope.cs`,
    `StudyAccessEvaluator.cs`, `PermissionMatrix*.cs`, `PermissionPolicyProvider.cs`
  - `packages/security/src/Authentication/GatewayIdentity.cs`, `GatewayAuthenticationHandler.cs`, `HmacSigner.cs`
  - `apps/gateway/src/GatewayIdentityForwarding.cs`, `docker/cadence-realm.json`
  - `packages/kernel/src/Cadence.Kernel.Api/KernelApiEndpointRouteBuilderExtensions.cs`,
    `KernelSimulationEndpoints.cs`
  - Every module's route mappings.
- **Verification:**
  - `RouteAuthorizationTests` boots every service host and, for each one:
    - every route declares a permission, a listed signed-in reason, or an allowed anonymous probe;
    - every permission route refuses a signed caller holding every role that lacks the permission, and a caller with
      no roles, with `403` from the authorization middleware, and an unsigned caller with `401`;
    - every permission route admits a caller holding the permission.
  - `PermissionPolicyTests` covers the deny-by-default fallback, study scope from the route only, an unsigned study
    header being ignored, a tampered study assignment failing authentication, and an unknown permission being refused.
  - `CadenceSecurityUnitTests.StudyAccessEvaluator_FailsClosed` and `GatewayStudyAssignmentTests` cover the evaluator and
    the gateway's signed assignments.
