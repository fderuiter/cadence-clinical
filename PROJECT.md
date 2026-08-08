# Project: Eradicate `packages/core-models` & Implement Anti-Corruption Layers (ACLs)

## Architecture
- **Decoupling Rules**: Microservices strictly own their domain models under `apps/<service>/src/domain/`. No service may import database models or domain schemas from another service.
- **Foundational Shared Utilities**: GxP audit mixins (`audit.py`), timezone helpers (`datetime_helpers.py`), signature models (`signature.py`), and object storage DTOs (`storage/`) are housed in core foundational packages (`packages/database`, `packages/security`, `packages/storage`).
- **Anti-Corruption Layers (ACLs)**: Cross-service communication occurs via authenticated REST HTTP endpoints using HMAC-SHA256 V2 signatures (`generate_gateway_signature`). Incoming payloads are deserialized directly into local consumer-owned Pydantic DTOs under `apps/<service>/src/domain/acl/`.

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | Infrastructure Utilities Migration | Move `audit.py`, `datetime_helpers.py`, `signature.py`, `storage/` to `packages/database`, `packages/security`, `packages/storage` | M1 | survey_1 |
| 2 | Designer Domain Models Migration | Move USDM, Protocol Authoring, Protocol Render, Protocol Version Ref, Eligibility, USDM Ingestion, Document Renderer to `apps/designer/domain/` | M2 | survey_1 |
| 3 | Safety Domain Models Migration | Move `sae_icsr` and ICSR models to `apps/safety/domain/` | M2 | survey_1 |
| 4 | CTMS Domain Models Migration | Move `ctms` DOA models to `apps/ctms/domain/` | M2 | survey_1 |
| 5 | eTMF Domain Models Migration | Move TMF reference model & `etmf` models to `apps/etmf/domain/` | M2 | survey_1 |
| 6 | Notifications & Org Models Migration | Move `notifications` and `organization_domain` to `apps/notifications/domain/` & `apps/org/domain/` | M2 | survey_1 |
| 7 | Interop Domain Models Migration | Move `sync_engine` models to `apps/interop/domain/` | M2 | survey_1 |
| 8 | Execution Domain Models Migration | Move `execution/` offline models, ePRO, safety, SDTM, trial lock to `apps/execution/domain/` | M3 | survey_1 |
| 9 | Execution Service ACL Implementation | Add local DTOs (`DesignerEligibilityCriterionDTO`, `ProtocolVersionRefDTO`, `USDMValidationDTO`) in `apps/execution/domain/acl/` and update `designer_client.py`, `eligibility_service.py`, `translator.py` | M4 | survey_2, survey_3 |
| 10 | CTMS Service ACL Implementation | Add local DTOs (`DocumentRendererDTO`, `SyncEngineDTO`) in `apps/ctms/domain/acl/` and update `doa.py`, `main.py` | M4 | survey_2, survey_3 |
| 11 | eTMF Service ACL Implementation | Add local DTO (`ProtocolVersionRefDTO`) in `apps/etmf/domain/acl/` and update `ingestion.py`, `ingestion_service.py` | M4 | survey_2, survey_3 |
| 12 | Interop Service ACL Implementation | Add local DTOs (`EligibilityCriterionDTO`, `EPROTransportDTO`) in `apps/interop/domain/acl/` and update `designer_client.py`, `main.py` | M4 | survey_2, survey_3 |
| 13 | Eradicate `packages/core-models` | Delete `packages/core-models` directory and remove `sys.path.insert` in `packages/__init__.py` | M5 | survey_1, survey_2 |
| 14 | Pipeline & Config Cleanup | Clean `pyproject.toml`, `scripts/validate_schemas.py`, and `scripts/detect_duplication.py` | M5 | survey_3 |
| 15 | Test Suite & GxP Verification | Run full pytest suite, ruff check/format, schema validation, duplication detection, and `scripts/sync_gxp.py` | M_TEST | survey_3 |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M1 | Foundational Utilities Migration | Relocate `audit.py`, `datetime_helpers.py`, `signature.py`, `storage/` to core packages | None | DONE |
| M2 | Primary Services Domain Migration | Relocate domain models for `designer`, `safety`, `ctms`, `etmf`, `notifications`, `org`, `interop` | M1 | DONE |
| M3 | Execution Service Domain Migration | Relocate domain models for `execution` to `apps/execution/domain/` | M1, M2 | DONE |
| M4 | ACL & Cross-Service Refactoring | Create local Pydantic DTOs in `apps/<service>/domain/acl/` and replace direct cross-service imports | M2, M3 | DONE |
| M5 | Eradication & Pipeline Cleanup | Delete `packages/core-models`, update `pyproject.toml`, `scripts/validate_schemas.py`, `scripts/detect_duplication.py` | M4 | DONE |
| M_TEST | E2E & GxP Verification | Verify full test suite, ruff check/format, OpenAPI schema export, and sync GxP docs | M1-M5 | DONE |

## Interface Contracts
### Inter-Service Communication Contracts
- All cross-service communications must pass through Gateway HTTP client calls (`GatewayBaseClient`, `generate_gateway_signature`).
- Consumer services deserialize JSON responses into local Pydantic DTOs defined in `apps/<service>/src/domain/acl/`.
- No direct database or entity imports are permitted across microservice boundaries.

## Code Layout
- Foundational Database Utilities: `packages/database/`
- Foundational Security Utilities: `packages/security/`
- Foundational Storage Utilities: `packages/storage/`
- Service Domain Models: `apps/<service>/domain/`
- Service ACL DTOs: `apps/<service>/domain/acl/`
- Service API Routers & Clients: `apps/<service>/`
