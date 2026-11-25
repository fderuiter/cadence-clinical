# ADR-2678: Centralize Floating Primitives and Top-Right Toast Stack

- **Status:** Accepted
- **Date:** 2026-11-24
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.2 Level AA SC 1.4.10 Reflow, SC 1.4.13 Content on Hover or Focus, SC 2.4.11 Focus Not Obscured, SC 2.5.5 / SC 2.5.8 Target Size, SC 4.1.3 Status Messages), PRD-SYS-001 (21 CFR Part 11 explicit signature submission visibility)

---

## 1. Context & Problem Statement

Audits across narrow (`320x640`, `390x844`), zoomed (`320x256`), tablet (`768x1024`), and landscape (`1024x416`) viewports identified recurring viewport clipping, action occlusion, and dismissal gaps across floating UI overlays:

1. **Bottom-Right Web Toast Occlusion & Unbounded Stacking (`ClinicalToastHost.vue`, `ToastHost.vue`):** Persistent `error` alerts (`timeoutMs = 0`, ADR-2655) anchored at `bottom: 16px; right: 16px` in `apps/web` overlapped bottom-right submit actions such as `Save Signed Rule` on `#/rules`, lacked vertical scroll containment on short viewports (`320x256`, `1024x416`), offered no batch clear action when `>= 4` alerts accumulated, and rendered a legacy text multiplication glyph (`\u00d7`) on dismiss buttons without `44px` coarse-pointer hit targets.
2. **Uncoordinated Shell Disclosures & Clipped Command Palette (`AppShell.vue`, `CanvasPalette.vue`):** `#mobile-nav-menu` and `#header-secondary-menu` could remain open simultaneously at `768x1024` (`<= 860px`), `#mobile-nav-menu` did not close on outside pointer press, and `#header-secondary-menu` could clip `#btn-logout` below `320x256`. `#canvas-palette` lacked a visible `44px` close button, clipped footnotes below `320x256` and `1024x416` when `#canvas-palette-kernel-note` or `#canvas-palette-subject-note` rendered, and kept fixed `7rem` side-by-side `.palette-group` columns on `< 640px` screens.
3. **Unclamped Ad-Hoc Popovers & Legacy Text Glyphs (`IcfSectionEditor.vue`, `PersonaSwitcher.vue`):** `IcfSectionEditor.vue` positioned `.glossary-popover` with unclamped absolute coordinates (`pointer-events: none`), causing horizontal page overflow (`scrollWidth > viewportWidth`) at `320x640` and `390x844`, violating WCAG 2.2 SC 1.4.13 hover persistence and `Escape` dismissal, and gating keyboard `focusin` behind `(hover: hover)`. `PersonaSwitcher.vue` anchored `.persona-menu` with `right: 0` (`x < 0` on left-aligned triggers) without `Escape` or outside-click dismissal, and used legacy text glyphs (`\u2630`, `\u25b2`, `\u25bc`).

## 2. Decision Drivers & Constraints

- **Zero External Positioning Dependencies & Cold-Boot Bundle Ceiling:** `pnpm-workspace.yaml` must not add third-party positioning libraries, and `apps/web` initial cold-boot JavaScript must remain strictly within `<= 250 KiB` raw / `<= 90 KiB` gzip (`apps/web/tests/cold_boot_bundle.spec.ts`).
- **WCAG 2.2 Level AA & Two-App Toast Compatibility (PRD-CRF-015, PRD-SYS-001):** `apps/web` toasts must anchor top-right below `.app-header` while `apps/subject-portal` preserves `bottom-right` clearance above mobile bottom navigation (`data-portal-toast-host="true"`), keeping persistent `error` alerts (`timeoutMs = 0`), hover/focus timer pause, and `[data-focus-trap-exempt="true"]` `Tab` bridging intact.
- **Persistent Header Budget:** `.app-header` in `apps/web` must remain `<= 52px` on tablet and desktop viewports (`>= 768px`).

## 3. Options Considered

### Option 1: Lightweight Shared `useFloatingPosition` + `ClinicalPopover` in `packages/ui` and Targeted Overlay Adoptions (Selected)

- **Overview:** Export `getVisualViewportBounds`, `computeFloatingPosition`, `useFloatingPosition`, and `ClinicalPopover.vue` from `packages/ui` (`12px` visual-viewport margin clamping, automatic vertical flip, `overflow-y: auto` scroll containment, optional `hoverBridge`, `useEscapeClose` integration, and outside-pointer dismissal); extend `ClinicalIcon.vue` with `menu`, `chevron-down`, and `chevron-up` vector paths; add `position="top-right"`, bounded scroll height, `>= 4` batch clear, vector dismiss icons, and `@media (pointer: coarse)` `44px` hit targets to `ClinicalToastHost.vue` (adopted in `apps/web/src/components/ToastHost.vue` while keeping `bottom-right` in `apps/subject-portal/src/widgets/toast.js`); coordinate `AppShell.vue` menus with mutual exclusion, outside pointer + single `Escape` dismissal, and viewport-clamped scroll bounds; add a `44px` close button, short-viewport scrollable `.palette-body`, and `< 640px` stacked `.palette-group` layout to `CanvasPalette.vue`; and migrate `IcfSectionEditor.vue` and `PersonaSwitcher.vue` onto `ClinicalPopover`.
- **Pros:**
  - Zero external npm dependencies and negligible cold-boot byte impact.
  - Centralizes `visualViewport` `12px` clamping, flip math, `Escape` dismissal, and outside-pointer handling in `packages/ui` for reuse across both apps.
  - Preserves inline DOM rendering (`teleport: false` default) for SSR and detached component unit tests while using `position: fixed` viewport coordinates.
- **Cons:**
  - Requires maintaining viewport clamping math in `packages/ui` unit tests across zoomed and short viewports.

### Option 2: Adopt an External Floating Positioning Library Across Workspace Packages

- **Overview:** Add `@floating-ui/vue` and `@floating-ui/dom` to `pnpm-workspace.yaml` and wire middleware across `packages/ui` and `apps/web`.
- **Pros:**
  - Offloads anchor/flip/shift middleware to a third-party package.
- **Cons:**
  - Violates workspace dependency constraints and increases the `AppShell.vue` cold-boot JavaScript bundle toward the `90 KiB` gzip ceiling.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** A focused ~250-line composable (`useFloatingPosition.js`) and `ClinicalPopover.vue` primitive in `packages/ui` satisfy every WCAG 2.2 SC 1.4.10 / SC 1.4.13 / SC 2.4.11 / SC 2.5.8 requirement, keep `Save Signed Rule` and `#btn-logout` unobstructed down to `320x256`, and add zero external dependencies.

## 5. Consequences & Trade-offs

- **Positive Impact:** Persistent `error` toast stacks in `apps/web` never obscure bottom form submit bars; `>= 4` stacked notifications expose a one-click `Clear all` control; `AppShell` menus enforce mutual exclusion and viewport scroll bounds while keeping `.app-header <= 52px` on `>= 768px`; `CanvasPalette` exposes a `44px` close button with `0px` clipping at `320x256` and `1024x416`; and `IcfSectionEditor` / `PersonaSwitcher` popovers remain clamped within `12px` of all viewport edges with hover persistence and `Escape` / outside-click dismissal.
- **Negative Impact / Technical Debt:** Shared UI component inventory increases from `30` to `31` (`ClinicalPopover`), requiring `packages/ui` type definitions and harmony assertions to stay synchronized.
- **Mitigation Strategy:** Enforced by `packages/ui/tests/design_system_harmony.test.js`, `packages/ui/tests/floating_primitives.test.js`, `apps/web/tests/cold_boot_bundle.spec.ts`, and component suites in `apps/web/tests/`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `docs/components/`, `docs/adr/`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm --filter ui test`, `pnpm --filter ui typecheck`, `pnpm --filter ui lint`, `pnpm --filter cadence-web test`, `pnpm --filter @cadence/subject-portal test`, `pnpm typecheck`, `pnpm lint`, Cadence CLI documentation/ADR checks, and headless Chromium viewport and hit-test verification across `320x256`, `320x640`, `390x844`, `768x1024`, `1024x416`, and `1280x800`.
