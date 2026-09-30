# ADR-2392: Automated Aggregate Safety Compiler (DSUR / PSUR / PBRER) and Exposure Calculator

Also known as: ADR-2199 (former number)

- **Status:** Accepted
- **Date:** 2026-08-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Periodic aggregate safety reports—including Development Safety Update Reports (DSUR, ICH E2F), Periodic Safety Update Reports (PSUR, ICH E2C(R2)), and Periodic Benefit-Risk Evaluation Reports (PBRER)—are mandatory statutory submissions required by health authorities (FDA, EMA, PMDA, MHRA) to continuously evaluate the benefit-risk balance of investigational and authorized medicinal products.

Historically, aggregate pharmacovigilance reports required labor-intensive, error-prone manual data aggregation across multiple isolated databases, leading to reconciliation discrepancies, missing exposure patient-years, and delayed regulatory filings.

Under **PRD-PVG-004**, **PRD-AGG-001**, **PRD-SYS-125**, **ICH E2F**, and **21 CFR Part 11**, Cadence requires an automated, end-to-end aggregate safety report compilation and exposure calculation engine in `apps/safety` that guarantees:
1. **Immutable Point-in-Time Regulatory Snapshots:** Deterministic, SHA-256 Merkle root dataset locking pegged to reporting period boundaries (e.g. Development International Birth Date DIBD anniversaries) ensuring 100% bitwise reproducibility and tamper detection.
2. **Subject Exposure Calculator:** Automated computation of cumulative and interval patient-years of exposure (PYE) stratified by treatment arm, dosage cohort, and demographic subgroups (pediatric `< 18`, adult `18-64`, elderly `>= 65`, sex).
3. **Automated Summary Tabulations & Reference Safety Information (RSI) Comparison:** Cross-tabulations of serious adverse events (SAEs), fatal cases, SUSARs, and Adverse Events of Special Interest (AESI), comparing observed event rates against RSI expected baseline thresholds to flag emerging rate breaches.
4. **Executive QPPV Sign-Off Workflow:** 21 CFR Part 11 gated electronic signature approval restricted to the Qualified Person for Pharmacovigilance (QPPV) or authorized Safety Physicians.
5. **Structured Regulatory Submission Package Export:** Structured XML submissions per ICH E2F / E2C(R2) specifications alongside publication-ready print HTML representations.

## 2. Decision Drivers & Constraints

* **Regulatory Compliance:** Strict conformance to ICH E2F (DSUR), ICH E2C(R2) (PBRER), EU GVP Module VII, and 21 CFR Part 11 electronic signature rules.
* **Cryptographic Immutability:** Point-in-time regulatory snapshots must generate canonical SHA-256 digests over sorted, normalized case and exposure datasets.
* **Role-Based Authorization:** Report finalization and approval must strictly require authorized QPPV or Safety Physician roles, rejecting unauthorized personnel with comprehensive audit logging.
* **Audit Trail Integrity:** All snapshot locks, report compilations, and electronic signatures must create immutable `SafetyAuditLog` entries in `apps/safety`.
* **Zero Tailwind Design Standard:** Web components in `apps/web` must strictly use Vanilla CSS design tokens (`0 Tailwind classes`).

## 3. Options Considered

### Option 1: **Option A: Integrated Microservice Architecture in `apps/safety` with Dedicated Compiler Services (Selected)**

- **Overview:** - Implement `SubjectExposureCalculator` for exact interval/cumulative patient-year formulas:
     $$\text{Patient-Years} = \frac{\sum \text{Dosing Duration (Days)}}{365.25}$$
   - Implement `RegulatorySnapshotManager` for SHA-256 Merkle root hashing and verification.
   - Implement `PeriodicSafetyReportCompiler` with automated MedDRA SOC/PT cross-tabulations, RSI threshold breach detection, and ICH XML serialization.
   - Implement `AggregateSafetyConsole.vue` in `apps/web` with 100% Vanilla CSS design tokens.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option B: External Batch Analytics Warehouse Pipeline**

- **Overview:** - Offload aggregate report compilation to an external reporting warehouse (e.g. Snowflake or Databricks).
   - Disadvantages: Breaks real-time operational workflow in Cadence, fragments 21 CFR Part 11 audit trails, complicates QPPV electronic signature re-authentication, and introduces synchronization latency.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead


## 4. Decision Outcome

- **Chosen Option:** **Option A** because it embeds regulatory compliance directly within the operational microservice architecture, maintains an unalterable audit ledger, ensures instantaneous compilation, and enables single-click QPPV electronic signatures and XML export.
- **Justification:** Selected as primary architectural approach.

Key architectural characteristics:
- **Snapshot Sealing:** Canonical JSON serialization sorted by key and hashed with SHA-256:
  $$\text{Snapshot Hash} = \text{SHA256}(\text{CanonicalJSON}(\text{Manifest}))$$
- **Exposure Stratification:** Multidimensional grouping by Treatment Arm, Dosage, Age Bracket, and Sex with mean and median duration calculation.
- **RSI Rate Ratio & Breach Logic:**
  $$\text{Rate Ratio} = \frac{\text{Observed Incidence Rate}}{\text{RSI Expected Frequency}}$$
  Events are flagged as breaches if unlisted and serious/fatal, or if observed incidence significantly exceeds the RSI expected threshold.
- **QPPV Electronic Sign-Off Manifest:** Structured cryptographic certificate embedded in `report_payload["qppv_signoff"]` containing signer ID, timestamp, snapshot hash, report hash, and HMAC-SHA256 token.

## 5. Consequences & Trade-offs

* **Positive:**
  - 100% automated, reproducible generation of ICH E2F DSUR and ICH E2C(R2) PSUR/PBRER reports.
  - Eliminated manual data aggregation errors and reconciliation delays.
  - Full cryptographic proof of dataset state at the DIBD anniversary.
  - Legally compliant 21 CFR Part 11 QPPV electronic signature workflow.
* **Negative / Overhead:**
  - Computation of multidimensional stratifications across large cohorts.
* **Mitigation:**
  - High-performance pure-Python calculation engine in `SubjectExposureCalculator` processes thousands of exposure records in milliseconds.

## 6. Implementation & Verification

* **Backend Services:**
  - `apps/safety/domain/periodic_report_models.py` (Domain models, DTOs, request/response schemas).
  - `apps/safety/services/periodic_report_compiler.py` (`SubjectExposureCalculator`, `RegulatorySnapshotManager`, `PeriodicSafetyReportCompiler`).
  - `apps/safety/services/signal_alert_service.py` (QPPV signing orchestration & audit trail integration).
  - `apps/safety/presentation/routers/safety.py` (REST endpoints for compile, calculate, snapshot lock/verify, qppv-sign, export).
* **Frontend Components:**
  - `apps/web/src/components/clinical/AggregateSafetyConsole.vue` (Interactive console with 100% Vanilla CSS design tokens).
  - `apps/web/src/views/AggregateSafetyView.vue` (Routed view).
* **Verification:**
  - Unit and integration tests in `apps/safety/tests/test_safety_aggregate_report_compiler.py` and `apps/safety/tests/test_safety_signal_detection_disproportionality.py`.
  - GxP compliance synchronization via `scripts/sync_gxp.py`.
