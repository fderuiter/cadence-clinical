# ADR-2642: Wire Execution RTSM Workflows into Clinical Canvas with Blind-Safe Supply Permissions

- **Status:** Accepted
- **Date:** 2026-11-21
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Issue:** #4833, #5607
- **Requirements:** `PRD-SUB-001` (consent verified before randomisation), `PRD-SUB-003` (stratified block randomisation, encrypted at rest, locked allocation), `PRD-SUB-004` (minimisation, high-entropy random source), `PRD-SSU-003` (site activation gate before randomisation), `PRD-MDR-006` (blinding and separation of duties), ICH E6(R3) 2.8, 21 CFR 11.10(e)
- **Extends:** ADR-2640 (randomisation allocates from approved, encrypted scheme versions), ADR-2516 (route permissions), ADR-2551 (shared signature verification primitives)

---

## 1. Context & Problem Statement

ADR-2640 implemented versioned randomization schemes, encrypted allocation lists, kit-level encrypted treatment arms, consent/site-activation gates, blind-aware subject detail projections, and emergency unblinding in the .NET `Execution` service. However, `apps/web` had not yet wired those endpoints into the Clinical Canvas (`/studies/:studyCode`) or CTMS site supply workflows:

1. Site investigators and coordinators (`ROLE_INVESTIGATOR`, `ROLE_CRC`) could not inspect the approved randomization scheme's stratification factors (`randomization_scheme:read`) when preparing to randomize an `ENROLLED` participant, and clinical/oversight roles could not read blind-safe site kit inventory without holding pharmacist-only `rtsm_supply:read` permissions.
2. `ParticipantStateChange.vue` still treated `RANDOMIZED` as an ordinary dropdown state change (which `Execution` rejects with `409 RANDOMIZATION_REQUIRES_ALLOCATION`) and left emergency unblinding without a step-gated modal.
3. In `ClinicalSubject.cs`, a participant who entered `RECONSENT_REQUIRED` prior to randomization (`RandomizationId is null`) was not domain-blocked from transitioning directly to `ACTIVE`, `UNBLINDED`, or `COMPLETED` without an allocation.

## 2. Decision Drivers & Constraints

- **Blinding & Separation of Duties (`PRD-MDR-006`, ICH E6(R3)):**
  - Scheme drafting and approval remain restricted to `ROLE_UNBLINDED_STATISTICIAN` (`randomization_scheme:write`, `randomization_scheme:approve`) with two-person sign-off (`DraftedBy != actorId`) and step-up signature verification (`X-Sig-Token`).
  - Site kit return scanning and batch disposition (`rtsm_supply:read`, `rtsm_supply:write`) must remain strictly restricted to `ROLE_PHARMACIST` (and system administrators), while blind-safe site inventory inspection (`GET /api/v1/execution/rtsm/kits/inventory/{studyId}/{siteId}`, which omits `TreatmentArmCiphertext`) must be readable by authorized site, monitor, sponsor data-management, pharmacist, and auditor roles.
  - Emergency unblinding must remain gated to `Principal Investigator` and `Authorized ER Physician` with a clinical justification of at least 50 characters.
- **Allocation & Consent Integrity (`PRD-SUB-001`, `PRD-SUB-003`, `PRD-SUB-004`, `PRD-SSU-003`):**
  - Randomization must always flow through `POST /api/v1/execution/subjects/{subjectId}/randomize` under a single-use step-up signature token (`execution.subject.randomize`), never through `PATCH /state`, and unrandomized subjects in `RECONSENT_REQUIRED` must never bypass randomization into post-randomization states.
- **Clinical Canvas Interaction Model (`CLINICAL_CANVAS.md` UX-1, UX-3, §5):**
  - Blinded callers must see `"Randomized, arm masked"` alongside the assigned kit number, while callers carrying gateway-verified `unblinded_access` (or inspecting an `UNBLINDED` subject) see the revealed `treatment_group`.

## 3. Options Considered

### Option 1: Broaden `rtsm_supply:read` and `rtsm_supply:write` to site and sponsor roles

- ✅ Minimal permission-matrix diff.
- ❌ Violates pharmacy separation of duties by letting non-pharmacist roles scan and reconcile returned investigational product kits (`rtsm_supply:write`).

### Option 2: Dedicated `ip_kit_inventory:read` permission, `randomization_scheme:read` for site clinicians, domain pre-randomization guard, and Clinical Canvas RTSM sections (Selected)

- ✅ Preserves `rtsm_supply:read` and `rtsm_supply:write` exclusively for `ROLE_PHARMACIST` (and `ROLE_SYSADMIN`) while granting blind-safe `ip_kit_inventory:read` to clinical, monitoring, data-management, pharmacist, and audit roles, and `randomization_scheme:read` to `ROLE_INVESTIGATOR` and `ROLE_CRC`.
- ✅ Enforces `RequiresRandomizationBefore(target)` in `ClinicalSubject.cs` when `Status == SubjectState.ReconsentRequired && RandomizationId is null`.
- ✅ Surfaces scheme management (`Start -> Randomization schemes`), blind-safe kit supply (`Conduct -> Investigational supply`), informed consent capture, step-up signed participant randomization, blind-masked/unblinded allocation inspection, and emergency unblinding directly inside Clinical Canvas.

### Option 3: Build standalone RTSM pages outside Clinical Canvas

- ❌ Violates `CLINICAL_CANVAS.md` UX-1 (no product-partitioned navigation) and duplicates participant selection and inspector state.

## 4. Decision Outcome

**Chosen option: Option 2.**

- **RBAC & Route Authorization (`packages/security/src/Rbac/PermissionMatrix.Execution.cs`, `apps/execution/src/Endpoints/IpKitEndpoints.cs`):**
  - Grant `randomization_scheme:read` to `ROLE_INVESTIGATOR` and `ROLE_CRC` so `RandomizeParticipantDrawer.vue` can read declared stratification factors and levels without exposing encrypted slots.
  - Introduce `ip_kit_inventory:read` on `GET /api/v1/execution/rtsm/kits/inventory/{studyId}/{siteId}` for `ROLE_SYSADMIN`, `ROLE_SPONSOR_ADMIN`, `ROLE_SPONSOR_DM`, `ROLE_INVESTIGATOR`, `ROLE_CRC`, `ROLE_CRA`, `ROLE_PHARMACIST`, and `ROLE_AUDITOR`. Keep `ip_kit_inventory:seed` restricted to `ROLE_SPONSOR_DM` / `ROLE_SYSADMIN` and keep `rtsm_supply:read` / `rtsm_supply:write` restricted to `ROLE_PHARMACIST` / `ROLE_SYSADMIN`.
- **Domain State Guard (`apps/execution/src/Domain/Subjects/ClinicalSubject.cs`):**
  - Block `TransitionTo` and `Unblind` from `SubjectState.ReconsentRequired` to `Active`, `Unblinded`, or `Completed` whenever `RandomizationId is null`.
- **Keycloak Realm & Canonical Demo Personas (`docker/cadence-realm.json`, `DevelopmentDemoData.cs`, `DemoManifestGeneratorService.cs`):**
  - Provision `unblinded_statistician`, `pharmacist`, `Principal Investigator`, and `Authorized ER Physician` roles, an `unblinded_access` OIDC protocol mapper on `cadence-web`, and synthetic personas `stat.author`, `stat.approver`, and `pharmacist.site101`.
- **Clinical Canvas & CTMS Integration (`apps/web`):**
  - Add `RandomizationSchemesSection.vue`, `SupplySection.vue`, `RandomizeParticipantDrawer.vue`, and `EmergencyUnblindModal.vue`, wire explicit-date consent capture in `CanvasInspector.vue` and `useConsentGating.js`, withhold `RANDOMIZED` and `UNBLINDED` from generic state transitions, render blind-masked or unblinded allocations in `CanvasInspector.vue` and `ParticipantRecord.vue`, and gate returned-kit reconciliation in `SupplySection.vue` and non-demo `CtmsView.vue` to `canReconcileKitInventory` (`pharmacist`).

## 5. Consequences & Trade-offs

- **Positive:** End-to-end RTSM scheme governance, explicit-date consent gating, signed randomization, blind-safe kit supply, pharmacist-only return reconciliation, and audited emergency unblinding work cohesively in Clinical Canvas against both the .NET `Execution` service and isolated demo builds.
- **Negative:** Adds one granular read permission (`ip_kit_inventory:read`) to `PermissionMatrix.Execution.cs` until supply assertions migrate into the clinical kernel host.

## 6. Implementation & Verification

- `ClinicalSubjectTests` and `SubjectApplicationServiceStateTests` verify the pre-randomization `RECONSENT_REQUIRED` transition and unblinding guards.
- `CadenceSecurityUnitTests` and `KeycloakRealmTenantTests` verify separation of duties across `randomization_scheme:*`, `ip_kit_inventory:*`, and `rtsm_supply:*` plus the `unblinded_access` realm mapper and specialist personas.
- `executionApi.test.js`, `contracts.spec.ts`, `StudyCanvasRtsm.spec.js`, and `demo_isolation_stateful_msw.spec.js` verify typed client calls, step-up signature payloads, role gating, WCAG 2.2 AA axe-core compliance, and stateful MSW parity.
