# ADR-2395: SMART on FHIR Ingestion Pipeline & CRC Source Data Reconciliation Drawer

Also known as: ADR-2199 (former number)

- **Status:** Accepted
- **Status**: Accepted
- **Date**: 2026-08-23
- **Deciders**: Platform Architecture, Clinical Interoperability Team, GxP Compliance & Security
- **Requirements References**: PRD-ESR-001, PRD-ESR-002, PRD-ESR-003, PRD-SYS-122, 21 CFR Part 11, FDA Electronic Source Data in Clinical Investigations

## 1. Context & Problem Statement

Clinical research sites spend an estimated 40–60% of coordinator hours performing manual transcription of Electronic Health Record (EHR) data into electronic Case Report Forms (eCRF). Traditional Direct Data Ingestion approaches either rely on fragile manual batch file uploads or run the risk of uncontrolled background data overwrites that violate FDA regulations on electronic source data, investigator oversight, and GxP data integrity.

To enable secure, automated EHR polling without manual screen scraping or batch file handling while maintaining strict 21 CFR Part 11 compliance and non-destructive review gating, Cadence Clinical requires:
1. **Automated SMART on FHIR Backend Services Authentication**: Asymmetric private-key JWT OAuth2 client credentials (RFC 7523 / HL7 SMART Backend Services) for automated, server-to-server hospital EHR polling without user credentials.
2. **Deterministic CDASH Domain Normalization**: Automated ingestion and mapping of FHIR `Patient`, `Observation` (vital-signs & laboratory), `Condition`, and `MedicationRequest`/`MedicationStatement` resources into CDISC CDASH domains (`DM`, `VS`, `LB`, `MH`, `CM`).
3. **CRC Source Data Reconciliation Drawer**: A split-screen, non-destructive coordinator interface presenting candidate EHR data side-by-side with current eCRF values, extraction timestamps, and FHIR resource provenance.
4. **Non-Destructive Review Gating**: Strict 1-click review primitives ('Accept & Commit', 'Reject with Reason', 'Raise Clinical Query') ensuring zero silent background overwrites, full electronic signatures, and immutable GxP audit ledgers.

## 2. Decision Drivers & Constraints

- **Regulatory Compliance**: FDA Guidance on Electronic Source Data in Clinical Investigations mandates investigator control, clear data origin, immutable provenance, and explicit attribution.
- **Data Integrity (ALCOA+)**: Source EHR data must never silently overwrite eCRF forms in the background without explicit coordinator/investigator review and documented rationale.
- **Security & Standards**: Asymmetric private-key JWT OAuth2 client credentials adhering to RFC 7523 and HL7 SMART on FHIR Backend Services Authorization.
- **User Experience**: Fast, responsive split-screen reconciliation drawer built with 100% Vanilla CSS design tokens (0 Tailwind classes).

## 3. Options Considered

### Option 1: **Option A (Selected)**: SMART on FHIR Backend Services with CDASH Mapping and Non-Destructive CRC Drawer

- **Overview:** **Option A (Selected)**: SMART on FHIR Backend Services with CDASH Mapping and Non-Destructive CRC Drawer
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option B**: Direct Background Database Merges without Human-in-the-Loop Gating (Rejected due to GxP risk)

- **Overview:** **Option B**: Direct Background Database Merges without Human-in-the-Loop Gating (Rejected due to GxP risk)
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead


## 4. Decision Outcome

- **Chosen Option:** Option A.
- **Justification:** Selected as primary architectural approach.

### 1. Asymmetric SMART Backend Services Authentication

- Employs asymmetric cryptography (RS384 or RS256 with 2048+ bit RSA keys).
- The client assertion JWT is signed with the platform's private key containing standard RFC 7523 claims:
  - `iss`: Registered OAuth2 client identifier.
  - `sub`: OAuth2 client identifier (`iss == sub`).
  - `aud`: Token endpoint URL of the hospital EHR server.
  - `jti`: Cryptographically unique UUID (tracked to prevent replay attacks).
  - `exp`: Lifetime bounded to a maximum of 300 seconds (5 minutes).
  - `iat`: Timestamp of assertion issuance.
- Token exchange yields short-lived scoped Bearer tokens for querying FHIR R4 endpoints (`/Patient`, `/Observation`, `/Condition`, `/MedicationRequest`, `/MedicationStatement`).

### 2. CDASH Normalization & Comparison Pipeline

- Extracted FHIR resources are normalized into standard CDASH variables:
  - `Patient` -> `DM.USUBJID`, `DM.SUBJID`, `DM.BRTHDTC`, `DM.AGE`, `DM.SEX`, `DM.RACE`, `DM.ETHNIC` (with PII stripping and deterministic pseudonymization).
  - `Observation` (vital signs & components) -> `VS.SYSBP`, `VS.DIABP`, `VS.PULSE`, `VS.TEMP`, `VS.WEIGHT`, `VS.HEIGHT`, `VS.RESP`, `VS.OXYSAT`, `VS.BMI`.
  - `Observation` (laboratory) -> `LB.GLUC`, `LB.HGB`, `LB.PLAT`, `LB.WBC`, `LB.CREAT`, `LB.ALT`, `LB.AST`, `LB.K`, `LB.SODIUM`, `LB.CA`, `LB.HBA1C`.
  - `Condition` -> `MH.MHTERM`, `MH.MHSTDTC`, `MH.MHSTATUS`.
  - `MedicationRequest` / `MedicationStatement` -> `CM.CMTRT`, `CM.CMDOS`, `CM.CMDOSU`, `CM.CMSTDTC`, `CM.CMSTATUS`.
- Incoming values are compared against current eCRF context to classify discrepancies (`NEW_FIELD`, `EXACT_MATCH`, `VALUE_MISMATCH`, `UNIT_MISMATCH`).

### 3. Non-Destructive Review Gating (CRC Drawer)

- All ingested EHR data is held in the `PENDING_REVIEW` state in `ehr_reconciliation_candidates`.
- **Zero Silent Overwrites**: Data is never merged into eCRF records without explicit action.
- **1-Click Accept & Commit**: Commits candidate value to eCRF target variable, transitions status to `ACCEPTED`, records 21 CFR Part 11 electronic signature metadata, and emits an immutable `InteropAuditLog` with complete source provenance.
- **1-Click Reject with Reason**: Enforces mandatory coordinator justification, transitions status to `REJECTED`, and logs GxP audit record.
- **1-Click Raise Clinical Query**: Opens a formal `ClinicalQuery` discrepancy record in the study query ledger, transitions candidate status to `QUERIED`, and records audit attribution.

## 5. Consequences & Trade-offs

- **Positive**: Eliminates manual double-data entry while providing ironclad regulatory compliance and zero risk of silent data overwrites.
- **Auditing**: Full provenance trail preserved from originating hospital EHR endpoint, raw FHIR resource JSON, transformation rules, down to individual electronic signature commitments.

## 6. Implementation & Verification

- Backend endpoints in `apps/interop/presentation/routers/interop.py`
- Tests in `apps/interop/tests/test_interop.py`
- **Security**: Asymmetric OAuth2 eliminates stored hospital client secrets and ensures bounded token lifetimes.
