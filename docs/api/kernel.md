# kernel API

Generated from `docs/openapi/kernel_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

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
| `GET` | `/api/v1/execution/forms` | ExecutionListEnrollmentForms | Lists the forms opened for an enrollment, oldest first. |
| `POST` | `/api/v1/execution/forms` | ExecutionOpenForm | Opens a form for a participant's visit under the protocol version in force (collection.open). |
| `GET` | `/api/v1/execution/forms/queries` | ExecutionListEnrollmentQueries | Lists the queries on every form of an enrollment or across visible enrollments of a study, oldest first. |
| `GET` | `/api/v1/execution/forms/{formId}` | ExecutionGetForm | Reads a form: its items in layout order with their values, completeness, locks, assertions and signatures. |
| `GET` | `/api/v1/execution/forms/{formId}/queries` | ExecutionListFormQueries | Lists a form's queries, oldest first, each with whether its value changed since it was raised. |
| `POST` | `/api/v1/execution/forms/{formId}/queries` | ExecutionRaiseFormQuery | Raises a query on the current revision of one of the form's values or on a blank item (execution.query.raise). |
| `POST` | `/api/v1/execution/forms/{formId}/queries/{queryId}/answer` | ExecutionAnswerFormQuery | Answers an open query (execution.query.answer). |
| `POST` | `/api/v1/execution/forms/{formId}/queries/{queryId}/cancel` | ExecutionCancelFormQuery | Cancels an open or answered query raised in error (execution.query.manage). |
| `POST` | `/api/v1/execution/forms/{formId}/queries/{queryId}/close` | ExecutionCloseFormQuery | Closes an open or answered query; the change reason is the closing note (execution.query.manage). |
| `POST` | `/api/v1/execution/forms/{formId}/queries/{queryId}/reopen` | ExecutionReopenFormQuery | Reopens an answered or closed query with why it is not settled (execution.query.manage). |
| `POST` | `/api/v1/execution/forms/{formId}/signatures` | ExecutionSignForm | Signs the form's current values with an X-Sig-Token step-up token (collection.sign); 409 when the form changed since it was read. |
| `PUT` | `/api/v1/execution/forms/{formId}/values` | ExecutionSaveFormValues | Saves a page of values: all of them or, on one stale revision or invalid value, none (collection.record). |
| `GET` | `/api/v1/kernel/activity` | KernelActivity | What happened in a scope, newest first. |
| `POST` | `/api/v1/kernel/assertions/{assertionRef}/retraction` | KernelRetractAssertion | Retracts a verification, answer or query assertion with the signed reason for change (assertion.retract). |
| `GET` | `/api/v1/kernel/authorization/can-i` | KernelCanI | Whether the caller may exercise a capability on an object now, and why. |
| `GET` | `/api/v1/kernel/entitlements` | KernelGetEntitlements | The caller's tenant commercial subscription tier, trial state, and enabled product families. |
| `POST` | `/api/v1/kernel/entitlements/tier` | KernelUpgradeSubscriptionTier | Activates or upgrades the tenant's commercial tier in place while preserving all clinical and audit records. |
| `GET` | `/api/v1/kernel/nodes/{nodeRef}` | KernelGetNode | Resolves a NodeRef to its summary. |
| `GET` | `/api/v1/kernel/nodes/{nodeRef}/children` | KernelGetChildren | The children of a scope node, with their own child counts. |
| `GET` | `/api/v1/kernel/nodes/{nodeRef}/commands` | KernelGetCommands | The kernel commands offered on an object, each with the caller's dry-run decision. |
| `GET` | `/api/v1/kernel/nodes/{nodeRef}/dependents` | KernelDependents | What depends on an object, grouped by kind with counts. |
| `GET` | `/api/v1/kernel/nodes/{nodeRef}/lineage` | KernelTrace | Walks an object's lineage backward to its inputs or forward to its dependents. |
| `GET` | `/api/v1/kernel/nodes/{nodeRef}/path` | KernelGetZoomPath | The zoom path from the tenant down to an object. |
| `POST` | `/api/v1/kernel/observations` | KernelRecordObservation | Records an observation for the participant of an enrollment (observation.record). |
| `GET` | `/api/v1/kernel/observations/{observationRef}` | KernelGetObservation | An observation with its revisions, the locks covering it, the assertions about it and its kernel audit entries. |
| `POST` | `/api/v1/kernel/observations/{observationRef}/corrections` | KernelCorrectObservation | Corrects an observation's value with the signed reason for change (observation.correct). |
| `POST` | `/api/v1/kernel/observations/{observationRef}/verifications` | KernelVerifyObservation | Verifies the current revision of an observation against source with the signed reason for change (observation.verify). |
| `POST` | `/api/v1/kernel/onboarding/trial` | KernelBootstrapTrialWorkspace | Provisions an isolated self-service trial workspace for an authenticated OIDC caller. |
| `GET` | `/api/v1/kernel/proposals/{proposalRef}` | KernelGetProposal | One proposal with its intent, plan, affected objects, requested capabilities, model, content hash and decision. |
| `POST` | `/api/v1/kernel/proposals/{proposalRef}/rejection` | KernelRejectProposal | Rejects an open proposal; the signed reason for change is the rejection reason (proposal.reject). |
| `GET` | `/api/v1/kernel/protocol-versions/{versionRef}` | KernelGetProtocolVersion | One protocol version, without its content. |
| `GET` | `/api/v1/kernel/protocol-versions/{versionRef}/impact` | KernelAmendmentImpact | What depends on the version a protocol amendment changes (K-PROT-2). |
| `GET` | `/api/v1/kernel/protocol-versions/{versionRef}/schedule-of-activities` | KernelGetScheduleOfActivities | A protocol version's Schedule of Activities, compiled from its design. |
| `POST` | `/api/v1/kernel/protocols/matrix-gap-check` | KernelMatrixGapCheck | Performs immediate real-time matrix gap check on a candidate protocol design graph. |
| `GET` | `/api/v1/kernel/search` | KernelSearch | Finds kernel objects in the caller's tenant by code or title. |
| `POST` | `/api/v1/kernel/simulation/simulate` | KernelSimulateProtocol | Evaluates a candidate USDM protocol version in memory without database changes. |
| `GET` | `/api/v1/kernel/status` | KernelStatus | Returns the Clinical Kernel API's operational status. |
| `GET` | `/api/v1/kernel/studies/{studyRef}/proposals` | KernelListProposals | A study's proposals, newest first, optionally only those with one status (Open, Approved, Rejected, Withdrawn, Expired). |
| `GET` | `/api/v1/kernel/studies/{studyRef}/protocol-versions` | KernelListProtocolVersions | A study's protocol versions, newest first, without their content. |
| `GET` | `/healthz` | KernelHealthCheck | Clinical Kernel API health probe. |
