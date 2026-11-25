# ADR-2834: Bundle Self-Hosted Clinical WOFF2 Fonts, Rem Typography Scale, and Touch Control Lint Gates

- **Status:** Accepted
- **Date:** 2026-11-24
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-UI-001, PRD-A11Y-001

---

## 1. Context & Problem Statement

Clinical trial operations (`apps/web`) and participant eCOA/ePRO (`apps/subject-portal`) surfaces rely on `Inter` (with OpenType character variants `cv05` and `cv08`) for proportional prose and `JetBrains Mono` (with slashed zero and tabular widths) for structured clinical identifiers (`CLINICAL_CANVAS.md` §2.13). Following [ADR-2824](2026-11-24-standardize-clinical-typography-utilities-12px-floor-and-ast-lints.md), four architectural gaps remained:

1. **Unbundled Typefaces:** Neither `Inter` nor `JetBrains Mono` `.woff2` binaries or `@font-face` declarations were shipped in `packages/ui`, causing unprovisioned workstations, tablets, and participant smartphones to fall back to OS system fonts (`Segoe UI`, `Roboto`, `SF Pro`, `Courier New`) where `cv05` and `cv08` character-disambiguation tables do not exist.
2. **Fixed-Pixel Typography Scale & Untokenized CSS:** `packages/ui/tokens.css` defined `--font-size-2xs` through `--font-size-3xl` in fixed `px` units rather than root-relative `rem` units, and over 2,500 component `font-size` declarations across `packages/ui`, `apps/web`, and `apps/subject-portal` bypassed shared scale tokens entirely, impairing WCAG 2.2 SC 1.4.4 (`200%` text resize) and user browser font-size preferences.
3. **Sub-`1rem` (`16px`) Touch Controls:** Standalone `<input>`, `<select>`, and `<textarea>` controls (such as `.persona-select`, audit filter controls, trial setup inputs, and query textareas) resolved below `1rem` (`16px`) on coarse pointers, triggering jarring iOS Safari and Android Chrome viewport auto-zoom during clinical and participant data entry.
4. **Incomplete Identifier Scoping & Lint Coverage:** `NodeRefChip.vue` inherited proportional sans-serif text instead of `--semantic-font-mono`, zoom-path and filter chips mixed prose prefixes (`Site`) into monospace spans, signature rows and SHA-256 digests lacked explicit `--semantic-font-mono` bindings, and ESLint lacked static gates blocking untokenized `font-size` declarations or sub-`1rem` touch controls.

## 2. Decision Drivers & Constraints

- **Offline & Strict CSP Compatibility:** Bundled font URLs must be same-origin relative assets (`font-src 'self' data:`) cached automatically by `apps/subject-portal`'s service worker (`sw.js`) with zero external CDN requests.
- **Asset Budget Discipline ([ADR-2537](2026-11-22-split-ui-vitest-matcher-and-enforce-demo-bundle-budgets.md)):** Combined `.woff2` font weight must stay within `65 KB`–`95 KB` so `/subject-portal/assets/` stays well under `942,080` B and `/demo/` cold-boot payloads stay under `1,415,577` B raw / `460,800` B gzip, accompanied by SIL Open Font License 1.1 notices.
- **WCAG 2.2 SC 1.4.4 & Mobile Ergonomics:** All typography tokens must scale proportionally in `rem` from a `0.75rem` (`12px`) minimum floor, and every non-checkbox/radio `<input>`, `<select>`, and `<textarea>` must compute to `>= 1rem` (`16px`) on `@media (pointer: coarse)` viewports without overflowing the `<= 52px` persistent desktop shell header.
- **Automated Regression Prevention:** ESLint must fail closed on untokenized `font-size` literals, non-`rem` scale definitions, sub-`0.75rem` sizes, custom `font-family` stacks, and sub-`1rem` touch control declarations across `.vue`, `.css`, and frontend `.js`/`.ts` files.

## 3. Options Considered

### Option 1: Self-Hosted Variable WOFF2 Subsets in `packages/ui`, Full `rem` Token Codemod, Coarse-Pointer `1rem` Floor, and ESLint Gates (Selected)

- **Overview:** Bundle Latin variable subsets `packages/ui/fonts/InterVariable-latin.woff2` (`wght` 100–900, preserving `cv05`, `cv08`, `tnum`, `zero`) and `packages/ui/fonts/JetBrainsMonoVariable-latin.woff2` (`wght` 300–800, preserving `zero`) (~`87.3 KB` combined) alongside `OFL-Inter.txt` and `OFL-JetBrainsMono.txt`; add `@font-face` rules and convert `--font-size-2xs` (`0.75rem`) through `--font-size-5xl` (`2.5rem`) to `rem` in `packages/ui/tokens.css`; add global `@media (pointer: coarse)` `1rem` floors for `:where(input, select, textarea):not([type="checkbox"]):not([type="radio"])` and lift standalone control rules to `var(--font-size-md)`; scope `--semantic-font-mono` strictly to clinical IDs, CDISC item codes, signature metadata, and SHA-256 digests; codemod all untokenized `font-size` declarations onto `packages/ui` `rem` tokens; and enforce `custom-vue/no-untokenized-font-size` and `custom-vue/no-sub1rem-touch-control` in `eslint.config.mjs`.
- **Pros:**
  - Deterministic, air-gapped rendering of `Inter` (`cv05`, `cv08`) and `JetBrains Mono` across clinician, participant, and VitePress documentation surfaces under strict CSP.
  - Full `rem` scaling for `200%` browser font-size zoom and zero mobile input auto-zoom across coarse-pointer devices.
  - Hard static ESLint enforcement prevents untokenized `font-size` or sub-`1rem` touch controls from re-entering the codebase.
- **Cons:**
  - Adds ~`87.3 KB` of immutable `.woff2` assets to the shared UI bundle and touches ~200 frontend files during the one-time `rem` token codemod.

### Option 2: External Webfont Hosting or Unsubsetted Static Font Weights

- **Overview:** Load fonts from third-party CDNs or ship full unsubsetted multi-file static `.woff2` weights without codemodding component `px` declarations.
- **Pros:**
  - Avoids maintaining a subsetting configuration.
- **Cons:**
  - External font CDNs violate `font-src 'self' data:`, leak participant IP metadata, and fail offline in `apps/subject-portal`; unsubsetted static weights exceed `350 KB+`; and leaving `px` declarations in components breaks `200%` root `rem` scaling.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Self-hosting subsetted Latin variable `Inter` and `JetBrains Mono` `.woff2` binaries in `packages/ui` (~`87.3 KB` combined), converting the shared typography scale and all component rules to `rem` tokens, enforcing a `1rem` coarse-pointer input floor, scoping `--semantic-font-mono` strictly to clinical identifiers, and gating compliance in ESLint satisfies `CLINICAL_CANVAS.md` §2.13, WCAG 2.2 AA SC 1.4.4, strict CSP, and public bundle budgets simultaneously.

## 5. Consequences & Trade-offs

- **Positive Impact:** Guaranteed `Inter` (`cv05`, `cv08`) and `JetBrains Mono` glyph rendering offline and under `font-src 'self' data:`; `100%` `rem`-tokenized `font-size` declarations across `packages/ui`, `apps/web`, and `apps/subject-portal`; `>= 16px` touch text/select controls preventing mobile viewport zoom; and automated ESLint enforcement.
- **Negative Impact / Technical Debt:** Non-Latin script glyph packs (Cyrillic, Greek, CJK) remain delegated to platform fallback fonts in `--font-sans` / `--font-mono` to keep the core `.woff2` payload within `65 KB`–`95 KB`.
- **Mitigation Strategy:** Retain full system font fallback chains after `"Inter"` and `"JetBrains Mono"` in `--font-sans` and `--font-mono`; future localized deployments can add `unicode-range` split `.woff2` packs on demand.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `docs/.vitepress`, `eslint.config.mjs`, `docs/adr`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm lint`, `pnpm typecheck`, `pnpm test` (`packages/ui/tests/design_system_harmony.test.js` and workspace suites), `pnpm build`, `pnpm portal:build`, `pnpm cadence check public-bundle`, headless Chromium checks for loaded `document.fonts`, `pointer: coarse` `>= 16px` input sizing (`1024x768`, `768x1024`, `375x812`), `200%` root `rem` zoom, and `pnpm cadence check --base origin/main`.
