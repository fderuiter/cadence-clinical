# ADR-2677: Enforce Shadow, CSS Named-Color, Modern Color-Function, and VitePress Token Gates

- **Status:** Accepted
- **Date:** 2026-11-24
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.2 Level AA contrast across light, dark, and system-auto themes), PRD-SYS-051 (Design-system token discipline and fail-closed static quality gates)

---

## 1. Context & Problem Statement

[ADR-2675](2026-11-23-standardize-semantic-role-tokens-and-eslint-color-gates.md) standardized paired light, dark, and system-auto semantic role tokens, added `resolveThemeToken` for HTML5 `<canvas>` viewports, routed `.css` files through `FrontendQualityGateService.SupportedEslintExtensions`, and blocked `#hex`, `rgba?()`, `hsla?()`, and `white`/`black` CSS property declarations. However, four residual enforcement gaps from closed PR `#6059` remained on `main`:

1. **Untokenized `box-shadow` and `drop-shadow()` Declarations & Incomplete `--semantic-shadow-*` Scale:** `packages/ui/tokens.css` only exposed `--semantic-shadow-sm` and `--semantic-shadow-xl` (omitting `md`, `lg`, `drawer-*`, `focus-ring`, `focus-accent`, `drop-sm`, and status-pulse shadows, as well as a shared `.btn-secondary` utility), while `eslint.config.mjs` did not inspect `box-shadow` or `drop-shadow()` values—leaving raw shadows in `docs/.vitepress/theme/custom.css`, `apps/subject-portal/src/style.css`, `DictionaryBrowserModal.vue`, `MedicalCodingView.vue`, and `ClinicalGanttVisualizer.vue` ungated.
2. **CSS Level 4 Named Colors, Modern Color Functions, and JS/Vue AST Style Bindings:** `custom-vue/no-inline-hex` only matched `white|black` on 6 flat CSS properties via regex; the remaining 146 CSS Level 4 named colors (`red`, `crimson`, `blue`, `gray`, `whitesmoke`, etc.), modern CSS Color 4 functions (`hwb()`, `oklab()`, `oklch()`, `lab()`, `lch()`), Vue object-style bindings (`:style="{ color: 'white', boxShadow: '...' }"`—such as `CrcFormRenderer.vue`'s uncalibrated `color: 'white'` badge on `--semantic-color-error`), DOM style assignments (`el.style.backgroundColor = 'white'`), and `<canvas>` `gradient.addColorStop(...)` calls bypassed static analysis.
3. **VitePress Theme Stylesheet & Script Coverage:** `eslint.config.mjs` excluded `docs/.vitepress/theme/**/*.css` from CSS linting and omitted `custom-vue/no-inline-hex` and `custom-vue/no-undefined-css-vars` from `docs/.vitepress/theme/**/*.js`.
4. **CLI Pre-Commit Gate Documentation Drift:** `QualityGates.cs` still described `eslint`'s `PreCommitNote` as covering only `"staged JS, TS and Vue blobs over stdin"`, omitting `CSS`.

## 2. Decision Drivers & Constraints

- **Complete Elevation & Shadow Discipline:** Surface separation must rely on crisp `1px` borders, `box-shadow: none`, zero-blur WCAG Technique C40 focus/border halos, or centralized `--shadow-*` / `--semantic-shadow-*` tokens with 1:1 light, dark, and system-auto parity.
- **Zero Bypass Across Color Syntaxes & AST Forms:** Named CSS colors, CSS Color 4 functions, object-style `:style` bindings, imperative `.style.*` / `<canvas>` assignments, and VitePress theme assets must be gated identically to `#hex` and `rgba()` without false positives on clinical prose strings, DOM ID selectors, CSS keywords (`transparent`, `currentColor`, `inherit`, `Highlight`), or VitePress `--vp-*` theme variables.
- **Preserve Upstream `ADR-2673`, `ADR-2675`, `ADR-2676`, & `ADR-2824` Contracts:** Complement upstream role tokens, focus-handoff rules, clinical typography rules, and `FrontendQualityGateService` `.css` plumbing cleanly without duplicating or reverting upstream changes.

## 3. Options Considered

### Option 1: Complete Semantic Shadow Scale, Full AST Color/Shadow Enforcement, VitePress Coverage, and CLI Metadata Alignment (Selected)

- **Overview:** Complete the `--semantic-shadow-*` alias family and paired light/dark/auto `--shadow-{focus-accent,drop-sm,pulse-warning-*}` tokens plus shared `.btn-secondary` rules in `packages/ui/tokens.css`; upgrade `custom-vue/no-inline-hex` to enforce all 148 CSS Level 4 named colors, CSS Color 4 functions (`hwb`, `oklab`, `oklch`, `lab`, `lch`), `box-shadow`/`drop-shadow()` token discipline, and Vue/JS/TS AST `Property`, `AssignmentExpression`, and `CallExpression` (`addColorStop`) checks; enable CSS and JS token linting across `docs/.vitepress/theme/` (allowing `--vp-*` custom properties and `var(--vp-font-family-*)` stacks strictly within `docs/.vitepress/`); migrate all remaining violations (`CrcFormRenderer.vue`, `DictionaryBrowserModal.vue`, `MedicalCodingView.vue`, `ClinicalGanttVisualizer.vue`, `apps/subject-portal/src/style.css`, `docs/.vitepress/theme/custom.css`); and synchronize `QualityGates.cs` `PreCommitNote` and generated CLI docs.
- **Pros:**
  - Closes every remaining color and shadow bypass across CSS, Vue templates/scripts/styles, JS/TS modules, and VitePress theme assets.
  - Fixes `CrcFormRenderer.vue`'s dark-mode discrepancy badge contrast (`>= 6.47:1` via `--semantic-color-error-interactive` + `--semantic-color-text-on-solid`).
  - Aligns `QualityGates.cs` and `docs/cli.md` with `FrontendQualityGateService.SupportedEslintExtensions`.
- **Cons:**
  - New shadow treatments require defining a shared `--shadow-*` / `--semantic-shadow-*` token in `packages/ui/tokens.css` rather than inline pixel offsets.

### Option 2: Keep `no-inline-hex` Limited to Flat `rgba()`/`hsl()` and `white`/`black` Regexes

- **Overview:** Rely on code review to catch untokenized `box-shadow` values, named colors like `red` or object-style `{ color: 'white' }`, modern `oklch()`/`hwb()` functions, and VitePress stylesheet regressions.
- **Pros:**
  - Simpler ESLint rule implementation.
- **Cons:**
  - Leaves real contrast and elevation drift (such as `CrcFormRenderer.vue`'s `{ color: 'white' }` and `docs/.vitepress/theme/custom.css`'s raw `rgba()` shadow) undetected.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Extending `packages/ui/tokens.css` with complete semantic shadow aliases and upgrading `eslint.config.mjs` to inspect AST style properties, all 148 CSS named colors, CSS Color 4 functions, `box-shadow`/`drop-shadow()`, and `docs/.vitepress/theme/` closes every remaining gap from `#6059` cleanly on top of `ADR-2675`, `ADR-2676`, and `ADR-2824`.

## 5. Consequences & Trade-offs

- **Positive Impact:** All application and documentation stylesheets, Vue SFCs, and JS/TS modules enforce centralized color and shadow tokens end-to-end across CI and `--pre-commit`; `CrcFormRenderer.vue` badges maintain `>= 4.5:1` contrast in both light and dark modes; and CLI documentation accurately reflects staged CSS linting.
- **Negative Impact / Technical Debt:** Component-specific elevation or keyframe pulse spreads must be promoted to `packages/ui/tokens.css` across `:root`, `dark`, and `auto` theme blocks.
- **Mitigation Strategy:** `packages/ui/tests/assets.test.js` validates 1:1 light/dark/auto shadow parity, `.btn-secondary` rules, and positive/negative ESLint AST fixtures automatically.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `packages/cli`, `docs/.vitepress/theme`, `eslint.config.mjs`, `tests/Cadence.UnitTests`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`, `docs/cli.md`.
- **Verification Plan:** Verified via `pnpm lint`, `pnpm typecheck`, `pnpm test`, `pnpm docs:build`, `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter PreCommitGuardrailsTests`, Playwright `axe-core` suites, and `pnpm cadence check --base origin/main`.
