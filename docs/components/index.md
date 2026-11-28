# Cadence Enterprise UI Components & Design System

The Cadence Clinical platform provides a shared, vanilla CSS-based enterprise design system in `ui` (`packages/ui`).

## Core Principles

- **Zero Tailwind Dependency**: Standard Vanilla CSS scoped styling using centralized CSS Custom Properties (`tokens.css`) across paired light, dark (`:root[data-theme="dark"]`, `.dark`), and OS-auto (`@media (prefers-color-scheme: dark)` targeting `:root:not([data-theme="light"]), :root[data-theme="auto"]`) themes synchronized via `vitepress-theme-appearance` ([ADR-2690](../adr/2026-11-25-unified-css-cascade-and-shared-theme-controller.md)).
- **Full-Width Authoring Workspaces & Tabular Numerals**: High-density clinical tables, matrices, `<time>` timestamps, `.clinical-badge` indicators, KPI/currency readouts, live countdowns, and numeric/date form controls enforce `font-variant-numeric: tabular-nums` (`--font-variant-numeric-tabular`) digit alignment while keeping narrative `<textarea>` fields and multi-sentence prose proportional.
- **Accessibility (WCAG 2.2 AA / Section 508)**: ARIA landmarks, keyboard focus management (`useFocusTrap`, `getFocusableElements`, `useEscapeClose`), two-color `:focus-visible` indicators (`2px` `--semantic-color-text-on-solid` inner halo + `3px` `--semantic-color-accent` outer outline delivering `>= 6.34:1` contrast across light, brand, and dark surfaces, `:not([tabindex="-1"])` programmatic landmark/heading focus ring suppression, and `@media (forced-colors: active)` `Highlight` outlines), centralized `.btn:disabled` / `.btn[aria-disabled="true"]` and `:not(:disabled)` hover guards, live regions for terminology lookup feedback, `48px` enhanced touch targets (`--semantic-touch-target-min`) on `.clinical-input` and `.touch-target`, `24px` minimum pointer targets (`--semantic-control-target-min`) on compact controls and `<summary>` disclosures per SC 2.5.8, `>= 4.5:1` AA semantic text/status token contrast, and `>= 3:1` interactive control boundary contrast (`--semantic-color-border-control`).

---

## Component & Composable Catalog

All 27 Vue components and interaction helpers are exported directly from the root `ui` package:

| Export                                  | Description                                                                                                                                                                                                          | ARIA Landmark / Role                        |
| :-------------------------------------- | :------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | :------------------------------------------ |
| `ClinicalContextBar`                    | Accessible clinical scope breadcrumb landmark linking Studies, study, site, participant, and current workflow                                                                                                        | `navigation`, `list`, `link`                |
| `ClinicalBreadcrumbs`                   | Accessible `<nav aria-label="Breadcrumb">` trail marking the active item with `aria-current="page"` and `48px` mobile touch targets                                                                                  | `navigation`, `list`, `link`                |
| `ClinicalExplanation`                   | Structured ADR-2482 rule explanation card rendering claims, met/unmet conditions, steps, and `NextAction` `NodeRef` links                                                                                            | `region`, `aria-live="polite"`              |
| `ClinicalGatedAction`                   | Visible capability-gated action wrapper with accessible inline or expandable why-blocked explanations                                                                                                                | `button`, `aria-describedby`                |
| `EntityLink`                            | `NodeRef`-compatible keyboard-operable button or link trigger and K-EXP-2 masked badge with `.sr-only` context and peek/open focus restoration                                                                       | `button` / `link`                           |
| `ClinicalIcon`                          | Zero-emoji `1.75px` stroke-only vector icon primitive (`aria-hidden="true"`) paired with visible labels                                                                                                              | Decorative SVG (`aria-hidden="true"`)       |
| [`ClinicalDataTable`](./data-table.md)  | Accessible, keyboard-sortable, client/external/none paginated clinical dataset table with focusable or masked rows, row selection, header/cell slots, live pagination status, and tabular numerals                   | `table`, `region`, `columnheader`, `status` |
| [`ClinicalModal`](./modal.md)           | Accessible modal dialog with focus trap, `ariaDescribedby` support, and backdrop blur                                                                                                                                | `dialog`, `aria-modal="true"`               |
| [`ClinicalDrawer`](./modal.md)          | Accessible slide-over drawer and `position="bottom"` / `mobileBottomSheet` `90dvh` safe-area bottom sheet with reactive focus containment                                                                            | `dialog`, `aria-modal="true"`               |
| `ClinicalPopover`                       | Visual-viewport-clamped anchored floating popover with `12px` edge margins, vertical flip, WCAG 2.2 SC 1.4.13 hover bridge, and `Escape` / outside dismissal                                                         | `tooltip` / `dialog` / `region`             |
| `ClinicalToastHost`                     | Focus-trap-exempt `bottom-right` / `top-right` toast host persisting `error` alerts, batch-clearing `>= 4` notifications, pausing timed status toasts on hover/focus, and splitting live roles                       | `region`, `alert`, `status`                 |
| `ClinicalDropzone`                      | Accessible drag-and-drop and native `<button type="button">` file selector with persistent live selected-file status and sibling remove                                                                              | `button`, `status`                          |
| [`ClinicalLiveRegion`](./live-region.md) | Persistent pre-mounted polite (`role="status"`) and assertive (`role="alert"`) live-region strip, banner, inline alert, or `.sr-only` announcer with sibling `#pills` and `#actions` slots                            | `status`, `alert`                           |
| `ClinicalIconButton`                    | Accessible glyph-only `<button>` requiring a non-empty `label` prop (`aria-label`) with `aria-hidden="true"` glyph slot                                                                                              | `button`                                    |
| `SignOutConfirmModal`                   | Draft-safe voluntary sign-out confirmation dialog with focus containment and unsaved-work warning                                                                                                                    | `dialog`, `aria-modal="true"`               |
| `ClinicalFieldLayout`                   | Shared accessible field wrapper supporting `touch` (`48px`) and `compact` (`36px`) densities, required markers, hints, persistent `role="alert"` validation messages, and query flags                                | `group`, `alert`                            |
| `ClinicalFormField`                     | Schema-driven CDASH form field dispatcher with automatic `data-tabular-nums="true"` propagation for numeric/date types                                                                                               | `group`                                     |
| `ClinicalInput`                         | Accessible `48px` touch-target or `compact` staff clinical `<input>`, `<select>`, and `<textarea>` control with `16px` coarse/mobile font-size guard plus `type`, `inputmode`, and `tabularNums` alignment           | `textbox`, `combobox`                       |
| `ClinicalRadioGroup`                    | Accessible clinical radio group fieldset                                                                                                                                                                             | `radiogroup`                                |
| `ClinicalTabs`                          | Accessible WAI-ARIA tablist with `underline`/`pills`/`segmented` variants, `scroll`/`wrap` overflow containment with edge fade cues, `44px` touch targets, roving `tabindex`, and motion-safe focus scroll alignment | `tablist`, `tab`, `tabpanel`                |
| `ClinicalThemeToggle`                   | Zero-emoji Lucide (`Sun`, `Moon`, `Monitor`) `light` / `dark` / `auto` theme switcher supporting `cycle` and `segmented` variants across `compact` (`44px`) staff and `touch` (`48px`) participant portal headers    | `button` / `radiogroup`                     |
| `ClinicalLookupInput`                   | Real-time terminology lookup field with dynamic status indicators                                                                                                                                                    | `combobox`, `aria-live`                     |
| `ClinicalQueryFlag`                     | Discrepancy query status indicator button                                                                                                                                                                            | `button`                                    |
| `ClinicalQueryPanel`                    | Inline clinical query discussion thread with Part 11 compliance and `Escape` dismissal                                                                                                                               | `region`, `list`                            |
| `EmptyState`                            | Shared empty-state placeholder (`role="region"`) with opt-in `live` polite status mode (`role="status"`) for filtered zero-match states                                                                              | `region` / `status`                         |
| `MediaPlayer`                           | Clinical PDF, image pan/zoom, audio waveform, and video viewer with GxP watermark support                                                                                                                            | `region`                                    |
| `ModalErrorAlert`                       | Accessible error banner with retry and dismiss actions                                                                                                                                                               | `alert`                                     |
| [`useStatusAnnouncer`](./live-region.md) | Debounced polite status and immediate assertive error composable with duplicate-message tick clearing and timer cleanup                                                                                              | Live-region composable                      |
| `createSrOnlyActionButton`              | Imperative DOM `<button>` builder appending `.sr-only` target context after the visible verb                                                                                                                         | `button` helper                             |
| `useFocusTrap` / `getFocusableElements` | Reactive keyboard focus containment, modal stack queries (`hasActiveModalFocusTrap`), and `fallbackFocus` / visible fallback restoration                                                                             | Keyboard helper                             |
| `useEscapeClose`                        | Stack-aware `Escape` dismissal composable                                                                                                                                                                            | Keyboard helper                             |
| `useFloatingPosition` helpers           | Zero-dependency `visualViewport` bounds, `12px` edge clamping, vertical flip (`computeFloatingPosition`), and `useViewportMaxHeight`                                                                                 | Positioning helper                          |
| `useTheme` / `themeController` helpers  | Reactive `light` / `dark` / `auto` theme composable, pre-paint `bootstrapTheme`, cross-tab `storage` & live `prefers-color-scheme` `subscribeThemeSync`, and shared `vitepress-theme-appearance` persistence         | Theme controller                            |
| `callerScope` helpers                   | OIDC `studyId`/`siteId` claim normalisers (`extractAssignedStudies`, `extractAssignedSites`, `extractPrimaryStudyId`, `extractPrimarySiteId`)                                                                        | Scope helper                                |

---

## Design System Tokens

Tokens are imported from [tokens.css](../../packages/ui/tokens.css):

```css
@import "ui/tokens.css";

.custom-card {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: var(--semantic-radius-md);
  color: var(--semantic-color-text);
}
```

### Tabular Numerals & Clinical Typography

`packages/ui/tokens.css` defines `--font-variant-numeric-tabular: tabular-nums` (and `--semantic-font-variant-numeric-tabular`) as the single typographic source of truth across `packages/ui`, `apps/web`, and `apps/subject-portal` (ADR-2670):

- **Global Selectors & Opt-In Hooks**: Applied automatically to `table`, `th`, `td`, `time`, `.clinical-table`, `.clinical-visit-matrix`, `.data-table`, `.clinical-badge`, `.tabular-nums`, and `[data-tabular-nums]:not([data-tabular-nums="false"])`.
- **UA Form-Control Reset Overrides**: Browsers reset `font` on `<input>` and `<select>`, stripping inherited `font-variant-numeric` inside `<td>` cells and wrappers. `tokens.css` explicitly applies `--font-variant-numeric-tabular` to `input[type="number" | "date" | "datetime-local" | "time" | "month" | "week" | "tel" | "range"]`, `input[inputmode="numeric" | "decimal"]`, `td input:not([type="checkbox"]):not([type="radio"])`, `td select`, `.tabular-nums input/select`, `[data-tabular-nums] input/select`, and `.clinical-input-control` (`tabularNums` prop or `data-tabular-nums` attribute on `ClinicalInput`).
- **Touch Viewport & Prose Safeguards**: Coarse-pointer devices (`@media (pointer: coarse)`) enforce `font-size: max(1rem, var(--font-size-md, 16px))` on form controls to prevent iOS Safari auto-zoom. Narrative `<textarea>` fields and multi-sentence prose paragraphs remain in proportional Inter digits, and `--font-mono` (`JetBrains Mono`) is reserved strictly for clinical identifiers, protocol versions, `NodeRef`s, CDISC variable names, and Part 11 signature hashes.
- **AST Lint Enforcement**: `custom-vue/require-tabular-nums` in `eslint.config.mjs` flags unaligned numeric `<input>` / `<select>` / `<ClinicalInput>` controls, live countdowns, count badges, KPI/currency readouts, and `font-variant-numeric: normal` overrides.

### Side-Over Drawer Width Tokens

Viewport-relative design tokens defined in `packages/ui/tokens.css` specify standard widths for side-over panel drawers:

| Token               | Semantic Token               | Formula             | Target Max Width |
| :------------------ | :--------------------------- | :------------------ | :--------------- |
| `--drawer-width-sm` | `--semantic-drawer-width-sm` | `min(100vw, 440px)` | 440px            |
| `--drawer-width-md` | `--semantic-drawer-width-md` | `min(100vw, 480px)` | 480px            |
| `--drawer-width-lg` | `--semantic-drawer-width-lg` | `min(100vw, 640px)` | 640px            |
| `--drawer-width-xl` | `--semantic-drawer-width-xl` | `min(100vw, 920px)` | 920px            |

Responsive utilities in `packages/ui/responsive.css` provide `.drawer-mobile-full` and `.drawer-panel-mobile-full` for expanding side drawer panels to full viewport width on mobile viewports (<= 640px).

### Inline Explanation & Gated Action Primitives

- `EntityLink`: Renders Clinical Kernel `NodeRef` links in monospace with K-EXP-2 masked badge support and scope-preserving peek-drawer activation.
- `ClinicalExplanation`: Renders ADR-2482 `Explanation` claims, evaluated met/unmet conditions, reasoning steps, and clickable `NextAction.Target` links.
- `ClinicalGatedAction`: Keeps blocked clinical controls visible with accessible why-blocked explanations.
