# ADR-2653: Adopt ClinicalModal for Electronic Signatures and Reflow Split-Pane Workspaces at 320px

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.1 SC 1.4.10 Reflow and WCAG 2.2 AA dialog focus containment), PRD-SYS-001 (21 CFR Part 11 electronic signature and GxP change-reason dismissal locks)

---

## 1. Context & Problem Statement

Following the initial migration of web overlays onto shared clinical dialog primitives (ADR-2821), three regulated workflow dialogs in `apps/web` — `SignatureCaptureModal.vue`, `TicketSignModal.vue`, and `ReasonModal.vue` — remained on bespoke `.modal-overlay` and `.modal-backdrop` markup wired directly to `useFocusTrap`. On narrow and short viewports (`320x568` and `320x256`, corresponding to WCAG 2.1 SC 1.4.10 400% browser zoom), fixed dialog flex columns and non-wrapping modal footers could clip primary electronic signature and change-reason submission buttons outside the viewport. In addition, the `custom-vue/no-raw-overlays` ESLint rule still exempted raw `.modal-overlay`/`.modal-backdrop` elements whenever `useFocusTrap` was imported in the component script, and split-pane workspaces in `DocumentManagerView.vue` (`/etmf`), `KnowledgeView.vue` (`/knowledge`), and `ExportsSection.vue` (`SHA-256` snapshot digests) overflowed horizontally at `320px`.

## 2. Decision Drivers & Constraints

- **WCAG 2.1 SC 1.4.10 Reflow Across Regulated Modals & Split-Pane Workspaces (PRD-CRF-015):** Electronic signature and GxP reason dialogs must keep action controls reachable within `320x568` and `320x256` viewports without horizontal overflow, and `/etmf`, `/knowledge`, and Study Canvas `ExportsSection` must reflow cleanly within a `320px` CSS viewport (`document.documentElement.scrollWidth <= 320`).
- **21 CFR Part 11 Submission Dismissal Locks & Test Compatibility (PRD-SYS-001):** Migrated signature and reason dialogs must preserve their existing props, events, DOM IDs, ARIA attributes, `defineExpose` contracts, inline mounting (`:teleport="false"`), and in-flight submission locks (`:dismissible="!busy"` and `:dismissible="!loading"`).
- **Zero-Bypass AST Overlay Enforcement:** `custom-vue/no-raw-overlays` must reject new raw `.modal-overlay` and `.modal-backdrop` markup regardless of whether `useFocusTrap` is imported, leaving only the shrink-only legacy allowlist.

## 3. Options Considered

### Option 1: Enhance `ClinicalModal` for Short Viewports, Migrate Signature/Reason Dialogs, Tighten AST Rule, and Reflow 320px Workspaces (Selected)

- **Overview:** Update `ClinicalModal.vue` (`showClose` prop, wrapping `.modal-footer`, compact `@media (max-width: 480px)` chrome, and `@media (max-height: 480px)` dialog-box vertical scrolling), normalize inline disabled-`<Teleport>` comment anchors in `packages/ui/accessibility-matcher.js`, migrate `SignatureCaptureModal.vue`, `TicketSignModal.vue`, and `ReasonModal.vue` onto `ClinicalModal`, remove the `useFocusTrap` exemption from `custom-vue/no-raw-overlays` in `eslint.config.mjs`, and add responsive `320px` single-column reflow and `SHA-256` digest wrapping to `DocumentManagerView.vue` (with `TmfBinderTree.vue` and `DocumentGrid.vue`), `KnowledgeView.vue`, and `ExportsSection.vue`.
- **Pros:**
  - ✅ Centralizes short-viewport scrolling (`max-height: 480px`) and footer wrapping in `ClinicalModal` for all consumers across `apps/web` and `packages/ui`.
  - ✅ Preserves Part 11 dismissal locks, detached Vitest mount behavior (`:teleport="false"`), and `toBeAccessible(wrapper)` axe assertions while closing the `useFocusTrap` lint bypass.
  - ✅ Eliminates horizontal overflow at `320px` on `/etmf`, `/knowledge`, and `ExportsSection`.
- **Cons:**
  - ❌ Requires rebuilding `packages/ui` (`pnpm --filter ui build`) so `apps/web` consumes updated compiled dialog styles and TypeScript definitions.

### Option 2: Patch Individual Scroll Containers on Raw Dialog Wrappers

- **Overview:** Keep bespoke `.modal-overlay` and `.modal-backdrop` wrappers in `SignatureCaptureModal.vue`, `TicketSignModal.vue`, and `ReasonModal.vue` and add per-component `@media (max-height: 480px)` rules while retaining the `useFocusTrap` lint exemption.
- **Pros:**
  - ✅ Avoids touching `packages/ui`.
- **Cons:**
  - ❌ Duplicates short-viewport scrolling CSS across regulated modals, leaves other `ClinicalModal` consumers unimproved at `320x256`, and keeps the `useFocusTrap` raw-overlay bypass open in ESLint.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Solving short-viewport dialog scrolling and footer wrapping once in `ClinicalModal` while migrating the electronic signature and reason dialogs, closing the `useFocusTrap` raw-overlay lint loophole, and reflowing `/etmf`, `/knowledge`, and `ExportsSection` satisfies both PRD-CRF-015 and PRD-SYS-001 with automated unit, AST, and Playwright E2E regression gates.

## 5. Consequences & Trade-offs

- **Positive Impact:** Action controls in `SignatureCaptureModal`, `TicketSignModal`, and `ReasonModal` scroll cleanly into `320x568` and `320x256` viewports; headerless or cancel-only dialogs can suppress the header `✕` control via `:show-close="false"`; inline `:teleport="false"` modals work directly with `expect(wrapper).toBeAccessible()`; and `/etmf`, `/knowledge`, and `ExportsSection` meet WCAG 2.1 SC 1.4.10 at `320px`.
- **Negative Impact / Technical Debt:** Remaining unmigrated legacy overlays stay explicitly enumerated in `LEGACY_WEB_RAW_MODAL_OVERLAY_ALLOWLIST` until migrated in follow-up slices.
- **Mitigation Strategy:** Enforced via `packages/ui/tests/design_system_harmony.test.js`, `eslint.config.mjs` (`custom-vue/no-raw-overlays`), and `apps/web/tests/e2e/web-accessibility.spec.ts`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `eslint.config.mjs`, `docs/components/modal.md`, `docs/adr/index.md`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm --filter ui test`, `pnpm lint`, `pnpm --filter web typecheck`, `pnpm --filter web test`, `pnpm --filter web exec playwright test tests/e2e/web-accessibility.spec.ts`, and Cadence CLI documentation/ADR checks.
