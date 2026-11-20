# ADR-2232: Financial and Site Operations Engines Standard

- **Status:** Accepted
- **Date:** 2026-08-29
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** `PRD-SYS-001`, `PRD-SYS-003`, `PRD-SYS-050`, `PRD-SYS-053`, `PRD-PAY-001`, `PRD-SSU-001`, `PRD-CTMS-004`

---

## 1. Context & Problem Statement

Clinical trial site payments and site startup (SSU) operations involve complex financial contracts and strict regulatory gating:

1. **Financial Operations**: Site investigator grant contracts require multi-tiered overhead calculation, split payee distributions (e.g. institution vs. investigator fund), withholding taxes, and holdback retention reserves. Invoicing must be event-driven (triggered by clinical execution milestones such as subject enrollment, visit completion, form approval, or query resolution) with exact line item calculation and 21 CFR Part 11 cryptographic sealing.
2. **Site Startup & Regulatory Clearance**: Releasing investigational product (IP) to clinical sites requires multi-criteria regulatory clearance gating: verification of Institutional Review Board / Ethics Committee (IRB/EC) approvals, executed Clinical Trial Agreements (CTA), Essential Documents / eTMF checklists (Form FDA 1572, CVs, GCP certificates, FDF), protocol-specific staff training logs, Delegation of Authority (DOA) logs, and Qualified Person (QP) release authorizations.

## 2. Decision Drivers & Constraints

- **Zero-I/O In-Memory Engine Core**: Per `ADR-2228`, financial ledger calculations and site startup clearance evaluations must execute as pure, zero-I/O in-memory functional domain engines in `packages/engine_core/`.
- **21 CFR Part 11 Cryptographic Audit Traceability**: Every generated payable line item, invoice ledger rollup, and site greenlight assessment must produce a deterministic SHA-256 validation seal.
- **Tiered Financial Math & Rounding Invariants**: Multi-tier overhead curves, percentage splits, withholding deductions, and holdback retention reserves must maintain exact ledger balance without floating-point cent leakage.
- **Deficiency Diagnostics**: SSU clearance evaluations must provide granular, actionable deficiency diagnostics categorizing why a site is blocked from IP release (e.g. expired IRB approval, untrained Sub-Investigator, unexecuted CTA).

## 3. Options Considered

### Option 1: **Option A (Selected) - Modular Pure In-Memory Financial & SSU Engines in `packages/engine_core/`**:

- **Overview:** - `packages/engine_core/financial/`: `PayableLedgerEngine`, `GrantContract`, `FeeMatrixItem`, `PayeeSplitRule`, multi-tier overhead and holdback retention calculations.
  - `packages/engine_core/operations/`: `SiteGreenlightEngine`, multi-stage regulatory clearance matrices, deficiency diagnostics, and greenlight status determination (`BLOCKED`, `CONDITIONAL_RELEASE`, `GREENLIGHT_CLEARED`).
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option B - Direct Database Coupling in `apps/payments` and `apps/ctms`**:

- **Overview:** - Tightly coupling financial and greenlight logic with SQL ORM tables inside application services.
  - _Rejected_: Violates clean hexagonal boundaries and prevents pure domain testing across microservices.
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

## 4. Decision Outcome

**Chosen Option:** **Option A**.

1. **Event-Triggered Payable Ledger & Fee Matrix Engine (`packages/engine_core/financial/`)**:
   - Ingests `GrantContract` and `FeeMatrixItem` definitions.
   - Evaluates incoming `ClinicalEvent` instances against fee schedules.
   - Computes tiered overhead rates, split payee distributions, withholding deductions, and holdback reserve balances.
   - Produces `PayableLineItem` records and consolidated `LedgerSummary` with SHA-256 audit seals.

2. **Site Startup Greenlight Engine (`packages/engine_core/operations/`)**:
   - Evaluates comprehensive readiness matrices covering IRB/EC approvals, executed CTAs, Essential Documents, staff protocol training, DOA logs, and IP release criteria.
   - Diagnoses blocking deficiencies (`DeficiencyItem`) and outputs `SiteGreenlightAssessment` with cryptographic verification.

## 5. Consequences & Trade-offs

- **Positive**:
  - High performance sub-millisecond in-memory calculations for complex site payment batches and startup readiness checks.
  - Complete testability with pure input/output assertions and zero database mocking required.
  - Consistent domain contracts across CTMS, Execution, and Payments microservices.
- **Negative**:
  - Requires maintaining dedicated transport adapters in presentation layers.

## 6. Implementation & Verification

- **Core Package**: `packages/engine_core/financial/`, `packages/engine_core/operations/`
- **Test Suites**: `packages/engine_core/tests/test_payable_ledger_engine.py`, `packages/engine_core/tests/test_site_greenlight_engine.py`
- **Compliance Traceability**: Annotated with `@req:PRD-SYS-050`, `@req:PRD-PAY-001`, `@req:PRD-SSU-001`, `@req:PRD-CTMS-004`.
