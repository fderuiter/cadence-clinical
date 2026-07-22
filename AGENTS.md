# Agent Guidelines: Cadence Clinical Platform

## Product Mission
Cadence Clinical is a unified, standalone eClinical platform synthesizing upstream Clinical Metadata Management (MDR) with downstream Electronic Data Capture (EDC) into an automated Digital Data Flow (DDF) platform.

---

## Multi-Repository Context & System Boundaries

When operating within the multi-repository workspace, you have access to three connected repositories:

1. **`openstudybuilder-ref`** *(READ-ONLY REFERENCE)*
   - Use strictly as a reference blueprint for CDISC USDM graph algorithms, protocol versioning logic, and Schedule of Activities (SoA) definitions.
   - **DO NOT** edit or submit commits to this repository.

2. **`openclinica-ref`** *(READ-ONLY REFERENCE)*
   - Use strictly as a reference blueprint for eCRF form evaluation, OpenRosa/Enketo XForm rendering rules, subject state machines, query management, and audit log schemas.
   - **DO NOT** edit or submit commits to this repository.

3. **`cadence-clinical`** *(PRIMARY OWNED REPOSITORY)*
   - **ALL** new code generation, refactoring, API endpoints, Pydantic v2 domain models, Keycloak OIDC configs, and Docker setups MUST be authored strictly within this repository.

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

- Data models & CDISC schemas ──► `packages/core-models/`
- Study authoring / MDR logic ──► `apps/designer/`
- Data capture / eCRF logic ──► `apps/execution/`
- OIDC Auth & Routers ──► `apps/gateway/`
- Stack orchestration ──► `docker/`

---

## Pull Request & Contribution Verification Standards

To maintain code health, architectural transparency, and GxP audit readiness across the **Cadence Clinical** monorepo, every Pull Request (PR) must satisfy three mandatory verification gates before being merged into `main`.

### Gate 1: Comprehensive Documentation & Docstrings
Every new module, class, function, and public API endpoint must be thoroughly documented.
* **Python Codebases (`apps/`, `packages/`):** All functions and classes must include clear docstrings following Google or NumPy style guidelines. Complex business logic (such as USDM-to-ODM transformers or state transition machines) must include inline comments explaining *why* a specific transformation pattern is applied.
* **Workspace Documentation (`docs/`):** If a PR introduces a new service boundary or changes an existing data flow, the corresponding Markdown documents (`docs/SRS.md`, `docs/DATA_LIFECYCLE.md`, etc.) must be updated to reflect the new state.

### Gate 2: Architecture Decision Records (ADRs)
Cadence Clinical enforces a strict **"Code + Context"** design policy. Any PR that introduces significant architectural changes must include an Architecture Decision Record.
* **When is an ADR required?**
  * Adding a new third-party dependency or database engine.
  * Modifying inter-service data contracts or introducing new API gateways.
  * Changing data storage models (e.g., Neo4j graph nodes or PostgreSQL schema migrations).
* **Where do ADRs live?**
  * Create a new markdown file inside `docs/adr/` using the format `YYYY-MM-DD-short-title.md` (e.g., `docs/adr/2026-06-06-usdm-pydantic-models.md`).
  * If an existing ADR covers the architectural pattern, reference its ID in the PR description.

### Gate 3: Mandatory Test Coverage & Verification Passes
No code is merged untested. Every feature, bug fix, or data transformation must be accompanied by automated tests.
* **Test Location:** All unit and integration tests must reside inside the `tests/` directory (e.g., `tests/test_transformers.py`).
* **Framework Requirements:**
  * Tests must run successfully using `pytest` and `pytest-asyncio`.
  * Integration tests must mock database interactions or spin up test containers where appropriate.
* **Automated Validation:** CI/CD execution environments will automatically execute `poetry run pytest` and linting checks (`poetry run ruff check`) prior to opening a Pull Request. Any test failures or un-typed functions will block the merge queue.

---

## Summary Checklist for Pull Requests

Before submitting a PR, verify it meets this checklist:

* [ ] Code is fully typed with strict Python type hints.
* [ ] Comprehensive docstrings are included on all public functions and classes.
* [ ] Unit and/or integration tests are added under `tests/`.
* [ ] An Architectural Decision Record (ADR) is added to `docs/adr/` if introducing major new design patterns.
* [ ] All local checks (`poetry run pytest`, `poetry run ruff check`) pass successfully.
