# Forensic Audit Report — Milestone M2: Primary Services Domain Migration

**Work Product**: Milestone M2: Primary Services Domain Migration (`apps/<service>/src/domain/` & `packages/core-models`)  
**Profile**: General Project (Demo Mode)  
**Verdict**: **INTEGRITY VIOLATION**  
**Audit Date**: 2026-08-07  
**Auditor**: Forensic Auditor 1 (`teamwork_preview_auditor_m2_1`)  

---

## Executive Summary

A comprehensive forensic audit was conducted on Milestone M2: Primary Services Domain Migration. Ground-truth requirements established in `ORIGINAL_REQUEST.md` (Requirement R1 & Acceptance Criterion 1) dictate:
> **R1. Eradicate `packages/core-models`**: Move all domain models currently in `packages/core-models` to the `src/domain/` folder of the service that rightfully owns them.  
> **Structural Integrity**: The directory `packages/core-models` no longer exists.

While all 27 relocated domain model modules in `apps/<service>/src/domain/` (`designer`, `safety`, `ctms`, `etmf`, `notifications`, `org`, `interop`) were verified to contain authentic Pydantic v2 / SQLModel schemas, field validators, and DTOs without hardcoded test shortcuts or facade implementations, **the overall work product fails the mandatory eradication constraint.**

Specifically, **the directory `packages/core-models` still exists on disk**, containing 45 Python files across `execution/`, `localization/`, `sdtm/`, and `tests/`. Furthermore, `pyproject.toml` retains `packages-core-models` as an active workspace source, and `packages/__init__.py` continues to inject `packages/core-models` into `sys.path` to support legacy imports (e.g. `sdtm.*`) from `apps/execution`.

Under the Integrity Forensics Protocol, if ANY check fails, the verdict must be **INTEGRITY VIOLATION** and the work product rejected.

---

## Phase Audit Results

| # | Forensic Check | Status | Key Finding |
|---|---|:---:|---|
| 1 | **Eradication of `packages/core-models`** | 🔴 **FAIL** | `packages/core-models` directory exists on disk with 45 Python files, registered in `pyproject.toml`, and injected via `sys.path` in `packages/__init__.py`. |
| 2 | **Relocated Domain Model Audit (27 Modules)** | 🟢 **PASS** | All 27 relocated domain model modules across `apps/*/src/domain/` contain genuine Pydantic v2 / SQLModel schemas, validators, and DTOs. |
| 3 | **Facade & Hardcoded Shortcut Detection** | 🟢 **PASS** | Zero hardcoded test shortcuts, fake return values, or facade implementations detected in relocated domain modules. |
| 4 | **Cross-Service Sibling DB Import Decoupling** | 🟢 **PASS** | Zero cross-service database model imports (`apps.<app>.models` / `database`) found in production `src` code. |
| 5 | **Anti-Corruption Layer (ACL) DTOs** | 🟢 **PASS** | Cross-service data exchanges are handled via local DTO transport models (`usdm_transport_models`, `doa_transport_models`, `eisf_transport_models`, `synopsis_transport_models`). |
| 6 | **Static Analysis & Test Execution** | 🔴 **FAIL** | `ruff check` and `ruff format` passed cleanly (0 errors), but test suite execution (`uv run pytest -n auto`) resulted in 8 failures out of 2,148 tests (91.67% total coverage). |

---

## Forensic Evidence & Findings

### Finding 1: Failure to Eradicate `packages/core-models` (INTEGRITY VIOLATION)

#### Evidence 1.1: Directory Presence on Filesystem
Running empirical filesystem test `test -d packages/core-models` returned `EXISTS` (exit code 0).
The directory contains **45 Python files**:
- **`packages/core-models/execution/`** (13 files): `doa_models.py`, `econsent_models.py`, `eisf_models.py`, `epro_transport_models.py`, `lab_models.py`, `lab_transport_models.py`, `lock_models.py`, `lock_transport_models.py`, `offline_models.py`, `safety_models.py`, `safety_transport_models.py`, `sdv_transport_models.py`, `signature_transport_models.py`.
- **`packages/core-models/localization/`** (2 files): `__init__.py`, `models.py`.
- **`packages/core-models/sdtm/`** (6 files): `__init__.py`, `dataset_json_models.py`, `enums.py`, `models.py`, `scrubber_models.py`, `sdtm_models.py`, `terminology.py`.
- **`packages/core-models/watermark.py`**
- **`packages/core-models/pyproject.toml`**
- **`packages/core-models/tests/`** (22 test files).

#### Evidence 1.2: Workspace Source Registration in `pyproject.toml`
Root `pyproject.toml` line 26 maintains active workspace registration for `packages-core-models`:
```toml
[tool.uv.sources]
packages-security = { workspace = true }
packages-database = { workspace = true }
packages-deid = { workspace = true }
packages-storage = { workspace = true }
packages-core-models = { workspace = true } # <-- Non-compliant retention
```

#### Evidence 1.3: Active `sys.path` Injection in `packages/__init__.py`
`packages/__init__.py` (lines 6-10) actively injects `packages/core-models` into `sys.path`:
```python
_core_models_path = os.path.abspath(
    os.path.join(os.path.dirname(__file__), "core-models")
)
if _core_models_path not in sys.path:
    sys.path.insert(0, _core_models_path)
```

#### Evidence 1.4: Lingering Core-Models Imports in `apps/execution/`
Production modules in `apps/execution` bypass standard module namespace resolution and import directly from `packages/core-models` via `sys.path`:
- `apps/execution/biostat/terminology.py:1`: `from sdtm.enums import AESeverity, Race, Sex`
- `apps/execution/exports/sdtm_json_builder.py:8`: `from sdtm.scrubber_models import DeidentConfig`
- `apps/execution/sdtm_mapper.py:16`: `from sdtm.models import AE, CM, DM, LB, VS`
- `apps/execution/services/sdtm_mapper.py:15`: `from sdtm.sdtm_models import ...`

---

### Finding 2: Verification of the 27 Relocated Domain Model Modules (PASS)

All 27 relocated domain model modules across `apps/<service>/src/domain/` were audited via AST analysis and manual code review:

1. `apps/ctms/src/domain/doa_models.py` (74 lines, 4 classes) — Genuine Pydantic DTOs for CTMS DOA delegation.
2. `apps/ctms/src/domain/doa_transport_models.py` (63 lines, 4 classes) — Transport schemas for CTMS delegation API requests.
3. `apps/designer/src/domain/cdisc/terminology_cache.py` (278 lines, 1 class, 8 methods) — SQLite caching implementation for CDISC terminology.
4. `apps/designer/src/domain/cdisc/sentinel_models.py` (153 lines, 8 classes) — Pydantic domain models for rule evaluation.
5. `apps/designer/src/domain/cdisc/usdm_transport_models.py` (38 lines, 3 classes) — USDM DTO transport objects.
6. `apps/designer/src/domain/cdisc/usdm_models.py` (125 lines, 9 classes) — CDISC USDM v3/v4 models.
7. `apps/designer/src/domain/cdisc/cascade_models.py` (45 lines, 2 classes) — Cascade propagation models.
8. `apps/designer/src/domain/cdisc/usdm_importer.py` (110 lines, 2 classes, 2 methods) — Neo4j USDM graph import handler.
9. `apps/designer/src/domain/cdisc/branch_models.py` (62 lines, 3 classes) — Graph branching models.
10. `apps/designer/src/domain/cdisc/cdisc_library_client.py` (411 lines, 7 classes, 9 methods) — Async HTTP client for CDISC API.
11. `apps/designer/src/domain/protocol_render/models.py` (253 lines, 11 classes, 1 validator) — Rendering domain schemas.
12. `apps/designer/src/domain/document_renderer.py` (191 lines, 1 class, 2 methods) — PDF/DOCX document generator using WeasyPrint & docxtpl.
13. `apps/designer/src/domain/protocol_authoring/models.py` (396 lines, 13 classes) — ICH protocol structure models.
14. `apps/designer/src/domain/protocol_authoring/soa.py` (657 lines, 41 classes, 12 validators) — Schedule of Activities models & validator logic.
15. `apps/designer/src/domain/eligibility/models.py` (393 lines, 8 classes, 5 validators) — Eligibility expression models & validator logic.
16. `apps/designer/src/domain/eligibility/parser.py` (248 lines, 2 classes, 14 methods) — Recursive descent DSL parser.
17. `apps/designer/src/domain/eligibility/evaluator.py` (363 lines, 5 functions) — AST tree evaluator for eligibility criteria.
18. `apps/designer/src/domain/protocol_version_ref/models.py` (84 lines, 2 classes, 3 validators) — Version reference DTOs.
19. `apps/designer/src/domain/usdm_ingestion.py` (550 lines, 4 classes, 11 methods, 1 validator) — USDM JSON ingestion transformer.
20. `apps/designer/src/domain/synopsis_transport_models.py` (38 lines, 2 classes) — Synopsis DTOs.
21. `apps/etmf/src/domain/tmf_reference_model/models.py` (103 lines, 4 classes, 4 methods) — OASIS TMF Reference Model schemas.
22. `apps/etmf/src/domain/etmf/eisf_models.py` (54 lines, 2 classes) — eISF document schemas.
23. `apps/etmf/src/domain/etmf/eisf_transport_models.py` (70 lines, 3 classes) — eISF DTO transport models.
24. `apps/interop/src/domain/sync_engine.py` (239 lines, 3 classes, 4 methods) — Data synchronization engine.
25. `apps/notifications/src/domain/event_models.py` (48 lines, 2 classes) — Event notification schemas.
26. `apps/org/src/domain/models.py` (55 lines, 3 classes) — Organization & staff schemas.
27. `apps/safety/src/domain/sae_icsr/models.py` (349 lines, 9 classes, 18 methods, 15 validators) — E2B(R3) ICSR safety models & validators.

**Result**: PASS. All 27 modules contain complete, authentic domain logic. No dummy mocks or facade returns were present in these modules.

---

### Finding 3: Sibling Database Model Isolation (PASS)

Analysis of cross-app imports in `apps/` confirmed **0 occurrences** where a service imports database models (`apps.<app>.models` or `apps.<app>.database`) from another service in production `src` code. All inter-service communications are properly routed through DTO transport models located in `src/domain/`.

---

### Finding 4: Static Analysis & Test Execution Summary

- `uv run ruff check apps packages scripts tests` -> Passed cleanly (0 errors).
- `uv run ruff format --check apps packages scripts tests` -> Passed cleanly (691 files formatted).
- `uv run pytest -n auto` -> Executed 2,148 tests: **2,140 passed**, **8 failed** (in `packages/database/tests/` and `scripts/tests/`), total coverage **91.67%**.

---

## Required Remediation Actions

To resolve the **INTEGRITY VIOLATION** and achieve compliance with `ORIGINAL_REQUEST.md`:

1. **Relocate Remaining Core Models**:
   - Move `packages/core-models/sdtm/` to `apps/execution/src/domain/sdtm/` (or `packages/sdtm/`).
   - Move `packages/core-models/execution/` models to `apps/execution/src/domain/`.
   - Move `packages/core-models/localization/` models to `apps/execution/src/domain/localization/` or `packages/localization/`.
2. **Remove Directory**: Delete `packages/core-models` completely.
3. **Clean Configuration**:
   - Remove `packages-core-models` entry from `pyproject.toml`.
   - Remove `_core_models_path` and `sys.path.insert` from `packages/__init__.py`.
4. **Update Imports**: Update all `from sdtm...` and `from execution...` imports in `apps/execution` to reference local domain paths (`from apps.execution.src.domain.sdtm...`).

---

## Verdict Statement

**VERDICT: INTEGRITY VIOLATION**

The work product fails Acceptance Criterion 1 ("The directory `packages/core-models` no longer exists"). Despite high code quality in the 27 relocated domain modules, the incomplete eradication of `packages/core-models` requires rejecting the work product.
