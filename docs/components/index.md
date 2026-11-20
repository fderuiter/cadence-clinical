# Cadence Enterprise UI Components & Design System

The Cadence Clinical platform provides a shared, vanilla CSS-based enterprise design system in `@cadence/ui` (`packages/ui`).

## Core Principles

- **Zero Tailwind Dependency**: Standard Vanilla CSS scoped styling using centralized CSS Custom Properties (`tokens.css`).
- **Full-Width Authoring Workspaces**: High-density clinical tables and matrices occupy full viewport width.
- **Accessibility (WCAG 2.2 AA / Section 508)**: ARIA landmarks, keyboard focus management, live regions for terminology lookup feedback, `>= 4.5:1` semantic text/status token contrast across light surfaces, and `>= 3:1` interactive control boundary contrast (`--semantic-color-border-control`).
- **Multi-Persona Testing**: Built-in Persona Switcher for verifying RBAC roles across clinical workflows.

---

## Component Catalog

| Component                      | Description                                                       | ARIA Landmark / Role          |
| :----------------------------- | :---------------------------------------------------------------- | :---------------------------- |
| [`ClinicalModal`](./modal.md)  | Accessible modal dialog with focus trap and backdrop blur         | `dialog`, `aria-modal="true"` |
| [`ClinicalDrawer`](./modal.md) | Accessible slide-over drawer with reactive focus containment      | `dialog`, `aria-modal="true"` |
| `ClinicalLookupInput`          | Real-time terminology lookup field with dynamic status indicators | `combobox`, `aria-live`       |
| `ClinicalQueryPanel`           | Inline clinical query discussion thread with `Escape` dismissal   | `region`, `list`              |

---

## Design System Tokens

Tokens are imported from [tokens.css](../../packages/ui/tokens.css):

```css
@import "@cadence/ui/tokens.css";

.custom-card {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md, 8px);
  color: var(--color-text);
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
