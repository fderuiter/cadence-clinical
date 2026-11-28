# ADR-FRONTEND-20261128-slot-projected-clinicalrecordcard-mobile-mode-and-shared-use2dgrid: Slot-projected ClinicalRecordCard mobile mode and shared use2DGrid

- **Status:** Accepted
- **Date:** 2026-11-28
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Clinical coordinator workspaces (`apps/web`) and participant portals (`apps/subject-portal`) render operational record lists alongside true two-dimensional clinical matrices (Schedule of Activities, participant casebooks, eTMF completeness grids, RACT risk matrices, repeating CRF log grids, and oncology lesion calipers). On narrow mobile viewports (`320px` and `390px`) and inside bounded slide-over drawers (`<= 480px`), wide `<table>` layouts force horizontal panning, clip primary identifiers when scrolled, or leak `.sr-only` screen-reader context spans outside non-positioned overflow containers. At the same time, Unicode sort glyphs (`⇅`, `▲`, `▼`) rendered inconsistently across operating systems, sort/pagination buttons lacked explicit `44px` / `48px` target sizing, and `use2DGrid` lived exclusively inside `apps/web/src/composables/` without consistent adoption across all exempted 2D clinical matrices (PRD-SYS-001).

## 2. Decision Drivers & Constraints

- **WCAG 2.2 AA Reflow & Target Size (SC 1.4.10, SC 2.5.5, SC 2.5.8):** Record lists must reflow cleanly at `320px` and `390px` without horizontal document overflow, and sort/pagination controls must meet `44px` (`compact`/`comfortable`) and `48px` (`touch`) pointer target floors.
- **Zero-Duplicate Slot Projection:** Views already provide rich `#cell-<key>`, `#masked-row`, `#after-row`, and `#empty` slots to `ClinicalDataTable`; mobile record cards must reuse those slots directly rather than duplicating mobile templates per view.
- **Sticky Clinical Identifier Context:** Multi-column desktop/tablet tables (`>= 768px`) and 2D clinical matrices must pin primary subject/site/record identifier columns (`position: sticky; left: 0`) during horizontal panning.
- **Shared 2D ARIA Grid Operability:** Every matrix in `EXEMPT_2D_CLINICAL_MATRICES` requires uniform roving `tabindex` (`0` active, `-1` inactive) and 4-way arrow-key / `Home` / `End` navigation from `packages/ui`.
- **Scroll-Wrapper `.sr-only` Containment & Zero-Emoji Vector Iconography:** Every `overflow-x: auto|scroll` or `overflow: auto|scroll` wrapper must establish `position: relative` (`position: sticky` / `fixed` / `absolute` where applicable) so `position: absolute` `.sr-only` spans never escape scroll bounds, and sort indicators must use `16px` `lucide-vue-next` icons at `1.75px` stroke width.

## 3. Options Considered

### Option 1: Shared `ClinicalRecordCard` + Automatic Slot Projection in `ClinicalDataTable` + Promoted `use2DGrid` in `packages/ui` (Selected)

- **Overview:** Export `ClinicalRecordCard` (`compact` and `touch` densities, monospace/`EntityLink` identifier header, semantic status slot, `<dl>` tabular key-value grid, and `position: relative` footer) and `use2DGrid` from `packages/ui`. Teach `ClinicalDataTable` to project rows into `ClinicalRecordCard` items automatically when `mobileMode="auto"` (default at `<= 480px` viewport or container width) or `mobileMode="cards"`, render a mobile sort selector bar when columns declare `sortable: true`, support `sticky: "left" | "right"`, `stickyHeader`, and `density`, replace Unicode sort arrows with `ArrowUpDown` / `ArrowUp` / `ArrowDown` (`16px`, `1.75px` stroke), wire `use2DGrid` across all 7 `EXEMPT_2D_CLINICAL_MATRICES`, migrate remaining record-list raw tables (`AuditView`, `EcrfItemTable`, `EcrfSignaturesSection`, `ObservationView`), and enforce scroll-wrapper `position: relative` and Unicode sort glyph bans in ESLint and `design_system_harmony.test.js`.
- **Pros:**
  - Eliminates `<= 480px` horizontal table overflow across both `apps/web` and `apps/subject-portal` while reusing existing `#cell-*`, `#masked-row`, `#after-row`, and `#empty` slots.
  - Preserves primary identifier visibility on `>= 768px` tables and all 7 exempted 2D matrices via `position: sticky; left: 0`.
  - Unifies 2D roving `tabindex` keyboard traversal in `packages/ui` and shrinks `LEGACY_WEB_RAW_TABLE_ALLOWLIST`.
  - Prevents regressions automatically through `custom-vue/require-scroll-wrapper-relative` and `custom-vue/no-unicode-sort-glyphs`.
- **Cons:**
  - Adds column-role classification (`cardRole`) and resize/media observation logic inside `ClinicalDataTable`.

### Option 2: Per-View CSS `display: block` Table Stacking + App-Local `use2DGrid`

- **Overview:** Keep `<table>` DOM markup on mobile and override `tr`/`td` to `display: block` with `td::before { content: attr(data-label) }` in each view, while leaving `use2DGrid` inside `apps/web`.
- **Pros:**
  - Avoids introducing a dedicated `ClinicalRecordCard` component.
- **Cons:**
  - Strips native ARIA table semantics inconsistently across screen readers when CSS `display: block` mutates `<tr>`/`<td>` layout boxes, cannot express structured header/badge/footer hierarchy cleanly, and leaves `use2DGrid` unavailable to shared `packages/ui` consumers.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Projecting `ClinicalDataTable` rows into `ClinicalRecordCard` at `<= 480px` delivers accessible semantic `<article>` + `<dl>` card structure with zero duplicated view templates, while promoting `use2DGrid` to `packages/ui` standardizes keyboard matrix navigation across all clinical grids and satisfies PRD-SYS-001.

## 5. Consequences & Trade-offs

- **Positive Impact:** Zero horizontal overflow on mobile clinical record lists (`320x640`, `390x844`), sticky identifier columns on tablet/desktop tables and 2D matrices, consistent `16px` `1.75px`-stroke vector sort controls with `44px`/`48px` hit targets, and AST-level lint protection against unpositioned scroll containers and Unicode sort glyphs.
- **Negative Impact / Technical Debt:** Custom column layouts with non-standard action or status column keys may specify `cardRole` (`"identifier" | "title" | "status" | "actions" | "field" | "select" | "hidden"`) when overriding automatic column-role heuristics.
- **Mitigation Strategy:** Comprehensive unit, harmony, accessibility (`axe-core`), and 2D keyboard matrix tests across `packages/ui` and `apps/web`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui` (`ClinicalRecordCard.vue`, `ClinicalDataTable.vue`, `use2DGrid.js`, `responsive.css`, `index.js`, `index.d.ts`), `apps/web` (migrated views, sticky column metadata, and all 7 `EXEMPT_2D_CLINICAL_MATRICES`), `apps/subject-portal` (`ComplianceView.vue` and scroll wrappers), and `scripts/eslint/` (`require-scroll-wrapper-relative.mjs`, `no-unicode-sort-glyphs.mjs`, allowlists).
- **Verification Plan:** Verified via `packages/ui/tests/ClinicalRecordCard.test.js`, `packages/ui/tests/ClinicalDataTable.test.js`, `packages/ui/tests/use2DGrid.test.js`, `packages/ui/tests/design_system_harmony.test.js`, `apps/web/tests/components/Exempt2DMatricesNavigation.spec.ts`, `pnpm lint`, `pnpm -r typecheck`, and multi-viewport headless Chromium geometry checks.
