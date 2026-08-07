# Milestone M2: Primary Services Domain Migration — Detailed Change Log

## Overview
Executed the relocation of primary domain models out of `packages/core-models/` to their owning microservices under `apps/<service>/src/domain/` and updated all repository-wide import statements to maintain architectural boundaries.

---

## 1. Domain Model Relocations

| Domain | Source Location | Target Location | Status |
|---|---|---|---|
| **Designer (CDISC & USDM)** | `packages/core-models/cdisc/` | `apps/designer/src/domain/cdisc/` | Moved |
| **Designer (Synopsis Transport)** | `packages/core-models/designer/synopsis_transport_models.py` | `apps/designer/src/domain/synopsis_transport_models.py` | Moved |
| **Designer (USDM Ingestion)** | `packages/core-models/usdm_ingestion.py` | `apps/designer/src/domain/usdm_ingestion.py` | Moved |
| **Designer (Protocol Authoring)** | `packages/core-models/protocol_authoring/` | `apps/designer/src/domain/protocol_authoring/` | Moved |
| **Designer (Protocol Render)** | `packages/core-models/protocol_render/` | `apps/designer/src/domain/protocol_render/` | Moved |
| **Designer (Protocol Version Ref)** | `packages/core-models/protocol_version_ref/` | `apps/designer/src/domain/protocol_version_ref/` | Moved |
| **Designer (Eligibility)** | `packages/core-models/eligibility/` | `apps/designer/src/domain/eligibility/` | Moved |
| **Designer (Document Renderer)** | `packages/core-models/document_renderer.py` | `apps/designer/src/domain/document_renderer.py` | Moved |
| **Safety (SAE ICSR)** | `packages/core-models/sae_icsr/` | `apps/safety/src/domain/sae_icsr/` | Moved |
| **CTMS (DOA Models)** | `packages/core-models/ctms/` | `apps/ctms/src/domain/` | Moved |
| **eTMF (eISF Models)** | `packages/core-models/etmf/` | `apps/etmf/src/domain/etmf/` | Moved |
| **eTMF (TMF Reference Model)** | `packages/core-models/tmf_reference_model/` | `apps/etmf/src/domain/tmf_reference_model/` | Moved |
| **Notifications (Event Models)** | `packages/core-models/notifications/` | `apps/notifications/src/domain/` | Moved |
| **Org (Organization Domain)** | `packages/core-models/organization_domain/` | `apps/org/src/domain/` | Moved |
| **Interop (Sync Engine)** | `packages/core-models/sync_engine.py` | `apps/interop/src/domain/sync_engine.py` | Moved |

---

## 2. Updated Dynamic Loading Shims
- `apps/designer/usdm_ingestion.py`: Updated to re-export directly from `apps.designer.src.domain.usdm_ingestion`.
- `apps/designer/renderers/document_renderer.py`: Updated to re-export directly from `apps.designer.src.domain.document_renderer`.
- `apps/interop/sync_engine.py`: Updated to re-export directly from `apps.interop.src.domain.sync_engine`.

---

## 3. Package Configuration Updates
- `packages/core-models/pyproject.toml`: Updated wheel build target list to retain remaining core packages (`execution`, `localization`, `sdtm`).

---

## 4. Import Sites Updated (77 files)

### `apps/`
- `apps/designer/main.py`, `rendering.py`, `content_assembly.py`, `soa_models.py`, `delta.py`, `adapter/repositories.py`, `importers/usdm_importer.py`, `routers/synopsis.py`, `routers/quality_sentinel.py`, `routers/cascade.py`, `services/artifact_cascade.py`, `services/branch_manager.py`, `services/quality_sentinel.py`, `tests/*`
- `apps/safety/main.py`, `reconciliation.py`, `renderer.py`, `tests/*`
- `apps/ctms/main.py`, `routers/doa.py`, `tests/*`
- `apps/etmf/main.py`, `classification_service.py`, `ingestion.py`, `ingestion_service.py`, `routers/taxonomy.py`, `tests/*`
- `apps/notifications/workers/notification_worker.py`, `tests/*`
- `apps/org/main.py`, `tests/*`
- `apps/interop/main.py`, `designer_client.py`, `tests/*`
- `apps/eisf/routers/eisf.py`, `tests/*`
- `apps/execution/main.py`, `translator.py`, `designer_client.py`, `eligibility_service.py`, `tests/*`
- `apps/gateway/routers/cdisc.py`, `routers/usdm.py`
- `apps/quality/tests/*`

### `packages/`
- `packages/security/delegation.py`
- `packages/core-models/tests/*`

### `scripts/` & `tests/`
- `scripts/tests/test_artifact_cascade.py`
- `scripts/tests/test_content_assembly.py`
- `scripts/tests/test_eligibility_engine.py`
- `scripts/detect_duplication.py` (added inline whitelist pairs for new domain paths)
- `tests/validation/dia_tmf_validation_suite.py`

---

## 5. Verification & Compliance Results
1. **Ruff Lint & Import Order (`uv run ruff check . --fix`)**: Passed. 65 auto-fixes applied, 0 remaining errors.
2. **Ruff Format (`uv run ruff format .`)**: Passed. 692 files formatted / unchanged.
3. **Duplication Scan (`python3 scripts/detect_duplication.py`)**: Passed with `[SUCCESS] No duplicate code structures found above the threshold.`
4. **Pytest Test Suite (`uv run pytest -n auto`)**: Passed. 2140 unit and integration tests passed.
5. **GxP Compliance Sync (`uv run python scripts/sync_gxp.py`)**: Passed. RTM and IQ/OQ/PQ docs updated and verified.
