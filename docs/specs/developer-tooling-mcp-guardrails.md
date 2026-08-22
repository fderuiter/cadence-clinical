# Specification: Unified Developer Tooling, Native MCP Server & Staged Guardrails

- **Status:** Ready for Implementation
- **Requirement ID:** PRD-SYS-049
- **ADR Reference:** [ADR-2189](../adr/2026-08-21-comprehensive-developer-tooling-native-mcp-server-and-staged-guardrails.md)
- **Triage Label:** `ready-for-agent`

---

## Problem Statement

In the Cadence Clinical high-assurance eClinical platform, engineering involves both human software engineers and autonomous AI coding agents operating across microservices, CDISC data pipelines, and strict GxP (21 CFR Part 11) compliance boundaries.

However, developer tooling currently exhibits critical friction points:
1. **Unstructured Agent Interoperability:** Autonomous agents must invoke shell commands and parse unstructured Rich console output, leading to shell-escaping bugs, token waste, and heuristic parsing errors.
2. **Missing Local Pre-Commit Guardrails:** Architectural violations (import ordering `I001`, SQLAlchemy boolean comparisons `E712`, secret exposures, and OpenAPI schema drift) are often discovered late in CI, causing avoidable PR roundtrips and developer friction.
3. **Inconsistent Terminal Output:** Command line interfaces lack visual document hierarchy, status card indicators, and actionable Next-Action CTAs, confusing developers during multi-service debugging.
4. **Manual Environment Recovery:** Recovering from missing SQLite schemas, unindexed ADRs, or stale test databases requires disparate manual scripts.

---

## Solution

Build a unified, four-pillar developer tooling architecture:
1. **Native Stdio MCP Server (`cadence mcp`):** Provide a Model Context Protocol endpoint directly within the `cadence` CLI exposing 6 workflow-centric tools with parameter-level zoom control (`summary`, `fields`, `dry_run`).
2. **Two-Tier Staged Pre-Commit Hook:** Implement a fast, Python/UV-native git pre-commit pipeline executing sub-500ms staged file checks and sub-1.5s repo assertions, auto-installed via `cadence doctor --auto-fix`.
3. **Centralized `TerminalDocument` Builder:** Standardize all human CLI output into authored terminal documents with narrative headings, status metric badges, column-aligned layouts, and contextual Next-Action CTAs, paired with strict NDJSON/JSON output in non-TTY contexts.
4. **Deterministic Tooling Self-Healing:** Provide safe, non-destructive automated remediation of environment drift, SQLite scratch databases, import ordering, code formatting, ADR indexing, and OpenAPI schema exports.

---

## User Stories

1. As an autonomous coding agent, I want to invoke `doctor_diagnose` via MCP stdio, so that I can verify Python 3.14 runtime, port bindings, and database readiness with structured JSON output and zero shell parsing.
2. As an autonomous coding agent, I want to run `run_sentinels` via MCP with a `summary=True` parameter, so that I can inspect quality gate results without consuming excessive context tokens.
3. As an autonomous coding agent, I want to execute `run_fast_tests` via MCP, so that I can run the TDD red-green-refactor feedback loop in under 1 second.
4. As an autonomous coding agent, I want to call `seed_clinical_scenario` via MCP with scenario name parameters, so that I can provision reproducible graph and relational test states without running shell scripts.
5. As an autonomous coding agent, I want to invoke `sync_gxp_compliance` with `dry_run=True`, so that I can test whether my changes introduced RTM or IQ/OQ/PQ doc drift before committing.
6. As an autonomous coding agent, I want to invoke `introspect_service_contracts`, so that I can discover machine-readable OpenAPI specifications and cross-app routes at runtime.
7. As a human software engineer, I want the git pre-commit hook to run staged formatting and linting in sub-500ms, so that committing code never breaks my flow state.
8. As a human software engineer, I want the pre-commit hook to automatically catch and fix Ruff formatting and import sorting violations on staged files, so that I never fail CI on `I001` or style rules.
9. As a human software engineer, I want the pre-commit hook to reject bare SQLAlchemy boolean equality checks (`E712`) before commits are created, so that GxP-critical SQL queries remain safe.
10. As a human software engineer, I want the pre-commit hook to run a secret scan across staged hunks, so that API credentials or JWT private keys are never committed.
11. As a human software engineer, I want `uv run cadence doctor --auto-fix` to automatically install the git pre-commit hook into `.git/hooks/`, so that onboarding new clones requires zero manual hook setup.
12. As a human software engineer, I want `cadence doctor` to output an authored terminal document with status cards and aligned columns, so that I can immediately see which ports, databases, or tools need attention.
13. As a human software engineer, I want `cadence check` to display a Next-Action CTA pointing to `uv run cadence fix --all` whenever format or lint errors occur, so that I know the exact remediation command immediately.
14. As a human software engineer, I want `cadence fix --all` to format code, sort imports, re-index ADRs, and export OpenAPI schemas in one atomic command, so that all repository metadata stays synchronized.
15. As a CI/CD pipeline, I want `cadence check --json` to emit clean, parseable JSON error arrays, so that automated quality gates integrate seamlessly with GitHub Actions and status checks.
16. As a compliance auditor, I want all developer tool actions to enforce and preserve 21 CFR Part 11 audit trails and non-destructive historical retention across database operations.

---

## Implementation Decisions

### 1. Native Stdio MCP Server Module
- **Module Boundary:** Built inside the CLI package under an isolated MCP submodule.
- **Protocol:** Standard JSON-RPC 2.0 over `sys.stdin` / `sys.stdout`.
- **Tool Catalog (6 Workflow-Centric Tools):**
  - `doctor_diagnose(auto_heal: bool = False, summary: bool = True)`: Runs system diagnostics, reports port allocations, Python 3.14+ compatibility, and database initialization.
  - `run_sentinels(gate: str | None = None, parallel: bool = True, summary: bool = True)`: Executes architecture sentinels and quality gates.
  - `run_fast_tests(subsystem: str | None = None, summary: bool = True)`: Executes sub-second fast unit tests.
  - `seed_clinical_scenario(tier: str = "standard", scenario: str = "default", dry_run: bool = False)`: Seeds multi-engine test data.
  - `sync_gxp_compliance(dry_run: bool = False)`: Verifies and syncs RTM and IQ/OQ/PQ docs.
  - `introspect_service_contracts(service: str | None = None)`: Provides OpenAPI and service boundary schemas.
- **Response Zoom Envelope:** Every tool response encapsulates a standardized envelope:
  ```json
  {
    "status": "success | error",
    "summary": "Concise 1-line narrative summary",
    "metrics": { "duration_ms": 120, "passed": 10, "failed": 0 },
    "details": {},
    "cta": "Suggested next command"
  }
  ```

### 2. Two-Tier Staged Pre-Commit Hook Module
- **Stage 1 (Staged Hunks Only, Target <500ms):**
  - Extract staged `.py` and documentation paths using `git diff --cached --name-only --diff-filter=ACM`.
  - Execute `ruff check --fix` and `ruff format` exclusively against staged Python files.
  - Execute `clean_secrets_baseline.py` and `validate_path_patterns.py` on staged files.
  - Automatically re-stage fixed files if safe formatting mutations occurred.
- **Stage 2 (Repository Scope Invariant Assertions, Target <1.5s):**
  - Execute `validate_imports.py` to enforce zero sibling DB imports across microservice boundaries.
  - Execute fast unit test suite (`pytest -m "not integration"`).
- **Auto-Installation:** Embedded into `.git/hooks/pre-commit` as a lightweight shell wrapper delegating to `uv run python scripts/pre_commit.py`.

### 3. Centralized `TerminalDocument` Builder Module
- **Encapsulation:** Implemented in `packages/cli/formatting.py` as a fluent document renderer.
- **Visual Sections:**
  - **Narrative Header:** Bold colored title + muted subtitle.
  - **Status Card Row:** Boxed metrics (e.g. `10 Passed`, `0 Failed`, `1.2s Duration`).
  - **Column-Aligned Content:** Tabular or key-value aligned data with styled labels and dimmed metadata.
  - **Clickable File Links:** Proper `file://` link generation.
  - **Contextual Next-Action CTA:** Distinctive footer banner providing exact follow-up commands.
- **TTY Detection:** When `not sys.stdout.isatty()` or `--json` is supplied, bypass Rich console layout and stream NDJSON/JSON.

### 4. Deterministic Self-Healing Engine
- **Safe Auto-Fixes:**
  - Auto-create and initialize missing SQLite schema files.
  - Install `.git/hooks/pre-commit` if missing or outdated.
  - Auto-sort imports and format Python source code.
  - Auto-index ADR files under `docs/adr/index.md`.
  - Auto-export FastAPI OpenAPI JSON schemas to `docs/openapi/`.
- **Guided CTAs:** Output explicit shell instructions when external services (Neo4j, PostgreSQL Docker containers) or port conflicts require developer intervention.

---

## Testing Decisions

### What Makes a Good Test
- Tests must assert observable public behavior (CLI exit codes, stdout/stderr document structure, structured JSON payloads, git hook exit codes) rather than mocking internal helper variables.
- Zero network or external Docker dependencies for unit test execution.

### Tested Surfaces
1. **CLI Commands (`packages/cli/tests/test_cadence_cli.py`):**
   - Assert `doctor`, `check`, `fix`, `test`, `mcp` subcommands with `typer.testing.CliRunner`.
   - Validate both human-readable document output and `--json` structured parity.
2. **MCP Server Protocol (`packages/cli/tests/test_mcp_server.py`):**
   - Send JSON-RPC `initialize`, `tools/list`, and `tools/call` payloads to the stdio server handler.
   - Verify tool schema validity, parameter validation, and zoom envelope output.
3. **Pre-Commit Runner (`scripts/tests/test_pre_commit.py`):**
   - Test staged file detection, formatting remediation, and repository invariant gate passing.
4. **Terminal Formatting (`packages/cli/tests/test_formatting.py`):**
   - Verify `TerminalDocument` layout rendering, column alignment, and non-TTY JSON fallback.

### Prior Art
- CLI unit tests in `packages/cli/tests/test_cadence_cli.py`.
- Quality gate validation in `scripts/validate_path_patterns.py` and `scripts/validate_imports.py`.

---

## Out of Scope

- Rewriting underlying microservice domain logic or REST API endpoints.
- Replacing Pytest with another testing engine.
- Adding third-party Node/npm global binaries into the Python root workflow.

---

## Further Notes

- All code and test files must adhere to Python 3.14+ runtime invariants and strict typing.
- All new tests must include `@req:PRD-SYS-049` docstrings for GxP traceability.
