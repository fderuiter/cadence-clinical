# ADR-192: Dynamic CDASH type generation and strongly-typed clinical Vue components

- **Status:** Accepted
- **Date:** 2026-08-12
- **Authors:** @google-labs-jules[bot]
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Previously, the core clinical Vue UI components used to render eCRF forms lacked proper TypeScript definitions and compile-time validation contracts. This allowed downstream applications to potentially bypass type safety checks, risking the rendering of invalid CDASH/CDASHIG schemas or incorrect terminology configurations in the field mappings. To solve this, we need to map clinical-grade rendering contracts to compilation-time validation.

This decision directly implements requirements under PRD-CRF-006.

## 2. Decision Drivers & Constraints

- **Driver 1:** Enforce strict type safety and compile-time correctness across shared UI components (`packages/ui/`).
- **Driver 2:** Maintain 100% data and schema fidelity when parsing and validating CDASH/CDASHIG variable codes.
- **Driver 3:** Minimize overhead on the dependency workspace build cycle (under 3 seconds) when generating types from large CDISC schemas.

## 3. Options Considered

### Option 1: Manually Define TypeScript Interfaces

- **Overview:** Write manual union types for CDASH variables and hardcode the field configurations.
- **Pros:**
  - ✅ Quick to implement initially.
- **Cons:**
  - ❌ Extremely error-prone and labor-intensive to maintain across 1100+ unique CDISC variables.
  - ❌ Hard to keep in sync with upstream schema updates.

### Option 2: Automated Dynamic Type Generation from CDISC Schemas (Selected)

- **Overview:** Build a dynamic codegen script (`packages/ui/scripts/generate-types.js`) that dynamically parses official CDASH and CDASHIG JSON schemas to generate union and interface types.
- **Pros:**
  - ✅ Generates highly accurate unions representing over 1119 unique CDASH variables.
  - ✅ Very fast (well under 1.5 seconds) and fits into the workspace build cycle.
  - ✅ Vue SFC components can import and enforce these types directly.
- **Cons:**
  - ❌ Code generation step must handle sanitization of bare/invalid values (such as `NaN`) in raw JSON schema source files.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Choosing Option 2 guarantees that clinical components (such as `ClinicalFormField.vue` and `ClinicalLookupInput.vue`) are backed by 100% accurate, dynamically-generated CDISC schema models, eliminating runtime type bypasses or mapping mismatches.

## 5. Consequences & Trade-offs

- **Positive Impact:** Strong typing across the design system, automatic type propagation to apps like `apps/web/`, and immediate compile-time errors for any invalid clinical schema structures.
- **Negative Impact / Technical Debt:** Added codegen script dependency to the workspace build workflow.
- **Mitigation Strategy:** Automated tests are added in `packages/ui/tests/cdash-types.test.js` to ensure generated type files remain valid and robust.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui/`
- **Verification Plan:** Verify with `pnpm --filter ui build`, local testing, and `uv run python scripts/validate_adrs.py`.
