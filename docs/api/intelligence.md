# intelligence API

Generated from `docs/openapi/intelligence_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `POST` | `/api/v1/ai/biometrics/verify-liveness` | AiVerifyLiveness | Tests video frame payloads for anti-spoofing in temporary memory and returns signed liveness tokens. |
| `GET` | `/api/v1/ai/controls` | AiListControls | The tenant's AI kill switches and monthly token budgets. |
| `PUT` | `/api/v1/ai/controls/{scope}` | AiSetControl | Switches agents on or off for every task ('*') or one task, and sets the monthly token budget (agent_control.set). |
| `POST` | `/api/v1/ai/evaluation-runs` | AiRecordEvaluationRun | Stores an evaluation run record after checking it against its task configuration version, set, criteria and case results (evaluation_run.record, ADR-2634). |
| `GET` | `/api/v1/ai/evaluation-runs/{runId}` | AiGetEvaluationRun | One stored evaluation run with its record, exactly as hashed. |
| `POST` | `/api/v1/ai/intent/route` | RouteAiIntent | Match a command-palette query against fixed patterns and return the navigation targets the caller's roles allow. |
| `GET` | `/api/v1/ai/runs` | AiListMyRuns | The caller's latest agent runs from the interaction ledger, newest first. |
| `GET` | `/api/v1/ai/runs/{runId}` | AiGetMyRun | One of the caller's agent runs with its request, answer, usage and integrity hash. |
| `GET` | `/api/v1/ai/tasks` | AiListTasks | The agent task configuration versions in this deployment, with their content hashes and the tenant's qualification of each. |
| `GET` | `/api/v1/ai/tasks/{key}/evaluation-runs` | AiListEvaluationRuns | The stored evaluation runs of a task, newest first, optionally of one version. |
| `POST` | `/api/v1/ai/tasks/{key}/versions/{version}/qualification` | AiQualifyTask | Qualifies a task configuration version on a stored, accepted evaluation run and the caller's signed approval (task_configuration.qualify, ADR-2631). |
| `POST` | `/api/v1/ai/tasks/{key}/versions/{version}/suspension` | AiSuspendTask | Takes a qualified task configuration version out of use until it is qualified again (task_configuration.suspend). |
