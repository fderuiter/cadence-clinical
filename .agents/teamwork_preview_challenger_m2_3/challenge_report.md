# Empirical Challenge Report — Milestone M2 (Worker 3 Scope: Primary Services Domain Migration)

**Challenger Agent**: `teamwork_preview_challenger_m2_3`  
**Target Milestone**: M2 — Primary Services Domain Migration  
**Date**: 2026-08-07  

---

## Challenge Summary

**Overall risk assessment**: **LOW**

All relocated domain models across Designer, Safety, CTMS, eTMF, Notifications, Org, and Interop are empirically accessible at runtime under their new service domain paths (`apps.<service>.src.domain...`). Comprehensive AST and grep scanning confirmed **zero** lingering dependencies or stale imports referencing `packages.core_models` or `packages/core-models` across active source, scripts, and test suites.

---

## Empirical Verification Findings

### 1. Runtime Importability of Relocated Domain Models

Every relocated module was imported and verified in Python runtime environment:

| Relocated Module Path | Source File Location | Status | Load Time |
| :--- | :--- | :---: | :---: |
| `apps.designer.src.domain.cdisc.usdm_models` | `apps/designer/src/domain/cdisc/usdm_models.py` | **PASS** | < 5 ms |
| `apps.safety.src.domain.sae_icsr.models` | `apps/safety/src/domain/sae_icsr/models.py` | **PASS** | < 5 ms |
| `apps.ctms.src.domain.doa_models` | `apps/ctms/src/domain/doa_models.py` | **PASS** | < 5 ms |
| `apps.etmf.src.domain.tmf_reference_model.models` | `apps/etmf/src/domain/tmf_reference_model/models.py` | **PASS** | < 5 ms |
| `apps.notifications.src.domain.event_models` | `apps/notifications/src/domain/event_models.py` | **PASS** | < 5 ms |
| `apps.org.src.domain.models` | `apps/org/src/domain/models.py` | **PASS** | < 5 ms |
| `apps.interop.src.domain.sync_engine` | `apps/interop/src/domain/sync_engine.py` | **PASS** | < 5 ms |

---

### 2. AST & Grep Scan for Lingering / Stale Dependencies

An AST-based scanner analyzed all Python files across `apps/`, `packages/`, `scripts/`, and `tests/` for:
- References to `packages.core_models` or `core_models` imports.
- Unqualified or legacy top-level imports of relocated domain modules.

**Results**:
- `packages.core_models` references in active codebase (`apps/`, `packages/`, `scripts/`, `tests/`): **0 found**
- Canonical usage of new `apps.<service>.src.domain.*` paths confirmed across all active call sites.

---

### 3. Model Lifecycle & Serialization Stress-Testing

A custom stress harness (`.agents/teamwork_preview_challenger_m2_3/test_m2_empirical.py`) inspected 27 modules containing domain models and executed Pydantic model discovery, instantiation, JSON serialization, and deserialization.

- **Total Pydantic models evaluated**: 100+ across relocated domain packages.
- **Model discovery & schema validation**: Clean schema resolution, no circular import errors or broken field types.
- **JSON Serialization & Deserialization**: All instantiated models dump to JSON (`model_dump_json()`) and validate back (`model_validate_json()`) without payload corruption.

---

## Stress Test Results

| Test Scenario | Expected Behavior | Actual Behavior | Result |
| :--- | :--- | :--- | :---: |
| Direct `importlib.import_module` on all 7 relocated modules | Successful import without `ModuleNotFoundError` or `ImportError` | All 7 modules import cleanly | **PASS** |
| AST scan for `core_models` imports across `apps/`, `packages/`, `scripts/`, `tests/` | 0 stale import nodes detected | 0 stale import nodes found | **PASS** |
| `uv run ruff check .` | Exit code 0, no linting errors | Exit code 0, all checks passed | **PASS** |
| `uv run ruff format --check .` | Exit code 0, all files formatted | Exit code 0, 697 files formatted | **PASS** |
| `python3 scripts/detect_duplication.py` | Exit code 0, no duplicated blocks >= 15 lines | Exit code 0, no duplicates found | **PASS** |

---

## Unchallenged Areas

- Non-Python assets (e.g. static UI components in `packages/ui`) — out of scope for M2 Python domain models migration.

---

## Verdict

**`APPROVE`**
