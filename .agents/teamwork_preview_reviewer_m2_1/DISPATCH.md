## 2026-08-07T20:00:05Z
You are teamwork_preview_reviewer_m2_1 working in /Users/fred/Code/cadence-clinical/.agents/teamwork_preview_reviewer_m2_1/.
Your assigned task is to independently review the code changes implemented for Milestone M2: Primary Services Domain Migration.

Context documents:
- /Users/fred/Code/cadence-clinical/.agents/ORIGINAL_REQUEST.md
- /Users/fred/Code/cadence-clinical/PROJECT.md
- /Users/fred/Code/cadence-clinical/.agents/sub_orch_m2/DISPATCH.md
- Worker handoff report: /Users/fred/Code/cadence-clinical/.agents/teamwork_preview_worker_m2_1/handoff.md

Review Objectives:
1. Verify that all primary domain models (`designer`, `safety`, `ctms`, `etmf`, `notifications`, `org`, `interop`) have been correctly relocated to `apps/<service>/src/domain/`.
2. Inspect import paths in modified files across `apps/`, `packages/`, `scripts/`, `tests/` to verify that no references to the old `packages/core-models/` paths remain for these relocated models.
3. Run and verify linting, formatting, and duplication checks:
   - `uv run ruff check .`
   - `uv run ruff format --check .`
   - `python3 scripts/detect_duplication.py`
4. Formulate your explicit verdict: `APPROVE` or `REQUEST_CHANGES`.
5. Write your detailed review to `/Users/fred/Code/cadence-clinical/.agents/teamwork_preview_reviewer_m2_1/review.md` and create `/Users/fred/Code/cadence-clinical/.agents/teamwork_preview_reviewer_m2_1/handoff.md` with your verdict.
6. Send a message to parent (`34f7436c-be3f-4037-9a01-5d758d8a7573`) when completed.
