# Handoff Report: Milestone M1 Foundational Core Utilities Migration

**Author**: Explorer 1 (`teamwork_preview_explorer_m1_r1_1`)  
**Date**: 2026-08-07  
**Working Directory**: `/Users/fred/Code/cadence-clinical/.agents/explorer_m1_r1_1/`  
**Target Handoff File**: `/Users/fred/Code/cadence-clinical/.agents/explorer_m1_r1_1/handoff.md`  

---

## 1. Observation

Direct observations of source files in `packages/core-models/` and target package directories:

### Source Utilities in `packages/core-models/`
1. **`packages/core-models/audit.py`**:
   - Lines 11-44: `Part11AuditMixin(BaseModel)` with fields `created_at: AwareDatetime`, `created_by: str`, `reason_for_change: str`, `version_index: int`, and validator `validate_reason_for_change`.
   - Lines 46-52: `AuditFields(Part11AuditMixin)`.
   - Line 7 import: `from datetime_helpers import AwareDatetime`.
2. **`packages/core-models/datetime_helpers.py`**:
   - Lines 11-24: `validate_timezone_aware_datetime(v: Any, handler) -> datetime`.
   - Lines 27-32: `serialize_utc_z(dt: datetime) -> str`.
   - Lines 36-40: `AwareDatetime = Annotated[datetime, WrapValidator(validate_timezone_aware_datetime), PlainSerializer(serialize_utc_z, return_type=str, when_used="json-unless-none")]`.
   - Dependencies: `datetime`, `typing`, `pydantic` only. No internal project dependencies.
3. **`packages/core-models/signature.py`**:
   - Lines 8-23: `SigningReason(StrEnum)`.
   - Lines 26-31: `ApprovalStatus(StrEnum)`.
   - Lines 34-113: `SignatureManifestation(BaseModel)` with fields `signer_id`, `timestamp: AwareDatetime`, `signing_reason: SigningReason`, `ip_address`, `user_agent`, `sha256_hash`, `signature`, `certificate_pem`, `key_identifier`.
   - Line 4 import: `from datetime_helpers import AwareDatetime`.
   - Lines 95 & 107 lazy imports: `from packages.security.signing import canonical_serialize`, `from packages.security.signing import asymmetric_verify`.
4. **`packages/core-models/storage/`**:
   - Directory containing `__init__.py` and `document_models.py`.
   - `document_models.py` defines `DocumentMetadataResponse`, `DocumentUploadResponse`, `ArchiveJobResponse`.

### Target Package Directories State
- **`packages/database/`**: Directory exists with `__init__.py`, `pyproject.toml`, `tests/`. Relocating `audit.py` -> `packages/database/audit.py` and `datetime_helpers.py` -> `packages/database/datetime_helpers.py`.
- **`packages/security/`**: Directory exists with `__init__.py`, `pyproject.toml`, `tests/`, `signing.py`. Relocating `signature.py` -> `packages/security/signature.py`.
- **`packages/storage/`**: Directory exists with `__init__.py`, `pyproject.toml`, `blob_store.py`, `local_store.py`, `s3_store.py`. Relocating `storage/document_models.py` -> `packages/storage/document_models.py`.

---

## 2. Logic Chain

1. **Observation**: `audit.py` defines GxP 21 CFR Part 11 audit fields (`created_at`, `created_by`, `reason_for_change`, `version_index`) used across transactional entities.  
   **Inference**: Database persistence layers in `packages/database` are the natural domain owner for transaction audit fields.  
   **Step 1 Conclusion**: Move `audit.py` to `packages/database/audit.py`.

2. **Observation**: `datetime_helpers.py` defines `AwareDatetime` and timezone normalization helpers. `audit.py` imports `AwareDatetime` on line 7. `signature.py` imports `AwareDatetime` on line 4.  
   **Inference**: `datetime_helpers.py` has no external dependencies. Moving `datetime_helpers.py` to `packages/database/datetime_helpers.py` ensures `packages/database/audit.py` can import `AwareDatetime` locally within `packages.database` without introducing a cyclic or inter-package dependency back to `packages/security`.  
   **Step 2 Conclusion**: Move `datetime_helpers.py` to `packages/database/datetime_helpers.py`.

3. **Observation**: `signature.py` (`SignatureManifestation`) lazily imports `canonical_serialize` and `asymmetric_verify` from `packages.security.signing`.  
   **Inference**: Cryptographic signature manifestations and signing reasons belong in the security package (`packages/security`).  
   **Step 3 Conclusion**: Move `signature.py` to `packages/security/signature.py` and update internal `datetime_helpers` import to `from packages.database.datetime_helpers import AwareDatetime`.

4. **Observation**: `packages/core-models/storage/document_models.py` defines response DTOs (`DocumentMetadataResponse`, `DocumentUploadResponse`, `ArchiveJobResponse`) for file blob storage operations. `packages/storage/` is already a dedicated workspace package for blob/S3 storage.  
   **Inference**: Relocating `document_models.py` into `packages/storage/document_models.py` consolidates all storage abstractions.  
   **Step 4 Conclusion**: Move `storage/document_models.py` to `packages/storage/document_models.py` and delete `packages/core-models/storage/`.

5. **Observation**: 20 downstream files in `apps/` and `packages/` currently import these utilities via `from audit import ...`, `from datetime_helpers import ...`, `from signature import ...`, or `from storage.document_models import ...`.  
   **Inference**: All 20 import statements must be updated to reference `packages.database`, `packages.security`, or `packages.storage`.  
   **Step 5 Conclusion**: Comprehensive import migration map is fully specified in `analysis.md`.

---

## 3. Caveats

- **Scope Limit**: M1 only relocates `audit.py`, `datetime_helpers.py`, `signature.py`, and `storage/`. Remaining domain models in `packages/core-models/` (`cdisc`, `designer`, `eligibility`, `etmf`, `execution`, `protocol_authoring`, `sdtm`, etc.) remain in `packages/core-models/` for M2/M3.
- **Ruff I001 Re-sorting**: Updating `from audit import ...` to `from packages.database.audit import ...` changes standard library/bare imports to first-party package imports. Implementers must execute `uv run ruff check . --fix` and `uv run ruff format .` to maintain CI import compliance.
- **Sys.path Manipulation**: `packages/__init__.py` currently injects `packages/core-models` into `sys.path`. Removing `sys.path` injection is scheduled for M5 after all domain models are migrated.

---

## 4. Conclusion

Relocation of the four M1 core utilities is fully analyzed, scoped, and ready for implementation. Target packages (`packages/database`, `packages/security`, `packages/storage`) are ready to receive the files, and all import changes are cataloged.

---

## 5. Verification Method

To independently verify M1 relocation after implementation:

1. **File Existence Checks**:
   - `packages/database/audit.py` exists
   - `packages/database/datetime_helpers.py` exists
   - `packages/security/signature.py` exists
   - `packages/storage/document_models.py` exists
   - `packages/core-models/audit.py` NO LONGER exists
   - `packages/core-models/datetime_helpers.py` NO LONGER exists
   - `packages/core-models/signature.py` NO LONGER exists
   - `packages/core-models/storage/` NO LONGER exists

2. **Lint & Formatting Verification**:
   ```bash
   uv run ruff check .
   uv run ruff format --check .
   ```

3. **Test Suite Execution**:
   ```bash
   uv run pytest -n auto
   ```
