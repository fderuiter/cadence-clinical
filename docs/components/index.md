# Cadence Enterprise UI Components & Design System

The Cadence Clinical platform provides a shared, vanilla CSS-based enterprise
design system in `ui` (`packages/ui`).

## Core Principles

- **Zero Tailwind Dependency**: Standard Vanilla CSS scoped styling using
  centralized CSS Custom Properties (`tokens.css`) across paired light and dark
  themes (`[data-theme="dark"]`).
- **Full-Width Authoring Workspaces**: High-density clinical tables and matrices
  occupy full viewport width with `font-variant-numeric: tabular-nums` digit
  alignment.
- **Accessibility (WCAG 2.2 AA / Section 508)**: ARIA landmarks, keyboard focus
  management (`useFocusTrap`, `getFocusableElements`, `useEscapeClose`), live
  regions for terminology lookup feedback, 48px touch targets, `>= 4.5:1` AA
  semantic text/status token contrast, and `>= 3:1` interactive control boundary
  contrast (`--semantic-color-border-control`).
- **Multi-Persona Testing**: Built-in Persona Switcher for verifying RBAC roles
  across clinical workflows.

---

## Component & Composable Catalog

All 20 Vue components and interaction helpers are exported directly from the
root `ui` package:

- [`ClinicalDataTable`](./data-table.md) (`table`, `region`, `columnheader`):
  Accessible, sortable, paginated clinical dataset table with row selection and
  tabular numerals.
- [`ClinicalModal`](./modal.md) (`dialog`, `aria-modal="true"`): Accessible
  modal dialog with focus trap and backdrop blur.
- [`ClinicalDrawer`](./modal.md) (`dialog`, `aria-modal="true"`): Accessible
  slide-over drawer with reactive focus containment.
- `SignOutConfirmModal` (`dialog`, `aria-modal="true"`): Draft-safe voluntary
  sign-out confirmation dialog with focus containment and unsaved-work warning.
- [`PersonaSwitcher`](./persona-switcher.md) (`menu`, `menuitem`): Top-bar
  multi-role persona switcher (`super_admin`, `sponsor_designer`, `site_crc`,
  `cra_monitor`, `data_manager`, `auditor`).
- [`AuditLogViewer`](./audit-viewer.md) (`feed`, `article`): 21 CFR Part 11
  immutable audit trail explorer with diff viewer.
- `ClinicalFieldLayout` (`group`, `status`): Shared accessible field wrapper for
  inputs, radio groups, validation messages, and query flags.
- `ClinicalFormField` (`group`): Schema-driven CDASH form field dispatcher.
- `ClinicalInput` (`textbox`): Accessible 48px touch-target clinical text input.
- `ClinicalRadioGroup` (`radiogroup`): Accessible clinical radio group fieldset.
- `ClinicalLookupInput` (`combobox`, `aria-live`): Real-time terminology lookup
  field with dynamic status indicators.
- `ClinicalQueryFlag` (`button`): Discrepancy query status indicator button.
- `ClinicalQueryPanel` (`region`, `list`): Inline clinical query discussion
  thread with Part 11 compliance and `Escape` dismissal.
- `AiActionButton` (`button`): Role-gated AI assist trigger styled with
  `--semantic-color-ai`.
- `AiSuggestionDrawer` (`dialog`, `aria-modal="true"`): Human-in-the-loop AI
  proposal review drawer.
- `ConfidenceBadge` (`status`): AI confidence score indicator badge.
- `EmptyState` (`region`): Shared empty-state placeholder region with optional
  call-to-action.
- `FileUploadModal` (`dialog`, `aria-modal="true"`): Multipart GxP file upload
  dialog with SHA-256 verification.
- `MediaPlayer` (`region`): Clinical PDF, image pan/zoom, audio waveform, and
  video viewer with GxP watermark support.
- `ModalErrorAlert` (`alert`): Accessible error banner with retry and dismiss
  actions.
- `useFocusTrap` / `getFocusableElements` (Keyboard helper): Reactive keyboard
  focus containment and focusable element discovery.
- `useEscapeClose` (Keyboard helper): Stack-aware `Escape` dismissal composable.
- `useAsyncRetry` (Async helper): Reactive async execution and retry state
  helper.

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

Viewport-relative design tokens defined in `packages/ui/tokens.css` specify
standard widths for side-over panel drawers:

| Token               | Semantic Token               | Formula             | Target Max Width |
| :------------------ | :--------------------------- | :------------------ | :--------------- |
| `--drawer-width-sm` | `--semantic-drawer-width-sm` | `min(100vw, 440px)` | 440px            |
| `--drawer-width-md` | `--semantic-drawer-width-md` | `min(100vw, 480px)` | 480px            |
| `--drawer-width-lg` | `--semantic-drawer-width-lg` | `min(100vw, 640px)` | 640px            |
| `--drawer-width-xl` | `--semantic-drawer-width-xl` | `min(100vw, 920px)` | 920px            |

Responsive utilities in `packages/ui/responsive.css` provide
`.drawer-mobile-full` and `.drawer-panel-mobile-full` for expanding side drawer
panels to full viewport width on mobile viewports (<= 640px).
