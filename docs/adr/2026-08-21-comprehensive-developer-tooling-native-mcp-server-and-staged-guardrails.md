# ADR-2189: Comprehensive Developer Tooling Native MCP Server and Staged Guardrails

* **Status:** Accepted
* **Date:** 2026-08-21
* **Authors:** @fderuiter
* **Deciders:** @fderuiter
* **Requirement ID:** PRD-SYS-049

---

## 1. Context & Problem Statement

Cadence Clinical is a high-assurance eClinical research software platform synthesizing Clinical Metadata Management (MDR) with Electronic Data Capture (EDC) under strict GxP (21 CFR Part 11, ICH GCP E6) compliance rules. As the platform's development velocity expands across both human engineers and autonomous AI coding agents, our developer tooling must deliver deterministic feedback, zero-translation-loss agent interfaces, sub-second local guardrails, and rigorous architectural sentinel enforcement.

Prior developer workflows exhibited several friction points:
1. **Unstructured Agent Interfaces:** AI coding agents had to invoke shell commands and scrape unstructured Rich terminal output, introducing shell-escaping vulnerabilities and wasting context tokens.
2. **Missing Local Pre-Commit Guardrails:** Quality gates (import ordering `I001`, SQLAlchemy boolean comparisons `E712`, secret baselines, and OpenAPI drift) were caught only late in CI or via manual runs of `cadence check`.
3. **Inconsistent Terminal Output:** Command output lacked uniform layout hierarchy, clear status card indicators, and actionable Next-Action CTAs as prescribed by modern CLI craft guidelines.
4. **Manual Environment Recovery:** Diagnosing and recovering from missing SQLite schemas, unindexed ADRs, or stale test databases required ad-hoc manual scripts.

## 2. Decision Drivers & Constraints

* **Agent DX & Context Window Discipline (PRD-SYS-049):** The CLI must achieve Level 3 ("Agent-First") across all 7 axes of the Agent DX Scale, supporting native Model Context Protocol (MCP) stdio execution with zoom mechanics.
* **Inner-Loop Developer Velocity:** Local pre-commit git hooks must execute in under 2 seconds to preserve engineer flow state without degrading commit frequency.
* **GxP Compliance & Traceability:** Automated tools must guarantee that Requirements Traceability Matrix (RTM), IQ/OQ/PQ docs, ADR indices, and OpenAPI schema exports remain in lockstep.
* **Terminal Craft & Next-Action CTAs:** Human-facing CLI surfaces must follow authored terminal document standards with clear visual hierarchy, aligned columns, and explicit next actions.

## 3. Options Considered

### Option 1: Fragmented Shell Scripts & Ad-Hoc CLI Print Statements
- **Overview:** Maintain standalone Python scripts under `scripts/` with disparate CLI arguments and unstructured Rich console output.
- **Pros:** Low initial implementation effort.
- **Cons:** High cognitive overhead, poor agent interoperability, no standardized pre-commit automation, context-heavy token waste for AI agents.

### Option 2: Heavy Monolithic Pre-Commit & Shell Wrappers
- **Overview:** Run all 10 architecture gates and full test suites on every git commit.
- **Pros:** Comprehensive validation.
- **Cons:** 30+ second commit latency, frequent commit aborts, developer friction leading to `--no-verify` bypasses.

### Option 3: Unified Multi-Pillar Developer Tooling Architecture (Selected)
- **Overview:** Orchestrate four unified developer tooling pillars:
  1. **Native Stdio MCP Server (`cadence mcp`):** Directly expose typed repository tools to AI coding agents via stdio JSON-RPC.
  2. **Two-Tier Staged Pre-Commit Hook:** Sub-500ms staged file checks + sub-1.5s repo invariant assertions.
  3. **Centralized `TerminalDocument` Builder:** Authored terminal documents with narrative headers, status metric cards, aligned columns, and contextual CTAs, paired with strict NDJSON/JSON output in non-TTY contexts.
  4. **Deterministic Self-Healing Engine:** Auto-repair SQLite scratch schemas, install git hooks, format code, sort imports, index ADRs, and export schemas via `cadence doctor --auto-fix` and `cadence fix --all`.
- **Pros:**
  - Zero-translation-loss agent execution without shell escaping.
  - Sub-2s commit loop guaranteeing clean CI runs.
  - Consistent terminal aesthetics and clear developer next actions.
  - Automated self-healing for common developer environment drift.
- **Cons:** Requires initial scaffolding and maintenance of the MCP tool schemas and terminal document formatting abstractions.

## 4. Decision Outcome

**Chosen Option:** Option 3 (Unified Multi-Pillar Developer Tooling Architecture).

### Pillar 1: Native Stdio MCP Server (`packages/cli/mcp/`)
Expose a native stdio Model Context Protocol (MCP) server via `uv run cadence mcp` providing 6 workflow-centric tools equipped with parameter zoom controls (`summary`, `fields`, `dry_run`):
1. `doctor_diagnose`: Validates Python 3.14+ runtime, port availability, database connectivity, and auto-heals SQLite schemas.
2. `run_sentinels`: Concurrently executes repository quality gates (ruff, bandit, secrets, ADRs, imports, contracts).
3. `run_fast_tests`: Runs unit and contract tests in sub-second time slices.
4. `seed_clinical_scenario`: Seeds multi-engine clinical test scenarios across Neo4j and PostgreSQL.
5. `sync_gxp_compliance`: Regenerates the RTM and IQ/OQ/PQ docs in lockstep with test executions.
6. `introspect_service_contracts`: Returns machine-readable OpenAPI and service contracts for cross-microservice endpoints.

### Pillar 2: Two-Tier Staged Pre-Commit Hook (`scripts/pre_commit.py`)
- **Stage 1 (Staged Files Only, <500ms):**
  - `ruff check --fix` and `ruff format` on staged `.py` files.
  - `clean_secrets_baseline.py` on staged files.
  - `validate_path_patterns.py` on staged paths.
- **Stage 2 (Repository Scope, <1.5s):**
  - `validate_imports.py` (enforcing zero sibling DB imports across microservice boundaries).
  - Fast contract test suite (`cadence test --fast`).
- **Auto-Installation:** Embedded into `.git/hooks/pre-commit` automatically during `cadence doctor --auto-fix` or `cadence dev init-hooks`.

### Pillar 3: Centralized `TerminalDocument` Builder (`packages/cli/formatting.py`)
Implement a declarative `TerminalDocument` rendering engine:
- **Narrative Header:** Command title and subtitle description.
- **Status Metrics Card:** Prominently formatted badge row (e.g. `[10 Passed] [0 Failed] [1.4s]`).
- **Structured Sections:** Aligned columns with dimmed metadata and clickable `file://` links.
- **Hub Next-Action CTAs:** Contextual guidance suggesting exact remediation commands (e.g. `uv run cadence fix --all`).
- **Agent Output Discipline:** Automatic emission of raw NDJSON / structured JSON when piped (`not sys.stdout.isatty()`) or when `--json` is supplied.

### Pillar 4: Deterministic Self-Healing Engine
- **Non-Destructive Auto-Fixes:** Safe auto-initialization of missing SQLite storage files, auto-indexing of ADRs under `docs/adr/index.md`, auto-reformatting of code and imports, and re-exporting OpenAPI schemas.
- **Guided Recovery CTAs:** Clear diagnostic instructions and copy-paste commands for external services (Neo4j Docker startup, Postgres migrations, port conflict resolution).

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - AI coding agents can interact natively with repo operations via typed MCP tools with zero token waste.
  - Developers receive instantaneous feedback at commit time, eliminating CI roundtrips for lint, format, import, or secret errors.
  - Standardized CLI output eliminates visual clutter and guides developers directly to resolution commands.
  - GxP compliance artifacts remain synchronized without manual human intervention.
- **Negative Impact / Trade-offs:**
  - Developers must use Python 3.14+ / `uv` for local git hook execution.
  - Hook installation must be maintained in `.git/hooks/` across fresh clones.
- **Mitigation Strategy:**
  - `cadence doctor --auto-fix` automatically checks and installs the pre-commit hook if missing, making clone setup a single-command operation.

## 6. Implementation & Verification

- **Affected Packages & Directories:**
  - `packages/cli/main.py` (registers `mcp` subcommand).
  - `packages/cli/mcp/server.py` (implements stdio JSON-RPC MCP server & tool definitions).
  - `packages/cli/formatting.py` (implements `TerminalDocument` builder & NDJSON/JSON formatters).
  - `packages/cli/commands/doctor.py` (enhances diagnostic checks & hook auto-installation).
  - `packages/cli/commands/check.py` & `fix.py` (integrated with `TerminalDocument` output).
  - `scripts/pre_commit.py` (implements two-tier staged hook logic).
- **Verification Plan:**
  - Unit and contract tests under `packages/cli/tests/test_mcp_server.py`, `packages/cli/tests/test_formatting.py`, and `scripts/tests/test_pre_commit.py`.
  - Full sentinel and GxP synchronization validation via `uv run cadence check --parallel` and `uv run cadence gxp sync`.
