# ADR-2160: Shared Zod Schemas and Backend Immutability

- **Status:** Accepted
- **Date:** 2026-09-10
- **Authors:** @jules
- **Deciders:** @jules

---

## 1. Context & Problem Statement

To enforce high data integrity and ensure alignment with the CDISC Unified Study Design Model (USDM) standards, we need to guarantee that once clinical study design elements (such as Study Arms, Epochs, and Activities) are created or published, they cannot be modified in an unregulated/non-destructive manner. Additionally, we must bridge the gap between backend data models and frontend validation. This requires a shared schema definition mechanism so that frontend mutations are strictly validated prior to submission, and the backend enforces immutability on active study states, in accordance with PRD-MDR-002 and Trace-30.

## 2. Decision Drivers & Constraints

- **Data Integrity & Compliance:** Enforce 21 CFR Part 11 and GxP standards by preventing unauthorized modifications to active study design components (PRD-MDR-002, Trace-30).
- **Single Source of Truth:** Eliminate schema definition drift between frontend validation and backend database modeling.
- **Developer Velocity:** Facilitate automatic TypeScript typing from Zod validation schemas.

## 3. Options Considered

### Option 1: Fragmented Validation (Separate Backend and Frontend Definitions)

- **Overview:** Define Pydantic models on the Python backend and separate TypeScript interfaces/validators on the frontend.
- **Pros:**
  - ✅ Quick to implement initially.
- **Cons:**
  - ❌ High risk of schema drift.
  - ❌ No automated compile-time or build-time verification between layers.

### Option 2: Shared Zod/TypeScript Schemas with Backend Immutability Constraints (Selected)

- **Overview:** Use a shared package (`usdm-schemas`) to publish Zod definitions of CDISC USDM components, while configuring Python-side domain layer models (`apps/designer/domain/cdisc/usdm_models.py`) with strict immutability checks.
- **Pros:**
  - ✅ Strict single source of truth for validation.
  - ✅ Fulfills PRD-MDR-002 and Trace-30 requirements perfectly.
  - ✅ Easy to keep frontend client validation in sync.
- **Cons:**
  - ❌ Requires synchronization across package build pipelines.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Implementing shared Zod schemas (`usdm-schemas`) ensures the frontend validates inputs exactly according to USDM specifications. Combining this with strict backend domain models in `apps/designer/domain/cdisc/usdm_models.py` enforces backend immutability for active studies (PRD-MDR-002, Trace-30) and audit log traceability (PRD-SYS-001).

## 5. Consequences & Trade-offs

- **Positive Impact:** Zero schema drift between layers, immediate validation feedback at the UI level, and secure GxP-compliant backend lock checks.
- **Negative Impact / Technical Debt:** Requires keeping package exports updated.
- **Mitigation Strategy:** Automated CI validation will enforce schema parity.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `apps/designer/domain/cdisc/usdm_models.py`
  - `apps/web/src/stores/clinical.js`
  - `pyproject.toml`
- **Verification Plan:**
  - Backend immutability unit tests: `apps/designer/tests/test_usdm_immutability.py`
  - Automated ADR checks: `python3 scripts/validate_adrs.py`
  - Requirements Traceability Matrix validation: `uv run python scripts/sync_gxp.py`
