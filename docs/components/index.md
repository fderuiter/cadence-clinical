# Cadence Enterprise UI Components & Design System

The Cadence Clinical platform provides a shared, vanilla CSS-based enterprise design system in `@cadence/ui` (`packages/ui`).

## Core Principles

- **Zero Tailwind Dependency**: Standard Vanilla CSS scoped styling using centralized CSS Custom Properties (`tokens.css`).
- **Full-Width Authoring Workspaces**: High-density clinical tables and matrices occupy full viewport width.
- **Accessibility (WCAG 2.1 AA / Section 508)**: ARIA landmarks, keyboard focus management, live regions for terminology lookup feedback, and color contrast compliance.
- **Multi-Persona Testing**: Built-in Persona Switcher for verifying RBAC roles across clinical workflows.

---

## Component Catalog

| Component                                  | Description                                                                                                                   | ARIA Landmark / Role              |
| :----------------------------------------- | :---------------------------------------------------------------------------------------------------------------------------- | :-------------------------------- |
| [`ClinicalDataTable`](./data-table.md)     | Accessible, sortable, paginated clinical dataset table with row selection                                                     | `table`, `region`, `columnheader` |
| [`ClinicalModal`](./modal.md)              | Accessible modal dialog with focus trap and backdrop blur                                                                     | `dialog`, `aria-modal="true"`     |
| [`PersonaSwitcher`](./persona-switcher.md) | Top-bar multi-role persona switcher (`super_admin`, `sponsor_designer`, `site_crc`, `cra_monitor`, `data_manager`, `auditor`) | `menu`, `menuitem`                |
| [`AuditLogViewer`](./audit-viewer.md)      | 21 CFR Part 11 immutable audit trail explorer with diff viewer                                                                | `feed`, `article`                 |
| `ClinicalLookupInput`                      | Real-time terminology lookup field with dynamic status indicators                                                             | `combobox`, `aria-live`           |
| `ClinicalQueryPanel`                       | Clinical query discussion thread with Part 11 compliance                                                                      | `region`, `list`                  |

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
