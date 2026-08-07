# Progress Log

Last visited: 2026-08-07T19:52:40Z

- Initialized DISPATCH.md, BRIEFING.md, progress.md.
- Verified path to `uv` (/Users/fred/.local/bin/uv).
- Baseline test run completed: 2140 passed.
- Relocated domain models from `packages/core-models/` to `apps/<service>/src/domain/`:
  - Designer: `cdisc/`, `synopsis_transport_models.py`, `usdm_ingestion.py`, `protocol_authoring/`, `protocol_render/`, `protocol_version_ref/`, `eligibility/`, `document_renderer.py`
  - Safety: `sae_icsr/`
  - CTMS: `doa_models.py`, `doa_transport_models.py`, `__init__.py`
  - eTMF: `etmf/`, `tmf_reference_model/`
  - Notifications: `event_models.py`, `__init__.py`
  - Org: `models.py`, `__init__.py`
  - Interop: `sync_engine.py`
- Updated 77 import sites across `apps/`, `packages/`, `scripts/`, `tests/` to target `apps.<service>.src.domain...`.
- Updated shims in `apps/designer/usdm_ingestion.py`, `apps/designer/renderers/document_renderer.py`, `apps/interop/sync_engine.py`.
- Updated `packages/core-models/pyproject.toml` wheel targets list.
- Ran `uv run ruff check . --fix` (fixed 65 errors).
- Ran `uv run ruff format .` (clean).
- Updated `scripts/detect_duplication.py` inline whitelist for new domain path pairs and ran scanner (passed).
- Post-migration pytest suite launched.
