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
| `GET` | `/api/v1/execution/forms/queries/digests` | ExecutionListFormQueryAgingDigests | Lists daily de-identified query aging digests for visible scopes of a study. |
| `POST` | `/api/v1/execution/forms/queries/escalate` | ExecutionEscalateOverdueFormQueries | Escalates open queries past their 14-day SLA on IClock and emits daily aging digests (execution.query.manage). |
| `GET` | `/api/v1/execution/forms/{formId}` | ExecutionGetForm | Reads a form: its items in layout order with their values, completeness, locks, assertions and signatures. |
| `POST` | `/api/v1/execution/forms/{formId}/preview` | ExecutionPreviewFormValues | Evaluates pinned study collection rules in memory over draft form values without mutating state (K-RULE-3). |
| `GET` | `/api/v1/execution/forms/{formId}/queries` | ExecutionListFormQueries | Lists a form's queries, oldest first, each with whether its value changed since it was raised. |
| `POST` | `/api/v1/execution/forms/{formId}/queries` | ExecutionRaiseFormQuery | Raises a query on the current revision of one of the form's values or on a blank item (execution.query.raise). |
| `POST` | `/api/v1/execution/forms/{formId}/queries/{queryId}/answer` | ExecutionAnswerFormQuery | Answers an open query (execution.query.answer). |
| `POST` | `/api/v1/execution/forms/{formId}/queries/{queryId}/cancel` | ExecutionCancelFormQuery | Cancels an open or answered query raised in error (execution.query.manage). |
| `POST` | `/api/v1/execution/forms/{formId}/queries/{queryId}/close` | ExecutionCloseFormQuery | Closes an answered manual query (separation of duties) or system query (execution.query.manage). |
| `POST` | `/api/v1/execution/forms/{formId}/queries/{queryId}/reopen` | ExecutionReopenFormQuery | Reopens an answered or closed query with why it is not settled (execution.query.manage). |
| `POST` | `/api/v1/execution/forms/{formId}/signatures` | ExecutionSignForm | Signs the form's current values with an X-Sig-Token step-up token (collection.sign); 409 when the form changed since it was read. |
| `PUT` | `/api/v1/execution/forms/{formId}/values` | ExecutionSaveFormValues | Saves a page of values: all of them or, on one stale revision or invalid value, none (collection.record). |
| `GET` | `/api/v1/kernel/activity` | KernelActivity | What happened in a scope, newest first. |
| `POST` | `/api/v1/kernel/assertions/{assertionRef}/retraction` | KernelRetractAssertion | Retracts a verification, answer or query assertion with the signed reason for change (assertion.retract). |
| `GET` | `/api/v1/kernel/authorization/can-i` | KernelCanI | Whether the caller may exercise a capability on an object now, and why. |
| `GET` | `/api/v1/kernel/enrollments/lookup` | KernelLookupEnrollment | Resolves a study code, subject code, and optional site code to pinned study and enrollment references. |
| `POST` | `/api/v1/kernel/enrollments/screen` | KernelScreenEnrollment | Registers a pseudonymous participant when needed and starts a screening enrollment at a study site (participant.screen). |
| `GET` | `/api/v1/kernel/enrollments/{enrollmentRef}/casebook` | KernelExportEnrollmentCasebook | Streams a participant casebook with revisions, queries, verifications, attestations and SHA-256 digests. |
| `GET` | `/api/v1/kernel/entitlements` | KernelGetEntitlements | The caller's tenant commercial subscription tier, trial state, and enabled product families. |
| `POST` | `/api/v1/kernel/entitlements/activate` | KernelActivateSubscription | Activates a Trialing or Expired commercial subscription after validating study scoping and a verified quote token. |
| `GET` | `/api/v1/kernel/entitlements/posture` | KernelGetEntitlementPosture | Minimal internal tenant commercial posture for the API gateway. |
| `POST` | `/api/v1/kernel/entitlements/tier` | KernelUpgradeSubscriptionTier | Switches the tenant's commercial evaluation tier in place while preserving trial countdown and clinical records. |
| `POST` | `/api/v1/kernel/library-items/{itemRef}/approve` | KernelApproveLibraryItem | Approves a draft library item version with an Approved attestation (library_item.approve). |
| `POST` | `/api/v1/kernel/locks` | KernelApplyLock | Applies a Frozen or Locked assertion to a study, study site, enrollment, form instance or observation (lock.apply). |
| `POST` | `/api/v1/kernel/locks/{lockRef}/release` | KernelReleaseLock | Releases a Frozen or Locked assertion with a signed retracting assertion (lock.release). |
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
| `POST` | `/api/v1/kernel/onboarding/trial` | KernelBootstrapTrialWorkspace | Provisions an isolated self-service trial workspace for an unbound authenticated OIDC caller. |
| `GET` | `/api/v1/kernel/organizations` | KernelListOrganizations | Lists visible organizations in the caller's tenant. |
| `GET` | `/api/v1/kernel/proposals/{proposalRef}` | KernelGetProposal | One proposal with its intent, plan, affected objects, requested capabilities, model, content hash and decision. |
| `POST` | `/api/v1/kernel/proposals/{proposalRef}/rejection` | KernelRejectProposal | Rejects an open proposal; the signed reason for change is the rejection reason (proposal.reject). |
| `GET` | `/api/v1/kernel/protocol-templates` | KernelListProtocolTemplates | Lists bundled standard CDISC Library eCRF templates available for protocol draft import. |
| `GET` | `/api/v1/kernel/protocol-versions/{versionRef}` | KernelGetProtocolVersion | One protocol version, without its content. |
| `GET` | `/api/v1/kernel/protocol-versions/{versionRef}/design-summary` | KernelGetProtocolDesignSummary | Returns a viewable summary of a protocol version's forms, ordered items, and import notes. |
| `GET` | `/api/v1/kernel/protocol-versions/{versionRef}/impact` | KernelAmendmentImpact | What depends on the version a protocol amendment changes (K-PROT-2). |
| `GET` | `/api/v1/kernel/protocol-versions/{versionRef}/schedule-of-activities` | KernelGetScheduleOfActivities | A protocol version's Schedule of Activities, compiled from its design. |
| `POST` | `/api/v1/kernel/protocols/matrix-gap-check` | KernelMatrixGapCheck | Performs immediate real-time matrix gap check on a candidate protocol design graph. |
| `GET` | `/api/v1/kernel/search` | KernelSearch | Finds kernel objects in the caller's tenant by code or title. |
| `POST` | `/api/v1/kernel/simulation/simulate` | KernelSimulateProtocol | Evaluates a candidate USDM protocol version in memory without database changes. |
| `GET` | `/api/v1/kernel/status` | KernelStatus | Returns the Clinical Kernel API's operational status. |
| `GET` | `/api/v1/kernel/studies` | KernelListStudies | Lists visible studies in the caller's tenant. |
| `POST` | `/api/v1/kernel/studies` | KernelCreateStudy | Registers a new study in the caller's tenant with a signed reason for change (study.register). |
| `GET` | `/api/v1/kernel/studies/{studyRef}/exports` | KernelExportStudySnapshot | Streams a bitemporal study snapshot as CSV, CDISC ODM v1.3.2 XML, CDISC Dataset-JSON, SAS bundle ZIP, R bundle ZIP, or Study Archive ZIP. |
| `GET` | `/api/v1/kernel/studies/{studyRef}/locks` | KernelListStudyLocks | Lists Frozen and Locked assertions recorded for a study at an optional bitemporal as_of cut. |
| `GET` | `/api/v1/kernel/studies/{studyRef}/locks/readiness` | KernelGetStudyLockReadiness | Evaluates lock readiness (open queries, missing required items, and unresolved Error validation findings) for a study or child lock scope. |
| `GET` | `/api/v1/kernel/studies/{studyRef}/proposals` | KernelListProposals | A study's proposals, newest first, optionally only those with one status (Open, Approved, Rejected, Withdrawn, Expired). |
| `POST` | `/api/v1/kernel/studies/{studyRef}/protocol-imports` | KernelImportProtocolDraft | Synchronously imports ODM v1.3.2 XML, USDM v4 JSON, or bundled eCRF templates into a protocol draft. |
| `GET` | `/api/v1/kernel/studies/{studyRef}/protocol-versions` | KernelListProtocolVersions | A study's protocol versions, newest first, without their content. |
| `GET` | `/api/v1/kernel/studies/{studyRef}/rule-packs` | KernelListStudyRulePacks | Lists rule-pack library items in the tenant and indicates which versions are pinned on the study. |
| `POST` | `/api/v1/kernel/studies/{studyRef}/rule-packs/{itemRef}/preview` | KernelPreviewStudyRulePack | Evaluates a draft or approved rule pack in memory over the study's current collection records without mutating state. |
| `PUT` | `/api/v1/kernel/studies/{studyRef}/standards` | KernelDeclareStudyRulePacks | Declares or updates the approved rule-pack library items pinned on a study (study.declare_standards). |
| `GET` | `/api/v1/kernel/studies/{studyRef}/validation/findings` | KernelListStudyValidationFindings | Lists derived validation findings for a study, optionally filtered by stage, status, severity, rule pack or dataset. |
| `GET` | `/api/v1/kernel/validation/findings/{findingRef}` | KernelGetValidationFinding | Returns one derived validation finding and its structured provenance explanation. |
| `POST` | `/api/v1/kernel/validation/findings/{findingRef}/accept` | KernelAcceptValidationFinding | Accepts an open or investigating validation finding with a Responsibility electronic signature (validation_finding.accept). |
| `POST` | `/api/v1/kernel/validation/findings/{findingRef}/classify` | KernelClassifyValidationFinding | Classifies a detected validation finding (validation_finding.classify). |
| `POST` | `/api/v1/kernel/validation/findings/{findingRef}/explain` | KernelExplainValidationFinding | Explains an open or investigating validation finding with an Approved electronic signature (validation_finding.explain). |
| `POST` | `/api/v1/kernel/validation/findings/{findingRef}/investigate` | KernelInvestigateValidationFinding | Marks an open validation finding as under investigation (validation_finding.investigate). |
| `POST` | `/api/v1/kernel/validation/findings/{findingRef}/reopen` | KernelReopenValidationFinding | Withdraws an investigation, acceptance or explanation on a validation finding (validation_finding.reopen). |
| `GET` | `/api/v1/kernel/work-queues` | KernelReadWorkQueues | Reads the caller's capability-keyed work queues in the current tenant or within a scope. |
| `POST` | `/api/v1/kernel/work-queues` | KernelDefineWorkQueue | Defines the next version of a capability-keyed work queue in the caller's tenant (work_queue.define). |
| `GET` | `/healthz` | KernelHealthCheck | Clinical Kernel API health probe. |
