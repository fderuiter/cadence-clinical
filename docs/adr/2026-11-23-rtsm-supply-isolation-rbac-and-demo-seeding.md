# ADR-2833: Isolate RTSM Supply Loads, Align Unblinded Statistician and Trial Coordinator RBAC, and Seed Demo Randomization

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SUB-001, PRD-SUB-003, PRD-SYS-004, PRD-SYS-192, ICH E6(R3) 4.2, 21 CFR 11.10(g)
- **Extends:** [ADR-2510](2026-09-30-endpoint-permissions-from-the-permission-matrix.md), [ADR-2516](2026-10-16-routes-are-denied-unless-they-declare-who-may-call-them.md), [ADR-2640](2026-10-20-randomisation-allocates-from-approved-encrypted-scheme-versions.md), [ADR-2823](2026-11-22-self-service-trial-site-workflows-and-scoped-trial-coordinator-enrollment.md)

---

## 1. Context & Problem Statement

In `RtsmSupplySection.vue`, loading the RTSM & Investigational Supply workspace unconditionally executed `Promise.all` across cell and gene therapy (CGT) chain-of-custody routes (`cgt_chain_of_custody:read`), site IP kit inventory (`rtsm_supply:read`), and randomization schemes (`randomization_scheme:read`). Because `unblinded_statistician` holds randomization scheme authority without CGT custody grants, opening the RTSM workspace triggered an unhandled HTTP `403 Forbidden` from CGT routes that aborted randomization scheme loading and blocked two-person scheme approval.

In addition:

- `unblinded_statistician` lacked `subject_enrollment:read` in `PermissionMatrix.Execution.cs`, preventing study participant listing, counts, and ADR-2640-governed unblinded allocation detail inspection (`SubjectEndpoints.GetSubjectDetailAsync`).
- `trial_coordinator` lacked `cgt_chain_of_custody:read` in `PermissionMatrix.Execution.cs`.
- Operational route guards (`/ctms`, `/ssu`) and workspace persona switchers (`auth.js`, `demoPolicy.js`) omitted `unblinded_statistician`, `pharmacist`, and `trial_coordinator`.
- `ExecutionDbSeeder.cs` seeded `CADENCE-101` site compliance and a baseline subject in `cadence-local` without an approved `RandomizationScheme` or `ACTIVE_AT_SITE` `IpKitInventoryItem` rows, leaving pre-seeded local demo studies unable to randomize participants immediately.

## 2. Decision Drivers & Constraints

- **Treatment blinding & separation of duties (ADR-2640, 21 CFR 11.10(g)):** Blinded roles must never receive revealed arm or kit values from `SubjectEndpoints`, two-person electronic signature separation (`stat.author` != `stat.approver`) must remain enforced, and `unblinded_statistician` must remain excluded from `cgt_chain_of_custody:read`.
- **Subpanel fault and permission isolation:** Each RTSM supply subpanel (CGT custody, site IP kits, randomization schemes) must gate requests on its own matrix capability and isolate load errors (`role="alert"`) without clearing or blocking sibling subpanels.
- **Tenant-isolated, GxP-audited demo seeding (PRD-SYS-192, ADR-2446, K-TIME-3):** Demo randomization scheme, encrypted list slots, and `ACTIVE_AT_SITE` IP kits must be seeded strictly inside `cadence-local` using deterministic `IClock` timestamps and `IAllocationProtector` encryption, writing zero unhashed audit rows directly and relying solely on `GxPAuditInterceptor`.

## 3. Options Considered

### Option 1: Isolate RTSM supply subpanel loads, align least-privilege RBAC & route guards, and seed `cadence-local` randomization fixtures (Selected)

- **Overview:**
  1. Add `canReadChainOfCustody` (`cgt_chain_of_custody:read`) in `apps/web/src/canvas/authority/supply.ts`; gate CGT panels and `#rtsm-subject` in `RtsmSupplySection.vue` on `canReadChainOfCustody`, and isolate CGT, IP kit, and randomization scheme loaders with per-subpanel `role="alert"` error banners.
  2. Grant `subject_enrollment:read` to `RoleNormalization.ROLE_UNBLINDED_STATISTICIAN` and `cgt_chain_of_custody:read` to `RoleNormalization.ROLE_TRIAL_COORDINATOR` in `PermissionMatrix.Execution.cs` (keeping `unblinded_statistician` excluded from `cgt_chain_of_custody:read`), then regenerate `apps/web/src/canvas/generated/permissions.ts`.
  3. Include `unblinded_statistician`, `pharmacist`, and `trial_coordinator` in `/ctms` and `/ssu` route guards, canvas navigation helpers, and `auth.js` / `demoPolicy.js` persona presets.
  4. Extend `ExecutionDbSeeder.cs` to idempotently seed an approved baseline `permuted_block` `RandomizationScheme` (`PLACEBO:1`, `ACTIVE:1`), encrypted `RandomizationListSlot` rows, and encrypted `ACTIVE_AT_SITE` `IpKitInventoryItem` rows for `CADENCE-101` strictly in `cadence-local`.
- **Pros:**
  - Preserves least-privilege separation between statistical randomization governance, pharmacy kit reconciliation, and CGT custody tracking.
  - Prevents any single subpanel failure or missing permission from degrading permitted sibling workflows.
  - Enables out-of-the-box local demo randomization in `cadence-local` with zero impact on customer or self-service trial tenants.
- **Cons:**
  - Requires keeping frontend generated permission maps and MSW demo policy rules synchronized with `PermissionMatrix.Execution.cs`.

### Option 2: Grant broad CGT and IP kit read permissions to `unblinded_statistician`

- **Overview:** Keep monolithic `Promise.all` loading in `RtsmSupplySection.vue` and grant `cgt_chain_of_custody:read` and `rtsm_supply:read` to `unblinded_statistician`.
- **Pros:**
  - Fewer frontend component changes.
- **Cons:**
  - Violates least-privilege clinical role boundaries by exposing autologous/allogeneic chain-of-custody records and site pharmacy inventory reconciliation to statistical personnel.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **Justification:** Option 1 resolves the RTSM workspace failure at its root by aligning UI subpanel data loading with backend route capabilities, granting only the exact subject-read and trial-coordinator custody-read permissions required by clinical workflows, and seeding encrypted baseline randomization fixtures strictly inside `cadence-local`.

## 5. Consequences & Trade-offs

- **Positive Impact:** Unblinded statisticians (`stat.author`, `stat.approver`) can open `/ctms?section=supply`, draft and approve randomization schemes with two-person Part 11 signatures, and inspect participant enrollment and unblinded allocations without touching CGT custody endpoints; pharmacists and trial coordinators navigate `/ctms` and `/ssu` cleanly; pre-seeded `CADENCE-101` demo environments support immediate participant randomization.
- **Negative Impact / Technical Debt:** Relational `ExecutionDbSeeder` runs require an `IAllocationProtector` (`RANDOMIZATION_ALLOCATION_KEY`) and ambient audit attribution (`ServiceAccounts.DbSeeder` via `AmbientAuditActorScope` in `--seed`).
- **Mitigation Strategy:** `Program.cs --seed` opens `AmbientAuditActorScope.BeginScope(ServiceAccounts.DbSeeder.UserId, ServiceAccounts.DbSeeder.ChangeReason)` and passes `IAllocationProtector`, while `ExecutionDbSeeder` resolves `IAllocationProtector` from the `DbContext` service provider for existing integration test callers.

## 6. Implementation & Verification

- **Affected Packages / Services:** `packages/security` (`PermissionMatrix.Execution.cs`), `apps/execution` (`ExecutionDbSeeder.cs`, `Program.cs`), `packages/kernel` (`KernelProtocolGovernanceEndpoints.cs`), `apps/web` (`supply.ts`, `permissions.ts`, `RtsmSupplySection.vue`, `auth.js`, `demoPolicy.js`, `router/index.js`, `router/availability.js`, `palette.ts`, `onboardingSummary.js`, `OverviewView.vue`), `packages/demo-fixtures` (`index.js`).
- **Verification Plan:**
  - `SubjectRouteAuthorityTests`, `RandomizationPostgresTests`, and `ExecutionTenantIsolationPostgresTests` verify `unblinded_statistician` subject list/count/unblinded detail reads, blinded-role arm masking, `trial_coordinator` CGT custody reads, `unblinded_statistician` CGT `403` rejection, `cadence-local`-only scheme/kit seeding, Merkle audit chain integrity, and immediate participant randomization.
  - `StudyCanvasRtsmWorkflows.spec.js` and `contracts.spec.ts` verify permission-gated subpanel loading, role-accurate `403` API mocks, scoped subpanel error alerts, two-person scheme approval, and `/ctms` + `/ssu` persona routing.
