# Summary of Changes - Milestone M1 Foundational Core Utilities Migration

## 1. File Relocations & Cleanups
- **`packages/database/audit.py`**: Created containing `Part11AuditMixin` and `AuditFields`. Internal import updated to `from packages.database.datetime_helpers import AwareDatetime`.
- **`packages/database/datetime_helpers.py`**: Created containing `validate_timezone_aware_datetime`, `serialize_utc_z`, and `AwareDatetime`.
- **`packages/security/signature.py`**: Created containing `SigningReason`, `ApprovalStatus`, and `SignatureManifestation`. Internal import updated to `from packages.database.datetime_helpers import AwareDatetime`.
- **`packages/storage/document_models.py`**: Created containing `DocumentMetadataResponse`, `DocumentUploadResponse`, and `ArchiveJobResponse`.
- **`packages/core-models/`**: Removed legacy files `audit.py`, `datetime_helpers.py`, `signature.py`, and directory `storage/`.

## 2. Package Configuration & Re-exports
- **`packages/core-models/pyproject.toml`**: Removed `"storage"` from `tool.hatch.build.targets.wheel.packages`.
- **`packages/database/pyproject.toml`**: Added `"pydantic>=2.6.0"` to dependencies list.
- **`packages/storage/__init__.py`**: Re-exported `ArchiveJobResponse`, `DocumentMetadataResponse`, `DocumentUploadResponse`.

## 3. Import Updates Across Codebase (19 files updated)
- **`apps/designer/main.py`**: Updated `from signature import SigningReason` and inline `from signature import SignatureManifestation` to `from packages.security.signature import ...`.
- **`apps/econsent/main.py`**: Updated `from audit import AuditFields` and inline `from signature import SignatureManifestation, SigningReason` to `from packages.database.audit import AuditFields` and `from packages.security.signature import ...`.
- **`apps/econsent/tests/test_econsent.py`**: Updated `from audit import AuditFields` to `from packages.database.audit import AuditFields`.
- **`apps/etmf/ingestion_service.py`**: Updated `from signature import SignatureManifestation, SigningReason` to `from packages.security.signature import ...`.
- **`apps/etmf/main.py`**: Updated `from signature import SigningReason` and inline `from signature import SignatureManifestation` to `from packages.security.signature import ...`.
- **`apps/etmf/routers/archive.py`**: Updated `from storage.document_models import ArchiveJobResponse` to `from packages.storage.document_models import ArchiveJobResponse`.
- **`apps/etmf/tests/test_etmf_signing_lifecycle.py`**: Updated `from signature import SignatureManifestation` to `from packages.security.signature import ...`.
- **`apps/execution/routers/documents.py`**: Updated `from storage.document_models import ...` to `from packages.storage.document_models import ...`.
- **`apps/execution/tests/test_signature_manifestation.py`**: Updated `from signature import ...` to `from packages.security.signature import ...`.
- **`apps/execution/tests/test_soa_persistence.py`**: Updated inline `from audit import AuditFields, Part11AuditMixin` to `from packages.database.audit import ...`.
- **`packages/core-models/eligibility/models.py`**: Updated `from audit import Part11AuditMixin` to `from packages.database.audit import Part11AuditMixin`.
- **`packages/core-models/organization_domain/__init__.py`**: Updated `from audit import AuditFields` to `from packages.database.audit import AuditFields`.
- **`packages/core-models/organization_domain/models.py`**: Updated `from audit import AuditFields # noqa: F401` to `from packages.database.audit import AuditFields # noqa: F401`.
- **`packages/core-models/protocol_authoring/models.py`**: Updated `from audit import AuditFields` and `from datetime_helpers import AwareDatetime` to `from packages.database.audit import AuditFields` and `from packages.database.datetime_helpers import AwareDatetime`.
- **`packages/core-models/protocol_authoring/soa.py`**: Updated `from audit import AuditFields` to `from packages.database.audit import AuditFields`.
- **`packages/core-models/sdtm/models.py`**: Updated `from datetime_helpers import AwareDatetime` to `from packages.database.datetime_helpers import AwareDatetime`.
- **`scripts/detect_duplication.py`**: Updated code duplication scanner exemption path from `"packages/core-models/audit.py"` to `"packages/database/audit.py"`.

## 4. Verification & Formatting
- **Ruff Check & Format**: Executed `uv run ruff check . --fix` (18 I001 import order issues auto-fixed) and `uv run ruff format .` (681 files formatted/verified).
- **Code Duplication Check**: Executed `python3 scripts/detect_duplication.py` (0 duplicates found, SUCCESS).
- **Test Suite Execution**: Executed `uv run pytest -n auto` (169 tests passed in 23.36s).
- **GxP Sync Execution**: Executed `uv run python scripts/sync_gxp.py` (RTM docs regenerated with 103 items, IQ/OQ/PQ report generated and staged cleanly).
