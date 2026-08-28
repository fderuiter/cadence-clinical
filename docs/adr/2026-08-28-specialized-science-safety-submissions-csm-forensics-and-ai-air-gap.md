# ADR-2223: Specialized Science Safety Submissions CSM Forensics and AI Air Gap

* **Status:** Accepted
* **Date:** 2026-08-28
* **Authors:** @fderuiter
* **Deciders:** Engineering Architecture Board, Pharmacovigilance Working Group, Data Science & Quality Committee
* **Requirements:** PRD-SYS-001, PRD-SAF-001, PRD-SAF-002, PRD-RBQM-001, PRD-IMG-001, PRD-AI-001

---

## 1. Context & Problem Statement

Specialized clinical trials require high-velocity safety surveillance, rigorous statistical fraud detection, blinded imaging endpoint adjudication, and secure AI-assisted protocol/coding intelligence. Without automated toxicology grading, electronic regulatory transmissions (AS2 EDI), centralized statistical monitoring (CSM), and strict air-gapped AI governance, trials face regulatory non-compliance, delayed expedited reporting, and Protected Health Information (PHI) leakage.

The platform requires:
1. Automated laboratory toxicity grading against NCI-CTCAE v5.0 staging expedited Adverse Event drafts in `apps/safety`.
2. Native ICH E2B(R3) ICSR XML schema compilation and RFC 4130 AS2 S/MIME B2B EDI gateway transmission with synchronous MDN and ACK verification.
3. Centralized Statistical Monitoring (CSM) analyzing Benford's Law digit distributions, terminal digit bias, and Mahalanobis distance multivariate outlier detection, alongside 64-bit DCT perceptual hashing (`pHash`) image duplication sentinels.
4. Blinded dual-reader RECIST 1.1 / Lugano 2014 tumor adjudication with >20% Sum of Diameters (SOD) discordance calculation and Committee Chair electronic sign-off.
5. In-flight PHI de-identification air-gap (`DeidAirGapVault`), 3-tier model routing, and dual-attribution `DRAFT_AI` 21 CFR Part 11 electronic signature ledger.

## 2. Decision Drivers & Constraints

* **FDA 21 CFR 312.32 / ICH E2A / ICH E2B(R3)**: 7-day and 15-day expedited safety reporting mandates native electronic AS2 transmission with cryptographic non-repudiation.
* **ICH E6(R2) §5.18.3 / FDA RBQM Guidance**: Centralized monitoring must statistically identify site data anomalies, fabricated digit patterns, and cross-patient duplicate records.
* **RECIST 1.1 Oncology Standard**: Independent blinded dual-reader tumor evaluations must mathematically detect progression discordance and require independent adjudication.
* **HIPAA Safe Harbor & 21 CFR Part 11 for AI**: Zero raw PHI may reach third-party AI models; AI-generated clinical records must require human review and electronic signature prior to active release.

## 3. Options Considered

1. **Option 1 (Selected): NCI-CTCAE Evaluator + ICH E2B(R3) AS2 Gateway + CSM/pHash Forensics + Blinded Imaging Adjudication + PHI Air-Gap AI Vault**:
   - Automated toxicity evaluator stages Grade 3/4 lab events for Safety Physician causality adjudication.
   - Native AS2 S/MIME gateway transmits E2B(R3) ICSR XML with MDN verification.
   - CSM statistical forensicator runs Benford's Law Chi-Square, digit bias, and 64-bit DCT pHash (Hamming distance $\le 5$) image fraud sentinels.
   - Dual-reader RECIST 1.1 detects >20% SOD discordance triggering Adjudication Chair step-up sign-off.
   - In-flight `DeidAirGapVault` scrubs prompts with surrogate tokens and enforces `DRAFT_AI` Part 11 signature gates.

2. **Option 2: Fragmented Manual Operations**:
   - Manual lab PDF review, manual web portal E2B upload, on-site only monitoring, and unrestricted cloud LLM prompts.

3. **Option 3: Black-Box Autonomous Execution**:
   - Automated AI direct release without human electronic signatures and direct unsanitized data transmission.

## 4. Decision Outcome

**Chosen option:** Option 1 because it guarantees statutory pharmacovigilance compliance, uncompromised patient privacy, robust scientific endpoint validation, and rigorous risk-based quality surveillance.

## 5. Consequences & Trade-offs

* **Positive**:
  - Eliminates delayed safety signal detection via real-time NCI-CTCAE grading.
  - End-to-end electronic regulatory submission audit trail with AS2 MDN receipts.
  - High-sensitivity fraud and duplication detection across sites and patients.
  - Safe, compliant AI enablement with zero PHI exposure.
* **Negative**:
  - Requires maintaining AS2 certificates, S/MIME keypairs, and EDI partner configurations.
  - Image pHash and statistical calculations require bounded background worker queues.

## 6. Implementation & Verification

* **Core Target Modules**:
  - `apps/safety/`: NCI-CTCAE toxicity evaluator, ICH E2B(R3) ICSR compiler, and AS2 EDI gateway.
  - `apps/quality/`: Centralized Statistical Monitoring engine, Benford sentinels, and pHash image duplication sentinels.
  - `apps/imaging/`: Blinded dual-reader RECIST 1.1 adjudication router and DICOM de-identifier.
  - `apps/ai_gateway/` & `packages/deid/`: `DeidAirGapVault`, 3-tier router, and dual-attribution `DRAFT_AI` ledger.
* **Verification Suites**:
  - `apps/safety/tests/`
  - `apps/quality/tests/`
  - `apps/imaging/tests/`
  - `apps/ai_gateway/tests/`
  - `packages/deid/tests/` (384 passed tests)
