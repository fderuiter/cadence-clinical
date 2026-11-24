# Cadence Enterprise UI Components & Design System

The Cadence Clinical platform provides a shared, vanilla CSS-based enterprise design system in `ui` (`packages/ui`).

## Core Principles

- **Zero Tailwind Dependency**: Standard Vanilla CSS scoped styling using centralized CSS Custom Properties (`tokens.css`) across paired light and dark themes (`[data-theme="dark"]`).
- **Full-Width Authoring Workspaces**: High-density clinical tables and matrices occupy full viewport width with `font-variant-numeric: tabular-nums` digit alignment.
- **Accessibility (WCAG 2.2 AA / Section 508)**: ARIA landmarks, keyboard focus management (`useFocusTrap`, `getFocusableElements`, `useEscapeClose`), two-color `:focus-visible` indicators (`2px` `--semantic-color-text-on-solid` inner halo + `3px` `--semantic-color-accent` outer outline delivering `>= 6.34:1` contrast across light, brand, and dark surfaces, with `@media (forced-colors: active)` `Highlight` outlines), live regions for terminology lookup feedback, `48px` enhanced touch targets (`--semantic-touch-target-min`) on `.clinical-input` and `.touch-target`, `24px` minimum pointer targets (`--semantic-control-target-min`) on compact controls per SC 2.5.8, `>= 4.5:1` AA semantic text/status token contrast, and `>= 3:1` interactive control boundary contrast (`--semantic-color-border-control`).
- **Multi-Persona Testing**: Built-in Persona Switcher for verifying RBAC roles across clinical workflows.

---

## Component & Composable Catalog

All 29 Vue components and interaction helpers are exported directly from the root `ui` package:

| Export                                     | Description                                                                                                                                                                                                          | ARIA Landmark / Role                        |
| :----------------------------------------- | :------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | :------------------------------------------ |
| `ClinicalContextBar`                       | Accessible clinical scope breadcrumb landmark linking Studies, study, site, participant, and current workflow                                                                                                        | `navigation`, `list`, `link`                |
| `ClinicalBreadcrumbs`                      | Accessible `<nav aria-label="Breadcrumb">` trail marking the active item with `aria-current="page"` and `48px` mobile touch targets                                                                                  | `navigation`, `list`, `link`                |
| `EntityLink`                               | `NodeRef`-compatible keyboard-operable button or link trigger with `.sr-only` context and peek/open focus restoration                                                                                                | `button` / `link`                           |
| `ClinicalIcon`                             | Zero-emoji `1.75px` stroke-only vector icon primitive (`aria-hidden="true"`) paired with visible labels                                                                                                              | Decorative SVG (`aria-hidden="true"`)       |
| [`ClinicalDataTable`](./data-table.md)     | Accessible, keyboard-sortable, paginated clinical dataset table with focusable rows, row selection, live pagination status, and tabular numerals                                                                     | `table`, `region`, `columnheader`, `status` |
| [`ClinicalModal`](./modal.md)              | Accessible modal dialog with focus trap, `ariaDescribedby` support, and backdrop blur                                                                                                                                | `dialog`, `aria-modal="true"`               |
| [`ClinicalDrawer`](./modal.md)             | Accessible slide-over drawer and `position="bottom"` / `mobileBottomSheet` `90dvh` safe-area bottom sheet with reactive focus containment                                                                            | `dialog`, `aria-modal="true"`               |
| `ClinicalToastHost`                        | Focus-trap-exempt toast host persisting `error` alerts, pausing timed status toasts on hover/focus, and splitting live roles                                                                                         | `region`, `alert`, `status`                 |
| `ClinicalDropzone`                         | Accessible drag-and-drop and native `<button type="button">` file selector with live selected-file status and sibling remove                                                                                         | `button`, `status`                          |
| `ClinicalIconButton`                       | Accessible glyph-only `<button>` requiring a non-empty `label` prop (`aria-label`) with `aria-hidden="true"` glyph slot                                                                                              | `button`                                    |
| `SignOutConfirmModal`                      | Draft-safe voluntary sign-out confirmation dialog with focus containment and unsaved-work warning                                                                                                                    | `dialog`, `aria-modal="true"`               |
| [`PersonaSwitcher`](./persona-switcher.md) | Top-bar multi-role persona switcher (`super_admin`, `sponsor_designer`, `site_crc`, `cra_monitor`, `data_manager`, `auditor`)                                                                                        | `menu`, `menuitem`                          |
| [`AuditLogViewer`](./audit-viewer.md)      | 21 CFR Part 11 immutable audit trail explorer with diff viewer                                                                                                                                                       | `feed`, `article`                           |
| `ClinicalFieldLayout`                      | Shared accessible field wrapper for inputs, radio groups, validation messages, and query flags                                                                                                                       | `group`, `status`                           |
| `ClinicalFormField`                        | Schema-driven CDASH form field dispatcher                                                                                                                                                                            | `group`                                     |
| `ClinicalInput`                            | Accessible 48px touch-target clinical text input                                                                                                                                                                     | `textbox`                                   |
| `ClinicalRadioGroup`                       | Accessible clinical radio group fieldset                                                                                                                                                                             | `radiogroup`                                |
| `ClinicalTabs`                             | Accessible WAI-ARIA tablist with `underline`/`pills`/`segmented` variants, `scroll`/`wrap` overflow containment with edge fade cues, `44px` touch targets, roving `tabindex`, and motion-safe focus scroll alignment | `tablist`, `tab`, `tabpanel`                |
| `ClinicalLookupInput`                      | Real-time terminology lookup field with dynamic status indicators                                                                                                                                                    | `combobox`, `aria-live`                     |
| `ClinicalQueryFlag`                        | Discrepancy query status indicator button                                                                                                                                                                            | `button`                                    |
| `ClinicalQueryPanel`                       | Inline clinical query discussion thread with Part 11 compliance and `Escape` dismissal                                                                                                                               | `region`, `list`                            |
| `AiActionButton`                           | Role-gated AI assist trigger styled with `--semantic-color-ai`                                                                                                                                                       | `button`                                    |
| `AiSuggestionDrawer`                       | Human-in-the-loop AI proposal review drawer                                                                                                                                                                          | `dialog`, `aria-modal="true"`               |
| `ConfidenceBadge`                          | AI confidence score indicator badge                                                                                                                                                                                  | `status`                                    |
| `EmptyState`                               | Shared empty-state placeholder region with optional call-to-action                                                                                                                                                   | `region`                                    |
| `FileUploadModal`                          | Multipart GxP file upload dialog with SHA-256 verification                                                                                                                                                           | `dialog`, `aria-modal="true"`               |
| `MediaPlayer`                              | Clinical PDF, image pan/zoom, audio waveform, and video viewer with GxP watermark support                                                                                                                            | `region`                                    |
| `ViewportPanPad`                           | Single-pointer 4-way directional pan button pad (`>= 24px` target floor) for zoomed media and imaging viewports                                                                                                      | `group`, `button`                           |
| `ModalErrorAlert`                          | Accessible error banner with retry and dismiss actions                                                                                                                                                               | `alert`                                     |
| `createSrOnlyActionButton`                 | Imperative DOM `<button>` builder appending `.sr-only` target context after the visible verb                                                                                                                         | `button` helper                             |
| `useAccessibleViewport`                    | Shared 2D reticle stepping, 4-way pan offsets, two-click endpoint placement, and throttled polite live status announcements                                                                                          | Spatial helper                              |
| `useFocusTrap` / `getFocusableElements`    | Reactive keyboard focus containment, modal stack queries (`hasActiveModalFocusTrap`), and `fallbackFocus` / visible fallback restoration                                                                             | Keyboard helper                             |
| `useEscapeClose`                           | Stack-aware `Escape` dismissal composable                                                                                                                                                                            | Keyboard helper                             |
| `useAsyncRetry`                            | Reactive async execution and retry state helper                                                                                                                                                                      | Async helper                                |
| `callerScope` helpers                      | OIDC `studyId`/`siteId` claim normalisers (`extractAssignedStudies`, `extractAssignedSites`, `extractPrimaryStudyId`, `extractPrimarySiteId`)                                                                        | Scope helper                                |

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

### Side-Over Drawer Width Tokens

Viewport-relative design tokens defined in `packages/ui/tokens.css` specify standard widths for side-over panel drawers:

| Token               | Semantic Token               | Formula             | Target Max Width |
| :------------------ | :--------------------------- | :------------------ | :--------------- |
| `--drawer-width-sm` | `--semantic-drawer-width-sm` | `min(100vw, 440px)` | 440px            |
| `--drawer-width-md` | `--semantic-drawer-width-md` | `min(100vw, 480px)` | 480px            |
| `--drawer-width-lg` | `--semantic-drawer-width-lg` | `min(100vw, 640px)` | 640px            |
| `--drawer-width-xl` | `--semantic-drawer-width-xl` | `min(100vw, 920px)` | 920px            |

Responsive utilities in `packages/ui/responsive.css` provide `.drawer-mobile-full` and `.drawer-panel-mobile-full` for expanding side drawer panels to full viewport width on mobile viewports (<= 640px).
