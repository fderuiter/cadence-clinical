# ADR-2661: Consolidate Standalone Caller Attribution and Clinical Scope Guards in Cadence.Security

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-013, PRD-SYS-053, PRD-SYS-192

---

## 1. Context & Problem Statement

Following `ADR-2516`, `ADR-2548`, and `ADR-2552`, standalone clinical services still maintained four local caller/scope helper classes (`ExecutionScopeGuard`, `CallerIdentity`, `SafetyCaller`, and `PaymentsCaller`), 12 standalone endpoint files that read `HttpContext.Items[GatewayAuthenticationHandler.PrincipalItemKey]` directly or defined private `CallerId` / `ResolveCaller` / `ResolveActor` wrappers, and eight private `OutsideStudy()` helpers building untyped `Results.Problem` responses. This duplication introduced concrete authorization inconsistencies:

1. **Case-Sensitive Site Set Drift:** `SiteAccessEvaluator.CanAccessSite` compares site codes with `StringComparison.OrdinalIgnoreCase`, whereas `ClinicalScopeGuard.ResolveSiteFilter` and `ExecutionScopeGuard.ResolveSiteFilter` constructed `HashSet<string>` instances with `StringComparer.Ordinal` and `ApplyFilters` did not trim selector values, causing mixed-case site assignments (for example `"Site-101"` vs `"site-101"`) to pass single-site admission while dropping records from list endpoints.
2. **Direct Handler Test Claim Fallback Gaps:** `CallerScope.Principal(HttpContext)` inspected only `GatewayAuthenticationHandler.PrincipalItemKey`, forcing standalone handlers to maintain parallel `ClaimsPrincipal` parsers (`CallerIdentity`, `SafetyCaller`, `PaymentsCaller`) for direct unit tests that bypass the gateway authentication handler.
3. **Untyped 403 Results & Service-Local `IQueryable` Narrowing:** `ClinicalScopeGuard.Forbidden` returned untyped `IResult` without `title` or `extensions["code"]` customization, preventing `Execution` endpoints (`code: "FORBIDDEN_SCOPE"`) and typed handler assertions from inspecting `ProblemHttpResult.ProblemDetails` directly, while EF Core `IQueryable<T>` study/site narrowing lived exclusively inside `PaymentsCaller`.

## 2. Decision Drivers & Constraints

- **Single Canonical Security Surface (`PRD-SYS-053`, `PRD-SYS-192`, `ADR-2516`, `ADR-2548`, `ADR-2552`):** Standalone clinical services must resolve verified caller identity, workspace tenant scope, audit actor attribution, and study/site narrowing exclusively through `Cadence.Security.Rbac.CallerScope` and `Cadence.Security.Rbac.ClinicalScopeGuard`, with zero standalone endpoint reads of `GatewayAuthenticationHandler.PrincipalItemKey`.
- **Case-Insensitive Site Parity:** `ClinicalScopeGuard.ResolveSiteFilter`, `ApplyFilters`, and `NarrowToScope<T>` must treat site codes case-insensitively (`StringComparer.OrdinalIgnoreCase` in memory and trimmed uppercase comparison in EF Core expression trees) to match `SiteAccessEvaluator.CanAccessSite`.
- **Exact HTTP Status & Payload Parity:** Consolidating onto shared helpers must preserve every existing HTTP `401`, `403`, and `404` status code, `ProblemDetails.Title`, `ProblemDetails.Detail`, `extensions["code"]` (`FORBIDDEN_SCOPE`), SCE `OUTSIDE_STUDY_SCOPE` JSON bodies, 255-character `PaymentAuditLog.UserRoles` truncation, and system actor constants (`SeedSystemUser`, `AutoApprovalSystemUser`).
- **Typed Minimal API Testability:** All `ProblemDetails`-backed 403 paths on `ClinicalScopeGuard` (`Forbidden`, `OutsideStudy`, `Require*`, and `Resolve*`) must return `ProblemHttpResult` so unit and integration tests can inspect `StatusCode` and `ProblemDetails` without executing an `HttpContext` response pipeline.

## 3. Options Considered

### Option 1: Retain Service-Local Caller Wrappers and Patch Site Comparers Individually

- **Overview:** Patch `StringComparer.OrdinalIgnoreCase` in `ClinicalScopeGuard` and `ExecutionScopeGuard` while keeping `CallerIdentity`, `SafetyCaller`, `PaymentsCaller`, and endpoint-private `PrincipalItemKey` readers in place.
- **Pros:**
  - Smaller diff across standalone endpoint signatures.
- **Cons:**
  - Leaves duplicated `ClaimsPrincipal` parsing, untyped `IResult` 403 responses, and divergent study/site narrowing implementations across `execution`, `safety`, `payments`, `ctms`, `econsent`, `imaging`, `interop`, `etmf`, and `knowledge`.

### Option 2: Consolidate Caller Attribution and Study/Site Scope Helpers into `CallerScope` and `ClinicalScopeGuard` (Selected)

- **Overview:** Extend `CallerScope.Principal(HttpContext)` to check `GatewayAuthenticationHandler.PrincipalItemKey` first and fall back to authenticated `ClaimsPrincipal` `CadenceClaimTypes.Subject` claims; add `VerifiedUserId`, `RequiredPrincipal`, `RequiredUserId`, `Roles(HttpContext, int maxLength)`, and `ResolveActor` on `CallerScope`; update `ClinicalScopeGuard` to use `StringComparer.OrdinalIgnoreCase` in `ResolveSiteFilter`, trim selectors in `ApplyFilters`, enforce gateway-verified workspace tenant scope via `RequireVerifiedTenant` / `requireVerifiedTenant: true` (`ADR-2552`), return `ProblemHttpResult` from `Forbidden` and `OutsideStudy`, expose single-site pushdown `ResolveSiteFilter` overloads, and provide `IsUnrestricted`, `NarrowToStudyScope<T>`, and case-insensitive `NarrowToScope<T>` over `IQueryable<T>`; and delete `ExecutionScopeGuard`, `CallerIdentity`, `SafetyCaller`, and `PaymentsCaller`.
- **Pros:**
  - Eliminates all standalone caller/scope helper duplication and direct `PrincipalItemKey` coupling across standalone services.
  - Aligns in-memory and EF Core site filtering with `SiteAccessEvaluator.CanAccessSite` (`OrdinalIgnoreCase`).
  - Returns strongly typed `ProblemHttpResult` across all `ProblemDetails` 403 paths while preserving exact wire contracts.
- **Cons:**
  - Requires updating standalone handler signatures (`PaymentEndpoints`) from `ClaimsPrincipal` to `HttpContext` so gateway-verified `Principal` items take precedence over raw claims.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Extending `CallerScope` and `ClinicalScopeGuard` in `packages/security` provides a single fail-closed implementation for gateway-authenticated requests, direct `ClaimsPrincipal` handler tests, in-memory `HashSet` filtering, and EF Core `IQueryable<T>` narrowing while preserving exact HTTP status and payload contracts.

## 5. Consequences & Trade-offs

- **Positive Impact:** Mixed-case site assignments behave identically across single-site checks, in-memory filters, and EF Core queries; standalone services share one audited identity and scope surface; and unit tests inspect `ProblemHttpResult` directly.
- **Negative Impact / Technical Debt:** `ClinicalScopeGuard` carries dedicated single-site pushdown helpers (`ResolveSingleSiteFilter`, `RequireStudyAndResolveSingleSiteFilter`, `ResolveStudyAndSingleSiteFilters`) alongside `ResolveSiteFilter` to support repository endpoints that optimize single-site queries without C# `out`-parameter overload ambiguity.
- **Mitigation Strategy:** Comprehensive unit tests in `CadenceSecurityUnitTests` and cross-service authorization tests (`ExecutionCallerScopeTests`, `RouteAuthorizationTests`, `StandaloneServiceScopeAuthorizationTests`) lock in case-insensitivity, fallback precedence, and `ProblemHttpResult` semantics.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/security` and standalone services `ctms`, `designer`, `econsent`, `etmf`, `execution`, `imaging`, `interop`, `knowledge`, `payments`, `safety`, and `tickets`.
- **Verification Plan:** Verified via `CadenceSecurityUnitTests`, `ExecutionCallerScopeTests`, `RouteAuthorizationTests`, `StandaloneServiceScopeAuthorizationTests`, `PaymentsStudySiteScopeTests`, and `cadence check adrs`, `duplication`, and `openapi`.
