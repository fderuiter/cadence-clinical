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

| Phase                     | Antigravity Tool       | Invocation Pattern                                                                                                                              | Objective                                                                           |
| :------------------------ | :--------------------- | :---------------------------------------------------------------------------------------------------------------------------------------------- | :---------------------------------------------------------------------------------- |
| **Status Inspection**     | `run_command`          | `CommandLine="git status --short"`                                                                                                              | Identify conflicted files and merge state                                           |
| **Marker Inspection**     | `view_file`            | Read conflicted files via `AbsolutePath`                                                                                                        | Inspect `<<<<<<<`, `=======`, `>>>>>>>` blocks                                      |
| **Conflict Resolution**   | `replace_file_content` | Target: conflicted source files, surgical edit                                                                                                  | Resolve domain logic preserving GxP properties                                      |
| **GxP Snapshot Recovery** | `run_command`          | `CommandLine="git checkout origin/main -- docs/SDLC/Requirements_Traceability_Matrix.md docs/SDLC/IQ_OQ_PQ_Execution_Report.md docs/SDLC/runs"` | Take `main`'s copy of the generated GxP snapshot; never regenerate it on the branch |
| **Quality Verification**  | `run_command`          | `CommandLine="dotnet cadence fix && dotnet cadence test --fast"`                                                                                | Format code and verify fast test suites                                             |

---

## 1. Inspect In-Progress Conflict State

Check status and identify conflicted files:

```bash
git status --short
```

---

## 2. Special Resolution Rules by File Type

### A. Generated GxP Snapshot (`docs/SDLC/Requirements_Traceability_Matrix.md`, `docs/SDLC/IQ_OQ_PQ_Execution_Report.md`, `docs/SDLC/runs/`)

**Never resolve conflicts in these files by hand.** They are a snapshot that only the integration owner takes with `CADENCE_GXP_SNAPSHOT=1 pnpm cadence gxp snapshot`, not feature-branch merge currency (ADR-2532).

1. Take `main`'s copy:
   `git checkout origin/main -- docs/SDLC/Requirements_Traceability_Matrix.md docs/SDLC/IQ_OQ_PQ_Execution_Report.md docs/SDLC/runs`
2. Do not regenerate them on the branch. To check the branch's requirement tags and results, run
   `pnpm cadence gxp sync`, which writes only a preview to `TestResults/gxp/preview/`.
3. No custom merge driver or `merge=union` exists for them; never add one.

### B. Dependency Lockfile (`pnpm-lock.yaml`)

**Never hand-edit lockfile conflict markers.**

1. `git checkout --ours pnpm-lock.yaml && pnpm install`
2. NuGet versions live in `Directory.Packages.props`; resolve that file like source code.

### C. ADR Index (`docs/adr/index.md`)

1. Keep both sides' entries, then validate:
   `dotnet cadence fix`, which validates the index but does not rewrite it; place your entry in its domain section by hand

### D. Source Code & Database Migrations

1. Use `view_file` to inspect conflict markers (`<<<<<<<`, `=======`, `>>>>>>>`).
2. Use `replace_file_content` to resolve domain logic preserving type safety (nullable reference types, kernel IDs) and GxP audit fields.
3. Verify with `dotnet cadence fix`.

---

## 3. Verify & Finalize

1. Run full test suite:
   `dotnet cadence test --fast`
2. Complete merge/rebase:
   `git add . && git merge --continue` (or `git rebase --continue`)
