# Agent Guidelines: Cadence Clinical Platform

## Product Mission

Cadence Clinical is a unified, standalone eClinical platform synthesising
upstream Clinical Metadata Management (MDR) with downstream Electronic Data
Capture (EDC) into an automated Digital Data Flow (DDF) platform.

---

## Technical Stack & Standards

| Concern | Technology |
|---|---|
| Language & Runtime | Python 3.14+ |
| Web Framework | FastAPI |
| Data Validation | Pydantic v2 (strict typing required — no `Any` shortcuts) |
| Async HTTP | HTTPX |
| Code Style | **Ruff** (lint + format); replaces Black. Run `uv run ruff format .` |
| Designer DB | Async Neo4j Python Driver (`apps/designer/`) |
| Execution DB | Async SQLAlchemy + SQLModel for PostgreSQL (`apps/execution/`) |
| Clinical Standards | CDISC USDM v3.0/v4.0, CDISC ODM XML/JSON |
| GxP Audit Fields | `created_at`, `created_by`, `reason_for_change`, `version_index` (21 CFR Part 11) |

---

## Directory Target Rules for Generated Code

| Code type | Target directory |
|---|---|
| Data models & CDISC schemas | `apps/designer/` **and** `apps/execution/` |
| Study authoring / MDR logic | `apps/designer/` |
| Data capture / eCRF logic | `apps/execution/` |
| OIDC Auth & API routers | `apps/gateway/` |
| Stack orchestration | `docker/` |
| Automation & helper scripts | `scripts/` |
| Unit & integration tests | `tests/` |
| Architecture Decision Records | `docs/adr/` |
| GxP compliance docs | `docs/SDLC/` (never edit manually — always via `scripts/sync_gxp.py`) |

---

## Critical Coding Patterns

### Import Ordering (I001)

Ruff enforces isort-style import ordering. **Violations are a blocking CI error.**
Agents must always write imports in the following order, with each group
alphabetically sorted:

```python
# 1. Standard library — alphabetical
import copy
import logging
from datetime import datetime
from typing import Any, List, Optional  # ← names inside also alphabetical

# 2. Third-party — alphabetical
from fastapi import Depends, HTTPException
from sqlalchemy import select

# 3. First-party — alphabetical by module path, names inside alphabetical
from apps.execution.database.models import (
    ClinicalObservation,  # ← A before F before S
    FormSubmission,
    StudyAuthoredRule,  # ← NEVER append new symbols at the end
)
```

**When adding a new symbol to an existing import block, insert it in
alphabetical position — never append it at the bottom of the list.**

To auto-fix after the fact: `uv run ruff check . --fix`

---

### SQLAlchemy Boolean Filter Pattern (E712)

> **GxP-critical.** Using Python `==` in a SQLAlchemy `.where()` clause emits
> `col = 1` SQL, which is semantically different from `col IS TRUE` and may
> silently return wrong result sets on nullable boolean columns.

```python
# ✘ WRONG — triggers ruff E712 and produces incorrect SQL
stmt = select(StudyAuthoredRule).where(
    StudyAuthoredRule.is_active == True,  # noqa won't save you here
    StudyAuthoredRule.is_deleted == False,
)

# ✔ CORRECT — emits IS TRUE / IS FALSE SQL via SQLAlchemy ORM
stmt = select(StudyAuthoredRule).where(
    StudyAuthoredRule.is_active.is_(True),
    StudyAuthoredRule.is_deleted.is_(False),
)
```

This pattern applies to **every** SQLAlchemy `.where()`, `.filter()`, and
`.having()` call that tests a boolean column.

---

### models.py Exclusion

`apps/execution/database/models.py` is excluded from all ruff checks — both
via the CLI `--exclude` flag **and** via `[tool.ruff.lint.per-file-ignores]`
in `pyproject.toml`. **Do not add `# noqa` directives to that file; the
exclusion is global.**

---

### REST API-First Architecture & Microservice Decoupling

To ensure proper GxP boundaries and architectural decoupling across the Cadence Clinical Platform, agents must adhere strictly to the following standards for all inter-service communications:

1. **No Sibling Database Imports:** Sibling database imports (of models, schemas, or session helpers) across distinct microservice boundary paths (e.g., CTMS importing execution database models) are strictly prohibited.
2. **REST Endpoints for Cross-App Operations:** All inter-service communications, state changes, and validations must be routed through secure, performance-optimized, and well-typed REST endpoints exposed by the owning microservice (e.g., `/api/v1/execution/doa/*`).
3. **Gateway Token Authentication:** Every cross-service HTTP client request must be authenticated using internal gateway signatures and tokens generated via `generate_gateway_signature(...)` from `packages.security.signing` to pass `GatewayAuthMiddleware` checks.
4. **SLA Enforcements:** High-performance, low-latency asynchronous connection pooling via `httpx.AsyncClient` must be maintained to adhere to our strict **100ms internal SLA.**

---
## GxP Compliance Sync Protocol

The CI `compliance` job regenerates the RTM docs and diffs them against the
checked-in files. If they diverge, CI fails with:

```
GxP compliance documentation is out of sync with the current system state!
```

### When agents must run the sync

Agents **must** run the GxP sync after any of the following:
- Adding, renaming, or removing test functions.
- Adding or changing requirement IDs in test docstrings.
- Any change that alters test pass/fail counts.

### The correct single command

```bash
uv run python scripts/sync_gxp.py
```

This script automates the full three-step workflow:

| Step | Action |
|---|---|
| 1 | `uv run pytest -n auto --junitxml=report.xml` |
| 2 | `uv run python scripts/generate_rtm.py` |
| 3 | `git add docs/SDLC/Requirements_Traceability_Matrix.md docs/SDLC/IQ_OQ_PQ_Execution_Report.md` |

Then commit the staged files:

```bash
git commit -m "docs(rtm): sync GxP compliance docs with current test state"
```

### Script flags

| Flag | Behaviour |
|---|---|
| *(none)* | Full sync — runs tests, generates RTM, stages docs |
| `--dry-run` | Validate only — no test run, no file changes, exits 1 if stale |
| `--commit` | Full sync + auto-commit (do not use in interactive agent sessions) |

### Deprecated approach — do not use

The old manual three-step sequence that AGENTS.md previously described is now
encapsulated by `sync_gxp.py`. **Never instruct a human or another agent to
run the three steps individually.**

```bash
# ✘ OLD — do not use
uv run pytest --junitxml=report.xml
python scripts/generate_rtm.py
git add docs/SDLC/
```

---

## Issue-to-Documentation Synchronization Protocol

To keep requirements, specifications, decisions, and tests aligned, agents must
follow the **3-Tier Cascade Protocol** on every PR:

### Tier 1 — Requirements (`PRD` / `SRS`)

Updates to scope or user-facing functionality must update
`docs/SDLC/01_Product_Requirements_Document_PRD.md` or `docs/SRS.md` and
reference a unique Requirement ID (`PRD-SYS-xxx` or `Trace-x`).

### Tier 2 — Architecture & Decision (`ADR`)

Architectural or design changes require scaffolding a new ADR:

```bash
python3 scripts/create_adr.py --title "Short Title" --domain "core-platform" --req "PRD-SYS-xxx"
```

This creates a dated ADR file under `docs/adr/` (e.g. `2026-07-29-short-title.md`) and
auto-indexes it in `docs/adr/index.md`.

**An ADR is required when:**
- Adding a new third-party dependency or database engine.
- Modifying inter-service data contracts or introducing new API gateways.
- Changing data storage models (Neo4j graph nodes or PostgreSQL schema migrations).

### Tier 3 — Traceability (`RTM`)

Tests must reference requirement IDs in their docstrings:

```python
async def test_enrollment_state_transition():
    """Validate subject state machine advances on enrollment trigger.

    @req:PRD-SYS-042
    """
    ...
```

After updating tests, run `uv run python scripts/sync_gxp.py` to regenerate
and commit the RTM (see [GxP Compliance Sync Protocol](#gxp-compliance-sync-protocol) above).

---

## CI Failure Runbook for Agents

When CI fails, use this table to identify the root cause and exact fix:

| CI Error | Root Cause | Agent Action |
|---|---|---|
| `I001 Import block is un-sorted or un-formatted` | New symbol inserted at wrong position in import block | `uv run ruff check . --fix` then verify the block is alphabetical |
| `E712 Avoid equality comparisons to True/False` | Used `col == True` in SQLAlchemy `.where()` | Replace with `col.is_(True)` / `col.is_(False)` — see [pattern above](#sqlalchemy-boolean-filter-pattern-e712) |
| `GxP compliance documentation is out of sync` | RTM docs not regenerated after test changes | `uv run python scripts/sync_gxp.py` then commit `docs/SDLC/` |
| `Would reformat: <file>` (ruff format check) | Code not formatted | `uv run ruff format .` |
| `Coverage < 80%` | New code paths not covered | Add tests for the uncovered lines in the coverage report |
| `ADR validation failed` | Architectural change without a matching ADR | `python3 scripts/create_adr.py ...` — fill in rationale |
| `Bandit: high severity issue` | Security-sensitive pattern in code | Fix the flagged pattern; if intentional add `# nosec B<code>: <justification>` |
| `Secret detected` | Credential or token in source | Remove the secret; update `.secrets.baseline` with `detect-secrets scan` |

---

## Pull Request Verification Standards

Every PR must satisfy **three mandatory gates** before merging into `main`.

### Gate 1 — Comprehensive Documentation & Docstrings

- All public functions, classes, and API endpoints: Google-style docstrings.
- Docstrings must state *what* the callable does, *args*, *returns*, and
  *raises*.
- Complex business logic (USDM-to-ODM transformers, state machines): inline
  comments explaining *why* a transformation is structured as it is.
- If the PR introduces a new service boundary or changes an existing data flow,
  update the corresponding `docs/SDLC/` Markdown documents.

### Gate 2 — Architecture Decision Records

See [Tier 2](#tier-2--architecture--decision-adr) above.

### Gate 3 — Mandatory Test Coverage

- Tests live in `tests/` (e.g., `tests/test_transformers.py`).
- Must run under `pytest` + `pytest-asyncio`. Minimum **80%** total coverage.
- Integration tests must mock database interactions or use test containers.
- CI runs: `uv run pytest -n auto --cov=apps --cov=packages --cov-fail-under=80`

---

## Developer Experience & Agent Pain Point Prevention

### 1. Script Signature Stability

When modifying utility scripts (e.g., `scripts/post_pr_comment.py`), maintain
default parameter values so existing test suites (`tests/test_pr_comment.py`)
do not break:

```python
def get_pr_metadata(
    repo: str = "owner/repo", pr_number: str = "123"
) -> tuple[str, list[str]]:
    pass
```

### 2. Binary File Hygiene

Never commit `.docx` files — they are gitignored. Rebuild protocol templates
dynamically:

```bash
python3 scripts/regenerate_templates.py
```

### 3. RTM Synchronization (updated)

The legacy `pnpm rtm` command only *validates* the RTM (read-only). To
**regenerate** and **commit** updated docs, always use:

```bash
uv run python scripts/sync_gxp.py   # or: pnpm sync-gxp / make sync-gxp
```

### 4. CI Permission Drift

Scripts interacting with GitHub APIs (`scripts/sync_ruleset.py`) must output
non-blocking `WARNING` log lines on HTTP 403 errors unless
`FAIL_ON_RULESET_SYNC_ERROR="true"` is explicitly set in the environment.

### 5. Import Block Maintenance

When adding a new import symbol, always insert it in **alphabetical position**
within its group. Running `uv run ruff check . --fix` after every write session
prevents I001 errors from accumulating.

### 6. No Bare Boolean Comparisons in ORM Queries

See [SQLAlchemy Boolean Filter Pattern](#sqlalchemy-boolean-filter-pattern-e712).
This rule is enforced by ruff E712 and must never be suppressed with `# noqa`
in ORM query code — use `.is_(True)` / `.is_(False)` instead.

### 7. Module Import Tracking Verification on Refactoring

When deleting or replacing legacy entry points (e.g. replacing legacy web helpers in favor of modular components under `apps/web/src/views/`), verify that all newly created helper modules are explicitly tracked and committed in git before opening a PR. Always verify `pnpm run build` locally.

### 8. OpenAPI Schema Export & Parity Synchronization

When adding or updating FastAPI routes (such as soft-delete/retirement endpoints in `apps/designer/main.py`), re-export the OpenAPI schemas using `uv run python scripts/validate_schemas.py --export-dir docs/openapi` and ensure contract parity tests in `tests/test_api_contract_validation.py` pass cleanly.

### 9. Gateway Auth Header & Tenant Signature Parity

When testing endpoints protected by `packages/security/middleware.py` (`GatewayAuthMiddleware`), pass canonical gateway headers generated by test helpers. Note that `verify_gateway_signature()` accommodates fallback legacy signatures when `X-Tenant-Id` is missing; do not enforce explicit tenant scopes in signature validation when defaulting missing claims to `tenant_default`.

### 10. Virtual Environment Sandbox Compatibility

When executing `uv run` commands in sandboxed terminal environments, ensure Python virtual environment binaries are synchronized (`uv sync --all-extras`) or run commands with appropriate environment permissions if subprocess execution requires external Python path resolution.

---

## Environment State Recovery

This section details instructions for autonomous agents to diagnose and recover from local environment blockages (e.g., port conflicts, database migration failures, or corrupted database states) independently, without requiring developer intervention.

### 1. Pre-Flight Port Allocation & Diagnostics

To verify system health and check for port collisions across the stack before running local services or tests, run:

```bash
make ports
```

- **Dynamic Orchestration Parsing:** Rather than using hardcoded system lists, the port diagnostic tool reads and parses actual port maps directly from `docker/docker-compose.yml` dynamically during normal execution.
- **Service Coverage:** The tool scans and verifies connection availability for all 13 system microservices, infrastructure, databases, and frontend portals:
  - **13 Microservices:** CTMS, Designer, Execution, Gateway, Interop, Notifications, Organization, Quality, Safety, Tickets, eConsent, eISF, and eTMF.
  - **Databases & Identity:** Postgres, Neo4j, and Keycloak.
  - **Frontends:** Subject Portal and Web Application.

### 2. Multi-Database State Recovery (Database Resets)

If you encounter corrupt local databases, stale schema migration states, or test data pollution, you can execute a full local state wipe and rebuild using:

```bash
make db-reset
```

- **Concurrently Re-applied Schema:** This command concurrently drops, re-creates, migrates, and seeds the PostgreSQL database, Neo4j graph database, and all 10 local microservice SQLite instances in parallel in under 15 seconds.
- **Mock Data Seeding:** It automatically seeds standard developer mock study nodes into the Neo4j graph database and populates Expected Document Lists (EDLs) into the eTMF SQLite database.

### 3. Strict Production & Remote Safety Guardrails

To prevent accidental destruction of production or remote databases, strict safety filters are embedded directly into the database reset utility:

- **Hostname Check:** The tool checks the host portion of all database connection URLs. Execution is blocked instantly if any host is detected that is not local (i.e. not `localhost`, `127.0.0.1`, `0.0.0.0`, `postgres`, `neo4j`, `db`, `host.docker.internal`, or `.local`).
- **Production Keyword Protection:** Execution is instantly halted if any connection string contains production-related strings or keywords (e.g., `production`, `prod`, `live`, `secure`, `aws`, `rds`, `azure`, `gcp`, or `cloud`).

### 4. Offline Recovery Mode (Bypassing Network Crashes)

When working in environments with limited or blocked network access, or when certain docker containers are offline/unreachable, standard database resets can fail. Bypasses are provided to allow offline database resets:

```bash
make db-reset-offline
```

- **Warning Generation:** This targets databases via `--allow-offline`, which generates non-blocking warnings on unreachable/offline databases instead of raising connection errors and crashing. This allows partial/SQLite-only resets to complete successfully even when central database services are down.

---

## Available Developer Tools

Agents may invoke these tools directly when needed:

| Command | Purpose |
|---|---|
| `make ports` | Check that all 13 microservice, database, and frontend ports are free and available |
| `make db-reset` | Concurrently wipe, migrate, and seed all SQL/NoSQL/graph databases in under 15 seconds |
| `make db-reset-offline` | Execute database resets offline, generating warnings instead of crashing if databases are unreachable |
| `uv run ruff check . --fix` | Auto-fix all fixable lint errors (I001, F-strings, etc.) |
| `uv run ruff format .` | Auto-format all Python files |
| `uv run python scripts/sync_gxp.py` | Full GxP compliance sync (tests → RTM → stage) |
| `uv run python scripts/sync_gxp.py --dry-run` | Validate GxP docs without modifying files |
| `python3 scripts/create_adr.py --title "..." --domain "..." --req "PRD-SYS-xxx"` | Scaffold ADR |
| `python3 scripts/validate_adrs.py --fix-index` | Rebuild the ADR index |
| `python3 scripts/validate_markdown.py` | Check all Markdown link integrity |
| `uv run pytest -n auto --cov=apps --cov=packages` | Run full test suite with coverage |
| `uv run bandit -c pyproject.toml -ll -ii -r apps packages` | Static security analysis |

---

## Summary Checklist for Pull Requests

Before submitting a PR, verify all items:

* [ ] All Python code is fully typed with strict type hints — no bare `Any`.
* [ ] All public functions and classes have Google-style docstrings.
* [ ] Imports are alphabetically ordered within each group (run `uv run ruff check . --fix`).
* [ ] SQLAlchemy boolean filters use `.is_(True)` / `.is_(False)` — not `== True` / `== False`.
* [ ] Unit and/or integration tests added under `tests/` with requirement IDs in docstrings.
* [ ] An ADR added to `docs/adr/` if the PR introduces a significant architectural change.
* [ ] All local checks pass: `uv run ruff check .` and `uv run ruff format --check .`
* [ ] GxP compliance docs are up to date: `uv run python scripts/sync_gxp.py` run and committed.
* [ ] `docs/SDLC/` Markdown docs updated if a service boundary or data flow changed.
* [ ] No binary `.docx` files, `report.xml`, or secrets are staged.
