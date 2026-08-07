# Milestone M1 Analysis Report: Application Import Audit (`apps/`)

**Author**: Explorer 2 (`teamwork_preview_explorer`)  
**Date**: 2026-08-07  
**Scope**: All Python files across `apps/` referencing relocated core utilities (`audit.py`, `datetime_helpers.py`, `signature.py`, `storage/`).

---

## Executive Summary

As part of Milestone M1 (Foundational Core Utilities Migration), four foundational infrastructure and GxP utility modules are being relocated from `packages/core-models/` to dedicated package domains:
1. `audit.py` (`Part11AuditMixin`, `AuditFields`) → `packages/database/audit.py`
2. `datetime_helpers.py` (`validate_timezone_aware_datetime`, `serialize_utc_z`, `AwareDatetime`) → `packages/database/datetime_helpers.py` (or `packages/security/datetime_helpers.py`)
3. `signature.py` (`SigningReason`, `ApprovalStatus`, `SignatureManifestation`) → `packages/security/signature.py`
4. `storage/` (`document_models.py`: `DocumentMetadataResponse`, `DocumentUploadResponse`, `ArchiveJobResponse`) → `packages/storage/`

This report provides an exhaustive audit of all import statements across `apps/` that reference these four utility domains.

### Summary Statistics
- **Total `apps/` subdirectories scanned**: 17 (`compliance`, `ctms`, `designer`, `econsent`, `eisf`, `etmf`, `execution`, `gateway`, `interop`, `notifications`, `org`, `quality`, `safety`, `subject-portal`, `tickets`, `web`, `conftest.py`)
- **Total files with affected imports**: 9 files across 4 services (`designer`, `econsent`, `etmf`, `execution`)
- **Total import locations**: 11 import statements (including top-level and function-level inline imports)
- **Re-exports in `apps/`**: None

---

## Target Package Destination Mapping

| Relocated Utility | Key Symbols | Legacy Import Style | New Target Import |
|---|---|---|---|
| `audit.py` | `AuditFields`, `Part11AuditMixin` | `from audit import ...` | `from packages.database.audit import ...` |
| `datetime_helpers.py` | `AwareDatetime`, `serialize_utc_z`, `validate_timezone_aware_datetime` | `from datetime_helpers import ...` | `from packages.database.datetime_helpers import ...` |
| `signature.py` | `SigningReason`, `ApprovalStatus`, `SignatureManifestation` | `from signature import ...` | `from packages.security.signature import ...` |
| `storage/` | `ArchiveJobResponse`, `DocumentMetadataResponse`, `DocumentUploadResponse` | `from storage.document_models import ...` | `from packages.storage.document_models import ...` |

---

## Detailed Application Breakdown

### 1. `apps/designer`
- **File**: `apps/designer/main.py`
  - **Line 51**: Top-level import of `SigningReason`
    - Current: `from signature import SigningReason`
    - Updated: `from packages.security.signature import SigningReason`
  - **Line 2467**: Function-level inline import of `SignatureManifestation` in `post_signature_manifestation()`
    - Current: `from signature import SignatureManifestation`
    - Updated: `from packages.security.signature import SignatureManifestation`

### 2. `apps/econsent`
- **File**: `apps/econsent/main.py`
  - **Line 8**: Top-level import of `AuditFields`
    - Current: `from audit import AuditFields`
    - Updated: `from packages.database.audit import AuditFields`
  - **Line 1278**: Function-level inline import of `SignatureManifestation` and `SigningReason`
    - Current: `from signature import SignatureManifestation, SigningReason`
    - Updated: `from packages.security.signature import SignatureManifestation, SigningReason`
- **File**: `apps/econsent/tests/test_econsent.py`
  - **Line 7**: Top-level import of `AuditFields`
    - Current: `from audit import AuditFields`
    - Updated: `from packages.database.audit import AuditFields`

### 3. `apps/etmf`
- **File**: `apps/etmf/ingestion_service.py`
  - **Line 10**: Top-level import of `SignatureManifestation` and `SigningReason`
    - Current: `from signature import SignatureManifestation, SigningReason`
    - Updated: `from packages.security.signature import SignatureManifestation, SigningReason`
- **File**: `apps/etmf/main.py`
  - **Line 19**: Top-level import of `SigningReason`
    - Current: `from signature import SigningReason`
    - Updated: `from packages.security.signature import SigningReason`
  - **Line 2631**: Function-level inline import of `SignatureManifestation` in e-signature creation endpoint
    - Current: `from signature import SignatureManifestation`
    - Updated: `from packages.security.signature import SignatureManifestation`
- **File**: `apps/etmf/routers/archive.py`
  - **Line 14**: Top-level import of `ArchiveJobResponse`
    - Current: `from storage.document_models import ArchiveJobResponse`
    - Updated: `from packages.storage.document_models import ArchiveJobResponse`
- **File**: `apps/etmf/tests/test_etmf_signing_lifecycle.py`
  - **Line 7**: Top-level import of `SignatureManifestation`
    - Current: `from signature import SignatureManifestation`
    - Updated: `from packages.security.signature import SignatureManifestation`

### 4. `apps/execution`
- **File**: `apps/execution/routers/documents.py`
  - **Lines 22–25**: Top-level import of document response models
    - Current:
      ```python
      from storage.document_models import (
          DocumentMetadataResponse,
          DocumentUploadResponse,
      )
      ```
    - Updated:
      ```python
      from packages.storage.document_models import (
          DocumentMetadataResponse,
          DocumentUploadResponse,
      )
      ```
- **File**: `apps/execution/tests/test_signature_manifestation.py`
  - **Line 8**: Top-level import of signature enums and models
    - Current: `from signature import ApprovalStatus, SignatureManifestation, SigningReason`
    - Updated: `from packages.security.signature import ApprovalStatus, SignatureManifestation, SigningReason`
- **File**: `apps/execution/tests/test_soa_persistence.py`
  - **Line 411**: Function-level inline import inside `test_soa_persistence_with_audit_mixins()`
    - Current: `from audit import AuditFields, Part11AuditMixin`
    - Updated: `from packages.database.audit import AuditFields, Part11AuditMixin`

---

## Exhaustive Import Inventory Table

| Index | Target File Path | Line No. | Relocated Module | Current Import Statement | Required Updated Import Statement |
|---|---|---|---|---|---|
| 1 | `apps/designer/main.py` | 51 | `signature.py` | `from signature import SigningReason` | `from packages.security.signature import SigningReason` |
| 2 | `apps/designer/main.py` | 2467 | `signature.py` | `from signature import SignatureManifestation` | `from packages.security.signature import SignatureManifestation` |
| 3 | `apps/econsent/main.py` | 8 | `audit.py` | `from audit import AuditFields` | `from packages.database.audit import AuditFields` |
| 4 | `apps/econsent/main.py` | 1278 | `signature.py` | `from signature import SignatureManifestation, SigningReason` | `from packages.security.signature import SignatureManifestation, SigningReason` |
| 5 | `apps/econsent/tests/test_econsent.py` | 7 | `audit.py` | `from audit import AuditFields` | `from packages.database.audit import AuditFields` |
| 6 | `apps/etmf/ingestion_service.py` | 10 | `signature.py` | `from signature import SignatureManifestation, SigningReason` | `from packages.security.signature import SignatureManifestation, SigningReason` |
| 7 | `apps/etmf/main.py` | 19 | `signature.py` | `from signature import SigningReason` | `from packages.security.signature import SigningReason` |
| 8 | `apps/etmf/main.py` | 2631 | `signature.py` | `from signature import SignatureManifestation` | `from packages.security.signature import SignatureManifestation` |
| 9 | `apps/etmf/routers/archive.py` | 14 | `storage/` | `from storage.document_models import ArchiveJobResponse` | `from packages.storage.document_models import ArchiveJobResponse` |
| 10 | `apps/etmf/tests/test_etmf_signing_lifecycle.py` | 7 | `signature.py` | `from signature import SignatureManifestation` | `from packages.security.signature import SignatureManifestation` |
| 11 | `apps/execution/routers/documents.py` | 22-25 | `storage/` | `from storage.document_models import ( DocumentMetadataResponse, DocumentUploadResponse, )` | `from packages.storage.document_models import ( DocumentMetadataResponse, DocumentUploadResponse, )` |
| 12 | `apps/execution/tests/test_signature_manifestation.py` | 8 | `signature.py` | `from signature import ApprovalStatus, SignatureManifestation, SigningReason` | `from packages.security.signature import ApprovalStatus, SignatureManifestation, SigningReason` |
| 13 | `apps/execution/tests/test_soa_persistence.py` | 411 | `audit.py` | `from audit import AuditFields, Part11AuditMixin` | `from packages.database.audit import AuditFields, Part11AuditMixin` |

---

## Verification & Implementation Guidelines

1. **Import Sorting (I001 Compliance)**:
   Per `AGENTS.md`, imports must be ordered alphabetically by group. When replacing `from audit import ...` with `from packages.database.audit import ...`, place the line in group 3 (First-party) in alphabetical position.
2. **Inline Imports**:
   Pay special attention to function-level inline imports (e.g. line 2467 of `apps/designer/main.py`, line 1278 of `apps/econsent/main.py`, line 2631 of `apps/etmf/main.py`, and line 411 of `apps/execution/tests/test_soa_persistence.py`).
3. **Execution Commands**:
   - `uv run ruff check apps/` (with `--fix` for auto-formatting import ordering)
   - `uv run pytest apps/designer apps/econsent apps/etmf apps/execution`
