# Handoff Report: Explorer 2 (M1 R1 2)

**Author**: Explorer 2 (`teamwork_preview_explorer`)  
**Target Milestone**: Milestone M1: Foundational Core Utilities Migration  
**Scope**: Import statement analysis across `apps/` referencing relocated M1 modules (`audit.py`, `datetime_helpers.py`, `signature.py`, `storage/`).

---

## 1. Observation

Direct grep searches across `/Users/fred/Code/cadence-clinical/apps` revealed 13 specific import locations in 9 files across 4 microservices (`designer`, `econsent`, `etmf`, `execution`) that reference relocated M1 modules:

1. `apps/designer/main.py`
   - Line 51: `from signature import SigningReason`
   - Line 2467: `from signature import SignatureManifestation`
2. `apps/econsent/main.py`
   - Line 8: `from audit import AuditFields`
   - Line 1278: `from signature import SignatureManifestation, SigningReason`
3. `apps/econsent/tests/test_econsent.py`
   - Line 7: `from audit import AuditFields`
4. `apps/etmf/ingestion_service.py`
   - Line 10: `from signature import SignatureManifestation, SigningReason`
5. `apps/etmf/main.py`
   - Line 19: `from signature import SigningReason`
   - Line 2631: `from signature import SignatureManifestation`
6. `apps/etmf/routers/archive.py`
   - Line 14: `from storage.document_models import ArchiveJobResponse`
7. `apps/etmf/tests/test_etmf_signing_lifecycle.py`
   - Line 7: `from signature import SignatureManifestation`
8. `apps/execution/routers/documents.py`
   - Lines 22-25:
     ```python
     from storage.document_models import (
         DocumentMetadataResponse,
         DocumentUploadResponse,
     )
     ```
9. `apps/execution/tests/test_signature_manifestation.py`
   - Line 8: `from signature import ApprovalStatus, SignatureManifestation, SigningReason`
10. `apps/execution/tests/test_soa_persistence.py`
    - Line 411: `from audit import AuditFields, Part11AuditMixin`

Zero direct imports of `datetime_helpers.py` were found in `apps/`.
Zero re-exports of relocated M1 modules were found in `apps/` `__init__.py` files.

---

## 2. Logic Chain

1. **Step 1: Module Relocation Definition**:
   - `audit.py` (`Part11AuditMixin`, `AuditFields`) moves to `packages/database/audit.py`.
   - `datetime_helpers.py` moves to `packages/database/datetime_helpers.py` (or `packages/security/datetime_helpers.py`).
   - `signature.py` (`SigningReason`, `ApprovalStatus`, `SignatureManifestation`) moves to `packages/security/signature.py`.
   - `storage/` (`document_models.py`) moves to `packages/storage/`.

2. **Step 2: Legacy Import Identification**:
   - Legacy import patterns used implicit `sys.path` resolution (e.g. `from audit import AuditFields`, `from signature import SigningReason`, `from storage.document_models import ArchiveJobResponse`).

3. **Step 3: Required Target Import Statements**:
   - `from audit import ...` must be replaced with `from packages.database.audit import ...`.
   - `from signature import ...` must be replaced with `from packages.security.signature import ...`.
   - `from storage.document_models import ...` must be replaced with `from packages.storage.document_models import ...`.

4. **Step 4: Scope Mapping**:
   - All 13 import locations identified in Section 1 require updates by implementers during M1 execution.

---

## 3. Caveats

- **Scope Scope Boundary**: This report explicitly targets `apps/`. Imports within `packages/` or `scripts/` are covered by Explorer 1 (`explorer_m1_r1_1`).
- **Inline Function Imports**: 4 of the 13 import statements occur inside endpoint function bodies (`apps/designer/main.py:2467`, `apps/econsent/main.py:1278`, `apps/etmf/main.py:2631`, `apps/execution/tests/test_soa_persistence.py:411`). Implementers must update these inline imports in place rather than missing them during top-level refactoring.
- **Ruff Import Sorting (I001)**: Refactored top-level imports must be positioned in alphabetical order within first-party import blocks (`packages.*`).

---

## 4. Conclusion

All import locations in `apps/` referencing relocated M1 core utilities have been identified and mapped to their exact file paths, line numbers, current import syntax, and required target import syntax.

The full detailed report is stored at:
`/Users/fred/Code/cadence-clinical/.agents/explorer_m1_r1_2/analysis.md`

---

## 5. Verification Method

1. **File Inspection**:
   Inspect `/Users/fred/Code/cadence-clinical/.agents/explorer_m1_r1_2/analysis.md` to confirm the complete mapping table.
2. **Grep Validation**:
   Run grep across `apps/` for legacy import strings:
   ```bash
   grep -rn "from audit import" apps/
   grep -rn "from signature import" apps/
   grep -rn "from storage.document_models import" apps/
   ```
   After implementer updates, these queries should return 0 results.
3. **Automated Linter and Test Suite Verification**:
   ```bash
   uv run ruff check apps/
   uv run pytest apps/
   ```
