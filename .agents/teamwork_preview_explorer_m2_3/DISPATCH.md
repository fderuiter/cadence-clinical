## 2026-08-07T14:47:18Z

You are teamwork_preview_explorer_m2_3 working in /Users/fred/Code/cadence-clinical/.agents/teamwork_preview_explorer_m2_3/.
Your assigned task is to investigate and map the files, model classes, and import sites for:
1. Notifications domain models: `notifications` models -> target `apps/notifications/src/domain/`
2. Organization domain models: `organization_domain` models -> target `apps/org/src/domain/`
3. Interop domain models: `sync_engine` models -> target `apps/interop/src/domain/`

Context documents:
- /Users/fred/Code/cadence-clinical/.agents/ORIGINAL_REQUEST.md
- /Users/fred/Code/cadence-clinical/PROJECT.md
- /Users/fred/Code/cadence-clinical/.agents/sub_orch_m2/DISPATCH.md

Instructions:
1. Search `packages/core-models/` for all source files corresponding to Notifications (`notifications`), Organization (`organization_domain`), and Interop (`sync_engine`) domain models.
2. For each file identified, list the exact model classes/functions defined.
3. Search across `apps/`, `packages/`, `scripts/`, `tests/` for all import statements referencing these files/symbols.
4. Specify the exact target destination path under `apps/<service>/src/domain/` for each file/symbol.
5. Identify any potential import conflicts or circular dependency risks.
6. Write your detailed findings to `/Users/fred/Code/cadence-clinical/.agents/teamwork_preview_explorer_m2_3/analysis.md` and create `/Users/fred/Code/cadence-clinical/.agents/teamwork_preview_explorer_m2_3/handoff.md`.
7. Send a message to parent (`34f7436c-be3f-4037-9a01-5d758d8a7573`) when finished.
Do NOT modify any source code files. You are a read-only exploration agent.
