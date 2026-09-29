# Cadence Clinical Research Software — Contributor Guide

Welcome to the **Cadence Clinical Research Software** platform repository. This document is the
single authoritative reference for development workflows, coding standards,
quality gates, and CI failure recovery procedures.

> **Before you start**: on a brand-new machine/Codespace/devcontainer, run
> `make wizard` once to install the .NET 10 SDK plus all dependencies,
> Playwright browsers, and local Git hooks. Already set up? `make setup` (or
> `pnpm setup:dev`) refreshes just the Python/Node side.

---

## Quick Reference Card

The most common commands you will reach for day-to-day (via the unified Cadence CLI or Make):

| Task                           | .NET 10 / CLI Command                                         | Description / Shortcut                                                    |
| :----------------------------- | :------------------------------------------------------------ | :------------------------------------------------------------------------ |
| Build Entire Solution          | `dotnet build Cadence.sln`                                    | Compiles all projects under net10.0                                       |
| Fast Unit Tests                | `dotnet test --filter "Category!=Integration"`                | Sub-second unit tests across projects                                     |
| Architecture Sentinels         | `dotnet test tests/Cadence.ArchitectureTests.csproj`          | Verifies Clean Architecture purity via ArchUnitNET                        |
| Integration Tests (PG / Neo4j) | `dotnet test --filter "Category=Integration"`                 | Runs tests with Testcontainers                                            |
| Code Style & Formatting        | `dotnet format --verify-no-changes`                           | Validates .editorconfig & Roslyn rules                                    |
| Developer Onboarding Wizard    | `dotnet cadence wizard onboarding` / `make wizard`            | Full-stack setup: .NET SDK, Node/pnpm, Python/uv, certs, datastores, .env |
| .NET-Only Onboarding Wizard    | `dotnet cadence wizard dotnet`                                | Just the .NET 10 SDK, global tools, dev certs                             |
| Microservice Migration Wizard  | `dotnet cadence wizard migrate <name>`                        | Interactive Clean Architecture scaffolding                                |
| Service Decommission Wizard    | `dotnet cadence wizard decommission <name>`                   | Guided 4-step legacy Python retirement                                    |
| Manage Database Migrations     | `powershell .\scripts\manage_migrations.ps1 add <svc> <name>` | Manages EF Core PostgreSQL migrations                                     |
| Sync GxP Compliance Docs       | `python scripts/sync_gxp.py`                                  | `make sync-gxp`                                                           |
| Scaffold New ADR               | `python scripts/create_adr.py --title "Title"`                | `make adr`                                                                |
| Activate Git Pre-Commit Hooks  | `git config core.hooksPath .githooks`                         | Enforces format and ADR checks on commit                                  |

---

## Cadence Developer CLI (`packages/cli`)

The repository features a unified CLI (`uv run cadence`) designed to maximize developer velocity and streamline local orchestration:

### 1. Interactive Dev TUI Cockpit (`cadence dev --tui`)

Runs and monitors all 20+ active microservices (`gateway`, `designer`, `execution`, `etmf`, `ctms`, `interop`, etc.) in an interactive terminal cockpit with live health monitoring and log streaming:

- `[r]`: Restart all services
- `[g]`: Hot-restart Gateway API (`8000`)
- `[e]`: Hot-restart Execution Engine (`8002`)
- `[d]`: Hot-restart Designer MDR (`8001`)
- `[c]`: Hot-restart CTMS (`8005`)
- `[q]`: Gracefully exit and terminate processes

### 2. Smart Test Watcher (`cadence test --watch` & `--fast`)

- `cadence test --watch`: Monitors source files across `apps/`, `packages/`, and `tests/` and automatically executes the relevant test file when changes are saved.
- `cadence test --fast`: Runs unit tests using in-memory test harnesses bypassing heavy global coverage calculation for instant iteration.

### 3. Diagnostic Healing (`cadence doctor --auto-fix`)

Inspects system prerequisites, ports, databases, and environment configuration. Passing `--auto-fix` automatically provisions missing local SQLite schemas (`econsent.db`, `eisf.db`, `interop.db`, `notifications.db`, `safety.db`, `tickets.db`).

### 4. Quality Sentinel Check (`cadence check`)

`dotnet cadence check` runs all 21 quality gates. The pre-commit hook runs it, and CI runs each gate as well. Run one gate with `dotnet cadence check <gate>`; an unknown gate name fails.

| Gate (short name)     | What it checks                                                      |
| --------------------- | ------------------------------------------------------------------- |
| `dotnet-format`       | Whitespace formatting against `.editorconfig`                       |
| `dotnet-format-style` | Roslyn code-style rules                                             |
| `clean-secrets`       | Normalizes `.secrets.baseline`                                      |
| `duplication`         | Duplicated code blocks over 15 normalized lines                     |
| `cli-docs`            | `docs/cli.md` matches the CLI's commands                            |
| `adrs`                | ADR schema and `docs/adr/index.md` consistency                      |
| `arch-drift`          | Architecture diagrams match the solution                            |
| `sanitizer`           | Requirement tag prefixes in tests                                   |
| `markdown`            | Markdown structure and links                                        |
| `env-vars`            | Environment variables documented in `docs/ENVIRONMENT_VARIABLES.md` |
| `readmes`             | Each project has a README                                           |
| `imports`             | No cross-service persistence references                             |
| `security`            | Static security audit and secret patterns                           |
| `deid`                | PHI/PII patterns in source and fixtures                             |
| `openapi`             | OpenAPI specs are regenerated and staged                            |
| `api-clients`         | Frontend API clients match the OpenAPI contracts                    |
| `schema-doc`          | Schema documentation is regenerated and staged                      |
| `path-patterns`       | Files live in allowed directories                                   |
| `contracts`           | Hexagonal port/adapter contracts                                    |
| `query-linter`        | No interpolated raw SQL (`FromSqlRaw($...)`, `ExecuteSqlRaw($...)`) |
| `vulnerabilities`     | NuGet and npm dependency vulnerabilities                            |

`dotnet cadence check rtm` (not part of the 21) validates the traceability matrix without regenerating it.

---

## Centralized Test Infrastructure (`packages/testing`)

To maintain clean GxP test isolation and high performance, always use `packages/testing` when authoring tests:

### 1. Domain Entity Factories (`packages.testing.factories`)

Generate typed domain model instances with authentic GxP attributes:

- `SubjectFactory.create(...)`
- `ProtocolDefinitionFactory.create(...)`
- `ClinicalObservationFactory.create(...)`
- `QueryDiscrepancyFactory.create(...)`
- `ConsentRecordFactory.create(...)`
- `DocumentMetadataFactory.create(...)`
- `AuditLogFactory.create(...)`

### 2. In-Memory Repository Fakes (`packages.testing.fakes`)

Test domain use-cases without touching live databases:

```python
from packages.testing.fakes import InMemoryRepository

repo = InMemoryRepository()
```

### 3. Security & Gateway Auth Mocks (`packages.testing.security`)

Generate authentic HMAC signed gateway headers matching `GatewayAuthMiddleware`:

```python
from packages.testing.security import create_test_auth_headers

headers = create_test_auth_headers(
    user_id="crc.user@site101.org",
    roles=["site_crc"],
    tenant_id="tenant_default",
)
```

---

## CI Failure Runbook

CI runs in `.github/workflows/ci.yml`. Every check can be reproduced locally with the command in the last column.

| Failing job or step                                            | Usual cause                                                                       | Reproduce and fix locally                                                                       |
| -------------------------------------------------------------- | --------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| Style › Verify .NET C# code formatting                         | Code doesn't match `.editorconfig` or analyzer style                              | `dotnet format Cadence.sln`, then commit                                                        |
| Style › Run frontend style and lint                            | ESLint or Prettier findings in `apps/web`, `apps/subject-portal` or `packages/ui` | `pnpm format` and `pnpm -r lint`                                                                |
| Style › Run Cadence quality sentinels, or a single gate step   | One of the 21 gates failed; the log names it                                      | `dotnet cadence check <gate>`                                                                   |
| Style › Run architecture tests                                 | A layer dependency rule was broken                                                | `dotnet test tests/Cadence.ArchitectureTests.csproj`                                            |
| Style › Run Hadolint or ShellCheck                             | Dockerfile or shell script findings                                               | `hadolint <Dockerfile>`, `shellcheck <script>`                                                  |
| Style › Verify schema documentation freshness or OpenAPI drift | Generated docs or specs not regenerated                                           | `dotnet cadence fix`, then commit the regenerated files                                         |
| Style › Run ADR validation                                     | ADR missing, malformed or not indexed                                             | `dotnet cadence scaffold adr`, then `dotnet cadence check adrs`                                 |
| Security › dependency audit                                    | A vulnerable NuGet or npm package                                                 | `dotnet cadence check vulnerabilities`; upgrade in `Directory.Packages.props` or `package.json` |
| Frontend Unit Tests                                            | A Vitest failure                                                                  | `pnpm -r test`                                                                                  |
| .NET 10 C# xUnit Verification                                  | A build warning (warnings are errors) or an xUnit failure                         | `dotnet build Cadence.sln` and `dotnet test Cadence.sln`                                        |
| GxP Compliance › Assert No Documentation Git Diff              | RTM docs not regenerated after test changes                                       | `dotnet cadence gxp sync`, then commit `docs/SDLC/`                                             |
| Check for Merge Conflicts                                      | The PR conflicts with `main`                                                      | `git merge origin/main` and resolve                                                             |

Jobs that only run on pushes to `main`: accessibility E2E tests, the container build and Trivy scan, the production pipeline (GxP release evidence), the ruleset sync and the docs deploy.

---

## 1. First-Time Setup

```bash
# Clone and enter the repo
git clone https://github.com/fderuiter/cadence-clinical.git
cd cadence-clinical

# Brand-new machine, Codespace, or devcontainer? Run the full-stack wizard —
# it installs the .NET 10 SDK too, which `make setup` does not.
make wizard
# Equivalent: bash scripts/setup_wizard.sh   (Windows: scripts/setup_wizard.ps1)
# Non-interactive (CI/agents): scripts/setup_wizard.sh --dev [--minimal]

# Already have .NET installed and just need Python/Node deps refreshed?
make setup
# Equivalent: pnpm setup:dev
```

`make wizard` (`scripts/setup_wizard.sh`) detects the environment (local
workstation, GitHub Codespaces, a devcontainer, a Claude Code remote session,
or CI) and then, per stage:

1. Installs/validates the .NET 10 SDK, `dotnet-ef`/`dotnet-format` global
   tools, and trusts the local HTTPS dev cert
2. Installs Node/pnpm workspace dependencies (`pnpm install`)
3. Installs Python 3.14 dependencies via `uv sync`, Playwright Chromium, and
   all pre-commit Git hooks
4. Probes local datastores (PostgreSQL, Neo4j) and prints how to start them
5. Bootstraps `.env` from `.env.example`
6. Runs `cadence doctor` as a final health check

Pass `--minimal` to skip the Playwright browser download on short-lived
sessions, or `--dev`/`-y` to skip confirmation prompts. `make setup` remains
the narrower Python+Node-only path (steps 2–3 above) for machines that
already have the .NET SDK.

---

## 2. Branching & Workflow Strategy

All development uses short-lived branches off `main`.

### Branch Naming

| Type         | Pattern                                       | Example                                 |
| ------------ | --------------------------------------------- | --------------------------------------- |
| Feature      | `feature/<ticket>-<description>`              | `feature/cad-104-enrollment-validation` |
| Bug Fix      | `bugfix/<ticket>-<description>`               | `bugfix/cad-215-signature-overflow`     |
| Docs / Chore | `docs/<description>` or `chore/<description>` | `chore/ci-cd-fixes`                     |

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
from typing import Any, List, Optional  # ← alphabetical within typing

from sqlalchemy import select

from apps.execution.database.models import (
    ClinicalObservation,  # ← alphabetical
    FormSubmission,
    StudyAuthoredRule,  # ← NOT appended at the end
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
dotnet cadence gxp sync
# Equivalent: make sync-gxp or pnpm sync-gxp
```

This runs three steps:

1. `dotnet test Cadence.sln --configuration Release --logger "trx;LogFileName=report_dotnet.trx"`
2. Regenerates the RTM and IQ/OQ/PQ report from the TRX results.
3. `git add docs/SDLC/Requirements_Traceability_Matrix.md docs/SDLC/IQ_OQ_PQ_Execution_Report.md`

Then commit and push:

```bash
git commit -m "docs(rtm): sync GxP compliance docs with current test state"
git push
```

### Advanced options

```bash
# Validate only (no test run, no file changes)
dotnet cadence gxp sync --dry-run

# Full sync and auto-commit in one step
dotnet cadence gxp sync --commit
```

### Mapping requirement IDs in tests

For the RTM to track your test, declare the requirement ID using xUnit `[Trait("Requirement", "...")]` attributes, `[GxPRequirement("...")]`, or XML documentation comments:

```csharp
[Fact]
[Trait("Requirement", "PRD-SYS-042")]
[GxPRequirement("PRD-SYS-042")]
public async Task ValidateSubjectEnrollmentTransition()
{
    /// <summary>
    /// Validates subject state machine on enrollment.
    /// @req:PRD-SYS-042
    /// </summary>
    ...
}
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
uv run python scripts/create_adr.py --title "Your Title" --domain "core-platform" --req "PRD-SYS-001"
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
  explaining _why_, not just what.

### Gate 2 — Architecture Decision Records

See [Tier 2](#tier-2--architecture-decisions-adr) above.

### Gate 3 — Test Coverage

- Tests live in `tests/`.
- Backend: .NET 10 xUnit v3 (`dotnet test`). Minimum **80%** coverage enforced.
- Frontend/packages: `vitest`.
- Reference requirement IDs using xUnit attributes (`[Trait("Requirement", "...")]` or `[GxPRequirement("...")])`) or XML doc comments (`/// @req:...`) for RTM traceability.

---

## 7. Pre-commit Hooks Reference

`scripts/setup_wizard.sh` points git at `.githooks/` (`git config core.hooksPath .githooks`). The `pre-commit` hook (`pre-commit.ps1` on Windows) runs:

1. `dotnet format Cadence.sln --verify-no-changes`
2. `dotnet cadence check` (all 21 gates above)

There is no pre-push hook. The traceability matrix is checked in CI by the compliance job; run `dotnet cadence gxp sync` before pushing test changes.

```bash
# Run the same checks without committing
dotnet format Cadence.sln --verify-no-changes
dotnet cadence check

# Enable the hooks by hand if setup was skipped
git config core.hooksPath .githooks
```

Don't bypass the hook with `--no-verify`; fix the failing gate instead.

---

## 8. Human vs. AI Agent Boundary

This repository is co-authored by humans and autonomous AI agents. Agents must
comply with `AGENTS.md`. Human developers use this guide.

Key distinctions:

- **Human developers:** interactive shells, native `git`/`node`/`python`/`uv`, run checks in parallel using `pnpm check`.
- **AI agents:** strict directory target rules, no interactive prompts, `ruff`/`black` formatting only, must follow the 3-Tier Cascade Protocol on every PR.

---

## 9. Binary File Hygiene

- **Never commit `.docx` files.** They are gitignored. Use `python scripts/regenerate_templates.py` to rebuild protocol templates dynamically.
- **Never commit test execution reports (`*.trx`, `report.xml`).** They are gitignored. Generate them locally via `dotnet test Cadence.sln --logger "trx;LogFileName=report_dotnet.trx"` but do not stage them.
- **Never commit `.env` or secret files.** Use `detect-secrets` baseline workflow.

---

## 10. Additional Resources

| Document                                                                                         | Purpose                                                 |
| ------------------------------------------------------------------------------------------------ | ------------------------------------------------------- |
| [`AGENTS.md`](AGENTS.md)                                                                         | AI agent rules and directory target constraints         |
| [`docs/LOCAL_DEV_ENVIRONMENT.md`](docs/LOCAL_DEV_ENVIRONMENT.md)                                 | Port allocation map, service directory, Docker setup    |
| [`docs/SDLC/ISSUE_STRUCTURE_GUIDE.md`](docs/SDLC/ISSUE_STRUCTURE_GUIDE.md)                       | Issue templates, work streams, project board automation |
| [`docs/adr/index.md`](docs/adr/index.md)                                                         | Architecture Decision Record index                      |
| [`docs/SDLC/Requirements_Traceability_Matrix.md`](docs/SDLC/Requirements_Traceability_Matrix.md) | Live RTM — test ↔ requirement coverage                  |
| [`Makefile`](Makefile)                                                                           | All developer shortcuts — run `make help`               |

---

## Schema Validation Failures

To prevent namespace collisions in the gateway schema aggregation layer, each downstream microservice is assigned a unique name-based prefix. If the schema validator detects non-namespaced schemas, name overlaps, or prefix mismatches, it will fail the build.

### Microservice Schema Prefix Matrix

All data models/schemas defined in downstream microservices must start with their designated prefix rule to ensure clean merging at the Interoperability Gateway level.

| Microservice Name                 | Target Directory      | Prefix Rule             | Expected Prefix  |
| :-------------------------------- | :-------------------- | :---------------------- | :--------------- |
| Clinical Trial Management System  | `apps/ctms/`          | Capitalized name        | `Ctms_`          |
| Protocol Designer                 | `apps/designer/`      | Capitalized name        | `Designer_`      |
| Electronic Consent                | `apps/econsent/`      | Capitalized name        | `Econsent_`      |
| electronic Investigator Site File | `apps/eisf/`          | Capitalized name        | `Eisf_`          |
| electronic Trial Master File      | `apps/etmf/`          | Special Case (All-caps) | `ETMF_`          |
| EDC Execution Engine              | `apps/execution/`     | Capitalized name        | `Execution_`     |
| Interoperability Gateway          | `apps/interop/`       | Capitalized name        | `Interop_`       |
| Notification Service              | `apps/notifications/` | Capitalized name        | `Notifications_` |
| Organization Directory            | `apps/org/`           | Capitalized name        | `Org_`           |
| Quality & CAPA Engine             | `apps/quality/`       | Capitalized name        | `Quality_`       |
| Safety & Pharmacovigilance        | `apps/safety/`        | Capitalized name        | `Safety_`        |
| Support Ticketing                 | `apps/tickets/`       | Capitalized name        | `Tickets_`       |

_Note: The `apps/gateway` aggregator service, the shared packages under `packages/` (including `packages/compliance`), and frontend single-page web applications (`apps/web`, `apps/subject-portal`) are excluded from prefix enforcement._

### Troubleshooting Schema Validation Issues

#### 1. What are namespace collisions?

A namespace collision occurs when two downstream microservices define a model with the exact same name (e.g. `CommentCreate`). When the gateway merges these schemas into a single aggregated schema dictionary, one definition will overwrite the other, leading to API type definition discrepancies, broken client generation, or run-time failures.

#### 2. How to resolve overlapping models

If you see validation failures indicating a name collision or overlapping model (e.g., `ValidationError` or `ConflictStrategy` is defined in multiple services):

1. **Locate the model definition** in the downstream service (usually under `models.py` or a router file).
2. **Rename the model** to include the correct prefix. For example, if you are working in `apps/tickets/` and define a `CommentCreate` model, you must rename it to `Tickets_CommentCreate`.
3. If data is shared across microservices, use local Anti-Corruption Layer (ACL) DTOs or service-specific prefixed models to maintain isolated domain boundaries.

#### 3. How to run local validation

You can check schemas locally before pushing:

```bash
# Run schema validation offline with required environment variables
AUDIT_LOG_SECRET_KEY=dummy INBOUND_EMAIL_HMAC_SECRET=dummy uv run python scripts/validate_schemas.py
```

This script runs in under 5 seconds and will report any violations, including overlapping model definitions and prefix mismatches.
