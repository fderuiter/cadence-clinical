# ADR-2658: Unify Shared Persona Catalog, Account-Scoped Onboarding State, and Responsive Tour Anchoring

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (21 CFR Part 11 multi-tenant session isolation, realm role parity, and draft-safe account transitions), PRD-CRF-015 (WCAG 2.2 Level AA responsive viewport clamping and accessible workspace orientation)

---

## 1. Context & Problem Statement

Persona metadata, quick-start checklists, and guided tour sequences had drifted across five client definitions (`apps/web/src/stores/auth.js`, `packages/ui/src/components/clinical/PersonaSwitcher.vue`, `apps/web/src/views/OverviewView.vue`, `apps/web/src/stores/onboarding.js`, and `apps/web/src/router/index.js`) while `docker/cadence-realm.json` seeded eight clinical personas (`super_admin`, `sponsor_designer`, `data_manager`, `site_crc`, `site_investigator`, `cra_monitor`, `safety_officer`, `auditor`). As a result, `site_investigator` (`pi.site101`) and `safety_officer` (`safety.officer`) collapsed onto coordinator and data-manager realm roles, fell back to `super_admin` orientation copy, or landed on `/forbidden` when `bypassOverview` was enabled. In addition, `/overview` and `OnboardingChecklistDrawer.vue` maintained disconnected checklist lists, `onboarding.js` persisted unscoped `onboarding_<persona>_*` keys without tenant/user claims or logout cleanup, switching demo personas within the `500ms` eCRF auto-save window dropped pending drafts, and `OnboardingTour.vue` targeted legacy `.subject-selector-card` / `.ecrf-form-renderer` selectors while clipping popovers on `<860px` viewports.

## 2. Decision Drivers & Constraints

- **Single Canonical Eight-Persona Lens Contract (UX-6 / PRD-SYS-001):** All eight seeded clinical personas must share one frozen catalog in `packages/ui` with role requirements, titles, descriptions, landing routes, checklist tasks, and opt-in tour steps, while lens switching intersects active persona roles with verified token roles (except `super_admin`) so a presentation lens never elevates authority.
- **Account-Scoped Browser Persistence & Draft Safety (PRD-SYS-001):** Orientation state must follow `cadence_user:<tenant>:<user>:onboarding:<persona>:<field>` via `userScopedStorage.js`, write nothing when tenant or user claims are absent, purge account-scoped and legacy `onboarding_*` keys plus in-memory Pinia state on `authStore.logout()`, and synchronously flush `useEcrfForm.ts` / `useEcrfDrafts.js` before demo persona switches.
- **Live DOM Tour Anchoring & Responsive Viewport Clamping (PRD-CRF-015):** Guided tours must remain strictly opt-in on sign-in, target live `/ecrf` DOM anchors (`#ecrf-participant-bar` and `#ecrf-form-instance`) across live and demo builds, remap `<860px` `#nav-*` steps to `#btn-mobile-menu-toggle`, and clamp popover coordinates inside `12px` horizontal and vertical viewport margins from `375px` to `1440px` using semantic design tokens.

## 3. Options Considered

### Option 1: Export Frozen `PERSONA_CATALOG` from `packages/ui`, Seed Dedicated PI/Safety Realm Roles, Scope Onboarding Under `cadence_user:<tenant>:<user>:`, and Clamp Tour Popovers (Selected)

- **Overview:** Define `PERSONA_CATALOG`, `PERSONA_CATALOG_BY_KEY`, and `getPersonaCatalogEntry` in `packages/ui/src/catalog/personas.js`; consume it across `PersonaSwitcher.vue`, `auth.js`, `onboarding.js`, `router/index.js`, `OverviewView.vue`, `OnboardingChecklistDrawer.vue`, and `ContextualHelpDrawer.vue`; seed `"Principal Investigator"` (`pi.site101`) and `"Safety Reviewer"` (`safety.officer`) in `docker/cadence-realm.json` and regenerate `packages/demo-fixtures/src/manifest.js`; intersect active persona roles with user realm roles via `resolveEffectivePersonaRoles`; call `flushRegisteredDraftSourcesSync()` prior to demo persona mutation and re-scope active eCRF drafts on scope transitions; bind `/overview`, `AppShell.vue`, and `OnboardingChecklistDrawer.vue` to one `onboardingStore` task list under `cadence_user:<tenant>:<user>:onboarding:`; and update `OnboardingTour.vue` and `CrcFormRenderer.vue` for live `/ecrf` anchors, `<860px` mobile toggle remapping, and `12px` viewport margin clamping.
- **Pros:**
  - Eliminates multi-file persona drift and gives `site_investigator` and `safety_officer` deterministic Keycloak/demo parity, authorized `bypassOverview` landings, and dedicated checklists and tours.
  - Guarantees zero cross-account onboarding or draft leakage on shared workstations and keeps `/overview`, the shell badge, and the checklist drawer synchronized.
- **Cons:**
  - Existing unit tests that asserted legacy unscoped `onboarding_<persona>_*` keys without `tenantId`/`sub` claims must sign in with scoped account claims to exercise `localStorage` persistence.

### Option 2: Patch Individual Views Locally Without Consolidating `packages/ui` or Account-Scoping `onboarding.js`

- **Overview:** Add `site_investigator` and `safety_officer` branches separately in `OverviewView.vue`, `PersonaSwitcher.vue`, and `onboarding.js` while retaining `onboarding_<persona>_*` keys.
- **Pros:**
  - Fewer touched files in `packages/ui`.
- **Cons:**
  - Preserves five-way catalog duplication, leaves cross-account `onboarding_*` state on shared workstations after logout, and fails to synchronize `/overview` and drawer task counts.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing `PERSONA_CATALOG` in `packages/ui`, aligning `docker/cadence-realm.json` and generated demo fixtures, enforcing `UX-6` role intersection, flushing drafts before demo switches, and scoping onboarding keys under `cadence_user:<tenant>:<user>:onboarding:` resolves persona drift, route-guard mismatches, shared-workstation leakage, and responsive tour clipping at their architectural boundaries.

## 5. Consequences & Trade-offs

- **Positive Impact:** Every client surface reads one immutable eight-persona catalog; multi-role users narrowing their lens to `auditor` see read-only audit/study launchers and tasks without losing underlying token claims; demo switches within `500ms` of typing preserve eCRF drafts per demo account; and guided tours anchor cleanly from `375px` mobile screens to `1440px` desktops.
- **Negative Impact / Technical Debt:** Browser orientation preferences remain local to each browser profile (`cadence_user:<tenant>:<user>:onboarding:*`) rather than roaming across devices via server-side user preference endpoints.
- **Mitigation Strategy:** `DemoManifestGeneratorTests`, `KeycloakRealmTenantTests`, `AdrValidatorService`, and Vitest suites across `packages/ui` and `apps/web` enforce realm-to-manifest parity, `UX-6` lens intersection, account-scoped storage isolation, `<500ms` draft flushing, and responsive tour clamping.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `packages/demo-fixtures`, `docker/cadence-realm.json`, `apps/web`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `dotnet run --project packages/cli/src/Cadence.Cli.csproj -- generate-demo-manifest`, `dotnet test`, `pnpm --filter ui build`, `pnpm --filter ui test`, `pnpm --filter web typecheck`, `pnpm --filter web test`, `pnpm lint`, `pnpm build`, `pnpm build:demo`, and `pnpm cadence check`.
