# ADR-111: SAE Reconciliation Architecture

- **Status:** Accepted
- **Date:** 2026-08-26
- **Authors:** @fderuiter
- **Deciders:** @fderuiter, @architect-lead

---

## 1. Context & Problem Statement

In clinical trials, patient safety data is collected in two parallel, isolated systems: the Electronic Data Capture (EDC) system (which tracks Adverse Events reported at clinical sites) and the Safety/Pharmacovigilance database (which maintains authoritative Serious Adverse Event (SAE) cases for regulatory reporting). To ensure data integrity, GCP compliance, and participant safety, these two sources must be reconciled periodically.

The platform requires a robust, automated, and secure gateway to align clinical EDC observations with authoritative safety cases, identify discrepancies, persist results with full version history, and alert the Sponsor Medical Monitor of any material differences.

This decision implements requirements under Trace-14.

## 2. Decision Drivers & Constraints

- **Driver 1 (GxP Compliance & Traceability):** Reconciliation must run within a GxP-validated, 21 CFR Part 11 compliant framework, maintaining full audit logging, change justification, and logical state sequencing.
- **Driver 2 (Privacy & PII Protection):** The safety subsystem must perform all comparative calculations and persist results without storing or leaking raw clinical PII or PHI.
- **Driver 3 (Fault Tolerance & Fail-Open Behavior):** Network or secondary notification service failures must never crash or block the safety subsystem's core transaction commit or job lifecycle. Medical Monitor alert notifications must be dispatched with fail-open behavior.

## 3. Options Considered

### Option 1: Direct Database-to-Database Sync

Expose database views from the execution/EDC service directly to the safety subsystem and perform SQL-level joins/compares.

- **Pros:** Simpler implementation initially.
- **Cons:** Violates service boundary isolation and introduces database-level cross-dependency.

### Option 2: API-Driven Gateway with Async Worker and Stable Keys (Selected)

Utilize decoupled gateway API queries to fetch Dataset-JSON observations, perform localized stable comparison on PII-free keys, track executions via an asynchronous job state machine, and persist audit-tracked discrepancies.

- **Pros:** Strictly preserves service boundaries, provides secure de-identified comparisons, and handles failures gracefully.
- **Cons:** Requires custom normalization logic and stable key generation algorithm.

---

## 4. Decision Outcome

**Chosen Option:** Option 2

### Sourcing & Normalization

- **Dataset-JSON AE Sourcing:** The safety subsystem queries the clinical trial execution engine's SDTM AE dataset via `ExecutionClient.fetch_ae_data`, which returns a standardized, de-identified representation of the Adverse Events table.
- **MedDRA Resolution:** The client resolves verbatim terms to standard MedDRA codes via `resolve_meddra_code`. To ensure reliability, it utilizes a fail-soft strategy where exceptions or network timeouts in the dictionary service do not fail the run, instead reverting to `meddra_coding=None` and logging a warning.

### Stable Event Comparison

- **generate_stable_event_key:** To compare EDC and Safety records, a stable, deterministic, and PII-free comparison key is generated using `generate_stable_event_key`.
  - When a sequence number (`AESEQ`) is present: `{subject_key}:SEQ-{AESEQ}`.
  - When `AESEQ` is absent: `{subject_key}:TERM-{normalized_term}:{start_date}` (verbatim term stripped, normalized, and uppercase).
- **Pure Comparison Engine:** `compare_sae_records` aligns standard variables (`AESER`, `AESTDTC`, `AEENDTC`, `AESEV`, `AEREL`, `AEOUT`) and MedDRA hierarchy codes, sorting all found discrepancies deterministically by case event key and field name.

### State Machine & Persistence

- **Job Lifecycle:** The asynchronous job orchestrator (`reconciliation_worker`) transitions through a strict state machine: `PENDING -> PROCESSING -> COMPLETED / FAILED`.
- **PII-Sanitized Errors:** In case of failure, error messages are truncated to safe exception names (e.g., `AttributeError`, `HTTPStatusError`) to completely eliminate any leak of PII/PHI or internal URLs in public logs.
- **Transaction Isolation:** The comparative run and discrepancy records are saved inside a single database transaction via SQLAlchemy `session.begin_nested()`, ensuring either full persistence or atomic rollback, while maintaining chronological `version_index` and Part 11 audit fields.
- **Fail-Open Alert Dispatch:** When material discrepancies are identified, a notification targeting the Sponsor Medical Monitor (`recipient_role = "sponsor_mm"`) is enqueued using HMAC-SHA256 Gateway V2 signatures. Any network or transport error is gracefully handled (logging `RECONCILIATION_ALERT_FAILED`), leaving the job status in `COMPLETED` state.

---

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Complete decoupling of safety and execution databases.
  - Bulletproof GxP traceability with version-incrementing discrepancy tables.
  - High resilience through fail-open alerts and sanitized error propagation.
- **Negative Impact:**
  - Extra processing cost of resolving MedDRA codes on the fly.
- **Cross-Reference:**
  - This decision works in conjunction with the outbound ICSR pipeline defined in [ADR-064](2026-08-09-safety-e2b-icsr-xml-export-pipeline.md).

---

## 6. Implementation & Verification

### Affected Files

- `apps/safety/reconciliation.py` (Comparison core, stable key generator, and orchestrator)
- `apps/safety/execution_client.py` (Dataset-JSON and MedDRA dictionary resolver)
- `apps/safety/models.py` (Relational schemas for jobs, runs, discrepancies, and audit logs)
- `apps/safety/main.py` (FastAPI router endpoints, dependency injections, and background tasks)

### Verification & Reproducible Commands

Execute the complete safety reconciliation and background job validation suite to confirm regulatory compliance:

```bash
uv run pytest tests/test_sae_reconciliation.py tests/test_sae_reconciliation_jobs.py --no-cov
uv run python scripts/validate_adrs.py
```
