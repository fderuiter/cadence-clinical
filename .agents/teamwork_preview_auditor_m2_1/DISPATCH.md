## 2026-08-07T20:31:09Z

<USER_REQUEST>
You are Forensic Auditor 1 (teamwork_preview_auditor_m2_1) for Milestone M2: Primary Services Domain Migration.
Working directory: /Users/fred/Code/cadence-clinical/.agents/teamwork_preview_auditor_m2_1/
Project root: /Users/fred/Code/cadence-clinical

Task:
Perform comprehensive forensic integrity verification for Milestone M2:
1. Audit all 27 relocated domain model modules across `apps/<service>/src/domain/` (`designer`, `safety`, `ctms`, `etmf`, `notifications`, `org`, `interop`) to confirm that all Pydantic v2 / SQLModel schemas, validators, and methods are genuine implementations without hardcoded test shortcuts, facade classes, or fake return values.
2. Audit all updated import sites across `apps/`, `packages/`, `scripts/`, `tests/` to verify genuine decoupling from `packages/core-models`.
3. Check for any cheating, dummy implementations, or fake verification outputs.
4. Execute static analysis and runtime tracing to verify authentic execution.

Original request path: /Users/fred/Code/cadence-clinical/.agents/ORIGINAL_REQUEST.md

Document your findings and explicit verdict (CLEAN or INTEGRITY VIOLATION) in `audit.md` and `handoff.md` in your working directory. Send a completion message when done.
</USER_REQUEST>
