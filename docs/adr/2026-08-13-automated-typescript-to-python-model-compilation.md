# ADR-[NUMBER]: Automated TypeScript-to-Python Model Compilation

- **Status:** Accepted
- **Date:** 2026-08-13
- **Authors:** @google-labs-jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Upstream TypeScript clinical study schema modifications in the Cadence platform can break downstream Python biostatistical pipelines and data validation scripts. To prevent structural drift and ensure a robust, metadata-driven eClinical workflow, we must establish a single source of truth for the USDM schemas and automatically compile them to downstream language targets (Python/Pydantic). This addresses Trace-24 to maintain complete schema and mapping fidelity.

## 2. Decision Drivers & Constraints

- **Single Source of Truth:** Maintain clinical schemas purely in TypeScript (`packages/usdm-schemas/src/index.ts`) as designated.
- **Fidelity and Preservation:** Retain exact camelCase fields, snake_case mappings, defaults, and aliases in Python.
- **Developer Velocity:** Automatic compilation should happen during local development and builds without manual code synchronization.
- **Compliance:** Support regulatory field tracking and strict Pydantic v2 validation.

## 3. Options Considered

### Option 1: Manual Synchronization

- **Overview:** Rely on developers to manually synchronize TypeScript schemas and Pydantic models.
- **Pros:**
  - ✅ Simple to implement initially.
- **Cons:**
  - ❌ High risk of drift, manual mistakes, and broken downstream pipelines.

### Option 2: Automated Compilation via AST/Regular Expressions (Selected)

- **Overview:** Use a schema compiler in `scripts/generate_schemas.py` to parse TypeScript/Zod schemas and compile equivalent, strictly-typed Pydantic v2 validation classes inside `apps/designer/domain/cdisc/usdm_models.py`.
- **Pros:**
  - ✅ Establishes TypeScript as the single source of truth.
  - ✅ Guaranteed field-for-field parity with automatic formatting (Ruff) and validation.
- **Cons:**
  - ❌ Requires parsing logic in python, but is highly robust and validated in testing.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Implementing an inverted compiler ensures 100% fidelity between TS schemas and Python types, fully satisfying Trace-24. It integrates with `pnpm --filter usdm-schemas build` so every schema change is instantly compiled and formatted with Ruff.

## 5. Consequences & Trade-offs

- **Positive Impact:** No more manual schema drift or accidental pipeline breakages. Parity is enforced in CI/CD.
- **Negative Impact / Technical Debt:** Added dependency on the generator parsing script.
- **Mitigation Strategy:** Any changes to schemas are guarded by AST drift checkers and comprehensive pytest validation blocks.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `packages/usdm-schemas/`
  - `apps/designer/`
  - `scripts/`
- **Verification Plan:**
  - Validated via `python3 scripts/validate_adrs.py` and `uv run pytest`.
