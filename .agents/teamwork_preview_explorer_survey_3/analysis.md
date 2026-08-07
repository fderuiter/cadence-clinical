# Comprehensive Audit of Anti-Corruption Layer (ACL) Requirements, Inter-Service Communication, and Verification Pipelines

## Executive Summary

This report provides a detailed architectural audit of the Cadence Clinical Research Software Platform to support the complete eradication of `packages/core-models` and the implementation of Anti-Corruption Layers (ACLs) across all consuming services.

1. **Inter-Service Communication & Gateway Auth**: Microservices communicate over HTTP using `httpx.AsyncClient` wrapped with HMAC-SHA256 Gateway Version 2 signatures (`GatewayAuthMiddleware` and `generate_gateway_signature(...)`).
2. **Anti-Corruption Layer (ACL) Architecture**: Rather than importing database models or entity definitions from producer services or shared core-models packages, consuming services must define local Pydantic DTOs to deserialize HTTP payloads, maintaining strict domain isolation.
3. **Build & Quality Pipeline**: Comprehensive verification requires strict ruff linting (`I001`, `E712`), clean pytest suite execution (`uv run pytest -n auto`), schema validation (`scripts/validate_schemas.py`), code duplication detection (`scripts/detect_duplication.py`), and GxP compliance synchronization (`scripts/sync_gxp.py`).

---

## 1. Inter-Service Communication & Security Architecture

### 1.1 Gateway Authentication & HMAC Signing Mechanism
- **Security Middleware**: Mounted across microservice FastAPI applications via `app.add_middleware(GatewayAuthMiddleware)` from `packages/security/middleware.py`.
- **Signature Verification**: Validates incoming HTTP request headers using HMAC-SHA256 V2 signatures (`generate_gateway_signature(...)` / `verify_gateway_signature(...)` in `packages/security/signing.py`).
- **Required Gateway Headers**:
  - `X-User-Id`: System client or requesting user ID (e.g. `"execution-service"`, `"etmf-service"`).
  - `X-User-Roles`: Role permissions string (e.g. `"system"`, `"admin"`, `"Data Manager"`).
  - `X-Gateway-Timestamp`: UTC epoch timestamp string.
  - `X-Gateway-Signature`: HMAC-SHA256 signature calculated over request credentials and `GATEWAY_SECRET`.
  - `X-Signature-Version`: `"2"`.
  - `X-Change-Reason`: Audit reason for change string (GxP Part 11 requirement).
  - Optional headers: `X-Site-Id`, `X-Sponsor-Id`, `X-Tenant-Id`, `X-Unblinded-Access`.

### 1.2 Shared Gateway Base Client
- `packages/security/gateway_client.py` defines `GatewayBaseClient`:
  - Enforces low-latency asynchronous HTTP connection pooling using `httpx.AsyncClient` adhering to the 100ms internal SLA.
  - Automates header building via `build_headers(...)`.
  - Handles non-blocking async event loop resolution via `run_async(...)`.

### 1.3 Microservice HTTP Client Inventory

| Consuming Service | Client File Path | Target Service | Endpoints & Functionality | Current Dependency |
|---|---|---|---|---|
| `apps/execution` | `apps/execution/designer_client.py` | `designer` | `GET /api/v1/studies/{study_id}/eligibility-criteria` | Imports `EligibilityCriterion` from `packages/core-models/eligibility/models.py` |
| `apps/econsent` | `apps/econsent/etmf_client.py` | `etmf` | `POST /api/v1/etmf/ingest` | Raw dictionary payload for ICF document archival |
| `apps/etmf` | `apps/etmf/lock_client.py` | `execution` | `GET /api/v1/execution/locks`<br>`POST /api/v1/execution/locks/trial/lock` | Queries and triggers trial lock status |
| `apps/etmf` | `apps/etmf/notifications_client.py` | `notifications` | `POST /api/v1/notifications/send` | Sends notification event payloads |
| `apps/execution` | `apps/execution/econsent_client.py` | `econsent` | `GET /api/v1/econsent/signatures/{study_id}/{subject_id}` | Fetches eConsent signature metadata |
| `apps/execution` | `apps/execution/notifications_client.py` | `notifications` | `POST /api/v1/notifications/send` | Sends execution notification payloads |
| `apps/interop` | `apps/interop/designer_client.py` | `designer` | `GET /api/v1/studies/{study_id}/export/odm`<br>`GET /api/v1/studies/{study_id}/export/usdm` | Retrieves ODM/USDM exports |
| `apps/safety` | `apps/safety/execution_client.py` | `execution` | `GET /api/v1/execution/sae/cases`<br>`POST /api/v1/execution/sae/sync` | Fetches and synchronizes SAE cases |
| `apps/tickets` | `apps/tickets/notifications_client.py` | `notifications` | `POST /api/v1/notifications/send` | Sends ticket update notifications |

---

## 2. Anti-Corruption Layer (ACL) Specifications per Consuming Service

To eliminate direct coupling, sibling database imports, and `packages/core-models` imports, each consuming microservice must implement an Anti-Corruption Layer (ACL) consisting of local Pydantic DTOs.

### 2.1 Service: `apps/execution` (Execution Service)
- **Target Path**: `apps/execution/src/domain/acl/` (or `apps/execution/domain/acl/`)
- **Required ACL DTOs**:
  1. `DesignerEligibilityCriterionDTO`: Local Pydantic model for deserializing response data from `GET /api/v1/studies/{study_id}/eligibility-criteria`. Replaces `eligibility.models.EligibilityCriterion`.
  2. `CtmsDoaAssignmentDTO`: Local DTO for receiving delegation of authority assignments from CTMS.
  3. `EConsentSignatureStatusDTO`: Local DTO for receiving subject signature status from eConsent.

### 2.2 Service: `apps/designer` (Designer Service)
- **Target Path**: `apps/designer/src/domain/` & `apps/designer/domain/acl/`
- **Required ACL DTOs**:
  1. `USDMIngestionDTO` / `USDMStudyDTO`: Local Pydantic domain models representing CDISC USDM v3.0/v4.0 study structures owned by Designer.
  2. `CDISCTerminologyDTO`: Local DTO for external NCI/CDISC terminology cache responses.
  3. `ProtocolSynopsisDTO` & `ProtocolSoADTO`: Local DTOs for protocol authoring and document rendering payloads.

### 2.3 Service: `apps/etmf` (eTMF Service)
- **Target Path**: `apps/etmf/src/domain/acl/`
- **Required ACL DTOs**:
  1. `ExecutionTrialLockStatusDTO`: Local Pydantic model matching `GET /api/v1/execution/locks` (`trial_locked: bool`, `reason: str | None`, `locked_at: datetime | None`).
  2. `IngestArtifactDTO`: Local Pydantic model representing incoming document ingestion requests from `econsent` or `eisf`.

### 2.4 Service: `apps/ctms` (CTMS Service)
- **Target Path**: `apps/ctms/src/domain/acl/`
- **Required ACL DTOs**:
  1. `DOASiteMemberDTO` & `DOASignatureDTO`: Local Pydantic models for delegation of authority payloads.

### 2.5 Service: `apps/safety` (Safety Service)
- **Target Path**: `apps/safety/src/domain/acl/`
- **Required ACL DTOs**:
  1. `ExecutionSAECaseDTO`: Local Pydantic model matching `GET /api/v1/execution/sae/cases`.

### 2.6 Service: `apps/interop` (Interop Service)
- **Target Path**: `apps/interop/src/domain/acl/`
- **Required ACL DTOs**:
  1. `DesignerStudyExportDTO`: Local DTO for receiving ODM/USDM study export JSON from Designer.

### 2.7 Service: `apps/notifications` (Notifications Service)
- **Target Path**: `apps/notifications/src/domain/acl/`
- **Required ACL DTOs**:
  1. `NotificationEventPayloadDTO`: Local Pydantic model for incoming notification POST requests.

---

## 3. Build, Linting, and Automated Verification Pipeline Requirements

To achieve gate approval for refactoring PRs, all automated checks and scripts must pass cleanly.

### 3.1 Code Formatting and Linting (`ruff`)
- **Config file**: `pyproject.toml` (`[tool.ruff]`, `[tool.ruff.lint]`).
- **Commands**:
  - Check formatting: `uv run ruff format --check .`
  - Run linter: `uv run ruff check .`
  - Auto-fix lints: `uv run ruff check . --fix`
- **Mandatory Linter Standards**:
  - `I001` (Import Ordering): Strict isort ordering (Standard Library -> Third Party -> First Party, each alphabetically sorted).
  - `E712` (SQLAlchemy Boolean Filter Pattern): Must use `.is_(True)` or `.is_(False)` in `.where()` clauses instead of `== True`/`== False`.
- **Pipeline Update Required**: Remove `"packages/core-models/sdtm/dataset_json_models.py"` from `per-file-ignores` in `pyproject.toml`.

### 3.2 Workspace & Dependency Configuration (`pyproject.toml`)
- **Workspace Sources**: Remove `packages-core-models = { workspace = true }` from `[tool.uv.sources]`.
- **Directory Verification**: Ensure `packages/core-models` folder is deleted and no sys.path entries reference it.

### 3.3 Code Duplication Scanner (`scripts/detect_duplication.py`)
- **Mechanism**: 15-line sliding window scanner on `.py`, `.js`, `.vue`, `.css` files after line normalization (comment stripping, URL placeholders, quote standardization).
- **Execution Command**: `python3 scripts/detect_duplication.py`
- **Pipeline Update Required**: Remove deleted `packages/core-models/...` paths from the hardcoded `ignored` file pair sets in `scripts/detect_duplication.py` (lines 224–346) and add new domain/ACL model pairs if legitimate duplication across microservice boundaries exceeds 15 lines.

### 3.4 Schema Validation & Aggregation (`scripts/validate_schemas.py`)
- **Mechanism**: Dynamically imports microservice entrypoints (`apps/*/main.py`) and compiles OpenAPI schemas to verify non-colliding service prefix namespacing (`Designer_`, `Execution_`, `ETMF_`, etc.).
- **Execution Command**: `uv run python scripts/validate_schemas.py --export-dir docs/openapi`
- **Pipeline Update Required**: Remove `"core-models"` from the `packages_dir` list in line 40 of `scripts/validate_schemas.py`.

### 3.5 Unit & Integration Test Suite (`pytest`)
- **Execution Command**: `uv run pytest -n auto --cov=apps --cov=packages --cov-fail-under=80`
- **Threshold**: Minimum **80%** total statement coverage.
- **Async Mode**: Auto (`asyncio_mode = "auto"`).

### 3.6 GxP Compliance Synchronization (`scripts/sync_gxp.py`)
- **Mechanism**: Executes full 4-phase pytest runs (`report_main.xml`, `report_notif.xml`, `report_integration.xml`, `report_qualification.xml`), merges into `report.xml`, regenerates RTM (`docs/SDLC/Requirements_Traceability_Matrix.md`) and IQ/OQ/PQ report (`docs/SDLC/IQ_OQ_PQ_Execution_Report.md`), and stages files.
- **Execution Command**: `uv run python scripts/sync_gxp.py` (or `pnpm sync-gxp` / `make sync-gxp`).
- **Dry-run check**: `uv run python scripts/sync_gxp.py --dry-run`

---

## 4. Summary Matrix of Required File Modifications for Pipeline Gate Approval

| Target File | Required Modification | Rationale |
|---|---|---|
| `pyproject.toml` | Remove `packages-core-models` from `[tool.uv.sources]`; remove `packages/core-models/...` from `per-file-ignores`. | Clean workspace dependencies after eradicating `packages/core-models`. |
| `scripts/validate_schemas.py` | Remove `"core-models"` from package path loop (line 40). | Prevents import error when `packages/core-models` is deleted. |
| `scripts/detect_duplication.py` | Clean up `packages/core-models/...` paths in `ignored` sets (lines 224-346). | Keeps duplication scanner whitelist clean and accurate. |
| `apps/execution/designer_client.py` | Import local `DesignerEligibilityCriterionDTO` instead of `eligibility.models.EligibilityCriterion`. | Establishes execution service ACL. |
| `docs/SDLC/Requirements_Traceability_Matrix.md` | Regenerate via `uv run python scripts/sync_gxp.py`. | Keeps GxP compliance docs in sync with updated tests. |
