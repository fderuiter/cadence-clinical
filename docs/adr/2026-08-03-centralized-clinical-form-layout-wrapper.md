# ADR-254: Centralized Clinical Form Layout Wrapper

- **Status:** Accepted
- **Date:** 2026-08-03
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Prior to this architectural change, our individual clinical input components (`ClinicalInput.vue`, `ClinicalRadioGroup.vue`, and `ClinicalLookupInput.vue`) were heavily coupled with duplicated logic. Each component independently managed query state, validation error representation, query flags, event bubbling logic, and interactive panel toggle states (`ClinicalQueryPanel.vue`).

This structural coupling resulted in significant code duplication across input elements, increased maintenance overhead, and blocked planned styling migrations to modern primitives (such as Radix Vue and Tailwind CSS). To unblock these advancements and guarantee consistent visual and behavioral patterns across the platform, we needed to separate regulatory/query/layout logic from basic input rendering.

This decision addresses the following requirements:

- **PRD-CRF-001:** CRF metadata-driven rendering
- **PRD-QRY-001:** Query State Transitions and Constraints
- **PRD-CRF-009:** Role-Based Authorization Gates

## 2. Decision Drivers & Constraints

- **Driver 1 (Consistency):** Ensure identical alignment, labels, layout structures, and validation error messages across all clinical fields using the unified 12-column grid system.
- **Driver 2 (Compliance):** Enforce strict role-based authorization rules (e.g. CRA vs Investigator permissions on query management) directly and uniformly before query interactions are allowed.
- **Driver 3 (Maintainability):** Reduce boilerplate code and simplify basic clinical inputs, converting them into headless primitives focusing strictly on data entry.
- **Driver 4 (Accessibility):** Standardize keyboard navigation (e.g., handling the `Escape` key to dismiss query panels) and accessibility features like ARIA describedby attributes.

## 3. Options Considered

### Option 1: Decentralized State (Status Quo)

- **Overview:** Retain query-related logic, validation error containers, and interactive panel toggles inside each independent input component.
- **Pros:**
  - ✅ No new components or wrapper layers are introduced.
- **Cons:**
  - ❌ Extreme code duplication of template logic and query-handling event maps.
  - ❌ Brittle integration where new clinical input primitives would have to duplicate all query panel logic.
  - ❌ Disparate accessibility keyboard-handling implementations.

### Option 2: Centralized Layout Wrapper (`ClinicalFieldLayout.vue`)

- **Overview:** Abstract all common layout, label/legend generation, query flags, validation messages, role authorization, and panel toggle logic into a centralized wrapper component (`ClinicalFieldLayout.vue`). Inputs are simplified to act as "headless" elements inside a default slot, utilizing standard Vue slot bindings to share the relevant identifiers.
- **Pros:**
  - ✅ Eliminates redundant code across all input components.
  - ✅ Guarantees identical styling, error messages, and layout behavior across all forms.
  - ✅ Centralizes role validation gates and key listeners (such as closing open query panels on `Escape` keypress).
  - ✅ Simplifies future styling migrations and the onboarding of new input types.
- **Cons:**
  - ❌ Adds a nesting layer in the DOM, which is negligible in terms of performance.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Option 2 cleanly decouples input functionality from regulatory metadata and layout management. This satisfies **PRD-CRF-001** and **PRD-QRY-001** by ensuring that any clinical input component automatically inherits compliant query panel capabilities and role authorization checks without manual reimplementation.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Over 150 lines of duplicate logic were successfully removed from clinical input components.
  - Unified accessibility behavior (Escape key dismissals and focus management) across all input components.
  - Creation of new inputs (e.g. multi-select checkbox grids, datepickers) is now straightforward since the layout wrapper handles all query lifecycle events natively.
- **Negative Impact / Technical Debt:** Minimal. Downstream parent dispatchers must route query-related events from the dynamic fields.
- **Mitigation Strategy:** Automated test suites have been updated and validated to ensure no regression in current query emission mechanisms.

## 6. Implementation & Verification

- **Target Files Modified:**
  - `packages/ui/src/components/clinical/ClinicalFieldLayout.vue`
  - `packages/ui/src/components/clinical/ClinicalInput.vue`
  - `packages/ui/src/components/clinical/ClinicalLookupInput.vue`
  - `packages/ui/src/components/clinical/ClinicalRadioGroup.vue`
  - `packages/ui/src/components/clinical/ClinicalFormField.vue`
- **Verification Plan:**
  - Verify through unit and integration tests: `pnpm --filter web test:unit`
  - Confirm all clinical workflows function using end-to-end tests: `pnpm --filter web test:e2e`
  - Confirm markdown and ADR compliance via `uv run python scripts/validate_adrs.py` and `uv run python scripts/validate_markdown.py`.
