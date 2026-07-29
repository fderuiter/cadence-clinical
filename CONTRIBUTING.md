# Cadence Clinical — Contributor Guide

Welcome to the **Cadence Clinical** platform repository. This document is the
single authoritative reference for development workflows, coding standards,
quality gates, and CI failure recovery procedures.

> **Before you start**: run `make setup` (or `pnpm setup:dev`) once to install
> all dependencies, Playwright browsers, and local Git hooks.

---

## Quick Reference Card

The most common commands you will reach for day-to-day:

| Task | Command |
|---|---|
| Fix all ruff lint + format errors | `make fix` or `pnpm fix` |
| Run all pre-push quality gates | `make check` or `pnpm check` |
| Full verification (gates + tests) | `make verify` or `pnpm verify` |
| Sync GxP compliance docs | `make sync-gxp` or `pnpm sync-gxp` |
| Run tests only | `uv run pytest -n auto` |
| Scaffold a new ADR | `make adr` or `python3 scripts/create_adr.py ...` |
| Reset local database | `make db-reset` |
| See all make targets | `make help` |

---

## CI Failure Runbook

Use this table to resolve CI errors without digging through logs:

| CI Error Message | Root Cause | Fix |
|---|---|---|
| `I001 Import block is un-sorted or un-formatted` | New symbol added to an import block without maintaining alphabetical order | `make fix` — ruff auto-sorts all import blocks |
| `E712 Avoid equality comparisons to True/False` | Used `column == True` instead of SQLAlchemy `.is_(True)` | See [SQLAlchemy Boolean Filter Pattern](#sqlalchemy-boolean-filter-pattern) below |
| `GxP compliance documentation is out of sync` | RTM docs not regenerated after test changes | `make sync-gxp` — runs tests → generates RTM → stages docs |
| `Found N errors` (ruff, no `[*]`) | Manual fix needed; error is not auto-fixable | Check error code in ruff docs, fix manually, then `make fix` |
| `Coverage < 80%` | New code paths not covered by tests | Add tests for the uncovered lines shown in the coverage report |
| `ADR validation failed` | Architectural change without a matching ADR file | `make adr` to scaffold an ADR, then fill in the rationale |
| `Bandit: high severity issue found` | Security-sensitive code pattern detected | Review the flagged line; add `# nosec B...` with justification if intentional |

---

## 1. First-Time Setup

```bash
# Clone and enter the repo
git clone https://github.com/fderuiter/cadence-clinical.git
cd cadence-clinical

# Install everything and activate Git hooks
make setup
# Equivalent: pnpm setup:dev
```

This single command:
1. Installs Python 3.11 dependencies via `uv sync`
2. Installs Playwright Chromium for integration tests
3. Installs all pre-commit Git hooks (`pre-commit install --install-hooks`)
4. Installs Node/pnpm workspace dependencies

---

## 2. Branching & Workflow Strategy

All development uses short-lived branches off `main`.

### Branch Naming

| Type | Pattern | Example |
|---|---|---|
| Feature | `feature/<ticket>-<description>` | `feature/cad-104-enrollment-validation` |
| Bug Fix | `bugfix/<ticket>-<description>` | `bugfix/cad-215-signature-overflow` |
| Docs / Chore | `docs/<description>` or `chore/<description>` | `chore/ci-cd-fixes` |

### Workflow Lifecycle

```bash
# 1. Sync with upstream
git checkout main && git pull origin main

# 2. Create branch
git checkout -b feature/cad-XXX-my-feature

# 3. Develop, then auto-fix linting before committing
make fix

# 4. Run full verification before pushing
make verify

# 5. If you added/changed tests, sync GxP docs
make sync-gxp
git commit -m "docs(rtm): sync GxP compliance docs"

# 6. Push and open a PR against main
git push origin feature/cad-XXX-my-feature
```

---

## 3. Python Coding Patterns

### Import Ordering

All imports are enforced by ruff's isort (`I` rule set). Follow these rules to
avoid `I001` errors:

- **Standard library** imports first, alphabetical.
- **Third-party** imports second, alphabetical.
- **First-party** (`apps.*`, `packages.*`) imports third, alphabetical.
- Within a multi-name import block, symbols must be **alphabetical**.

```python
# CORRECT
import copy
import logging
from datetime import datetime
from typing import Any, List, Optional   # ← alphabetical within typing

from sqlalchemy import select

from apps.execution.database.models import (
    ClinicalObservation,    # ← alphabetical
    FormSubmission,
    StudyAuthoredRule,      # ← NOT appended at the end
)
```

**Fastest fix:** run `make fix` — ruff will sort everything automatically.

---

### SQLAlchemy Boolean Filter Pattern

> **This is a GxP-critical pattern.** Using it incorrectly generates wrong SQL
> that may silently return incorrect data sets.

Never use Python equality (`==`) to filter boolean SQLAlchemy columns in a
`.where()` clause. Ruff flags this as **E712** and it produces incorrect SQL.

```python
# ✘ WRONG — triggers E712; generates "col = 1" not "col IS TRUE"
stmt = select(StudyAuthoredRule).where(
    StudyAuthoredRule.is_active == True,
    StudyAuthoredRule.is_deleted == False,
)

# ✔ CORRECT — generates "col IS TRUE" / "col IS FALSE"
stmt = select(StudyAuthoredRule).where(
    StudyAuthoredRule.is_active.is_(True),
    StudyAuthoredRule.is_deleted.is_(False),
)
```

**Why:** `.is_(True)` calls SQLAlchemy's `ColumnElement.is_()` method which
emits the SQL `IS TRUE` / `IS FALSE` operator — database-portable, null-safe,
and semantically correct for tri-state boolean columns.

---

## 4. GxP Compliance Sync Workflow

The CI `compliance` job regenerates the RTM docs and diffs them against the
checked-in files. If they don't match, CI fails with:

```
GxP compliance documentation is out of sync with the current system state!
```

### When does this happen?

- You added or renamed test functions that reference requirement IDs.
- You added new `PRD-SYS-xxx` or `Trace-x` requirement IDs to tests.
- Test pass/fail counts changed since the last RTM commit.

### How to fix it

```bash
make sync-gxp
# Equivalent: pnpm sync-gxp
```

This runs three steps automatically:
1. `uv run pytest -n auto --junitxml=report.xml`
2. `uv run python scripts/generate_rtm.py`
3. `git add docs/SDLC/Requirements_Traceability_Matrix.md docs/SDLC/IQ_OQ_PQ_Execution_Report.md`

Then commit and push:

```bash
git commit -m "docs(rtm): sync GxP compliance docs with current test state"
git push
```

### Advanced options

```bash
# Validate-only (no test run, no file changes) — useful for a quick check
uv run python scripts/sync_gxp.py --dry-run

# Full sync AND auto-commit in one step
uv run python scripts/sync_gxp.py --commit
```

### Mapping requirement IDs in tests

For the RTM to track your test, add a requirement ID to the test docstring:

```python
async def test_enrollment_transition():
    """Validate subject state machine on enrollment.

    Requirements: PRD-SYS-042
    """
    ...
```

---

## 5. Issue-to-Documentation Synchronization

When addressing open GitHub issues or modifying system functionality, follow
the **3-Tier Documentation Cascade**:

### Tier 1 — Requirements (`PRD` / `SRS`)
Update `docs/SDLC/01_Product_Requirements_Document_PRD.md` or `docs/SRS.md`
and reference a Requirement ID (`PRD-SYS-xxx` or `Trace-x`).

### Tier 2 — Architecture Decisions (`ADR`)
If introducing architectural changes, scaffold a domain-indexed ADR:

```bash
make adr
# Or directly:
python3 scripts/create_adr.py --title "Your Title" --domain "core-platform" --req "PRD-SYS-001"
```

ADRs are automatically indexed into `docs/adr/index.md` and validated on commit.

**When an ADR is required:**
- Adding a new third-party library or database engine
- Modifying inter-service REST contracts
- Changing global database schemas

### Tier 3 — Traceability (`RTM`)
After updating tests, run `make sync-gxp` to regenerate and commit the RTM.

---

## 6. Quality Gates

All PRs must pass three gates before merging.

### Gate 1 — Docstrings & Documentation
- All public functions and classes: Google-style docstrings.
- Complex business logic (USDM transformers, state machines): inline comments
  explaining *why*, not just what.

### Gate 2 — Architecture Decision Records
See [Tier 2](#tier-2--architecture-decisions-adr) above.

### Gate 3 — Test Coverage
- Tests live in `tests/`.
- Backend: `pytest` + `pytest-asyncio`. Minimum **80%** coverage enforced.
- Frontend/packages: `vitest`.
- Reference requirement IDs in test docstrings for RTM traceability.

---

## 7. Pre-commit Hooks Reference

Hooks are installed automatically by `make setup`. They run at two stages:

### On `git commit`
| Hook | What it checks | Auto-fix? |
|---|---|---|
| `ruff` | Python lint (E, F, I rules) | ✅ Yes (`--fix`) |
| `ruff-format` | Python formatting | ✅ Yes |
| `trailing-whitespace` | Trailing whitespace | ✅ Yes |
| `end-of-file-fixer` | Missing newline at EOF | ✅ Yes |
| `check-yaml` | YAML syntax | ❌ Manual |
| `validate-adrs` | ADR index consistency | ❌ Manual (`make adrs:fix`) |
| `check-markdown-links` | Broken doc links | ❌ Manual |
| `detect-code-duplication` | High code similarity | ❌ Manual |
| `deid-compliance-scan` | PII/PHI in source files | ❌ Manual |

### On `git push`
| Hook | What it checks | Fix |
|---|---|---|
| `gxp-rtm-validate` | GxP docs up to date | `make sync-gxp` |
| `bandit` | Python security issues | Manual (or `# nosec`) |
| `detect-secrets` | Secrets in source | Remove secret, update baseline |

### Running hooks manually

```bash
# Run all hooks on all files
uv run pre-commit run --all-files

# Run a specific hook
uv run pre-commit run ruff --all-files

# Skip a single hook for one commit (use sparingly)
SKIP=gxp-rtm-validate git push
```

---

## 8. Human vs. AI Agent Boundary

This repository is co-authored by humans and autonomous AI agents. Agents must
comply with `AGENTS.md`. Human developers use this guide.

Key distinctions:
- **Human developers:** interactive shells, native `git`/`node`/`python`/`uv`, run checks in parallel using `pnpm check`.
- **AI agents:** strict directory target rules, no interactive prompts, `ruff`/`black` formatting only, must follow the 3-Tier Cascade Protocol on every PR.

---

## 9. Binary File Hygiene

- **Never commit `.docx` files.** They are gitignored. Use `python3 scripts/regenerate_templates.py` to rebuild protocol templates dynamically.
- **Never commit `report.xml`.** It is gitignored. Generate it locally with `uv run pytest --junitxml=report.xml` but do not stage it.
- **Never commit `.env` or secret files.** Use `detect-secrets` baseline workflow.

---

## 10. Additional Resources

| Document | Purpose |
|---|---|
| [`AGENTS.md`](AGENTS.md) | AI agent rules and directory target constraints |
| [`docs/LOCAL_DEV_ENVIRONMENT.md`](docs/LOCAL_DEV_ENVIRONMENT.md) | Port allocation map, service directory, Docker setup |
| [`docs/SDLC/ISSUE_STRUCTURE_GUIDE.md`](docs/SDLC/ISSUE_STRUCTURE_GUIDE.md) | Issue templates, work streams, project board automation |
| [`docs/adr/index.md`](docs/adr/index.md) | Architecture Decision Record index |
| [`docs/SDLC/Requirements_Traceability_Matrix.md`](docs/SDLC/Requirements_Traceability_Matrix.md) | Live RTM — test ↔ requirement coverage |
| [`Makefile`](Makefile) | All developer shortcuts — run `make help` |
