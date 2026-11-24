# ADR-2671: Unify Staff Persona Catalog and Account-Scoped Onboarding

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Traces:** PRD-SYS-050, PRD-SYS-192, ADR-2493, ADR-2551, ADR-2657

---

## 1. Context & Problem Statement

Staff persona definitions, role-lens filtering, orientation copy, setup checklists, workflow launchers, and guided tours were split across independent client modules (`apps/web/src/stores/auth.js`, `apps/web/src/stores/onboarding.js`, `apps/web/src/views/OverviewView.vue`, `apps/web/src/components/OnboardingChecklistDrawer.vue`, and `packages/ui/src/components/clinical/PersonaSwitcher.vue`). As specialist clinical roles (`principal_investigator`, `safety_reviewer`, `pharmacist`, and `unblinded_statistician`) were added to Keycloak (`docker/cadence-realm.json`) and canonical demo fixtures (`packages/demo-fixtures/src/manifest.js`), several divergences emerged:

1. Specialist staff accounts either fell through landing guards to unauthorized `/mdr` routes or collapsed onto generic personas (`pi.site101` onto `site_crc` and `safety.officer` onto `super_admin`).
2. Multi-role accounts holding `Sponsor Admin` alongside domain roles saw `Sponsor Admin` override `#persona-select` orientation lenses in checklists, launchers, and tour filtering.
3. Setup checklist tasks in `OverviewView.vue` and `OnboardingChecklistDrawer.vue` maintained separate task lists and counts, and onboarding keys (`onboarding_*`) in `localStorage` lacked `cadence_user:<tenant>:<user>:` isolation and survived sign-out.
4. Switching personas during an unsaved demo eCRF edit mutated identity before registered draft sources flushed, risking cross-scope loss, while guided tours dropped active `studyCode` across `/ecrf` redirects outside demo mode and overflowed narrow viewports.

## 2. Decision Drivers & Constraints

- **UX-6 Role-as-Lens Integrity (`docs/ux/CLINICAL_CANVAS.md`):** Switching `#persona-select` must focus orientation copy, checklists, launchers, and tour steps on the chosen clinical lens without elevating or revoking granted OIDC token roles (`authStore.normalizedRoles`).
- **GxP Multi-User Browser Hygiene (PRD-SYS-050, PRD-SYS-192):** Orientation progress and landing preferences must isolate per `{ tenantId, userId }` using `userScopedStorage.js` with zero participant PII and purge completely on sign-out.
- **Draft Preservation Across Demo Persona Switches (ADR-2551):** Pending debounced eCRF drafts must flush synchronously under the outgoing user scope before demo persona state mutates.
- **Zero-Emoji & Clinical Design System Compliance:** Shared UI (`PersonaSwitcher.vue`) and orientation surfaces must use `ClinicalIcon` primitives, plain-language clinical labels (UX-1), and `>= 12px` viewport margins for 320px tour popovers.

## 3. Options Considered

### Option 1: Patch Individual Persona Lists and Ad-Hoc LocalStorage Keys

- **Overview:** Add missing specialist entries independently in `auth.js`, `onboarding.js`, `OverviewView.vue`, and `PersonaSwitcher.vue`, and delete `onboarding_*` keys inside `OverviewView.vue`.
- **Pros:**
  - Smaller diff per individual file.
- **Cons:**
  - Keeps duplicated persona definitions, separate checklist counts between `AppShell`, `OnboardingChecklistDrawer`, and `OverviewView`, and unowned `localStorage` keys shared across users on clinical workstations.

### Option 2: Single Staff Persona Catalog and Account-Scoped Onboarding Store (Selected)

- **Overview:** Consolidate all 10 staff personas (`super_admin`, `sponsor_designer`, `data_manager`, `site_crc`, `site_investigator`, `cra_monitor`, `safety_officer`, `auditor`, `pharmacist`, `unblinded_statistician`), UX-6 lens intersection (`resolveLensEffectiveRoles`), orientation copy, shared setup checklists, workflow launchers, and guided tours into `apps/web/src/catalog/staffPersonas.js` paired with `DEFAULT_STAFF_PERSONAS` in `packages/ui`, account-scoped `cadence_user:<tenant>:<user>:onboarding:` persistence, synchronous draft flushing on `switchPersona`, full 11-screen task/tour route authorization, and study-scoped tour navigation.
- **Pros:**
  - Single source of truth for all 11 seeded Keycloak/manifest staff accounts and all 10 clinical staff personas.
  - Synchronizes header badge, setup drawer, and orientation hub checklists from one reactive task array per persona.
  - Isolates onboarding preferences per `{ tenantId, userId }` and guarantees zero leftover `onboarding_*` or `cadence_user:*` keys after sign-out.
- **Cons:**
  - Requires coordinated updates across `docker/cadence-realm.json`, regenerated `packages/demo-fixtures/src/manifest.js`, `packages/ui`, and `apps/web`.

## 4. Decision Outcome

- **Chosen Option:** Option 2.
- **Justification:** Centralizing staff persona metadata, UX-6 lens intersection, and account-scoped onboarding state eliminates drift across Keycloak seeds, demo fixtures, route guards, checklists, and guided tours while upholding clinical workstation storage isolation and draft safety.

## 5. Consequences & Trade-offs

- **Positive Impact:** Every seeded staff account resolves a deterministic persona, authorized landing route, synchronized checklist, and role-appropriate guided tour; switching `#persona-select` immediately adapts orientation surfaces without altering token entitlements; demo persona switches preserve unsaved drafts under the outgoing user scope.
- **Negative Impact / Technical Debt:** Adding a new staff role in the future requires declaring its orientation profile, checklist, launchers, and tour sequence in `staffPersonas.js` and `DEFAULT_STAFF_PERSONAS`.
- **Mitigation Strategy:** Vitest integration suites verify parity across `docker/cadence-realm.json`, `packages/demo-fixtures/src/manifest.js`, `DEFAULT_STAFF_PERSONAS`, `AVAILABLE_SCREENS`, account-scoped storage purge, and viewport tour bounds.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `docker/cadence-realm.json`, `packages/demo-fixtures`, `apps/web`, `docs/adr`.
- **Verification Plan:** Verified via `pnpm --filter ui build && pnpm --filter ui test`, `pnpm --filter web test`, `pnpm -r typecheck`, `pnpm -r lint`, `pnpm cadence check demo-manifest`, `pnpm cadence check adrs`, `pnpm cadence check markdown`, and headless Chromium verification across desktop and narrow viewports.
