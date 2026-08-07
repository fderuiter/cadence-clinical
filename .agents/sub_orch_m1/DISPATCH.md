## 2026-08-07T18:34:00Z

<USER_REQUEST>
You are the Sub-Orchestrator for Milestone M1: Foundational Core Utilities Migration.
Your working directory is: /Users/fred/Code/cadence-clinical/.agents/sub_orch_m1/
Project root: /Users/fred/Code/cadence-clinical/
Original request file path: /Users/fred/Code/cadence-clinical/.agents/ORIGINAL_REQUEST.md
Master Project Plan: /Users/fred/Code/cadence-clinical/PROJECT.md

YOUR SCOPE & OBJECTIVES:
1. Relocate shared infrastructure/GxP utilities out of `packages/core-models`:
   - `audit.py` (`Part11AuditMixin`, `AuditFields`) -> `packages/database/audit.py`
   - `datetime_helpers.py` -> `packages/database/datetime_helpers.py` (or `packages/security/datetime_helpers.py`)
   - `signature.py` (`SigningReason`, `ApprovalStatus`, `SignatureManifestation`) -> `packages/security/signature.py`
   - `storage/` -> `packages/storage/`
2. Update all import statements across `apps/` and `packages/` referencing these moved files.
3. Follow the Iteration Loop (Assess -> Explorer -> Worker -> Reviewer -> Challenger -> teamwork_preview_auditor).
4. Verify that `uv run ruff check .` and `uv run ruff format .` pass cleanly, and tests for affected packages pass.
5. Create `SCOPE.md` in `/Users/fred/Code/cadence-clinical/.agents/sub_orch_m1/SCOPE.md`.
6. Write your handoff report to `/Users/fred/Code/cadence-clinical/.agents/sub_orch_m1/handoff.md` and report completion to parent when done.
</USER_REQUEST>
