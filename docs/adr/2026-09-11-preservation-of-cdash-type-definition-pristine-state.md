# ADR-328: Preservation of CDASH Type Definition Pristine State

- **Status:** Accepted
- **Date:** 2026-09-11
- **Authors:** @jules
- **Deciders:** @validator, @reviewer

---

## 1. Context & Problem Statement

The Cadence Clinical platform relies on precise, standard-compliant schema definitions from Clinical Data Acquisition Standards Harmonization (CDASH) v1.3 and CDASHIG v2.3. The type definitions in `packages/ui/src/types/cdash.ts` were carefully derived from official specifications. Standard code formatters such as Prettier may format property quoting and multi-line structures in TypeScript type definitions, transforming quoted keys to unquoted ones. While syntactically valid in standard TypeScript, such transformations disrupt the strict raw mapping validation scripts that verify exact matching against external JSON clinical models.

This decision directly implements requirements under PRD-CRF-006 to preserve type definition schemas without structural drift.

We need to preserve the exact, pristine property quoting and layout in `packages/ui/src/types/cdash.ts` while still allowing monorepo-wide code formatting checks to pass successfully.

## 2. Decision Drivers & Constraints

- **Driver 1:** Regulatory GxP Compliance (21 CFR Part 11 / Annex 11 alignment) and preservation of pristine schema definitions.
- **Driver 2:** Automated Code Quality (ensuring `pnpm -r format` and lint checks pass cleanly).
- **Driver 3:** Zero-network static analysis and strict ADR gating.

## 3. Options Considered

### Option 1: Re-format cdash.ts on Every Commit

- **Overview:** Allow formatting to happen and manually adjust matching scripts to handle both quoted and unquoted attributes.
- **Pros:**
  - ✅ Keeps Prettier completely default.
- **Cons:**
  - ❌ High maintenance overhead and risk of mismatched keys.

### Option 2: Add CDASH Type Definition to Prettier Ignore Configuration (Selected)

- **Overview:** Exclude `packages/ui/src/types/cdash.ts` from Prettier formatting using directory-agnostic glob patterns in `.prettierignore`.
- **Pros:**
  - ✅ Preserves exact property quoting and pristine state for the CDASH schemas.
  - ✅ Restricts automatic formatting from introducing architectural file differences.
- **Cons:**
  - ❌ Requires tracking the ignore pattern at the repository root.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Excluding `packages/ui/src/types/cdash.ts` via `.prettierignore` is the most robust way to guarantee the CDASH definitions remain pristine and compliant without breaking monorepo formatting workflows.

## 5. Consequences & Trade-offs

- **Positive Impact:** Pristine structure is fully locked and protected from formatting drift.
- **Negative Impact / Technical Debt:** Requires a `.prettierignore` rule at the repository root level.
- **Mitigation Strategy:** Documented in `.prettierignore` with comments pointing to this ADR.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`
- **Verification Plan:** Verify that `pnpm -r format` and the ADR validation script pass cleanly.
