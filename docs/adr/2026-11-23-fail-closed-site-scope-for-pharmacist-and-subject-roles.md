# ADR-2834: Fail-Closed Site Scope for Pharmacist and Subject Roles

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-013, PRD-SYS-192

---

## 1. Context & Problem Statement

`SiteAccessEvaluator.SITE_SCOPED_ROLES` in `Cadence.Security` defines the canonical trial personas whose authorization is bound to explicit site assignments (`AssignedSites`). While `RoleNormalization` already defines `ROLE_PHARMACIST` (`"pharmacist"`, including `"unblinded_pharmacist"`) and `ROLE_SUBJECT` (`"subject"`, including `"patient"` and `"epro"`), neither role was registered in `SiteAccessEvaluator.SITE_SCOPED_ROLES`.

Because `SiteAccessEvaluator.CanAccessSite`, `ClinicalScopeGuard.IsSiteBound`, `CallerStudyAndSiteScopeAuthorizationHandler`, `SiteScopeAuthorizationHandler`, `ExecutionScopeGuard`, and `PaymentsCaller.SiteFilter` all derive site-bound enforcement from `SiteAccessEvaluator.SITE_SCOPED_ROLES`:

1. A `pharmacist` token provisioned without `site_id` attributes (`AssignedSites = []`) bypassed `hasSiteScopedRoles` and received unrestricted sponsor-level site access across RTSM IP kit inventory, scan validation, batch reconciliation, and unblinded subject detail routes within any assigned study.
2. A `subject` token provisioned without `site_id` attributes (`AssignedSites = []`) similarly evaluated `SiteAccessEvaluator.CanAccessSite` as `true` and `PaymentsCaller.SiteFilter` as `null`, failing open instead of requiring an explicit site assignment on participant self-service wallet and expense routes.
3. A dual-role principal holding both a sponsor role and `pharmacist` without site assignments was not constrained to `AssignedSites`.

## 2. Decision Drivers & Constraints

- **Fail-Closed Site Isolation (`PRD-SYS-192`, `PRD-CRF-013`, `ADR-2516`, `ADR-2548`):** Site-level clinical roles (`pharmacist`) and trial participants (`subject`) operate strictly at assigned clinical sites and must never default to global cross-site access when `site_id` claims are omitted.
- **Single Source of Truth in `Cadence.Security`:** Downstream authorization handlers (`CallerStudyAndSiteScopeAuthorizationHandler`, `SiteScopeAuthorizationHandler`), shared guards (`ClinicalScopeGuard`, `ExecutionScopeGuard`), and service callers (`PaymentsCaller`) must inherit the rule automatically without bespoke per-endpoint role lists.
- **Blinding & Supply Chain Integrity (`ADR-2640`):** Unblinded site pharmacists hold `unblinded_access` for investigational product dispensing at their own site; missing site claims must fail closed with HTTP `403 Forbidden` before any allocation ciphertext or foreign site kit inventory can be accessed.

## 3. Options Considered

### Option 1: Register `ROLE_PHARMACIST` and `ROLE_SUBJECT` in `SiteAccessEvaluator.SITE_SCOPED_ROLES` (Selected)

- **Overview:** Add `RoleNormalization.ROLE_PHARMACIST` and `RoleNormalization.ROLE_SUBJECT` to `SiteAccessEvaluator.SITE_SCOPED_ROLES` while preserving the existing fail-closed evaluation in `SiteAccessEvaluator.CanAccessSite` (denying `null`/whitespace `siteId` and empty `AssignedSites` whenever `hasSiteScopedRoles` is `true`, while keeping unassigned sponsor roles global).
- **Pros:**
  - Automatically enforces fail-closed site scope across `SiteAccessEvaluator.CanAccessSite`, `ClinicalScopeGuard.IsSiteBound`, `CallerStudyAndSiteScopeAuthorizationHandler`, `SiteScopeAuthorizationHandler`, `ExecutionScopeGuard`, `IpKitEndpoints`, `TrainingEndpoints`, and `PaymentsCaller` (`Reaches`, `SiteFilter`, `InScope`, `IsUnrestricted`) with zero API signature changes.
  - Properly constrains dual-role principals (e.g., `sponsor_dm` + `pharmacist`) to `AssignedSites`.
  - Automatically extends `RouteAuthorizationTests` site-scope probes to `rtsm_supply:read` and `rtsm_supply:write` endpoints.
- **Cons:**
  - Participant test fixtures that previously omitted `SiteIds` on `subject` identities must supply explicit `SiteIds` when exercising site-bound self-service routes.

### Option 2: Add Ad-Hoc Role Checks Inside `IpKitEndpoints` and `PaymentsCaller`

- **Overview:** Check `pharmacist` inside `IpKitEndpoints` and `subject` inside `PaymentsCaller` while leaving `SiteAccessEvaluator.SITE_SCOPED_ROLES` unchanged.
- **Pros:**
  - Limits changes to two services.
- **Cons:**
  - Leaves `SiteAccessEvaluator.CanAccessSite`, `ClinicalScopeGuard.IsSiteBound`, `SubjectEndpoints`, `DeviceEndpoints`, `TrainingEndpoints`, and future endpoints inconsistent and vulnerable to fail-open drift.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing `RoleNormalization.ROLE_PHARMACIST` and `RoleNormalization.ROLE_SUBJECT` in `SiteAccessEvaluator.SITE_SCOPED_ROLES` eliminates the fail-open gap everywhere site scope is evaluated in `Cadence.Security` and all standalone services.

## 5. Consequences & Trade-offs

- **Positive Impact:** Unassigned `pharmacist` and `subject` callers (including aliases `"unblinded_pharmacist"`, `"patient"`, and `"epro"`, as well as dual-role callers) deterministically fail closed (`403 Forbidden` or empty query scope) whenever `AssignedSites` is empty or does not match the target site.
- **Negative Impact / Technical Debt:** None; Keycloak realm provisioning (`KeycloakRealmTenantTests`) and production enrollment tokens already stamp `site_id` for site pharmacists and enrolled participants.
- **Mitigation Strategy:** Covered by unit and endpoint regression suites in `CadenceSecurityUnitTests`, `ExecutionCallerScopeTests`, `RouteAuthorizationTests`, `ParticipantSelfServiceAndOcrTests`, and `PaymentsCallerAuthorizationTests`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/security`, `apps/payments`, `apps/payments/README.md`, `packages/security/README.md`, `tests/Cadence.UnitTests`.
- **Verification Plan:** Verified via `CadenceSecurityUnitTests`, `ExecutionCallerScopeTests`, `DeviceEndpointsTests`, `RouteAuthorizationTests`, `ParticipantSelfServiceAndOcrTests`, `PaymentsCallerAuthorizationTests`, `Cadence.ArchitectureTests`, and `cadence check` quality gates.
