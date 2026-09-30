---
name: resolving-merge-conflicts
description: "Systematically resolve in-progress git merge or rebase conflicts, with specialized handling for GxP compliance reports, lockfiles, and ADR indices. Use when resolving git conflicts."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Merge Conflict Resolution

Systematically resolves merge and rebase conflicts across code, schema models, generated GxP documents, and lockfiles using Antigravity inspection, editing, and qualification tools.

## Antigravity Tool Execution Matrix

| Phase | Antigravity Tool | Invocation Pattern | Objective |
| :--- | :--- | :--- | :--- |
| **Status Inspection** | `run_command` | `CommandLine="git status --short"` | Identify conflicted files and merge state |
| **Marker Inspection** | `view_file` | Read conflicted files via `AbsolutePath` | Inspect `<<<<<<<`, `=======`, `>>>>>>>` blocks |
| **Conflict Resolution**| `replace_file_content` | Target: conflicted source files, surgical edit | Resolve domain logic preserving GxP properties |
| **GxP Auto-Recovery** | `run_command` | `CommandLine="git checkout --ours docs/SDLC/ && dotnet cadence gxp sync"` | Cleanly regenerate GxP compliance reports |
| **Quality Verification**| `run_command` | `CommandLine="dotnet cadence fix --all && dotnet cadence test --fast"` | Format code and verify fast test suites |

---

## 1. Inspect In-Progress Conflict State
Check status and identify conflicted files:
```bash
git status --short
```

---

## 2. Special Resolution Rules by File Type

### A. GxP Compliance Documents (`docs/SDLC/*.md`)
**Never resolve conflicts in `Requirements_Traceability_Matrix.md` or `IQ_OQ_PQ_Execution_Report.md` by hand.**
1. Accept incoming or current version:
   `git checkout --ours docs/SDLC/`
2. Re-run automated sync to cleanly regenerate:
   `dotnet cadence gxp sync`

### B. Dependency Lockfiles (`uv.lock`, `pnpm-lock.yaml`)
**Never hand-edit lockfile conflict markers.**
1. For Python: `git checkout --ours uv.lock && uv lock`
2. For Frontend: `git checkout --ours pnpm-lock.yaml && pnpm install`

### C. ADR Index (`docs/adr/index.md`)
1. Re-sort and re-index using the ADR tooling:
   `dotnet cadence fix --all` (or `dotnet cadence fix --all`)

### D. Source Code & Database Migrations
1. Use `view_file` to inspect conflict markers (`<<<<<<<`, `=======`, `>>>>>>>`).
2. Use `replace_file_content` to resolve domain logic preserving type safety (nullable reference types, kernel IDs) and GxP audit fields.
3. Verify with `dotnet cadence fix --all`.

---

## 3. Verify & Finalize
1. Run full test suite:
   `dotnet cadence test --fast`
2. Complete merge/rebase:
   `git add . && git merge --continue` (or `git rebase --continue`)

