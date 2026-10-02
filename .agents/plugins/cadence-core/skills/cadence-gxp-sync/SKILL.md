---
name: cadence-gxp-sync
description: "Synchronize 21 CFR Part 11 GxP compliance documentation, Requirements Traceability Matrix (RTM), and IQ/OQ/PQ execution reports. Use when adding/modifying tests, updating @req docstrings, or resolving CI compliance drift."
license: MIT
metadata:
  author: Cadence Team
  version: "1.0"
---

# Cadence GxP Compliance Synchronization

Automates 21 CFR Part 11 and FDA GxP compliance document regeneration and traceability matrix alignment using Antigravity asynchronous tasks and validation tools.

## Antigravity Tool Execution Matrix

| Phase                | Antigravity Tool | Invocation Pattern                                                 | Objective                                           |
| :------------------- | :--------------- | :----------------------------------------------------------------- | :-------------------------------------------------- |
| **Execution**        | `run_command`    | `CommandLine="dotnet cadence gxp sync"`, `WaitMsBeforeAsync=10000` | Launch qualification suite and RTM generator        |
| **Task Monitoring**  | `manage_task`    | `Action="status"`, `TaskId="..."`                                  | Monitor long-running test execution asynchronously  |
| **Watchdog Timer**   | `schedule`       | `DurationSeconds=180`, `TimerCondition=<task-id>`                  | Bound execution time to prevent hanging workers     |
| **Verification**     | `view_file`      | `AbsolutePath=".../docs/SDLC/Requirements_Traceability_Matrix.md"` | Verify requirement coverage percentage and outcomes |
| **Summary Artifact** | `write_to_file`  | Target: `<appDataDir>/brain/<conversation-id>/gxp_sync_summary.md` | Emit audit-ready execution summary artifact         |

## When to Run

Run this workflow whenever:

- Test methods are added, renamed, or deleted in xUnit v3 test suites.
- `@req:PRD-SYS-xxx` tags in test doc comments or `[GxPRequirement("PRD-SYS-xxx")]` attributes in C# tests are added or modified.
- Test pass/fail outcomes change.
- CI fails with: `GxP compliance documentation is out of sync with the current system state!`

## Execution Protocol

1. **Launch Sync**: Run `dotnet cadence gxp sync` via `run_command`.
   - Executes `dotnet test Cadence.sln` across all test assemblies.
   - Tooling scans `.cs` test files, parsing TRX reports into a unified Requirements Traceability Matrix.
2. **Handle Background Task**: If sent to background, inspect progress using `manage_task(Action="status")`. Set a `schedule` watchdog timer if needed.
3. **Verify Staged Artifacts**:
   - Inspect [`docs/SDLC/Requirements_Traceability_Matrix.md`](../../../../docs/SDLC/Requirements_Traceability_Matrix.md).
   - Inspect [`docs/SDLC/IQ_OQ_PQ_Execution_Report.md`](../../../../docs/SDLC/IQ_OQ_PQ_Execution_Report.md).

4. **Emit Session Artifact**: Write a structured summary to `<appDataDir>/brain/<conversation-id>/gxp_sync_summary.md` via `write_to_file`.
5. **Commit**: Guide the user or commit staged SDLC files:
   ```bash
   git commit -m "docs(rtm): sync GxP compliance docs with current test state"
   ```
