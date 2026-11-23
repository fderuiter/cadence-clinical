# ADR-2651: Vendor Variable WOFF2 Fonts, Rem Typography Scale, and CSS/AST Typography Guardrails

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (21 CFR Part 11 signature hash legibility and offline self-contained assets), PRD-CRF-015 (WCAG 2.2 Level AA SC 1.4.4 200% text resize, SC 1.4.10 reflow, 0.75rem minimum floor, and 1rem mobile form-control zoom prevention)

---

## 1. Context & Problem Statement

Across `packages/ui`, `apps/web`, `apps/subject-portal`, and `docs/.vitepress/theme`, clinical UI typography exhibited four structural gaps relative to `docs/ux/CLINICAL_CANVAS.md` §2.13 and WCAG 2.2 Level AA:

1. **System-Only Font Stacks:** `packages/ui/tokens.css` bound `--font-sans` and `--font-mono` exclusively to operating-system fallbacks (`-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, ...`) without bundling self-hosted `Inter` or `JetBrains Mono` `.woff2` files, leaving homoglyph disambiguation (`I` / `l` / `1` / `0` / `O`) dependent on the host OS.
2. **Hardcoded `px` Scale & Sub-Floor Rules:** `packages/ui/tokens.css` defined `--font-size-*` in fixed `px` units (`12px`–`28px`), `.clinical-badge` rendered at `0.7rem` (`11.2px`), `.clinical-input` rendered at `14px` (triggering automatic viewport zoom on iOS/iPadOS Safari when clinicians or participants focus inputs), `apps/subject-portal/src/style.css` overrode `body` with a raw `font-family` stack, and components across `apps/web` and `apps/subject-portal` carried sub-`0.75rem` (`< 12px`) rules, raw `px` `font-size` declarations, and ad-hoc `font-family` stacks.
3. **Inconsistent Clinical Identifier Monospace:** `NodeRef` deep-link chips, study and participant codes, CDISC item identifiers, and 21 CFR Part 11 signature records lacked uniform `var(--font-mono)` + `tabular-nums` styling.
4. **Missing CSS Pre-Commit & AST Typography Guardrails:** `FrontendQualityGateService.EslintExtensions` excluded `.css` files during `cadence check --pre-commit`, and `custom-vue` in `eslint.config.mjs` lacked rules blocking hardcoded font stacks, raw `px` font sizes outside `:root`, sub-`0.75rem` text, or sub-`1rem` form controls.

## 2. Decision Drivers & Constraints

- **Zero External Font Network Calls (`font-src 'self' data:`):** Regulated clinical deployments, VitePress documentation builds (`docs/.vitepress/config.mjs`), and offline-capable `apps/subject-portal` PWA sessions cannot fetch fonts from external CDNs (`fonts.googleapis.com` / `fonts.gstatic.com`).
- **Character Disambiguation & Clinical Canvas Alignment (`CLINICAL_CANVAS.md` §2.13):** `Inter` v4 variable subsets must retain OpenType `cv05` (lower-case `l` with tail) and `cv08` (upper-case `I` with serifs) alongside `tnum`, and `JetBrains Mono` variable subsets must retain `zero` (slashed zero) for `NodeRef` chips, study/site/participant codes, CDISC item IDs, and Part 11 SHA-256 digests.
- **WCAG 2.2 SC 1.4.4 & Mobile Viewport Stability (PRD-CRF-015):** Root-relative `rem` tokens must scale cleanly at `200%` root font size without clipped text, maintain a strict `0.75rem` (`12px`) floor on dense clinical badges/captions, maintain `1rem` (`16px`) on participant body copy (`apps/subject-portal`) and all text/select/textarea form controls (`--semantic-font-size-control: 1rem`), and preserve `0.875rem` (`14px`) staff workspace density in `apps/web`.
- **Fail-Closed Pre-Commit & CI Enforcement:** Both `.vue` and `.css` changes must be linted over `--stdin` from the Git index during `cadence check --pre-commit` as well as in `Vitest` design-system harmony suites.

## 3. Options Considered

### Option 1: Vendor Upstream SIL OFL 1.1 Variable WOFF2 Subsets in `packages/ui/fonts/` with Shared `rem` Tokens and AST/CSS Guardrails (Selected)

- **Overview:** Vendor Latin/Latin-Ext + technical symbol variable `.woff2` subsets of `Inter` v4 (`InterVariable.woff2`, `InterVariable-Italic.woff2`) and `JetBrains Mono` (`JetBrainsMonoVariable.woff2`, `JetBrainsMonoVariable-Italic.woff2`) alongside SIL OFL 1.1 notices in `packages/ui/fonts/`; expose relative `@font-face` declarations in `packages/ui/fonts.css` (`./fonts.css` export) imported by `packages/ui/tokens.css`; convert `--font-size-2xs` .. `--font-size-3xl` to `rem` (`0.75rem`–`1.75rem`) with `--semantic-font-size-control: var(--font-size-md)` (`1rem`); migrate `packages/ui`, `apps/web`, `apps/subject-portal`, and `docs/.vitepress/theme`; cache `.woff2` assets in `apps/subject-portal/public/sw.js` and verify staged demo `.woff2` assets in `scripts/stage-portal-demos.js`; add `custom-vue/no-raw-font-family`, `custom-vue/no-px-font-size`, `custom-vue/no-subfloor-font-size`, and `custom-vue/no-sub-1rem-form-control` to `eslint.config.mjs`; and add `.css` to `FrontendQualityGateService.EslintExtensions`.
- **Pros:**
  - ✅ Self-contained under `font-src 'self' data:` and offline PWA service-worker caching with zero third-party network dependencies.
  - ✅ Enforces `cv05`/`cv08` homoglyph disambiguation on prose and `JetBrains Mono` + `tabular-nums` on clinical identifiers while keeping `apps/subject-portal` in plain `1rem` participant copy.
  - ✅ Prevents regression at commit time across `.vue`, `.css`, and `.js`/`.ts` files.
- **Cons:**
  - ❌ Adds ~680 KB of immutable compressed `.woff2` variable font assets to `packages/ui/fonts/`.

### Option 2: Install Third-Party `@fontsource-variable/*` npm Packages

- **Overview:** Add runtime npm dependencies on `@fontsource-variable/inter` and `@fontsource-variable/jetbrains-mono` across workspace packages.
- **Pros:**
  - ✅ Avoids committing `.woff2` files directly into `packages/ui/fonts/`.
- **Cons:**
  - ❌ Expands external supply-chain surface area and splits per-unicode-range CSS chunks in ways that complicate deterministic single-request offline caching and `cv05`/`cv08` feature subset guarantees.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Vendoring verified SIL OFL 1.1 variable `.woff2` subsets directly in `packages/ui/fonts/` with `packages/ui/fonts.css` and `packages/ui/tokens.css` provides deterministic OpenType `cv05`/`cv08`/`tnum`/`zero` rendering across staff, participant, documentation, and staged demo surfaces without external runtime packages or CDN requests, while ESLint + `.NET` pre-commit `.css` gating locks in the `rem` floor and control sizing invariants.

## 5. Consequences & Trade-offs

- **Positive Impact:** Uniform `Inter` (`cv05`, `cv08`) and `JetBrains Mono` typography across `apps/web`, `apps/subject-portal`, `docs/.vitepress`, and staged demos; zero iOS/iPadOS auto-zoom on form controls (`1rem`); full `200%` root `rem` scaling; and automated pre-commit/CI enforcement on `.vue` and `.css` files.
- **Negative Impact / Technical Debt:** Custom component form controls using non-standard tags must either use `ClinicalInput` / `.clinical-input` or explicitly bind `var(--semantic-font-size-control)`.
- **Mitigation Strategy:** `eslint.config.mjs` inspects both standard control selectors (`input`, `select`, `textarea`, `.clinical-input`, `.form-control`) and any class attached to `<input>`, `<select>`, `<textarea>`, `<ClinicalInput>`, or `<ClinicalLookupInput>` in Vue templates, backed by `packages/ui/tests/assets.test.js`, `packages/ui/tests/design_system_harmony.test.js`, and `PreCommitGuardrailsTests.cs`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `docs/.vitepress`, `scripts/stage-portal-demos.js`, `eslint.config.mjs`, `packages/tooling_core`, `packages/cli`, `tests/Cadence.UnitTests`.
- **Verification Plan:** Verified via `pnpm --filter ui test`, `pnpm --filter subject-portal test`, `pnpm --filter web test:run`, `pnpm lint`, `pnpm build`, `pnpm docs:build`, `pnpm docs:stage-demos`, `dotnet test tests/Cadence.UnitTests`, `pnpm cadence check`, and headless browser verification at `100%` and `200%` root font scale across `320px`, `768px`, and `1280px` viewports.
