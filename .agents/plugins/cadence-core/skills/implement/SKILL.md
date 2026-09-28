---
name: implement
description: "Implement a feature, bug fix, or refactor with strict adherence to Cadence coding standards, GxP traceability, and automated quality gates. Use when implementing approved plans, specs, or tickets."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Implementation Workflow

Executes code implementation tasks with strict adherence to repository standards, 21 CFR Part 11 requirements, and Antigravity artifact reporting using Antigravity code, task, and verification tools.

## Antigravity Tool Execution Matrix

| Phase | Antigravity Tool | Invocation Pattern | Objective |
| :--- | :--- | :--- | :--- |
| **Context Review** | `find_by_name` / `view_file` | Read relevant spec, models, and tests via `AbsolutePath` | Map dependencies and domain invariants |
| **Surgical Editing** | `replace_file_content` | Target: production file, single contiguous chunk | Implement changes with strict typing and E712/I001 compliance |
| **File Creation** | `write_to_file` | Target: new module/test, with complete contents | Scaffold new services, endpoints, or test suites |
| **Quality Remediation**| `run_command` | `CommandLine="uv run cadence fix --all"`, `WaitMsBeforeAsync=5000` | Auto-format and sort imports |
| **Fast Verification** | `run_command` | `CommandLine="uv run cadence test --fast"`, `WaitMsBeforeAsync=10000` | Run unit tests across 10 workers |
| **Task Monitoring** | `manage_task` | `Action="status"`, `TaskId="..."` | Track long-running test execution |
| **Sentinels Check** | `run_command` | `CommandLine="uv run cadence check --parallel"` | Concurrently run all 10 quality gates |
| **Compliance Sync** | `run_command` | `CommandLine="uv run python scripts/sync_gxp.py"` | Sync RTM and IQ/OQ/PQ execution reports |
| **Walkthrough Report** | `write_to_file` | Target: `<appDataDir>/brain/<conversation-id>/walkthrough.md` | Emit structured completion artifact with file links |

---

## 1. Pre-Implementation Review
- Read the relevant spec, ticket, or plan.
- Check [`CONTEXT.md`](../../../../CONTEXT.md) for canonical domain terminology.

- Identify the target directories:
  - `apps/designer/`: Study authoring / MDR / Neo4j logic.
  - `apps/execution/`: Data capture / eCRF / PostgreSQL logic.
  - `apps/gateway/`: OIDC Auth & API routers.
  - `apps/web/`: Frontend Vue 3 views (Vanilla CSS design tokens, no Tailwind).
  - `packages/`: Reusable cross-app packages (`packages.testing`, `packages.security`).

---

## 2. Implementation Execution

### MANDATORY TOOL INVOCATION (`replace_file_content` / `write_to_file`)
- Make surgical, cohesive edits using `replace_file_content` (never full-file overwrite for existing files).
- Use `write_to_file` only when creating brand new modules or session artifacts.
- Follow critical patterns:
  - **E712 Boolean Filters**: Always use `.is_(True)` / `.is_(False)` in SQLAlchemy queries.
  - **I001 Import Ordering**: Standard library → third-party → first-party, strictly sorted alphabetically.
  - **PEP 695 Generics**: Use native syntax (`class Repository[T]: ...`).
  - **Frontend Tokens**: Use semantic CSS variables (`var(--primary)`, `var(--surface)`).
  - **@req: Docstrings**: Add requirement tags (`@req:PRD-SYS-xxx`) to all new/modified test functions.
- If scope trade-offs or breaking changes arise during implementation, pause and invoke `ask_question`.

---

## 3. Mandatory Quality Gate Sequence (MANDATORY TOOL: `run_command`)

Before concluding any implementation turn, execute the four quality gates:

```bash
# 1. Auto-remediate formatting and import ordering
uv run cadence fix --all

# 2. Run fast unit and integration tests
uv run cadence test --fast

# 3. Run all 10 architecture and compliance sentinels
uv run cadence check --parallel

# 4. Sync GxP Traceability Matrix & Execution Report
uv run python scripts/sync_gxp.py
```

---

## 4. Produce Antigravity Walkthrough Artifact (MANDATORY TOOL: `write_to_file`)

Record changes in `<appDataDir>/brain/<conversation-id>/walkthrough.md` via `write_to_file`:
- Multi-component summary of files created/modified with clickable `[basename](file:///path/to/file)` links.
- Verification results table showing test outcomes and sentinel checks.
- Key GxP requirement IDs satisfied.


