# ADR-2656: Unify Staff and Participant Surface Density Profiles, Portal Clinical Primitives, and Cross-Surface Theme Synchronization

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.2 Level AA contrast, touch target sizing, and plain-language participant usability), PRD-SYS-001 (21 CFR Part 11 electronic signature and offline ePRO integrity)

---

## 1. Context & Problem Statement

Four design-system seams separated the clinical staff workspace (`apps/web`), the mobile participant portal (`apps/subject-portal`), and the VitePress documentation hub (`docs/.vitepress`):

1. **Implicit Density & `!important` Overrides:** `packages/ui/tokens.css` lacked explicit `[data-density="staff"]` and `[data-density="participant"]` token scales, while `packages/ui/responsive.css` relied on `!important` declarations inside `.compact-*` and `.btn-sm` utilities that overrode participant touch targets (`48px`) and prevented clean responsive scaling across tablet staff viewports (`768px-1280px`).
2. **Portal Stylesheet & Primitive Fragmentation:** `apps/subject-portal` aliased `ui` to `packages/ui/dist/index.js` without extracting per-component scoped styles, six participant dialogs (`SignatureModal.vue`, `ReconsentModal.vue`, `PinSetupModal.vue`, `PinUnlockModal.vue`, `QuestionnaireView.vue`, and `WalletView.vue`) plus `SyncDrawer.vue` rendered raw `.modal-overlay` and `.global-drawer` containers exempted from `custom-vue/no-raw-overlays` whenever `useFocusTrap` was imported, and `QuestionnaireView.vue` built survey controls through imperative HTML string concatenation in `apps/subject-portal/src/widgets/clinical-fields.js`.
3. **Internal Clinical Identifier Leaks in Participant Copy:** Participant consent, session status, site support, audit receipt, and survey views exposed internal trial identifiers (`CADENCE-101`, `v1.0`, `SUBJ-101-001`, `(SITE-101)`), raw 64-character SHA-256 hex digests, and clinician-facing headings (`Enter Clinical Findings`).
4. **Disconnected Demo & Documentation Surfaces:** `docs/.vitepress/theme` maintained standalone colors and fonts without importing `packages/ui/tokens.css` or sharing theme persistence (`vitepress-theme-appearance` / `cadence-theme`) with `/demo/` and `/subject-portal/`, while `CtmsDemoWorkspace.vue` and `EcrfDemoWorkspace.vue` carried inline styles and Unicode dingbats.

## 2. Decision Drivers & Constraints

- **Two-Profile Clinical Ergonomics (PRD-CRF-015):** Clinical staff require compact `52px` top chrome, `1px` borders, `tabular-nums` alignment, `44px` tablet hit areas (`768px-1280px`), and `>= 16px` coarse/tablet input text, whereas mobile participants require spacious `>= 48px` interactive targets, `>= 16px` input text, `>= 12px` minimum caption text, and plain 6th-grade copy free of internal study/site/subject codes or cryptographic digests.
- **21 CFR Part 11 & Offline ePRO Continuity (PRD-SYS-001):** Migrating modals, drawers, and survey fields onto shared `packages/ui` Vue primitives must preserve step-up signature flows, PIN-wrapped AES-GCM IndexedDB draft persistence, and strict demo state isolation without altering backend `.NET` services, database schemas, or OpenAPI contracts.
- **Zero-Exception Static Governance:** Removing the `useFocusTrap` loophole in `custom-vue/no-raw-overlays` ensures no new raw `.modal-overlay` or `.global-drawer` markup can enter `apps/subject-portal` or migrated `apps/web` components.

## 3. Options Considered

### Option 1: Declarative `[data-density]` Token Scales, Shared `packages/ui` Primitives (`ClinicalModal`, `ClinicalDrawer`, `ClinicalSlider`), and `useSurfaceTheme` Bridge (Selected)

- **Overview:** Define `Inter` (`cv05`, `cv08`, `tabular-nums`), `JetBrains Mono` (`--font-mono`), a `12px` global font floor, and `[data-density="staff"]` / `[data-density="participant"]` token scales in lines 1-240 of `packages/ui/tokens.css`; replace `!important` compact rules in `packages/ui/responsive.css` with density custom properties; export `ClinicalSlider` and `useSurfaceTheme` from `packages/ui/index.js`; resolve `ui` from `packages/ui/index.js` in `apps/subject-portal/vite.config.js` so Vite extracts only imported `packages/ui` SFC scoped styles, migrate all six portal modals onto `ClinicalModal`, `SyncDrawer.vue` onto `ClinicalDrawer`, and `QuestionnaireView.vue` onto declarative `ClinicalInput`, `ClinicalRadioGroup`, and `ClinicalSlider` while deleting `widgets/clinical-fields.js` and removing the `useFocusTrap` exemption in `eslint.config.mjs`; rewrite participant copy to plain language; apply `data-density="staff"` in `apps/web/src/App.vue`, refactor `CtmsDemoWorkspace.vue` and `EcrfDemoWorkspace.vue` onto shared tokens and `lucide-vue-next` icons, and bridge `docs/.vitepress/theme/{index.js,custom.css}` to `packages/ui/tokens.css` with synchronized light/dark persistence.
- **Pros:**
  - Single token contract governs VitePress docs, `/demo/` staff workspaces, and `/subject-portal/` without `!important` specificity wars or local CSS overrides.
  - Eliminates imperative HTML field strings (`clinical-fields.js`) and raw overlay exemptions while keeping encrypted IndexedDB drafts and Part 11 signatures intact.
- **Cons:**
  - Requires synchronous Vue render flushing on legacy imperative portal entrypoints (`startQuestionnaire`, `openSignatureModal`, `loadConsentDetails`) called synchronously by unit integration tests.

### Option 2: Per-Application Stylesheet Overrides and Standalone Portal Overlay Markup

- **Overview:** Keep separate compact/spacious CSS overrides inside `apps/web` and `apps/subject-portal` and retain raw `.modal-overlay` / `clinical-fields.js` helpers in `apps/subject-portal`.
- **Pros:**
  - Smaller component diff inside `apps/subject-portal`.
- **Cons:**
  - Perpetuates `!important` specificity collisions, leaves raw overlay loopholes open in ESLint, and keeps VitePress and demo workspaces visually and thematically out of sync.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Encoding staff and participant ergonomics declaratively in `packages/ui/tokens.css` and `packages/ui/responsive.css` while standardizing overlays, survey controls, Lucide iconography, plain participant copy, and persisted theme state on `packages/ui` establishes one auditable design-system contract across every frontend surface.

## 5. Consequences & Trade-offs

- **Positive Impact:** Uniform WCAG 2.2 AA contrast (`>= 4.5:1` text, `>= 3:1` control boundaries), `>= 48px` participant touch targets, `44px` tablet staff hit areas, `>= 16px` touch inputs preventing mobile browser auto-zoom, zero raw overlays or leaked clinical identifiers in `apps/subject-portal`, and seamless light/dark mode continuity across docs, `/demo/`, and `/subject-portal/`.
- **Negative Impact / Technical Debt:** Legacy pre-ADR-2821 `apps/web` overlays remain explicitly enumerated in `eslint.config.mjs` until their scheduled incremental `ClinicalModal` / `ClinicalDrawer` migration completes.
- **Mitigation Strategy:** Enforce `design_system_harmony.test.js`, `portal-density-and-copy.test.js`, ESLint AST rules, and Playwright `axe-core` E2E checks in CI.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `packages/usdm-schemas`, `apps/subject-portal`, `apps/web`, `docs/.vitepress/theme`, `eslint.config.mjs`, `docs/adr/index.md`, `docs/DOCUMENTATION_INDEX.md` (including isolating `usdm-schemas/epro` and tree-shaking `packages/ui` SFC styles via `packages/ui/index.js` so the staged `/subject-portal/assets/` bundle stays well under the ADR-2537 `920 KiB` budget).
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm lint`, `pnpm --filter web typecheck`, `pnpm test`, `pnpm docs:build`, `pnpm docs:stage-demos`, Playwright accessibility/theme E2E checks, and `pnpm cadence check`.
