## 2026-08-07T19:50:07Z
<USER_REQUEST>
You are teamwork_preview_worker_m2_1 working in /Users/fred/Code/cadence-clinical/.agents/teamwork_preview_worker_m2_1/.
Your assigned task is to execute Milestone M2: Primary Services Domain Migration.

Context documents:
- /Users/fred/Code/cadence-clinical/.agents/ORIGINAL_REQUEST.md
- /Users/fred/Code/cadence-clinical/PROJECT.md
- /Users/fred/Code/cadence-clinical/.agents/sub_orch_m2/DISPATCH.md
- Explorer 1 handoff report: /Users/fred/Code/cadence-clinical/.agents/teamwork_preview_explorer_m2_1/handoff.md
- Explorer 2 handoff report: /Users/fred/Code/cadence-clinical/.agents/teamwork_preview_explorer_m2_2/handoff.md
- Explorer 3 handoff report: /Users/fred/Code/cadence-clinical/.agents/teamwork_preview_explorer_m2_3/handoff.md

Detailed Instructions:
1. Relocate primary services domain models from `packages/core-models/` to their owning microservice `src/domain/` folders:
   a. **Designer** (`apps/designer/src/domain/`):
      - Move `packages/core-models/cdisc/` -> `apps/designer/src/domain/cdisc/`
      - Move `packages/core-models/designer/synopsis_transport_models.py` -> `apps/designer/src/domain/synopsis_transport_models.py`
      - Move `packages/core-models/usdm_ingestion.py` -> `apps/designer/src/domain/usdm_ingestion.py`
      - Move `packages/core-models/protocol_authoring/` -> `apps/designer/src/domain/protocol_authoring/`
      - Move `packages/core-models/protocol_render/` -> `apps/designer/src/domain/protocol_render/`
      - Move `packages/core-models/protocol_version_ref/` -> `apps/designer/src/domain/protocol_version_ref/`
      - Move `packages/core-models/eligibility/` -> `apps/designer/src/domain/eligibility/`
      - Move `packages/core-models/document_renderer.py` -> `apps/designer/src/domain/document_renderer.py`
   b. **Safety** (`apps/safety/src/domain/`):
      - Move `packages/core-models/sae_icsr/` -> `apps/safety/src/domain/sae_icsr/`
   c. **CTMS** (`apps/ctms/src/domain/`):
      - Move `packages/core-models/ctms/` -> `apps/ctms/src/domain/` (e.g. `doa_models.py`, `doa_transport_models.py`)
   d. **eTMF** (`apps/etmf/src/domain/`):
      - Move `packages/core-models/etmf/` -> `apps/etmf/src/domain/etmf/`
      - Move `packages/core-models/tmf_reference_model/` -> `apps/etmf/src/domain/tmf_reference_model/`
   e. **Notifications & Org**:
      - Move `packages/core-models/notifications/` -> `apps/notifications/src/domain/`
      - Move `packages/core-models/organization_domain/` -> `apps/org/src/domain/`
   f. **Interop**:
      - Move `packages/core-models/sync_engine.py` -> `apps/interop/src/domain/sync_engine.py`

2. Update all import sites across `apps/`, `packages/`, `scripts/`, `tests/` that reference the moved models to point to their new import paths under `apps.<service>.src.domain...`.
   - Take extra care with internal relative/absolute imports within relocated packages (e.g. `from cdisc.usdm_models import ...` -> `from apps.designer.src.domain.cdisc.usdm_models import ...` or relative imports `from .usdm_models import ...`).
   - Remember AGENTS.md import ordering (I001) guidelines: alphabetical order within groups!

3. Run verification and auto-fixes:
   - `uv run ruff check . --fix`
   - `uv run ruff format .`
   - `python3 scripts/detect_duplication.py`
   - `uv run pytest -n auto`
   - `uv run python scripts/sync_gxp.py`

4. DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_preview_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

5. Write a comprehensive report of all changes, build/test execution results to `/Users/fred/Code/cadence-clinical/.agents/teamwork_preview_worker_m2_1/changes.md` and `/Users/fred/Code/cadence-clinical/.agents/teamwork_preview_worker_m2_1/handoff.md`.
6. Send a message to parent (`34f7436c-be3f-4037-9a01-5d758d8a7573`) when completed.
</USER_REQUEST>
