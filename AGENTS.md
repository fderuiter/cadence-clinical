# Agent Guidelines: Cadence Clinical Research Software Platform

## Product Mission

Cadence Clinical Research Software is a unified, standalone eClinical platform synthesising
upstream Clinical Metadata Management (MDR) with downstream Electronic Data
Capture (EDC) into an automated Digital Data Flow (DDF) platform.

---

## Technical Stack & Standards

| Concern            | Technology                                                                        |
| ------------------ | --------------------------------------------------------------------------------- |
| Language & Runtime | Python 3.14+                                                                      |
| Web Framework      | FastAPI                                                                           |
| Data Validation    | Pydantic v2 (strict typing required — no `Any` shortcuts)                         |
| Async HTTP         | HTTPX                                                                             |
| Code Style         | **Ruff** (lint + format); replaces Black. Run `uv run ruff format .`              |
| Designer DB        | Async Neo4j Python Driver (`apps/designer/`)                                      |
| Execution DB       | Async SQLAlchemy + SQLModel for PostgreSQL (`apps/execution/`)                    |
| Clinical Standards | CDISC USDM v3.0/v4.0, CDISC ODM XML/JSON                                          |
| GxP Audit Fields   | `created_at`, `created_by`, `reason_for_change`, `version_index` (21 CFR Part 11) |
| CLI & DX Tooling   | Cadence CLI (`uv run cadence`) in `packages/cli/`                                  |

---

## Directory Target Rules for Generated Code

| Code type                     | Target directory                                                      |
| ----------------------------- | --------------------------------------------------------------------- |
| Data models & CDISC schemas   | `apps/designer/` **and** `apps/execution/`                            |
| Study authoring / MDR logic   | `apps/designer/`                                                      |
| Data capture / eCRF logic     | `apps/execution/`                                                     |
| OIDC Auth & API routers       | `apps/gateway/`                                                       |
| Stack orchestration           | `docker/`                                                             |
| Automation & helper scripts   | `scripts/`                                                            |
| Unit & integration tests      | `apps/<name>/tests/`, `packages/<name>/tests/`, `scripts/tests/`      |
| Architecture Decision Records | `docs/adr/`                                                           |
| GxP compliance docs           | `docs/SDLC/` (never edit manually — always via `scripts/sync_gxp.py`) |

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

### PEP 695 Generic Classes Pattern (UP046)

In Python 3.14+, generic classes must use native type parameter syntax rather than inheriting from `typing.Generic`.

```python
# ✘ WRONG — triggers Ruff UP046
from typing import Generic, TypeVar

T = TypeVar("T")

class RepositoryPort(Generic[T], ABC):
    ...

# ✔ CORRECT — native PEP 695 type parameter syntax
class RepositoryPort[T](ABC):
    ...
```

---

### Safe Tarfile Extraction Pattern (Bandit B202 / CWE-22)

When extracting archives (e.g. database snapshot restorations), never call `tar.extractall()` without verifying member paths against directory traversal:

```python
# ✔ CORRECT — filters out members with path traversal or absolute paths
with tarfile.open(archive_path, "r:gz") as tar:
    safe_members = [
        m for m in tar.getmembers()
        if not m.name.startswith("/") and ".." not in m.name
    ]
    tar.extractall(path=repo_root, members=safe_members)  # nosec B202: verified safe members
```

---

### Primary Developer & Agent CLI Commands (`cadence`)

Agents should always prefer the unified `cadence` CLI (`packages/cli`) for local development, diagnostic, and validation tasks:

| Task | Command | Description |
| :--- | :--- | :--- |
| System Diagnostics | `uv run cadence doctor` | Validates Python, dependencies, databases, and ports (supports `--json`) |
| Quality Gates | `uv run cadence check` | Concurrently runs all 10 architecture sentinels and quality gates |
| Auto-Remediation | `uv run cadence fix` | Auto-remediates lints, formats code, aligns ADRs and schemas |
| Test Runner | `uv run cadence test` | Runs unit/integration/frontend test suites with filtering |
| Multi-Engine Seeding | `uv run cadence db seed --tier full` | Seeds multi-engine clinical test scenarios across Neo4j, PG, and SQLite |
| GxP Sync | `uv run cadence gxp sync` | Runs tests, regenerates RTM, and stages docs |
| Service Scaffolding | `uv run cadence scaffold adr "Title"` | Scaffolds new ADRs and auto-indexes under `docs/adr/index.md` |

---

### REST API-First Architecture & Microservice Decoupling

To ensure proper GxP boundaries and architectural decoupling across the Cadence Clinical Research Software Platform, agents must adhere strictly to the following standards for all inter-service communications:

1. **No Sibling Database Imports:** Sibling database imports (of models, schemas, or session helpers) across distinct microservice boundary paths (e.g., CTMS importing execution database models) are strictly prohibited.
2. **REST Endpoints for Cross-App Operations:** All inter-service communications, state changes, and validations must be routed through secure, performance-optimized, and well-typed REST endpoints exposed by the owning microservice (e.g., `/api/v1/execution/doa/*`).
3. **Gateway Token Authentication:** Every cross-service HTTP client request must be authenticated using internal gateway signatures and tokens generated via `generate_gateway_signature(...)` from `packages.security.signing` to pass `GatewayAuthMiddleware` checks.
4. **SLA Enforcements:** High-performance, low-latency asynchronous connection pooling via `httpx.AsyncClient` must be maintained to adhere to our strict **100ms internal SLA.**

---

### Frontend Architecture & Styling Standard (Vanilla CSS)

`apps/web` utilizes standard **Vanilla CSS** with a centralized design token system in `apps/web/src/style.css`.

- **No Tailwind CSS:** Do not use Tailwind CSS utility classes in `apps/web` Vue components (e.g. `flex-col`, `gap-4`, `grid-cols-12`, `bg-slate-50`). They are not processed by the Vite pipeline and will result in unstyled, broken interfaces.
- **Enterprise Design Tokens:** Use semantic CSS variables (`var(--primary)`, `var(--surface)`, `var(--border)`, `var(--radius-md)`) and standard scoped CSS.
- **Full-Width Authoring Workspaces:** High-density clinical workspaces (such as the Schedule of Activities Matrix and Arm-Aware Gantt Visualizer) must occupy full screen width. Auxiliary inspection panes (e.g. raw CDISC USDM JSON viewers) must be placed in collapsible drawers rather than rigid side columns.
- **Interactive Multi-Persona Support:** `apps/web` must provide a top-bar Role/Persona switcher (`super_admin`, `sponsor_designer`, `site_crc`, `cra_monitor`, `data_manager`, `auditor`) to allow seamless demonstration and testing of all clinical module workflows.

---

### Pytest-Xdist Test Harness Isolation & Database Gating

To maintain complete isolation across concurrent local test runs and prevent worker deadlock:

1. **No Import-Time Database Provisioning:** Database creation, schema migrations, and connection validations must never execute as module-level import side effects in `conftest.py`. All database provisioning must be gated inside `pytest_configure(config)`.
2. **Controller Zero-Provisioning Boundary:** The pytest-xdist controller process (`is_xdist_controller(config)`) coordinates worker dispatch and runs 0 test cases; it must never provision database schemas.
3. **Collision-Free Run & Worker Suffixes:** All database names must incorporate both the run identifier and worker identifier (`_{run_uid}_{worker_id}`) via `get_run_uid(config)` and `build_worker_suffix()`, adhering to PostgreSQL's 63-byte identifier limit (`NAMEDATALEN - 1`).
4. **Bounded Teardown & Async Timeouts:** All async database operations, schema resets, and subprocess invocations must be wrapped with bounded timeouts (5s connection, 15–30s execution) via `asyncio.wait_for` to prevent hanging workers upon disconnection.
5. **Background Subscriber & Worker Loop Testing:** When testing background worker/subscriber loops that incorporate retry logic (`while not self._stop_event.is_set():`), tests must signal or mock `_stop_event.wait` rather than patching `time.sleep` to guarantee clean loop termination.

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

| Step | Action                                                                                         |
| ---- | ---------------------------------------------------------------------------------------------- |
| 1    | `uv run pytest -n auto --junitxml=report.xml`                                                  |
| 2    | `uv run python scripts/generate_rtm.py`                                                        |
| 3    | `git add docs/SDLC/Requirements_Traceability_Matrix.md docs/SDLC/IQ_OQ_PQ_Execution_Report.md` |

Then commit the staged files:

```bash
git commit -m "docs(rtm): sync GxP compliance docs with current test state"
```

### Script flags

| Flag        | Behaviour                                                          |
| ----------- | ------------------------------------------------------------------ |
| _(none)_    | Full sync — runs tests, generates RTM, stages docs                 |
| `--dry-run` | Validate only — no test run, no file changes, exits 1 if stale     |
| `--commit`  | Full sync + auto-commit (do not use in interactive agent sessions) |

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

| CI Error                                         | Root Cause                                                                                       | Agent Action                                                                                                                                                                  |
| ------------------------------------------------ | ------------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `I001 Import block is un-sorted or un-formatted` | New symbol inserted at wrong position in import block                                            | `uv run ruff check . --fix` then verify the block is alphabetical                                                                                                             |
| `E712 Avoid equality comparisons to True/False`  | Used `col == True` in SQLAlchemy `.where()`                                                      | Replace with `col.is_(True)` / `col.is_(False)` — see [pattern above](#sqlalchemy-boolean-filter-pattern-e712)                                                                |
| `GxP compliance documentation is out of sync`    | RTM docs not regenerated after test changes                                                      | `uv run python scripts/sync_gxp.py` then commit `docs/SDLC/`                                                                                                                  |
| `Would reformat: <file>` (ruff format check)     | Code not formatted                                                                               | `uv run ruff format .`                                                                                                                                                        |
| `Coverage < 80%`                                 | New code paths not covered                                                                       | Add tests for the uncovered lines in the coverage report                                                                                                                      |
| `ADR validation failed`                          | Architectural change without a matching ADR                                                      | `python3 scripts/create_adr.py ...` — fill in rationale                                                                                                                       |
| `Bandit: high severity issue`                    | Security-sensitive pattern in code                                                               | Fix the flagged pattern; if intentional add `# nosec B<code>: <justification>`                                                                                                |
| `Secret detected`                                | Credential or token in source                                                                    | Remove the secret; update `.secrets.baseline` with `detect-secrets scan`                                                                                                      |
| `DEID compliance scan failure`                   | Sensitive PII/PHI (SSN, Email, Date) flagged in files                                            | Apply inline bypass (e.g., `# deid-ignore`) in mock/test files; remove actual sensitive data                                                                                  |
| `Code Duplication Detected Above Threshold`      | A consecutive block of 15 or more normalized lines of code is duplicated across different files. | Run `python3 scripts/detect_duplication.py` to identify, refactor to share logic, or add to the inline list of ignored sets inside `scripts/detect_duplication.py` if exempt. |

---

## Pull Request Verification Standards

Every PR must satisfy **three mandatory gates** before merging into `main`.

### Gate 1 — Comprehensive Documentation & Docstrings

- All public functions, classes, and API endpoints: Google-style docstrings.
- Docstrings must state _what_ the callable does, _args_, _returns_, and
  _raises_.
- Complex business logic (USDM-to-ODM transformers, state machines): inline
  comments explaining _why_ a transformation is structured as it is.
- If the PR introduces a new service boundary or changes an existing data flow,
  update the corresponding `docs/SDLC/` Markdown documents.

### Gate 2 — Architecture Decision Records

See [Tier 2](#tier-2--architecture--decision-adr) above.

### Gate 3 — Mandatory Test Coverage

- Tests live in decentralized directories (`apps/<name>/tests/`, `packages/<name>/tests/`, `scripts/tests/`).
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

The legacy `pnpm rtm` command only _validates_ the RTM (read-only). To
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

### 11. Code Duplication Prevention & Detection Guidelines

To prevent pipeline failures caused by unexpected code duplication, agents must understand the mechanics of the workspace's automated Code Duplication Scanner and how to manage logical similarity.

#### Duplication Thresholds & Targets

- **15-Line Sliding Window:** The detection engine uses a sliding-window threshold of **15 consecutive identical normalized lines**. Any contiguous block of logic meeting or exceeding this threshold across different files will trigger a pipeline blockage.
- **Target File Formats:** The scanner targets only the following extension formats: `.py`, `.js`, `.vue`, and `.css`.

#### Line Normalization Mechanics

The scanner is highly robust against surface-level styling differences. To prevent false positives, each line of code is normalized through the following steps before comparison:

1. **URL Masking:** Standard URLs are mapped to a generic placeholder to prevent differences in URL endpoints from masking duplicated structures.
2. **Comment Stripping:**
   - Single-line comments starting with `#` or `//` are completely stripped.
   - Multi-line block comments (`/* ... */` format in JS/CSS) are removed.
   - Inline comments are truncated from the line.
3. **Whitespace & Boilerplate Removal:** All leading/trailing whitespaces are trimmed. Empty lines and standard boilerplate instructions—such as `import`, `from`, `export`, destructuring assignments like `const {`, and standalone brackets/braces (`{`, `}`, `[`, `]`)—are ignored.
4. **String Format Standardization:** Single quotes `'` and backticks `` ` `` are replaced with standard double quotes `"` to treat functionally equivalent strings identically.

#### Running the Scanner Locally

Agents must run the duplication scanner locally to verify changes before pushing them:

- **Workspace-wide Scan:**
  ```bash
  python3 scripts/detect_duplication.py
  ```
- **Targeted Scan (Changed Files Mode):**
  Pass specific target files as arguments to run the scanner in a faster, incremental mode:
  ```bash
  python3 scripts/detect_duplication.py apps/execution/main.py apps/execution/routers/sdv.py
  ```
  To dynamically run against all staged and unstaged modified files from Git:
  ```bash
  python3 scripts/detect_duplication.py $(git diff --name-only | grep -E '\.(py|js|vue|css)$')
  ```

#### How to Whitelist Legitimate Duplications (Inline Exemptions)

In scenarios where refactoring to shared helpers is highly impractical or technically impossible due to strict microservice/module boundaries, you may exempt specific file pairs from the scanner.

1. Open the scanner script at `/app/scripts/detect_duplication.py`.
2. Locate the hardcoded inline list of `ignored` sets in the loop that evaluates duplicates (around line 223).
3. Append a new set containing the relative repo paths of the files to exempt. For example:

```python
ignored_pair = {
    "apps/my-new-app/main.py",
    "apps/another-app/main.py",
}
```

4. **Note:** External configuration files (such as YAML/JSON) must not be created or modified for whitelists, keeping the scanner self-contained and inline.

### 12. Monorepo Containerization & Multi-Architecture Base Images

- **Unified Base Image (`docker/Dockerfile`):** When building and launching containerized microservices in Docker Compose, do not use fragmented per-service Dockerfiles that only copy a subset of `packages/`. Because `pyproject.toml` defines workspace dependencies across packages (`[tool.uv.sources]`), `uv sync` requires the entire workspace structure to parse correctly. All backend services in `docker/docker-compose.yml` should reference `dockerfile: docker/Dockerfile` and rely on live volume mounts (`- ..:/app`).
- **Avoid Platform-Specific Digest Pinning for Local Dev:** Avoid hardcoding architecture-specific `@sha256:...` digests on base images in Dockerfiles (`python:3.14-slim-bookworm`, `ghcr.io/astral-sh/uv:python3.14-bookworm`) to ensure multi-arch builds succeed seamlessly on both Apple Silicon (ARM64) and x86_64 hosts.

### 13. Subprocess Environment & Fail-Fast Cryptographic Secret Defaults

- **Migration Subprocess `PYTHONPATH`:** When running pre-boot database migrations or initialization routines as subprocesses (e.g., in `scripts/start.py`), always pass `PYTHONPATH=os.getcwd()` in the subprocess environment dictionary to prevent `ModuleNotFoundError: No module named 'apps'` failures.
- **Development Secret Defaults:** The GxP compliance and Part 11 security modules (`packages/security`) fail fast if cryptographic keys are missing on import. Container environments must export default non-secret development values for `AUDIT_LOG_SECRET_KEY`, `GATEWAY_SECRET`, `SIGNING_SECRET`, and `INBOUND_EMAIL_HMAC_SECRET`.

### 14. Non-Interactive PNPM Execution in Containerized & Scripted Contexts

When executing `pnpm install` in Docker containers, background tasks, or scripts where TTY interaction is unavailable, set `CI=true` or configure `confirmModulesPurge=false` to prevent `ERR_PNPM_ABORTED_REMOVE_MODULES_DIR_NO_TTY` errors when syncing host-mounted `node_modules`.

### 15. Keycloak Local Development Persistence

When configuring Keycloak in `docker/docker-compose.yml` for local sandbox development, use `KC_DB=dev-file` rather than the default in-memory or raw file locks. This ensures realm imports and user credentials persist cleanly across container restarts without H2 database locking collisions.

### 16. pnpm Workspace Cache Hygiene

Always ensure package manager artifacts and temporary caches (`.pnpm-store/`, `.pnpm/`, `.pnpm-debug.log*`) remain gitignored to prevent workspace bloat and merge conflicts across branches.

### 17. Pytest/xdist Test Database Isolation & Recovery (`scripts/clean_test_dbs.py`)

When running automated test suites concurrently or under `pytest-xdist`:

- **Unique Database Suffixes:** Every pytest run generates a unique 8-character alphanumeric run ID (`PYTEST_XDIST_TESTRUNUID`), and each worker node receives a dedicated suffix (`_{run_uid}_{worker_id}`). All PostgreSQL databases for microservices are named using this pattern to guarantee complete collision-free concurrency across parallel runs on the same machine.
- **Controller Boundary:** The xdist controller coordinates test distribution across workers and executes 0 tests; it never creates or migrates database schemas.
- **Orphan Database Cleanup:** If a test process is forcefully killed (`SIGKILL`) or interrupted before session unconfiguration hooks finish, orphaned worker test databases can be inspected and purged using the CLI helper:
  ```bash
  # List all detected test databases
  uv run python scripts/clean_test_dbs.py --list

  # Drop all orphaned test databases
  uv run python scripts/clean_test_dbs.py --all

  # Drop only databases from a specific run UID
  uv run python scripts/clean_test_dbs.py --run-id <run_uid>
  ```

---

## GxP & HIPAA Compliance Scan Protocol

To accelerate release velocity, prevent accidental PII/PHI leakage, and resolve false positives in mock clinical data without manual intervention, follow the compliance scan protocol below. These guidelines ensure 100% alignment between local pre-commit verification and central CI security jobs.

### 1. Compliance Scanning & Security Tools

Local security sweeps and compliance checks run using the workspace runner (`uv run`).

#### De-identification (DEID) Scan Tool

Scans files for PII/PHI leakage (emails, SSNs, IP addresses, dates, and geographic information).

- **Package Path:** `packages/deid/`
- **CLI Entry Point:** `packages.deid.cli`
- **Local Run Command:**
  ```bash
  uv run python -m packages.deid.cli [paths...] [--profile PROFILE]
  ```

#### Security Audit Tool

Scans code and configurations for hardcoded secrets, unencrypted tokens, private keys, and insecure configurations to comply with GxP 21 CFR Part 11 security guidelines.

- **Script Path:** `scripts/audit_security.py`
- **Local Run Command:**
  ```bash
  uv run python scripts/audit_security.py
  ```

---

### 2. Scanning Profiles

The DEID compliance scan supports specific regional scanning standards using the `--profile` flag:

- **`HIPAA` (Default):** Enables all standard clinical detectors (including emails, dates, SSNs, geographic details, IP addresses, URLs, MRN/accounts, and age-related fields).
- **`GDPR`:** Enables all standard regional identifiers and compliance rules matching GDPR specifications.
- **`EU_CTR`:** Enables a subset focused specifically on clinical trials: `DATES`, `MEDICAL_RECORD_ACCOUNT`, and `AGE`.

Example running a GDPR scan on specific directories:

```bash
uv run python -m packages.deid.cli apps/designer/ --profile GDPR
```

---

### 3. Verification Patterns & Branch Filters (CI Alignment)

Local execution patterns must match the exact verification patterns and branch filters configured in central CI jobs (`.github/workflows/ci.yml`). In CI, the de-identification checks target specific file extensions: `.py`, `.js`, `.ts`, `.tsx`, `.jsx`, `.json`, `.md`, `.log`, and `.txt`.

To simulate CI checks locally before pushing, use the appropriate branch filtering command below:

#### Simulating Pull Request Verification (Local Branch vs. Main)

Runs the scanner only on changed files in your branch compared to the common ancestor branch (`origin/main`):

```bash
# Fetch origin/main to find the correct ancestor ref
git fetch origin main --depth=1 || true

# Run the DEID scanner against files changed on your PR branch
uv run python -m packages.deid.cli $(git diff --name-only origin/main...HEAD 2>/dev/null | grep -E '\.(py|js|ts|tsx|jsx|json|md|log|txt)$' || true)
```

#### Simulating Push/Commit-by-Commit Verification (Incremental Commit checks)

Runs the scanner on changes introduced in the last commit:

```bash
uv run python -m packages.deid.cli $(git diff --name-only HEAD~1..HEAD 2>/dev/null | grep -E '\.(py|js|ts|tsx|jsx|json|md|log|txt)$' || true)
```

---

### 4. Multi-Language Comment Bypass Pragmas

To resolve false positives in testing and mock clinical data, use inline developer comment bypass pragmas.

- **Strict Security Guardrail:** Inline bypass pragmas are strictly restricted to non-production code, mock data structures, and tests. They must **never** be applied to production codebase paths or production configuration files to ensure core security scans are never bypassed.

There are three case-sensitive inline comment pragmas supported globally by the scanners:

1. `deid-ignore`
2. `pragma: allowlist`
3. `deid: ignore`
   _Note: For the security audit script (`scripts/audit_security.py`), the `nosec` comment is used to bypass credential warnings (e.g., `# nosec B<code>: <justification>`)._

#### Language-Specific Syntax Examples

##### Python (`#` comment syntax)

```python
mock_ssn = "000-12-3456"  # deid-ignore
mock_email = "test-patient@example.com"  # pragma: allowlist
mock_birth_date = "1960-01-01"  # deid: ignore
```

##### Frontend / Scripting (`//` or `/* */` comments in JS, TS, JSX, TSX, CSS)

```typescript
const mockSsn = "000-12-3456"; // deid-ignore
const mockEmail = "test-patient@example.com"; // pragma: allowlist
const mockBirthDate = "1960-01-01"; // deid: ignore
```

##### Configuration & Markup (YAML & Markdown / HTML)

- **YAML:**
  ```yaml
  mock_ssn: "000-12-3456" # deid-ignore
  mock_email: "test-patient@example.com" # pragma: allowlist
  ```
- **Markdown / HTML Comments:**
  ```html
  <!-- deid-ignore -->
  ```
- **JSON files:** JSON files do not natively support inline comments. To handle JSON mock data false-positives, rely on the scanner's automated file-level exclusion hierarchy (such as placing the file in a `tests/` directory) or utilize built-in automated value-level heuristics.

---

### 5. Nested Exclusion Rules Hierarchy & Automated Heuristics

The compliance parser resolves scanning constraints through a multi-tiered filtering hierarchy to prevent blocking developers with harmless mock/test records.

#### Tier 1: Directory & File-Level Exclusions (Configured in Scanner)

The scanner automatically skips directories and files typically containing non-production data, tests, dependencies, or configuration:

- **Test Directories:** `tests/`, `test/`, and files starting/ending with test names (e.g., `test_*.py`, `*.test.js`).
- **Dependencies & Build Assets:** `node_modules/`, `.git/`, `.venv/`, `env/`, `build/`, `dist/`.
- **Caches & GitHub Configurations:** `.github/`, `.ruff_cache/`, `.pytest_cache/`.
- **Gitignored Files:** Automatically ignored using standard `.gitignore` rules (validated via `git check-ignore`).

#### Tier 2: Built-in Automated Value-Level Heuristics

Certain values are globally excluded or bypassed by the parser heuristics automatically:

- **IP Addresses & URLs:** Bypasses localhost/loopback addresses (`127.0.0.1`, `0.0.0.0`, `::1`) and test-infrastructure or registry domains (e.g., `github.com`, `pypi.org`, `npmjs.com`, `nih.gov`, `cadence-clinical.com`, `transmit-mock`).
- **Emails:** Any email address containing the substring `"cadence"` or `"clinical"`.
- **Dates:** Common testing default dates (e.g., `"2024-09-27"`, `"1960-01-01"`, `"2026-07-30"`, `"02-aug-2026"`, `"2026-08-04"`) or any lines containing standard config/version fields such as `"version"`, `"package"`, `"release"`, `"epoch"`, or `"default"`.
- **Geographic/ZIPs:** Standard test/dummy identifiers (e.g., `12345`, `65537`, `65536`, `86400`, `30000`).

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

| Command                                                                          | Purpose                                                                                               |
| -------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| `make ports`                                                                     | Check that all 13 microservice, database, and frontend ports are free and available                   |
| `make db-reset`                                                                  | Concurrently wipe, migrate, and seed all SQL/NoSQL/graph databases in under 15 seconds                |
| `make db-reset-offline`                                                          | Execute database resets offline, generating warnings instead of crashing if databases are unreachable |
| `uv run ruff check . --fix`                                                      | Auto-fix all fixable lint errors (I001, F-strings, etc.)                                              |
| `uv run ruff format .`                                                           | Auto-format all Python files                                                                          |
| `uv run python scripts/sync_gxp.py`                                              | Full GxP compliance sync (tests → RTM → stage)                                                        |
| `uv run python scripts/sync_gxp.py --dry-run`                                    | Validate GxP docs without modifying files                                                             |
| `python3 scripts/create_adr.py --title "..." --domain "..." --req "PRD-SYS-xxx"` | Scaffold ADR                                                                                          |
| `python3 scripts/validate_adrs.py --fix-index`                                   | Rebuild the ADR index                                                                                 |
| `python3 scripts/validate_markdown.py`                                           | Check all Markdown link integrity                                                                     |
| `uv run pytest -n auto --cov=apps --cov=packages`                                | Run full test suite with coverage                                                                     |
| `uv run bandit -c pyproject.toml -ll -ii -r apps packages`                       | Static security analysis                                                                              |
| `uv run python -m packages.deid.cli [paths...]`                                  | Local de-identification (DEID) scanner to check specific files/directories for PII/PHI leakage        |
| `uv run python scripts/audit_security.py`                                        | Execute standard repository-wide security and credentials sweep                                       |
| `python3 scripts/detect_duplication.py`                                          | Run workspace-wide code duplication scanner                                                           |
| `python3 scripts/detect_duplication.py <files>`                                  | Run duplication scanner in target changed-files mode                                                  |

---

## Summary Checklist for Pull Requests

Before submitting a PR, verify all items:

- [ ] All Python code is fully typed with strict type hints — no bare `Any`.
- [ ] All public functions and classes have Google-style docstrings.
- [ ] Imports are alphabetically ordered within each group (run `uv run ruff check . --fix`).
- [ ] SQLAlchemy boolean filters use `.is_(True)` / `.is_(False)` — not `== True` / `== False`.
- [ ] Unit and/or integration tests added under `tests/` with requirement IDs in docstrings.
- [ ] An ADR added to `docs/adr/` if the PR introduces a significant architectural change.
- [ ] All local checks pass: `uv run ruff check .` and `uv run ruff format --check .`
- [ ] GxP compliance docs are up to date: `uv run python scripts/sync_gxp.py` run and committed.
- [ ] `docs/SDLC/` Markdown docs updated if a service boundary or data flow changed.
- [ ] Local compliance and security sweeps run and pass, with any false positives bypassed using standard comment pragmas (restricted to mock/test files).
- [ ] No code duplication failures (run `python3 scripts/detect_duplication.py` locally to verify, or whitelist if exempt).
- [ ] No binary `.docx` files, `report.xml`, or secrets are staged.
