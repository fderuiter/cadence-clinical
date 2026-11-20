# ADR-2206: Cell & Gene Therapy Vein-to-Vein Chain of Custody and Identity

- **Status:** Accepted
- **Date:** 2026-08-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Advanced Therapy Medicinal Products (ATMPs), including autologous CAR-T cell therapies, gene-modified cellular products, and neoantigen personalized vaccines, require unbroken, tamper-evident Vein-to-Vein Chain of Custody (COC) and Chain of Identity (COI). Unlike standard small molecules or off-the-shelf biologics, autologous cell products are patient-specific: infusing a misallocated or mismatched batch into the wrong recipient is fatal (graft-versus-host disease or acute ABO/HLA incompatibility).

Under **PRD-SCM-005** and **PRD-SCM-006**, the Cadence RTSM and Execution engine (`apps/execution`) must enforce:

1. Tamper-evident cryptographic barcode generation binding Subject UUID, ISBT 128 Donation Identification Number (DIN), and manufacturing batch records.
2. Continuous cryogenic transit tracking (-196°C liquid nitrogen vapor phase to -70°C dry ice) with GPS milestones and automated excursion quarantines.
3. Mandatory bedside two-person electronic verification (e.g. Bedside Nurse + Principal Investigator dual e-signatures) before unlocking the drug infusion eCRF.
4. Formal Certificates of Destruction with dual witness electronic signatures for returned or residual kit and vial reconciliation.
5. Strict 21 CFR Part 11 electronic audit trail immutability.

## 2. Decision Drivers & Constraints

- **Zero-Mismatch Patient Safety:** Immediate cryptographic detection of any mismatch between patient wristband and autologous cell product bag.
- **GxP & 21 CFR Part 11 Compliance:** All state transitions, temperature logs, dual-witness e-signatures, and destruction certificates must be recorded in trigger-backed shadow audit tables (`audit_logs`) with full attribution and non-repudiation.
- **ISBT 128 Standard Conformance:** Donation Identification Numbers (DIN) must follow the global ISBT 128 standard with ISO 7064 Mod 37-2 check character algorithms.
- **Bedside Step-Up Gating:** Drug infusion forms in EDC must remain completely locked until primary (Nurse) and secondary (PI) verifiers execute successful dual e-signatures.
- **Blinding & Trial Locking:** Conformance to `TrialLockManager` site-level and study-level locks.

## 3. Options Considered

### Option 1: **Option A: Integrated Vein-to-Vein Domain within Execution Service (Selected)**

- **Overview:** - Model `CGTChainOfCustody`, `CGTMilestoneEvent`, `CGTBedsideVerification`, `CGTDestructionCertificate`, and `CGTVialReturnReconciliation` in `apps/execution/database/models/rtsm.py`.
  - Implement ISBT 128, barcode hashing, cryogenic tracking, and bedside gating in `apps/execution/rtsm_cgt.py`.
  - Expose REST API endpoints via `apps/execution/presentation/routers/rtsm.py`.
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option B: Standalone External CGT Track-and-Trace Service**

- **Overview:** - Disadvantages: Fragmented relational data model, latency across bedside e-signatures, and increased integration complexity.
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

## 4. Decision Outcome

- **Chosen Option:** **Option A** because it natively inherits the validated GxP audit trigger foundation, enforces PostgreSQL transactional integrity, integrates seamlessly with subject visit scheduling and `TrialLockManager`, and ensures zero patient-batch mismatch.
- **Justification:** Selected as primary architectural approach.

### Key Architectural Components:

1. **ISBT 128 DIN Engine:** Deterministic facility-code, year, sequence, and ISO 7064 Mod 37-2 check character calculation.
2. **Cryptographic Barcode Engine:** HMAC-SHA256 digital signature binding `{study_id, site_id, subject_id, din, batch_id, timestamp}` into a 2D DataMatrix payload.
3. **Cryogenic Transit & Milestone Tracking:** Real-time logging of GPS coordinates and temperature readings across all stages.
4. **Bedside Dual Verification Engine:** Mandatory distinct dual e-signatures (`primary_verifier_id != secondary_verifier_id`), wristband UUID matching, and cryptographic bag barcode verification producing an unlock token.
5. **Formal Destruction Engine:** Dual-witness e-signatures (`witness_1_id != witness_2_id`), SHA-256 certificate sealing, and automatic transitioning of `IPKit` and reconciliation records to `DESTROYED`.

## 5. Consequences & Trade-offs

- **Positive:**
  - Absolute protection against patient-batch mismatch in cell and gene therapy clinical trials.
  - End-to-end auditability and compliance with FDA 21 CFR Part 11, EU GMP Annex 13, and ISBT 128.
  - Seamless outbox notifications.
- **Negative / Overhead:**
  - Additional database tables and audit ledger entries.
- **Mitigation:**
  - High-performance indexed lookups and fast HMAC-SHA256 cryptographic routines keep endpoint latency under 10ms.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/execution/` (models, services, presentation routers).
- **Verification Plan:**
  - Comprehensive unit and integration tests in `apps/execution/tests/test_rtsm_cell_gene_therapy_coc_coi.py`.
  - GxP compliance synchronization via `scripts/sync_gxp.py`.
