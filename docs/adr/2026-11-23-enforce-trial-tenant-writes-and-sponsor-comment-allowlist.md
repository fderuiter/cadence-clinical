# ADR-2662: Enforce Verified Trial-Tenant Writes Centrally and Switch Ticket Comments to a Sponsor Allowlist

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-TCK-001, PRD-TCK-002, PRD-SYS-053, PRD-SYS-192

---

## 1. Context & Problem Statement

Self-service trial workspaces (`^trial-[0-9a-f]{12}\z`) assign the `trial_coordinator` role so evaluation founders can perform site-side operational workflows alongside `Sponsor Admin` (`sponsor_dm`) without collapsing enterprise separation of duties (`ADR-2823`). Two gaps remained across standalone services and support ticket discussion threads:

1. `PermissionMatrix.Tickets.cs` (via `OperationalStaffRoles`) and other module permission tables grant `trial_coordinator` read and write permissions statically, while `PermissionAuthorizationHandler` evaluated `PermissionMatrix.HasPermission` without checking whether the signed `tenant_id` matched a verified `trial-*` workspace, and `ClinicalScopeGuard.IsSiteBound` treated callers outside `SiteAccessEvaluator.SITE_SCOPED_ROLES` with empty `AssignedSites` as unscoped across sites. Consequently, a `trial_coordinator` in an enterprise tenant without `site_id` claims could list, inspect, create, update, transition, comment on, or sign tickets across sites.
2. `TicketEndpoints.IsSiteRestrictedCommentCaller` and `TicketDetailDrawer.vue` (`canViewInternalSponsorNotes`) used deny-lists (`SiteCommentRestrictedRoles` and `SITE_ONLY_ROLES`) that failed open whenever a non-sponsor role such as `trial_coordinator` was absent from the deny-list, exposing `INTERNAL_SPONSOR` notes and comment audit entries and permitting `INTERNAL_SPONSOR` comment creation.

## 2. Decision Drivers & Constraints

- **Centralized Verified Trial-Tenant Enforcement (`PRD-SYS-053`, `PRD-SYS-192`):** Ignore `trial_coordinator` write grants in `PermissionAuthorizationHandler` unless `principal.TenantId` matches `^trial-[0-9a-f]{12}\z` (`RegexOptions.CultureInvariant`), and treat non-trial `trial_coordinator` callers without an unscoped sponsor or oversight role as site-bound in `ClinicalScopeGuard.IsSiteBound` and `SiteAccessEvaluator.CanAccessSite`.
- **Preserve Tenantless Static Matrix Contracts:** Keep `PermissionMatrix.HasPermission` and `SiteAccessEvaluator.SITE_SCOPED_ROLES` static and tenantless so static matrix and `SubjectAuthorityTests` invariants remain intact while HTTP authorization and scope guards enforce tenant boundaries.
- **Fail-Closed Sponsor Comment Allowlist (`PRD-TCK-001`, `PRD-TCK-002`):** Restrict `INTERNAL_SPONSOR` comment reads, comment writes, comment audit-trail visibility, and `TicketDetailDrawer.vue` filter/radio controls strictly to callers holding a role in `SponsorCommentCapableRoles`.
- **Multi-Role Union Semantics:** A trial founder holding both `Sponsor Admin` and `Trial Coordinator` in a `trial-*` workspace retains full sponsor comment visibility and self-service operational capabilities, and an enterprise caller holding both `Sponsor Admin` and `Trial Coordinator` retains `Sponsor Admin` grants and unscoped sponsor access while `trial_coordinator`-only writes are ignored.

## 3. Options Considered

### Option 1: Per-Endpoint Role and Tenant Checks Inside `TicketEndpoints` Only

- **Overview:** Add bespoke `trial-*` regex checks inside each `TicketEndpoints` handler while leaving `PermissionAuthorizationHandler` and `ClinicalScopeGuard` unchanged.
- **Pros:**
  - Limits code changes to `apps/tickets`.
- **Cons:**
  - Leaves every other standalone service route where `trial_coordinator` holds write permissions exposed to non-trial `trial_coordinator` mutations before handler-specific checks run.
  - Duplicates tenant regex and site-bound scope logic across endpoints.

### Option 2: Centralize `trial-*` Write and Site-Bound Guards in `Cadence.Security` and Switch Ticket Comments to a `SponsorCommentCapableRoles` Allowlist (Selected)

- **Overview:** Add `ClinicalScopeGuard.IsVerifiedTrialTenant(string? tenantId)` (`^trial-[0-9a-f]{12}\z`), filter out `trial_coordinator` write grants for non-trial tenants in `PermissionAuthorizationHandler`, treat non-trial `trial_coordinator` callers without unscoped sponsor/oversight roles as site-bound in `ClinicalScopeGuard.IsSiteBound` and `SiteAccessEvaluator.CanAccessSite`, and replace the fail-open comment deny-lists in `TicketEndpoints.IsSiteRestrictedCommentCaller` and `TicketDetailDrawer.vue` with fail-closed `SponsorCommentCapableRoles` allowlists.
- **Pros:**
  - Enforces verified `trial-*` write admission and non-trial site-bound scoping uniformly across all standalone services wired with `AddCadenceSecurity()`.
  - Closes fail-open `INTERNAL_SPONSOR` comment and audit visibility bypasses for `trial_coordinator` and any future non-sponsor role across both API and web UI.
  - Preserves static `PermissionMatrix` lookups and multi-role union semantics.
- **Cons:**
  - Requires keeping `SPONSOR_COMMENT_CAPABLE_ROLES` in `TicketDetailDrawer.vue` aligned with `TicketEndpoints.SponsorCommentCapableRoles` via canonical backend role normalization (`normalizeBackendRole`).

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Centralizing verified `trial-*` write and site-bound checks in `Cadence.Security` while switching ticket comment visibility to a fail-closed `SponsorCommentCapableRoles` allowlist eliminates cross-site and non-trial mutation bypasses across standalone routes and guarantees separation of sponsor-confidential notes from site and coordinator roles.

## 5. Consequences & Trade-offs

- **Positive:** Non-trial `trial_coordinator` callers without `site_id` claims receive HTTP `403 Forbidden` across ticket reads and mutations, site-assigned non-trial `trial_coordinator` callers can read only their assigned sites' `PUBLIC` ticket data without mutation authority, standalone `trial_coordinator` callers in `trial-*` workspaces see and post `PUBLIC` notes only, and multi-role trial founders (`Sponsor Admin` + `Trial Coordinator`) retain full evaluation capabilities.
- **Negative / Mitigations:** Integration tests verifying non-trial `Trial Coordinator` subject creation (`TrialSubjectWorkflowPostgresTests`) now observe HTTP `403 Forbidden` directly from the authorization middleware before `SubjectEndpoints` executes, matching `Sponsor Admin` route-level refusal (`ADR-2516`).

## 6. Implementation & Verification

- **Affected Components:**
  - `packages/security/src/Rbac/ClinicalScopeGuard.cs`
  - `packages/security/src/Rbac/SiteAccessEvaluator.cs`
  - `packages/security/src/Rbac/AuthorizationHandlers.cs`
  - `apps/tickets/src/Endpoints/TicketEndpoints.cs`
  - `apps/web/src/utils/ticketPermissions.ts`
  - `apps/web/src/components/tickets/TicketDetailDrawer.vue`
- **Verification:**
  - `tests/Cadence.UnitTests/Tickets/TicketEndpointsTests.cs` verifies HTTP `403` across enterprise `trial_coordinator` ticket routes, site-assigned read isolation, standalone `trial-*` `PUBLIC`-only comments and audit trails, and multi-role founder `INTERNAL_SPONSOR` retention.
  - `tests/Cadence.UnitTests/Packages/PermissionPolicyTests.cs` and `CadenceSecurityUnitTests.cs` verify `IsVerifiedTrialTenant`, `IsSiteBound`, `SiteAccessEvaluator.CanAccessSite`, and `PermissionAuthorizationHandler` write-grant filtering across single-role and multi-role principals.
  - `apps/web/tests/views/TicketsView.spec.js` verifies `TicketDetailDrawer.vue` hides `INTERNAL_SPONSOR` filters and radio options for standalone `trial_coordinator` callers while preserving `PUBLIC` comment submission in `trial-*` tenants and sponsor visibility for multi-role founders.
