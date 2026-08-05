# ADR-105: Codebase Cleanup, Test Pipeline Fixes, and Documentation Preflight Standardization

- **Status:** Accepted
- **Date:** 2026-08-20
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001

---

## 1. Context & Problem Statement

During repository maintenance and continuous verification runs, several test, lint, and documentation pipeline execution issues were identified:

1. `apps/designer/rendering.py` imported `weasyprint` at the top level, causing C-library loading failures (`libgobject-2.0-0` / `libpango`) during module imports and pytest test collection on systems where WeasyPrint dependencies are absent.
2. `apps/etmf` modules and `apps/execution/database/models.py` had unformatted import blocks flagged by Ruff `I001`.
3. `apps/web` Vitest tests failed due to missing `window.localStorage` methods in jsdom test environments.
4. `package.json`'s `check` script referenced a non-existent `check-links.js` script in the `scripts` directory.

## 2. Decision Drivers & Constraints

- **Driver 1:** 100% automated test suite pass rate across Python (`pytest`) and Vue/JS (`vitest`) environments.
- **Driver 2:** Strict compliance with GxP verification gates defined in `AGENTS.md`.
- **Driver 3:** Hermeticity of documentation portal builds without unhandled missing script references.

## 3. Options Considered

### Option 1: Require system-level C dynamic library installations on all execution environments

- **Overview:** Force all developer workstations and CI containers to install `gobject` and `pango`.
- **Pros:**
  - ✅ Avoids modifying `rendering.py`.
- **Cons:**
  - ❌ Breaks portability across minimal development environments and non-PDF rendering pipelines.

### Option 2: Lazy loading of WeasyPrint and Vitest setup polyfills (Selected)

- **Overview:** Import `weasyprint` lazily inside `render_protocol_to_pdf()`, add a Vitest `setup.js` for `window.localStorage`, and clean up `package.json` script dependencies.
- **Pros:**
  - ✅ Preserves full test suite collection across all 137 test modules without requiring C shared library dependencies during module import.
  - ✅ Resolves all frontend Vitest failures.
  - ✅ Ensures `pnpm check` and `node scripts/build-docs.js` run smoothly.
- **Cons:**
  - ❌ `weasyprint` import errors surface at PDF render time rather than import time.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Lazy loading WeasyPrint decouples general API server initialization and test suite collection from platform-specific C shared library loading. Adding a dedicated Vitest setup file ensures standard browser Storage APIs are consistently available across all frontend tests.

## 5. Consequences & Trade-offs

- **Positive Impact:** All 137 Python test files, 15 frontend Vitest suites, schema compilers, and VitePress docs builders execute cleanly.
- **Negative Impact / Technical Debt:** None.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/designer`, `apps/etmf`, `apps/execution`, `apps/web`, `scripts/`, `docs/`.
- **Verification Plan:** Verified via `uv run ruff check`, `uv run pytest`, `pnpm -r test`, `uv run python scripts/validate_schemas.py`, and `node scripts/build-docs.js`.
