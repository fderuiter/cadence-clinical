# ADR-2679: Centralize Curated Lucide Icons and Enforce CLI Emoji Gate

- **Status:** Accepted
- **Date:** 2026-11-25
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015, PRD-SYS-001

---

## 1. Context & Problem Statement

Clinical web surfaces across `packages/ui`, `apps/web`, `apps/subject-portal`, locale catalogs (`de`, `en`, `es`, `fr`), and `packages/demo-fixtures` previously mixed platform-dependent Unicode emoji glyphs (`Extended_Pictographic`, `Regional_Indicator`, and keycap combining sequences), bespoke inline `<svg>` snippets with `2px` strokes, and ad-hoc `makeLucideIcon` wrappers:

1. **Cross-Platform Rendering & Assistive Technology Drift:** Platform color-emoji fonts render inconsistently across hospital workstations and regulated participant tablets and announce verbose or ambiguous names in screen readers under WCAG 2.2 Level AA (SC 1.1.1 Non-text Content, SC 1.4.1 Use of Color).
2. **Cold-Boot Bundle Discipline:** Importing dynamic `lucide-vue-next` icon barrels into `packages/ui` defeats Rollup/Rolldown tree-shaking and breaches the `<= 250 KiB` raw / `<= 90 KiB` gzip cold-boot JS budget enforced by `apps/web/tests/cold_boot_bundle.spec.ts`.
3. **Audit & Regression Gaps:** Without dual-layer AST and CLI/bundle enforcement, new Unicode emojis or unlabeled `<button><ClinicalIcon ... /></button>` elements could enter `.vue` templates, `.json` locale catalogs, `.css` stylesheets, or compiled `dist/` bundles.

## 2. Decision Drivers & Constraints

- **Single `1.75px` Vector Icon Standard (`PRD-CRF-015`):** Standardize decorative UI icons on `lucide-vue-next` at `16px` / `20px` sizes with uniform `1.75px` stroke width, `aria-hidden="true"`, and `focusable="false"`, paired with visible text labels or explicit `aria-label` attributes (`ADR-2822`).
- **Tree-Shakable Shared Library & Split Shell/Route Loading (`ADR-2527`, `ADR-2658`):** Keep `"lucide-vue-next": "catalog:"` externalized in `packages/ui/vite.config.js` with `preserveModules: true`, statically importing only boot-shell icons (`shellIcons.js`) in `ClinicalIcon.vue` while lazy-loading route-level icons (`routeIcons.js`).
- **Native AOT & Zero-Exemption Gate Parity (`ADR-2496`, `ADR-2497`, `ADR-2534`):** Enforce zero emojis in ESLint (`custom-vue/no-emoji-glyphs`), `cadence check no-emojis` (`EmojiValidatorService`), and `cadence check public-bundle` (`PublicBundleValidatorService`) with zero reflection and zero comment exemptions.

## 3. Options Considered

### Option 1: Curated Split `lucide-vue-next` Registry (`shellIcons` + `routeIcons`), `renderClinicalIconSvg` Helper, ESLint AST Rules, and Native AOT `no-emojis` + `public-bundle` Gates (Selected)

- **Overview:** Split curated named `lucide-vue-next` imports in `packages/ui/src/icons/` into `shellIcons.js` (7 cold-boot shell icons) and `routeIcons.js` (41 lazy-loaded route icons), expose `ClinicalIconName`, `isClinicalIconName`, and `renderClinicalIconSvg(name, options)` for imperative DOM builders, migrate all runtime emojis across `packages/ui`, `apps/web`, and `apps/subject-portal` to `1.75px` vector icons + accessible labels, harden `custom-vue/no-unlabeled-glyph-button` for PascalCase icon components, add `custom-vue/no-emoji-glyphs` in `eslint.config.mjs`, and add `EmojiValidatorService` backing `cadence check no-emojis` and `PublicBundleValidatorService`.
- **Pros:**
  - Eliminates every Unicode emoji glyph across source, locales, fixtures, VitePress theme, and compiled app/demo bundles while keeping cold-boot and route JS within strict byte caps.
  - Throws in development/test on unknown `ClinicalIconName` values so typos fail fast during unit tests.
  - Binary-searched `Rune` interval scanning in `EmojiValidatorService` matches V8 `/\p{Extended_Pictographic}/u`, `Regional_Indicator`, and keycap sequences in Native AOT with zero regex backtracking.
- **Cons:**
  - Adding a new icon requires registering a named `lucide-vue-next` import in `packages/ui/src/icons/` rather than using arbitrary inline glyphs.

### Option 2: Dynamic `lucide-vue-next` Barrel Import in `ClinicalIcon.vue`

- **Overview:** Import `* as LucideIcons from "lucide-vue-next"` inside `ClinicalIcon.vue` and resolve arbitrary kebab-case icon names dynamically at runtime.
- **Pros:**
  - Avoids maintaining an explicit curated icon list.
- **Cons:**
  - Pulls over 1,400 unused icons into the initial bundle closure, violating `cold_boot_bundle.spec.ts` and `PublicBundleValidatorService` demo cold-boot budgets.

### Option 3: ESLint-Only Emoji Lint Rule Without CLI or Public-Bundle Gates

- **Overview:** Enforce a custom ESLint rule on `.vue` and `.js`/`.ts` files only.
- **Pros:**
  - Smaller tooling footprint.
- **Cons:**
  - Leaves `.html`, `.svg`, `.webmanifest`, and compiled `dist/` public bundles unguarded in `cadence check` and CI post-build verification (`ADR-2497`).

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Pairing a curated, code-split `lucide-vue-next` icon registry in `packages/ui` with ESLint AST rules (`custom-vue/no-emoji-glyphs` and PascalCase-aware `custom-vue/no-unlabeled-glyph-button`) and Native AOT C# gates (`EmojiValidatorService` in `no-emojis` and `public-bundle`) achieves crisp cross-platform `1.75px` clinical iconography, WCAG 2.2 AA accessible naming, preserved cold-boot budgets, and fail-closed regression protection from editor to production bundle.

## 5. Consequences & Trade-offs

- **Positive Impact:** Zero Unicode emoji glyphs remain across `packages/ui`, `apps/web`, `apps/subject-portal`, `packages/demo-fixtures`, and `docs/.vitepress/theme`; `ClinicalIcon`, `ClinicalIconButton`, and `renderClinicalIconSvg` render uniform `16px`/`20px` `1.75px` SVGs with `aria-hidden="true"` and `focusable="false"`; and both `cadence check no-emojis` (including `--pre-commit` staged blobs) and `cadence check public-bundle` fail closed on emoji regressions.
- **Negative Impact / Technical Debt:** New clinical workflows needing an unregistered Lucide icon must add its named import to `routeIcons.js` (or `shellIcons.js` if rendered in persistent boot chrome) and update `iconNames.js` / `index.d.ts`.
- **Mitigation Strategy:** `ClinicalIcon` and `resolveClinicalIcon` throw descriptive errors in non-production environments naming `packages/ui/src/icons/`, and `design_system_harmony.test.js` verifies 1:1 parity across `iconNames.js`, `shellIcons.js`, `routeIcons.js`, and `index.d.ts`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `eslint.config.mjs`, `packages/tooling_core`, `packages/cli`, `tests/Cadence.UnitTests`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`, `docs/cli.md`, `docs/CI.md`.
- **Verification Plan:** Verified via `pnpm --filter ui test` (`design_system_harmony.test.js`, `shared_components.test.js`, `ui_consumer_reachability.test.js`), `pnpm --filter subject-portal test`, `pnpm --filter web test` (including `cold_boot_bundle.spec.ts`), `pnpm typecheck`, `pnpm lint`, `dotnet build Cadence.sln`, `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj` (`QualityGateRegistryTests`, `PublicBundleValidatorServiceTests`), `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`, `pnpm cadence check`, and headless browser + `axe-core` visual verification across `/etmf`, `/tickets`, and `apps/subject-portal`.
