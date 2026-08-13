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

- **Pros:**
  - ✅ Single source of truth for all semantic UI tokens and shared interactive elements.
  - ✅ Guarantees 48px touch target sizes and validation visual cues automatically.
  - ✅ Ensures robust GxP validation predictability across clinical interfaces.
- **Cons:**
  - ❌ App-specific visual exceptions must be isolated locally.

### Option B: Local Application mapping variables with import aliases

Each app continues to maintain local mappings to base variables, using compile-time import aliases to pull shared stylesheets.

- **Pros:**
  - ✅ Highly customizable local branding rules.
- **Cons:**
  - ❌ High risk of layout drift and style duplication over time.
  - ❌ Increases the surface area of manual GxP visual verification audits.

## 4. Decision Outcome

Chosen option: **Option A** because it enforces a single, centralized semantic contract and standardizes component layout behaviors under `packages/ui/tokens.css`, directly satisfying PRD-SYS-001 and maximizing platform consistency.

## 5. Consequences & Trade-offs

- **Positive:** Clear operational boundaries, zero duplicate visual styling rules, automated WCAG 2.1 target sizes, and cleaner local application stylesheets.
- **Negative:** Local application overrides are restricted to isolation behaviors (e.g., query panel display filters) to avoid polluting the core shared token library.

## 6. Implementation & Verification

- **Affected Files:**
  - `packages/ui/tokens.css` (centralized token contract & shared component styles)
  - `apps/web/src/style.css` (eliminated duplicated classes)
  - `apps/subject-portal/src/style.css` (eliminated duplicated classes)
- **Verification Plan:**
  - Run local formatting: `pnpm -r format`
  - Run local linting: `pnpm -r lint`
  - Execute local ADR and schema checks: `uv run python scripts/validate_adrs.py`
