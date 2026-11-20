/**
 * Generated from docs/openapi/kernel_openapi.json by scripts/generate-api-types.js.
 * Do not edit; run `pnpm --filter web generate:api` after regenerating the spec.
 */

export interface paths {
  "/healthz": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Clinical Kernel API health probe. */
    get: operations["KernelHealthCheck"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/status": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Returns the Clinical Kernel API's operational status. */
    get: operations["KernelStatus"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/nodes/{nodeRef}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * Resolves a NodeRef to its summary.
     * @description Title, code and state of one kernel object in the caller's tenant, at its current version.
     */
    get: operations["KernelGetNode"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/nodes/{nodeRef}/path": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * The zoom path from the tenant down to an object.
     * @description Presentation only: the path never grants or implies authority (spec §9.3).
     */
    get: operations["KernelGetZoomPath"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/nodes/{nodeRef}/children": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * The children of a scope node, with their own child counts.
     * @description A node the caller may not see is refused; a child the caller may not see is masked in place (ADR-2471).
     */
    get: operations["KernelGetChildren"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/nodes/{nodeRef}/commands": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * The kernel commands offered on an object, each with the caller's dry-run decision.
     * @description Each decision is the one sending the command would get now (ADR-2483), explained (ADR-2482).
     */
    get: operations["KernelGetCommands"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/authorization/can-i": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * Whether the caller may exercise a capability on an object now, and why.
     * @description A dry run: it executes nothing and is recorded in the authorization access log, not the audit chain (ADR-2483).
     */
    get: operations["KernelCanI"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/search": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * Finds kernel objects in the caller's tenant by code or title.
     * @description Each word matches as a prefix; an exact code ranks first. Sources that reveal identity are searched only when the caller holds the capability (ADR-2484).
     */
    get: operations["KernelSearch"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/studies/{studyRef}/protocol-versions": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * A study's protocol versions, newest first, without their content.
     * @description A study the caller may not see is refused; a version the caller may not see is masked in place (ADR-2471).
     */
    get: operations["KernelListProtocolVersions"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/protocol-versions/{versionRef}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** One protocol version, without its content. */
    get: operations["KernelGetProtocolVersion"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/protocol-versions/{versionRef}/schedule-of-activities": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * A protocol version's Schedule of Activities, compiled from its design.
     * @description Planned activities as columns grouped by epoch, collection specifications as rows (ADR-2475). A version the caller may not see is refused.
     */
    get: operations["KernelGetScheduleOfActivities"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/nodes/{nodeRef}/lineage": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * Walks an object's lineage backward to its inputs or forward to its dependents.
     * @description direction is Backward (default) or Forward; depth 1 to 50 (default 10); edge_kind repeats (for example DerivedFrom); valid_at and known_at read the graph as it was.
     */
    get: operations["KernelTrace"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/nodes/{nodeRef}/dependents": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * What depends on an object, grouped by kind with counts.
     * @description Masked objects are counted like any other, so a count never depends on the reader. depth 1 to 50 (default 10); edge_kind repeats; valid_at and known_at read the graph as it was.
     */
    get: operations["KernelDependents"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/protocol-versions/{versionRef}/impact": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** What depends on the version a protocol amendment changes (K-PROT-2). */
    get: operations["KernelAmendmentImpact"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/activity": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * What happened in a scope, newest first.
     * @description scope is any NodeRef (the whole tenant when absent); family repeats (Knowledge, Execution, Evidence, Trust, Quality, Intelligence); actor filters by who did it; from is the earliest recorded time; before is the previous page's next cursor; limit 1 to 200 (default 50). Entries about objects you may not see are masked in place.
     */
    get: operations["KernelActivity"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/simulation/simulate": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Evaluates a candidate USDM protocol version in memory without database changes. */
    post: operations["KernelSimulateProtocol"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/protocols/matrix-gap-check": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Performs immediate real-time matrix gap check on a candidate protocol design graph. */
    post: operations["KernelMatrixGapCheck"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/observations/{observationRef}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** An observation with its revisions, the locks covering it, the assertions about it and its kernel audit entries. */
    get: operations["KernelGetObservation"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/observations": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Records an observation for the participant of an enrollment (observation.record). */
    post: operations["KernelRecordObservation"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/observations/{observationRef}/corrections": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Corrects an observation's value with the signed reason for change (observation.correct). */
    post: operations["KernelCorrectObservation"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/observations/{observationRef}/verifications": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Verifies the current revision of an observation against source with the signed reason for change (observation.verify). */
    post: operations["KernelVerifyObservation"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/assertions/{assertionRef}/retraction": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Retracts a verification, answer or query assertion with the signed reason for change (assertion.retract). */
    post: operations["KernelRetractAssertion"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/studies/{studyRef}/proposals": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * A study's proposals, newest first, optionally only those with one status (Open, Approved, Rejected, Withdrawn, Expired).
     * @description A study the caller may not see is refused; a proposal the caller may not see is masked in place (ADR-2471).
     */
    get: operations["KernelListProposals"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/proposals/{proposalRef}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** One proposal with its intent, plan, affected objects, requested capabilities, model, content hash and decision. */
    get: operations["KernelGetProposal"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/proposals/{proposalRef}/rejection": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Rejects an open proposal; the signed reason for change is the rejection reason (proposal.reject). */
    post: operations["KernelRejectProposal"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ai/intent/route": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Match a command-palette query against fixed patterns and return the navigation targets the caller's roles allow. */
    post: operations["RouteAiIntent"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ai/runs": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** The caller's latest agent runs from the interaction ledger, newest first. */
    get: operations["AiListMyRuns"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ai/runs/{runId}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** One of the caller's agent runs with its request, answer, usage and integrity hash. */
    get: operations["AiGetMyRun"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ai/tasks": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** The agent task configuration versions in this deployment, with their content hashes and the tenant's qualification of each. */
    get: operations["AiListTasks"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ai/evaluation-runs": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Stores an evaluation run record after checking it against its task configuration version, set, criteria and case results (evaluation_run.record, ADR-2634). */
    post: operations["AiRecordEvaluationRun"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ai/evaluation-runs/{runId}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** One stored evaluation run with its record, exactly as hashed. */
    get: operations["AiGetEvaluationRun"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ai/tasks/{key}/evaluation-runs": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** The stored evaluation runs of a task, newest first, optionally of one version. */
    get: operations["AiListEvaluationRuns"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ai/tasks/{key}/versions/{version}/qualification": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Qualifies a task configuration version on a stored, accepted evaluation run and the caller's signed approval (task_configuration.qualify, ADR-2631). */
    post: operations["AiQualifyTask"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ai/tasks/{key}/versions/{version}/suspension": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Takes a qualified task configuration version out of use until it is qualified again (task_configuration.suspend). */
    post: operations["AiSuspendTask"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ai/controls": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** The tenant's AI kill switches and monthly token budgets. */
    get: operations["AiListControls"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ai/controls/{scope}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    /** Switches agents on or off for every task ('*') or one task, and sets the monthly token budget (agent_control.set). */
    put: operations["AiSetControl"];
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ai/biometrics/verify-liveness": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Tests video frame payloads for anti-spoofing in temporary memory and returns signed liveness tokens. */
    post: operations["AiVerifyLiveness"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Lists the forms opened for an enrollment, oldest first. */
    get: operations["ExecutionListEnrollmentForms"];
    put?: never;
    /** Opens a form for a participant's visit under the protocol version in force (collection.open). */
    post: operations["ExecutionOpenForm"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms/{formId}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Reads a form: its items in layout order with their values, completeness, locks, assertions and signatures. */
    get: operations["ExecutionGetForm"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms/{formId}/values": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    /** Saves a page of values: all of them or, on one stale revision or invalid value, none (collection.record). */
    put: operations["ExecutionSaveFormValues"];
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms/queries": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Lists the queries on every form of an enrollment or across visible enrollments of a study, oldest first. */
    get: operations["ExecutionListEnrollmentQueries"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms/{formId}/queries": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Lists a form's queries, oldest first, each with whether its value changed since it was raised. */
    get: operations["ExecutionListFormQueries"];
    put?: never;
    /** Raises a query on the current revision of one of the form's values or on a blank item (execution.query.raise). */
    post: operations["ExecutionRaiseFormQuery"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms/{formId}/queries/{queryId}/answer": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Answers an open query (execution.query.answer). */
    post: operations["ExecutionAnswerFormQuery"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms/{formId}/queries/{queryId}/reopen": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Reopens an answered or closed query with why it is not settled (execution.query.manage). */
    post: operations["ExecutionReopenFormQuery"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms/{formId}/queries/{queryId}/close": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Closes an open or answered query; the change reason is the closing note (execution.query.manage). */
    post: operations["ExecutionCloseFormQuery"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms/{formId}/queries/{queryId}/cancel": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Cancels an open or answered query raised in error (execution.query.manage). */
    post: operations["ExecutionCancelFormQuery"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms/{formId}/signatures": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Signs the form's current values with an X-Sig-Token step-up token (collection.sign); 409 when the form changed since it was read. */
    post: operations["ExecutionSignForm"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
}
export type webhooks = Record<string, never>;
export interface components {
  schemas: {
    Kernel_AcceptanceCriteria: {
      /** Format: double */
      minimum_pass_rate: number | string;
      /** Format: double */
      minimum_reviewed_pass_rate: number | string;
      maximum_mean_latency?: null | string;
      /** Format: double */
      maximum_mean_cost?: null | number | string;
    };
    Kernel_AgentControlRequest: {
      enabled: boolean;
      /** Format: int64 */
      monthly_token_limit: null | number | string;
    };
    Kernel_AgentControlResponse: {
      scope: string;
      enabled: boolean;
      /** Format: int64 */
      monthly_token_limit: null | number | string;
      /** Format: int64 */
      version_index: number | string;
    };
    Kernel_AgentRunResponse: {
      summary: components["schemas"]["Kernel_AgentRunSummaryResponse"];
      provider: string;
      configuration_hash: string;
      request_json: string;
      response_text: null | string;
      failure_reason: null | string;
      content_hash: null | string;
      content_hash_verified: boolean;
    };
    Kernel_AgentRunSummaryResponse: {
      /** Format: uuid */
      run_id: string;
      task_key: string;
      /** Format: int32 */
      task_version: number | string;
      model: string;
      status: string;
      /** Format: date-time */
      started_at: string;
      /** Format: date-time */
      completed_at: null | string;
      /** Format: int64 */
      input_tokens: number | string;
      /** Format: int64 */
      output_tokens: number | string;
      /** Format: double */
      cost_usd: null | number | string;
    };
    Kernel_AgentTaskResponse: {
      key: string;
      /** Format: int32 */
      version: number | string;
      provider: string;
      model: string;
      autonomy_level: string;
      content_hash: string;
      tools: string[];
      retrieval_sources: string[];
      evaluation_set: null | components["schemas"]["Kernel_EvaluationSetRef"];
      acceptance_criteria:
        null | components["schemas"]["Kernel_AcceptanceCriteria"];
      risk_class: null | string;
      qualification_gaps: string[];
      qualification:
        null | components["schemas"]["Kernel_TaskQualificationResponse"];
    };
    Kernel_AnonymousTypeOfstringAndstring: {
      status: null | string;
      service: null | string;
    };
    Kernel_AssertionId: unknown;
    /** @enum {unknown} */
    Kernel_AssertionKind:
      | "Verified"
      | "Reviewed"
      | "Queried"
      | "QueryAnswered"
      | "CodedAs"
      | "Adjudicated"
      | "Frozen"
      | "Locked";
    Kernel_AssertionView: {
      assertion_id: components["schemas"]["Kernel_AssertionId"];
      kind: components["schemas"]["Kernel_AssertionKind"];
      target: components["schemas"]["Kernel_NodeRef"];
      /** Format: int32 */
      revision: null | number | string;
      text: null | string;
      asserted_by: string;
      /** Format: date-time */
      asserted_at: string;
      reason_for_change: string;
      retracted_by: null | components["schemas"]["Kernel_AssertionId"];
      /** Format: date-time */
      retracted_at: null | string;
    };
    Kernel_AttestationId: unknown;
    Kernel_CandidateKind: number;
    Kernel_CandidateSummary: {
      kind: components["schemas"]["Kernel_CandidateKind"];
      version: null | components["schemas"]["Kernel_NodeRef"];
      base_version: null | components["schemas"]["Kernel_NodeRef"];
      content_hash: null | string;
      base_content_hash: null | string;
    };
    Kernel_CodeSystemVersionId: unknown;
    /** @enum {unknown} */
    Kernel_DateTimePrecision:
      "Year" | "Month" | "Day" | "Minute" | "Second" | null;
    Kernel_DependentGroup: {
      kind: string;
      /** Format: int32 */
      count: number | string;
      objects: components["schemas"]["Kernel_LineageNode"][];
    };
    Kernel_DependentsReport: {
      start: string;
      /** Format: int32 */
      depth: number | string;
      /** Format: date-time */
      valid_at: string;
      /** Format: date-time */
      known_at: string;
      groups: components["schemas"]["Kernel_DependentGroup"][];
      /** Format: int32 */
      total: number | string;
      truncated: boolean;
    };
    Kernel_ElementChange: {
      element_id: string;
      instance_type: null | string;
      change: components["schemas"]["Kernel_ElementChangeKind"];
      properties: string[];
    };
    Kernel_ElementChangeKind: number;
    Kernel_EngineVersion: {
      name: string;
      version: string;
    };
    Kernel_EvaluationRunResponse: {
      run: components["schemas"]["Kernel_EvaluationRunSummaryResponse"];
      record_json: string;
    };
    Kernel_EvaluationRunStateResponse: {
      /** Format: uuid */
      run_id: string;
      task_key: string;
      /** Format: int32 */
      task_version: number | string;
      configuration_hash: string;
      evaluation_set_hash: string;
      record_hash: string;
      accepted: boolean;
      reasons: string[];
    };
    Kernel_EvaluationRunSummaryResponse: {
      /** Format: uuid */
      run_id: string;
      task_key: string;
      /** Format: int32 */
      task_version: number | string;
      configuration_hash: string;
      evaluation_set_id: string;
      /** Format: int32 */
      evaluation_set_version: number | string;
      evaluation_set_hash: string;
      provider: string;
      model: string;
      accepted: boolean;
      /** Format: date-time */
      started_at: string;
      /** Format: date-time */
      completed_at: string;
      record_hash: string;
    };
    Kernel_EvaluationSetRef: {
      set_id: string;
      /** Format: int32 */
      version: number | string;
      content_hash: string;
    };
    Kernel_FormAssertionResponse: {
      /** Format: uuid */
      assertion_id: string;
      kind: string;
      target: string;
      /** Format: int32 */
      revision: null | number | string;
      text: null | string;
      asserted_by: string;
      /** Format: date-time */
      asserted_at: string;
      reason: string;
      /** Format: date-time */
      retracted_at: null | string;
    };
    Kernel_FormItemResponse: {
      item_id: string;
      concept: null | string;
      unit: null | string;
      value_kind: null | string;
      required: boolean;
      value: null | components["schemas"]["Kernel_ObservationValue"];
      /** Format: uuid */
      observation_id: null | string;
      /** Format: int32 */
      revision: number | string;
      recorded_by: null | string;
      /** Format: date-time */
      recorded_at: null | string;
    };
    Kernel_FormLockResponse: {
      /** Format: uuid */
      lock_id: string;
      kind: string;
      scope: string;
      /** Format: date-time */
      locked_at: string;
      locked_by: string;
      reason: string;
    };
    Kernel_FormQueryMessageResponse: {
      kind: string;
      text: string;
      by: string;
      /** Format: date-time */
      at: string;
    };
    Kernel_FormQueryResponse: {
      /** Format: uuid */
      query_id: string;
      /** Format: uuid */
      form_id: string;
      /** Format: uuid */
      enrollment_id: string;
      item_id: string;
      /** Format: uuid */
      observation_id: string;
      /** Format: int32 */
      revision: number | string;
      /** Format: int32 */
      current_revision: null | number | string;
      value_changed: boolean;
      status: string;
      text: string;
      messages: components["schemas"]["Kernel_FormQueryMessageResponse"][];
      raised_by: string;
      /** Format: date-time */
      raised_at: string;
      /** Format: int64 */
      version: number | string;
      /** Format: uuid */
      study_id?: null | string;
      /** Format: uuid */
      study_site_id?: null | string;
      site_code?: null | string;
      subject_code?: null | string;
      collection_spec_id?: null | string;
      planned_activity_id?: null | string;
    };
    Kernel_FormQueryTextRequest: {
      text: string;
    };
    Kernel_FormResponse: {
      /** Format: uuid */
      form_id: string;
      /** Format: uuid */
      enrollment_id: string;
      /** Format: uuid */
      participant_id: string;
      /** Format: uuid */
      study_id: string;
      /** Format: uuid */
      study_site_id: string;
      /** Format: uuid */
      protocol_version_id: string;
      collection_spec_id: string;
      planned_activity_id: null | string;
      /** Format: date-time */
      collected_at: string;
      opened_by: string;
      /** Format: date-time */
      opened_at: string;
      /** Format: int64 */
      version: number | string;
      complete: boolean;
      missing_required: string[];
      items: components["schemas"]["Kernel_FormItemResponse"][];
      locks: components["schemas"]["Kernel_FormLockResponse"][];
      assertions: components["schemas"]["Kernel_FormAssertionResponse"][];
      signatures: components["schemas"]["Kernel_FormSignatureResponse"][];
      content_hash: null | string;
      collection_title?: null | string;
    };
    Kernel_FormSavedResponse: {
      /** Format: uuid */
      form_id: string;
      /** Format: int64 */
      version: number | string;
      values: components["schemas"]["Kernel_FormSavedValueResponse"][];
    };
    Kernel_FormSavedValueResponse: {
      item_id: string;
      /** Format: uuid */
      observation_id: string;
      value: components["schemas"]["Kernel_ObservationValue"];
      /** Format: int32 */
      revision: number | string;
      changed: boolean;
    };
    Kernel_FormSignatureResponse: {
      /** Format: uuid */
      attestation_id: string;
      meaning: string;
      signed_by: string;
      /** Format: date-time */
      signed_at: string;
      reason: string;
      content_hash: string;
      superseded: boolean;
      signed_values:
        null | components["schemas"]["Kernel_FormSignedValueResponse"][];
    };
    Kernel_FormSignedValueResponse: {
      item_id: string;
      /** Format: uuid */
      observation_id: string;
      /** Format: int32 */
      revision: number | string;
      value: components["schemas"]["Kernel_ObservationValue"];
    };
    Kernel_FormStateResponse: {
      /** Format: uuid */
      form_id: string;
      /** Format: uuid */
      enrollment_id: string;
      /** Format: uuid */
      protocol_version_id: string;
      collection_spec_id: string;
      planned_activity_id: null | string;
      /** Format: date-time */
      collected_at: string;
      /** Format: int64 */
      version: number | string;
    };
    Kernel_FormSummaryResponse: {
      /** Format: uuid */
      form_id: string;
      /** Format: uuid */
      enrollment_id: string;
      /** Format: uuid */
      participant_id: string;
      /** Format: uuid */
      study_id: string;
      /** Format: uuid */
      study_site_id: string;
      /** Format: uuid */
      protocol_version_id: string;
      collection_spec_id: string;
      planned_activity_id: null | string;
      /** Format: date-time */
      collected_at: string;
      opened_by: string;
      /** Format: date-time */
      opened_at: string;
      /** Format: int64 */
      version: number | string;
      complete: boolean;
      /** Format: int32 */
      item_count: number | string;
      /** Format: int32 */
      recorded_count: number | string;
      collection_title?: null | string;
    };
    Kernel_FormValueRequest: {
      item_id: string;
      value: null | components["schemas"]["Kernel_ObservationValue"];
      /** Format: int32 */
      expected_revision: number | string;
      /** Format: uuid */
      observation_id: null | string;
      /** Format: date-time */
      observed_at: null | string;
    };
    Kernel_IntentRecommendation: {
      id: string;
      name: string;
      description: string;
      category: string;
      icon: string;
      target_route: string;
      action: null | string;
      /** Format: double */
      confidence: number | string;
      roles: string[];
    };
    Kernel_IntentRouteRequest: {
      query: string;
      context?: null | {
        [key: string]: string;
      };
    };
    Kernel_IntentRouteResponse: {
      query: string;
      recommendations: components["schemas"]["Kernel_IntentRecommendation"][];
    };
    Kernel_JsonElement: unknown;
    Kernel_KernelActivityEntryResponse: {
      /** Format: uuid */
      event_id: string;
      type: string;
      family: string;
      /** Format: date-time */
      recorded: string;
      /** Format: date-time */
      occurred_valid: null | string;
      actor_id: string;
      subject_kind: null | string;
      subject: null | string;
      mask: null | string;
      /** Format: uuid */
      correlation_id: string;
    };
    Kernel_KernelActivityPageResponse: {
      entries: components["schemas"]["Kernel_KernelActivityEntryResponse"][];
      next: null | string;
    };
    Kernel_KernelAmendmentImpactResponse: {
      amended: string;
      parent: string;
      impacted_elements: string[];
      dependents: components["schemas"]["Kernel_KernelDependentsResponse"];
      changed_elements: components["schemas"]["Kernel_KernelElementChangeResponse"][];
      schedule:
        null | components["schemas"]["Kernel_KernelSoaAmendmentDiffResponse"];
    };
    Kernel_KernelAssertionStateResponse: {
      ref: string;
      kind: string;
      target: string;
      /** Format: int32 */
      revision: null | number | string;
      retracts: null | string;
      /** Format: date-time */
      asserted_at: string;
      asserted_by: string;
    };
    Kernel_KernelChildResponse: {
      node: components["schemas"]["Kernel_KernelNodeResponse"];
      /** Format: int32 */
      child_count: null | number | string;
      group: null | string;
    };
    Kernel_KernelCommandResponse: {
      command: string;
      decision: components["schemas"]["Kernel_KernelDecisionResponse"];
    };
    Kernel_KernelConditionResponse: {
      code: string;
      description: string;
      met: boolean;
      evidence: string[];
    };
    Kernel_KernelCorrectObservationRequest: {
      enrollment_ref: string;
      value: null | components["schemas"]["Kernel_ObservationValue"];
      /** Format: int64 */
      expected_version: number | string;
    };
    Kernel_KernelDecisionResponse: {
      capability: string;
      verb_class: string;
      resource: string;
      permitted: boolean;
      reason: null | string;
      explanation: components["schemas"]["Kernel_KernelExplanationResponse"];
    };
    Kernel_KernelDependentGroupResponse: {
      kind: string;
      /** Format: int32 */
      count: number | string;
      objects: components["schemas"]["Kernel_KernelLineageNodeResponse"][];
    };
    Kernel_KernelDependentsResponse: {
      start: string;
      /** Format: int32 */
      depth: number | string;
      /** Format: date-time */
      valid_at: string;
      /** Format: date-time */
      known_at: string;
      groups: components["schemas"]["Kernel_KernelDependentGroupResponse"][];
      /** Format: int32 */
      total: number | string;
      truncated: boolean;
    };
    Kernel_KernelElementChangeResponse: {
      element_id: string;
      instance_type: null | string;
      change: string;
      properties: string[];
    };
    Kernel_KernelExplanationResponse: {
      subject: null | string;
      claim: string;
      producer: string;
      steps: components["schemas"]["Kernel_KernelExplanationStepResponse"][];
      conditions: components["schemas"]["Kernel_KernelConditionResponse"][];
      next_actions: components["schemas"]["Kernel_KernelNextActionResponse"][];
    };
    Kernel_KernelExplanationStepResponse: {
      claim: string;
      evidence: string[];
    };
    Kernel_KernelLineageEdgeResponse: {
      from: string;
      to: string;
      kind: string;
      /** Format: uuid */
      activity: null | string;
      /** Format: date-time */
      recorded_at: string;
      /** Format: date-time */
      valid_from: null | string;
    };
    Kernel_KernelLineageNodeResponse: {
      handle: string;
      kind: string;
      ref: null | string;
      mask: null | string;
      /** Format: int32 */
      distance: number | string;
    };
    Kernel_KernelLineageResponse: {
      start: string;
      direction: string;
      /** Format: int32 */
      depth: number | string;
      /** Format: date-time */
      valid_at: string;
      /** Format: date-time */
      known_at: string;
      nodes: components["schemas"]["Kernel_KernelLineageNodeResponse"][];
      edges: components["schemas"]["Kernel_KernelLineageEdgeResponse"][];
      truncated: boolean;
    };
    Kernel_KernelNextActionResponse: {
      description: string;
      capability: null | string;
      target: null | string;
    };
    Kernel_KernelNodeResponse: {
      ref: null | string;
      kind: string;
      title: null | string;
      code: null | string;
      status: null | string;
      mask: null | string;
    };
    Kernel_KernelObservationResponse: {
      ref: string;
      enrollment_ref: string;
      concept: string;
      value: components["schemas"]["Kernel_ObservationValue"];
      /** Format: date-time */
      observed_at: string;
      /** Format: int64 */
      version: number | string;
      locks: components["schemas"]["Kernel_LockView"][];
      revisions: components["schemas"]["Kernel_ObservationRevisionView"][];
      assertions: components["schemas"]["Kernel_AssertionView"][];
      audit: components["schemas"]["Kernel_ObservationAuditView"][];
    };
    Kernel_KernelObservationStateResponse: {
      ref: string;
      enrollment_ref: string;
      value: components["schemas"]["Kernel_ObservationValue"];
      /** Format: int64 */
      version: number | string;
      /** Format: int32 */
      revision: number | string;
    };
    Kernel_KernelProposalAffectedResponse: {
      ref: string;
      mask: null | string;
    };
    Kernel_KernelProposalPlanResponse: {
      ref: string;
      title: null | string;
      /** Format: int32 */
      number: null | number | string;
      media_type: null | string;
      content_hash: null | string;
      mask: null | string;
    };
    Kernel_KernelProposalResponse: {
      ref: null | string;
      study: null | string;
      intent: null | string;
      plan: null | components["schemas"]["Kernel_KernelProposalPlanResponse"];
      affected:
        null | components["schemas"]["Kernel_KernelProposalAffectedResponse"][];
      simulated_impact: null | string;
      requested_capabilities: null | string[];
      /** Format: date-time */
      proposed_at: null | string;
      /** Format: date-time */
      expires_at: null | string;
      proposed_by: null | string;
      proposed_by_kind: null | string;
      requested_by: null | string;
      model_id: null | string;
      /** Format: double */
      confidence_score: null | number | string;
      content_hash: null | string;
      status: null | string;
      /** Format: date-time */
      decided_at: null | string;
      decided_by: null | string;
      approval_attestation: null | string;
      decision_reason: null | string;
      is_escalated: null | boolean;
      escalated_to: null | string;
      escalation_reason: null | string;
      mask: null | string;
    };
    Kernel_KernelProposalStateResponse: {
      ref: string;
      status: string;
      content_hash: string;
      /** Format: date-time */
      decided_at: null | string;
      decided_by: null | string;
    };
    Kernel_KernelProtocolVersionResponse: {
      ref: null | string;
      study: null | string;
      /** Format: int32 */
      number: null | number | string;
      parent_version: null | string;
      status: null | string;
      content_hash: null | string;
      amendment_intent: null | string;
      approval_attestation: null | string;
      withdrawal_reason: null | string;
      mask: null | string;
    };
    Kernel_KernelRecordObservationRequest: {
      /** Format: uuid */
      observation_id: null | string;
      enrollment_ref: string;
      concept: string;
      value: null | components["schemas"]["Kernel_ObservationValue"];
      /** Format: date-time */
      observed_at: string;
    };
    Kernel_KernelRetractAssertionRequest: {
      enrollment_ref: string;
      /** Format: uuid */
      assertion_id?: null | string;
    };
    Kernel_KernelScheduleOfActivitiesResponse: {
      version: string;
      content_hash: null | string;
      hash: string;
      title: string;
      epochs: components["schemas"]["Kernel_KernelSoaEpochResponse"][];
      columns: components["schemas"]["Kernel_KernelSoaColumnResponse"][];
      rows: components["schemas"]["Kernel_KernelSoaRowResponse"][];
      footnotes: components["schemas"]["Kernel_KernelSoaFootnoteResponse"][];
    };
    Kernel_KernelSearchHitResponse: {
      ref: string;
      kind: string;
      title: string;
      code: null | string;
      detail: null | string;
      exact_code: boolean;
      /** Format: double */
      rank: number | string;
    };
    Kernel_KernelSoaAmendmentDiffResponse: {
      directives: components["schemas"]["Kernel_KernelSoaDirectiveResponse"][];
      impacted_elements: string[];
      impacted_concepts: string[];
      undeclared: string[];
      changes_schedule: boolean;
      changes_eligibility: boolean;
    };
    Kernel_KernelSoaColumnResponse: {
      activity_id: string;
      name: string;
      epoch_id: string;
      /** Format: int32 */
      study_day: null | number | string;
      /** Format: int32 */
      window_before: number | string;
      /** Format: int32 */
      window_after: number | string;
      locations: string[];
      footnotes: (number | string)[];
    };
    Kernel_KernelSoaDirectiveResponse: {
      kind: string;
      element_id: string;
      impacted_elements: string[];
      impacted_concepts: string[];
      before: null | components["schemas"]["Kernel_KernelSoaVisitResponse"];
      after: null | components["schemas"]["Kernel_KernelSoaVisitResponse"];
      /** Format: int32 */
      frequency_before: null | number | string;
      /** Format: int32 */
      frequency_after: null | number | string;
      collected_at_added: null | string[];
      collected_at_removed: null | string[];
    };
    Kernel_KernelSoaEpochResponse: {
      epoch_id: string;
      name: string;
      /** Format: int32 */
      order: number | string;
      /** Format: int32 */
      first_column: number | string;
      /** Format: int32 */
      column_count: number | string;
    };
    Kernel_KernelSoaFootnoteResponse: {
      /** Format: int32 */
      number: number | string;
      kind: string;
      text: string;
    };
    Kernel_KernelSoaRowResponse: {
      collection_spec_id: string;
      name: string;
      item_ids: string[];
      concepts: string[];
      columns: (number | string)[];
      /** Format: int32 */
      frequency: number | string;
      footnotes: (number | string)[];
    };
    Kernel_KernelSoaVisitResponse: {
      epoch_id: string;
      /** Format: int32 */
      study_day: null | number | string;
      /** Format: int32 */
      window_before: number | string;
      /** Format: int32 */
      window_after: number | string;
      locations: string[];
    };
    Kernel_KernelVerifyObservationRequest: {
      enrollment_ref: string;
      /** Format: int32 */
      revision: number | string;
      /** Format: uuid */
      assertion_id?: null | string;
    };
    Kernel_KernelZoomStepResponse: {
      level: string;
      label: null | string;
      ref: null | string;
      mask: null | string;
    };
    Kernel_LineageNode: {
      handle: string;
      kind: string;
      ref: null | components["schemas"]["Kernel_NodeRef"];
      mask: null | components["schemas"]["Kernel_MaskReason"];
      /** Format: int32 */
      distance: number | string;
      is_masked?: boolean;
    };
    /** @enum {unknown} */
    Kernel_LockKind: "Frozen" | "Locked";
    Kernel_LockView: {
      lock_id: components["schemas"]["Kernel_AssertionId"];
      kind: components["schemas"]["Kernel_LockKind"];
      scope: components["schemas"]["Kernel_NodeRef"];
      attestation_id: components["schemas"]["Kernel_AttestationId"];
      /** Format: date-time */
      locked_at: string;
      locked_by: string;
      reason_for_change: string;
    };
    Kernel_MaskedPart: {
      part: string;
      reason: components["schemas"]["Kernel_MaskReason"];
    };
    Kernel_MaskReason: number;
    Kernel_MatrixGapCheckRequest: {
      protocol_content: string;
    };
    Kernel_MatrixGapFinding: {
      element_id: string;
      kind: components["schemas"]["Kernel_MatrixGapKind"];
      severity: components["schemas"]["Kernel_MatrixGapSeverity"];
      json_pointer_path: string;
      description: string;
      recommendation?: null | string;
    };
    Kernel_MatrixGapKind: number;
    Kernel_MatrixGapSeverity: number;
    Kernel_NodeRef: unknown;
    /** @enum {unknown} */
    Kernel_NullFlavor:
      | "NotDone"
      | "Unknown"
      | "AskedButUnknown"
      | "NotAsked"
      | "NotApplicable"
      | "Masked";
    Kernel_ObservationAuditView: {
      /** Format: int64 */
      sequence: number | string;
      /** Format: date-time */
      recorded_at: string;
      actor_id: string;
      action: string;
      subject: string;
      /** Format: int64 */
      version: null | number | string;
      reason_for_change: string;
    };
    Kernel_ObservationRevisionView: {
      /** Format: int32 */
      number: number | string;
      value: components["schemas"]["Kernel_ObservationValue"];
      reason_for_change: string;
      recorded_by: string;
      /** Format: date-time */
      recorded_at: string;
      /** Format: date-time */
      recorded_until: null | string;
    };
    Kernel_ObservationValue:
      | components["schemas"]["Kernel_ObservationValueQuantityValue"]
      | components["schemas"]["Kernel_ObservationValueCodedValue"]
      | components["schemas"]["Kernel_ObservationValueTextValue"]
      | components["schemas"]["Kernel_ObservationValueBooleanValue"]
      | components["schemas"]["Kernel_ObservationValueDateTimeValue"]
      | components["schemas"]["Kernel_ObservationValueMissingValue"];
    Kernel_ObservationValueBooleanValue: {
      /** @enum {string} */
      type?: "boolean";
      value: boolean;
    };
    Kernel_ObservationValueCodedValue: {
      /** @enum {string} */
      type?: "coded";
      code_system_version_id: components["schemas"]["Kernel_CodeSystemVersionId"];
      code: string;
    };
    Kernel_ObservationValueDateTimeValue: {
      /** @enum {string} */
      type?: "dateTime";
      value: string;
      precision?: components["schemas"]["Kernel_DateTimePrecision"];
    };
    Kernel_ObservationValueMissingValue: {
      /** @enum {string} */
      type?: "missing";
      reason: components["schemas"]["Kernel_NullFlavor"];
      detail?: null | string;
    };
    Kernel_ObservationValueQuantityValue: {
      /** @enum {string} */
      type?: "quantity";
      /** Format: double */
      value: number | string;
      unit: string;
      comparator?: null | components["schemas"]["Kernel_QuantityComparator"];
    };
    Kernel_ObservationValueTextValue: {
      /** @enum {string} */
      type?: "text";
      value: string;
    };
    Kernel_OpenFormRequest: {
      /** Format: uuid */
      form_id: null | string;
      /** Format: uuid */
      enrollment_id: string;
      collection_spec_id: string;
      planned_activity_id: null | string;
      /** Format: date-time */
      collected_at: string;
    };
    Kernel_PlannedVisit: {
      activity_id: string;
      name: string;
      epoch_id: string;
      /** Format: int32 */
      epoch_order: number | string;
      /** Format: int32 */
      study_day: number | string;
      /** Format: int32 */
      window_before: number | string;
      /** Format: int32 */
      window_after: number | string;
    };
    Kernel_ProblemDetails: {
      type?: null | string;
      title?: null | string;
      /** Format: int32 */
      status?: null | number | string;
      detail?: null | string;
      instance?: null | string;
    };
    Kernel_QualificationRequest: {
      configuration_hash: string;
      /** Format: uuid */
      evaluation_run_id: string;
      /** Format: uuid */
      attestation_id: string;
    };
    Kernel_QualificationResponse: {
      task_key: string;
      /** Format: int32 */
      task_version: number | string;
      configuration_hash: string;
      status: string;
      risk_class: string;
      /** Format: int64 */
      version_index: number | string;
    };
    /** @enum {unknown} */
    Kernel_QuantityComparator: "<" | "<=" | ">" | ">=" | null;
    Kernel_RaiseFormQueryRequest: {
      /** Format: uuid */
      observation_id: null | string;
      /** Format: int32 */
      revision: null | number | string;
      text: string;
      /** Format: uuid */
      query_id?: null | string;
      item_id?: null | string;
    };
    Kernel_SaveFormValuesRequest: {
      values: components["schemas"]["Kernel_FormValueRequest"][];
    };
    Kernel_ScheduleChange: {
      activity_id: string;
      change: components["schemas"]["Kernel_ElementChangeKind"];
      before: null | components["schemas"]["Kernel_PlannedVisit"];
      after: null | components["schemas"]["Kernel_PlannedVisit"];
    };
    Kernel_SignFormRequest: {
      meaning: string;
      content_hash: string;
      /** Format: uuid */
      signature_id: null | string;
    };
    Kernel_SimulateProtocolRequest: {
      base_version_ref: null | string;
      stored_version_ref: null | string;
      candidate_content: null | string;
      /** Format: date-time */
      valid_at?: null | string;
      /** Format: date-time */
      known_at?: null | string;
    };
    Kernel_SimulationAsOf: {
      /** Format: date-time */
      valid_at: string;
      /** Format: date-time */
      known_at: string;
    };
    Kernel_SimulationResult: {
      scope: components["schemas"]["Kernel_NodeRef"];
      as_of: components["schemas"]["Kernel_SimulationAsOf"];
      candidate: components["schemas"]["Kernel_CandidateSummary"];
      engine_versions: components["schemas"]["Kernel_EngineVersion"][];
      input_hash: string;
      result_hash: string;
      structural_diff: components["schemas"]["Kernel_ElementChange"][];
      schedule_changes: components["schemas"]["Kernel_ScheduleChange"][];
      masked: components["schemas"]["Kernel_MaskedPart"][];
      dependents?: null | components["schemas"]["Kernel_DependentsReport"];
      matrix_gaps?: null | components["schemas"]["Kernel_MatrixGapFinding"][];
      is_empty?: boolean;
    };
    Kernel_TaskQualificationResponse: {
      status: string;
      usable: boolean;
      configuration_hash: string;
      risk_class: string;
      evaluation_run_reference: string;
      evaluation_run_hash: string;
      /** Format: date-time */
      qualified_at: string;
      /** Format: date-time */
      suspended_at: null | string;
      suspension_reason: null | string;
    };
    Kernel_VerifyLivenessRequest: {
      subject_pseudonym: null | string;
      video_frames: null | string[];
      challenge_id: null | string;
      /** @default false */
      fallback_mode: boolean;
    };
    Kernel_VerifyLivenessResponse: {
      liveness_token: string;
      is_live: boolean;
      /** Format: double */
      anti_spoof_score: number | string;
      /** Format: date-time */
      expires_at: string;
      status: string;
    };
  };
  responses: never;
  parameters: never;
  requestBodies: never;
  headers: never;
  pathItems: never;
}
export type $defs = Record<string, never>;
export interface operations {
  KernelHealthCheck: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_AnonymousTypeOfstringAndstring"];
        };
      };
    };
  };
  KernelStatus: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_AnonymousTypeOfstringAndstring"];
        };
      };
    };
  };
  KernelGetNode: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        nodeRef: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelNodeResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Service Unavailable */
      503: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelGetZoomPath: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        nodeRef: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelZoomStepResponse"][];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Service Unavailable */
      503: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelGetChildren: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        nodeRef: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelChildResponse"][];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Service Unavailable */
      503: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelGetCommands: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        nodeRef: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelCommandResponse"][];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Service Unavailable */
      503: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelCanI: {
    parameters: {
      query: {
        capability: string;
        resource: string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelDecisionResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Service Unavailable */
      503: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelSearch: {
    parameters: {
      query: {
        q: string;
        kind?: string[];
        limit?: number | string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelSearchHitResponse"][];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Service Unavailable */
      503: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelListProtocolVersions: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        studyRef: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelProtocolVersionResponse"][];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Service Unavailable */
      503: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelGetProtocolVersion: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        versionRef: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelProtocolVersionResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Service Unavailable */
      503: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelGetScheduleOfActivities: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        versionRef: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelScheduleOfActivitiesResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Service Unavailable */
      503: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelTrace: {
    parameters: {
      query?: {
        direction?: string;
        depth?: number | string;
        edge_kind?: string[];
        valid_at?: string;
        known_at?: string;
      };
      header?: never;
      path: {
        nodeRef: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelLineageResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Service Unavailable */
      503: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelDependents: {
    parameters: {
      query?: {
        depth?: number | string;
        edge_kind?: string[];
        valid_at?: string;
        known_at?: string;
      };
      header?: never;
      path: {
        nodeRef: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelDependentsResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Service Unavailable */
      503: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelAmendmentImpact: {
    parameters: {
      query?: {
        depth?: number | string;
        valid_at?: string;
        known_at?: string;
      };
      header?: never;
      path: {
        versionRef: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelAmendmentImpactResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Service Unavailable */
      503: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelActivity: {
    parameters: {
      query?: {
        scope?: string;
        family?: string[];
        actor?: string;
        from?: string;
        before?: string;
        limit?: number | string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelActivityPageResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Service Unavailable */
      503: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelSimulateProtocol: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_SimulateProtocolRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_SimulationResult"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Simulation.VersionUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Simulation.BaseNotFrozen`, `K.Simulation.ContentInvalid`, `K.Simulation.ElementIdDuplicated`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelMatrixGapCheck: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_MatrixGapCheckRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_MatrixGapFinding"][];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelGetObservation: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        observationRef: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelObservationResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelRecordObservation: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_KernelRecordObservationRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelObservationStateResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Observation.EnrollmentUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Observation.ParticipantWithdrawn`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Observation.ObservedAtInvalid`, `K.Observation.ValueInvalid`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Locked. Kernel errors: `K.Observation.Locked`. */
      423: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelCorrectObservation: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        observationRef: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_KernelCorrectObservationRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelObservationStateResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Aggregate.NotFound`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Observation.VersionStale`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Observation.ValueInvalid`, `K.Observation.ValueUnchanged`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Locked. Kernel errors: `K.Observation.Locked`. */
      423: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelVerifyObservation: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        observationRef: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_KernelVerifyObservationRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelAssertionStateResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Aggregate.NotFound`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Assertion.RevisionStale`, `K.Command.IdempotencyConflict`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Locked. Kernel errors: `K.Assertion.Locked`. */
      423: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelRetractAssertion: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        assertionRef: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_KernelRetractAssertionRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelAssertionStateResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Aggregate.NotFound`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Assertion.AlreadyRetracted`, `K.Command.IdempotencyConflict`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Assertion.NotRetractable`, `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Locked. Kernel errors: `K.Assertion.Locked`. */
      423: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelListProposals: {
    parameters: {
      query?: {
        status?: string;
        limit?: number | string;
      };
      header?: never;
      path: {
        studyRef: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelProposalResponse"][];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelGetProposal: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        proposalRef: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelProposalResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelRejectProposal: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        proposalRef: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelProposalStateResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Aggregate.NotFound`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Proposal.Expired`, `K.Proposal.NotOpen`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  RouteAiIntent: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_IntentRouteRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_IntentRouteResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unauthorized */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  AiListMyRuns: {
    parameters: {
      query?: {
        limit?: number | string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_AgentRunSummaryResponse"][];
        };
      };
    };
  };
  AiGetMyRun: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        runId: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_AgentRunResponse"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  AiListTasks: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_AgentTaskResponse"][];
        };
      };
    };
  };
  AiRecordEvaluationRun: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_JsonElement"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_EvaluationRunStateResponse"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `M.Intelligence.Task.Unknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `M.Intelligence.Configuration.Changed`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `M.Intelligence.Configuration.NotQualifiable`, `M.Intelligence.Evaluation.RunInvalid`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  AiGetEvaluationRun: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        runId: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_EvaluationRunResponse"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  AiListEvaluationRuns: {
    parameters: {
      query?: {
        version?: number | string;
        limit?: number | string;
      };
      header?: never;
      path: {
        key: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_EvaluationRunSummaryResponse"][];
        };
      };
    };
  };
  AiQualifyTask: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        key: string;
        version: number;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_QualificationRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_QualificationResponse"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `M.Intelligence.Evaluation.RunNotFound`, `M.Intelligence.Task.Unknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `M.Intelligence.Configuration.Changed`, `M.Intelligence.Qualification.AlreadyQualified`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `M.Intelligence.Configuration.NotQualifiable`, `M.Intelligence.Qualification.AttestationRejected`, `M.Intelligence.Qualification.EvaluationNotAccepted`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  AiSuspendTask: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        key: string;
        version: number;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_QualificationResponse"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `M.Intelligence.Qualification.NotQualified`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  AiListControls: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_AgentControlResponse"][];
        };
      };
    };
  };
  AiSetControl: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        scope: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_AgentControlRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_AgentControlResponse"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  AiVerifyLiveness: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_VerifyLivenessRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_VerifyLivenessResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  ExecutionListEnrollmentForms: {
    parameters: {
      query?: {
        enrollment_id?: string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_FormSummaryResponse"][];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  ExecutionOpenForm: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_OpenFormRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_FormStateResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Collection.EnrollmentUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Collection.NoGoverningVersion`, `K.Command.IdempotencyConflict`, `K.Observation.ParticipantWithdrawn`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Collection.CollectedAtInvalid`, `K.Collection.SpecUnknown`, `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Locked. Kernel errors: `K.Observation.Locked`. */
      423: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  ExecutionGetForm: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        formId: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_FormResponse"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  ExecutionSaveFormValues: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        formId: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_SaveFormValuesRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_FormSavedResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Aggregate.NotFound`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Collection.RevisionStale`, `K.Command.IdempotencyConflict`, `K.Observation.ParticipantWithdrawn`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Collection.ValuesInvalid`, `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Observation.ObservedAtInvalid`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Locked. Kernel errors: `K.Observation.Locked`. */
      423: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  ExecutionListEnrollmentQueries: {
    parameters: {
      query?: {
        enrollment_id?: string;
        study_id?: string;
        status?: string;
        site?: string;
        participant?: string;
        value_changed?: boolean;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_FormQueryResponse"][];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  ExecutionListFormQueries: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        formId: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_FormQueryResponse"][];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  ExecutionRaiseFormQuery: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        formId: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_RaiseFormQueryRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_FormQueryResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`. Kernel errors: `K.Authorization.Denied`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Aggregate.NotFound`. Kernel errors: `M.Execution.Form.CollectionInstanceUnknown`, `M.Execution.Query.ObservationUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `M.Execution.Query.RevisionStale`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Collection.ValuesInvalid`, `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Locked. Kernel errors: `K.Assertion.Locked`. Kernel errors: `K.Assertion.Locked`. */
      423: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  ExecutionAnswerFormQuery: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        formId: string;
        queryId: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_FormQueryTextRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_FormQueryResponse"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Aggregate.NotFound`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `M.Execution.Query.InvalidTransition`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Locked. Kernel errors: `K.Assertion.Locked`. */
      423: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  ExecutionReopenFormQuery: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        formId: string;
        queryId: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_FormQueryTextRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_FormQueryResponse"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Aggregate.NotFound`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `M.Execution.Query.InvalidTransition`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Locked. Kernel errors: `K.Assertion.Locked`. */
      423: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  ExecutionCloseFormQuery: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        formId: string;
        queryId: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_FormQueryResponse"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Aggregate.NotFound`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `M.Execution.Query.InvalidTransition`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Locked. Kernel errors: `K.Assertion.Locked`. */
      423: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  ExecutionCancelFormQuery: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        formId: string;
        queryId: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_FormQueryResponse"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Aggregate.NotFound`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `M.Execution.Query.InvalidTransition`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Locked. Kernel errors: `K.Assertion.Locked`. */
      423: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  ExecutionSignForm: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        formId: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_SignFormRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_FormSignatureResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`, `K.Trust.AuthenticationStale`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Trust.PersonRequired`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Aggregate.NotFound`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Trust.TargetContentChanged`, `K.Trust.TargetNotSignable`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Collection.SignatureMeaningInvalid`, `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Trust.MethodRequired`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
}
