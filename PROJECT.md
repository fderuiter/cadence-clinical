# Project: Cadence Clinical Research Software Platform — Phase 1 Deliverables

## Architecture
Cadence synthesizes upstream Clinical Metadata Management (MDR) in Neo4j with downstream Electronic Data Capture (EDC) and Clinical Data Management (CDM) in PostgreSQL into an automated Digital Data Flow (DDF) platform.

```
┌─────────────────────────────────────────────────────────────┐
│                 Frontend: apps/web (Vue 3)                  │
│   - /coding (MedicalCodingView, CodingQueueTable, etc.)     │
│   - /data-lock (DataLockView, Hierarchical Tree, Modals)    │
│   - /exports (ExportWizardView: 5-step regulatory wizard)   │
│   - Navigation: AppShell.vue (Vanilla CSS Tokens)           │
└──────────────────────────────┬──────────────────────────────┘
                               │ REST / OpenAPI + Gateway Auth (X-Sig-Token)
┌──────────────────────────────▼──────────────────────────────┐
│            Backend Execution API: apps/execution            │
│  - /api/v1/execution/coding (Queue, MedDRA/WHODrug, Query)  │
│  - /api/v1/execution/locks (Relational DataLock, Hier. Gating)
│  - /api/v1/execution/labs (CSV / HL7 / FHIR Ingestion)      │
│  - /api/v1/execution/biostat (SAS XPT v5/v8, ODM-XML, JSON) │
├─────────────────────────────────────────────────────────────┤
│  Database Layer: PostgreSQL (SQLModel + Async SQLAlchemy)    │
│  - AuditedModel GxP Hooks, Merkle Root Seals, Lock Check    │
└─────────────────────────────────────────────────────────────┘
```

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | Medical Coding Queue & API | Paginated uncoded/suggested terms with dictionary match score filtering | M1 | ORIGINAL_REQUEST §R1 |
| 2 | MedDRA & WHODrug Hierarchy Traversal | Term search and hierarchy tree lookups (LLT->PT->HLT->HLGT->SOC, ATC) | M1 | ORIGINAL_REQUEST §R1 |
| 3 | Batch Assignment & GxP Audit | Single/batch coding assignment with reason-for-change audit logging | M1 | ORIGINAL_REQUEST §R1 |
| 4 | Dictionary Upversioning Impact | Impact analysis engine (unchanged, deprecated, reclassified terms) | M1 | ORIGINAL_REQUEST §R1 |
| 5 | Automated eCRF Query Escalation | Raising discrepancies and clinical query generation from coding queue | M1 | ORIGINAL_REQUEST §R1 |
| 6 | Medical Coding UI Workbench | MedicalCodingView.vue, CodingQueueTable.vue, Modals, Drawers, Pinia store | M1 | ORIGINAL_REQUEST §R1 |
| 7 | Relational DataLock SQLModel | Persisted DataLock table in PostgreSQL with scope and audit columns | M2 | ORIGINAL_REQUEST §R2 |
| 8 | Hierarchical Lock Interceptor | Database interceptor enforcing Study->Site->Subject->Visit->Form->Field | M2 | ORIGINAL_REQUEST §R2 |
| 9 | Dual-Signature & Step-up Token | Step-up token authorization (X-Sig-Token) on hard-lock actions | M2 | ORIGINAL_REQUEST §R2 |
| 10 | Unlock Justification Enforcement | Strict validation requiring >=50 char justification on unlock | M2 | ORIGINAL_REQUEST §R2 |
| 11 | Data Lock Console UI | DataLockView.vue with hierarchical tree navigation and status badges | M2 | ORIGINAL_REQUEST §R2 |
| 12 | Multi-format Lab Ingestion | lab_ingestion_service.py parsing CSV, HL7 v2.x (ORU^R01), and FHIR | M3 | ORIGINAL_REQUEST §R3 |
| 13 | UCUM Normalization & Range Eval | Unit conversion & age/sex stratified reference range evaluation | M3 | ORIGINAL_REQUEST §R3 |
| 14 | Lab Discrepancy & SAE Auto-Queries | Out-of-range query triggers & critical threshold investigator alerts | M3 | ORIGINAL_REQUEST §R3 |
| 15 | SAS Transport (XPT v5/v8) Binary Export | Binary serializers generating valid SAS XPT v5 and XPT v8 files | M4 | ORIGINAL_REQUEST §R4 |
| 16 | CDISC ODM-XML v1.3.2 Serializer | Regulatory XML generator with embedded <AuditRecord> trails | M4 | ORIGINAL_REQUEST §R4 |
| 17 | CDISC Dataset-JSON v1.0.0 Export | SDTM and ADaM Dataset-JSON 1.0.0 serialization | M4 | ORIGINAL_REQUEST §R4 |
| 18 | HIPAA/GDPR De-identified CSV Export | Deterministic pseudonymization, date shifting, and CSV serialization | M4 | ORIGINAL_REQUEST §R4 |
| 19 | Clinical Data Export Wizard UI | Multi-step ExportWizardView.vue managing selection, filters, download | M4 | ORIGINAL_REQUEST §R4 |
| 20 | E2E Testing Suite (Tiers 1-4) | Comprehensive requirement-driven opaque-box E2E test suite | M5 | ORIGINAL_REQUEST §R5 |
| 21 | GxP Sync & RTM Traceability | scripts/sync_gxp.py execution and @req: docstring synchronization | M5 | ORIGINAL_REQUEST §R5 |
| 22 | Code Quality & UI Standards Pass | Ruff I001/E712, >=85% test coverage, pnpm run build verification | M5 | ORIGINAL_REQUEST §R5 |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M1 | Medical Coding Workbench | Backend batch assign & query escalation endpoints; Frontend MedicalCodingView, CodingQueueTable, DictionaryBrowserModal, UpversioningImpactDrawer, coding.js store; test_medical_coding_workbench.py | None | IN_PROGRESS |
| M2 | Persistent Data Lock & Freeze System | Relational DataLock SQLModel, hierarchical database interceptor, step-up token auth, unlock justification (>=50 chars); Frontend DataLockView; test_data_locks_persistence.py | None | IN_PROGRESS |
| M3 | Lab Batch Ingestion Pipeline | lab_ingestion_service.py (CSV, HL7 v2, FHIR), UCUM conversion, reference range evaluation, out-of-range and SAE auto-query generation; test_lab_batch_ingestion.py | None | IN_PROGRESS |
| M4 | Regulatory Biostatistical Export Wizard | SAS Transport (XPT v5/v8) serializer, CDISC ODM-XML v1.3.2 serializer with audit trails, de-identified CSV serializer, backend export routes; Frontend ExportWizardView; test_biostat_exports.py | None | IN_PROGRESS |
| M5 | E2E Testing, GxP Sync & Adversarial Hardening | E2E Test Suite (Tiers 1-4), 100% pass across all execution suites with >=85% coverage, sync_gxp.py RTM generation, pnpm build, and forensic integrity audit | M1, M2, M3, M4 | PLANNED |

## Interface Contracts

### Medical Coding Workbench
- `POST /api/v1/execution/coding/assignments/batch-assign`:
  - Request: `{ assignment_ids: list[str], code: str, dictionary_type: str, dictionary_version: str, reason: str }`
  - Response: `{ success_count: int, failed_count: int, results: list[dict] }`
- `POST /api/v1/execution/coding/assignments/{id}/raise-query`:
  - Request: `{ query_text: str, reason: str }`
  - Response: `{ query_id: str, status: str, assignment_id: str }`

### Data Lock & Freeze System
- `POST /api/v1/execution/locks/lock`:
  - Request: `{ scope_type: str, scope_id: str, lock_type: str, reason: str }`
  - Headers: `X-Sig-Token` (required for HARD_LOCK)
  - Response: `{ lock_id: str, status: str, locked_at: str }`
- `POST /api/v1/execution/locks/unlock`:
  - Request: `{ lock_id: str, justification: str (min 50 chars), reason: str }`
  - Response: `{ lock_id: str, status: "UNLOCKED", unlocked_at: str }`

### Lab Batch Ingestion
- `POST /api/v1/execution/labs/ingest`:
  - Request: multipart/form-data or JSON `{ format: "csv"|"hl7"|"fhir", payload: str, study_id: str, site_id: str }`
  - Response: `{ total_processed: int, ingested_count: int, out_of_range_count: int, critical_alerts: int, errors: list }`

### Biostatistical Exports
- `POST /api/v1/execution/biostat/export`:
  - Request: `{ format: "xpt_v5"|"xpt_v8"|"odm_xml"|"dataset_json"|"csv", domain: str, study_id: str, privacy_profile: dict }`
  - Response: StreamingResponse or file download URL with Content-Type header.

## Code Layout
- Backend:
  - Models: `apps/execution/database/models/` (dynamic registration via `__init__.py`)
  - Coding logic: `apps/execution/coding/`
  - Lock interceptor: `apps/execution/database/audit.py`, `apps/execution/domain/lock_models.py`, `apps/execution/database/models/lock.py`
  - Lab ingestion: `apps/execution/services/lab_ingestion_service.py` (and `apps/execution/lab_ranges.py`, `ucum.py`)
  - Biostat exports: `apps/execution/biostat/`
  - API Routers: `apps/execution/presentation/routers/`
  - Test suites: `apps/execution/tests/`
- Frontend:
  - Views: `apps/web/src/views/` (`MedicalCodingView.vue`, `DataLockView.vue`, `ExportWizardView.vue`)
  - Components: `apps/web/src/components/` and `apps/web/src/features/`
  - Stores: `apps/web/src/stores/` (`coding.js`, etc.)
  - Routing: `apps/web/src/router/index.js`
  - Navigation: `apps/web/src/components/AppShell.vue`
  - Styling: `apps/web/src/style.css` (Vanilla CSS semantic tokens only)
