---
name: diagnosing-bugs
description: "Diagnose, isolate, and debug failing tests, backend exceptions, database drift, or frontend errors in the Cadence platform. Use when investigating unexpected errors, regressions, or system anomalies."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Systematic Bug Diagnosis

A structured diagnosis loop for isolating root causes in multi-service clinical backends, Neo4j graphs, PostgreSQL databases, and Vue 3 frontend components using Antigravity debugging, task, and inspection tools.

## Antigravity Tool Execution Matrix

| Phase | Antigravity Tool | Invocation Pattern | Objective |
| :--- | :--- | :--- | :--- |
| **Error Localization** | `grep_search` | `Query="<exception_or_symbol>"`, `SearchPath="apps/"` | Locate error sites, callers, and exception stack traces |
| **System Diagnostics** | `run_command` | `CommandLine="uv run cadence doctor --auto-fix"`, `WaitMsBeforeAsync=5000` | Verify environment, schemas, and dependencies |
| **Isolated Repro** | `run_command` | `CommandLine="uv run pytest <test_file> -v -k '<test>'"` | Run targeted minimal reproduction test |
| **Task Monitoring** | `manage_task` | `Action="status"`, `TaskId="..."` | Inspect logs and stderr from background test tasks |
| **Watchdog Timer** | `schedule` | `DurationSeconds=60`, `TimerCondition=<task-id>` | Guard against hanging database locks or workers |
| **Root-Cause Fix** | `replace_file_content` | Target: source file, single contiguous fix | Apply surgical minimal fix to production code |
| **Diagnostic Report** | `write_to_file` | Target: `<appDataDir>/brain/<conversation-id>/bug_diagnosis.md` | Emit root-cause analysis and verification artifact |

---

## 1. Environment & Infrastructure Check

Run Cadence diagnostics to ensure local environments and SQLite/PostgreSQL schemas are intact:

```bash
uv run cadence doctor --auto-fix
```

---

## 2. Reproduce in Isolation

Always construct or run a minimal reproducing test case rather than diagnosing via exploratory mutations:

- Use `grep_search` and `find_by_name` to pinpoint relevant test files.
- Run the failing test in isolation:
  ```bash
  uv run pytest apps/<service>/tests/test_failing_case.py -v -k "test_name"
  ```
- If no test exists, author a failing regression test slice using `packages.testing.factories` and `packages.testing.fakes`.

---

## 3. Layer-Specific Diagnostic Runbooks

### A. Execution Backend & PostgreSQL (`apps/execution`)
- Check SQLAlchemy boolean filter syntax: ensure `.is_(True)` / `.is_(False)` is used instead of `== True`.
- Check database migrations and schema versioning under `apps/execution/database/`.
- Verify GxP audit trail triggers (`created_at`, `created_by`, `version_index`).

### B. Designer Backend & Neo4j (`apps/designer`)
- Check Neo4j async session lifecycle and Cypher parameter bindings.
- Validate USDM Pydantic schemas against CDISC specifications.
- Verify graph relationship integrity (predecessor links, immutable version branches).

### C. Gateway & Security (`apps/gateway`)
- Inspect HMAC signature generation and token header propagation (`packages.security.signing`).
- Check tenant isolation and persona context headers (`create_test_auth_headers`).

### D. Web Frontend (`apps/web`)
- Verify vanilla CSS tokens in `apps/web/src/style.css` (ensure no unsupported Tailwind utility classes).
- Check Pinia store state transitions and persona switcher context.

---

## 4. Fix & Verify

1. Apply the minimal root-cause fix using `replace_file_content`.
2. Verify the reproducing test turns green:
   ```bash
   uv run cadence test --fast -k "test_name"
   ```
3. Run full quality sentinels:
   ```bash
   uv run cadence check --parallel
   ```
4. Sync GxP compliance documentation:
   ```bash
   uv run python scripts/sync_gxp.py
   ```
5. Emit diagnosis report artifact to `<appDataDir>/brain/<conversation-id>/bug_diagnosis.md` via `write_to_file`.

