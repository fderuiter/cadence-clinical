# kernel API

Generated from `docs/openapi/kernel_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `GET` | `/api/v1/kernel/activity` | KernelActivity | What happened in a scope, newest first. |
| `POST` | `/api/v1/kernel/assertions/{assertionRef}/retraction` | KernelRetractAssertion | Retracts a verification, answer or query assertion with the signed reason for change (assertion.retract). |
| `GET` | `/api/v1/kernel/authorization/can-i` | KernelCanI | Whether the caller may exercise a capability on an object now, and why. |
| `POST` | `/api/v1/kernel/consents/sync` | KernelSyncConsentStatus | Synchronizes signed coordinator or electronic consent from Execution into Kernel bitemporal ConsentStatus and Applicability (consent.record). |
| `POST` | `/api/v1/kernel/effectivities` | KernelDeclareScopedEffectivity | Declares an approved protocol version's effectivity at study, country, or study-site scope (effectivity.declare). |
| `POST` | `/api/v1/kernel/effectivities/{effectivityRef}/status` | KernelChangeEffectivityStatus | Transitions an effectivity's status in valid time with supporting approval artifact evidence (effectivity.change_status, K-TIME-5). |
| `GET` | `/api/v1/kernel/enrollments/lookup` | KernelLookupEnrollment | Resolves a study code, subject code, and optional site code to pinned study and enrollment references. |
| `POST` | `/api/v1/kernel/enrollments/screen` | KernelScreenEnrollment | Registers a pseudonymous participant when needed and starts a screening enrollment at a study site (participant.screen). |
| `GET` | `/api/v1/kernel/enrollments/{enrollmentRef}/casebook` | KernelExportEnrollmentCasebook | Streams a participant casebook with revisions, queries, verifications, attestations and SHA-256 digests. |
| `POST` | `/api/v1/kernel/enrollments/{enrollmentRef}/consents` | KernelRecordConsentStatus | Records bitemporal participant consent at SignedAt and resolves covered pending re-consent transitions (consent.record). |
| `GET` | `/api/v1/kernel/enrollments/{enrollmentRef}/schedule-of-activities` | KernelGetEnrollmentScheduleOfActivities | An enrollment's governing Schedule of Activities and re-consent lock state. |
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
| `POST` | `/api/v1/kernel/organizations` | KernelCreateOrganization | Registers an organization such as a site institution or sponsor in the caller's tenant (organization.register). |
| `GET` | `/api/v1/kernel/proposals/{proposalRef}` | KernelGetProposal | One proposal with its intent, plan, affected objects, requested capabilities, model, content hash and decision. |
| `POST` | `/api/v1/kernel/proposals/{proposalRef}/rejection` | KernelRejectProposal | Rejects an open proposal; the signed reason for change is the rejection reason (proposal.reject). |
| `GET` | `/api/v1/kernel/protocol-templates` | KernelListProtocolTemplates | Lists bundled standard CDISC Library eCRF templates available for protocol draft import. |
| `GET` | `/api/v1/kernel/protocol-versions/{versionRef}` | KernelGetProtocolVersion | One protocol version, without its content. |
| `POST` | `/api/v1/kernel/protocol-versions/{versionRef}/approve` | KernelApproveProtocolVersion | Records a 21 CFR Part 11 Approved attestation bound to the frozen protocol content hash and approves the protocol version (protocol.approve). |
| `GET` | `/api/v1/kernel/protocol-versions/{versionRef}/design-summary` | KernelGetProtocolDesignSummary | Returns a viewable summary of a protocol version's forms, ordered items, and import notes. |
| `POST` | `/api/v1/kernel/protocol-versions/{versionRef}/effectivities` | KernelDeclareEffectivity | Declares a Pending protocol-version effectivity for the whole study, a country, or one study site (effectivity.declare). |
| `GET` | `/api/v1/kernel/protocol-versions/{versionRef}/impact` | KernelAmendmentImpact | What depends on the version a protocol amendment changes (K-PROT-2). |
| `POST` | `/api/v1/kernel/protocol-versions/{versionRef}/return-to-draft` | KernelReturnProtocolVersionToDraft | Returns an InReview protocol version to Draft, clearing its content hash so it can be revised (protocol.return_to_draft). |
| `GET` | `/api/v1/kernel/protocol-versions/{versionRef}/schedule-of-activities` | KernelGetScheduleOfActivities | A protocol version's Schedule of Activities, compiled from its design. |
| `POST` | `/api/v1/kernel/protocol-versions/{versionRef}/submit-for-review` | KernelSubmitProtocolVersionForReview | Freezes a draft protocol version's content, computes its content hash, and moves it to InReview (protocol.submit_for_review). |
| `POST` | `/api/v1/kernel/protocols/matrix-gap-check` | KernelMatrixGapCheck | Performs immediate real-time matrix gap check on a candidate protocol design graph. |
| `GET` | `/api/v1/kernel/search` | KernelSearch | Finds kernel objects in the caller's tenant by code or title. |
| `POST` | `/api/v1/kernel/simulation/simulate` | KernelSimulateProtocol | Evaluates a candidate USDM protocol version in memory without database changes. |
| `GET` | `/api/v1/kernel/sites` | KernelListSites | Lists visible investigational sites in the caller's tenant. |
| `POST` | `/api/v1/kernel/sites` | KernelCreateSite | Registers an investigational site under a site institution in the caller's tenant (site.register). |
| `GET` | `/api/v1/kernel/status` | KernelStatus | Returns the Clinical Kernel API's operational status. |
| `GET` | `/api/v1/kernel/studies` | KernelListStudies | Lists visible studies in the caller's tenant. |
| `POST` | `/api/v1/kernel/studies` | KernelCreateStudy | Registers a new study in the caller's tenant with a signed reason for change (study.register) and optional one-step site link. |
| `GET` | `/api/v1/kernel/studies/{studyRef}/artifacts` | KernelListStudyArtifacts | Lists evidence artifacts registered for a study, newest first. |
| `POST` | `/api/v1/kernel/studies/{studyRef}/artifacts` | KernelRegisterStudyArtifact | Registers an immutable study evidence artifact version such as an ethics approval, regulatory approval, or CTA acknowledgement (artifact.register). |
| `GET` | `/api/v1/kernel/studies/{studyRef}/effectivities` | KernelListStudyEffectivities | Lists bitemporal protocol-version effectivities declared for a study. |
| `GET` | `/api/v1/kernel/studies/{studyRef}/exports` | KernelExportStudySnapshot | Streams a bitemporal study snapshot as CSV, CDISC ODM v1.3.2 XML, CDISC Dataset-JSON, SAS bundle ZIP, R bundle ZIP, or Study Archive ZIP. |
| `GET` | `/api/v1/kernel/studies/{studyRef}/locks` | KernelListStudyLocks | Lists Frozen and Locked assertions recorded for a study at an optional bitemporal as_of cut. |
| `GET` | `/api/v1/kernel/studies/{studyRef}/locks/readiness` | KernelGetStudyLockReadiness | Evaluates lock readiness (open queries and missing required items) for a study or child lock scope. |
| `GET` | `/api/v1/kernel/studies/{studyRef}/proposals` | KernelListProposals | A study's proposals, newest first, optionally only those with one status (Open, Approved, Rejected, Withdrawn, Expired). |
| `POST` | `/api/v1/kernel/studies/{studyRef}/protocol-imports` | KernelImportProtocolDraft | Synchronously imports ODM v1.3.2 XML, USDM v4 JSON, or bundled eCRF templates into a protocol draft. |
| `GET` | `/api/v1/kernel/studies/{studyRef}/protocol-versions` | KernelListProtocolVersions | A study's protocol versions, newest first, without their content. |
| `GET` | `/api/v1/kernel/studies/{studyRef}/rule-packs` | KernelListStudyRulePacks | Lists rule-pack library items in the tenant and indicates which versions are pinned on the study. |
| `POST` | `/api/v1/kernel/studies/{studyRef}/rule-packs/{itemRef}/preview` | KernelPreviewStudyRulePack | Evaluates a draft or approved rule pack in memory over the study's current collection records without mutating state. |
| `GET` | `/api/v1/kernel/studies/{studyRef}/sites` | KernelListStudySites | Lists visible investigational sites linked to a study together with their activation status and currently effective protocol versions. |
| `POST` | `/api/v1/kernel/studies/{studyRef}/sites` | KernelLinkSiteToStudy | Links an investigational site (registering it inline when site details are supplied) to a study in the caller's tenant (site.register, study_site.link). |
| `PUT` | `/api/v1/kernel/studies/{studyRef}/standards` | KernelDeclareStudyRulePacks | Declares or updates the approved rule-pack library items pinned on a study (study.declare_standards). |
| `GET` | `/api/v1/kernel/studies/{studyRef}/validation/findings` | KernelListStudyValidationFindings | Lists derived validation findings for a study, optionally filtered by stage, status, severity, rule pack or dataset. |
| `POST` | `/api/v1/kernel/study-sites/activate` | KernelActivateStudySiteByCode | Idempotently activates a linked study site resolved by study code and site code (study_site.activate). |
| `POST` | `/api/v1/kernel/study-sites/{studySiteRef}/activate` | KernelActivateStudySite | Activates a kernel study site once at least one protocol version is effective there (study_site.activate, K-TIME-4). |
| `GET` | `/api/v1/kernel/validation/findings/{findingRef}` | KernelGetValidationFinding | Returns one derived validation finding and its structured provenance explanation. |
| `GET` | `/healthz` | KernelHealthCheck | Clinical Kernel API health probe. |
