# Project: Cadence Clinical — "Zero-Click" Study Build

## Architecture
Cadence Clinical Research Software is a unified eClinical platform synthesizing upstream Clinical Metadata Management (MDR) with downstream Electronic Data Capture (EDC) into an automated Digital Data Flow (DDF).

### Data Flow & Component Decoupling
```
[User / USDM v4.0 JSON Payload]
             │
             ▼
┌─────────────────────────────────────────────────────────────┐
│ Frontend: MdrView.vue (USDM Ingestion Modal & Metrics)      │
│  - Client-side Zod validation (usdm-schemas)                │
│  - Real-time synthesis dashboard (< 3.0s SLA display)       │
│  - 21 CFR Part 11 ReasonModal promotion to active EDC build │
└──────────────┬──────────────────────────────────────────────┘
               │ HTTP POST /api/v1/designer/studies/{id}/commit-usdm
               ▼
┌─────────────────────────────────────────────────────────────┐
│ apps/designer/ (Metadata Designer & Protocol Authoring)     │
│  1. USDMGraphImporter (apps/designer/domain/cdisc/)         │
│     - Ingests Study, StudyVersion, StudyDesign, Epochs,    │
│       Arms, Encounters, Activities, BiomedicalConcepts,     │
│       EligibilityCriteria.                                  │
│     - Establishes PERFORMS, MEASURES_CONCEPT, HAS_CRITERION │
│     - Neo4j transactional write with atomic rollback.       │
│  2. CRF Synthesizer (apps/designer/domain/synthesis/)       │
│     - CDASH variable & VLM mapping                          │
│     - UI widgets: text, numeric, select, vas_slider,        │
│       body_map (74-zone SNOMED CT)                          │
│     - Responsive 12/8/4-col layouts & declarative checks    │
│  3. SoA Compiler (apps/designer/domain/synthesis/)          │
│     - Compiles SoAMatrixView payload from PERFORMS edges    │
└──────────────┬──────────────────────────────────────────────┘
               │ Internal Gateway HTTP Request (Authenticated)
               ▼
┌─────────────────────────────────────────────────────────────┐
│ apps/etmf/ (Electronic Trial Master File)                   │
│  4. eTMF Ingestion Service (apps/etmf/ingestion_service.py) │
│     - Seeds DIA TMF Reference Model Zones 1–11 EDL records  │
│       (Zones 1, 2, 4, 5 mandatory) for trial milestones:    │
│       STUDY_INITIATION, ETHICS_SUBMISSION,                  │
│       SITE_ACTIVATION, FSI.                                 │
└─────────────────────────────────────────────────────────────┘
```

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| F1 | USDM Data Models | Pydantic v2 data models for BiomedicalConcept, BiomedicalConceptProperty, StudyVersion, Activity, etc. in `apps/designer/domain/cdisc/usdm_models.py` | M1 | Survey |
| F2 | Transactional Neo4j Importer | `USDMGraphImporter` in `apps/designer/domain/cdisc/usdm_importer.py` ingesting Study, StudyVersion, StudyDesign, StudyEpoch, StudyArm, Encounter, Activity, BiomedicalConcept, EligibilityCriterion with atomic rollback | M1 | Survey |
| F3 | Relational Graph Semantics | Graph relationships `PERFORMS`, `MEASURES_CONCEPT`, `HAS_CRITERION`, `HAS_VERSION`, `HAS_DESIGN`, `HAS_EPOCH`, `HAS_ARM`, `CONTAINS_ENCOUNTER`, `HAS_ACTIVITY`, `HAS_CONCEPT` | M1 | Survey |
| F4 | eCRF Layout Synthesis Engine | `synthesize_crf_layout_from_usdm` in `apps/designer/domain/synthesis/crf_synthesizer.py` mapping CDASH domains, VLM data types, UI widgets (`text`, `numeric`, `select`, `vas_slider`, `body_map`), and 12/8/4-col responsive layouts | M2 | Survey |
| F5 | Declarative Validation Rules | Automated edit check rule compilation (`VS_SYSBP > VS_DIABP`, `EG_QTC <= 500`, range checks) in `crf_synthesizer.py` | M2 | Survey |
| F6 | SoA Matrix Compilation | `compile_soa_matrix_payload` in `apps/designer/domain/synthesis/soa_compiler.py` querying Neo4j `PERFORMS` edges into `SoAMatrixView` payload | M3 | Survey |
| F7 | eTMF EDL Seeding | `seed_etmf_expected_documents_for_study` in `apps/etmf/ingestion_service.py` populating DIA TMF Reference Model Zones 1–11 (Zones 1, 2, 4, 5 mandatory) across trial milestones | M4 | Survey |
| F8 | Service REST & Ingestion Endpoints | REST endpoints in `apps/designer/` and `apps/etmf/` coordinating transactional ingestion, synthesis, and EDL seeding | M4 | Survey |
| F9 | Frontend USDM Ingestion Modal | Interactive modal with file dropzone, JSON paste, sample loader, and client-side Zod validation in `apps/web/src/views/MdrView.vue` | M5 | Survey |
| F10 | Synthesis Summary Metrics Dashboard | Real-time summary dashboard card with entity counts, synthesized CDASH forms, validation rules, eTMF EDL count, and < 3.0s SLA indicator | M5 | Survey |
| F11 | One-Click Promotion to EDC | One-click promotion button triggering 21 CFR Part 11 Electronic Signature / ReasonModal and activating study build | M5 | Survey |
| F12 | Automated E2E Test Suite | Comprehensive tests in `apps/designer/tests/test_zero_click_usdm_build.py` covering transactional ingestion, form synthesis, SoA compilation, EDL seeding, and < 5.0s benchmark | M6 | Survey |
| F13 | GxP & Code Quality Gates | Verification via `uv run python scripts/sync_gxp.py`, `uv run ruff check .`, `uv run ruff format --check .`, and `pnpm --filter @cadence/web build` | M6 | Survey |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M1 | USDM Graph Ingestion & Neo4j Model | F1, F2, F3 (`usdm_models.py`, `usdm_importer.py`) | None | DONE |
| M2 | Automated eCRF Layout Synthesis Engine | F4, F5 (`crf_synthesizer.py`) | M1 | PLANNED |
| M3 | Dynamic SoA Matrix Compilation | F6 (`soa_compiler.py`) | M1 | PLANNED |
| M4 | Automated eTMF EDL Seeding & Integration | F7, F8 (`ingestion_service.py`, etmf router/service) | M1 | PLANNED |
| M5 | Frontend MdrView USDM Ingestion Experience | F9, F10, F11 (`MdrView.vue`) | M2, M3, M4 | PLANNED |
| M6 | Test Suite, GxP Sync & Build Quality Gate | F12, F13 (`test_zero_click_usdm_build.py`, `sync_gxp.py`, ruff, pnpm build) | M1, M2, M3, M4, M5 | PLANNED |

## Interface Contracts

### M1 USDMGraphImporter ↔ Downstream Synthesizers (M2, M3)
- `USDMGraphImporter(driver: Any | None = None)`
- `async def import_usdm(self, payload: dict[str, Any] | USDMStudy, user_id: str = "system", change_reason: str = "Zero-Click USDM Study Ingestion") -> USDMImportResult`
- Output `USDMImportResult`:
```python
class USDMImportResult(BaseModel):
    study_id: str
    protocol_title: str
    phase: Optional[str] = None
    therapeutic_area: Optional[str] = None
    nodes_created: int
    relationships_created: int
    entity_counts: dict[str, int]  # arms, epochs, encounters, activities, biomedical_concepts, eligibility_criteria
    validation_warnings: list[str] = []
```

### M2 CRF Synthesizer ↔ Frontend / EDC Engine
- `def synthesize_crf_layout_from_usdm(study: USDMStudy | dict[str, Any] | list[dict]) -> list[SynthesizedECRFForm]`
- Output `SynthesizedECRFForm`:
```python
class SynthesizedECRFForm(BaseModel):
    form_id: str
    form_name: str
    cdash_domain: str
    items: list[dict[str, Any]]  # id, label, cdash_variable, data_type, widget_type, grid_span, mandatory, constraints, options
    rules: list[dict[str, Any]]  # rule_id, rule_name, target_variable, condition_expression, action, error_message
```

### M3 SoA Compiler ↔ ClinicalSoAMatrix.vue
- `def compile_soa_matrix_payload(driver: Any | None, study_id_or_data: str | USDMStudy | dict) -> dict[str, Any]`
- Output matches `SoAMatrixView`:
```json
{
  "id": "study-soa-001",
  "study_id": "study-soa-001",
  "name": "CADENCE-001",
  "protocolTitle": "Phase II Study",
  "epochs": [{"epoch_id": "EP-01", "epoch_name": "Screening", "sequence": 1, "arm_id": null}],
  "encounters": [{"encounter_id": "V-01", "encounter_name": "Visit 1", "epoch_id": "EP-01", "sequence": 1, "arm_id": null, "target_day": 1}],
  "arms": [{"arm_id": "ARM-01", "arm_name": "Arm 1", "sequence": 1}],
  "rows": [
    {
      "activity_id": "ACT-01",
      "activity_name": "Vital Signs",
      "cells": [
        {
          "activity_id": "ACT-01",
          "encounter_id": "V-01",
          "epoch_id": "EP-01",
          "is_applicable": true,
          "details": "Day 1"
        }
      ]
    }
  ]
}
```
            "is_applicable": true,
            "details": null,
            "arm_id": null,
            "derived_from_soa": false
          }
        ]
      }
    ]
  }
  ```

### M4 eTMF EDL Seeder ↔ Designer / Ingestion Workflow
- `def seed_etmf_expected_documents_for_study(study_id: str, db_session: Any = None, created_by: str = "system", reason_for_change: str = "Zero-Click USDM Study Ingestion") -> list[dict[str, Any]]`
- Populates DIA TMF Reference Model Zones (1, 2, 4, 5, 6, 7, 8, 10, 11) for milestones: `STUDY_INITIATION`, `ETHICS_SUBMISSION`, `SITE_ACTIVATION`, `FSI`.

## Code Layout
- `apps/designer/domain/cdisc/usdm_models.py`: Pydantic USDM models and BiomedicalConcept definitions.
- `apps/designer/domain/cdisc/usdm_importer.py`: USDMGraphImporter with transactional Cypher and rollback.
- `apps/designer/domain/synthesis/crf_synthesizer.py`: CDASH form synthesis, widget mapping, and edit check rules.
- `apps/designer/domain/synthesis/soa_compiler.py`: Dynamic SoA matrix compiler from graph PERFORMS edges.
- `apps/etmf/ingestion_service.py`: Automated DIA TMF EDL seeding.
- `apps/web/src/views/MdrView.vue`: Frontend USDM Ingestion Modal, Metrics Card, and Promotion to EDC.
- `apps/designer/tests/test_zero_click_usdm_build.py`: Master automated test suite.
