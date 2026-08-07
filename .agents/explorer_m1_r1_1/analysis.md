# Milestone M1 Analysis Report: Foundational Core Utilities Migration

**Author**: Explorer 1 (`teamwork_preview_explorer_m1_r1_1`)  
**Date**: 2026-08-07  
**Working Directory**: `/Users/fred/Code/cadence-clinical/.agents/explorer_m1_r1_1/`  
**Scope**: Relocation of foundational GxP and infrastructure core models out of `packages/core-models/` into dedicated core packages (`packages/database/`, `packages/security/`, `packages/storage/`).

---

## Executive Summary

This investigation analyzed four core utility source items in `packages/core-models/` targeted for relocation under Milestone M1:
1. `audit.py` -> `packages/database/audit.py`
2. `datetime_helpers.py` -> `packages/database/datetime_helpers.py`
3. `signature.py` -> `packages/security/signature.py`
4. `storage/` directory -> `packages/storage/` (specifically `packages/storage/document_models.py`)

All target directories (`packages/database/`, `packages/security/`, `packages/storage/`) already exist as workspace packages with active `pyproject.toml` configurations. Moving these foundational utilities out of `packages/core-models/` eliminates foundational cross-domain coupling and establishes clean technical boundaries.

---

## Detailed Source File Analysis

### 1. `packages/core-models/audit.py`
- **File path**: `/Users/fred/Code/cadence-clinical/packages/core-models/audit.py`
- **Content / Definitions**:
  - `Part11AuditMixin(BaseModel)`: Reusable Pydantic v2 mixin providing 21 CFR Part 11 compliant audit fields:
    - `created_at: AwareDatetime` (default: `datetime.now(UTC)`)
    - `created_by: str` (required)
    - `reason_for_change: str` (required)
    - `version_index: int` (default: 1)
    - `@field_validator("reason_for_change")`: Enforces non-empty, non-blank strings.
  - `AuditFields(Part11AuditMixin)`: Alias subclass inheriting `Part11AuditMixin`.
- **Dependencies**:
  - Standard library: `from datetime import UTC, datetime`
  - Third-party: `from pydantic import BaseModel, Field, field_validator`
  - Internal: `from datetime_helpers import AwareDatetime`
- **Target Location**: `packages/database/audit.py`
- **Destination Rationale**: `Part11AuditMixin` and `AuditFields` represent core transactional database audit trail metadata. Placing them in `packages/database/audit.py` aligns with database audit standards and ORM audit integration.

---

### 2. `packages/core-models/datetime_helpers.py`
- **File path**: `/Users/fred/Code/cadence-clinical/packages/core-models/datetime_helpers.py`
- **Content / Definitions**:
  - `validate_timezone_aware_datetime(v: Any, handler) -> datetime`: Rejects timezone-naive inputs, normalizes timezone-aware datetime inputs to UTC.
  - `serialize_utc_z(dt: datetime) -> str`: Formats datetime into ISO-8601 string enforcing trailing `'Z'`.
  - `AwareDatetime`: Custom Pydantic v2 annotated type:
    ```python
    AwareDatetime = Annotated[
        datetime,
        WrapValidator(validate_timezone_aware_datetime),
        PlainSerializer(serialize_utc_z, return_type=str, when_used="json-unless-none"),
    ]
    ```
- **Dependencies**:
  - Standard library: `from datetime import UTC, datetime`, `from typing import Annotated, Any`
  - Third-party: `from pydantic import PlainSerializer, WrapValidator`
  - Internal: None. Zero internal project dependencies.
- **Target Location**: `packages/database/datetime_helpers.py`
- **Destination Rationale & Fit**:
  - Placing `datetime_helpers.py` in `packages/database/datetime_helpers.py` allows `packages/database/audit.py` (`Part11AuditMixin`) to import `AwareDatetime` from within `packages.database` without requiring an inter-package dependency back to `packages/security`.
  - Database timestamp persistence requires strict UTC Z serialization and timezone enforcement.
  - `packages/database/__init__.py` or `packages/security/__init__.py` can export `AwareDatetime` if needed.

---

### 3. `packages/core-models/signature.py`
- **File path**: `/Users/fred/Code/cadence-clinical/packages/core-models/signature.py`
- **Content / Definitions**:
  - `SigningReason(StrEnum)`: Controlled Part 11 signing reasons (`AUTHOR`, `REVIEW`, `APPROVAL`, `SPONSOR_APPROVAL`, `INVESTIGATOR_SIGNATURE`, `TECHNICAL_QC`, `CLINICAL_QC`, `DATA_LOCK`, `SYSTEM_SEAL`, `PROTOCOL_APPROVAL`, `REGULATORY_FORM_SIGNATURE`, `TRAINING_ACKNOWLEDGEMENT`, `SITE_VISIT_SIGN_OFF`).
  - `ApprovalStatus(StrEnum)`: `PENDING`, `APPROVED`, `REJECTED`.
  - `SignatureManifestation(BaseModel)`: Part 11 electronic signature manifestation payload containing:
    - `signer_id: str`
    - `timestamp: AwareDatetime`
    - `signing_reason: SigningReason`
    - `ip_address: str`
    - `user_agent: str | None = None`
    - `sha256_hash: str`
    - Cryptographic outputs: `signature: str | None = None`, `certificate_pem: str | None = None`, `key_identifier: str | None = None`
    - Methods: `get_canonical_bytes() -> bytes`, `verify() -> bool`
- **Dependencies**:
  - Standard library: `from datetime import UTC`, `from enum import StrEnum`
  - Third-party: `from pydantic import BaseModel, Field`
  - Internal:
    - `from datetime_helpers import AwareDatetime` -> update to `from packages.database.datetime_helpers import AwareDatetime`
    - `from packages.security.signing import canonical_serialize` (lazy import inside `get_canonical_bytes`)
    - `from packages.security.signing import asymmetric_verify` (lazy import inside `verify`)
- **Target Location**: `packages/security/signature.py`
- **Destination Rationale**: `SignatureManifestation` already imports directly from `packages.security.signing`. Placing `signature.py` in `packages/security/signature.py` consolidates cryptographic signature validation into `packages/security`.

---

### 4. `packages/core-models/storage/` Directory
- **Source path**: `/Users/fred/Code/cadence-clinical/packages/core-models/storage/`
- **Files inside**:
  - `__init__.py`: Re-exports `ArchiveJobResponse`, `DocumentMetadataResponse`, `DocumentUploadResponse` from `storage.document_models`.
  - `document_models.py`:
    - `DocumentMetadataResponse(BaseModel)`: `document_id`, `filename`, `version_index`, `sha256_hash`, `dia_tmf_code`, `status`, `created_by`, `created_at: datetime`
    - `DocumentUploadResponse(BaseModel)`: `document_id`, `filename`, `version_index`, `sha256_hash`
    - `ArchiveJobResponse(BaseModel)`: `job_id`, `study_id`, `status: Literal["PENDING", "PROCESSING", "COMPLETED", "FAILED"]`, `download_url: str | None = None`
- **Target Location**: `packages/storage/document_models.py`
- **Destination Rationale**: `packages/storage/` is an existing workspace package containing `blob_store.py`, `local_store.py`, `s3_store.py`. Moving `document_models.py` into `packages/storage/document_models.py` collocates document storage DTOs with blob storage provider abstractions.

---

## Target Package Directories & Export Strategy

| Target Package | Target File Path | Current Status of Package | Recommended `__init__.py` Re-exports |
|---|---|---|---|
| `packages/database/` | `packages/database/audit.py`, `packages/database/datetime_helpers.py` | Exists (`__init__.py`, `pyproject.toml`, `tests/`) | Add `Part11AuditMixin`, `AuditFields` from `packages.database.audit` and `AwareDatetime`, `serialize_utc_z`, `validate_timezone_aware_datetime` from `packages.database.datetime_helpers` to `packages/database/__init__.py` `__all__`. |
| `packages/security/` | `packages/security/signature.py` | Exists (`__init__.py`, `pyproject.toml`, `tests/`) | Add `SigningReason`, `ApprovalStatus`, `SignatureManifestation` from `packages.security.signature` to `packages/security/__init__.py` `__all__`. |
| `packages/storage/` | `packages/storage/document_models.py` | Exists (`__init__.py`, `pyproject.toml`, `tests/`, `blob_store.py`, etc.) | Add `DocumentMetadataResponse`, `DocumentUploadResponse`, `ArchiveJobResponse` from `packages.storage.document_models` to `packages/storage/__init__.py` `__all__`. |

---

## Cross-References & Internal Import Dependencies

1. **`audit.py` internal dependency**:
   - Current: `from datetime_helpers import AwareDatetime`
   - New: `from packages.database.datetime_helpers import AwareDatetime`
2. **`signature.py` internal dependency**:
   - Current: `from datetime_helpers import AwareDatetime`
   - New: `from packages.database.datetime_helpers import AwareDatetime`
   - Lazy imports: `from packages.security.signing import canonical_serialize`, `from packages.security.signing import asymmetric_verify` (remains valid within `packages/security/`)

---

## Comprehensive Audit of Codebase Usages

Below is the complete list of files and import statements across `apps/` and `packages/` that must be updated when M1 is implemented:

### 1. `audit.py` Imports
| File Path | Current Import Line | Proposed New Import Line |
|---|---|---|
| `apps/econsent/main.py` (Line 8) | `from audit import AuditFields` | `from packages.database.audit import AuditFields` |
| `apps/econsent/tests/test_econsent.py` (Line 7) | `from audit import AuditFields` | `from packages.database.audit import AuditFields` |
| `apps/execution/tests/test_soa_persistence.py` (Line 411) | `from audit import AuditFields, Part11AuditMixin` | `from packages.database.audit import AuditFields, Part11AuditMixin` |
| `packages/core-models/eligibility/models.py` (Line 14) | `from audit import Part11AuditMixin` | `from packages.database.audit import Part11AuditMixin` |
| `packages/core-models/organization_domain/__init__.py` (Line 5) | `from audit import AuditFields` | `from packages.database.audit import AuditFields` |
| `packages/core-models/organization_domain/models.py` (Line 12) | `from audit import AuditFields  # noqa: F401` | `from packages.database.audit import AuditFields  # noqa: F401` |
| `packages/core-models/protocol_authoring/models.py` (Line 14) | `from audit import AuditFields` | `from packages.database.audit import AuditFields` |
| `packages/core-models/protocol_authoring/soa.py` (Line 11) | `from audit import AuditFields` | `from packages.database.audit import AuditFields` |
| `packages/core-models/tests/test_datetime_validation.py` (Line 13) | `from audit import AuditFields` | `from packages.database.audit import AuditFields` |

---

### 2. `datetime_helpers.py` Imports
| File Path | Current Import Line | Proposed New Import Line |
|---|---|---|
| `packages/core-models/audit.py` (Line 7) | `from datetime_helpers import AwareDatetime` | `from packages.database.datetime_helpers import AwareDatetime` |
| `packages/core-models/signature.py` (Line 4) | `from datetime_helpers import AwareDatetime` | `from packages.database.datetime_helpers import AwareDatetime` |
| `packages/core-models/protocol_authoring/models.py` (Line 15) | `from datetime_helpers import AwareDatetime` | `from packages.database.datetime_helpers import AwareDatetime` |
| `packages/core-models/protocol_render/models.py` (Line 12) | `from datetime_helpers import AwareDatetime` | `from packages.database.datetime_helpers import AwareDatetime` |
| `packages/core-models/sdtm/models.py` (Line 13) | `from datetime_helpers import AwareDatetime` | `from packages.database.datetime_helpers import AwareDatetime` |

---

### 3. `signature.py` Imports
| File Path | Current Import Line | Proposed New Import Line |
|---|---|---|
| `apps/designer/main.py` (Line 51) | `from signature import SigningReason` | `from packages.security.signature import SigningReason` |
| `apps/designer/main.py` (Line 2467) | `from signature import SignatureManifestation` | `from packages.security.signature import SignatureManifestation` |
| `apps/econsent/main.py` (Line 1278) | `from signature import SignatureManifestation, SigningReason` | `from packages.security.signature import SignatureManifestation, SigningReason` |
| `apps/etmf/ingestion_service.py` (Line 10) | `from signature import SignatureManifestation, SigningReason` | `from packages.security.signature import SignatureManifestation, SigningReason` |
| `apps/etmf/main.py` (Line 19) | `from signature import SigningReason` | `from packages.security.signature import SigningReason` |
| `apps/etmf/main.py` (Line 2631) | `from signature import SignatureManifestation` | `from packages.security.signature import SignatureManifestation` |
| `apps/etmf/tests/test_etmf_signing_lifecycle.py` (Line 7) | `from signature import SignatureManifestation` | `from packages.security.signature import SignatureManifestation` |
| `apps/execution/tests/test_signature_manifestation.py` (Line 8) | `from signature import ApprovalStatus, SignatureManifestation, SigningReason` | `from packages.security.signature import ApprovalStatus, SignatureManifestation, SigningReason` |
| `packages/core-models/tests/test_datetime_validation.py` (Line 17) | `from signature import SignatureManifestation, SigningReason` | `from packages.security.signature import SignatureManifestation, SigningReason` |

---

### 4. `storage/` Imports
| File Path | Current Import Line | Proposed New Import Line |
|---|---|---|
| `apps/etmf/routers/archive.py` (Line 14) | `from storage.document_models import ArchiveJobResponse` | `from packages.storage.document_models import ArchiveJobResponse` |
| `apps/execution/routers/documents.py` (Lines 22-25) | `from storage.document_models import (DocumentMetadataResponse, DocumentUploadResponse)` | `from packages.storage.document_models import (DocumentMetadataResponse, DocumentUploadResponse)` |
| `packages/core-models/storage/__init__.py` (Line 1) | `from storage.document_models import (...)` | Directory `packages/core-models/storage/` to be deleted |

---

## Risk Analysis & Caveats

1. **Ruff Import Order (I001)**:
   - Changing `from audit import ...` to `from packages.database.audit import ...` moves the import from bare module name to `packages` group. Ruff enforces alphabetical sorting within first-party imports.
   - Run `uv run ruff check . --fix` after updating imports.
2. **`packages/core-models/` test suite**:
   - `packages/core-models/tests/test_datetime_validation.py` tests `AuditFields`, `SignatureManifestation`, `AwareDatetime`, etc. When these utilities move, `test_datetime_validation.py` must import them from their new target packages (`packages.database` and `packages.security`).
3. **Scope Limit**:
   - M1 is restricted strictly to relocating `audit.py`, `datetime_helpers.py`, `signature.py`, and `storage/`.
   - Domain models in `packages/core-models/` (`cdisc`, `designer`, `eligibility`, `etmf`, `execution`, `protocol_authoring`, etc.) remain in `packages/core-models/` until M2-M3.

---

## Conclusion

The relocation of `audit.py`, `datetime_helpers.py`, `signature.py`, and `storage/` is clean, low-risk, and well-isolated. Target packages are prepared and equipped to receive these files. Implementation can proceed immediately.
