# ADR-2824: Semantic Lucide Icon Primitives and Zero-Emoji ESLint Gates

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.2 Level A & AA SC 1.1.1 Non-text Content, SC 2.5.3 Label in Name, and SC 4.1.2 Name/Role/Value compliance), PRD-SYS-001 (21 CFR Part 11 regulated workflow clarity)

---

## 1. Context & Problem Statement

Across `packages/ui`, `apps/web`, `apps/subject-portal`, and `apps/web/src/locales/*.json`, `106` runtime files and `4` locale catalogs rendered `1,175` OS-dependent Unicode emoji and regional-indicator pictographs (`Extended_Pictographic` and `Regional_Indicator`) because `pnpm-workspace.yaml` declared zero vector icon libraries:

1. **Cross-Platform Visual Noise & Regulated Ambiguity:** Clinical staff inspecting 21 CFR Part 11 signature states (`DocumentManagerView.vue`), terminology validation badges (`ClinicalLookupInput.vue`), and support SLA countdowns (`TicketsView.vue`), as well as trial participants navigating ePRO tabs and offline sync status (`TabBar.vue`, `PortalHeader.vue`, `clinical-fields.js`), saw inconsistent OS color emojis and regional flag pairs that clashed with the quiet cockpit aesthetic specified in `docs/ux/CLINICAL_CANVAS.md`.
2. **Screen-Reader & Voice-Control Friction (WCAG 2.2 SC 1.1.1, SC 2.5.3, SC 4.1.2):** Inline Unicode pictographs inside buttons, badges, and localized strings were announced verbosely or inconsistently across NVDA, JAWS, VoiceOver, and TalkBack, while imperative DOM string builders (`createClinicalLookupInput` and Subject Portal widgets) lacked a shared `aria-hidden="true"` SVG helper.
3. **Missing Lint & Pre-Commit Regression Gates:** Neither `eslint.config.mjs` nor `FrontendQualityGateService.cs` scanned `.vue`, `.js`, `.ts`, `.css`, `.html`, or `apps/web/src/locales/*.json` files for Unicode emoji characters, allowing pictographs to enter templates, stylesheets, HTML entrypoints, and locale catalogs undetected.

## 2. Decision Drivers & Constraints

- **Single Catalog-Pinned Icon Dependency:** Shared frontend packages must pin versions in `pnpm-workspace.yaml` (`catalog:`) per ADR-2527, while `apps/web` and `apps/subject-portal` consume UI primitives exclusively through `"ui"`.
- **Tree-Shakeable, Locked Geometry:** Named icons must be imported explicitly inside `packages/ui` (never `* as icons`) so Vite and Rollup preserve tree-shaking, and rendered vectors must lock to `16px` or `20px` sizes with a `1.75px` stroke width and `aria-hidden="true"` on decorative instances (ADR-2822).
- **Fail-Closed Lint & Pre-Commit Enforcement:** Accidental `Extended_Pictographic` or `Regional_Indicator` glyphs in `.vue`, `.js`, `.ts`, `.css`, `.html`, or `locales/*.json` must fail both `pnpm lint` and `cadence check --pre-commit` immediately without touching backend `.NET` services or `QualityGates.cs`.

## 3. Options Considered

### Option 1: Pin `lucide-vue-next` in Workspace Catalog, Export `ClinicalIcon` and `renderClinicalIconSvg` from `packages/ui`, and Gate Emojis via ESLint + `FrontendQualityGateService` (Selected)

- **Overview:** Pin `lucide-vue-next` (`^1.0.0`) in `pnpm-workspace.yaml` `catalog:` and declare `"lucide-vue-next": "catalog:"` in `packages/ui/package.json`. Export a typed `ClinicalIcon` Vue component and `renderClinicalIconSvg` HTML string helper from `packages/ui` backed by explicit named imports locked to `16px`/`20px` and `1.75px` stroke width. Migrate all shared `packages/ui` primitives, `apps/web` views/components/stores, `apps/subject-portal` views/routes/widgets, and `apps/web/src/locales/{en,de,es,fr}.json` (replacing regional flag pairs with plain ISO codes). Register `custom-vue/no-emoji-glyphs` in `eslint.config.mjs` across `.vue`, `.js`, `.ts`, `.css`, `.html`, and `locales/*.json` using lightweight stub parsers, and add `.css`, `.html`, and `.json` to `EslintExtensions` in `FrontendQualityGateService.cs`.
- **Pros:**
  - Centralizes vector rendering in `packages/ui` for both Vue SFCs and imperative DOM builders while preserving Rollup tree-shaking and serializable semantic icon names in Pinia stores and route metadata.
  - Enforces zero-emoji regressions uniformly across source code, stylesheets, HTML entrypoints, and locale JSON catalogs during `pnpm lint`, `pnpm test`, and `cadence check --pre-commit`.
- **Cons:**
  - Requires explicit registration in `packages/ui/src/icons/clinicalIcons.js` when adding new semantic icon names.

### Option 2: Import `lucide-vue-next` Directly Across Each App Without a Shared Wrapper or Lint Gate

- **Overview:** Add `lucide-vue-next` to `apps/web` and `apps/subject-portal` directly and import individual icon components per view.
- **Pros:**
  - Avoids maintaining a curated semantic icon map in `packages/ui`.
- **Cons:**
  - Permits arbitrary icon sizes and stroke widths, duplicates dependency declarations across apps, provides no HTML string helper for imperative widgets, and leaves `.css`, `.html`, and `locales/*.json` unguarded against emoji regressions.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Exporting `ClinicalIcon` and `renderClinicalIconSvg` from `packages/ui` paired with `custom-vue/no-emoji-glyphs` and `FrontendQualityGateService.EslintExtensions` delivers uniform `16px`/`20px` `1.75px`-stroke clinical iconography, clean screen-reader semantics, and deterministic pre-commit enforcement.

## 5. Consequences & Trade-offs

- **Positive Impact:** Crisp, OS-independent vector icons paired with visible text or `aria-label` across all clinician and participant workflows, `aria-hidden="true"` decorative SVGs by default, plain ISO jurisdiction badges instead of regional flag emojis, and immediate line-level ESLint diagnostics when emojis are staged or committed.
- **Negative Impact / Technical Debt:** Adding a new icon requires adding an explicit import and semantic key in `packages/ui/src/icons/clinicalIcons.js` and rebuilding `packages/ui`.
- **Mitigation Strategy:** Keep the semantic registry curated and verified by `packages/ui/tests/clinical_icon.test.js` and `packages/ui/tests/design_system_harmony.test.js`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `pnpm-workspace.yaml`, `packages/ui`, `apps/web`, `apps/subject-portal`, `eslint.config.mjs`, `packages/tooling_core/src/Services/FrontendQualityGateService.cs`, `tests/Cadence.UnitTests/Packages/PreCommitGuardrailsTests.cs`, `docs/components/index.md`, `docs/adr/index.md`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm lint`, `pnpm --filter web typecheck`, `pnpm test`, `dotnet test`, `pnpm cadence check adrs`, and negative staged-emoji probes against `pnpm lint` and `pnpm cadence check --pre-commit`.
