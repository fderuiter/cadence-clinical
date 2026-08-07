# Task Assignment — Sub-Orchestrator M2

## 2026-08-07T19:47:00Z

<DISPATCH>
You are the Sub-Orchestrator for Milestone M2: Primary Services Domain Migration.
Working directory: `/Users/fred/Code/cadence-clinical/.agents/sub_orch_m2/`
Parent Conversation ID: `34f7436c-be3f-4037-9a01-5d758d8a7573`
Project Scope Document: `/Users/fred/Code/cadence-clinical/PROJECT.md`
Original Request: `/Users/fred/Code/cadence-clinical/.agents/ORIGINAL_REQUEST.md`

### Objectives:
Complete Milestone M2: Relocate domain models out of `packages/core-models` to their owning primary service `src/domain/` directories:
1. **Designer**: USDM, Protocol Authoring, Protocol Render, Protocol Version Ref, Eligibility, USDM Ingestion, Document Renderer -> `apps/designer/src/domain/`
2. **Safety**: `sae_icsr` and ICSR models -> `apps/safety/src/domain/`
3. **CTMS**: `ctms` DOA models -> `apps/ctms/src/domain/`
4. **eTMF**: TMF reference model & `etmf` models -> `apps/etmf/src/domain/`
5. **Notifications & Org**: `notifications` and `organization_domain` -> `apps/notifications/src/domain/` & `apps/org/src/domain/`
6. **Interop**: `sync_engine` models -> `apps/interop/src/domain/`

### Execution Directives:
1. Update all import sites across `apps/`, `packages/`, `scripts/`, `tests/` that reference these models.
2. Ensure microservice boundary rules are respected: each service owns its domain models under `apps/<service>/src/domain/`.
3. Verify quality and compliance gates:
   - `uv run ruff check .`
   - `uv run ruff format --check .`
   - `python3 scripts/detect_duplication.py`
   - `uv run pytest -n auto`
   - `uv run python scripts/sync_gxp.py`
4. Execute the iteration loop (Explorer -> Worker -> Reviewers -> Challengers -> Forensic Auditor `teamwork_preview_auditor`).
5. STRICT CONCURRENCY CAP: Maintain a maximum of 3 active subagents under your sub-orchestrator at any time (so total hierarchy stays under 5).
6. When gate passes (all reviewers APPROVE, challengers APPROVE, auditor CLEAN), write `handoff.md` and report completion back to parent (`34f7436c-be3f-4037-9a01-5d758d8a7573`).

## 2026-08-07T20:13:00Z

<DISPATCH>
You are Sub-Orchestrator M2 for Milestone M2: Primary Services Domain Migration.
Working directory: /Users/fred/Code/cadence-clinical/.agents/sub_orch_m2/
Project root: /Users/fred/Code/cadence-clinical
Parent conversation ID: 46b1fc70-68c3-410e-ba5c-2336ebb72fb2

Mission:
Complete Milestone M2 (Primary Services Domain Migration).
Review existing state in /Users/fred/Code/cadence-clinical/.agents/sub_orch_m2/ (BRIEFING.md, progress.md, GATE_STATUS.md).
worker_m2_2 has completed fixing ruff check and formatting issues.
Your remaining tasks for M2 Iteration 2:
1. Dispatch Reviewers (teamwork_preview_reviewer) to review code formatting, model relocation, import updates, and test pass status.
2. Dispatch Challengers (teamwork_preview_challenger) to empirically test runtime behavior and check for import regressions.
3. Dispatch 1 Forensic Auditor (teamwork_preview_auditor) to perform integrity verification.
4. Evaluate Gate criteria in GATE_STATUS.md:
   - All tests pass (uv run pytest -n auto)
   - All Reviewers APPROVE
   - All Challengers confirm correctness
   - Forensic Auditor verdict is CLEAN
5. Update /Users/fred/Code/cadence-clinical/PROJECT.md to set Milestone M2 Status to DONE.
6. Write handoff.md in /Users/fred/Code/cadence-clinical/.agents/sub_orch_m2/ and send a message with completion report to parent (46b1fc70-68c3-410e-ba5c-2336ebb72fb2).

Constraints:
- CONCURRENCY CAP: Maximum 3 active subagents concurrently under sub_orch_m2 (total across hierarchy <= 5).
- Do NOT write or edit source code directly; use Workers.
</DISPATCH>

