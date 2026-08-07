# Handoff Report — Milestone M2: Primary Services Domain Migration

## 1. Observation

1. **Source Files Relocated**:
   - `packages/core-models/cdisc/` -> `apps/designer/src/domain/cdisc/`
   - `packages/core-models/designer/synopsis_transport_models.py` -> `apps/designer/src/domain/synopsis_transport_models.py`
   - `packages/core-models/usdm_ingestion.py` -> `apps/designer/src/domain/usdm_ingestion.py`
   - `packages/core-models/protocol_authoring/` -> `apps/designer/src/domain/protocol_authoring/`
   - `packages/core-models/protocol_render/` -> `apps/designer/src/domain/protocol_render/`
   - `packages/core-models/protocol_version_ref/` -> `apps/designer/src/domain/protocol_version_ref/`
   - `packages/core-models/eligibility/` -> `apps/designer/src/domain/eligibility/`
   - `packages/core-models/document_renderer.py` -> `apps/designer/src/domain/document_renderer.py`
   - `packages/core-models/sae_icsr/` -> `apps/safety/src/domain/sae_icsr/`
   - `packages/core-models/ctms/` -> `apps/ctms/src/domain/`
   - `packages/core-models/etmf/` -> `apps/etmf/src/domain/etmf/`
   - `packages/core-models/tmf_reference_model/` -> `apps/etmf/src/domain/tmf_reference_model/`
   - `packages/core-models/notifications/` -> `apps/notifications/src/domain/`
   - `packages/core-models/organization_domain/` -> `apps/org/src/domain/`
   - `packages/core-models/sync_engine.py` -> `apps/interop/src/domain/sync_engine.py`

2. **Package Markers Created**:
   - `apps/<service>/src/__init__.py` and `apps/<service>/src/domain/__init__.py` created for all target services (`designer`, `safety`, `ctms`, `etmf`, `notifications`, `org`, `interop`).

3. **Import Statements Updated**:
   - 77 files across `apps/`, `packages/`, `scripts/`, `tests/` updated to import from `apps.<service>.src.domain...`.
   - Internal imports within moved packages updated to relative/absolute target domain paths.

4. **Shims and Wheel Configuration**:
   - Shims in `apps/designer/usdm_ingestion.py`, `apps/designer/renderers/document_renderer.py`, and `apps/interop/sync_engine.py` updated to re-export from new domain locations.
   - `packages/core-models/pyproject.toml` updated to limit wheel targets to remaining core packages (`execution`, `localization`, `sdtm`).

5. **Verification Pipeline Execution**:
   - `uv run ruff check . --fix`: 65 errors auto-fixed, 0 remaining.
   - `uv run ruff format .`: Passed cleanly.
   - `python3 scripts/detect_duplication.py`: Passed with `[SUCCESS] No duplicate code structures found above the threshold.`
   - `uv run pytest -n auto`: 2140 unit and integration tests passed.
   - `uv run python scripts/sync_gxp.py`: `SUCCESS: GxP compliance docs are in sync.`

---

## 2. Logic Chain

1. **Relocation & Boundary Decoupling**: Primary domain models were centralized in `packages/core-models/`, creating tight coupling across microservices. Relocating them to `apps/<service>/src/domain/` enforces clear domain boundaries where each service explicitly owns its domain models.
2. **Import Integrity**: All 77 import sites repository-wide were updated to point to `apps.<service>.src.domain...`, guaranteeing no unresolved imports or circular dependencies.
3. **Dynamic Shims & Wheel Targets**: Existing import shims were adjusted to re-export from the new domain paths to preserve compatibility with dynamic loaders. The wheel configuration in `pyproject.toml` was pruned to ensure build targets match filesystem state.
4. **Compliance & Verification**: Code formatting, linting rules (I001 import ordering), code duplication thresholds, full test suite execution, and GxP documentation sync were verified sequentially to guarantee zero regressions.

---

## 3. Caveats

- **Cross-Service Direct Imports (Milestone M4 Target)**: Services such as `execution`, `etmf`, `interop`, and `gateway` currently import models directly from `apps.designer.src.domain...` or `apps.org.src.domain...`. In Milestone M2, these imports were updated to point to their new `apps.<service>.src.domain...` locations. Complete decoupled boundary isolation (via local ACL DTOs and REST client calls) will be established in Milestone M4.
- **Offline DB Tests**: 8 tests in `packages/database/tests/` and `scripts/tests/test_ast_merge_driver.py` require a live PostgreSQL/Neo4j server or git merge driver execution environment. These failures were present in the baseline before migration and do not impact domain models or microservice runtime logic.

---

## 4. Conclusion

Milestone M2: Primary Services Domain Migration is complete. All primary domain models have been successfully relocated from `packages/core-models/` to their owning service `src/domain/` folders (`apps/designer/`, `apps/safety/`, `apps/ctms/`, `apps/etmf/`, `apps/notifications/`, `apps/org/`, `apps/interop/`). All import sites have been updated, code quality checks passed, 2140 tests passed, and GxP compliance documentation has been synchronized.

---

## 5. Verification Method

To independently verify the implementation:

1. **Verify Relocated Files**:
   ```bash
   ls apps/designer/src/domain/cdisc
   ls apps/safety/src/domain/sae_icsr
   ls apps/ctms/src/domain/doa_models.py
   ls apps/etmf/src/domain/tmf_reference_model
   ls apps/notifications/src/domain/event_models.py
   ls apps/org/src/domain/models.py
   ls apps/interop/src/domain/sync_engine.py
   ```

2. **Run Ruff Linting & Formatting**:
   ```bash
   uv run ruff check .
   uv run ruff format --check .
   ```

3. **Run Code Duplication Scanner**:
   ```bash
   python3 scripts/detect_duplication.py
   ```

4. **Run Test Suite**:
   ```bash
   uv run pytest -n auto
   ```

5. **Verify GxP Compliance Sync**:
   ```bash
   uv run python scripts/sync_gxp.py --dry-run
   ```
