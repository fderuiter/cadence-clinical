# ADR-2823: Enforce Two-Profile CSS Density Tokens, Self-Hosted Clinical Fonts, and 1rem Touch Inputs

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Supersedes:** [ADR-2321](2026-08-03-implement-class-based-touch-target-optimization-for-dense-layouts.md)
- **Requirements:** PRD-CRF-015 (WCAG 2.1 SC 2.5.5 Target Size, WCAG 2.2 AA touch/reflow compliance, zoom-free bedside & ePRO data entry), PRD-SYS-001 (21 CFR Part 11 regulated workflow integrity)

---

## 1. Context & Problem Statement

Auditing 58 rendered clinical and participant screens revealed two systemic touch-ergonomics and typography gaps stemming from the opt-in `.touch-target` class model introduced in [ADR-2321](2026-08-03-implement-class-based-touch-target-optimization-for-dense-layouts.md):

1. **Opt-In Touch Classes Left Most Controls Below 44px / 48px:** Because ADR-2321 required explicit `.touch-target` or `.touch-target-interactive` classes on every control, shared primitives (`ClinicalIconButton`, `ClinicalDropzone`, `ClinicalModal`, `ClinicalDrawer`, `ClinicalQueryFlag`, `ClinicalRadioGroup`) and view-local buttons, stage pills, table row selectors, and `<summary>` disclosures rendered between `16.5px` and `36px` on tablet and mobile viewports—violating WCAG 2.1 SC 2.5.5 (`44px`) in `apps/web` and the `48px` participant standard established in [ADR-2328](2026-08-04-centralized-layout-utilities-and-48px-touch-targets.md) and `docs/components/index.md`.
2. **Sub-`1rem` Inputs Triggered iOS Safari Viewport Auto-Zoom:** `packages/ui/tokens.css` set `--font-size-base: 15px`, `packages/ui/index.js` hardcoded `0.8rem` inline input styles inside `createConditionRowHTML`, and app stylesheets (`apps/web/src/style.css`, `apps/subject-portal/src/style.css`) styled `<input>`, `<select>`, and `<textarea>` elements at `13px`–`15px`, causing iOS/iPadOS WebKit to zoom the viewport on field focus and obscure surrounding clinical context.
3. **System Font Fallbacks & Raw Emoji Glyphs Under Strict CSP:** Both `apps/web/index.html` and `apps/subject-portal/index.html` enforce `font-src 'self'`, requiring self-hosted `.woff2` variable fonts for `Inter` (with OpenType `cv05` lower-case `l` tail, `cv08` upper-case `I` serifs, and `tabular-nums` alignment) and `JetBrains Mono` (for `NodeRef` URIs, subject IDs, and SHA-256 digests), alongside standardized `lucide-vue-next` (`16px`/`20px`, `1.75px` stroke) icons on touch controls.

## 2. Decision Drivers & Constraints

- **Two Distinct Surface Profiles (PRD-CRF-015):** Clinical staff (`apps/web`, `data-surface-profile="staff"`) need `>= 44px` hit areas on tablet/mobile (`<= 1279px`) and `(pointer: coarse)` hardware with collapsed top chrome `<= 52px`, while retaining compact `32px`–`36px` controls and `0.875rem` (`14px`) table rows on `1280px+` `(pointer: fine)` workstations. Trial participants (`apps/subject-portal`, `data-surface-profile="portal"`) require `>= 48px` hit areas across all viewports.
- **Zero iOS Auto-Zoom on Touch Inputs:** Every visible `<input>`, `<select>`, and `<textarea>` must compute at `>= 16px` (`1rem`) on touch, tablet, and mobile viewports—including HTML-string rule builder rows in `packages/ui/index.js`.
- **Self-Hosted Clinical Typography & CSP Compliance:** Variable `.woff2` subsets for `Inter` and `JetBrains Mono` must ship inside `packages/ui/fonts/` under `font-src 'self'`, with `--font-size-*` tokens expressed in scalable `rem` units (`0.75rem` minimum floor) while preserving `--touch-target-min: 48px` and WCAG AA contrast (`>= 4.5:1`).
- **21 CFR Part 11 Stability (PRD-SYS-001):** Backend APIs, signature workflows, and canonical DOM IDs must remain unchanged.

## 3. Options Considered

### Option 1: Declarative Two-Profile CSS Tokens & Media Queries in `packages/ui` with Self-Hosted Variable Fonts and `lucide-vue-next` Primitives (Selected)

- **Overview:** Supersede ADR-2321's opt-in class model with declarative surface-profile custom properties in `packages/ui/tokens.css` and `packages/ui/responsive.css` (`--touch-target-staff: 44px`, `--touch-target-portal: 48px`, `--surface-control-min-height`, `--surface-control-min-width`, `--surface-input-min-height`, `--surface-input-font-size`, `--surface-table-font-size`, `--chrome-height-tablet-max: 52px`). Bind `data-surface-profile="staff"` on `apps/web` (`44px` default, compact `32px`–`36px` and `0.875rem` inputs/tables under `@media (min-width: 1280px) and (pointer: fine)`) and `data-surface-profile="portal"` on `apps/subject-portal` (`48px` and `1rem` inputs across all viewports). Bundle self-hosted `Inter` (`cv05`, `cv08`, `tabular-nums`) and `JetBrains Mono` `.woff2` fonts in `packages/ui/fonts/`, convert `--font-size-*` tokens to `rem`, remove inline sub-`1rem` styles in `packages/ui/index.js`, and standardize shared and touched icon controls on `lucide-vue-next` (`16px`/`20px`, `1.75px` stroke).
- **Pros:**
  - ✅ Enforces `44px` staff touch targets, `48px` participant targets, and `1rem` touch inputs automatically without per-template class boilerplate.
  - ✅ Preserves dense `32px`–`36px` fine-pointer desktop workflows at `1280px+` and keeps collapsed `apps/web` tablet top chrome `<= 52px`.
  - ✅ Satisfies `font-src 'self'` CSP with disambiguated clinical glyphs (`Inter` `cv05`/`cv08`, `JetBrains Mono`) and crisp `1.75px` stroke `lucide-vue-next` icons.
- **Cons:**
  - ❌ Adds ~137 KB of cached self-hosted `.woff2` font subsets and `lucide-vue-next` as a catalog dependency across UI packages.

### Option 2: Expand ADR-2321 Opt-In `.touch-target` Bindings Manually Across Every Template

- **Overview:** Keep ADR-2321's opt-in `.touch-target` utility classes and manually attach Vue viewport bindings and per-input `font-size` overrides to every component across `packages/ui`, `apps/web`, and `apps/subject-portal`.
- **Pros:**
  - ✅ Avoids introducing profile-level CSS custom properties.
- **Cons:**
  - ❌ Brittle and error-prone: any new button, link, `<summary>`, or HTML-string widget without `.touch-target` silently regresses below `44px`/`48px` or `16px` input font size.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Declarative two-profile CSS tokens and media rules in `packages/ui` solve both halves of ADR-2321's original dilemma—guaranteeing `44px` bedside staff touch targets, `48px` participant portal targets, and `1rem` zoom-free inputs automatically while preserving compact `32px`–`36px` desktop workstation density at `1280px+` `(pointer: fine)`.

## 5. Consequences & Trade-offs

- **Positive Impact:** Eliminates iOS Safari input auto-zoom across clinical forms, modals, and rule builders; brings shared primitives (`ClinicalIconButton`, `ClinicalDropzone`, `ClinicalRadioGroup`, `ClinicalModal`, `ClinicalDrawer`, `ClinicalQueryFlag`) and app shells into automatic `44px`/`48px` compliance; keeps `apps/web` tablet header chrome `<= 52px`; and renders disambiguated `Inter` (`cv05`, `cv08`, `tabular-nums`) and `JetBrains Mono` typography offline under `font-src 'self'`.
- **Negative Impact / Technical Debt:** Legacy `.touch-target`, `.min-touch-target`, and `.touch-target-interactive` classes remain supported as aliases in `packages/ui/responsive.css` for backward compatibility with existing callers.
- **Mitigation Strategy:** Guard token contracts, self-hosted `@font-face` declarations, `rem` scales, and `packages/ui/dist` outputs in `packages/ui/tests/design_system_harmony.test.js`, paired with multi-viewport Playwright E2E assertions in `apps/web` and `apps/subject-portal`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `pnpm-workspace.yaml`, `package.json`, `docs/components/index.md`, `docs/adr/`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm --filter ui test`, `pnpm --filter web test`, `pnpm --filter subject-portal test`, `pnpm --filter web exec playwright test --project=accessibility`, `pnpm --filter subject-portal test:e2e`, and `node scripts/run-cli.js check`.
