# Milestone M1 Foundational Utilities Migration Analysis: Packages, Scripts, and Test Suites

**Author:** Explorer 3 (teamwork_preview_explorer)  
**Date:** 2026-08-07  
**Working Directory:** `/Users/fred/Code/cadence-clinical/.agents/explorer_m1_r1_3/`  
**Milestone:** M1 — Foundational Core Utilities Migration  

---

## Executive Summary

This report delivers the comprehensive analysis of import statements, script configurations, test suites, package re-exports, and build configurations (`pyproject.toml`) across `packages/`, `scripts/`, `tests/`, `apps/*/tests/`, `packages/*/tests/`, and `scripts/tests/`.

The four core foundational utility components being relocated in Milestone M1 are:
1. **`audit.py`** (`Part11AuditMixin`, `AuditFields`) $\rightarrow$ Relocating to `packages/database/audit.py`
2. **`datetime_helpers.py`** (`validate_timezone_aware_datetime`, `serialize_utc_z`, `AwareDatetime`) $\rightarrow$ Relocating to `packages/database/datetime_helpers.py`
3. **`signature.py`** (`SigningReason`, `ApprovalStatus`, `SignatureManifestation`) $\rightarrow$ Relocating to `packages/security/signature.py`
4. **`storage/`** (`document_models.py` containing `DocumentMetadataResponse`, `DocumentUploadResponse`, `ArchiveJobResponse`) $\rightarrow$ Relocating to `packages/storage/document_models.py`

---

## 1. Import Statements in `packages/`

| Target File Path | Line # | Current Import Statement | Proposed Replacement Import Statement | Destination Package |
|------------------|--------|--------------------------|---------------------------------------|---------------------|
| `packages/core-models/eligibility/models.py` | 14 | `from audit import Part11AuditMixin` | `from packages.database.audit import Part11AuditMixin` | `packages/database/audit.py` |
| `packages/core-models/organization_domain/__init__.py` | 5 | `from audit import AuditFields` | `from packages.database.audit import AuditFields` | `packages/database/audit.py` |
| `packages/core-models/organization_domain/models.py` | 12 | `from audit import AuditFields  # noqa: F401` | `from packages.database.audit import AuditFields  # noqa: F401` | `packages/database/audit.py` |
| `packages/core-models/protocol_authoring/models.py` | 14 | `from audit import AuditFields` | `from packages.database.audit import AuditFields` | `packages/database/audit.py` |
| `packages/core-models/protocol_authoring/models.py` | 15 | `from datetime_helpers import AwareDatetime` | `from packages.database.datetime_helpers import AwareDatetime` | `packages/database/datetime_helpers.py` |
| `packages/core-models/protocol_authoring/soa.py` | 11 | `from audit import AuditFields` | `from packages.database.audit import AuditFields` | `packages/database/audit.py` |
| `packages/core-models/protocol_render/models.py` | 12 | `from datetime_helpers import AwareDatetime` | `from packages.database.datetime_helpers import AwareDatetime` | `packages/database/datetime_helpers.py` |
| `packages/core-models/sdtm/models.py` | 13 | `from datetime_helpers import AwareDatetime` | `from packages.database.datetime_helpers import AwareDatetime` | `packages/database/datetime_helpers.py` |
| `packages/core-models/audit.py` (relocated source) | 7 | `from datetime_helpers import AwareDatetime` | `from packages.database.datetime_helpers import AwareDatetime` | `packages/database/audit.py` |
| `packages/core-models/signature.py` (relocated source) | 4 | `from datetime_helpers import AwareDatetime` | `from packages.database.datetime_helpers import AwareDatetime` | `packages/security/signature.py` |
| `packages/core-models/storage/__init__.py` | 1-5 | `from storage.document_models import (...)` | `from packages.storage.document_models import (...)` | `packages/storage/document_models.py` |

---

## 2. Script References and Configs in `scripts/`

| Target File Path | Line # | Current Code / Pattern | Impact & Required Update | Destination / Concern |
|------------------|--------|------------------------|--------------------------|-----------------------|
| `scripts/detect_duplication.py` | 252-253 | `"packages/core-models/audit.py",` in ignored duplicate pair | Update path string to `"packages/database/audit.py"` to maintain whitelist after relocation | Code Duplication Scanner |
| `scripts/regenerate_templates.py` | 13-16 | `sys.path.insert(0, os.path.join(..., "packages", "core-models"))` | Legacy sys.path insertion for template builder; no direct utility import updates required in M1 | Template Builder |

*Note: No active direct python imports of `audit`, `datetime_helpers`, `signature`, or `storage` were found in operational scripts under `scripts/`.*

---

## 3. Test Suites (`tests/`, `apps/*/tests/`, `packages/*/tests/`, `scripts/tests/`)

| Test File Path | Line # | Current Import Statement | Proposed Replacement Import Statement | Destination Package |
|----------------|--------|--------------------------|---------------------------------------|---------------------|
| `packages/core-models/tests/test_datetime_validation.py` | 13 | `from audit import AuditFields` | `from packages.database.audit import AuditFields` | `packages/database/audit.py` |
| `packages/core-models/tests/test_datetime_validation.py` | 17 | `from signature import SignatureManifestation, SigningReason` | `from packages.security.signature import SignatureManifestation, SigningReason` | `packages/security/signature.py` |
| `apps/econsent/tests/test_econsent.py` | 7 | `from audit import AuditFields` | `from packages.database.audit import AuditFields` | `packages/database/audit.py` |
| `apps/execution/tests/test_soa_persistence.py` | 411 | `from audit import AuditFields, Part11AuditMixin` | `from packages.database.audit import AuditFields, Part11AuditMixin` | `packages/database/audit.py` |
| `apps/org/tests/test_organization_domain.py` | 9 | `from audit import AuditFields` | `from packages.database.audit import AuditFields` | `packages/database/audit.py` |
| `apps/execution/tests/test_signature_manifestation.py` | 8 | `from signature import ApprovalStatus, SignatureManifestation, SigningReason` | `from packages.security.signature import ApprovalStatus, SignatureManifestation, SigningReason` | `packages/security/signature.py` |
| `apps/etmf/tests/test_etmf_signing_lifecycle.py` | 7 | `from signature import SignatureManifestation` | `from packages.security.signature import SignatureManifestation` | `packages/security/signature.py` |

---

## 4. Build Configurations & Package Dependencies (`pyproject.toml` and `__init__.py`)

### 4.1 `packages/core-models/pyproject.toml`
- **Line 31**: `"storage"` is listed under `tool.hatch.build.targets.wheel.packages`.
- **Action**: When `storage/` is relocated out of `packages/core-models/`, remove `"storage"` from `tool.hatch.build.targets.wheel.packages`.

### 4.2 `packages/database/pyproject.toml`
- **Action**: Ensure `pydantic>=2.6.0` is present under `dependencies` to support `Part11AuditMixin`, `AuditFields`, and `AwareDatetime`.

### 4.3 `packages/security/pyproject.toml`
- **Action**: Verified `pydantic>=2.6.0` is already present under `dependencies`. `signature.py` integrates directly.

### 4.4 `packages/storage/pyproject.toml`
- **Action**: Verified `pydantic>=2.6.0` is already present under `dependencies`. `document_models.py` integrates directly.

### 4.5 `packages/__init__.py`
- **Lines 6-10**: Controls `sys.path.insert(0, _core_models_path)`.
- **Action**: Retain during M1 for non-relocated domain models in `packages/core-models/` until Milestone M5.

---

## Conclusion & Implementation Order

1. **Phase 1: Relocation**: Move `audit.py`, `datetime_helpers.py`, `signature.py`, and `storage/` to `packages/database/`, `packages/security/`, and `packages/storage/`.
2. **Phase 2: Package Imports Update**: Update all imports across `packages/` domain files and test suites as cataloged above.
3. **Phase 3: Script & Config Alignment**: Update `scripts/detect_duplication.py` and `packages/core-models/pyproject.toml`.
4. **Phase 4: Verification**: Run `uv run ruff check . --fix`, `uv run ruff format .`, and `uv run pytest -n auto`.
