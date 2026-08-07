# Review Report — Milestone M2: Primary Services Domain Migration

**Reviewer Agent**: `teamwork_preview_reviewer_m2_1`  
**Verdict**: **REQUEST_CHANGES**  
**Date**: 2026-08-07  

---

## Executive Summary

An independent objective review and adversarial evaluation of Milestone M2 (Primary Services Domain Migration) was performed. All 7 primary domain models (`designer`, `safety`, `ctms`, `etmf`, `notifications`, `org`, `interop`) have been correctly relocated to `apps/<service>/src/domain/`. All legacy import statements referencing `packages/core-models/` for these 7 models have been eradicated across `apps/`, `packages/`, `scripts/`, and `tests/`. Empirical testing confirmed that attempting to import legacy paths raises `ModuleNotFoundError` and importing new locations succeeds cleanly. Code duplication scanning (`scripts/detect_duplication.py`) and GxP compliance documentation (`scripts/sync_gxp.py`) pass cleanly. All 2148 unit and integration tests pass.

However, the mandatory repository quality gates (`uv run ruff check .` and `uv run ruff format --check .`) **FAILED**. Consequently, the verdict is **REQUEST_CHANGES**.

---

## Detailed Findings

### Finding 1 [Major]: Formatting Defect in `scripts/detect_duplication.py`

- **What**: `uv run ruff format --check .` flags `scripts/detect_duplication.py` as requiring reformatting.
- **Where**: `scripts/detect_duplication.py:277-286`
- **Why**: Worker updated `scripts/detect_duplication.py` to update whitelist entries (e.g. updating `packages/core-models/audit.py` to `packages/database/audit.py` and adding whitelist pairs for `apps/designer/soa_models.py` / `soa.py` and `rules.py` / `usdm_ingestion.py`). However, consecutive blank lines were introduced inside the whitelist set definitions, violating ruff formatting rules.
- **Suggestion**: Run `uv run ruff format scripts/detect_duplication.py` to format the file cleanly.

### Finding 2 [Major]: Workspace Layout Violation & Unformatted Script in `.agents/`

- **What**: Executable python script `.agents/teamwork_preview_challenger_m2_1/verify_m2.py` violates layout rules and breaks repository linting and formatting gates.
- **Where**: `.agents/teamwork_preview_challenger_m2_1/verify_m2.py`
- **Why**: 
  1. `AGENTS.md` explicitly specifies: `⚠️ .agents/ holds only agent metadata (plans, progress, handoffs). NEVER place source code, tests, or data files here.`
  2. Running `uv run ruff check .` fails with `UP015 [*] Unnecessary mode argument` at `.agents/teamwork_preview_challenger_m2_1/verify_m2.py:98:26`.
  3. Running `uv run ruff format --check .` fails due to unformatted code in `.agents/teamwork_preview_challenger_m2_1/verify_m2.py`.
- **Suggestion**: Remove or relocate `.agents/teamwork_preview_challenger_m2_1/verify_m2.py` outside `.agents/` (or ensure `.agents` content is cleaned up / excluded if transient) so that `uv run ruff check .` and `uv run ruff format --check .` pass without errors.

---

## Verified Claims

| Claim / Requirement | Verification Method | Result |
| --- | --- | --- |
| Relocate `designer` domain models to `apps/designer/src/domain/` | `find_by_name` / `ls` inspection | **PASS** |
| Relocate `safety` domain models to `apps/safety/src/domain/` | `find_by_name` / `ls` inspection | **PASS** |
| Relocate `ctms` domain models to `apps/ctms/src/domain/` | `find_by_name` / `ls` inspection | **PASS** |
| Relocate `etmf` domain models to `apps/etmf/src/domain/` | `find_by_name` / `ls` inspection | **PASS** |
| Relocate `notifications` domain models to `apps/notifications/src/domain/` | `find_by_name` / `ls` inspection | **PASS** |
| Relocate `org` domain models to `apps/org/src/domain/` | `find_by_name` / `ls` inspection | **PASS** |
| Relocate `interop` domain models to `apps/interop/src/domain/` | `find_by_name` / `ls` inspection | **PASS** |
| Eradicate old import paths for relocated models | AST / `grep_search` across `apps/`, `packages/`, `scripts/`, `tests/` | **PASS** (0 matches found) |
| Negative import isolation | Empirical `python -c "__import__('packages.core_models.cdisc')"` | **PASS** (raises `ModuleNotFoundError`) |
| Target domain model loading | Empirical `python -c "__import__('apps.<service>.src.domain...')"` | **PASS** (all 10 modules import cleanly) |
| Package wheel builds | `uv build --package packages-core-models` / `database` / `security` / `storage` | **PASS** |
| Code duplication detection | `python3 scripts/detect_duplication.py` | **PASS** |
| Test suite execution | `uv run pytest -n auto` | **PASS** (2148 tests passed) |
| GxP documentation sync | `uv run python scripts/sync_gxp.py --dry-run` | **PASS** (`✔ GxP docs up to date`) |
| Ruff linting check | `uv run ruff check .` | **FAIL** (`UP015` in `verify_m2.py`) |
| Ruff formatting check | `uv run ruff format --check .` | **FAIL** (`detect_duplication.py` & `verify_m2.py`) |

---

## Coverage Gaps & Stress Test Findings

- **Stress Test Scenario**: Tried importing legacy module paths from outside `packages.core_models`. All 10 legacy module paths cleanly raise `ModuleNotFoundError`.
- **Integrity Violation Check**: No hardcoded test results, facade implementations, or shortcuts were found in relocated models. Real domain models were preserved and updated correctly.
- **Quality Gate Violation**: Production script `scripts/detect_duplication.py` failed `ruff format --check .` and script `.agents/teamwork_preview_challenger_m2_1/verify_m2.py` failed both `ruff check .` and `ruff format --check .`.

---

## Unverified Items

- None. All requirements, domain relocations, import paths, linting, formatting, duplication, test suite, wheel packaging, and GxP documentation synchronization were empirically verified.

---

## Verdict Rationale

While the domain model relocations, import updates, test suite execution, and GxP compliance documentation sync were executed cleanly, the repository quality gate requires `uv run ruff check .` and `uv run ruff format --check .` to exit with status code 0 (clean pass). Because formatting and linting errors exist in `scripts/detect_duplication.py` and `.agents/teamwork_preview_challenger_m2_1/verify_m2.py`, the required verdict is **REQUEST_CHANGES**.
