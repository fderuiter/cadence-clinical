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
  "/api/v1/kernel/enrollments/lookup": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * Resolves a study code, subject code, and optional site code to pinned study and enrollment references.
     * @description Reads over the tenant's unique study and subject indexes without listing sites. Returns a non-confirming 404 when the study, site, or enrollment does not exist, mismatches, or is masked for the caller (ADR-2471).
     */
    get: operations["KernelLookupEnrollment"];
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
  "/api/v1/kernel/enrollments/{enrollmentRef}/schedule-of-activities": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * An enrollment's governing Schedule of Activities and re-consent lock state.
     * @description Resolves the participant's bitemporal Applicability at validAt (defaulting to now) and returns the governing Schedule of Activities plus any pending RequiresReconsent transitions (spec §7.5, PRD-SUB-007).
     */
    get: operations["KernelGetEnrollmentScheduleOfActivities"];
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
     * Streams a bitemporal study snapshot as CSV, CDISC ODM v1.3.2 XML, CDISC Dataset-JSON, SAS bundle ZIP, R bundle ZIP, or Study Archive ZIP.
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
     * @description Streams JSON, CDISC ODM v1.3.2 XML, printable HTML, or certified PDF (%PDF-1.7) for one enrollment at as_of(valid_at, known_at), masking unauthorized or blinded content under K-AUTH-4.
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
    /** Registers a new study in the caller's tenant with a signed reason for change (study.register) and optional one-step site link. */
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
    /** Registers an organization such as a site institution or sponsor in the caller's tenant (organization.register). */
    post: operations["KernelCreateOrganization"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/sites": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Lists visible investigational sites in the caller's tenant. */
    get: operations["KernelListSites"];
    put?: never;
    /** Registers an investigational site under a site institution in the caller's tenant (site.register). */
    post: operations["KernelCreateSite"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/studies/{studyRef}/sites": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Lists visible investigational sites linked to a study together with their activation status and currently effective protocol versions. */
    get: operations["KernelListStudySites"];
    put?: never;
    /** Links an investigational site (registering it inline when site details are supplied) to a study in the caller's tenant (site.register, study_site.link). */
    post: operations["KernelLinkSiteToStudy"];
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
  "/api/v1/kernel/protocol-versions/{versionRef}/submit-for-review": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Freezes a draft protocol version's content, computes its content hash, and moves it to InReview (protocol.submit_for_review). */
    post: operations["KernelSubmitProtocolVersionForReview"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/protocol-versions/{versionRef}/return-to-draft": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Returns an InReview protocol version to Draft, clearing its content hash so it can be revised (protocol.return_to_draft). */
    post: operations["KernelReturnProtocolVersionToDraft"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/protocol-versions/{versionRef}/approve": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Records a 21 CFR Part 11 Approved attestation bound to the frozen protocol content hash and approves the protocol version (protocol.approve). */
    post: operations["KernelApproveProtocolVersion"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/studies/{studyRef}/artifacts": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Lists evidence artifacts registered for a study, newest first. */
    get: operations["KernelListStudyArtifacts"];
    put?: never;
    /** Registers an immutable study evidence artifact version such as an ethics approval, regulatory approval, or CTA acknowledgement (artifact.register). */
    post: operations["KernelRegisterStudyArtifact"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/studies/{studyRef}/effectivities": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Lists bitemporal protocol-version effectivities declared for a study. */
    get: operations["KernelListStudyEffectivities"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/protocol-versions/{versionRef}/effectivities": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Declares a Pending protocol-version effectivity for the whole study, a country, or one study site (effectivity.declare). */
    post: operations["KernelDeclareEffectivity"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/effectivities/{effectivityRef}/status": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Transitions an effectivity's status in valid time with supporting approval artifact evidence (effectivity.change_status, K-TIME-5). */
    post: operations["KernelChangeEffectivityStatus"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/study-sites/{studySiteRef}/activate": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Activates a kernel study site once at least one protocol version is effective there (study_site.activate, K-TIME-4). */
    post: operations["KernelActivateStudySite"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/effectivities": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Declares an approved protocol version's effectivity at study, country, or study-site scope (effectivity.declare). */
    post: operations["KernelDeclareScopedEffectivity"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/study-sites/activate": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Idempotently activates a linked study site resolved by study code and site code (study_site.activate). */
    post: operations["KernelActivateStudySiteByCode"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/enrollments/{enrollmentRef}/consents": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Records bitemporal participant consent at SignedAt and resolves covered pending re-consent transitions (consent.record). */
    post: operations["KernelRecordConsentStatus"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/consents/sync": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Synchronizes signed coordinator or electronic consent from Execution into Kernel bitemporal ConsentStatus and Applicability (consent.record). */
    post: operations["KernelSyncConsentStatus"];
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
  "/api/v1/kernel/studies/{studyRef}/locks/readiness": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Evaluates lock readiness (open queries, missing required items, and unresolved Error validation findings) for a study or child lock scope. */
    get: operations["KernelGetStudyLockReadiness"];
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
  "/api/v1/kernel/validation/findings/{findingRef}/investigate": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Marks an open validation finding as under investigation (validation_finding.investigate). */
    post: operations["KernelInvestigateValidationFinding"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/validation/findings/{findingRef}/classify": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Classifies a detected validation finding (validation_finding.classify). */
    post: operations["KernelClassifyValidationFinding"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/validation/findings/{findingRef}/accept": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Accepts an open or investigating validation finding with a Responsibility electronic signature (validation_finding.accept). */
    post: operations["KernelAcceptValidationFinding"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/validation/findings/{findingRef}/explain": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Explains an open or investigating validation finding with an Approved electronic signature (validation_finding.explain). */
    post: operations["KernelExplainValidationFinding"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/validation/findings/{findingRef}/reopen": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Withdraws an investigation, acceptance or explanation on a validation finding (validation_finding.reopen). */
    post: operations["KernelReopenValidationFinding"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/work-queues": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * Reads the caller's capability-keyed work queues in the current tenant or within a scope.
     * @description within is an optional NodeRef (for example study:<uuid> or study_site:<uuid>); code repeats to filter queue codes; limit is 1 to 200 (default 50). Restricted or blinded items remain in place with masked set to NotPermitted or Blinded.
     */
    get: operations["KernelReadWorkQueues"];
    put?: never;
    /** Defines the next version of a capability-keyed work queue in the caller's tenant (work_queue.define). */
    post: operations["KernelDefineWorkQueue"];
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
}
export type webhooks = Record<string, never>;
export interface components {
  schemas: {
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
    Kernel_CodelistOptionView: {
      code: string;
      display: string;
      submission_value?: null | string;
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
    Kernel_KernelAcceptFindingRequest: {
      rationale: string;
      /** Format: uuid */
      attestation_id?: null | string;
    };
    Kernel_KernelActivateStudySiteByCodeRequest: {
      study_code: string;
      site_code: string;
    };
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
    Kernel_KernelApproveProtocolVersionRequest: {
      meaning?: null | string;
      expected_content_hash?: null | string;
      /** Format: uuid */
      attestation_id?: null | string;
    };
    Kernel_KernelArtifactResponse: {
      artifact_ref: string;
      /** Format: uuid */
      artifact_id: string;
      artifact_version_ref: string;
      /** Format: uuid */
      artifact_version_id: string;
      /** Format: int32 */
      version_number: number | string;
      kind: string;
      study_ref: null | string;
      title: string;
      content_hash: string;
      /** Format: int64 */
      size_bytes: number | string;
      media_type: string;
      storage_ref: string;
      /** Format: date-time */
      recorded_at: null | string;
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
    Kernel_KernelChangeEffectivityStatusRequest: {
      status?: null | string;
      evidence_artifact_version_refs?: null | string[];
      /** Format: date-time */
      effective_at?: null | string;
      target_status?: null | string;
    };
    Kernel_KernelChildResponse: {
      node: components["schemas"]["Kernel_KernelNodeResponse"];
      /** Format: int32 */
      child_count: null | number | string;
      group: null | string;
    };
    Kernel_KernelClassifyFindingRequest: {
      classification: string;
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
    Kernel_KernelConsentScopeRequest: {
      purpose: string;
      decision: string;
      conditions?: null | string;
    };
    Kernel_KernelConsentStatusStateResponse: {
      ref: string;
      /** Format: uuid */
      consent_status_id: string;
      enrollment_ref: string;
      protocol_version_ref: string;
      /** Format: int32 */
      protocol_version_number: number | string;
      signed: boolean;
      basis_kind: string;
      /** Format: date-time */
      signed_at: string;
      governing_version_ref: null | string;
      requires_reconsent: boolean;
    };
    Kernel_KernelCorrectObservationRequest: {
      enrollment_ref: string;
      value: null | components["schemas"]["Kernel_ObservationValue"];
      /** Format: int64 */
      expected_version: number | string;
    };
    Kernel_KernelCreateOrganizationRequest: {
      name: string;
      code?: null | string;
      kind?: null | string;
    };
    Kernel_KernelCreateSiteRequest: {
      site_code: string;
      name: string;
      country: string;
      institution_ref?: null | string;
      institution_code?: null | string;
      institution_name?: null | string;
    };
    Kernel_KernelCreateStudyRequest: {
      study_code: string;
      title: string;
      phase: string;
      sponsor_ref?: null | string;
      sponsor_code?: null | string;
      sponsor_name?: null | string;
      /** @default false */
      link_sandbox_site: boolean;
      initial_site_ref?: null | string;
      initial_site_code?: null | string;
    };
    Kernel_KernelDecisionResponse: {
      capability: string;
      verb_class: string;
      resource: string;
      permitted: boolean;
      reason: null | string;
      explanation: components["schemas"]["Kernel_KernelExplanationResponse"];
    };
    Kernel_KernelDeclareEffectivityRequest: {
      scope_level?: null | string;
      country?: null | string;
      study_site_ref?: null | string;
      /** Format: date-time */
      declared_at?: null | string;
      /** Format: uuid */
      effectivity_id?: null | string;
    };
    Kernel_KernelDeclareScopedEffectivityRequest: {
      /** Format: uuid */
      effectivity_id: null | string;
      protocol_version_ref: string;
      scope_kind: string;
      scope_target_ref: null | string;
      country_code: null | string;
      /** Format: date-time */
      valid_from: string;
    };
    Kernel_KernelDeclareStudyRulePacksRequest: {
      rule_pack_refs: string[];
    };
    Kernel_KernelDefineWorkQueueRequest: {
      code: null | string;
      title: null | string;
      capability: null | string;
      verb_class: null | string;
      source: null | string;
      /** Format: uuid */
      work_queue_definition_id?: null | string;
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
    Kernel_KernelEffectiveVersionResponse: {
      protocol_version_ref: string;
      /** Format: int32 */
      number: number | string;
      effectivity_ref: string;
      scope_level: string;
    };
    Kernel_KernelEffectivityResponse: {
      effectivity_ref: string;
      /** Format: uuid */
      effectivity_id: string;
      study_ref: null | string;
      protocol_version_ref: string;
      /** Format: int32 */
      protocol_version_number: null | number | string;
      scope_level: string;
      country: null | string;
      study_site_ref: null | string;
      status: string;
      /** Format: date-time */
      status_since: string;
      evidence_artifact_version_refs: string[];
      ref?: null | string;
      scope_kind?: null | string;
      country_code?: null | string;
    };
    Kernel_KernelEffectivityStateResponse: {
      ref: string;
      /** Format: uuid */
      effectivity_id: string;
      protocol_version_ref: string;
      scope_kind: string;
      country_code: null | string;
      study_site_ref: null | string;
      status: string;
      /** Format: date-time */
      status_since: string;
      evidence_artifact_version_refs: string[];
    };
    Kernel_KernelElementChangeResponse: {
      element_id: string;
      instance_type: null | string;
      change: string;
      properties: string[];
    };
    Kernel_KernelEnrollmentLookupResponse: {
      study_ref: string;
      study_code: string;
      study_title: string;
      study_site_ref: string;
      site_code: string;
      site_title: string;
      enrollment_ref: string;
      subject_code: string;
      status: string;
    };
    Kernel_KernelEnrollmentScheduleResponse: {
      enrollment_ref: string;
      governing_version:
        null | components["schemas"]["Kernel_KernelProtocolVersionResponse"];
      schedule_of_activities:
        | null
        | components["schemas"]["Kernel_KernelScheduleOfActivitiesResponse"];
      requires_reconsent: boolean;
      is_inconsistent: boolean;
      inconsistency_reason: null | string;
      pending_transitions: components["schemas"]["Kernel_KernelPendingReconsentResponse"][];
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
    Kernel_KernelExplainFindingRequest: {
      explanation: string;
      /** Format: uuid */
      attestation_id?: null | string;
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
    Kernel_KernelFindingDecisionResponse: {
      status: string;
      decided_by: string;
      /** Format: date-time */
      decided_at: string;
      text: null | string;
      decided_on_run_ref: null | string;
      attestation_ref: null | string;
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
    Kernel_KernelLinkSiteToStudyRequest: {
      site_ref?: null | string;
      site_code?: null | string;
      site_name?: null | string;
      country?: null | string;
      institution_ref?: null | string;
      institution_code?: null | string;
      /** Format: uuid */
      site_id?: null | string;
      /** Format: uuid */
      study_site_id?: null | string;
    };
    Kernel_KernelLockBlockerResponse: {
      kind: string;
      owner: string;
      target_ref: null | string;
      target_kind: string;
      subject_code: null | string;
      collection_spec_id: null | string;
      collection_title: null | string;
      item_id: null | string;
      item_label: null | string;
      query_ref: null | string;
      query_text: null | string;
      answered: boolean;
      next_action: string;
      next_capability: string;
      masked: boolean;
      mask: null | string;
      finding_ref?: null | string;
      rule_id?: null | string;
      detail?: null | string;
    };
    Kernel_KernelLockReadinessResponse: {
      study_ref: string;
      study_code: string;
      scope_ref: string;
      scope_kind: string;
      ready_for_lock: boolean;
      can_freeze: boolean;
      /** Format: int32 */
      open_query_count: number | string;
      /** Format: int32 */
      missing_required_item_count: number | string;
      /** Format: int32 */
      open_error_finding_count: number | string;
      /** Format: int32 */
      blocker_count: number | string;
      blockers: components["schemas"]["Kernel_KernelLockBlockerResponse"][];
      explanation: components["schemas"]["Kernel_KernelExplanationResponse"];
    };
    Kernel_KernelLockResponse: {
      ref: null | string;
      study_ref: string;
      scope_ref: null | string;
      scope_kind: string;
      study_site_ref: null | string;
      enrollment_ref: null | string;
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
      expected_codelist?: null | string;
      expected_codelist_options?:
        null | components["schemas"]["Kernel_CodelistOptionView"][];
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
    Kernel_KernelPendingReconsentResponse: {
      target_version_ref: string;
      /** Format: int32 */
      target_version_number: null | number | string;
      amendment_intent: null | string;
      source_effectivity_ref: string;
      /** Format: date-time */
      required_since: string;
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
    Kernel_KernelRecordConsentStatusRequest: {
      /** Format: uuid */
      consent_status_id: null | string;
      protocol_version_ref: string;
      signed: boolean;
      basis_kind: string;
      /** Format: date-time */
      signed_at: string;
      artifact_version_ref?: null | string;
      attestation_ref?: null | string;
      scopes?:
        null | components["schemas"]["Kernel_KernelConsentScopeRequest"][];
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
    Kernel_KernelRegisterArtifactRequest: {
      kind: null | string;
      title: null | string;
      content_hash: null | string;
      /** Format: int64 */
      size_bytes: number | string;
      media_type: null | string;
      storage_ref: null | string;
      /** Format: uuid */
      artifact_id?: null | string;
      /** Format: uuid */
      artifact_version_id?: null | string;
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
    Kernel_KernelSiteSummaryResponse: {
      ref: string;
      code: string;
      name: string;
      country: string;
      institution_ref: string;
      institution_code: string;
      institution_name: string;
      /** @default true */
      is_active: boolean;
      /**
       * Format: int32
       * @default 0
       */
      linked_study_count: number | string;
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
    Kernel_KernelStudySiteResponse: {
      study_site_ref: string;
      /** Format: uuid */
      study_site_id: string;
      study_ref: null | string;
      site_ref: null | string;
      site_code: null | string;
      site_name: null | string;
      country: null | string;
      status: string;
      effective_versions: components["schemas"]["Kernel_KernelEffectiveVersionResponse"][];
      ref?: null | string;
    };
    Kernel_KernelStudySiteStateResponse: {
      ref: string;
      /** Format: uuid */
      study_site_id: string;
      status: string;
    };
    Kernel_KernelStudySiteSummaryResponse: {
      ref: string;
      /** Format: uuid */
      study_site_id: string;
      study_site_ref: string;
      study_ref: string;
      site_ref: string;
      site_code: string;
      site_name: string;
      country: string;
      institution_ref: string;
      institution_code: string;
      institution_name: string;
      status: string;
      effective_versions: components["schemas"]["Kernel_KernelEffectiveVersionResponse"][];
      /** @default  */
      study_code: string;
      /** @default true */
      is_active: boolean;
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
      linked_site?:
        null | components["schemas"]["Kernel_KernelStudySiteSummaryResponse"];
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
    Kernel_KernelSyncConsentStatusRequest: {
      study_code: string;
      site_code: null | string;
      subject_code: string;
      protocol_version_ref: null | string;
      /** Format: int32 */
      version_index: number | string;
      version_tag: null | string;
      /** Format: date-time */
      signed_at: string;
      /** @default false */
      is_paper_override: boolean;
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
      sandbox_institution_ref: string;
      sandbox_institution_code: string;
      sandbox_site_ref: string;
      sandbox_site_code: string;
      sandbox_site_name: string;
      sandbox_site_country: string;
      person_ref: string;
      membership_ref: string;
      admin_role_ref: string;
      admin_grant_ref: string;
      assigned_roles: string[];
      assigned_site_ids: string[];
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
      decision?:
        null | components["schemas"]["Kernel_KernelFindingDecisionResponse"];
    };
    Kernel_KernelVerifyObservationRequest: {
      enrollment_ref: string;
      /** Format: int32 */
      revision: number | string;
      /** Format: uuid */
      assertion_id?: null | string;
    };
    Kernel_KernelWorkItemResponse: {
      subject: null | string;
      subject_kind: string;
      title: null | string;
      /** Format: date-time */
      since: string;
      masked: null | string;
      target:
        null | components["schemas"]["Kernel_KernelWorkItemTargetResponse"];
    };
    Kernel_KernelWorkItemTargetResponse: {
      study_ref: null | string;
      /** Format: uuid */
      study_id: null | string;
      /** Format: uuid */
      enrollment_id: null | string;
      /** Format: uuid */
      form_id: null | string;
      observation_ref: null | string;
      finding_ref: null | string;
      protocol_version_ref: null | string;
      proposal_ref?: null | string;
    };
    Kernel_KernelWorkQueueDefinitionResponse: {
      definition: string;
      code: string;
      /** Format: int32 */
      version: number | string;
      title: string;
      capability: string;
      verb_class: string;
      source: string;
    };
    Kernel_KernelWorkQueueResponse: {
      definition: string;
      code: string;
      /** Format: int32 */
      version: number | string;
      title: string;
      capability: string;
      items: components["schemas"]["Kernel_KernelWorkItemResponse"][];
      truncated: boolean;
    };
    Kernel_KernelWorkQueuesResponse: {
      queues: components["schemas"]["Kernel_KernelWorkQueueResponse"][];
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
    /** @enum {unknown} */
    Kernel_QuantityComparator: "<" | "<=" | ">" | ">=" | null;
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
  KernelLookupEnrollment: {
    parameters: {
      query?: {
        study_code?: string;
        subject_code?: string;
        site_code?: string;
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
          "application/json": components["schemas"]["Kernel_KernelEnrollmentLookupResponse"];
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
  KernelGetEnrollmentScheduleOfActivities: {
    parameters: {
      query?: {
        valid_at?: string;
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
        content: {
          "application/json": components["schemas"]["Kernel_KernelEnrollmentScheduleResponse"];
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
      /** @description Not Found. Kernel errors: `K.Identity.TenantUnknown`, `K.Study.SiteUnknown`, `K.Study.SponsorUnknown`, `K.Study.StudyUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Study.AlreadyLinked`, `K.Study.StudyCodeTaken`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Identity.CodeRequired`, `K.Identity.NameRequired`, `K.Study.CodeRequired`, `K.Study.TitleRequired`. */
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
  KernelCreateOrganization: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: {
      content: {
        "application/json":
          | null
          | components["schemas"]["Kernel_KernelCreateOrganizationRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelOrganizationSummaryResponse"];
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
      /** @description Not Found. Kernel errors: `K.Identity.TenantUnknown`. */
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
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Identity.CodeRequired`, `K.Identity.NameRequired`. */
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
  KernelListSites: {
    parameters: {
      query?: {
        institutionRef?: string;
        country?: string;
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
          "application/json": components["schemas"]["Kernel_KernelSiteSummaryResponse"][];
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
  KernelCreateSite: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: {
      content: {
        "application/json":
          null | components["schemas"]["Kernel_KernelCreateSiteRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelSiteSummaryResponse"];
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
      /** @description Not Found. Kernel errors: `K.Identity.TenantUnknown`, `K.Study.InstitutionUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Study.SiteCodeTaken`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Identity.CodeRequired`, `K.Identity.NameRequired`, `K.Study.CodeRequired`, `K.Study.CountryInvalid`, `K.Study.NameRequired`. */
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
  KernelListStudySites: {
    parameters: {
      query?: {
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
        content: {
          "application/json": components["schemas"]["Kernel_KernelStudySiteSummaryResponse"][];
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
  KernelLinkSiteToStudy: {
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
          null | components["schemas"]["Kernel_KernelLinkSiteToStudyRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelStudySiteSummaryResponse"];
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
      /** @description Not Found. Kernel errors: `K.Study.InstitutionUnknown`, `K.Study.SiteUnknown`, `K.Study.StudyUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Study.AlreadyLinked`, `K.Study.SiteCodeTaken`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Study.CodeRequired`, `K.Study.CountryInvalid`, `K.Study.NameRequired`. */
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
  KernelSubmitProtocolVersionForReview: {
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
      /** @description Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Protocol.InvalidTransition`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Protocol.ContentInvalid`, `K.Protocol.ContentRequired`. */
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
  KernelReturnProtocolVersionToDraft: {
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
      /** @description Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Protocol.InvalidTransition`. */
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
  KernelApproveProtocolVersion: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        versionRef: string;
      };
      cookie?: never;
    };
    requestBody?: {
      content: {
        "application/json":
          | null
          | components["schemas"]["Kernel_KernelApproveProtocolVersionRequest"];
      };
    };
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
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Protocol.InvalidTransition`, `K.Trust.TargetContentChanged`, `K.Trust.TargetNotSignable`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Protocol.AttestationRejected`, `K.Trust.MethodRequired`, `K.Trust.TargetKindUnsupported`. */
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
  KernelListStudyArtifacts: {
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
          "application/json": components["schemas"]["Kernel_KernelArtifactResponse"][];
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
  KernelRegisterStudyArtifact: {
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
          null | components["schemas"]["Kernel_KernelRegisterArtifactRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelArtifactResponse"];
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
      /** @description Not Found. Kernel errors: `K.Evidence.StudyUnknown`. */
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
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Evidence.ContentInvalid`, `K.Evidence.KindInvalid`, `K.Evidence.TitleInvalid`. */
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
  KernelListStudyEffectivities: {
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
          "application/json": components["schemas"]["Kernel_KernelEffectivityResponse"][];
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
  KernelDeclareEffectivity: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        versionRef: string;
      };
      cookie?: never;
    };
    requestBody?: {
      content: {
        "application/json":
          | null
          | components["schemas"]["Kernel_KernelDeclareEffectivityRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelEffectivityResponse"];
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
      /** @description Not Found. Kernel errors: `K.Effectivity.ProtocolVersionUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Effectivity.AlreadyDeclared`, `K.Effectivity.ProtocolVersionWithdrawn`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Effectivity.ScopeInvalid`, `K.Effectivity.StudySiteNotInStudy`, `K.Effectivity.TransitionTimeInvalid`. */
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
  KernelChangeEffectivityStatus: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        effectivityRef: string;
      };
      cookie?: never;
    };
    requestBody?: {
      content: {
        "application/json":
          | null
          | components["schemas"]["Kernel_KernelChangeEffectivityStatusRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelEffectivityResponse"];
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
      /** @description Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Effectivity.InvalidTransition`, `K.Effectivity.ProtocolVersionNotApproved`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Effectivity.EvidenceInvalid`, `K.Effectivity.EvidenceRequired`, `K.Effectivity.TransitionTimeInvalid`. */
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
  KernelActivateStudySite: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        studySiteRef: string;
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
          "application/json": components["schemas"]["Kernel_KernelStudySiteResponse"];
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
      /** @description Not Found. Kernel errors: `K.Effectivity.StudySiteUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Effectivity.NoEffectiveVersion`, `K.Effectivity.StudySiteAlreadyActive`. */
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
  KernelDeclareScopedEffectivity: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_KernelDeclareScopedEffectivityRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelEffectivityStateResponse"];
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
      /** @description Not Found. Kernel errors: `K.Effectivity.ProtocolVersionUnknown`, `K.Effectivity.StudySiteUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Effectivity.AlreadyDeclared`, `K.Effectivity.ProtocolVersionNotApproved`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Effectivity.ScopeInvalid`, `K.Effectivity.StudySiteNotInStudy`, `K.Effectivity.TransitionTimeInvalid`. */
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
  KernelActivateStudySiteByCode: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_KernelActivateStudySiteByCodeRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelStudySiteStateResponse"];
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
      /** @description Not Found. Kernel errors: `K.Effectivity.StudySiteUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Effectivity.NoEffectiveVersion`, `K.Effectivity.StudySiteAlreadyActive`. */
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
  KernelRecordConsentStatus: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        enrollmentRef: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_KernelRecordConsentStatusRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelConsentStatusStateResponse"];
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
      /** @description Not Found. Kernel errors: `K.Consent.EnrollmentUnknown`, `K.Consent.ProtocolVersionUnknown`. */
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
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Consent.ScopesInvalid`, `K.Consent.SignedAtInvalid`. */
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
  KernelSyncConsentStatus: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_KernelSyncConsentStatusRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelConsentStatusStateResponse"];
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
      /** @description Not Found. Kernel errors: `K.Consent.EnrollmentUnknown`, `K.Consent.ProtocolVersionUnknown`. */
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
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Consent.ScopesInvalid`, `K.Consent.SignedAtInvalid`. */
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
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Observation.NoGoverningVersion`, `K.Observation.ParticipantWithdrawn`, `K.Observation.StudySiteNotActive`. */
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
      /** @description Locked. Kernel errors: `K.Observation.ApplicabilityInconsistent`, `K.Observation.Locked`, `K.Observation.ReconsentRequired`. */
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
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Execution.AlreadyActiveAtSite`, `K.Execution.StudySiteNotActive`, `K.Execution.SubjectCodeTaken`. */
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
      /** @description Not Found. Kernel errors: `K.Assertion.ScopeUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Assertion.AlreadyLocked`, `K.Assertion.ScopeNotReady`, `K.Command.IdempotencyConflict`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Assertion.AttestationRejected`, `K.Assertion.ScopeInvalid`, `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Trust.MethodRequired`. */
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
      /** @description Unprocessable Entity. Kernel errors: `K.Assertion.AttestationRejected`, `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Trust.MethodRequired`. */
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
  KernelGetStudyLockReadiness: {
    parameters: {
      query?: {
        scope_ref?: string;
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
          "application/json": components["schemas"]["Kernel_KernelLockReadinessResponse"];
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
  KernelInvestigateValidationFinding: {
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
          "application/json": components["schemas"]["Kernel_KernelValidationFindingResponse"];
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
      /** @description Not Found. Kernel errors: `K.Validation.FindingUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Validation.InvalidTransition`. */
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
  KernelClassifyValidationFinding: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        findingRef: string;
      };
      cookie?: never;
    };
    requestBody?: {
      content: {
        "application/json":
          null | components["schemas"]["Kernel_KernelClassifyFindingRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelValidationFindingResponse"];
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
      /** @description Not Found. Kernel errors: `K.Validation.FindingUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Validation.InvalidTransition`. */
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
  KernelAcceptValidationFinding: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        findingRef: string;
      };
      cookie?: never;
    };
    requestBody?: {
      content: {
        "application/json":
          null | components["schemas"]["Kernel_KernelAcceptFindingRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelValidationFindingResponse"];
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`, `K.Trust.PersonRequired`, `K.Validation.AttestationRejected`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Trust.TargetUnknown`, `K.Validation.FindingUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Trust.TargetContentChanged`, `K.Trust.TargetNotSignable`, `K.Validation.InvalidTransition`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Trust.MethodRequired`, `K.Trust.TargetKindUnsupported`, `K.Validation.DecisionTextInvalid`. */
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
  KernelExplainValidationFinding: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        findingRef: string;
      };
      cookie?: never;
    };
    requestBody?: {
      content: {
        "application/json":
          null | components["schemas"]["Kernel_KernelExplainFindingRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelValidationFindingResponse"];
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
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`, `K.Trust.PersonRequired`, `K.Validation.AttestationRejected`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Trust.TargetUnknown`, `K.Validation.FindingUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Trust.TargetContentChanged`, `K.Trust.TargetNotSignable`, `K.Validation.InvalidTransition`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Trust.MethodRequired`, `K.Trust.TargetKindUnsupported`, `K.Validation.DecisionTextInvalid`. */
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
  KernelReopenValidationFinding: {
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
          "application/json": components["schemas"]["Kernel_KernelValidationFindingResponse"];
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
      /** @description Not Found. Kernel errors: `K.Validation.FindingUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Validation.InvalidTransition`. */
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
  KernelReadWorkQueues: {
    parameters: {
      query?: {
        within?: string;
        code?: string[];
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
          "application/json": components["schemas"]["Kernel_KernelWorkQueuesResponse"];
        };
      };
      /** @description Bad Request. Kernel errors: `K.WorkQueue.QueryInvalid`. */
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
  KernelDefineWorkQueue: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Kernel_KernelDefineWorkQueueRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Kernel_KernelWorkQueueDefinitionResponse"];
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
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.WorkQueue.CapabilityInvalid`, `K.WorkQueue.CodeInvalid`, `K.WorkQueue.SourceUnknown`, `K.WorkQueue.TitleInvalid`. */
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
      /** @description Not Found. Kernel errors: `K.Identity.TenantUnknown`, `K.Study.InstitutionUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Authority.RoleCodeTaken`, `K.Command.IdempotencyConflict`, `K.Study.SiteCodeTaken`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Authority.CapabilityInvalid`, `K.Authority.GrantTargetInvalid`, `K.Authority.RoleCodeInvalid`, `K.Authority.RoleNameInvalid`, `K.Authority.ScopeInvalid`, `K.Authority.ValidityInvalid`, `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Commercial.EntitlementRequired`, `K.Commercial.SubscriptionRequired`, `K.Commercial.TierInvalid`, `K.Commercial.TrialWindowInvalid`, `K.Identity.CodeRequired`, `K.Identity.MembershipRequired`, `K.Identity.NameRequired`, `K.Identity.PersonRequired`, `K.Identity.ResidencyInvalid`, `K.Study.CodeRequired`, `K.Study.CountryInvalid`, `K.Study.NameRequired`. */
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
}
