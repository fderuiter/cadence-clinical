# Agent Guidelines: Cadence Clinical Platform

## Product Mission
Cadence Clinical is a unified, standalone eClinical platform synthesizing upstream Clinical Metadata Management (MDR) with downstream Electronic Data Capture (EDC) into an automated Digital Data Flow (DDF) platform.

---

## Technical Stack & Standards

- **Language & Runtime:** Python 3.11+
- **Frameworks:** FastAPI, Pydantic v2 (strict typing required), HTTPX (async REST)
- **Code Style:** Black formatting, Ruff linting
- **Database Access:**
  - `apps/designer`: Async Neo4j Python Driver
  - `apps/execution`: Async SQLAlchemy / SQLModel for PostgreSQL
- **Standards:** CDISC USDM (v3.0/v4.0), CDISC ODM XML/JSON, 21 CFR Part 11 compliant audit fields (`created_at`, `created_by`, `reason_for_change`, `version_index`).

---

## Directory Target Rules for Generated Code

- Data models & CDISC schemas ──► `apps/designer/` and `apps/execution/`
- Study authoring / MDR logic ──► `apps/designer/`
- Data capture / eCRF logic ──► `apps/execution/`
- OIDC Auth & Routers ──► `apps/gateway/`
- Stack orchestration ──► `docker/`

---

## Issue-to-Documentation Synchronization Protocol

To keep requirements, specifications, decisions, and tests aligned across 100+ GitHub issues, agents must follow the **3-Tier Cascade Protocol**:

1. **Requirement Level (`PRD` / `SRS`)**:
   - Updates to scope or functionality must update `docs/SDLC/01_Product_Requirements_Document_PRD.md` or `docs/SRS.md` and reference a unique Requirement ID (`PRD-SYS-xxx` or `Trace-x`).
2. **Architecture & Decision Level (`ADR`)**:
   - Architectural or design changes require scaffolding a new ADR using the CLI helper:
     ```bash
     python3 scripts/create_adr.py --title "Short Title" --domain "core-platform" --req "PRD-SYS-xxx"
     ```
3. **Traceability Level (`RTM`)**:
   - Unit and integration tests must reference requirement IDs (`PRD-SYS-xxx`).
   - Run `node scripts/build-docs.js` to compile the portal and refresh the Requirements Traceability Matrix.

---

## Pull Request & Contribution Verification Standards

To maintain code health, architectural transparency, and GxP audit readiness across the **Cadence Clinical** monorepo, every Pull Request (PR) must satisfy three mandatory verification gates before being merged into `main`.

### Gate 1: Comprehensive Documentation & Docstrings
Every new module, class, function, and public API endpoint must be thoroughly documented.
* **Python Codebases (`apps/`, `packages/`):** All functions and classes must include clear docstrings following Google or NumPy style guidelines. Complex business logic (such as USDM-to-ODM transformers or state transition machines) must include inline comments explaining *why* a specific transformation pattern is applied.
* **Workspace Documentation (`docs/`):** If a PR introduces a new service boundary or changes an existing data flow, the corresponding Markdown documents (`docs/SRS.md`, `docs/SDLC/04_Data_Standards_Interoperability_Blueprint.md`, etc.) must be updated to reflect the new state.

### Gate 2: Architecture Decision Records (ADRs)
Cadence Clinical enforces a strict **"Code + Context"** design policy. Any PR that introduces significant architectural changes must include an Architecture Decision Record.
* **When is an ADR required?**
  * Adding a new third-party dependency or database engine.
  * Modifying inter-service data contracts or introducing new API gateways.
  * Changing data storage models (e.g., Neo4j graph nodes or PostgreSQL schema migrations).
* **Where do ADRs live & how to scaffold?**
  * Use the ADR helper tool: `python3 scripts/create_adr.py --title "..." --domain "..." --req "PRD-..."`
  * This creates `docs/adr/` files following the pattern `YYYY-MM-DD-short-title.md` and automatically indexes it under the correct domain in `docs/adr/index.md`.

### Gate 3: Mandatory Test Coverage & Verification Passes
No code is merged untested. Every feature, bug fix, or data transformation must be accompanied by automated tests.
* **Test Location:** All unit and integration tests must reside inside the `tests/` directory (e.g., `tests/test_transformers.py`).
* **Framework Requirements:**
  * Tests must run successfully using `pytest` and `pytest-asyncio`.
  * Integration tests must mock database interactions or spin up test containers where appropriate.
* **Automated Validation:** CI/CD execution environments will automatically execute `uv run pytest` and linting checks (`uv run ruff check`) prior to opening a Pull Request. Any test failures or un-typed functions will block the merge queue.

---

## Developer Experience & Agent Pain Point Prevention Standards

To prevent recurring development bottlenecks and maintain CI/CD stability:

1. **Script Signature Stability & Backward Compatibility**:
   - When modifying utility functions or automation scripts (e.g. `scripts/post_pr_comment.py`), maintain default parameter values (`repo="owner/repo"`, `pr_number="123"`) so existing test suites (`tests/test_pr_comment.py`) do not break.
2. **Binary File Hygiene (`.docx` Templates)**:
   - Do NOT track binary `.docx` files in git history. They must remain gitignored. Use `python3 scripts/regenerate_templates.py` to compile protocol templates dynamically on demand.
3. **Requirements Traceability Matrix (RTM) Synchronization**:
   - Whenever test cases are added or updated, execute `pnpm rtm` to regenerate `docs/SDLC/Requirements_Traceability_Matrix.md` and `docs/SDLC/IQ_OQ_PQ_Execution_Report.md`.
4. **CI Permission Drift Handling**:
   - Scripts interacting with GitHub APIs (such as `scripts/sync_ruleset.py`) must log non-blocking warnings on HTTP 403 permission errors unless strict mode (`FAIL_ON_RULESET_SYNC_ERROR="true"`) is explicitly set.

---

## Summary Checklist for Pull Requests

Before submitting a PR, verify it meets this checklist:

* [ ] Code is fully typed with strict Python type hints.
* [ ] Comprehensive docstrings are included on all public functions and classes.
* [ ] Unit and/or integration tests are added under `tests/`.
* [ ] An Architectural Decision Record (ADR) is added to `docs/adr/` if introducing major new design patterns.
* [ ] All local checks (`pnpm verify` or `uv run pytest`, `uv run ruff check`) pass successfully.
* [ ] GxP compliance reports are synchronized via `pnpm rtm`.
