# ADR-2659: Centralize `ClinicalTabs` Visual Variants, Overflow Containment, Focus Scroll Alignment, and Scoped CSS AST Gates

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.2 Level A & AA SC 1.4.3 Contrast Minimum, SC 1.4.10 Reflow, SC 1.4.11 Non-text Contrast, SC 2.1.1 Keyboard, SC 2.4.7 Focus Visible, SC 2.4.11 Focus Not Obscured Minimum, SC 2.5.8 Target Size Minimum, and SC 4.1.2 Name, Role, Value compliance)

---

## 1. Context & Problem Statement

Following the adoption of `ClinicalTabs` across seven operational workspaces in [ADR-2652](2026-11-23-clinical-tabs-focus-fallbacks-and-hub-a11y-gates.md), `packages/ui/src/components/clinical/ClinicalTabs.vue` rendered `role="tablist"` and optional `role="tabpanel"` nodes as a multi-root Vue 3 fragment with `inheritAttrs: false` and minimal layout-only styles:

1. **Dropped Parent `<style scoped>` Selectors & Unstyled Outset Buttons (WCAG 2.2 SC 1.4.11):** In Vue 3, parent `<style scoped>` selectors without `:deep(...)` do not match child component subtrees or multi-root fragments. Six of eight `apps/web` consumers (`TicketsView.vue`, `TicketDetailDrawer.vue`, `CtmsLiveWorkspace.vue`, `CtmsDemoWorkspace.vue`, `SsuGreenlightConsole.vue`, and `ProtocolTrainingConsole.vue`) passed `list-class` and `tab-class` props while keeping unscoped selectors in `<style scoped>`, causing browsers to drop every consumer tab rule and render native `2px outset` buttons with `1:1` active-vs-inactive state contrast. Meanwhile, `DocumentManagerView.vue` and `KnowledgeView.vue` maintained bespoke `:deep(...)` tab stylesheets.
2. **Undersized Touch Targets & Horizontal Viewport Overflow (WCAG 2.2 SC 1.4.10, SC 2.5.8):** Without built-in `44px` minimum dimensions or bounded horizontal scroll containment (`min-width: 0`, `max-width: 100%`, `overflow-x: auto`, `white-space: nowrap`, `flex-shrink: 0`), tab triggers squished to `35.58px` on `/ctms` and `/ssu` at `320x640`, while flex-column startup and casebook matrix panels allowed inner wide tables and uncontained `.sr-only` spans to push `document.documentElement.scrollWidth` past `320px`.
3. **Clipped Off-Screen Focused Tabs & Custom Outline Override (WCAG 2.2 SC 2.4.7, SC 2.4.11):** `focusTabAtIndex` called `btn.focus()` without aligning the tablist scroll offset when keyboard navigation (`ArrowRight`, `ArrowLeft`, `Home`, `End`) or programmatic `modelValue` updates targeted an off-screen tab (clipping `#ticket-drawer-tab-attachments` inside `TicketDetailDrawer.vue`), and `.clinical-tabs-trigger:focus-visible` overrode the shared two-color `:focus-visible` halo from `packages/ui/tokens.css`.
4. **No Static Lint Gate Against Unscoped `ClinicalTabs` Class Selectors:** Nothing in `eslint.config.mjs` prevented `<style scoped>` blocks from targeting `list-class`, `tab-class`, or `active-tab-class` props without `:deep(...)`.

## 2. Decision Drivers & Constraints

- **Self-Contained Semantic Token Variants (PRD-CRF-015):** Achieve `>= 4.5:1` label and badge contrast and `>= 3:1` active indicator contrast across paired light and dark themes using existing semantic tokens from `packages/ui/tokens.css` without modifying base color/font/iconography token definitions (`tokens.css` lines 2-294), introducing inline hex values, or referencing `--semantic-color-ai*` tokens.
- **Ergonomic Hit Targets & Zero Viewport Overflow:** Guarantee `>= 44px` trigger height across mobile, tablet, and desktop viewports, preserve `tabular-nums` on `.clinical-tabs-badge`, retain the global two-color `:focus-visible` ring, and maintain `0px` horizontal document overflow down to `320x640`.
- **Motion-Safe Scroll Alignment & Zero Runtime Dependencies:** Scroll off-screen active/focused tabs into visible tablist bounds using `behavior: "instant"` under `prefers-reduced-motion: reduce`, add zero runtime npm dependencies to `packages/ui/package.json`, and leave cross-route URL scope, `NodeRef` resolution, and `apps/subject-portal/src/components/TabBar.vue` unchanged.

## 3. Options Considered

### Option 1: Centralize `underline`, `pills`, and `segmented` Variants, Overflow Modes, Scroll Alignment, and an ESLint Scoped-CSS AST Rule (Selected)

- **Overview:**
  1. Extend `ClinicalTabs.vue` and `packages/ui/index.d.ts` with `variant?: "underline" | "pills" | "segmented"` (default `"underline"`) and `overflow?: "scroll" | "wrap"` (default `"scroll"`), full native `<button>` resets (`appearance: none`, solid borders, zero native outset styling), `min-height: 44px; min-width: 44px`, semantic light/dark `[aria-selected="true"]` indicators and `.clinical-tabs-badge` (`font-variant-numeric: tabular-nums`), preserved global `:focus-visible` rings, bounded horizontal scrolling with `ResizeObserver`-driven CSS `mask-image` edge fade cues (`can-scroll-start`, `can-scroll-end`), and `scrollTabIntoView` alignment on `focusTabAtIndex`, `modelValue` updates, and mount (honoring `prefers-reduced-motion: reduce`).
  2. Migrate all eight `apps/web` views (`TicketsView.vue`, `TicketDetailDrawer.vue`, `CtmsLiveWorkspace.vue`, `CtmsDemoWorkspace.vue`, `SsuGreenlightConsole.vue`, `ProtocolTrainingConsole.vue`, `DocumentManagerView.vue`, and `KnowledgeView.vue`) onto shared `ClinicalTabs` variants, delete bespoke local tab CSS, and add `min-width: 0; max-width: 100%; position: relative` scroll-container containment so `320x640` and `768x1024` viewports exhibit `0px` horizontal overflow.
  3. Add `custom-vue/no-unscoped-clinical-tabs-css` in `eslint.config.mjs` to inspect `<ClinicalTabs>` class props (`list-class`, `tab-class`, `active-tab-class`) and reject matching `<style scoped>` selectors lacking `:deep(...)`.
- **Pros:**
  - Eliminates dropped scoped CSS and duplicated `:deep(...)` tab overrides across all eight `apps/web` views from one governed primitive.
  - Guarantees `>= 4.5:1` label contrast, `>= 3:1` active state contrast, `>= 44px` hit areas, and visible keyboard-focused tabs in both light and dark themes.
  - Prevents unscoped `ClinicalTabs` CSS regressions statically at lint time before code merges.
- **Cons:**
  - Increases `packages/ui/dist/index.css` (and compiled shared component styles in consuming apps per [ADR-2658](2026-11-23-resolve-shared-ui-from-source-and-align-verification-scripts.md)) slightly (~2.5 kB uncompressed) to ship the three shared visual variants and scroll-mask rules.

### Option 2: Wrap Existing Local Tab Rules in `:deep(...)` Across Individual `apps/web` Views

- **Overview:** Keep `ClinicalTabs.vue` unstyled and rewrite each parent `<style scoped>` rule across the six broken views to use `:deep(...)`.
- **Pros:**
  - Avoids expanding `ClinicalTabs.vue` props and `packages/ui/index.d.ts`.
- **Cons:**
  - Perpetuates eight divergent copies of tab contrast, reset, focus, and responsive overflow rules, leaves off-screen keyboard-focused tabs clipped inside scrollable tablists, and fails to provide reusable design-system variants for future clinical views.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing visual variants, `44px` hit areas, bounded horizontal scroll containment with edge fade cues, and reduced-motion-aware scroll alignment inside `ClinicalTabs.vue` fixes the root multi-root scoped-CSS failure structurally while `custom-vue/no-unscoped-clinical-tabs-css` blocks future regressions in CI.

## 5. Consequences & Trade-offs

- **Positive Impact:** Every `ClinicalTabs` instance renders accessible active states (`>= 3:1` non-text contrast and `>= 4.5:1` text contrast in light and dark themes), `>= 44px` touch targets without native outset borders, bounded horizontal scrolling (`0px` page overflow at `320x640` and `768x1024`), automatic visibility alignment on `ArrowRight`/`ArrowLeft`/`Home`/`End` and `modelValue` updates, and static ESLint protection against unscoped class-prop selectors.
- **Negative Impact / Technical Debt:** Views that retain `list-class` or `tab-class` purely as test/DOM query hooks must not style those classes in `<style scoped>` without `:deep(...)`.
- **Mitigation Strategy:** `custom-vue/no-unscoped-clinical-tabs-css` reports a clear diagnostic directing authors to shared `variant`/`overflow` props or explicit `:deep(...)` selectors.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `eslint.config.mjs`, `docs/components/index.md`, `packages/ui/README.md`, `docs/adr/index.md`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm --filter ui test` (`ClinicalTabs.test.js` and `design_system_harmony.test.js`), `pnpm exec eslint --max-warnings 0 .`, `pnpm test`, `pnpm build`, `pnpm build:demo`, `pnpm docs:build`, `pnpm cadence check -- check adrs`, and `PLAYWRIGHT_CHROME_EXECUTABLE=/usr/bin/google-chrome pnpm --filter web exec playwright test --project=accessibility`.
