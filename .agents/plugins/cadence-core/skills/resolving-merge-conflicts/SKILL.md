---
name: resolving-merge-conflicts
description: "Systematically resolve in-progress git merge or rebase conflicts, with specialized handling for GxP compliance reports, lockfiles, and ADR indices. Use when resolving git conflicts."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Merge Conflict Resolution

Systematically resolves merge and rebase conflicts across code, schema models, generated GxP documents, and lockfiles.

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
   `uv run python scripts/sync_gxp.py`

### B. Dependency Lockfiles (`uv.lock`, `pnpm-lock.yaml`)
**Never hand-edit lockfile conflict markers.**
1. For Python: `git checkout --ours uv.lock && uv lock`
2. For Frontend: `git checkout --ours pnpm-lock.yaml && pnpm install`

### C. ADR Index (`docs/adr/index.md`)
1. Re-sort and re-index using the ADR tooling:
   `uv run python scripts/create_adr.py --reindex` (or `uv run cadence fix --all`)

### D. Source Code & Database Migrations
1. Open conflicting files and inspect conflict markers (`<<<<<<<`, `=======`, `>>>>>>>`).
2. Resolve domain logic preserving type safety (Pydantic v2) and GxP audit fields.
3. Verify with `uv run cadence fix --all`.

---

## 3. Verify & Finalize
1. Run full test suite:
   `uv run cadence test --fast`
2. Complete merge/rebase:
   `git add . && git merge --continue` (or `git rebase --continue`)
