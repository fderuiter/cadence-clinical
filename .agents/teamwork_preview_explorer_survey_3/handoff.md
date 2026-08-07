# Handoff Report: Anti-Corruption Layer (ACL), Inter-Service Communication, and Pipeline Verification Audit

## 1. Observation
- **Inter-Service Clients & Imports**:
  - `apps/execution/designer_client.py:6`: `from eligibility.models import EligibilityCriterion` — direct import of shared core-model from `packages/core-models/eligibility/models.py`.
  - `apps/econsent/etmf_client.py:7`: `from packages.security.signing import generate_gateway_signature` — uses HTTP client with HMAC-SHA256 V2 gateway signatures for ICF archival POST requests to `http://localhost:8003/api/v1/etmf/ingest`.
  - `apps/etmf/lock_client.py:9`: `from packages.security.signing import generate_gateway_signature` — queries `GET /api/v1/execution/locks` and posts to `POST /api/v1/execution/locks/trial/lock`.
  - `packages/security/gateway_client.py:60`: `class GatewayBaseClient` — central HTTPX async client base with `build_headers` and `request` implementing HMAC-SHA256 V2 authentication.
- **Pipeline Configurations & Scripts**:
  - `pyproject.toml:26`: `packages-core-models = { workspace = true }` under `[tool.uv.sources]`.
  - `pyproject.toml:72`: `"packages/core-models/sdtm/dataset_json_models.py" = ["N815"]` under `per-file-ignores`.
  - `scripts/validate_schemas.py:40`: `for name in ["core-models", "database", "deid", "security", "ui"]:` — loops over `core-models` to append to `sys.path`.
  - `scripts/detect_duplication.py:252-320`: Hardcoded `ignored` pairs contain paths in `packages/core-models/` (e.g. `packages/core-models/audit.py`, `packages/core-models/sdtm/models.py`, `packages/core-models/usdm_ingestion.py`).
  - `scripts/sync_gxp.py:168-243`: Runs 4 pytest suites (`report_main.xml`, `report_notif.xml`, `report_integration.xml`, `report_qualification.xml`), merges into `report.xml`, and regenerates `docs/SDLC/Requirements_Traceability_Matrix.md`.

## 2. Logic Chain
1. **Observation**: Microservices currently rely on direct imports from `packages/core-models` (e.g., `apps/execution/designer_client.py` importing `eligibility.models.EligibilityCriterion`).
2. **Logic**: Direct cross-service/core-models imports violate microservice boundary rules and prevent independent domain evolution. Replacing `packages/core-models` requires every consuming service to own local Pydantic DTOs for incoming API payloads (forming Anti-Corruption Layers).
3. **Observation**: Services authenticate cross-service HTTP requests using `GatewayAuthMiddleware` and `generate_gateway_signature(...)`.
4. **Logic**: Local Pydantic DTOs in consuming services (e.g., `DesignerEligibilityCriterionDTO` in `execution`) can cleanly deserialize incoming HTTP responses from producer services without referencing producer database or entity models.
5. **Observation**: Build and linting scripts (`pyproject.toml`, `scripts/validate_schemas.py`, `scripts/detect_duplication.py`) hardcode path references to `packages/core-models`.
6. **Logic**: Eradicating `packages/core-models` requires updating these configuration files and scripts to prevent import failures, missing module errors, or stale duplication whitelist entries during gate verification.

## 3. Caveats
- Direct schema compilation in `scripts/validate_schemas.py` requires all downstream apps (`apps/*/main.py`) to be importable without runtime errors. When `packages/core-models` is deleted, any lingering import statement pointing to `core-models` will cause `validate_schemas.py` to fail.
- Dynamic test mode in `apps/etmf/lock_client.py` inspects `sys.modules["apps.execution.trial_lock"]`. In production, communication uses HTTP endpoints exclusively.

## 4. Conclusion
Replacing `packages/core-models` and implementing service Anti-Corruption Layers requires:
1. Creating local Pydantic DTOs in each consuming service (`execution`, `designer`, `etmf`, `ctms`, `safety`, `interop`, `notifications`) under `apps/<service>/src/domain/acl/`.
2. Updating HTTP client files (e.g., `apps/execution/designer_client.py`) to deserialize HTTP JSON responses into local ACL DTOs instead of `packages/core-models` classes.
3. Updating pipeline files (`pyproject.toml`, `scripts/validate_schemas.py`, `scripts/detect_duplication.py`) to remove all references to `packages/core-models`.
4. Running the full 6-gate verification suite (`ruff format`, `ruff check`, `detect_duplication.py`, `validate_schemas.py`, `pytest -n auto`, `sync_gxp.py`) to confirm gate approval.

## 5. Verification Method
To verify the audit findings and test pipeline readiness:
1. **Ruff Verification**:
   ```bash
   uv run ruff check .
   uv run ruff format --check .
   ```
2. **Schema & Duplication Verification**:
   ```bash
   uv run python scripts/validate_schemas.py --export-dir docs/openapi
   python3 scripts/detect_duplication.py
   ```
3. **Test Suite Verification**:
   ```bash
   uv run pytest -n auto --cov=apps --cov=packages --cov-fail-under=80
   ```
4. **GxP Compliance Verification**:
   ```bash
   uv run python scripts/sync_gxp.py --dry-run
   ```
5. **Inspect Audit Findings**:
   Review detailed findings and DTO specifications in `/Users/fred/Code/cadence-clinical/.agents/teamwork_preview_explorer_survey_3/analysis.md`.
