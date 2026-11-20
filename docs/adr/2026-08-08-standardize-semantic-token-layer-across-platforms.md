# ADR-2166: Standardize Semantic Token Layer Across Platforms

- **Status:** Accepted
- **Date:** 2026-08-08
- **Authors:** @jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Previously, the clinician web application (`apps/web`) and the subject-facing portal (`apps/subject-portal`) mapped shared design tokens to conflicting local semantic roles. This resulted in visual drift, prevented UI component reuse, and forced developers to maintain duplicate, application-specific style rules. This duplication and conflicting configuration introduces risks for GxP system validations, specifically around consistent rendering of critical validation warnings and maintaining WCAG 2.1 compliance. To solve this, we must define a uniform semantic token layer in the shared library.

This decision relates to system requirements under **PRD-SYS-001**.

## 2. Decision Drivers & Constraints

- **Driver 1:** Eradication of duplicate component style definitions across the clinician web app and subject-facing portal.
- **Driver 2:** Strict visual consistency for form validation errors and interaction cues (PRD-SYS-001).
- **Driver 3:** Automated adherence to WCAG 2.1 minimum target dimensions (48px) and accessible focus indicators across all apps.

## 3. Options Considered

### Option A (Selected): Centralized Semantic Token Layer in `packages/ui`

Define direct mappings from base variables to semantically-meaningful custom variables (e.g., `--semantic-color-primary`, `--semantic-color-error`) in `packages/ui/tokens.css` along with centralized component CSS styles. Remove all duplicate, app-specific CSS definitions for inputs, radio grids, and validation states.

- _**Pros:**_
  - ✅ Single source of truth for all semantic UI tokens and shared interactive elements.
  - ✅ Guarantees 48px touch target sizes and validation visual cues automatically.
  - ✅ Ensures robust GxP validation predictability across clinical interfaces.
- _**Cons:**_
  - ❌ App-specific visual exceptions must be isolated locally.

### Option B: Local Application mapping variables with import aliases

Each app continues to maintain local mappings to base variables, using compile-time import aliases to pull shared stylesheets.

- _**Pros:**_
  - ✅ Highly customizable local branding rules.
- _**Cons:**_
  - ❌ High risk of layout drift and style duplication over time.
  - ❌ Increases the surface area of manual GxP visual verification audits.

## 4. Decision Outcome

- **Chosen Option:** **Option A** because it enforces a single, centralized semantic contract and standardizes component layout behaviors under `packages/ui/tokens.css`, directly satisfying PRD-SYS-001 and maximizing platform consistency.
- **Justification:** Selected as primary architectural approach.

## 5. Consequences & Trade-offs

- **Positive:** Clear operational boundaries, zero duplicate visual styling rules, automated WCAG 2.1 target sizes, and cleaner local application stylesheets.
- **Negative:** Local application overrides are restricted to isolation behaviors (e.g., query panel display filters) to avoid polluting the core shared token library.

## 6. Implementation & Verification

- **Affected Files:**
  - `packages/ui/tokens.css` (centralized light & `[data-theme="dark"]` semantic token contract, >= 4.5:1 WCAG 2.1 AA contrast pairs, `tabular-nums`, and 48px touch targets)
  - `packages/ui/index.js` & `packages/ui/index.d.ts` (complete root export surface for all 19 shared Vue components and keyboard interaction helpers)
  - `apps/web/src/style.css` (standardized `:root` semantic aliases, AA-compliant designer badges, and deduplicated shared controls)
  - `apps/subject-portal/src/style.css` (standardized `:root` semantic aliases and AA-compliant `.sync-badge`)
- **Verification Plan:**
  - Run design system harmony & coverage tests: `pnpm --filter ui test` (`packages/ui/tests/design_system_harmony.test.js` and `packages/ui/tests/shared_components.test.js`)
  - Run local linting: `pnpm -r lint`
  - Execute ADR and markdown sentinels: `pnpm cadence check adrs` and `pnpm cadence check markdown`
