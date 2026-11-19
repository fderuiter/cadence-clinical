# ADR-2210: Automated Golden Path Video Recording and GDrive Sync

> [!NOTE]
> Amended by [ADR-2505](2026-09-30-recording-evidence-goes-to-controlled-object-storage.md). Recording evidence is
> stored through the platform's object storage port, not Google Drive. The recording suites and the Triple Sentinel
> gate stand.

- **Status:** Accepted
- **Date:** 2026-08-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Clinical research software requires rigorous end-to-end verification and auditable visual demonstration of system capabilities across all user personas (`sponsor_designer`, `site_crc`, `cra_monitor`, `data_manager`, `safety_physician`, `auditor`). Manual recording of UI flows is error-prone, labor-intensive, and lacks automated assertions against silent frontend failures or backend audit log omissions. We need an automated, deterministic pipeline that records high-definition (1080p) video artifacts across all 25+ application views, enforces a Triple Sentinel verification gate (functional DOM interactions, zero console/network errors, and verified 21 CFR Part 11 audit trails), and synchronizes the resulting video artifacts, interactive HTML report, and SHA-256 manifest to Google Drive.

Reference Requirement: `PRD-SYS-105`

## 2. Decision Drivers & Constraints

- **Persona-Driven Granularity**: The clinical platform enforces strict role-based access control (RBAC). Recordings must execute within authenticated persona contexts covering complete clinical workflows.
- **Triple Sentinel Quality Gate**: Zero tolerance for silent JavaScript console errors, unexpected 4xx/5xx network errors, or unverified mutating actions lacking backend audit logs.
- **Demonstration-Ready Visual Pacing**: Automated tests must execute with human-perceptible pacing (`--demo`) for visual clarity in stakeholder videos, while supporting `--fast` for rapid CI verification.
- **Headless Cloud Synchronization**: Unattended, secure synchronization of video artifacts and cryptographic manifests to Google Drive using Google Cloud Service Account credentials.
- **21 CFR Part 11 Compliance & Integrity**: Every video file and report must be cryptographically hashed (SHA-256) and cataloged in a machine-readable run manifest envelope.

## 3. Options Considered

### Option 1: **Option A (Selected)**: Modular Playwright Golden Path Suites partitioned by persona with demonstration-speed pacing, Triple Sentinel interceptors, and a Python-driven Google Drive v3 client integrated into `cadence record`.

- **Overview:** **Option A (Selected)**: Modular Playwright Golden Path Suites partitioned by persona with demonstration-speed pacing, Triple Sentinel interceptors, and a Python-driven Google Drive v3 client integrated into `cadence record`.
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option B**: Single monolithic end-to-end video recording script without modular persona partitioning.

- **Overview:** **Option B**: Single monolithic end-to-end video recording script without modular persona partitioning.
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

### Option 3: **Option C**: Client-side manual screen capture script with post-hoc manual Google Drive uploads.

- **Overview:** **Option C**: Client-side manual screen capture script with post-hoc manual Google Drive uploads.
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

## 4. Decision Outcome

- **Chosen Option:** **Option A** because:
- **Justification:** Selected as primary architectural approach.

* Persona-partitioned modular suites allow targeted re-recording, produce streamable file sizes, and isolate test failure domains.
* The Python-based `GoogleDriveClient` in `packages/storage/gdrive.py` provides resilient resumable uploads, automatic folder creation, and SHA-256 integrity verification.
* Integrating the workflow into `cadence record` provides developers and CI/CD with a single unified CLI interface (`cadence record run`, `cadence record setup-gdrive`, `cadence record list`).

## 5. Consequences & Trade-offs

- **Positive**: Full-stack automated verification of all 25+ views; zero silent UI errors; auditable video artifacts stored in timestamped Google Drive folders; Part 11 compliance tracking.
- **Positive**: Dual-speed execution (`--demo` for smooth stakeholder recordings vs `--fast` for sub-minute CI passes).
- **Trade-off**: Requires Google Cloud Service Account credentials (`GOOGLE_APPLICATION_CREDENTIALS` and `GOOGLE_DRIVE_FOLDER_ID`) for cloud sync, with local-only fallback mode when unconfigured.

## 6. Implementation & Verification

- **Storage Integration**: `packages/storage/gdrive.py` (`GoogleDriveClient`) with unit tests in `packages/storage/tests/test_gdrive_client.py`.
- **CLI Command**: `packages/cli/commands/record.py` (`cadence record`) with tests in `packages/cli/tests/test_record_command.py`.
- **Playwright Harness & Suites**:
  - `apps/web/tests/e2e/helpers/recorder.ts` (pacing, console/network error interceptors)
  - `apps/web/tests/e2e/helpers/report_generator.ts` (HTML report + SHA-256 manifest generator)
  - `apps/web/tests/e2e/suites/01_designer_suite.spec.ts`
  - `apps/web/tests/e2e/suites/02_crc_ecrf_suite.spec.ts`
  - `apps/web/tests/e2e/suites/03_cra_monitoring_suite.spec.ts`
  - `apps/web/tests/e2e/suites/04_data_manager_suite.spec.ts`
  - `apps/web/tests/e2e/suites/05_safety_physician_suite.spec.ts`
  - `apps/web/tests/e2e/suites/06_advanced_compliance_suite.spec.ts`
