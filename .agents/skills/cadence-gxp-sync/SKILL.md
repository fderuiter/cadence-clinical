---
name: cadence-gxp-sync
description: "Validate 21 CFR Part 11 requirement traceability on a feature branch: run the tests, check requirement tags and results, and preview the Requirements Traceability Matrix (RTM) and IQ/OQ/PQ execution report without committing them. Use when adding/modifying tests, updating @req docstrings, or resolving CI compliance failures."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence GxP Compliance Synchronization

Validates requirement traceability on a feature branch using Antigravity asynchronous tasks and validation tools. `pnpm cadence gxp sync` runs the tests, writes a preview of the RTM and IQ/OQ/PQ report to `TestResults/gxp/preview/` (gitignored), and fails on unknown requirement IDs, failing tagged tests and skipped database tests. It writes and stages nothing under `docs/SDLC` (ADR-2532).

The committed `docs/SDLC/Requirements_Traceability_Matrix.md`, `docs/SDLC/IQ_OQ_PQ_Execution_Report.md` and `docs/SDLC/runs/` are a snapshot of the integrated candidate. They are not feature-branch merge currency: never commit them on a feature branch and never resolve a conflict in them by hand.

## Antigravity Tool Execution Matrix

| Phase                | Antigravity Tool | Invocation Pattern                                                               | Objective                                               |
| :------------------- | :--------------- | :------------------------------------------------------------------------------- | :------------------------------------------------------ |
| **Execution**        | `run_command`    | `CommandLine="pnpm cadence gxp sync"`, `WaitMsBeforeAsync=10000`                 | Run the tests and validate requirement tags and results |
| **Task Monitoring**  | `manage_task`    | `Action="status"`, `TaskId="..."`                                                | Monitor long-running test execution asynchronously      |
| **Watchdog Timer**   | `schedule`       | `DurationSeconds=180`, `TimerCondition=<task-id>`                                | Bound execution time to prevent hanging workers         |
| **Verification**     | `view_file`      | `AbsolutePath=".../TestResults/gxp/preview/Requirements_Traceability_Matrix.md"` | Review requirement coverage and outcomes in the preview |
| **Summary Artifact** | `write_to_file`  | Target: `<appDataDir>/brain/<conversation-id>/gxp_sync_summary.md`               | Emit audit-ready execution summary artifact             |

## When to Run

Run this workflow whenever:

- Test methods are added, renamed, or deleted in xUnit v3 test suites.
- `@req:PRD-SYS-XXX` tags in test doc comments or `[GxPRequirement("PRD-SYS-xxx")]` attributes in C# tests are added or modified.
- Test pass/fail outcomes change.
- CI's `Generate Compliance Reports` or `Check GxP Snapshot Policy` step fails.

## Execution Protocol

1. **Launch Sync**: Run `pnpm cadence gxp sync` via `run_command`, with `CADENCE_TEST_POSTGRES` or `CADENCE_TEST_DOCKER=1` set so database tests run instead of skipping.
   - Executes `dotnet test Cadence.sln` across all test assemblies.
   - Tooling scans `.cs` test files and parses the TRX reports into a preview RTM and IQ/OQ/PQ report.
   - For a quick check without tests, run `pnpm cadence gxp sync --dry-run` (same as `pnpm cadence gxp validate`).
2. **Handle Background Task**: If sent to background, inspect progress using `manage_task(Action="status")`. Set a `schedule` watchdog timer if needed.
3. **Review the Preview**:
   - Inspect `TestResults/gxp/preview/Requirements_Traceability_Matrix.md`.
   - Inspect `TestResults/gxp/preview/IQ_OQ_PQ_Execution_Report.md`.
   - Fix any unknown requirement ID or failing tagged test in the tests, then run the sync again.
4. **Emit Session Artifact**: Write a structured summary to `<appDataDir>/brain/<conversation-id>/gxp_sync_summary.md` via `write_to_file`.
5. **Do Not Commit the Snapshot**: Commit only the code and tests. If the branch changed the generated documents, restore `main`'s copy and do not regenerate them on the branch:
   ```bash
   git checkout origin/main -- docs/SDLC/Requirements_Traceability_Matrix.md docs/SDLC/IQ_OQ_PQ_Execution_Report.md docs/SDLC/runs
   ```
   The `gxp-snapshot` gate (pre-commit hook, `pnpm cadence check`, CI) refuses these files on a feature branch.

## Integration Owner Only: Taking the Snapshot

The integration owner refreshes the committed documents on the integrated candidate, against PostgreSQL:

```bash
CADENCE_GXP_SNAPSHOT=1 pnpm cadence gxp snapshot --commit
```

`gxp snapshot` runs the tests, writes and stages the RTM, the IQ/OQ/PQ report and `docs/SDLC/runs/`, and fails when the RTM's test mapping does not match the tags. `--commit` commits with `CADENCE_GXP_SNAPSHOT=1` set so the pre-commit gate admits the files. Never run it on a feature branch.

## Gotchas

- Skipped database tests fail the sync. Point `CADENCE_TEST_POSTGRES` at a local PostgreSQL 16 when Docker is unavailable, and never export `POSTGRES_CONNECTION_STRING` in the test shell.
- RLS tests count only when they run as the application role, not a superuser or `BYPASSRLS` role.
- Stale `.claude/worktrees/` checkouts break the documentation gates; `git worktree remove` them first.
