# ADR-2390: Clinical Endpoint Adjudication Committee (CEC) Dossier & 2+1 Blinded Review Suite

Also known as: ADR-2199 (former number)

- **Status:** Accepted
- **Date:** 2026-08-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CEC-001, PRD-CEC-002, PRD-CEC-003, PRD-SYS-124, PRD-SYS-001

---

## 1. Context & Problem Statement

In clinical trials assessing critical primary and secondary endpoints (e.g. Major Adverse Cardiovascular Events [MACE], ischemic/hemorrhagic stroke, cardiovascular mortality, heart failure hospitalization, disease progression), protocol endpoint events must be independently and objectively adjudicated by an expert Clinical Endpoint Adjudication Committee (CEC) per FDA Guidance on Clinical Trial Endpoints and ICH E6(R2)/E9.

Historically, endpoint adjudication systems suffered from several structural flaws:
1. **Manual or Incomplete Dossier Packaging & PHI Leakage:** Compiling diagnostic materials (eCRFs, lab panels, 12-lead ECGs, imaging, narratives) was manual and risked exposing direct patient/site identifiers to blinded reviewers.
2. **Blinding Compromise in Review Workflows:** Reviewers could inadvertently view peer adjudicator assessments prior to submitting their independent findings.
3. **Disconnected EDC and Regulatory SDTM Domains:** Adjudicated determinations lived in isolated spreadsheets or portals, requiring manual, error-prone data reconciliation back to EDC casebooks and CDISC SDTM datasets.
4. **Charter Governance & Part 11 Compliance Deficits:** Voting rules, quorum tracking, and tie-breaker determinations lacked cryptographic proof of integrity and 21 CFR Part 11 compliant electronic signatures.

## 2. Decision Drivers & Constraints

- **Driver 1 (Blinded Reader Isolation & 2+1 Workflow):** Reader 1 and Reader 2 must complete independent, blinded assessments. Concordant reviews automatically finalize (`FINALIZED_CONCORDANT`); discordant reviews escalate to the Committee Chair (`DISCORDANCE_DETECTED` / `ESCALATED_TO_CHAIR`).
- **Driver 2 (Automated Dossier Compilation & In-Flight PHI Redaction):** System must bundle eCRFs, lab panels (with LOINC codes and toxicity grades), 12-lead ECG findings, narratives, and specialty attachments with deterministic date-shifting and tokenized patient/site anonymization (`[REDACTED_PATIENT_NAME]`, `[REDACTED_SITE]`, `[REDACTED_PHYSICIAN]`).
- **Driver 3 (Bi-Directional EDC Writeback & CDISC SDTM CE Model):** Finalized consensus determinations must immediately update the EDC casebook and compile directly into the CDISC SDTM CE (Clinical Events) and SUPPCE domain datasets.
- **Driver 4 (21 CFR Part 11 Electronic Signatures & Auditing):** Tie-breaker adjudications and meeting quorum records must require step-up re-authentication, legal intent manifestation, and immutable audit trailing via `AuditedModel`.
- **Driver 5 (Zero Tailwind Frontend):** The `CecAdjudicationConsole.vue` interface in `apps/web` must strictly adhere to 100% Vanilla CSS design tokens.

## 3. Options Considered

### Option 1: Monolithic Custom Workflow Engine
Build a standalone adjudication portal with an external database.
- ***Pros:*** Decoupled from EDC execution core.
- ***Cons:*** Heavy reconciliation overhead, sync latency, fragmented audit trails, lack of immediate SDTM compilation.



### Option 2: Native Cadence Execution Service with Hexagonal Blinding & CDISC Integration (Chosen)
Implement `CecAdjudicationService`, `CecDossierRecord`, `CecCharterRecord`, and `CecMeetingRecord` directly within `apps/execution`, integrating with `SDTMDomain.CE` and `ClinicalObservation` persistence with role-projected blinding views.
- ***Pros:***
  - ✅ Instant zero-reconciliation EDC writeback and CDISC SDTM CE generation.
  - ✅ Cryptographic dossier and meeting minutes hashing (`SHA-256`).
  - ✅ Strict reader isolation enforced at the API projection layer.
  - ✅ 21 CFR Part 11 step-up electronic signatures integrated with platform security.
  - ✅ Audited model trigger protection across all committee actions.
- ***Cons:***
  - ❌ Requires careful role-based blinding projection logic in API endpoints.



## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Architecture Highlights:**
  1. **Automated Dossier Packaging Engine:** `package_dossier()` consumes multi-source clinical data, applies HIPAA Safe Harbor / Expert de-identification, computes SHA-256 integrity digests, and stores encrypted dossier payloads.
  2. **Role Blinding Projection Engine:** `get_blinded_dossier_view()` projects sanitized representations, hiding peer reader assessments until both primary reviews are submitted or consensus is reached.
  3. **Concordance & Consensus Engine:** `submit_reader_assessment()` evaluates primary diagnostic classification, subtype, and onset date concordance. Concordance triggers automatic finalization and EDC synchronization; discordance escalates to the Committee Chair.
  4. **Chair Tie-Breaker & Part 11 eSign:** `adjudicate_chair_tie_breaker()` enables the Chair to uphold Reader 1, uphold Reader 2, or issue a custom ruling, sealed with a 21 CFR Part 11 electronic signature certificate.
  5. **CDISC SDTM CE Compiler:** `compile_study_sdtm_ce_dataset()` compiles all finalized records into standard CDISC SDTM CE (`STUDYID`, `DOMAIN`, `USUBJID`, `CESEQ`, `CETERM`, `CEDECOD`, `CECAT`, `CESCAT`, `CEPRESP`, `CEOCCUR`, `CESEV`, `CESER`, `CEREL`, `CEOUT`, `CESTDTC`, `CEENDTC`, `EPOCH`) and SUPPCE datasets.
  6. **Interactive Console UI:** `CecAdjudicationConsole.vue` provides 100% Vanilla CSS metrics ribbons, dossier queues, de-identified package inspectors, blinded reader panes, tie-breaker forms, and quorum tracking.

## 5. Consequences & Trade-offs

- **Positive Impact:** End-to-end regulatory compliance with FDA Clinical Trial Endpoints guidance, zero reconciliation latency for safety/biostatistics teams, and complete audit trail immutability.

## 6. Implementation & Verification

- **Verification Plan:** Full automated test coverage in `test_cec_endpoint_adjudication_suite.py` asserting dossier compilation, PHI scrubbing, blinding isolation, concordant auto-finalization, discordant escalation, Chair tie-breaking, meeting quorum calculations, and SDTM CE dataset compilation.

