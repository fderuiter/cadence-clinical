# kernel API

Generated from `docs/openapi/kernel_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `GET` | `/api/v1/ai/controls` | AiListControls | The tenant's AI kill switches and monthly token budgets. |
| `PUT` | `/api/v1/ai/controls/{scope}` | AiSetControl | Switches agents on or off for every task ('*') or one task, and sets the monthly token budget (agent_control.set). |
| `POST` | `/api/v1/ai/intent/route` | RouteAiIntent | Match a command-palette query against fixed patterns and return the navigation targets the caller's roles allow. |
| `GET` | `/api/v1/ai/runs` | AiListMyRuns | The caller's latest agent runs from the interaction ledger, newest first. |
| `GET` | `/api/v1/ai/runs/{runId}` | AiGetMyRun | One of the caller's agent runs with its request, answer, usage and integrity hash. |
| `GET` | `/api/v1/ai/tasks` | AiListTasks | The agent task configuration versions in this deployment, with their content hashes and the tenant's qualification of each. |
| `POST` | `/api/v1/ai/tasks/{key}/versions/{version}/qualification` | AiQualifyTask | Qualifies a task configuration version on its evaluation evidence and the caller's signed approval (task_configuration.qualify, ADR-2631). |
| `POST` | `/api/v1/ai/tasks/{key}/versions/{version}/suspension` | AiSuspendTask | Takes a qualified task configuration version out of use until it is qualified again (task_configuration.suspend). |
| `GET` | `/api/v1/kernel/activity` | KernelActivity | What happened in a scope, newest first. |
| `GET` | `/api/v1/kernel/authorization/can-i` | KernelCanI | Whether the caller may exercise a capability on an object now, and why. |
| `GET` | `/api/v1/kernel/nodes/{nodeRef}` | KernelGetNode | Resolves a NodeRef to its summary. |
| `GET` | `/api/v1/kernel/nodes/{nodeRef}/children` | KernelGetChildren | The children of a scope node, with their own child counts. |
| `GET` | `/api/v1/kernel/nodes/{nodeRef}/commands` | KernelGetCommands | The kernel commands offered on an object, each with the caller's dry-run decision. |
| `GET` | `/api/v1/kernel/nodes/{nodeRef}/dependents` | KernelDependents | What depends on an object, grouped by kind with counts. |
| `GET` | `/api/v1/kernel/nodes/{nodeRef}/lineage` | KernelTrace | Walks an object's lineage backward to its inputs or forward to its dependents. |
| `GET` | `/api/v1/kernel/nodes/{nodeRef}/path` | KernelGetZoomPath | The zoom path from the tenant down to an object. |
| `POST` | `/api/v1/kernel/observations` | KernelRecordObservation | Records an observation for the participant of an enrollment (observation.record). |
| `GET` | `/api/v1/kernel/observations/{observationRef}` | KernelGetObservation | An observation with its revisions, the locks covering it, the assertions about it and its kernel audit entries. |
| `POST` | `/api/v1/kernel/observations/{observationRef}/corrections` | KernelCorrectObservation | Corrects an observation's value with the signed reason for change (observation.correct). |
| `GET` | `/api/v1/kernel/protocol-versions/{versionRef}` | KernelGetProtocolVersion | One protocol version, without its content. |
| `GET` | `/api/v1/kernel/protocol-versions/{versionRef}/impact` | KernelAmendmentImpact | What depends on the version a protocol amendment changes (K-PROT-2). |
| `GET` | `/api/v1/kernel/protocol-versions/{versionRef}/schedule-of-activities` | KernelGetScheduleOfActivities | A protocol version's Schedule of Activities, compiled from its design. |
| `POST` | `/api/v1/kernel/protocols/generate-proposal` | KernelGenerateProtocolProposal | Generates a complete USDM protocol proposal from a trial brief with reasoning lineage. |
| `POST` | `/api/v1/kernel/protocols/matrix-gap-check` | KernelMatrixGapCheck | Performs immediate real-time matrix gap check on a candidate protocol design graph. |
| `GET` | `/api/v1/kernel/search` | KernelSearch | Finds kernel objects in the caller's tenant by code or title. |
| `POST` | `/api/v1/kernel/simulation/simulate` | KernelSimulateProtocol | Evaluates a candidate USDM protocol version in memory without database changes. |
| `GET` | `/api/v1/kernel/status` | KernelStatus | Returns the Clinical Kernel API's operational status. |
| `GET` | `/api/v1/kernel/studies/{studyRef}/protocol-versions` | KernelListProtocolVersions | A study's protocol versions, newest first, without their content. |
| `GET` | `/healthz` | KernelHealthCheck | Clinical Kernel API health probe. |
