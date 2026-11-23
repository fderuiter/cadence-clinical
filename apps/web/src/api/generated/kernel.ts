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
  "/api/v1/kernel/studies/{studyRef}/exports": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * Streams a bitemporal study snapshot as CSV, CDISC ODM v1.3.2 XML, or CDISC Dataset-JSON.
     * @description Projects canonical kernel observations at as_of(valid_at, known_at), applying lock_filter and masking unauthorized sites or blinded treatment values in place (K-TRUTH-1, K-AUTH-4).
     */
    get: operations["KernelExportStudySnapshot"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/enrollments/{enrollmentRef}/casebook": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * Streams a participant casebook with revisions, queries, verifications, attestations and SHA-256 digests.
     * @description Streams JSON, CDISC ODM v1.3.2 XML, or printable HTML for one enrollment at as_of(valid_at, known_at), masking unauthorized or blinded content under K-AUTH-4.
     */
    get: operations["KernelExportEnrollmentCasebook"];
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
  "/api/v1/kernel/studies": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Lists visible studies in the caller's tenant. */
    get: operations["KernelListStudies"];
    put?: never;
    /** Registers a new study in the caller's tenant with a signed reason for change (study.register). */
    post: operations["KernelCreateStudy"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/organizations": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Lists visible organizations in the caller's tenant. */
    get: operations["KernelListOrganizations"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/protocol-templates": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Lists bundled standard CDISC Library eCRF templates available for protocol draft import. */
    get: operations["KernelListProtocolTemplates"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/protocol-versions/{versionRef}/design-summary": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Returns a viewable summary of a protocol version's forms, ordered items, and import notes. */
    get: operations["KernelGetProtocolDesignSummary"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/studies/{studyRef}/protocol-imports": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Synchronously imports ODM v1.3.2 XML, USDM v4 JSON, or bundled eCRF templates into a protocol draft. */
    post: operations["KernelImportProtocolDraft"];
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
  "/api/v1/kernel/enrollments/by-subject": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Resolves a study code and subject code to its kernel enrollment state when visible to the caller (K-AUTH-4). */
    get: operations["KernelGetEnrollmentBySubject"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/enrollments/screen": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Registers a pseudonymous participant when needed and starts a screening enrollment at a study site (participant.screen). */
    post: operations["KernelScreenEnrollment"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/enrollments/status": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Transitions an enrollment along EnrollmentTransitions and allocates a gap-free study enrollment number on entering Enrolled (enrollment.change_status). */
    post: operations["KernelChangeEnrollmentStatus"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/enrollments/withdraw": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Verifies a 60-second single-use X-Sig-Token, records a Withdrawn attestation over the enrollment's current content hash, and withdraws the participant (participant.withdraw). */
    post: operations["KernelWithdrawEnrollment"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/locks": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Applies a Frozen or Locked assertion to a study, study site, enrollment, form instance or observation (lock.apply). */
    post: operations["KernelApplyLock"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/locks/{lockRef}/release": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Releases a Frozen or Locked assertion with a signed retracting assertion (lock.release). */
    post: operations["KernelReleaseLock"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/studies/{studyRef}/locks": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Lists Frozen and Locked assertions recorded for a study at an optional bitemporal as_of cut. */
    get: operations["KernelListStudyLocks"];
    put?: never;
    post?: never;
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
  "/api/v1/kernel/studies/{studyRef}/rule-packs": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Lists rule-pack library items in the tenant and indicates which versions are pinned on the study. */
    get: operations["KernelListStudyRulePacks"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/studies/{studyRef}/rule-packs/{itemRef}/preview": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Evaluates a draft or approved rule pack in memory over the study's current collection records without mutating state. */
    post: operations["KernelPreviewStudyRulePack"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/library-items/{itemRef}/approve": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Approves a draft library item version with an Approved attestation (library_item.approve). */
    post: operations["KernelApproveLibraryItem"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/studies/{studyRef}/standards": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    /** Declares or updates the approved rule-pack library items pinned on a study (study.declare_standards). */
    put: operations["KernelDeclareStudyRulePacks"];
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/studies/{studyRef}/validation/findings": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Lists derived validation findings for a study, optionally filtered by stage, status, severity, rule pack or dataset. */
    get: operations["KernelListStudyValidationFindings"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/validation/findings/{findingRef}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Returns one derived validation finding and its structured provenance explanation. */
    get: operations["KernelGetValidationFinding"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/entitlements": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * The caller's tenant commercial subscription tier, trial state, and enabled product families.
     * @description Requires an enrolled TenantMembership and an active Grant visible via GrantNodeVisibility. BillingConfirmation is masked unless the caller holds billing permission. Commercial packaging boundary: never grants clinical study access or bypasses Grant, blinding, or consent.
     */
    get: operations["KernelGetEntitlements"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/entitlements/posture": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * Minimal internal tenant commercial posture for the API gateway.
     * @description Restricted to svc:gateway-entitlements. Returns read-only state, enabled product families, and quantitative scoping caps while omitting BillingConfirmation.
     */
    get: operations["KernelGetEntitlementPosture"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/entitlements/tier": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /**
     * Switches the tenant's commercial evaluation tier in place while preserving trial countdown and clinical records.
     * @description Requires caller Idempotency-Key and signed X-Change-Reason headers. Updates enabled product families on an active Trialing or Active subscription in one atomic transaction without activating billing or extending the original 30-day trial window.
     */
    post: operations["KernelUpgradeSubscriptionTier"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/entitlements/activate": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /**
     * Activates a Trialing or Expired commercial subscription after validating study scoping and a verified quote token.
     * @description Requires caller Idempotency-Key and signed X-Change-Reason headers, study scoping parameters, and a verified cryptographic commercial quote token (or CommercialOperators.BillingOperator) before transitioning a Trialing or Expired workspace to Active in one atomic transaction.
     */
    post: operations["KernelActivateSubscription"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/onboarding/trial": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /**
     * Provisions an isolated self-service trial workspace for an unbound authenticated OIDC caller.
     * @description Mapped outside KernelTenantFilter for signed-in callers without an active tenant_id claim, study/site scope, or clinical realm roles. Requires caller Idempotency-Key and signed X-Change-Reason headers.
     */
    post: operations["KernelBootstrapTrialWorkspace"];
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
  "/api/v1/execution/forms/{formId}/preview": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Evaluates pinned study collection rules in memory over draft form values without mutating state (K-RULE-3). */
    post: operations["ExecutionPreviewFormValues"];
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
    Kernel_EvaluatedCondition: {
      code: string;
      description: string;
      met: boolean;
      evidence: unknown[];
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
    Kernel_Explanation: {
      subject: null | components["schemas"]["Kernel_NodeRef"];
      claim: string;
      producer: string;
      steps: components["schemas"]["Kernel_ExplanationStep"][];
      conditions: components["schemas"]["Kernel_EvaluatedCondition"][];
      next_actions: components["schemas"]["Kernel_NextAction"][];
      unmet_conditions?:
        null | components["schemas"]["Kernel_EvaluatedCondition"][];
    };
    Kernel_ExplanationStep: {
      claim: string;
      evidence: unknown[];
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
    Kernel_FormPreviewResponse: {
      /** Format: uuid */
      form_id: string;
      warnings: components["schemas"]["Kernel_FormPreviewWarningResponse"][];
    };
    Kernel_FormPreviewWarningResponse: {
      pack_key: string;
      rule_id: string;
      /** Format: int32 */
      rule_version: number | string;
      severity: string;
      dataset: string;
      item_id: string;
      message: string;
      remediation_guidance?: null | string;
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
      /** @default true */
      can_sign: boolean;
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
      /**
       * Format: int32
       * @default 0
       */
      missing_required_count: number | string;
      /**
       * Format: int32
       * @default 0
       */
      open_query_count: number | string;
      /**
       * Format: int32
       * @default 0
       */
      answered_query_count: number | string;
      /**
       * Format: int32
       * @default 0
       */
      verified_count: number | string;
      /**
       * Format: int32
       * @default 0
       */
      unverified_count: number | string;
      /** @default false */
      locked: boolean;
      /** @default false */
      frozen: boolean;
      /**
       * Format: int32
       * @default 0
       */
      lock_count: number | string;
      /** @default false */
      signed: boolean;
      /** @default false */
      signature_superseded: boolean;
      /**
       * Format: int32
       * @default 0
       */
      signature_count: number | string;
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
    Kernel_KernelActivateSubscriptionRequest: {
      tier: string;
      study_phase?: null | string;
      /** Format: int32 */
      study_duration_months?: null | number | string;
      countries?: null | string[];
      /** Format: int32 */
      site_count?: null | number | string;
      /** Format: int32 */
      participant_count?: null | number | string;
      /** Format: int32 */
      active_study_count?: null | number | string;
      billing_confirmation?: null | string;
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
    Kernel_KernelApplyLockRequest: {
      study_ref: string;
      scope_ref: string;
      kind: string;
      meaning?: null | string;
      /** Format: uuid */
      lock_id?: null | string;
      /** Format: uuid */
      attestation_id?: null | string;
    };
    Kernel_KernelApproveLibraryItemRequest: {
      meaning?: null | string;
      /** Format: uuid */
      attestation_id?: null | string;
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
    Kernel_KernelChangeEnrollmentStatusRequest: {
      enrollment_ref?: null | string;
      study_code?: null | string;
      subject_code?: null | string;
      /** @default  */
      to_status: string;
      /** Format: date-time */
      effective_at?: null | string;
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
    Kernel_KernelCreateStudyRequest: {
      study_code: string;
      title: string;
      phase: string;
      sponsor_ref?: null | string;
      sponsor_code?: null | string;
      sponsor_name?: null | string;
    };
    Kernel_KernelDecisionResponse: {
      capability: string;
      verb_class: string;
      resource: string;
      permitted: boolean;
      reason: null | string;
      explanation: components["schemas"]["Kernel_KernelExplanationResponse"];
    };
    Kernel_KernelDeclareStudyRulePacksRequest: {
      rule_pack_refs: string[];
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
    Kernel_KernelEnrollmentStateResponse: {
      ref: string;
      /** Format: uuid */
      enrollment_id: string;
      /** Format: uuid */
      participant_id: string;
      /** Format: uuid */
      study_id: string;
      /** Format: uuid */
      study_site_id: string;
      subject_code: string;
      enrollment_status: string;
      /** Format: date-time */
      status_since: string;
      /** Format: int32 */
      enrollment_number: null | number | string;
      content_hash: string;
    };
    Kernel_KernelEntitlementPostureResponse: {
      is_read_only: boolean;
      product_families: string[];
      scoping?:
        null | components["schemas"]["Kernel_KernelPostureScopingResponse"];
    };
    Kernel_KernelEntitlementsResponse: {
      tenant_ref: string;
      /** Format: uuid */
      tenant_id: string;
      subscription_ref: null | string;
      tier: string;
      status: string;
      /** Format: date-time */
      started_at: null | string;
      /** Format: date-time */
      trial_ends_at: null | string;
      is_read_only: boolean;
      product_families: string[];
      baseline_capabilities: string[];
      environment_notice: string;
      scoping?:
        | null
        | components["schemas"]["Kernel_KernelSubscriptionScopingResponse"];
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
    Kernel_KernelFindingObservedValueResponse: {
      variable: string;
      value: null | string;
    };
    Kernel_KernelImportedRulePackSummaryResponse: {
      ref: string;
      key: string;
      /** Format: int32 */
      version: number | string;
      title: string;
      /** Format: int32 */
      rule_count: number | string;
      status: string;
    };
    Kernel_KernelImportProtocolJsonRequest: {
      format?: null | string;
      file_name?: null | string;
      content?: null | string;
      template_ids?: null | string[];
      /** @default false */
      replace_forms: boolean;
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
    Kernel_KernelLockResponse: {
      ref: null | string;
      study_ref: string;
      scope_ref: null | string;
      scope_kind: string;
      subject_code: null | string;
      kind: string;
      attestation_ref: null | string;
      attestation_meaning: null | string;
      /** Format: date-time */
      locked_at: null | string;
      locked_by: null | string;
      reason_for_change: null | string;
      released: boolean;
      release_ref: null | string;
      /** Format: date-time */
      released_at: null | string;
      released_by: null | string;
      release_reason: null | string;
      masked: boolean;
      mask: null | string;
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
      form_ref?: null | string;
      item_id?: null | string;
      expected_value_kind?: null | string;
      expected_unit?: null | string;
      /** Format: uuid */
      expected_code_system_version_id?: null | string;
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
    Kernel_KernelOrganizationSummaryResponse: {
      ref: string;
      code: string;
      name: string;
      kind: string;
    };
    Kernel_KernelPostureScopingResponse: {
      study_phase: string;
      /** Format: int32 */
      study_duration_months: number | string;
      countries: string[];
      /** Format: int32 */
      site_count: number | string;
      /** Format: int32 */
      participant_count: number | string;
      /** Format: int32 */
      active_study_count: number | string;
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
    Kernel_KernelProtocolDraftSummaryResponse: {
      version: components["schemas"]["Kernel_KernelProtocolVersionResponse"];
      title: string;
      /** Format: int32 */
      form_count: number | string;
      /** Format: int32 */
      item_count: number | string;
      forms: components["schemas"]["Kernel_KernelProtocolFormSummaryResponse"][];
      import_notes: components["schemas"]["Kernel_KernelProtocolImportNoteResponse"][];
      imported_rule_pack?:
        | null
        | components["schemas"]["Kernel_KernelImportedRulePackSummaryResponse"];
    };
    Kernel_KernelProtocolFormItemResponse: {
      id: string;
      /** Format: int32 */
      display_order: number | string;
      prompt: string;
      concept: string;
      required: boolean;
      value_kind: null | string;
      unit: null | string;
    };
    Kernel_KernelProtocolFormSummaryResponse: {
      id: string;
      /** Format: int32 */
      display_order: number | string;
      name: string;
      /** Format: int32 */
      item_count: number | string;
      /** Format: int32 */
      required_item_count: number | string;
      items: components["schemas"]["Kernel_KernelProtocolFormItemResponse"][];
    };
    Kernel_KernelProtocolImportNoteResponse: {
      source: string;
      message: string;
    };
    Kernel_KernelProtocolTemplateResponse: {
      id: string;
      category: string;
      form_oid: string;
      form_name: string;
      file_name: string;
      /** Format: int32 */
      item_count: number | string;
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
    Kernel_KernelReleaseLockRequest: {
      study_ref?: null | string;
      meaning?: null | string;
      /** Format: uuid */
      assertion_id?: null | string;
      /** Format: uuid */
      attestation_id?: null | string;
    };
    Kernel_KernelRetractAssertionRequest: {
      enrollment_ref: string;
      /** Format: uuid */
      assertion_id?: null | string;
    };
    Kernel_KernelRulePackPreviewResponse: {
      item_ref: string;
      /** Format: int32 */
      forms_evaluated: number | string;
      /** Format: int32 */
      rules_evaluated: number | string;
      /** Format: int32 */
      violation_count: number | string;
      violations: components["schemas"]["Kernel_KernelRulePackPreviewViolationResponse"][];
    };
    Kernel_KernelRulePackPreviewViolationResponse: {
      /** Format: uuid */
      form_id: string;
      pack_key: string;
      rule_id: string;
      /** Format: int32 */
      rule_version: number | string;
      severity: string;
      dataset: string;
      item_id: null | string;
      message: string;
      remediation_guidance: null | string;
    };
    Kernel_KernelRulePackResponse: {
      ref: string;
      key: string;
      /** Format: int32 */
      version: number | string;
      title: string;
      description: null | string;
      status: string;
      pinned: boolean;
      content_hash: string;
      /** Format: int32 */
      rule_count: number | string;
      rules: components["schemas"]["Kernel_KernelRuleSummaryResponse"][];
    };
    Kernel_KernelRuleSummaryResponse: {
      id: string;
      /** Format: int32 */
      version: number | string;
      stage: string;
      dataset: null | string;
      severity: string;
      explanation: string;
      remediation_guidance: null | string;
      condition?: null | string;
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
    Kernel_KernelScreenEnrollmentRequest: {
      /** Format: uuid */
      participant_id: null | string;
      /** Format: uuid */
      enrollment_id: null | string;
      study_code: string;
      site_code: string;
      subject_code: string;
      /** Format: date-time */
      screened_at: string;
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
    Kernel_KernelStudyLocksResponse: {
      study_ref: string;
      study_code: string;
      /** Format: date-time */
      valid_at: string;
      /** Format: date-time */
      known_at: string;
      locks: components["schemas"]["Kernel_KernelLockResponse"][];
    };
    Kernel_KernelStudyRulePacksResponse: {
      study_ref: string;
      profile_ref: null | string;
      pinned_rule_pack_refs: string[];
      rule_packs: components["schemas"]["Kernel_KernelRulePackResponse"][];
    };
    Kernel_KernelStudySummaryResponse: {
      ref: string;
      code: string;
      title: string;
      phase: string;
      sponsor_ref: string;
      sponsor_code: string;
      sponsor_name: string;
      /** Format: int32 */
      site_count: number | string;
    };
    Kernel_KernelSubscriptionScopingResponse: {
      study_phase: string;
      /** Format: int32 */
      study_duration_months: number | string;
      countries: string[];
      /** Format: int32 */
      site_count: number | string;
      /** Format: int32 */
      participant_count: number | string;
      /** Format: int32 */
      active_study_count: number | string;
      billing_confirmation?: null | string;
    };
    Kernel_KernelTrialOnboardingRequest: {
      organization_name: string;
      residency?: null | string;
      tier?: null | string;
    };
    Kernel_KernelTrialOnboardingResponse: {
      newly_provisioned: boolean;
      tenant_code: string;
      tenant_ref: string;
      organization_ref: string;
      person_ref: string;
      membership_ref: string;
      admin_role_ref: string;
      admin_grant_ref: string;
      assigned_roles: string[];
      token_refresh_required: boolean;
      entitlements: components["schemas"]["Kernel_KernelEntitlementsResponse"];
    };
    Kernel_KernelUpgradeSubscriptionRequest: {
      tier: string;
    };
    Kernel_KernelValidationFindingDetailResponse: {
      finding: components["schemas"]["Kernel_KernelValidationFindingResponse"];
      explanation: components["schemas"]["Kernel_Explanation"];
    };
    Kernel_KernelValidationFindingPageResponse: {
      /** Format: int32 */
      total: number | string;
      findings: components["schemas"]["Kernel_KernelValidationFindingResponse"][];
    };
    Kernel_KernelValidationFindingResponse: {
      ref: string;
      study_ref: string;
      stage: string;
      pack_key: string;
      rule_pack_ref: string;
      rule_id: string;
      /** Format: int32 */
      rule_version: number | string;
      severity: string;
      status: string;
      blocks: boolean;
      target_kind: string;
      dataset: string;
      record_key: string;
      variable: null | string;
      message: string;
      expected: null | string;
      observed: components["schemas"]["Kernel_KernelFindingObservedValueResponse"][];
      first_detected_run_ref: string;
      last_detected_run_ref: string;
      resolved_by_run_ref: null | string;
      classification: null | string;
    };
    Kernel_KernelVerifyObservationRequest: {
      enrollment_ref: string;
      /** Format: int32 */
      revision: number | string;
      /** Format: uuid */
      assertion_id?: null | string;
    };
    Kernel_KernelWithdrawEnrollmentRequest: {
      enrollment_ref?: null | string;
      study_code?: null | string;
      subject_code?: null | string;
      /** @default  */
      withdrawal_kind: string;
      /** Format: date-time */
      effective_at?: null | string;
      /** Format: uuid */
      attestation_id?: null | string;
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
    Kernel_NextAction: {
      description: string;
      capability?: null | string;
      target?: unknown;
    };
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
    Kernel_PreviewFormItemValueRequest: {
      item_id: string;
      value: null | components["schemas"]["Kernel_ObservationValue"];
    };
    Kernel_PreviewFormValuesRequest: {
      values: components["schemas"]["Kernel_PreviewFormItemValueRequest"][];
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
  KernelExportStudySnapshot: {
    parameters: {
      query?: {
        format?: string;
        lock_filter?: string;
        as_of?: string;
        valid_at?: string;
        known_at?: string;
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
        content?: never;
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
  KernelExportEnrollmentCasebook: {
    parameters: {
      query?: {
        format?: string;
        as_of?: string;
        valid_at?: string;
        known_at?: string;
      };
      header?: never;
      path: {
        enrollmentRef: string;
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
        content?: never;
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
  KernelListStudies: {
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
          "application/json": components["schemas"]["Kernel_KernelStudySummaryResponse"][];
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
  KernelCreateStudy: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: {
      content: {
        "application/json":
          null | components["schemas"]["Kernel_KernelCreateStudyRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelStudySummaryResponse"];
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Study.SponsorUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Study.StudyCodeTaken`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Study.CodeRequired`, `K.Study.TitleRequired`. */
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
  KernelListOrganizations: {
    parameters: {
      query?: {
        kind?: string;
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
          "application/json": components["schemas"]["Kernel_KernelOrganizationSummaryResponse"][];
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
  KernelListProtocolTemplates: {
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
          "application/json": components["schemas"]["Kernel_KernelProtocolTemplateResponse"][];
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
  KernelGetProtocolDesignSummary: {
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
          "application/json": components["schemas"]["Kernel_KernelProtocolDraftSummaryResponse"];
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
  KernelImportProtocolDraft: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        studyRef: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_KernelImportProtocolJsonRequest"];
        "multipart/form-data": components["schemas"]["Kernel_KernelImportProtocolJsonRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelProtocolDraftSummaryResponse"];
        };
      };
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelProtocolDraftSummaryResponse"];
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Aggregate.NotFound`, `K.Protocol.StudyUnknown`, `K.Standards.StandardVersionUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Protocol.FirstVersionExists`, `K.Protocol.InvalidTransition`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Payload Too Large */
      413: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Protocol.AmendmentInvalid`, `K.Protocol.AmendmentNotAllowed`, `K.Protocol.ContentInvalid`, `K.Protocol.ContentRequired`, `K.Standards.LibraryItemInvalid`. */
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
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
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Observation.ParticipantScreenFailed`, `K.Observation.ParticipantWithdrawn`. */
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
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
      /** @description Unprocessable Entity. Kernel errors: `K.Collection.ValuesInvalid`, `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Observation.ValueInvalid`, `K.Observation.ValueUnchanged`. */
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
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
  KernelGetEnrollmentBySubject: {
    parameters: {
      query?: {
        study_code?: string;
        subject_code?: string;
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
          "application/json": components["schemas"]["Kernel_KernelEnrollmentStateResponse"];
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
  KernelScreenEnrollment: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_KernelScreenEnrollmentRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelEnrollmentStateResponse"];
        };
      };
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelEnrollmentStateResponse"];
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Execution.ParticipantUnknown`, `K.Execution.StudySiteUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Execution.AlreadyActiveAtSite`, `K.Execution.SubjectCodeTaken`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Execution.SubjectCodeInvalid`, `K.Execution.TransitionTimeInvalid`. */
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
  KernelChangeEnrollmentStatus: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_KernelChangeEnrollmentStatusRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelEnrollmentStateResponse"];
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
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
      /** @description Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Execution.InvalidTransition`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Execution.TransitionTimeInvalid`, `K.Execution.WithdrawalRequiresCommand`. */
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
  KernelWithdrawEnrollment: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_KernelWithdrawEnrollmentRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelEnrollmentStateResponse"];
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`, `K.Trust.PersonRequired`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Aggregate.NotFound`, `K.Trust.TargetUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Execution.InvalidTransition`, `K.Trust.TargetContentChanged`, `K.Trust.TargetNotSignable`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Execution.AttestationRejected`, `K.Execution.TransitionTimeInvalid`, `K.Execution.WithdrawalKindInvalid`, `K.Trust.MethodRequired`, `K.Trust.TargetKindUnsupported`. */
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
  KernelApplyLock: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_KernelApplyLockRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelLockResponse"];
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`, `K.Trust.PersonRequired`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Assertion.ScopeUnknown`, `K.Trust.TargetUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Assertion.AlreadyLocked`, `K.Command.IdempotencyConflict`, `K.Trust.TargetContentChanged`, `K.Trust.TargetNotSignable`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Assertion.AttestationRejected`, `K.Assertion.ScopeInvalid`, `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Trust.MethodRequired`, `K.Trust.TargetKindUnsupported`. */
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
  KernelReleaseLock: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        lockRef: string;
      };
      cookie?: never;
    };
    requestBody?: {
      content: {
        "application/json":
          null | components["schemas"]["Kernel_KernelReleaseLockRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelLockResponse"];
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`, `K.Trust.PersonRequired`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Aggregate.NotFound`, `K.Trust.TargetUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Assertion.AlreadyRetracted`, `K.Command.IdempotencyConflict`, `K.Trust.TargetContentChanged`, `K.Trust.TargetNotSignable`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Assertion.AttestationRejected`, `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Trust.MethodRequired`, `K.Trust.TargetKindUnsupported`. */
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
  KernelListStudyLocks: {
    parameters: {
      query?: {
        as_of?: string;
        valid_at?: string;
        known_at?: string;
        active_only?: boolean;
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
          "application/json": components["schemas"]["Kernel_KernelStudyLocksResponse"];
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
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
  KernelListStudyRulePacks: {
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
          "application/json": components["schemas"]["Kernel_KernelStudyRulePacksResponse"];
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
  KernelPreviewStudyRulePack: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        studyRef: string;
        itemRef: string;
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
          "application/json": components["schemas"]["Kernel_KernelRulePackPreviewResponse"];
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
      /** @description Not Found. Kernel errors: `K.Aggregate.NotFound`, `K.Standards.LibraryItemUnknown`. */
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
  KernelApproveLibraryItem: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        itemRef: string;
      };
      cookie?: never;
    };
    requestBody?: {
      content: {
        "application/json":
          | null
          | components["schemas"]["Kernel_KernelApproveLibraryItemRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelRulePackResponse"];
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Standards.AttestationRejected`, `K.Subscription.Expired`, `K.Trust.PersonRequired`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Standards.LibraryItemUnknown`, `K.Trust.TargetUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Standards.InvalidTransition`, `K.Trust.TargetContentChanged`, `K.Trust.TargetNotSignable`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Trust.MethodRequired`, `K.Trust.TargetKindUnsupported`. */
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
  KernelDeclareStudyRulePacks: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        studyRef: string;
      };
      cookie?: never;
    };
    requestBody?: {
      content: {
        "application/json":
          | null
          | components["schemas"]["Kernel_KernelDeclareStudyRulePacksRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelStudyRulePacksResponse"];
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Standards.LibraryItemUnknown`, `K.Standards.StudyUnknown`. */
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
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Standards.DeclarationInvalid`, `K.Standards.NotApproved`. */
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
  KernelListStudyValidationFindings: {
    parameters: {
      query?: {
        stage?: string;
        status?: string;
        severity?: string;
        dataset?: string;
        pack_key?: string;
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
          "application/json": components["schemas"]["Kernel_KernelValidationFindingPageResponse"];
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
  KernelGetValidationFinding: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        findingRef: string;
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
          "application/json": components["schemas"]["Kernel_KernelValidationFindingDetailResponse"];
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
  KernelGetEntitlements: {
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
          "application/json": components["schemas"]["Kernel_KernelEntitlementsResponse"];
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
  KernelGetEntitlementPosture: {
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
          "application/json": components["schemas"]["Kernel_KernelEntitlementPostureResponse"];
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
  KernelUpgradeSubscriptionTier: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_KernelUpgradeSubscriptionRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelEntitlementsResponse"];
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Commercial.SubscriptionUnknown`. */
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
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Commercial.EntitlementRequired`, `K.Commercial.SubscriptionRequired`, `K.Commercial.TierInvalid`. */
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
  KernelActivateSubscription: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_KernelActivateSubscriptionRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelEntitlementsResponse"];
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Commercial.QuoteInvalid`, `K.Subscription.Expired`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Commercial.SubscriptionUnknown`. */
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
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Commercial.EntitlementRequired`, `K.Commercial.ScopingInvalid`, `K.Commercial.SubscriptionRequired`, `K.Commercial.TierInvalid`. */
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
  KernelBootstrapTrialWorkspace: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: {
      content: {
        "application/json":
          null | components["schemas"]["Kernel_KernelTrialOnboardingRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelTrialOnboardingResponse"];
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Commercial.OnboardingCallerInvalid`, `K.Subscription.Expired`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Identity.TenantUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Authority.RoleCodeTaken`, `K.Command.IdempotencyConflict`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Authority.CapabilityInvalid`, `K.Authority.GrantTargetInvalid`, `K.Authority.RoleCodeInvalid`, `K.Authority.RoleNameInvalid`, `K.Authority.ScopeInvalid`, `K.Authority.ValidityInvalid`, `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Commercial.EntitlementRequired`, `K.Commercial.SubscriptionRequired`, `K.Commercial.TierInvalid`, `K.Commercial.TrialWindowInvalid`, `K.Identity.CodeRequired`, `K.Identity.MembershipRequired`, `K.Identity.NameRequired`, `K.Identity.PersonRequired`, `K.Identity.ResidencyInvalid`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Bad Gateway. Kernel errors: `K.Commercial.IdentityProviderSyncFailed`. */
      502: {
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
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
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
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
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Collection.NoGoverningVersion`, `K.Command.IdempotencyConflict`, `K.Observation.ParticipantScreenFailed`, `K.Observation.ParticipantWithdrawn`. */
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
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
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Collection.RevisionStale`, `K.Command.IdempotencyConflict`, `K.Observation.ParticipantScreenFailed`, `K.Observation.ParticipantWithdrawn`. */
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
  ExecutionPreviewFormValues: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        formId: string;
      };
      cookie?: never;
    };
    requestBody?: {
      content: {
        "application/json":
          null | components["schemas"]["Kernel_PreviewFormValuesRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_FormPreviewResponse"];
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`, `K.Trust.PersonRequired`. */
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
