# ADR-2672: Centralize CSS Button, Disclosure, and Programmatic Focus States and Migrate Live Clinical Hubs onto Shared UI Primitives

- **Status:** Accepted
- **Date:** 2026-11-24
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.2 Level A & AA SC 1.3.1 Info and Relationships, SC 1.4.3 Contrast Minimum, SC 1.4.11 Non-text Contrast, SC 2.1.1 Keyboard, SC 2.4.7 Focus Visible, SC 2.5.8 Target Size Minimum, and SC 4.1.2 Name, Role, Value compliance)

---

## 1. Context & Problem Statement

Across `packages/ui`, `apps/web`, and `apps/subject-portal`, interactive state rules and clinical dataset/field primitives exhibited four systemic gaps:

1. **Unguarded Button Disabled States & Hover Bleed:** `packages/ui/tokens.css`, `apps/web/src/style.css`, `apps/subject-portal/src/style.css`, and `packages/ui/src/components/clinical/ClinicalModal.vue` omitted `.btn:disabled`, `.btn[aria-disabled="true"]`, and `:not(:disabled)` hover guards, leaving disabled buttons with `cursor: pointer` and active hover fills on non-dismissible confirmation dialogs and gated clinical forms.
2. **Undersized `<summary>` Disclosure Targets (WCAG 2.2 SC 2.5.8):** `packages/ui/tokens.css` excluded `summary` from the `24px` `--semantic-control-target-min` selector, leaving `<summary>` disclosures in `SupportDetails.vue`, `EcrfFormOpener.vue`, and `apps/subject-portal/src/style.css` at `18px-19.69px` height.
3. **Outlined Programmatic `tabindex="-1"` Landmarks & Skipped Portal Focus Lint (WCAG 2.2 SC 2.4.7):** `[tabindex]:focus-visible` in `packages/ui/tokens.css` matched `tabindex="-1"` landmarks and headings (`<main id="main-content" tabindex="-1">`, `<h2 tabindex="-1">`), drawing `3px` focus halos on programmatic route/inline focus handoffs, while `eslint.config.mjs` skipped `custom-vue/no-suppressed-focus-outline` across `apps/subject-portal`.
4. **Under-Adopted Shared Table & Field Primitives:** Live Clinical Canvas sections, CTMS/RTSM workspaces, eTMF/Documents hubs, Tickets views, and `apps/subject-portal/src/views/ComplianceView.vue` rendered bespoke `<table>` and `<label>`/`<input>`/`<select>` markup instead of `ClinicalDataTable`, `ClinicalFieldLayout`, and `ClinicalInput`, lacking static ESLint AST ratchets against raw tables and unguarded buttons.

## 2. Decision Drivers & Constraints

- **Centralized Accessible Interactive States:** Guarantee `cursor: not-allowed` and suppressed hover highlights on disabled `.btn` and modal controls, `>= 24px` height on all `<summary>` disclosures (`>= 48px` on participant portal touch cards), `outline-style: none` on programmatic `tabindex="-1"` landmarks/headings, and `>= 6.34:1` two-color `:focus-visible` halos on interactive elements ([ADR-2646](2026-11-22-standardize-24px-target-tokens-and-dual-focus-rings.md)).
- **Shared Primitive Ergonomics Across Staff & Participant Profiles:** Preserve `48px` (`--semantic-touch-target-min`) touch targets in `apps/subject-portal` ([ADR-2657](2026-11-23-share-ui-primitives-and-query-synced-portal-routes.md)) and `16px` minimum input font sizes on coarse/mobile viewports while supporting compact `36px` staff controls, `<select>`/`<textarea>` tags, external/server pagination, and masked/non-interactive rows in `ClinicalDataTable` ([ADR-2656](2026-11-23-clinical-data-table-keyboard-and-aria-compliance.md)).
- **Strict Bundle, Matrix Exemption, and Design Token Guardrails:** Keep initial cold-boot JS in `apps/web` within `<= 515 KiB` raw and `<= 167 KiB` gzip, exempt 2D multi-header clinical matrices (`ClinicalSoAMatrix.vue`, `SoAMatrixEditor.vue`, `RepeatingLogGridWidget.vue`, `RactMatrixViewer.vue`, `ParticipantFormsPanel.vue`, `DocumentGrid.vue`, `OncologyCalipers.vue`), introduce zero Unicode emojis or `--semantic-color-ai*` tokens on non-AI controls, and leave shell geometry and backend logic untouched.

## 3. Options Considered

### Option 1: Centralize CSS State Selectors in `packages/ui`, Extend `ClinicalDataTable` and `ClinicalFieldLayout` / `ClinicalInput`, Migrate Live Clinical Hubs, and Enforce ESLint AST Ratchets (Selected)

- **Overview:**
  1. Add `.btn:disabled`, `.btn[aria-disabled="true"]`, and `:not(:disabled)` hover guards across `packages/ui/tokens.css`, `apps/web/src/style.css`, `apps/subject-portal/src/style.css`, and `ClinicalModal.vue`; include `summary` in `--semantic-control-target-min` (`>= 24px`, and `>= 48px` on `.audit-details summary` in `apps/subject-portal`); append `:not([tabindex="-1"])` to `[tabindex]:focus-visible` and suppress `:focus-visible` outlines on programmatic non-interactive `[tabindex="-1"]` landmarks/headings; and enable `custom-vue/no-suppressed-focus-outline` on `apps/subject-portal`.
  2. Extend `ClinicalDataTable.vue` with `paginationMode` (`"client"` | `"external"` | `"none"`), `totalCount`, `page` (`update:page`), `sortBy` / `sortDirection` (`sort-change`), `rowInteractive` / `rowClass`, `tableClass` / `ariaLabel`, `#head-<key>`, `#masked-row`, and `#pagination` slots while preserving [ADR-2656](2026-11-23-clinical-data-table-keyboard-and-aria-compliance.md) keyboard sort and polite status semantics. Extend `ClinicalFieldLayout.vue` and `ClinicalInput.vue` with `density` (`"touch"` | `"compact"`), `required`, `hint`, and `tag` (`"input"` | `"select"` | `"textarea"`) with `16px` coarse/mobile input font sizing.
  3. Migrate live Clinical Canvas (`ParticipantsSection`, `SitesSection`, `AddParticipantForm`, `ProtocolVersionsSection`, `LocksSection`, `QueriesSection`), CTMS/RTSM (`CtmsSectionScopeBar`, `CtmsGreenlightSection`, `CtmsMilestonesSection`, `CtmsMonitoringSection`, `CtmsDelegationSection`, `RtsmSupplySection`, `SiteActivationPanel`, `BulkIpKitReconciliationDrawer`), eTMF/Documents (`CraQcQueue`, `BatchUploadDrawer`, `DocumentsHubView`, `DocumentManagerView`), Tickets (`TicketsView`, `TicketCreateModal`, `TicketSignModal`, `TicketDetailDrawer`), and `apps/subject-portal/src/views/ComplianceView.vue` onto shared primitives, and enforce `custom-vue/no-raw-clinical-table` and `custom-vue/no-unguarded-button-state` in `eslint.config.mjs`.
- **Pros:**
  - Eliminates disabled-button cursor/hover defects, undersized `<summary>` targets, and programmatic `tabindex="-1"` focus rings from one shared CSS layer.
  - Standardizes keyboard-accessible tables and labeled form fields across live staff hubs and participant compliance views without exceeding cold-boot JS budgets.
  - Prevents regressions in CI via shrink-only ESLint AST ratchets per [ADR-2659](2026-11-23-centralize-clinical-tabs-variants-overflow-and-scoped-css-gates.md).
- **Cons:**
  - Requires maintaining shrink-only legacy allowlists in `eslint.config.mjs` for unmigrated secondary/demo consoles until those consoles are migrated or retired.

### Option 2: Patch Button, Summary, and Focus Styles Locally per View Without Shared Primitive Extensions

- **Overview:** Add ad-hoc `:disabled`, `summary`, and `tabindex="-1"` CSS overrides in individual `apps/web` and `apps/subject-portal` views while keeping raw `<table>` and `<input>` markup.
- **Pros:**
  - Avoids modifying `ClinicalDataTable.vue`, `ClinicalFieldLayout.vue`, `ClinicalInput.vue`, and `packages/ui/index.d.ts`.
- **Cons:**
  - Duplicates accessibility and state rules across dozens of SFCs, leaves `ClinicalDataTable` and `ClinicalFieldLayout` unused in live clinical hubs, and provides no static lint gate against regressions.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing disabled button states, `24px` `<summary>` target floors, and `:not([tabindex="-1"])` focus selectors in `packages/ui/tokens.css` while migrating live clinical hubs onto `ClinicalDataTable`, `ClinicalFieldLayout`, and `ClinicalInput` resolves the WCAG 2.2 SC 1.3.1, SC 2.4.7, SC 2.5.8, and SC 4.1.2 gaps structurally and locks them with ESLint AST ratchets.

## 5. Consequences & Trade-offs

- **Positive Impact:** Disabled `.btn` and modal footer controls consistently render `cursor: not-allowed` with suppressed hover states; `<summary>` disclosures meet `>= 24px` (`>= 48px` on portal touch cards); programmatic focus on `<main tabindex="-1">` and `<h2 tabindex="-1">` renders no focus ring while interactive controls retain `>= 6.34:1` two-color `:focus-visible` halos; and live Canvas, CTMS, eTMF, Documents, Tickets, and Subject Portal compliance views share accessible table and field primitives enforced by ESLint.
- **Negative Impact / Technical Debt:** Legacy/demo `apps/web` consoles listed in `LEGACY_WEB_RAW_TABLE_ALLOWLIST` and `LEGACY_WEB_UNGUARDED_BUTTON_ALLOWLIST` remain to be migrated in subsequent slices.
- **Mitigation Strategy:** Both allowlists in `eslint.config.mjs` are shrink-only ratchets; any newly authored or migrated `.vue` or `.css` file is immediately gated by `custom-vue/no-raw-clinical-table`, `custom-vue/no-unguarded-button-state`, and `custom-vue/no-suppressed-focus-outline`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `eslint.config.mjs`, `docs/components/index.md`, `docs/components/data-table.md`, `packages/ui/README.md`, `docs/adr/index.md`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm --filter ui test` (`ClinicalDataTable.test.js`, `shared_components.test.js`, `design_system_harmony.test.js`), `pnpm exec eslint --max-warnings 0 .`, `pnpm -r typecheck`, `pnpm --filter subject-portal test`, `pnpm --filter web test` (including `cold_boot_bundle.spec.ts`), `PLAYWRIGHT_CHROME_EXECUTABLE=/usr/bin/google-chrome pnpm --filter web exec playwright test --project=accessibility`, headless Chromium computed-style checks, and `cadence check` ADR/markdown/README gates.
