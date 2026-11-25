# ADR-2689: Centralize Floating Primitives and Top-Right Toast Stack

- **Status:** Accepted
- **Date:** 2026-11-25
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015, PRD-SYS-001

---

## 1. Context & Problem Statement

Across the Cadence Clinical web workspace (`apps/web`) and shared component library (`packages/ui`), floating UI surfaces had diverged in viewport containment, keyboard/pointer dismissal, and clinical action-footer ergonomics:

1. **Bottom-Right Staff Toast Occlusion:** `apps/web/src/components/ToastHost.vue` rendered `ClinicalToastHost` at `bottom-right`, overlapping primary clinical action footers (such as **Save Signed Rule** on `#/rules`, eCRF save/sign bars, and split-pane authoring actions) and lacked a batch-clear control when four or more status or alert notifications accumulated.
2. **Ad-Hoc Popover Positioning:** `IcfSectionEditor.vue` positioned `.glossary-popover` with raw `rect.left + window.scrollX` coordinates without `visualViewport` clamping, vertical flip logic, hover persistence onto the tooltip surface (WCAG 2.2 SC 1.4.13 Content on Hover or Focus), or `Escape` dismissal.
3. **Header Menu Collisions & Short-Viewport Clipping:** `AppShell.vue`'s `#mobile-nav-menu` and `#header-secondary-menu` could remain open simultaneously on narrow/tablet viewports (`<= 860px`), lacked `12px` `visualViewport` edge clamping on short viewports (`320x256`, `1024x416`), and rendered a raw `☰` character in `#btn-mobile-menu-toggle` instead of a stroke-only vector icon.
4. **Command Palette Touch/Pointer Ergonomics:** `CanvasPalette.vue` (`⌘K`) relied solely on backdrop click or `Escape` to close without an explicit `44x44px` close button and used fixed `12vh` top padding + `50vh` list height that clipped footnotes on short viewports.

## 2. Decision Drivers & Constraints

- **WCAG 2.2 Level AA & Clinical Action Safety (`PRD-CRF-015`, `PRD-SYS-001`):** Floating surfaces must stay clamped within a `12px` `visualViewport` margin across `320x256` through `1280x800` viewports (SC 1.4.10 Reflow), keep tooltip content hoverable/dismissible without pointer loss (SC 1.4.13), provide `>= 24px` fine-pointer and `>= 44px` coarse-pointer targets (SC 2.5.8), and never occlude primary clinical save/sign buttons.
- **Zero Positioning Dependencies & Cold-Boot Budget (`#6259`, ADR-2658):** `packages/ui` must remain free of third-party positioning libraries (`@floating-ui/*`, `@vueuse/core`), and eager shell components (`AppShell.vue`, `ClinicalToastHost.vue`) must preserve the `<= 250 kB` raw JS cold-boot budget without pulling full icon dictionaries into `index.html`'s initial closure.
- **Live Consumer Reachability & Low Merge Contention (`#6262`, ADR-2677):** Every `packages/ui` export outside `STAGED_UI_PRIMITIVES` must have an active production consumer, and responsive header menu coordination should be isolated from `AppShell.vue`'s high-churn shell template.

## 3. Options Considered

### Option 1: Zero-Dependency Shared `useFloatingPosition` + `ClinicalPopover`, Top-Right Staff `ClinicalToastHost` with Batch Clear, and Extracted `useHeaderMenuCoordination` (Selected)

- **Overview:** Add `useFloatingPosition.js` (`getVisualViewportBounds`, `observeVisualViewport`, `computeFloatingPosition`, `useFloatingPosition`, `useViewportMaxHeight`) and `ClinicalPopover.vue` to `packages/ui`; extend `ClinicalToastHost.vue` with `position` (`"bottom-right"` | `"top-right"`), `@clear`, `.toast-clear-all` (`>= 4` toasts, `tabular-nums`), sticky-header clearance, coarse-pointer `44px` hit areas, and lightweight inline stroke-only vector icons; configure `apps/web/src/components/ToastHost.vue` to `position="top-right"` while keeping `apps/subject-portal` at `position="bottom-right"`; extract `apps/web/src/composables/useHeaderMenuCoordination.js` for `AppShell.vue`; bound `CanvasPalette.vue` with `useViewportMaxHeight` and a `44x44px` `#canvas-palette-close` button; and migrate `IcfSectionEditor.vue` glossary tooltips onto `ClinicalPopover`.
- **Pros:**
  - Eliminates bottom-right staff toast occlusion of save/sign actions while keeping mobile participant portal headers unobstructed.
  - Centralizes `12px` `visualViewport` (`offsetLeft` / `offsetTop`) clamping, vertical flipping, hover-bridge persistence, and stack-aware `Escape` dismissal with zero external dependencies and cold-boot JS headroom preserved.
  - Satisfies `ADR-2677` live consumer reachability across every exported component and composable while shrinking inline menu state in `AppShell.vue`.
- **Cons:**
  - Axis-aligned `top` / `bottom` placement math is tailored to clinical menus, tooltips, and dialogs rather than arbitrary polygon arrow middleware.

### Option 2: Adopt `@floating-ui/vue` and Move Both Staff and Participant Toasts to `top-right`

- **Overview:** Add `@floating-ui/vue` to `pnpm-workspace.yaml` and switch `ClinicalToastHost`'s global default to `top-right` for both `apps/web` and `apps/subject-portal`.
- **Pros:**
  - Offloads anchor positioning math to an external library.
- **Cons:**
  - Adds supply-chain and cold-boot bundle weight, violates the zero-positioning-dependency constraint, and obscures compact mobile headers and sync indicators in `apps/subject-portal`.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing zero-dependency `visualViewport` clamping in `packages/ui` (`useFloatingPosition` and `ClinicalPopover`), opting staff `ToastHost.vue` into `top-right` with `>= 4` batch clear while preserving `bottom-right` in `apps/subject-portal`, and isolating `AppShell` menu coordination in `useHeaderMenuCoordination.js` resolves toast occlusion, narrow/short-viewport clipping, and WCAG 2.2 AA hover/focus/target gaps while respecting both `ADR-2677` consumer reachability and `#6259` cold-boot bundle budgets.

## 5. Consequences & Trade-offs

- **Positive Impact:** `packages/ui` exports 25 shared Vue components (adding `ClinicalPopover`) and 5 live floating helpers (`computeFloatingPosition`, `getVisualViewportBounds`, `observeVisualViewport`, `useFloatingPosition`, `useViewportMaxHeight`); staff toasts clear bottom-right action footers across `320x256`–`1280x800` and support single-click `Clear all (N)` at `>= 4` items; `AppShell.vue`, `ClinicalToastHost.vue`, and `CanvasPalette.vue` replace raw `☰` / `×` glyphs with `1.75px` stroke-only vector icons while keeping cold-boot JS under `250 kB`; and `IcfSectionEditor.vue` glossary terms are keyboard-focusable (`tabindex="0"`), hover-persistent, edge-clamped, and `Escape`-dismissible.
- **Negative Impact / Technical Debt:** Shared barrel files (`packages/ui/src/index.js`, `packages/ui/index.d.ts`) and catalog tables still require coordinated updates when adding new `packages/ui` primitives.
- **Mitigation Strategy:** `design_system_harmony.test.js` uses dynamic `${EXPECTED_SHARED_COMPONENTS.length}` test titles instead of hardcoded count prose, `ui_consumer_reachability.test.js` enforces live app adoption, and header menu coordination lives in `apps/web/src/composables/useHeaderMenuCoordination.js` outside `AppShell.vue`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `docs/components/index.md`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm --filter ui test` (`floating_primitives.test.js`, `design_system_harmony.test.js`, `ui_consumer_reachability.test.js`), `pnpm --filter ui lint`, `apps/web` Vitest suites (`toasts.spec.js`, `AppShell.spec.js`, `CommandPaletteOverlay.spec.js`, `IcfSectionEditor.spec.js`, `focus_containment_overlays.spec.ts`, `cold_boot_bundle.spec.ts`, `lazy_workspace_branch_splits.spec.ts`), `pnpm --filter subject-portal test`, `docs/.vitepress/navigation.test.mjs`, `.NET` `Cadence.ArchitectureTests`, `cadence check` governance gates, and headless Chrome viewport verification across `320x256`, `320x640`, `390x844`, `768x1024`, `1024x416`, and `1280x800`.
