# Project: Cadence Clinical — Phase 1 Core Clinical Workbenches, EDC Data Flow & Ingestion

## Architecture
Cadence Clinical Research Software is a unified, standalone eClinical platform synthesizing upstream Clinical Metadata Management (MDR) with downstream Electronic Data Capture (EDC) into an automated Digital Data Flow (DDF).

```
┌────────────────────────────────────────────────────────────────────────────────────────┐
│ apps/web (Vanilla CSS + Semantic Tokens + Vue 3.5 + Pinia)                             │
│  - /coding (MedicalCodingView.vue, CodingQueueTable, DictionaryBrowser, ImpactDrawer)  │
│  - /data-lock (DataLockView.vue, Hierarchical Tree, Dual-Signature Part 11 Modals)     │
│  - /exports (ExportWizardView.vue, 5-Step Biostat Wizard, Domain/Cohort/Privacy Picker)│
└───────────────────────────────────────────┬────────────────────────────────────────────┘
                                            │ Authenticated HTTP / Gateway Auth V2
                                            ▼
┌────────────────────────────────────────────────────────────────────────────────────────┐
│ apps/execution (FastAPI + Async SQLAlchemy + Pydantic v2)                              │
│                                                                                        │
│ 1. Medical Coding Workbench (apps/execution/coding/, routers/coding.py)                │
│    - MedDRA (LLT/PT/HLT/HLGT/SOC) & WHODrug (ATC/Ingredients) relational lookup       │
│    - Levenshtein + Cosine NLP fuzzy matcher (>=0.85 auto-code, 0.60-0.85 suggestions)  │
│    - Batch assignment, upversioning impact analysis, query auto-escalation             │
│                                                                                        │
│ 2. Granular Data Lock & Freeze (models/lock.py, database/audit.py, routers/locks.py)   │
│    - PostgreSQL DataLock SQLModel (STUDY, SITE, SUBJECT, VISIT, FORM, FIELD)           │
│    - Hierarchical inheritance mutation interceptor in receive_before_flush             │
│    - Step-up dual signature (X-Sig-Token) & >= 50 char unlock justification validation │
│                                                                                        │
│ 3. Central & Local Lab Ingestion (services/lab_ingestion_service.py, routers/labs.py) │
│    - Batch ingestion: Delimited CSV, HL7 v2.x (ORU^R01), HL7 FHIR Observation bundles  │
│    - UCUM normalization, age/sex reference range evaluation (apps/execution/lab_ranges)│
│    - Auto-generation of OUT_OF_RANGE_WARNING & POTENTIAL_SAE_CRITICAL discrepancy queries│
│                                                                                        │
│ 4. Clinical Data Export Wizard (apps/execution/biostat/, routers/exports.py)          │
│    - SAS Transport (XPT v5 and XPT v8) binary serializers with 80-byte header cards    │
│    - CDISC ODM-XML v1.3.2 serializer with full 21 CFR Part 11 <AuditRecord> trees     │
│    - CDISC Dataset-JSON v1.0.0 serializer & HIPAA/GDPR de-identified CSV generator     │
│    - Asynchronous export generation & BiostatExport audit trail logging                │
└────────────────────────────────────────────────────────────────────────────────────────┘
```

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| F1 | Medical Coding Queue & Search APIs | Queue listing with pagination/filters, MedDRA 5-level & WHODrug ATC search/traversal in `apps/execution/presentation/routers/coding.py` and `dictionaries.py` | M1 | Survey |
| F2 | Batch Coding & Query Escalation | Batch code assignment under Part 11 audit context and discrepancy query auto-escalation in `apps/execution/coding/service.py` | M1 | Survey |
| F3 | Medical Coding Frontend Workbench | Interactive workbench in `apps/web/src/views/MedicalCodingView.vue` with queue table, dictionary browser modal, up-versioning drawer, and Pinia store | M1 | Survey |
| F4 | Medical Coding Test Suite | Comprehensive tests in `apps/execution/tests/test_medical_coding_workbench.py` covering queue, search, batch-assign, queries, and upversioning | M1 | Survey |
| F5 | Relational DataLock SQLModel | Persistent `DataLock` SQLModel in `apps/execution/database/models/lock.py` registered in `models/__init__.py` | M2 | Survey |
| F6 | Hierarchical Lock Interceptor | 6-tier hierarchical lock inheritance interceptor in `apps/execution/database/audit.py` blocking mutations on locked/frozen entities | M2 | Survey |
| F7 | Step-Up Signatures & Unlock Justification | `X-Sig-Token` JWT step-up authentication and >=50 character unlock justification in `apps/execution/presentation/routers/locks.py` | M2 | Survey |
| F8 | Data Lock Frontend Console | Interactive hierarchical tree explorer, freeze/lock/unlock modals with signature capture in `apps/web/src/views/DataLockView.vue` | M2 | Survey |
| F9 | Data Lock Persistence Test Suite | Comprehensive tests in `apps/execution/tests/test_data_locks_persistence.py` covering persistence, hierarchical inheritance, interceptors, and step-up auth | M2 | Survey |
| F10 | Multi-Format Lab Ingestion Service | Delimited CSV, HL7 v2.x (`ORU^R01`), and HL7 FHIR `Observation` parser in `apps/execution/services/lab_ingestion_service.py` | M3 | Survey |
| F11 | Lab Normalization & Auto-Queries | UCUM unit normalization, reference range evaluation, and automated discrepancy query generation for out-of-range & critical SAE alerts | M3 | Survey |
| F12 | Lab Batch Ingestion Test Suite | Comprehensive tests in `apps/execution/tests/test_lab_batch_ingestion.py` covering CSV/HL7/FHIR parsing, UCUM conversions, range evaluation, and queries | M3 | Survey |
| F13 | SAS Transport (XPT v5/v8) Serializer | Binary XPT v5 and XPT v8 generators with 80-byte header cards and variable labels in `apps/execution/biostat/xpt_serializer.py` | M4 | Survey |
| F14 | CDISC ODM-XML & De-ID Serializers | CDISC ODM-XML v1.3.2 with `<AuditRecord>` transaction trees in `odm_serializer.py` and HIPAA/GDPR de-identified CSV serializer | M4 | Survey |
| F15 | Clinical Export Wizard Frontend | 5-step wizard in `apps/web/src/views/ExportWizardView.vue` for format, domain, cohort, and privacy selection | M4 | Survey |
| F16 | Biostat Exports Test Suite | Comprehensive tests in `apps/execution/tests/test_biostat_exports.py` covering XPT v5/v8, ODM-XML, Dataset-JSON, and de-identified CSV | M4 | Survey |
| F17 | Router, Navigation & Design Tokens | Register `/coding`, `/data-lock`, `/exports` in `router/index.js`, update `AppShell.vue` sidebar navigation, verify strict Vanilla CSS design tokens | M5 | Survey |
| F18 | Full Platform Verification & GxP Sync | Pass `pytest -n auto` (>=85% coverage), `ruff check/format`, `detect_duplication.py`, `pnpm run build`, and `sync_gxp.py` | M5 | Survey |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M1 | Medical Coding Workbench | F1, F2, F3, F4 (`coding.py`, `service.py`, `MedicalCodingView.vue`, `test_medical_coding_workbench.py`) | None | IN_PROGRESS |
| M2 | Relational Data Lock & Freeze System | F5, F6, F7, F8, F9 (`models/lock.py`, `audit.py`, `locks.py`, `DataLockView.vue`, `test_data_locks_persistence.py`) | None | PLANNED |
| M3 | Central & Local Lab Batch Ingestion Pipeline | F10, F11, F12 (`lab_ingestion_service.py`, `labs.py`, `test_lab_batch_ingestion.py`) | None | PLANNED |
| M4 | Clinical Data Export Wizard & Serializers | F13, F14, F15, F16 (`xpt_serializer.py`, `odm_serializer.py`, `csv_serializer.py`, `ExportWizardView.vue`, `test_biostat_exports.py`) | None | PLANNED |
| M5 | Router, Navigation, Design Tokens & Platform Verification | F17, F18 (`router/index.js`, `AppShell.vue`, `style.css`, full pytest, ruff, duplication, build, `sync_gxp.py`) | M1, M2, M3, M4 | PLANNED |

## Interface Contracts

### M1 Medical Coding Workbench
- `POST /api/v1/execution/coding/batch-assign`: Accepts `{ assignment_ids: list[str], code: str, term: str, reason_for_change: str }`.
- `POST /api/v1/execution/coding/escalate-query`: Accepts `{ assignment_id: str, query_text: str }`, generates `ClinicalQuery(origin="SYSTEM_CODING")`.
- `GET /api/v1/execution/coding/queue`: Accepts filters (`status`, `dictionary_type`, `domain`, `search_term`, `page`, `page_size`).

### M2 Relational Data Lock & Freeze System
- `DataLock` SQLModel in `apps/execution/database/models/lock.py` with columns: `id`, `study_id`, `site_id`, `subject_id`, `visit_id`, `form_id`, `field_name`, `scope` (`STUDY`, `SITE`, `SUBJECT`, `VISIT`, `FORM`, `FIELD`), `status` (`LOCKED`, `FROZEN`, `UNLOCKED`), `locked_by`, `reason_for_change`, `created_at`, `created_by`, `version_index`, `is_deleted`.
- `receive_before_flush` in `apps/execution/database/audit.py` checks active locks across all 6 hierarchy tiers and raises `DataLockViolationError` (or `HTTPException(423)`) on blocked mutations.
- `POST /api/v1/execution/locks/lock` & `POST /api/v1/execution/locks/unlock`: Validates `X-Sig-Token` JWT for step-up auth and enforces `len(reason_for_change.strip()) >= 50` on unlocks.

### M3 Lab Batch Ingestion Pipeline
- `LabIngestionService.ingest_batch(content: bytes | str, format: str, study_id: str, lab_source: str, current_user_id: str) -> LabIngestionResult`
- Supports formats: `"CSV"`, `"HL7_V2"`, `"FHIR_JSON"`.
- Range checking creates `ClinicalQuery(query_type="OUT_OF_RANGE_WARNING")` or `ClinicalQuery(query_type="POTENTIAL_SAE_CRITICAL")`.

### M4 Clinical Data Export Wizard
- `XPTSerializer.serialize_dataset(domain: str, records: list[dict], version: str = "v5") -> bytes` (Generates standard 80-byte header cards for XPT v5/v8).
- `ODMSerializer.serialize_study(study_id: str, clinical_data: list[dict], audit_records: list[dict]) -> str` (Generates CDISC ODM-XML v1.3.2 with `<AuditRecord>`).
- `POST /api/v1/execution/exports/generate` & `GET /api/v1/execution/exports/{id}/download`.

## Code Layout
- `apps/execution/coding/`: Medical coding fuzzy matcher, impact analysis, coding service.
- `apps/execution/presentation/routers/coding.py`: Coding REST endpoints.
- `apps/execution/database/models/lock.py`: DataLock SQLModel entity.
- `apps/execution/database/audit.py`: Hierarchical lock interceptor in `receive_before_flush`.
- `apps/execution/presentation/routers/locks.py`: Data lock endpoints with step-up token and unlock validation.
- `apps/execution/services/lab_ingestion_service.py`: Multi-format lab batch ingestion engine.
- `apps/execution/presentation/routers/labs.py`: Lab ingestion endpoints.
- `apps/execution/biostat/xpt_serializer.py`: SAS XPT v5 and XPT v8 binary serializers.
- `apps/execution/biostat/odm_serializer.py`: CDISC ODM-XML v1.3.2 serializer.
- `apps/execution/biostat/csv_serializer.py`: HIPAA/GDPR de-identified CSV serializer.
- `apps/execution/presentation/routers/exports.py`: Biostatistical export endpoints.
- `apps/web/src/views/MedicalCodingView.vue`: Medical Coding Workbench.
- `apps/web/src/views/DataLockView.vue`: Granular Data Lock Console.
- `apps/web/src/views/ExportWizardView.vue`: Clinical Data Export Wizard.
- `apps/web/src/router/index.js` & `apps/web/src/components/AppShell.vue`: Navigation & routes.
- `apps/execution/tests/`: Verification test suites (`test_medical_coding_workbench.py`, `test_data_locks_persistence.py`, `test_lab_batch_ingestion.py`, `test_biostat_exports.py`).
