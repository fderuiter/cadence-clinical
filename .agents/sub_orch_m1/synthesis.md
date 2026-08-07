# Milestone M1 Synthesis & Implementation Plan

## Subagent Results Summary
- 3 completed (Explorer 1, Explorer 2, Explorer 3)
- 0 failed/timed out

## Aggregated Findings & Migration Mapping

### 1. File Relocations
| Original Path | Target Path | Exported Symbols |
|---------------|-------------|------------------|
| `packages/core-models/audit.py` | `packages/database/audit.py` | `Part11AuditMixin`, `AuditFields` |
| `packages/core-models/datetime_helpers.py` | `packages/database/datetime_helpers.py` | `AwareDatetime`, `validate_timezone_aware_datetime`, `serialize_utc_z` |
| `packages/core-models/signature.py` | `packages/security/signature.py` | `SigningReason`, `ApprovalStatus`, `SignatureManifestation` |
| `packages/core-models/storage/` | `packages/storage/` | `DocumentMetadataResponse`, `DocumentUploadResponse`, `ArchiveJobResponse`, etc. |

### 2. Imports to Update
- **`apps/`**:
  - `apps/econsent/main.py`: update imports of `Part11AuditMixin`, `AuditFields`, `SigningReason`, `SignatureManifestation`
  - `apps/econsent/tests/test_econsent.py`: update imports of `Part11AuditMixin`, `AuditFields`
  - `apps/execution/tests/test_soa_persistence.py`: update imports of `Part11AuditMixin`, `AuditFields`
  - `apps/designer/routers/...`: update imports of signature symbols
  - `apps/etmf/routers/archive.py`: update imports of storage models (`packages.storage.document_models` or `packages.storage`)
  - `apps/execution/routers/documents.py`: update imports of storage models
  - Any other imports identified by Explorers in `apps/`
- **`packages/`**:
  - `packages/core-models/eligibility.py`
  - `packages/core-models/organization_domain.py`
  - `packages/core-models/protocol_authoring.py`
  - `packages/core-models/protocol_render.py`
  - `packages/core-models/sdtm.py`
  - `packages/core-models/storage.py` (if present) or `packages/core-models/__init__.py`
  - Update `packages/core-models/pyproject.toml` wheel packages list (remove `"storage"` if moved to `packages/storage`)
- **`scripts/`**:
  - `scripts/detect_duplication.py`: update duplicate exemption pair from `packages/core-models/audit.py` to `packages/database/audit.py`
- **Tests**:
  - `tests/test_datetime_validation.py`
  - `tests/test_signature_manifestation.py`
  - `tests/test_etmf_signing_lifecycle.py`
  - `tests/test_econsent.py`
  - `tests/test_soa_persistence.py`
  - `tests/test_organization_domain.py`
  - Any other tests referencing the moved modules

### 3. GxP & Ruff Requirements
- Strict import ordering (I001): standard library -> 3rd party -> 1st party (alphabetical within groups).
- `uv run ruff check . --fix`
- `uv run ruff format .`
- `uv run pytest` on affected test suites.
- If test docstrings or requirement IDs change (or tests are moved/added), run `uv run python scripts/sync_gxp.py`.
