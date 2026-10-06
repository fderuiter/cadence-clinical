# ADR-2809: Clinical Endpoint Committee (CEC) Adjudication FSM and CDISC SDTM CE Compiler Schema

- **Status:** Accepted
- **Date:** 2026-10-06
- **Authors:** @cadence-dev
- **Deciders:** @cadence-architecture

---

## 1. Context & Problem Statement

Clinical trial endpoint adjudication requires independent, blinded assessments by multiple Clinical Endpoint Committee (CEC) Readers, followed by automated concordance evaluation. When Reader assessments disagree, the adjudication must escalate to a CEC Chair who acts as a tie-breaker. The Chair's decision requires 21 CFR Part 11 compliant step-up electronic signatures (`X-Sig-Token` header, password re-authentication, SHA-256 payload digest). Additionally, candidate dossier packaging requires deterministic date shifting and tokenized PHI redaction, while finalized adjudications must compile into standardized CDISC SDTM CE (Clinical Events) datasets.
@req:PRD-CEC-001

## 2. Decision Drivers & Constraints

- **GxP Compliance & 21 CFR Part 11:** Electronic signatures for Chair tie-breaker decisions must re-authenticate credentials, verify SHA-256 payload hashes, and log immutable audit trails.
- **Blinded Assessment Integrity:** Reader 1 and Reader 2 assessment projections must remain strictly blinded to peer reviewers until both submit primary assessments or until Chair escalation occurs.
- **CDISC SDTM CE Standard:** Finalized adjudications must support deterministic compilation into SDTM CE domain structure (`STUDYID`, `DOMAIN`, `USUBJID`, `CESEQ`, `CETERM`, `CEDECOD`, `CECAT`, `CESCAT`, `CEPRESP`, `CEOCCUR`, `CESTDTC`).
- **Data Privacy & PHI Redaction:** Dossier generation must systematically redact patient identifiers using deterministic date shifting and tokenized PHI masks (`[REDACTED_PATIENT_NAME]`, `[REDACTED_SITE]`, `[REDACTED_PHYSICIAN]`).

## 3. Options Considered

### Option 1: Client-side Blinding and Unstructured Adjudication Storage

- **Overview:** Perform blinding logic and concordance calculation on the frontend, storing unblinded assessments in a loose key-value JSON column.
- **Pros:**
  - Fast to prototype.
- **Cons:**
  - ❌ Risk of data leaks where blinded assessments are exposed over the network.
  - ❌ Lack of GxP auditability and non-compliance with 21 CFR Part 11 step-up signature verification.

### Option 2: Server-side FSM, DTO-Level Role-Blinded Projections, 21 CFR Part 11 Step-Up Signatures, and SDTM CE Compiler (Selected)

- **Overview:** Enforce state machine transitions in the `CecDossier` aggregate root in `.NET 10 ExecutionDbContext`. Expose DTO-level projection methods (`GetBlindedDossierView`) that strip peer assessments dynamically based on role/state. Require 21 CFR Part 11 step-up re-authentication headers (`X-Sig-Token`) for Chair adjudications and offer a dedicated CDISC SDTM CE compiler.
- **Pros:**
  - ✅ Fail-safe server-side blinding prevention against unauthorized data disclosure.
  - ✅ Cryptographic auditability and 21 CFR Part 11 compliance for Chair tie-breaker adjudications.
  - ✅ Standardized CDISC SDTM CE data pipeline for regulatory export.
- **Cons:**
  - ❌ Requires database schema migration and explicit state transition logic.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Option 2 guarantees regulatory compliance, zero-leak role blinding, and automated CDISC SDTM CE export capability.

## 5. Consequences & Trade-offs

- **Positive Impact:** Robust GxP compliance, automated concordance handling, role-blinded REST projections, and 100% CDISC SDTM CE export fidelity.
- **Negative Impact / Technical Debt:** Added migration schema `20261006192347_AddCecDossierAdjudication` to `ExecutionDbContext`.
- **Mitigation Strategy:** High-coverage unit tests and GxP requirement annotations (`PRD-CEC-001`, `PRD-CEC-002`, `PRD-CEC-003`).

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/execution` (Domain, Application, Endpoints, Infrastructure/EF Core), `apps/web` (`CecAdjudicationConsole.vue`).
- **Verification Plan:** Unit tests in `tests/Cadence.UnitTests/Execution/Cec/` and automated execution of `pnpm cadence check`.
