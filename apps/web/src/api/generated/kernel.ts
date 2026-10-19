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
  "/api/v1/kernel/protocols/generate-proposal": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Generates a complete USDM protocol proposal from a trial brief with reasoning lineage. */
    post: operations["KernelGenerateProtocolProposal"];
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
    /** An observation with its revisions, its lock and its kernel audit entries. */
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
  "/api/v1/kernel/observations/{observationRef}/lock": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Locks an observation so no correction commits after it (observation.lock). */
    post: operations["KernelLockObservation"];
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
    Kernel_ActivityLocation: number;
    Kernel_ActivityTiming: {
      /** Format: int32 */
      day: number | string;
      /**
       * Format: int32
       * @default 0
       */
      window_before: number | string;
      /**
       * Format: int32
       * @default 0
       */
      window_after: number | string;
    };
    Kernel_AnonymousTypeOfstringAndstring: {
      status: null | string;
      service: null | string;
    };
    Kernel_Arm: {
      id: string;
      name: string;
      type: components["schemas"]["Kernel_ArmType"];
      intervention_ids: string[];
    };
    Kernel_ArmType: number;
    Kernel_CandidateKind: number;
    Kernel_CandidateSummary: {
      kind: components["schemas"]["Kernel_CandidateKind"];
      version: null | components["schemas"]["Kernel_NodeRef"];
      base_version: null | components["schemas"]["Kernel_NodeRef"];
      content_hash: null | string;
      base_content_hash: null | string;
    };
    Kernel_CodeSystemVersionId: unknown;
    Kernel_Cohort: {
      id: string;
      name: string;
      arm_ids: string[];
      description?: null | string;
    };
    Kernel_CollectionItem: {
      id: string;
      concept: components["schemas"]["Kernel_ConceptRef"];
      unit?: null | string;
      terminology?: null | components["schemas"]["Kernel_TerminologyBinding"];
      /** @default false */
      required: boolean;
      purpose?: null | components["schemas"]["Kernel_DataPurpose"];
    };
    Kernel_CollectionSpec: {
      id: string;
      name: string;
      items: components["schemas"]["Kernel_CollectionItem"][];
      purpose?: null | components["schemas"]["Kernel_DataPurpose"];
    };
    Kernel_ConceptId: unknown;
    Kernel_ConceptRef: {
      concept_id: components["schemas"]["Kernel_ConceptId"];
      /** Format: int32 */
      version: number | string;
    };
    Kernel_CriterionCategory: number;
    Kernel_DataPurpose: {
      endpoint_ids?: string[];
      estimand_ids?: string[];
      safety_rule_ids?: string[];
      operational_need?: null | string;
    };
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
    Kernel_DesignReference: {
      id: string;
      title: string;
      artifact?: unknown;
    };
    Kernel_Dose: {
      /** Format: double */
      amount: number | string;
      unit: string;
    };
    Kernel_ElementChange: {
      element_id: string;
      instance_type: null | string;
      change: components["schemas"]["Kernel_ElementChangeKind"];
      properties: string[];
    };
    Kernel_ElementChangeKind: number;
    Kernel_EligibilityCriterion: {
      id: string;
      category: components["schemas"]["Kernel_CriterionCategory"];
      text: string;
      concepts: components["schemas"]["Kernel_ConceptRef"][];
      expression?: null | string;
    };
    Kernel_Endpoint: {
      id: string;
      objective_id: string;
      level: components["schemas"]["Kernel_ObjectiveLevel"];
      text: string;
      concept?: null | components["schemas"]["Kernel_ConceptRef"];
    };
    Kernel_EngineVersion: {
      name: string;
      version: string;
    };
    Kernel_Epoch: {
      id: string;
      name: string;
      /** Format: int32 */
      order: number | string;
    };
    Kernel_Estimand: {
      id: string;
      endpoint_id: string;
      population_id: string;
      arm_ids: string[];
      variable: string;
      intercurrent_events: components["schemas"]["Kernel_IntercurrentEvent"][];
      summary_measure: string;
      intervention_ids?: null | string[];
    };
    Kernel_GenerateProtocolProposalRequest: {
      trial_brief: string;
    };
    Kernel_GenerativeProtocolProposal: {
      design: components["schemas"]["Kernel_ProtocolDesign"];
      design_json: string;
      reasoning_chain: components["schemas"]["Kernel_ProtocolReasoningStep"][];
      initial_gap_check: components["schemas"]["Kernel_MatrixGapFinding"][];
    };
    Kernel_IntercurrentEvent: {
      description: string;
      strategy: components["schemas"]["Kernel_IntercurrentEventStrategy"];
      strategy_text?: null | string;
    };
    Kernel_IntercurrentEventStrategy: number;
    Kernel_Intervention: {
      id: string;
      name: string;
      product?: null | components["schemas"]["Kernel_ConceptRef"];
      dose?: null | components["schemas"]["Kernel_Dose"];
      route?: null | string;
      regimen?: null | string;
    };
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
      value: null | string;
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
    Kernel_KernelLockObservationRequest: {
      enrollment_ref: string;
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
    Kernel_KernelObservationLockResponse: {
      observation_ref: string;
      /** Format: date-time */
      locked_at: string;
      locked_by: string;
    };
    Kernel_KernelObservationResponse: {
      ref: string;
      enrollment_ref: string;
      concept: string;
      value: string;
      /** Format: date-time */
      observed_at: string;
      /** Format: int64 */
      version: number | string;
      locked: null | components["schemas"]["Kernel_ObservationLockView"];
      revisions: components["schemas"]["Kernel_ObservationRevisionView"][];
      audit: components["schemas"]["Kernel_ObservationAuditView"][];
    };
    Kernel_KernelObservationStateResponse: {
      ref: string;
      enrollment_ref: string;
      value: string;
      /** Format: int64 */
      version: number | string;
      /** Format: int32 */
      revision: number | string;
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
      value: null | string;
      /** Format: date-time */
      observed_at: string;
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
    Kernel_Objective: {
      id: string;
      level: components["schemas"]["Kernel_ObjectiveLevel"];
      text: string;
    };
    Kernel_ObjectiveLevel: number;
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
    Kernel_ObservationLockView: {
      /** Format: date-time */
      locked_at: string;
      locked_by: string;
      reason_for_change: string;
    };
    Kernel_ObservationRevisionView: {
      /** Format: int32 */
      number: number | string;
      value: string;
      reason_for_change: string;
      recorded_by: string;
      /** Format: date-time */
      recorded_at: string;
      /** Format: date-time */
      recorded_until: null | string;
    };
    Kernel_PlannedActivity: {
      id: string;
      name: string;
      epoch_id: string;
      locations: components["schemas"]["Kernel_ActivityLocation"][];
      collection_spec_ids: string[];
      timing?: null | components["schemas"]["Kernel_ActivityTiming"];
      purpose?: null | components["schemas"]["Kernel_DataPurpose"];
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
    Kernel_Population: {
      id: string;
      name: string;
      criterion_ids: string[];
      description?: null | string;
    };
    Kernel_ProblemDetails: {
      type?: null | string;
      title?: null | string;
      /** Format: int32 */
      status?: null | number | string;
      detail?: null | string;
      instance?: null | string;
    };
    Kernel_ProtocolDesign: {
      /** Format: int32 */
      schema_version?: number | string;
      title?: string;
      objectives?: components["schemas"]["Kernel_Objective"][];
      endpoints?: components["schemas"]["Kernel_Endpoint"][];
      estimands?: components["schemas"]["Kernel_Estimand"][];
      populations?: components["schemas"]["Kernel_Population"][];
      eligibility_criteria?: components["schemas"]["Kernel_EligibilityCriterion"][];
      arms?: components["schemas"]["Kernel_Arm"][];
      epochs?: components["schemas"]["Kernel_Epoch"][];
      cohorts?: components["schemas"]["Kernel_Cohort"][];
      interventions?: components["schemas"]["Kernel_Intervention"][];
      planned_activities?: components["schemas"]["Kernel_PlannedActivity"][];
      collection_specs?: components["schemas"]["Kernel_CollectionSpec"][];
      safety_rules?: components["schemas"]["Kernel_ProtocolRule"][];
      discontinuation_rules?: components["schemas"]["Kernel_ProtocolRule"][];
      randomization_scheme?:
        null | components["schemas"]["Kernel_DesignReference"];
      statistical_design?:
        null | components["schemas"]["Kernel_DesignReference"];
    };
    Kernel_ProtocolReasoningStep: {
      /** Format: int32 */
      step_number: number | string;
      step_type: string;
      summary: string;
      rationale: string;
      citations: string[];
    };
    Kernel_ProtocolRule: {
      id: string;
      text: string;
      concepts: components["schemas"]["Kernel_ConceptRef"][];
      expression?: null | string;
    };
    Kernel_ScheduleChange: {
      activity_id: string;
      change: components["schemas"]["Kernel_ElementChangeKind"];
      before: null | components["schemas"]["Kernel_PlannedVisit"];
      after: null | components["schemas"]["Kernel_PlannedVisit"];
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
    Kernel_TerminologyBinding: {
      code_system_version_id: components["schemas"]["Kernel_CodeSystemVersionId"];
      codelist?: null | string;
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
  KernelGenerateProtocolProposal: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_GenerateProtocolProposalRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_GenerativeProtocolProposal"];
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
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`. */
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
  KernelLockObservation: {
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
        "application/json": components["schemas"]["Kernel_KernelLockObservationRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelObservationLockResponse"];
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
      /** @description Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Observation.AlreadyLocked`. */
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
}
