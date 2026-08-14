# Project: Cadence Clinical Research Software Platform — Phase 1 Core

## Architecture
Cadence is a unified eClinical platform synthesizing upstream Clinical Metadata Management (MDR) with downstream Electronic Data Capture (EDC) into an automated Digital Data Flow (DDF).

Phase 1 Core Clinical Workbenches, EDC Data Flow & Ingestion encompasses:
1. **Medical Coding Workbench (`/coding`)**: MedDRA 5-tier (LLT, PT, HLT, HLGT, SOC) and WHODrug (ATC, active ingredients) fuzzy matching engines, auto-coding thresholds, coder actions, discrepancy queries, and dictionary up-versioning impact analysis.
2. **Granular Data Lock & Freeze System (`/data-lock`)**: 6-tier relational hierarchy (Study -> Site -> Subject -> Visit -> Form -> Field), SQLAlchemy pre-flush mutation blocker, step-up dual signatures (`X-Sig-Token`) with replay cache, and GxP unlock override governance requiring >= 50 chars justification.
3. **Central & Local Lab Batch Ingestion Pipeline**: Delimited CSV/TSV, HL7 v2.x (ORU^R01), and FHIR Observation batch parsers with UCUM unit conversion, demographic-stratified normal/critical reference range matching, automated query escalation, and critical SAE alerts.
4. **Clinical Data Export Wizard (`/exports`)**: Pure-Python SAS Transport (XPT v5/v8) serializer, CDISC ODM-XML v1.3.2 serializer with 21 CFR Part 11 `<AuditRecord>` elements, CDISC Dataset-JSON v1.0.0 serializer & validator, and deterministic HIPAA Safe Harbor / GDPR de-identification (HMAC date shifting and pseudonymization).
5. **Frontend Workbenches & Design Standards**: High-density Vanilla CSS clinical workspaces with design tokens in `apps/web/src/style.css` (0 Tailwind CSS utility classes), 6-persona switcher in `apps/web/src/components/AppShell.vue`, and Vue Router RBAC guards.

---

## Feature Inventory

| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | MedDRA 5-Tier Hierarchy & Data Model | MedDRA terms and hierarchical mappings across LLT, PT, HLT, HLGT, SOC | M1 | Survey Agent 2 |
| 2 | WHODrug Hierarchy & Data Model | WHODrug drug records, active ingredients, and ATC levels 1-5 | M1 | Survey Agent 2 |
| 3 | Streaming ASCII Dictionary Parsers | Batch stream parsers for MedDRA `.asc` and WHODrug fixed-width files | M1 | Survey Agent 1 |
| 4 | Clinical Fuzzy Matcher & Normalization | RapidFuzz similarity scoring ($0.4 \cdot S_{Lev} + 0.6 \cdot S_{Cos}$) with stop phrase stripping and stemming | M1 | Survey Agent 2 |
| 5 | Terminology Lookup TTL Cache | In-memory thread-safe TTL cache for dictionary search and auto-complete | M1 | Survey Agent 1 |
| 6 | Coder Action Processor | Handles `ACCEPT`, `OVERRIDE`, and `QUERY` actions with immutable `ClinicalCodingLedger` audit records | M1 | Survey Agent 1 |
| 7 | Batch Coding Assignment | Bulk application of codes across multiple assignments with itemized error reporting | M1 | Survey Agent 2 |
| 8 | Discrepancy Query Escalation | Auto-generates `ClinicalQuery` (`SYSTEM_CODING`, `CLARIFY_VERBATIM`, `HIGH`) for uncodable verbatims | M1 | Survey Agent 2 |
| 9 | Dictionary Up-Versioning Impact Analysis | Categorizes existing codes into unchanged, deprecated, and reclassified under new dictionary version | M1 | Survey Agent 1 |
| 10 | Medical Coding Router Endpoints | REST endpoints under `/api/v1/execution/coding/*` and `/api/v1/dictionaries/*` | M1 | Survey Agent 1 |
| 11 | Medical Coding Frontend Workbench | `/coding` view in `apps/web/src/views/MedicalCodingView.vue` with stats, queue, browser modal, and up-versioning drawer | M1 | Survey Agent 3 |
| 12 | 6-Tier Hierarchical DataLock Model | Relational DataLock model supporting STUDY, SITE, SUBJECT, VISIT, FORM, FIELD scopes | M2 | Survey Agent 2 |
| 13 | Pre-Flush Lock Interceptor | SQLAlchemy event listener blocking mutations on locked/frozen entities | M2 | Survey Agent 1 |
| 14 | In-Memory Trial Lock State Tracker | In-memory manager for lock lookup optimization | M2 | Survey Agent 1 |
| 15 | Step-Up Dual Signature Verification | `X-Sig-Token` JWT verification for `HARD_LOCK` operations | M2 | Survey Agent 2 |
| 16 | Single-Use Token Consumption Cache | Anti-replay token cache with TTL | M2 | Survey Agent 1 |
| 17 | GxP Unlock Override Governance | Mandatory $\ge 50$ character scientific justification validation for unlock overrides | M2 | Survey Agent 2 |
| 18 | Data Lock REST Router Endpoints | REST endpoints under `/api/v1/execution/locks/*` for lock, unlock, and status tree | M2 | Survey Agent 1 |
| 19 | Data Lock Frontend Workbench | `/data-lock` view in `apps/web/src/views/DataLockView.vue` with 6-tier explorer tree, lock modal, and unlock override dialog | M2 | Survey Agent 3 |
| 20 | Delimited CSV/TSV Lab Parser | Auto-sniffing delimiter detector with case-insensitive column header mapping | M3 | Survey Agent 2 |
| 21 | HL7 v2.x Lab Message Parser | ORU^R01 message parser extracting MSH, PID, PV1, OBR, and OBX segments | M3 | Survey Agent 1 |
| 22 | FHIR Observation Parser | HL7 FHIR Observation JSON and Bundle parser | M3 | Survey Agent 1 |
| 23 | Demographics-Stratified Range Matching | Multi-dimensional range selection by study, test, unit, source, site, sex, and age | M3 | Survey Agent 2 |
| 24 | Reference Range Evaluation & Indicators | Inclusive normal bounds and exclusive critical bounds evaluation (`NORMAL`, `LOW`, `HIGH`, `CRITICAL_LOW`, `CRITICAL_HIGH`) | M3 | Survey Agent 2 |
| 25 | UCUM & Database Unit Conversion | Multi-step unit conversion via database catalog with fallback to static UCUM table | M3 | Survey Agent 1 |
| 26 | Lab Discrepancy & Critical SAE Auto-Queries | Generates `OUT_OF_RANGE_WARNING` queries and `POTENTIAL_SAE_CRITICAL` alerts | M3 | Survey Agent 2 |
| 27 | Lab Ingestion REST Router Endpoints | REST endpoints under `/api/v1/execution/labs/*` for batch ingestion and status | M3 | Survey Agent 1 |
| 28 | SAS Transport (XPT v5) Serializer | Pure-Python XPT v5 binary generator with 80-byte cards and 140-byte NAMESTR records | M4 | Survey Agent 2 |
| 29 | SAS Transport (XPT v8) Serializer | Extended XPT v8 binary generator with 512-byte NAMESTR records | M4 | Survey Agent 2 |
| 30 | IBM 360 Floating Point Codec | 64-bit hexadecimal floating point encoder/decoder | M4 | Survey Agent 1 |
| 31 | CDISC ODM-XML v1.3.2 Serializer | Pure-Python XML serializer with embedded 21 CFR Part 11 `<AuditRecord>` elements | M4 | Survey Agent 1 |
| 32 | CDISC Dataset-JSON v1.0.0 Serializer | Pydantic v2 domain model serializer for SDTM and ADaM datasets | M4 | Survey Agent 2 |
| 33 | Dataset-JSON Conformance Validator | Strict validation of required variables, sequence uniqueness, null flavors, and cross-domain integrity | M4 | Survey Agent 2 |
| 34 | Deterministic Date Shifter | HMAC-SHA256 per-subject date shifting in $[-365, +365]$ days preserving longitudinal parity | M4 | Survey Agent 2 |
| 35 | Deterministic Pseudonymizer | Replaces direct identifiers with 64-character HMAC-SHA256 hashes | M4 | Survey Agent 2 |
| 36 | Age Capping & PII Redaction | Capping `AGE > 89` to 89 and masking direct identifiers with `[REDACTED]` | M4 | Survey Agent 2 |
| 37 | Biostat Export REST Endpoints | REST endpoints under `/api/v1/execution/biostat/*` for SDTM, ADaM, and bundle downloads | M4 | Survey Agent 1 |
| 38 | Clinical Data Export Wizard Frontend | `/exports` view in `apps/web/src/views/ExportWizardView.vue` with 5-step guided wizard and de-ID controls | M4 | Survey Agent 3 |
| 39 | Vanilla CSS Styling & Design Tokens | Complete absence of Tailwind CSS; semantic design tokens in `apps/web/src/style.css` | M5 | Survey Agent 3 |
| 40 | 6-Persona Top-Bar Switcher | Role switcher in `AppShell.vue` covering super_admin, sponsor_designer, site_crc, cra_monitor, data_manager, auditor | M5 | Survey Agent 3 |
| 41 | REST API Decoupling & Gateway Auth | Decoupled microservice architecture with HMAC gateway token signatures | M5 | Survey Agent 1 |
| 42 | GxP Traceability & Audit Matrix Sync | Synchronization of requirements traceability matrix via `scripts/sync_gxp.py` | M5 | Survey Agent 1 |

---

## Milestones

| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| 1 | M1: Medical Coding Workbench | Backend coding models, parsers, matcher, service, impact analysis, routers, frontend view (`/coding`), and `test_medical_coding_workbench.py` | None | DONE |
| 2 | M2: Data Lock & Freeze System | Backend lock models, pre-flush interceptor, `X-Sig-Token` dual-signatures, unlock override governance, router, frontend view (`/data-lock`), and `test_data_locks_persistence.py` | M1 | DONE |
| 3 | M3: Lab Batch Ingestion Pipeline | Multi-format parsers (CSV, HL7, FHIR), range matching, UCUM normalization, discrepancy/SAE auto-queries, router, and `test_lab_batch_ingestion.py` | M1 | DONE |
| 4 | M4: Clinical Data Export Wizard | SAS XPT v5/v8, CDISC ODM-XML 1.3.2 with audit records, Dataset-JSON 1.0.0, HIPAA/GDPR de-identification, router, frontend view (`/exports`), and `test_biostat_exports.py` | M1 | DONE |
| 5 | M5: Platform Verification & GxP Compliance | Full test suite pass (`uv run pytest -n auto`), ruff formatting/linting, duplication check, web build (`pnpm run build`), and GxP sync (`sync_gxp.py`) | M1, M2, M3, M4 | DONE |

---

## Interface Contracts

### Medical Coding Workbench (`apps/execution/coding/` <-> `apps/execution/presentation/routers/dictionaries.py`)
- `search_dictionary(session, dict_type, query, version, level, limit) -> list[dict]`
- `match_verbatim_term(session, verbatim, dict_type, version) -> dict(status, match, suggestions)`
- `process_coding_action(session, assignment_id, action, code, term, suggestion_index, reason, actor) -> ClinicalCodingAssignment`
- `batch_assign_codes(session, assignment_ids, code, term, action, reason, actor) -> dict(success_count, failed_count, results)`
- `run_impact_analysis(session, dict_type, new_version, actor) -> dict(unchanged, deprecated, reclassified, skipped)`

### Data Lock & Freeze (`apps/execution/database/models/lock.py` <-> `apps/execution/presentation/routers/locks.py`)
- `create_lock(session, study_id, site_id, subject_id, visit_id, form_id, field_name, lock_type, reason_for_change, signature_token, actor) -> DataLock`
- `unlock_scope(session, lock_id, justification, reason_for_change, actor) -> DataLock` (Enforces `len(justification) >= 50`)
- `get_lock_hierarchy(session, study_id) -> dict(study_id, is_locked, sites, total_active_locks)`
- `verify_and_consume_sig_token(token, user_id) -> dict(claims)`

### Lab Batch Ingestion (`apps/execution/services/lab_ingestion_service.py` <-> `apps/execution/presentation/routers/labs.py`)
- `ingest_lab_batch(session, payload, format_type, study_id, site_id, lab_source, actor) -> dict(batch_id, processed_count, error_count, queries_raised, sae_alerts)`
- `select_reference_range(ranges, study_id, test_code, unit, source, sex, age, site_id) -> LabReferenceRange | None`
- `evaluate_lab_value(value, range_obj) -> tuple[indicator, out_of_range, bounds_json]`

### Biostat Exports (`apps/execution/biostat/` <-> `apps/execution/presentation/routers/exports.py`)
- `serialize_xpt(dataset_name, records, variables_metadata, version='v5'|'v8') -> bytes`
- `serialize_odm_xml(study_id, data, metadata_version_oid, file_oid, originator) -> str`
- `serialize_dataset_json(data, study_id, metadata_version_id, file_oid) -> DatasetJSON`
- `apply_deidentification(records, privacy_profile, salt) -> list[dict]`

---

## Code Layout

```
apps/
├── execution/
│   ├── application/
│   │   └── services/
│   │       └── lab_ingestion_service.py
│   ├── biostat/
│   │   ├── csv_export.py
│   │   ├── deid.py
│   │   ├── models.py
│   │   ├── odm_xml.py
│   │   ├── serializer.py
│   │   ├── validator.py
│   │   └── xpt.py
│   ├── coding/
│   │   ├── impact.py
│   │   ├── importer.py
│   │   ├── matcher.py
│   │   ├── parsers.py
│   │   └── service.py
│   ├── database/
│   │   ├── audit.py
│   │   ├── models.py
│   │   └── models/
│   │       ├── audit.py
│   │       ├── biostat.py
│   │       ├── coding.py
│   │       ├── lab.py
│   │       └── lock.py
│   ├── presentation/
│   │   └── routers/
│   │       ├── dictionaries.py (coding)
│   │       ├── exports.py
│   │       ├── labs.py
│   │       ├── locks.py
│   │       └── signatures.py
│   └── tests/
│       ├── test_biostat_exports.py
│       ├── test_data_locks_persistence.py
│       ├── test_lab_batch_ingestion.py
│       └── test_medical_coding_workbench.py
├── web/
│   └── src/
│       ├── components/
│       │   ├── AppShell.vue
│       │   ├── CodingQueueTable.vue
│       │   ├── DictionaryBrowserModal.vue
│       │   └── UpversioningImpactDrawer.vue
│       ├── features/
│       │   └── signatures/
│       │       └── components/
│       │           └── SignatureCaptureModal.vue
│       ├── router/
│       │   └── index.js
│       ├── stores/
│       │   ├── auth.js
│       │   └── coding.js
│       ├── style.css
│       └── views/
│           ├── DataLockView.vue
│           ├── ExportWizardView.vue
│           └── MedicalCodingView.vue
└── ...
```
