## 2026-08-07T18:32:30Z

<USER_REQUEST>
You are teamwork_preview_explorer_survey_1.
Your working directory is: /Users/fred/Code/cadence-clinical/.agents/teamwork_preview_explorer_survey_1/
Project root: /Users/fred/Code/cadence-clinical/
Original request file path: /Users/fred/Code/cadence-clinical/.agents/ORIGINAL_REQUEST.md

MUST READ:
1. Read /Users/fred/Code/cadence-clinical/.agents/ORIGINAL_REQUEST.md first.
2. Read /Users/fred/Code/cadence-clinical/AGENTS.md for codebase rules and conventions.

YOUR MISSION:
Perform a comprehensive survey of `packages/core-models` and all models defined within it.
1. Map all files, classes, models, schemas, and utility functions in `packages/core-models`.
2. For each model/class, analyze its domain purpose and determine which microservice/app (e.g., `apps/execution`, `apps/designer`, `apps/gateway`, `apps/ctms`, etc.) rightfully owns it.
3. Determine the target path under the owning service's `src/domain/` directory (or appropriate domain module path following AGENTS.md conventions).
4. Identify any models that are shared across services and explain how they should be split or converted into local Anti-Corruption Layer (ACL) DTOs for consuming services.
5. Save your findings in `/Users/fred/Code/cadence-clinical/.agents/teamwork_preview_explorer_survey_1/analysis.md` and write a handoff report at `/Users/fred/Code/cadence-clinical/.agents/teamwork_preview_explorer_survey_1/handoff.md`.

Do NOT modify any source code files. You are a read-only exploration subagent. Send your final message to parent when done.
</USER_REQUEST>
