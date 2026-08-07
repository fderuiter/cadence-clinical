## 2026-08-07T20:12:01Z

You are teamwork_preview_reviewer_m2_3 working in /Users/fred/Code/cadence-clinical/.agents/teamwork_preview_reviewer_m2_3/.
Your assigned task is to independently review code quality, import statements, linting, and formatting for Milestone M2: Primary Services Domain Migration.

Context documents:
- /Users/fred/Code/cadence-clinical/.agents/ORIGINAL_REQUEST.md
- /Users/fred/Code/cadence-clinical/PROJECT.md
- /Users/fred/Code/cadence-clinical/.agents/sub_orch_m2/DISPATCH.md
- Worker 2 handoff report: /Users/fred/Code/cadence-clinical/.agents/teamwork_preview_worker_m2_2/handoff.md

Review Objectives:
1. Verify that all primary domain models (`designer`, `safety`, `ctms`, `etmf`, `notifications`, `org`, `interop`) have been relocated to `apps/<service>/src/domain/`.
2. Verify that no references to legacy `packages/core-models` paths remain for these relocated models in `apps/`, `packages/`, `scripts/`, `tests/`.
3. Verify linting, formatting, and duplication checks:
   - `uv run ruff check .`
   - `uv run ruff check .agents/`
   - `uv run ruff format --check .`
   - `uv run ruff format --check .agents/`
   - `python3 scripts/detect_duplication.py`
4. Formulate your explicit verdict: `APPROVE` or `REQUEST_CHANGES`.
5. Write your detailed review to `/Users/fred/Code/cadence-clinical/.agents/teamwork_preview_reviewer_m2_3/review.md` and create `/Users/fred/Code/cadence-clinical/.agents/teamwork_preview_reviewer_m2_3/handoff.md` with your verdict.
6. Send a message to parent (`34f7436c-be3f-4037-9a01-5d758d8a7573`) when completed.
