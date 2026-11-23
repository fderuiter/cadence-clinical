# ADR-2824: Standardize Clinical Typography Utilities, 12px Floor, and AST Lints

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.2 Level AA readability, contrast, and 320px reflow compliance), PRD-SYS-001 (21 CFR Part 11 regulated clinical data legibility and audit integrity)

---

## 1. Context & Problem Statement

Clinicians reviewing safety observations, eCRF entries, RTSM kits, and CTMS metrics in `apps/web` as well as trial participants reviewing stipend wallets, compliance counters, and offline sync queues in `apps/subject-portal` faced three systemic typography inconsistencies across the monorepo:

1. **Fragmented Font Stacks & Missing Semantic Monospace Utilities:** `packages/ui/tokens.css` placed generic `monospace` ahead of platform monospaced fonts in `--font-mono`, lacked a semantic `--semantic-font-mono` alias and shared `.font-mono` / `.numeric` utility classes, and left `apps/web/src/style.css`, `apps/subject-portal/src/style.css`, and individual `.vue` components declaring ad-hoc `font-family` stacks (`monospace`, `"Courier New"`, `ui-monospace`, `Arial`, `-apple-system`).
2. **Unaligned Numerals & Proportional Clinical Identifiers:** Study codes, site codes, participant identifiers, CDASH concept codes, eCRF dataset values, masked bank/card numbers, and audit timestamps rendered in proportional sans-serif in several views, while `font-variant-numeric: tabular-nums` did not cover `<input>` / `<select>` controls inside clinical tables or non-table KPI cards, wallet balances, compliance counters, and sync retry countdowns.
3. **Sub-12px Micro-Typography & Blind Spots in Harmony/Lint Guards:** `--font-size-2xs` resolved to `11px` and 372 component CSS/inline rules declared `9px`–`11px` (`0.6rem`–`0.74rem`) sizes, while `packages/ui/tests/design_system_harmony.test.js` only inspected `.vue` files matching `/<table[\s>]/` and `eslint.config.mjs` had no AST/CSS rules blocking sub-12px sizes, custom font stacks, or `font-variant-numeric: normal` overrides.

## 2. Decision Drivers & Constraints

- **Clinical Safety & Legibility (PRD-SYS-001, PRD-CRF-015):** Alphanumeric identifiers (`0`/`O`, `1`/`I`/`l`), laboratory/eCRF values, masked payout accounts, and ISO timestamps must use a platform-first clinical monospace stack (`"JetBrains Mono"`, `ui-monospace`, `SFMono-Regular`, `Menlo`, `Monaco`, `Consolas`, `"Liberation Mono"`, `"Courier New"`, `monospace`), numerals must align vertically via `tabular-nums`, and no production text may render below `12px` (`0.75rem`) while preserving `>= 4.5:1` WCAG AA contrast and zero horizontal overflow at `320px` width.
- **Imperative DOM Compatibility:** Existing HTML tag names and fixed DOM IDs in `apps/subject-portal/src/views/WalletView.vue`, `apps/subject-portal/src/views/ComplianceView.vue`, and `apps/subject-portal/src/components/SyncDrawer.vue` must remain unchanged.
- **Fail-Closed Static & Harmony Enforcement:** Regressions across `.vue` templates, `<style>` blocks, and `.css` stylesheets must fail `pnpm lint` and `packages/ui` harmony tests immediately.

## 3. Options Considered

### Option 1: Centralize Semantic Typography Tokens & Utilities in `packages/ui/tokens.css` Paired with Full-Repo ESLint AST/CSS Rules and Harmony Sentinels (Selected)

- **Overview:** Update `packages/ui/tokens.css` to prioritize `"JetBrains Mono"` and OS monospaced fonts before `monospace` in `--font-mono`, `"Inter"` first in `--font-sans`, define `--semantic-font-mono: var(--font-mono)` and `--font-size-2xs: 12px`, expose `.font-mono` and `.numeric` utilities, and extend `tabular-nums` to `input`/`select` inside tables and `.clinical-input` controls. Align `body` rules in `apps/web/src/style.css` and `apps/subject-portal/src/style.css` to semantic tokens (`"cv05" 1, "cv08" 1`), apply `--semantic-font-mono` and `tabular-nums` across clinical canvas, observation, eCRF, CTMS, RTSM, tickets, knowledge, and subject-portal views, convert all sub-12px rules to `var(--font-size-2xs)` / `var(--font-size-xs)`, and enforce `custom-vue/no-sub12px-font-size`, `custom-vue/no-custom-font-stack`, and `custom-vue/no-unaligned-numerals` in `eslint.config.mjs` alongside full-tree checks in `packages/ui/tests/design_system_harmony.test.js`.
- **Pros:**
  - ✅ Guarantees uniform clinical identifier legibility, vertical decimal/numeral alignment, and a `12px` minimum floor across both portals without altering Subject Portal DOM IDs or backend contracts.
  - ✅ Eliminates the non-table `.vue` blind spot and catches sub-12px sizes, ad-hoc `font-family` lists, and `normal` numeral overrides at lint and unit-test time.
- **Cons:**
  - ❌ Raising badges and dense table metadata to `12px` requires wrapping rules (`flex-wrap: wrap`, `min-width: 0`) so `320px` viewports do not overflow horizontally.

### Option 2: Patch Individual Views Without Shared Utilities, Token Floor Changes, or ESLint Rules

- **Overview:** Style select clinical tables manually while keeping `--font-size-2xs: 11px` and relying on code review.
- **Pros:**
  - ✅ Smaller initial diff across legacy components.
- **Cons:**
  - ❌ Leaves sub-12px micro-text in production, duplicates font stacks across SFCs, and allows unaligned numerals and custom font stacks to regress silently.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing `--semantic-font-mono`, `--font-size-2xs: 12px`, `.font-mono`, `.numeric`, and table/input `tabular-nums` in `packages/ui/tokens.css` while enforcing AST/CSS lints in `eslint.config.mjs` and full-repo assertions in `design_system_harmony.test.js` resolves root-cause clinical legibility gaps and prevents future drift.

## 5. Consequences & Trade-offs

- **Positive Impact:** Consistent `"JetBrains Mono"` / platform monospaced rendering for clinical codes, dataset values, masked accounts, and audit timestamps; jitter-free `tabular-nums` counters, balances, and table inputs; `>= 12px` text legibility with zero `320px` horizontal overflow; and automated lint/test enforcement.
- **Negative Impact / Technical Debt:** Components needing monospaced or sans-serif styling must reference governed CSS custom properties (`var(--semantic-font-mono)`, `var(--semantic-font-family)`) rather than inline OS font lists.
- **Mitigation Strategy:** Clear remediation messages in `eslint.config.mjs` rules and documented usage in `packages/ui/README.md`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `eslint.config.mjs`, `docs/adr/index.md`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm format`, `pnpm lint`, `pnpm build`, `pnpm --filter ui test`, `pnpm --filter web test` (including `cold_boot_bundle.spec.ts`), `pnpm --filter subject-portal test`, headless Chromium `320px` reflow verification, and `pnpm cadence check`.
