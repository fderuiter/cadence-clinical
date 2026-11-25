# kernel API

Generated from `docs/openapi/kernel_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

- **Service Maturity:** <span class="VPBadge tip">Active</span> (Port `8018` · Gateway prefixes: `/api/v1/kernel`, `/api/v1/ai`, `/api/v1/execution/forms`)
- **OpenAPI 3.1 Contract:** [`kernel_openapi.json`](../openapi/kernel_openapi.json) (75 operation(s), 164 schema(s))

## Module Overview

`kernel-api` serves the Clinical Kernel alongside consolidated `Intelligence` and `Execution` modules in one transactional composition host:

- **[Kernel Module](#kernel-module):** 67 operation(s) covering clinical studies, bitemporal graph nodes, protocol governance, effectivities, locks, proposals, validation rule packs, exports, and commercial entitlements.
- **[Intelligence Module](#intelligence-module):** 0 operation(s) in `kernel_openapi.json`; governed `/api/v1/ai/*` operations (including Streamable HTTP MCP `POST /api/v1/ai/mcp`, ledger, task qualification, evaluation runs, liveness, and kill-switch controls) are published in the dedicated [intelligence API](intelligence.md) contract.
- **[Execution Module](#execution-module):** 8 kernel subject enrollment, observation, verification, assertion, and casebook operation(s), plus the consolidated [execution_forms API](execution_forms.md) contract (`/api/v1/execution/forms*`) and standalone [execution API](execution.md).

## Kernel Module

Core Clinical Kernel read and command endpoints (`Kernel`, `Kernel protocol governance`, `Kernel locks`, `Kernel proposals`, `Kernel validation`, and host health probes).

| Method | Path | Operation | Summary | Contract |
| ------ | ---- | --------- | ------- | -------- |
| `GET` | `/api/v1/kernel/activity` | KernelActivity | What happened in a scope, newest first. | [Details](#op-kernelactivity) |
| `GET` | `/api/v1/kernel/authorization/can-i` | KernelCanI | Whether the caller may exercise a capability on an object now, and why. | [Details](#op-kernelcani) |
| `POST` | `/api/v1/kernel/effectivities/{effectivityRef}/status` | KernelChangeEffectivityStatus | Transitions an effectivity's status in valid time with supporting approval artifact evidence (effectivity.change_status, K-TIME-5). | [Details](#op-kernelchangeeffectivitystatus) |
| `GET` | `/api/v1/kernel/entitlements` | KernelGetEntitlements | The caller's tenant commercial subscription tier, trial state, and enabled product families. | [Details](#op-kernelgetentitlements) |
| `POST` | `/api/v1/kernel/entitlements/activate` | KernelActivateSubscription | Activates a Trialing or Expired commercial subscription after validating study scoping and a verified quote token. | [Details](#op-kernelactivatesubscription) |
| `GET` | `/api/v1/kernel/entitlements/posture` | KernelGetEntitlementPosture | Minimal internal tenant commercial posture for the API gateway. | [Details](#op-kernelgetentitlementposture) |
| `POST` | `/api/v1/kernel/entitlements/tier` | KernelUpgradeSubscriptionTier | Switches the tenant's commercial evaluation tier in place while preserving trial countdown and clinical records. | [Details](#op-kernelupgradesubscriptiontier) |
| `POST` | `/api/v1/kernel/library-items/{itemRef}/approve` | KernelApproveLibraryItem | Approves a draft library item version with an Approved attestation (library_item.approve). | [Details](#op-kernelapprovelibraryitem) |
| `POST` | `/api/v1/kernel/locks` | KernelApplyLock | Applies a Frozen or Locked assertion to a study, study site, enrollment, form instance or observation (lock.apply). | [Details](#op-kernelapplylock) |
| `GET` | `/api/v1/kernel/locks/in-force` | KernelGetScopeWriteLock | Checks whether any active write-denying lock (Frozen or Locked) covers a study, study site, or enrollment scope. | [Details](#op-kernelgetscopewritelock) |
| `POST` | `/api/v1/kernel/locks/{lockRef}/release` | KernelReleaseLock | Releases a Frozen or Locked assertion with a signed retracting assertion (lock.release). | [Details](#op-kernelreleaselock) |
| `GET` | `/api/v1/kernel/nodes` | KernelGetNodes | Resolves up to 100 NodeRefs to their summaries in one batch. | [Details](#op-kernelgetnodes) |
| `GET` | `/api/v1/kernel/nodes/{nodeRef}` | KernelGetNode | Resolves a NodeRef to its summary. | [Details](#op-kernelgetnode) |
| `GET` | `/api/v1/kernel/nodes/{nodeRef}/children` | KernelGetChildren | The children of a scope node, with their own child counts. | [Details](#op-kernelgetchildren) |
| `GET` | `/api/v1/kernel/nodes/{nodeRef}/commands` | KernelGetCommands | The kernel commands offered on an object, each with the caller's dry-run decision. | [Details](#op-kernelgetcommands) |
| `GET` | `/api/v1/kernel/nodes/{nodeRef}/dependents` | KernelDependents | What depends on an object, grouped by kind with counts. | [Details](#op-kerneldependents) |
| `GET` | `/api/v1/kernel/nodes/{nodeRef}/lineage` | KernelTrace | Walks an object's lineage backward to its inputs or forward to its dependents. | [Details](#op-kerneltrace) |
| `GET` | `/api/v1/kernel/nodes/{nodeRef}/path` | KernelGetZoomPath | The zoom path from the tenant down to an object. | [Details](#op-kernelgetzoompath) |
| `POST` | `/api/v1/kernel/onboarding/trial` | KernelBootstrapTrialWorkspace | Provisions an isolated self-service trial workspace for an unbound authenticated OIDC caller. | [Details](#op-kernelbootstraptrialworkspace) |
| `GET` | `/api/v1/kernel/organizations` | KernelListOrganizations | Lists visible organizations in the caller's tenant. | [Details](#op-kernellistorganizations) |
| `POST` | `/api/v1/kernel/organizations` | KernelCreateOrganization | Registers an organization such as a site institution or sponsor in the caller's tenant (organization.register). | [Details](#op-kernelcreateorganization) |
| `GET` | `/api/v1/kernel/proposals/{proposalRef}` | KernelGetProposal | One proposal with its intent, plan, affected objects, requested capabilities, model, content hash and decision. | [Details](#op-kernelgetproposal) |
| `POST` | `/api/v1/kernel/proposals/{proposalRef}/rejection` | KernelRejectProposal | Rejects an open proposal; the signed reason for change is the rejection reason (proposal.reject). | [Details](#op-kernelrejectproposal) |
| `GET` | `/api/v1/kernel/protocol-templates` | KernelListProtocolTemplates | Lists bundled standard CDISC Library eCRF templates available for protocol draft import. | [Details](#op-kernellistprotocoltemplates) |
| `GET` | `/api/v1/kernel/protocol-versions/{versionRef}` | KernelGetProtocolVersion | One protocol version, without its content. | [Details](#op-kernelgetprotocolversion) |
| `POST` | `/api/v1/kernel/protocol-versions/{versionRef}/approve` | KernelApproveProtocolVersion | Records a 21 CFR Part 11 Approved attestation bound to the frozen protocol content hash and approves the protocol version (protocol.approve). | [Details](#op-kernelapproveprotocolversion) |
| `GET` | `/api/v1/kernel/protocol-versions/{versionRef}/design-summary` | KernelGetProtocolDesignSummary | Returns a viewable summary of a protocol version's forms, ordered items, and import notes. | [Details](#op-kernelgetprotocoldesignsummary) |
| `POST` | `/api/v1/kernel/protocol-versions/{versionRef}/effectivities` | KernelDeclareEffectivity | Declares a Pending protocol-version effectivity for the whole study, a country, or one study site (effectivity.declare). | [Details](#op-kerneldeclareeffectivity) |
| `GET` | `/api/v1/kernel/protocol-versions/{versionRef}/impact` | KernelAmendmentImpact | What depends on the version a protocol amendment changes (K-PROT-2). | [Details](#op-kernelamendmentimpact) |
| `POST` | `/api/v1/kernel/protocol-versions/{versionRef}/return-to-draft` | KernelReturnProtocolVersionToDraft | Returns an InReview protocol version to Draft, clearing its content hash so it can be revised (protocol.return_to_draft). | [Details](#op-kernelreturnprotocolversiontodraft) |
| `GET` | `/api/v1/kernel/protocol-versions/{versionRef}/schedule-of-activities` | KernelGetScheduleOfActivities | A protocol version's Schedule of Activities, compiled from its design. | [Details](#op-kernelgetscheduleofactivities) |
| `POST` | `/api/v1/kernel/protocol-versions/{versionRef}/submit-for-review` | KernelSubmitProtocolVersionForReview | Freezes a draft protocol version's content, computes its content hash, and moves it to InReview (protocol.submit_for_review). | [Details](#op-kernelsubmitprotocolversionforreview) |
| `POST` | `/api/v1/kernel/protocols/matrix-gap-check` | KernelMatrixGapCheck | Performs immediate real-time matrix gap check on a candidate protocol design graph. | [Details](#op-kernelmatrixgapcheck) |
| `GET` | `/api/v1/kernel/search` | KernelSearch | Finds kernel objects in the caller's tenant by code or title. | [Details](#op-kernelsearch) |
| `POST` | `/api/v1/kernel/simulation/simulate` | KernelSimulateProtocol | Evaluates a candidate protocol version, mapping specification, or rule pack in memory at a bitemporal (valid_at, known_at) instant without database changes. | [Details](#op-kernelsimulateprotocol) |
| `GET` | `/api/v1/kernel/sites` | KernelListSites | Lists visible investigational sites in the caller's tenant. | [Details](#op-kernellistsites) |
| `POST` | `/api/v1/kernel/sites` | KernelCreateSite | Registers an investigational site under a site institution in the caller's tenant (site.register). | [Details](#op-kernelcreatesite) |
| `GET` | `/api/v1/kernel/status` | KernelStatus | Returns the Clinical Kernel API's operational status. | [Details](#op-kernelstatus) |
| `GET` | `/api/v1/kernel/studies` | KernelListStudies | Lists visible studies in the caller's tenant. | [Details](#op-kernelliststudies) |
| `POST` | `/api/v1/kernel/studies` | KernelCreateStudy | Registers a new study in the caller's tenant with a signed reason for change (study.register) and optional one-step site link. | [Details](#op-kernelcreatestudy) |
| `GET` | `/api/v1/kernel/studies/{studyRef}/artifacts` | KernelListStudyArtifacts | Lists evidence artifacts registered for a study, newest first. | [Details](#op-kernelliststudyartifacts) |
| `POST` | `/api/v1/kernel/studies/{studyRef}/artifacts` | KernelRegisterStudyArtifact | Registers an immutable study evidence artifact version such as an ethics approval, regulatory approval, or CTA acknowledgement (artifact.register). | [Details](#op-kernelregisterstudyartifact) |
| `GET` | `/api/v1/kernel/studies/{studyRef}/attention` | KernelGetStudyAttention | Lists a study's open proposals and active (Open or Investigating) validation findings in one attention read. | [Details](#op-kernelgetstudyattention) |
| `GET` | `/api/v1/kernel/studies/{studyRef}/effectivities` | KernelListStudyEffectivities | Lists bitemporal protocol-version effectivities declared for a study. | [Details](#op-kernelliststudyeffectivities) |
| `GET` | `/api/v1/kernel/studies/{studyRef}/exports` | KernelExportStudySnapshot | Streams a bitemporal study snapshot as CSV, CDISC ODM v1.3.2 XML, CDISC Dataset-JSON, SAS bundle ZIP, R bundle ZIP, or Study Archive ZIP. | [Details](#op-kernelexportstudysnapshot) |
| `GET` | `/api/v1/kernel/studies/{studyRef}/locks` | KernelListStudyLocks | Lists Frozen and Locked assertions recorded for a study at an optional bitemporal as_of cut. | [Details](#op-kernelliststudylocks) |
| `GET` | `/api/v1/kernel/studies/{studyRef}/locks/readiness` | KernelGetStudyLockReadiness | Evaluates lock readiness across queries, missing required items, unverified revisions, unsigned forms, unopened scheduled visits, and Error validation findings. | [Details](#op-kernelgetstudylockreadiness) |
| `GET` | `/api/v1/kernel/studies/{studyRef}/proposals` | KernelListProposals | A study's proposals, newest first, optionally only those with one status (Open, Approved, Rejected, Withdrawn, Expired). | [Details](#op-kernellistproposals) |
| `POST` | `/api/v1/kernel/studies/{studyRef}/protocol-imports` | KernelImportProtocolDraft | Synchronously imports ODM v1.3.2 XML, USDM v4 JSON, or bundled eCRF templates into a protocol draft. | [Details](#op-kernelimportprotocoldraft) |
| `GET` | `/api/v1/kernel/studies/{studyRef}/protocol-versions` | KernelListProtocolVersions | A study's protocol versions, newest first, without their content. | [Details](#op-kernellistprotocolversions) |
| `GET` | `/api/v1/kernel/studies/{studyRef}/rule-packs` | KernelListStudyRulePacks | Lists rule-pack library items in the tenant and indicates which versions are pinned on the study. | [Details](#op-kernelliststudyrulepacks) |
| `POST` | `/api/v1/kernel/studies/{studyRef}/rule-packs/{itemRef}/preview` | KernelPreviewStudyRulePack | Evaluates a draft or approved rule pack in memory over the study's current collection records without mutating state. | [Details](#op-kernelpreviewstudyrulepack) |
| `POST` | `/api/v1/kernel/studies/{studyRef}/simulation-reports` | KernelSaveSimulationReport | Registers an unmasked SimulationResult as an immutable SimulationReport artifact version for Part 11 review attestation (K-SIM-5). | [Details](#op-kernelsavesimulationreport) |
| `GET` | `/api/v1/kernel/studies/{studyRef}/sites` | KernelListStudySites | Lists visible investigational sites linked to a study together with their activation status and currently effective protocol versions. | [Details](#op-kernelliststudysites) |
| `POST` | `/api/v1/kernel/studies/{studyRef}/sites` | KernelLinkSiteToStudy | Links an investigational site (registering it inline when site details are supplied) to a study in the caller's tenant (site.register, study_site.link). | [Details](#op-kernellinksitetostudy) |
| `PUT` | `/api/v1/kernel/studies/{studyRef}/standards` | KernelDeclareStudyRulePacks | Declares or updates the approved rule-pack library items pinned on a study (study.declare_standards). | [Details](#op-kerneldeclarestudyrulepacks) |
| `GET` | `/api/v1/kernel/studies/{studyRef}/validation/findings` | KernelListStudyValidationFindings | Lists derived validation findings for a study, optionally filtered by stage, repeatable status, severity, rule pack or dataset. | [Details](#op-kernelliststudyvalidationfindings) |
| `POST` | `/api/v1/kernel/study-sites/{studySiteRef}/activate` | KernelActivateStudySite | Activates a kernel study site once at least one protocol version is effective there (study_site.activate, K-TIME-4). | [Details](#op-kernelactivatestudysite) |
| `GET` | `/api/v1/kernel/validation/findings/{findingRef}` | KernelGetValidationFinding | Returns one derived validation finding and its structured provenance explanation. | [Details](#op-kernelgetvalidationfinding) |
| `POST` | `/api/v1/kernel/validation/findings/{findingRef}/accept` | KernelAcceptValidationFinding | Accepts an open or investigating validation finding with a Responsibility electronic signature (validation_finding.accept). | [Details](#op-kernelacceptvalidationfinding) |
| `POST` | `/api/v1/kernel/validation/findings/{findingRef}/classify` | KernelClassifyValidationFinding | Classifies a detected validation finding (validation_finding.classify). | [Details](#op-kernelclassifyvalidationfinding) |
| `POST` | `/api/v1/kernel/validation/findings/{findingRef}/explain` | KernelExplainValidationFinding | Explains an open or investigating validation finding with an Approved electronic signature (validation_finding.explain). | [Details](#op-kernelexplainvalidationfinding) |
| `POST` | `/api/v1/kernel/validation/findings/{findingRef}/investigate` | KernelInvestigateValidationFinding | Marks an open validation finding as under investigation (validation_finding.investigate). | [Details](#op-kernelinvestigatevalidationfinding) |
| `POST` | `/api/v1/kernel/validation/findings/{findingRef}/reopen` | KernelReopenValidationFinding | Withdraws an investigation, acceptance or explanation on a validation finding (validation_finding.reopen). | [Details](#op-kernelreopenvalidationfinding) |
| `GET` | `/api/v1/kernel/work-queues` | KernelReadWorkQueues | Reads the caller's capability-keyed work queues in the current tenant or within a scope. | [Details](#op-kernelreadworkqueues) |
| `POST` | `/api/v1/kernel/work-queues` | KernelDefineWorkQueue | Defines the next version of a capability-keyed work queue in the caller's tenant (work_queue.define). | [Details](#op-kerneldefineworkqueue) |
| `GET` | `/healthz` | KernelHealthCheck | Clinical Kernel API health probe. | [Details](#op-kernelhealthcheck) |

### `GET` `/api/v1/kernel/activity` {#op-kernelactivity}

**Operation ID:** `KernelActivity` — What happened in a scope, newest first.

scope is any NodeRef or study code (the whole tenant when absent); family repeats (Knowledge, Execution, Evidence, Trust, Quality, Intelligence); actor filters by who did it; from is the earliest recorded time; before is the previous page's next cursor; limit 1 to 200 (default 50); include_nodes=true embeds resolved summaries for unmasked entry subjects. Entries about objects you may not see are masked in place.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `actor` | `query` | `optional` | `string` |  |
| `before` | `query` | `optional` | `string` |  |
| `family` | `query` | `optional` | array of `string` |  |
| `from` | `query` | `optional` | `string` (`date-time`) |  |
| `include_nodes` | `query` | `optional` | `boolean` |  |
| `limit` | `query` | `optional` | `integer` (`int32`) |  |
| `scope` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelActivityPageResponse`](#schema-kernel-kernelactivitypageresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `503` | Service Unavailable | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `GET` `/api/v1/kernel/authorization/can-i` {#op-kernelcani}

**Operation ID:** `KernelCanI` — Whether the caller may exercise a capability on an object now, and why.

A dry run: it executes nothing and is recorded in the authorization access log, not the audit chain (ADR-2483).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `capability` | `query` | `required` | `string` |  |
| `resource` | `query` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelDecisionResponse`](#schema-kernel-kerneldecisionresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `503` | Service Unavailable | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `POST` `/api/v1/kernel/effectivities/{effectivityRef}/status` {#op-kernelchangeeffectivitystatus}

**Operation ID:** `KernelChangeEffectivityStatus` — Transitions an effectivity's status in valid time with supporting approval artifact evidence (effectivity.change_status, K-TIME-5).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel protocol governance`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `effectivityRef` | `path` | `required` | `string` |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`optional`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_KernelChangeEffectivityStatusRequest`](#schema-kernel-kernelchangeeffectivitystatusrequest) \| `null` |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelEffectivityResponse`](#schema-kernel-kerneleffectivityresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Aggregate.NotFound`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Effectivity.InvalidTransition`, `K.Effectivity.ProtocolVersionNotApproved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Effectivity.EvidenceInvalid`, `K.Effectivity.EvidenceRequired`, `K.Effectivity.TransitionTimeInvalid`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Aggregate.NotFound` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `K.Effectivity.InvalidTransition` |
| `409` | `K.Effectivity.ProtocolVersionNotApproved` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `K.Effectivity.EvidenceInvalid` |
| `422` | `K.Effectivity.EvidenceRequired` |
| `422` | `K.Effectivity.TransitionTimeInvalid` |

</details>

### `GET` `/api/v1/kernel/entitlements` {#op-kernelgetentitlements}

**Operation ID:** `KernelGetEntitlements` — The caller's tenant commercial subscription tier, trial state, and enabled product families.

Requires an enrolled TenantMembership and an active Grant visible via GrantNodeVisibility. BillingConfirmation is masked unless the caller holds billing permission. Commercial packaging boundary: never grants clinical study access or bypasses Grant, blinding, or consent.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelEntitlementsResponse`](#schema-kernel-kernelentitlementsresponse) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `503` | Service Unavailable | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `POST` `/api/v1/kernel/entitlements/activate` {#op-kernelactivatesubscription}

**Operation ID:** `KernelActivateSubscription` — Activates a Trialing or Expired commercial subscription after validating study scoping and a verified quote token.

Requires caller Idempotency-Key and signed X-Change-Reason headers, study scoping parameters, and a verified cryptographic commercial quote token (or CommercialOperators.BillingOperator) before transitioning a Trialing or Expired workspace to Active in one atomic transaction.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_KernelActivateSubscriptionRequest`](#schema-kernel-kernelactivatesubscriptionrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelEntitlementsResponse`](#schema-kernel-kernelentitlementsresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Commercial.QuoteInvalid`, `K.Subscription.Expired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Commercial.SubscriptionUnknown`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Commercial.EntitlementRequired`, `K.Commercial.ScopingInvalid`, `K.Commercial.SubscriptionRequired`, `K.Commercial.TierInvalid`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Commercial.QuoteInvalid` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Commercial.SubscriptionUnknown` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `K.Commercial.EntitlementRequired` |
| `422` | `K.Commercial.ScopingInvalid` |
| `422` | `K.Commercial.SubscriptionRequired` |
| `422` | `K.Commercial.TierInvalid` |

</details>

### `GET` `/api/v1/kernel/entitlements/posture` {#op-kernelgetentitlementposture}

**Operation ID:** `KernelGetEntitlementPosture` — Minimal internal tenant commercial posture for the API gateway.

Restricted to svc:gateway-entitlements. Returns read-only state, enabled product families, and quantitative scoping caps while omitting BillingConfirmation.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelEntitlementPostureResponse`](#schema-kernel-kernelentitlementpostureresponse) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `503` | Service Unavailable | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `POST` `/api/v1/kernel/entitlements/tier` {#op-kernelupgradesubscriptiontier}

**Operation ID:** `KernelUpgradeSubscriptionTier` — Switches the tenant's commercial evaluation tier in place while preserving trial countdown and clinical records.

Requires caller Idempotency-Key and signed X-Change-Reason headers. Updates enabled product families on an active Trialing or Active subscription in one atomic transaction without activating billing or extending the original 30-day trial window.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_KernelUpgradeSubscriptionRequest`](#schema-kernel-kernelupgradesubscriptionrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelEntitlementsResponse`](#schema-kernel-kernelentitlementsresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Commercial.SubscriptionUnknown`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Commercial.EntitlementRequired`, `K.Commercial.SubscriptionRequired`, `K.Commercial.TierInvalid`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Commercial.SubscriptionUnknown` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `K.Commercial.EntitlementRequired` |
| `422` | `K.Commercial.SubscriptionRequired` |
| `422` | `K.Commercial.TierInvalid` |

</details>

### `POST` `/api/v1/kernel/library-items/{itemRef}/approve` {#op-kernelapprovelibraryitem}

**Operation ID:** `KernelApproveLibraryItem` — Approves a draft library item version with an Approved attestation (library_item.approve).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel validation`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `itemRef` | `path` | `required` | `string` |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |
| `X-Sig-Token` | `header` | `required` | `string` (minLen: 1) | Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). |

**Request Body** (`optional`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_KernelApproveLibraryItemRequest`](#schema-kernel-kernelapprovelibraryitemrequest) \| `null` |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelRulePackResponse`](#schema-kernel-kernelrulepackresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`, `K.Trust.AuthenticationStale`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Standards.AttestationRejected`, `K.Subscription.Expired`, `K.Trust.PersonRequired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Standards.LibraryItemUnknown`, `K.Trust.TargetUnknown`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Standards.InvalidTransition`, `K.Trust.TargetContentChanged`, `K.Trust.TargetNotSignable`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Trust.MethodRequired`, `K.Trust.TargetKindUnsupported`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `401` | `K.Trust.AuthenticationStale` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Standards.AttestationRejected` |
| `403` | `K.Subscription.Expired` |
| `403` | `K.Trust.PersonRequired` |
| `404` | `K.Standards.LibraryItemUnknown` |
| `404` | `K.Trust.TargetUnknown` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `K.Standards.InvalidTransition` |
| `409` | `K.Trust.TargetContentChanged` |
| `409` | `K.Trust.TargetNotSignable` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `K.Trust.MethodRequired` |
| `422` | `K.Trust.TargetKindUnsupported` |

</details>

### `POST` `/api/v1/kernel/locks` {#op-kernelapplylock}

**Operation ID:** `KernelApplyLock` — Applies a Frozen or Locked assertion to a study, study site, enrollment, form instance or observation (lock.apply).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel locks`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |
| `X-Sig-Token` | `header` | `required` | `string` (minLen: 1) | Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_KernelApplyLockRequest`](#schema-kernel-kernelapplylockrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Kernel_KernelLockResponse`](#schema-kernel-kernellockresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`, `K.Trust.AuthenticationStale`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`, `K.Trust.PersonRequired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Assertion.ScopeUnknown`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Assertion.AlreadyLocked`, `K.Assertion.ScopeNotReady`, `K.Command.IdempotencyConflict`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Assertion.AttestationRejected`, `K.Assertion.ScopeInvalid`, `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Trust.MethodRequired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `401` | `K.Trust.AuthenticationStale` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `403` | `K.Trust.PersonRequired` |
| `404` | `K.Assertion.ScopeUnknown` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Assertion.AlreadyLocked` |
| `409` | `K.Assertion.ScopeNotReady` |
| `409` | `K.Command.IdempotencyConflict` |
| `422` | `K.Assertion.AttestationRejected` |
| `422` | `K.Assertion.ScopeInvalid` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `K.Trust.MethodRequired` |

</details>

### `GET` `/api/v1/kernel/locks/in-force` {#op-kernelgetscopewritelock}

**Operation ID:** `KernelGetScopeWriteLock` — Checks whether any active write-denying lock (Frozen or Locked) covers a study, study site, or enrollment scope.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel locks`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `enrollment_id` | `query` | `optional` | `string` (`uuid`) |  |
| `site_code` | `query` | `optional` | `string` |  |
| `study_code` | `query` | `optional` | `string` |  |
| `subject_code` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelScopeWriteLockResponse`](#schema-kernel-kernelscopewritelockresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `POST` `/api/v1/kernel/locks/{lockRef}/release` {#op-kernelreleaselock}

**Operation ID:** `KernelReleaseLock` — Releases a Frozen or Locked assertion with a signed retracting assertion (lock.release).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel locks`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `lockRef` | `path` | `required` | `string` |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |
| `X-Sig-Token` | `header` | `required` | `string` (minLen: 1) | Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). |

**Request Body** (`optional`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_KernelReleaseLockRequest`](#schema-kernel-kernelreleaselockrequest) \| `null` |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelLockResponse`](#schema-kernel-kernellockresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`, `K.Trust.AuthenticationStale`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`, `K.Trust.PersonRequired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Aggregate.NotFound`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Assertion.AlreadyRetracted`, `K.Command.IdempotencyConflict`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Assertion.AttestationRejected`, `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Trust.MethodRequired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `401` | `K.Trust.AuthenticationStale` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `403` | `K.Trust.PersonRequired` |
| `404` | `K.Aggregate.NotFound` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Assertion.AlreadyRetracted` |
| `409` | `K.Command.IdempotencyConflict` |
| `422` | `K.Assertion.AttestationRejected` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `K.Trust.MethodRequired` |

</details>

### `GET` `/api/v1/kernel/nodes` {#op-kernelgetnodes}

**Operation ID:** `KernelGetNodes` — Resolves up to 100 NodeRefs to their summaries in one batch.

Returns a dictionary keyed by each requested ref that exists in the caller's tenant. Unknown objects are omitted; objects the caller may not see are masked in place (ADR-2471).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `ref` | `query` | `optional` | array of `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelBatchNodesResponse`](#schema-kernel-kernelbatchnodesresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `503` | Service Unavailable | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `GET` `/api/v1/kernel/nodes/{nodeRef}` {#op-kernelgetnode}

**Operation ID:** `KernelGetNode` — Resolves a NodeRef to its summary.

Title, code and state of one kernel object in the caller's tenant, at its current version.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `nodeRef` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelNodeResponse`](#schema-kernel-kernelnoderesponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `503` | Service Unavailable | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `GET` `/api/v1/kernel/nodes/{nodeRef}/children` {#op-kernelgetchildren}

**Operation ID:** `KernelGetChildren` — The children of a scope node, with their own child counts.

A node the caller may not see is refused; a child the caller may not see is masked in place (ADR-2471).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `nodeRef` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Kernel_KernelChildResponse`](#schema-kernel-kernelchildresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `503` | Service Unavailable | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `GET` `/api/v1/kernel/nodes/{nodeRef}/commands` {#op-kernelgetcommands}

**Operation ID:** `KernelGetCommands` — The kernel commands offered on an object, each with the caller's dry-run decision.

Each decision is the one sending the command would get now (ADR-2483), explained (ADR-2482).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `nodeRef` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Kernel_KernelCommandResponse`](#schema-kernel-kernelcommandresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `503` | Service Unavailable | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `GET` `/api/v1/kernel/nodes/{nodeRef}/dependents` {#op-kerneldependents}

**Operation ID:** `KernelDependents` — What depends on an object, grouped by kind with counts.

Masked objects are counted like any other, so a count never depends on the reader. depth 1 to 50 (default 10); edge_kind repeats; valid_at and known_at read the graph as it was.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `nodeRef` | `path` | `required` | `string` |  |
| `depth` | `query` | `optional` | `integer` (`int32`) |  |
| `edge_kind` | `query` | `optional` | array of `string` |  |
| `known_at` | `query` | `optional` | `string` (`date-time`) |  |
| `valid_at` | `query` | `optional` | `string` (`date-time`) |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelDependentsResponse`](#schema-kernel-kerneldependentsresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `503` | Service Unavailable | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `GET` `/api/v1/kernel/nodes/{nodeRef}/lineage` {#op-kerneltrace}

**Operation ID:** `KernelTrace` — Walks an object's lineage backward to its inputs or forward to its dependents.

direction is Backward (default) or Forward; depth 1 to 50 (default 10); edge_kind repeats (for example DerivedFrom); valid_at and known_at read the graph as it was.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `nodeRef` | `path` | `required` | `string` |  |
| `depth` | `query` | `optional` | `integer` (`int32`) |  |
| `direction` | `query` | `optional` | `string` |  |
| `edge_kind` | `query` | `optional` | array of `string` |  |
| `known_at` | `query` | `optional` | `string` (`date-time`) |  |
| `valid_at` | `query` | `optional` | `string` (`date-time`) |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelLineageResponse`](#schema-kernel-kernellineageresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `503` | Service Unavailable | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `GET` `/api/v1/kernel/nodes/{nodeRef}/path` {#op-kernelgetzoompath}

**Operation ID:** `KernelGetZoomPath` — The zoom path from the tenant down to an object.

Presentation only: the path never grants or implies authority (spec §9.3).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `nodeRef` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Kernel_KernelZoomStepResponse`](#schema-kernel-kernelzoomstepresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `503` | Service Unavailable | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `POST` `/api/v1/kernel/onboarding/trial` {#op-kernelbootstraptrialworkspace}

**Operation ID:** `KernelBootstrapTrialWorkspace` — Provisions an isolated self-service trial workspace for an unbound authenticated OIDC caller.

Mapped outside KernelTenantFilter for signed-in callers without an active tenant_id claim, study/site scope, or clinical realm roles. Requires caller Idempotency-Key and signed X-Change-Reason headers.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`optional`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_KernelTrialOnboardingRequest`](#schema-kernel-kerneltrialonboardingrequest) \| `null` |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelTrialOnboardingResponse`](#schema-kernel-kerneltrialonboardingresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Commercial.OnboardingCallerInvalid`, `K.Subscription.Expired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Identity.TenantUnknown`, `K.Study.InstitutionUnknown`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Authority.RoleCodeTaken`, `K.Command.IdempotencyConflict`, `K.Study.SiteCodeTaken`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Authority.CapabilityInvalid`, `K.Authority.GrantTargetInvalid`, `K.Authority.RoleCodeInvalid`, `K.Authority.RoleNameInvalid`, `K.Authority.ScopeInvalid`, `K.Authority.ValidityInvalid`, `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Commercial.EntitlementRequired`, `K.Commercial.SubscriptionRequired`, `K.Commercial.TierInvalid`, `K.Commercial.TrialWindowInvalid`, `K.Identity.CodeRequired`, `K.Identity.MembershipRequired`, `K.Identity.NameRequired`, `K.Identity.PersonRequired`, `K.Identity.ResidencyInvalid`, `K.Study.CodeRequired`, `K.Study.CountryInvalid`, `K.Study.NameRequired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `502` | Bad Gateway. Kernel errors: `K.Commercial.IdentityProviderSyncFailed`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Commercial.OnboardingCallerInvalid` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Identity.TenantUnknown` |
| `404` | `K.Study.InstitutionUnknown` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Authority.RoleCodeTaken` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `K.Study.SiteCodeTaken` |
| `422` | `K.Authority.CapabilityInvalid` |
| `422` | `K.Authority.GrantTargetInvalid` |
| `422` | `K.Authority.RoleCodeInvalid` |
| `422` | `K.Authority.RoleNameInvalid` |
| `422` | `K.Authority.ScopeInvalid` |
| `422` | `K.Authority.ValidityInvalid` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `K.Commercial.EntitlementRequired` |
| `422` | `K.Commercial.SubscriptionRequired` |
| `422` | `K.Commercial.TierInvalid` |
| `422` | `K.Commercial.TrialWindowInvalid` |
| `422` | `K.Identity.CodeRequired` |
| `422` | `K.Identity.MembershipRequired` |
| `422` | `K.Identity.NameRequired` |
| `422` | `K.Identity.PersonRequired` |
| `422` | `K.Identity.ResidencyInvalid` |
| `422` | `K.Study.CodeRequired` |
| `422` | `K.Study.CountryInvalid` |
| `422` | `K.Study.NameRequired` |
| `502` | `K.Commercial.IdentityProviderSyncFailed` |

</details>

### `GET` `/api/v1/kernel/organizations` {#op-kernellistorganizations}

**Operation ID:** `KernelListOrganizations` — Lists visible organizations in the caller's tenant.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `kind` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Kernel_KernelOrganizationSummaryResponse`](#schema-kernel-kernelorganizationsummaryresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `POST` `/api/v1/kernel/organizations` {#op-kernelcreateorganization}

**Operation ID:** `KernelCreateOrganization` — Registers an organization such as a site institution or sponsor in the caller's tenant (organization.register).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`optional`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_KernelCreateOrganizationRequest`](#schema-kernel-kernelcreateorganizationrequest) \| `null` |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Kernel_KernelOrganizationSummaryResponse`](#schema-kernel-kernelorganizationsummaryresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Identity.TenantUnknown`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Identity.CodeRequired`, `K.Identity.NameRequired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Identity.TenantUnknown` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `K.Identity.CodeRequired` |
| `422` | `K.Identity.NameRequired` |

</details>

### `GET` `/api/v1/kernel/proposals/{proposalRef}` {#op-kernelgetproposal}

**Operation ID:** `KernelGetProposal` — One proposal with its intent, plan, affected objects, requested capabilities, model, content hash and decision.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel proposals`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `proposalRef` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelProposalResponse`](#schema-kernel-kernelproposalresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `POST` `/api/v1/kernel/proposals/{proposalRef}/rejection` {#op-kernelrejectproposal}

**Operation ID:** `KernelRejectProposal` — Rejects an open proposal; the signed reason for change is the rejection reason (proposal.reject).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel proposals`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `proposalRef` | `path` | `required` | `string` |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelProposalStateResponse`](#schema-kernel-kernelproposalstateresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Aggregate.NotFound`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Proposal.Expired`, `K.Proposal.NotOpen`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Aggregate.NotFound` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `K.Proposal.Expired` |
| `409` | `K.Proposal.NotOpen` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |

</details>

### `GET` `/api/v1/kernel/protocol-templates` {#op-kernellistprotocoltemplates}

**Operation ID:** `KernelListProtocolTemplates` — Lists bundled standard CDISC Library eCRF templates available for protocol draft import.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Kernel_KernelProtocolTemplateResponse`](#schema-kernel-kernelprotocoltemplateresponse) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `GET` `/api/v1/kernel/protocol-versions/{versionRef}` {#op-kernelgetprotocolversion}

**Operation ID:** `KernelGetProtocolVersion` — One protocol version, without its content.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `versionRef` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelProtocolVersionResponse`](#schema-kernel-kernelprotocolversionresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `503` | Service Unavailable | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `POST` `/api/v1/kernel/protocol-versions/{versionRef}/approve` {#op-kernelapproveprotocolversion}

**Operation ID:** `KernelApproveProtocolVersion` — Records a 21 CFR Part 11 Approved attestation bound to the frozen protocol content hash and approves the protocol version (protocol.approve).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel protocol governance`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `versionRef` | `path` | `required` | `string` |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |
| `X-Sig-Token` | `header` | `required` | `string` (minLen: 1) | Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). |

**Request Body** (`optional`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_KernelApproveProtocolVersionRequest`](#schema-kernel-kernelapproveprotocolversionrequest) \| `null` |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelProtocolVersionResponse`](#schema-kernel-kernelprotocolversionresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`, `K.Trust.AuthenticationStale`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`, `K.Trust.PersonRequired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Aggregate.NotFound`, `K.Trust.TargetUnknown`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Protocol.InvalidTransition`, `K.Protocol.SimulationReviewRequired`, `K.Trust.TargetContentChanged`, `K.Trust.TargetNotSignable`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Protocol.AttestationRejected`, `K.Trust.MethodRequired`, `K.Trust.TargetKindUnsupported`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `401` | `K.Trust.AuthenticationStale` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `403` | `K.Trust.PersonRequired` |
| `404` | `K.Aggregate.NotFound` |
| `404` | `K.Trust.TargetUnknown` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `K.Protocol.InvalidTransition` |
| `409` | `K.Protocol.SimulationReviewRequired` |
| `409` | `K.Trust.TargetContentChanged` |
| `409` | `K.Trust.TargetNotSignable` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `K.Protocol.AttestationRejected` |
| `422` | `K.Trust.MethodRequired` |
| `422` | `K.Trust.TargetKindUnsupported` |

</details>

### `GET` `/api/v1/kernel/protocol-versions/{versionRef}/design-summary` {#op-kernelgetprotocoldesignsummary}

**Operation ID:** `KernelGetProtocolDesignSummary` — Returns a viewable summary of a protocol version's forms, ordered items, and import notes.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `versionRef` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelProtocolDraftSummaryResponse`](#schema-kernel-kernelprotocoldraftsummaryresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `POST` `/api/v1/kernel/protocol-versions/{versionRef}/effectivities` {#op-kerneldeclareeffectivity}

**Operation ID:** `KernelDeclareEffectivity` — Declares a Pending protocol-version effectivity for the whole study, a country, or one study site (effectivity.declare).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel protocol governance`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `versionRef` | `path` | `required` | `string` |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`optional`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_KernelDeclareEffectivityRequest`](#schema-kernel-kerneldeclareeffectivityrequest) \| `null` |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Kernel_KernelEffectivityResponse`](#schema-kernel-kerneleffectivityresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Effectivity.ProtocolVersionUnknown`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Effectivity.AlreadyDeclared`, `K.Effectivity.ProtocolVersionWithdrawn`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Effectivity.ScopeInvalid`, `K.Effectivity.StudySiteNotInStudy`, `K.Effectivity.TransitionTimeInvalid`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Effectivity.ProtocolVersionUnknown` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `K.Effectivity.AlreadyDeclared` |
| `409` | `K.Effectivity.ProtocolVersionWithdrawn` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `K.Effectivity.ScopeInvalid` |
| `422` | `K.Effectivity.StudySiteNotInStudy` |
| `422` | `K.Effectivity.TransitionTimeInvalid` |

</details>

### `GET` `/api/v1/kernel/protocol-versions/{versionRef}/impact` {#op-kernelamendmentimpact}

**Operation ID:** `KernelAmendmentImpact` — What depends on the version a protocol amendment changes (K-PROT-2).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `versionRef` | `path` | `required` | `string` |  |
| `depth` | `query` | `optional` | `integer` (`int32`) |  |
| `known_at` | `query` | `optional` | `string` (`date-time`) |  |
| `valid_at` | `query` | `optional` | `string` (`date-time`) |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelAmendmentImpactResponse`](#schema-kernel-kernelamendmentimpactresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `503` | Service Unavailable | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `POST` `/api/v1/kernel/protocol-versions/{versionRef}/return-to-draft` {#op-kernelreturnprotocolversiontodraft}

**Operation ID:** `KernelReturnProtocolVersionToDraft` — Returns an InReview protocol version to Draft, clearing its content hash so it can be revised (protocol.return_to_draft).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel protocol governance`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `versionRef` | `path` | `required` | `string` |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelProtocolVersionResponse`](#schema-kernel-kernelprotocolversionresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Aggregate.NotFound`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Protocol.InvalidTransition`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Aggregate.NotFound` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `K.Protocol.InvalidTransition` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |

</details>

### `GET` `/api/v1/kernel/protocol-versions/{versionRef}/schedule-of-activities` {#op-kernelgetscheduleofactivities}

**Operation ID:** `KernelGetScheduleOfActivities` — A protocol version's Schedule of Activities, compiled from its design.

Planned activities as columns grouped by epoch, collection specifications as rows (ADR-2475). A version the caller may not see is refused.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `versionRef` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelScheduleOfActivitiesResponse`](#schema-kernel-kernelscheduleofactivitiesresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `503` | Service Unavailable | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `POST` `/api/v1/kernel/protocol-versions/{versionRef}/submit-for-review` {#op-kernelsubmitprotocolversionforreview}

**Operation ID:** `KernelSubmitProtocolVersionForReview` — Freezes a draft protocol version's content, computes its content hash, and moves it to InReview (protocol.submit_for_review).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel protocol governance`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `versionRef` | `path` | `required` | `string` |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelProtocolVersionResponse`](#schema-kernel-kernelprotocolversionresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Aggregate.NotFound`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Protocol.InvalidTransition`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Protocol.ContentInvalid`, `K.Protocol.ContentRequired`, `K.Protocol.ReferenceUnknown`, `K.Protocol.UndeclaredImpactedElements`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Aggregate.NotFound` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `K.Protocol.InvalidTransition` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `K.Protocol.ContentInvalid` |
| `422` | `K.Protocol.ContentRequired` |
| `422` | `K.Protocol.ReferenceUnknown` |
| `422` | `K.Protocol.UndeclaredImpactedElements` |

</details>

### `POST` `/api/v1/kernel/protocols/matrix-gap-check` {#op-kernelmatrixgapcheck}

**Operation ID:** `KernelMatrixGapCheck` — Performs immediate real-time matrix gap check on a candidate protocol design graph.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_MatrixGapCheckRequest`](#schema-kernel-matrixgapcheckrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Kernel_KernelMatrixGapFindingResponse`](#schema-kernel-kernelmatrixgapfindingresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `GET` `/api/v1/kernel/search` {#op-kernelsearch}

**Operation ID:** `KernelSearch` — Finds kernel objects in the caller's tenant by code or title.

Each word matches as a prefix; an exact code ranks first. Sources that reveal identity are searched only when the caller holds the capability (ADR-2484).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `kind` | `query` | `optional` | array of `string` |  |
| `limit` | `query` | `optional` | `integer` (`int32`) |  |
| `q` | `query` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Kernel_KernelSearchHitResponse`](#schema-kernel-kernelsearchhitresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `503` | Service Unavailable | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `POST` `/api/v1/kernel/simulation/simulate` {#op-kernelsimulateprotocol}

**Operation ID:** `KernelSimulateProtocol` — Evaluates a candidate protocol version, mapping specification, or rule pack in memory at a bitemporal (valid_at, known_at) instant without database changes.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_SimulateProtocolRequest`](#schema-kernel-simulateprotocolrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelSimulationResponse`](#schema-kernel-kernelsimulationresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Simulation.LibraryItemUnknown`, `K.Simulation.StudyUnknown`, `K.Simulation.VersionUnknown`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Simulation.VersionStale`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Simulation.BaseNotFrozen`, `K.Simulation.ContentInvalid`, `K.Simulation.ElementIdDuplicated`, `K.Simulation.MappingSpecificationInvalid`, `K.Simulation.RulePackInvalid`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `404` | `K.Simulation.LibraryItemUnknown` |
| `404` | `K.Simulation.StudyUnknown` |
| `404` | `K.Simulation.VersionUnknown` |
| `409` | `K.Simulation.VersionStale` |
| `422` | `K.Simulation.BaseNotFrozen` |
| `422` | `K.Simulation.ContentInvalid` |
| `422` | `K.Simulation.ElementIdDuplicated` |
| `422` | `K.Simulation.MappingSpecificationInvalid` |
| `422` | `K.Simulation.RulePackInvalid` |

</details>

### `GET` `/api/v1/kernel/sites` {#op-kernellistsites}

**Operation ID:** `KernelListSites` — Lists visible investigational sites in the caller's tenant.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `country` | `query` | `optional` | `string` |  |
| `institutionRef` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Kernel_KernelSiteSummaryResponse`](#schema-kernel-kernelsitesummaryresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `POST` `/api/v1/kernel/sites` {#op-kernelcreatesite}

**Operation ID:** `KernelCreateSite` — Registers an investigational site under a site institution in the caller's tenant (site.register).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`optional`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_KernelCreateSiteRequest`](#schema-kernel-kernelcreatesiterequest) \| `null` |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Kernel_KernelSiteSummaryResponse`](#schema-kernel-kernelsitesummaryresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Identity.TenantUnknown`, `K.Study.InstitutionUnknown`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Study.SiteCodeTaken`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Identity.CodeRequired`, `K.Identity.NameRequired`, `K.Study.CodeRequired`, `K.Study.CountryInvalid`, `K.Study.NameRequired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Identity.TenantUnknown` |
| `404` | `K.Study.InstitutionUnknown` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `K.Study.SiteCodeTaken` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `K.Identity.CodeRequired` |
| `422` | `K.Identity.NameRequired` |
| `422` | `K.Study.CodeRequired` |
| `422` | `K.Study.CountryInvalid` |
| `422` | `K.Study.NameRequired` |

</details>

### `GET` `/api/v1/kernel/status` {#op-kernelstatus}

**Operation ID:** `KernelStatus` — Returns the Clinical Kernel API's operational status.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Kernel.Host`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_AnonymousTypeOfstringAndstring`](#schema-kernel-anonymoustypeofstringandstring) |

</details>

### `GET` `/api/v1/kernel/studies` {#op-kernelliststudies}

**Operation ID:** `KernelListStudies` — Lists visible studies in the caller's tenant.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Kernel_KernelStudySummaryResponse`](#schema-kernel-kernelstudysummaryresponse) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `POST` `/api/v1/kernel/studies` {#op-kernelcreatestudy}

**Operation ID:** `KernelCreateStudy` — Registers a new study in the caller's tenant with a signed reason for change (study.register) and optional one-step site link.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`optional`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_KernelCreateStudyRequest`](#schema-kernel-kernelcreatestudyrequest) \| `null` |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Kernel_KernelStudySummaryResponse`](#schema-kernel-kernelstudysummaryresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Identity.TenantUnknown`, `K.Study.SiteUnknown`, `K.Study.SponsorUnknown`, `K.Study.StudyUnknown`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Study.AlreadyLinked`, `K.Study.StudyCodeTaken`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Identity.CodeRequired`, `K.Identity.NameRequired`, `K.Study.CodeRequired`, `K.Study.TitleRequired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Identity.TenantUnknown` |
| `404` | `K.Study.SiteUnknown` |
| `404` | `K.Study.SponsorUnknown` |
| `404` | `K.Study.StudyUnknown` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `K.Study.AlreadyLinked` |
| `409` | `K.Study.StudyCodeTaken` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `K.Identity.CodeRequired` |
| `422` | `K.Identity.NameRequired` |
| `422` | `K.Study.CodeRequired` |
| `422` | `K.Study.TitleRequired` |

</details>

### `GET` `/api/v1/kernel/studies/{studyRef}/artifacts` {#op-kernelliststudyartifacts}

**Operation ID:** `KernelListStudyArtifacts` — Lists evidence artifacts registered for a study, newest first.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel protocol governance`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `studyRef` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Kernel_KernelArtifactResponse`](#schema-kernel-kernelartifactresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `POST` `/api/v1/kernel/studies/{studyRef}/artifacts` {#op-kernelregisterstudyartifact}

**Operation ID:** `KernelRegisterStudyArtifact` — Registers an immutable study evidence artifact version such as an ethics approval, regulatory approval, or CTA acknowledgement (artifact.register).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel protocol governance`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `studyRef` | `path` | `required` | `string` |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`optional`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_KernelRegisterArtifactRequest`](#schema-kernel-kernelregisterartifactrequest) \| `null` |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Kernel_KernelArtifactResponse`](#schema-kernel-kernelartifactresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Evidence.StudyUnknown`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Evidence.ContentInvalid`, `K.Evidence.KindInvalid`, `K.Evidence.TitleInvalid`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Evidence.StudyUnknown` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `K.Evidence.ContentInvalid` |
| `422` | `K.Evidence.KindInvalid` |
| `422` | `K.Evidence.TitleInvalid` |

</details>

### `GET` `/api/v1/kernel/studies/{studyRef}/attention` {#op-kernelgetstudyattention}

**Operation ID:** `KernelGetStudyAttention` — Lists a study's open proposals and active (Open or Investigating) validation findings in one attention read.

studyRef accepts a study NodeRef or study code. Returns the pinned study NodeRef, open proposals masked in place, and visible Open or Investigating findings up to limit (default 20).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel validation`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `studyRef` | `path` | `required` | `string` |  |
| `limit` | `query` | `optional` | `integer` (`int32`) |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelStudyAttentionResponse`](#schema-kernel-kernelstudyattentionresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `GET` `/api/v1/kernel/studies/{studyRef}/effectivities` {#op-kernelliststudyeffectivities}

**Operation ID:** `KernelListStudyEffectivities` — Lists bitemporal protocol-version effectivities declared for a study.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel protocol governance`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `studyRef` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Kernel_KernelEffectivityResponse`](#schema-kernel-kerneleffectivityresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `GET` `/api/v1/kernel/studies/{studyRef}/exports` {#op-kernelexportstudysnapshot}

**Operation ID:** `KernelExportStudySnapshot` — Streams a bitemporal study snapshot as CSV, CDISC ODM v1.3.2 XML, CDISC Dataset-JSON, SAS bundle ZIP, R bundle ZIP, or Study Archive ZIP.

Projects canonical kernel observations at as_of(valid_at, known_at), filtering to the caller's visible scope, reporting omitted scopes as a collapsed indicator, and masking blinded treatment values in place (K-TRUTH-1, K-AUTH-4, ADR-2590).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `studyRef` | `path` | `required` | `string` |  |
| `Idempotency-Key` | `header` | `optional` | `string` (maxLen: 255) | Optional caller-supplied idempotency key recorded on the ExportStudySnapshot audit command; when omitted, the kernel generates a deterministic v7 export key. |
| `as_of` | `query` | `optional` | `string` (`date-time`) |  |
| `format` | `query` | `optional` | `string` |  |
| `known_at` | `query` | `optional` | `string` (`date-time`) |  |
| `lock_filter` | `query` | `optional` | `string` |  |
| `valid_at` | `query` | `optional` | `string` (`date-time`) |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelDatasetJsonExportResponse`](#schema-kernel-kerneldatasetjsonexportresponse) |
| `200` | OK | `application/xml` | `string` |
| `200` | OK | `application/zip` | `string` (`binary`) |
| `200` | OK | `text/csv` | `string` |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `GET` `/api/v1/kernel/studies/{studyRef}/locks` {#op-kernelliststudylocks}

**Operation ID:** `KernelListStudyLocks` — Lists Frozen and Locked assertions recorded for a study at an optional bitemporal as_of cut.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel locks`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `studyRef` | `path` | `required` | `string` |  |
| `active_only` | `query` | `optional` | `boolean` |  |
| `as_of` | `query` | `optional` | `string` (`date-time`) |  |
| `known_at` | `query` | `optional` | `string` (`date-time`) |  |
| `valid_at` | `query` | `optional` | `string` (`date-time`) |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelStudyLocksResponse`](#schema-kernel-kernelstudylocksresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `GET` `/api/v1/kernel/studies/{studyRef}/locks/readiness` {#op-kernelgetstudylockreadiness}

**Operation ID:** `KernelGetStudyLockReadiness` — Evaluates lock readiness across queries, missing required items, unverified revisions, unsigned forms, unopened scheduled visits, and Error validation findings.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel locks`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `studyRef` | `path` | `required` | `string` |  |
| `scope_ref` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelLockReadinessResponse`](#schema-kernel-kernellockreadinessresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `GET` `/api/v1/kernel/studies/{studyRef}/proposals` {#op-kernellistproposals}

**Operation ID:** `KernelListProposals` — A study's proposals, newest first, optionally only those with one status (Open, Approved, Rejected, Withdrawn, Expired).

studyRef accepts a study NodeRef or study code. A study the caller may not see is refused; a proposal the caller may not see is masked in place (ADR-2471).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel proposals`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `studyRef` | `path` | `required` | `string` |  |
| `limit` | `query` | `optional` | `integer` (`int32`) |  |
| `status` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Kernel_KernelProposalResponse`](#schema-kernel-kernelproposalresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `POST` `/api/v1/kernel/studies/{studyRef}/protocol-imports` {#op-kernelimportprotocoldraft}

**Operation ID:** `KernelImportProtocolDraft` — Synchronously imports ODM v1.3.2 XML, USDM v4 JSON, or bundled eCRF templates into a protocol draft.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `studyRef` | `path` | `required` | `string` |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_KernelImportProtocolJsonRequest`](#schema-kernel-kernelimportprotocoljsonrequest) |
| `multipart/form-data` | [`Kernel_KernelImportProtocolJsonRequest`](#schema-kernel-kernelimportprotocoljsonrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelProtocolDraftSummaryResponse`](#schema-kernel-kernelprotocoldraftsummaryresponse) |
| `201` | Created | `application/json` | [`Kernel_KernelProtocolDraftSummaryResponse`](#schema-kernel-kernelprotocoldraftsummaryresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Aggregate.NotFound`, `K.Identity.TenantUnknown`, `K.Knowledge.CodeSystemUnknown`, `K.Protocol.StudyUnknown`, `K.Standards.StandardVersionUnknown`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Protocol.FirstVersionExists`, `K.Protocol.InvalidTransition`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `413` | Payload Too Large | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Identity.NameRequired`, `K.Knowledge.CodeSystemUriInvalid`, `K.Knowledge.ConceptLabelRequired`, `K.Knowledge.DuplicateEntry`, `K.Knowledge.EntriesInvalid`, `K.Knowledge.ExternalCodeMismatch`, `K.Knowledge.LicenseMismatch`, `K.Knowledge.PackageInvalid`, `K.Knowledge.VersionRequired`, `K.Protocol.AmendmentInvalid`, `K.Protocol.AmendmentNotAllowed`, `K.Protocol.ContentInvalid`, `K.Protocol.ContentRequired`, `K.Standards.LibraryItemInvalid`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Aggregate.NotFound` |
| `404` | `K.Identity.TenantUnknown` |
| `404` | `K.Knowledge.CodeSystemUnknown` |
| `404` | `K.Protocol.StudyUnknown` |
| `404` | `K.Standards.StandardVersionUnknown` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `K.Protocol.FirstVersionExists` |
| `409` | `K.Protocol.InvalidTransition` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `K.Identity.NameRequired` |
| `422` | `K.Knowledge.CodeSystemUriInvalid` |
| `422` | `K.Knowledge.ConceptLabelRequired` |
| `422` | `K.Knowledge.DuplicateEntry` |
| `422` | `K.Knowledge.EntriesInvalid` |
| `422` | `K.Knowledge.ExternalCodeMismatch` |
| `422` | `K.Knowledge.LicenseMismatch` |
| `422` | `K.Knowledge.PackageInvalid` |
| `422` | `K.Knowledge.VersionRequired` |
| `422` | `K.Protocol.AmendmentInvalid` |
| `422` | `K.Protocol.AmendmentNotAllowed` |
| `422` | `K.Protocol.ContentInvalid` |
| `422` | `K.Protocol.ContentRequired` |
| `422` | `K.Standards.LibraryItemInvalid` |

</details>

### `GET` `/api/v1/kernel/studies/{studyRef}/protocol-versions` {#op-kernellistprotocolversions}

**Operation ID:** `KernelListProtocolVersions` — A study's protocol versions, newest first, without their content.

A study the caller may not see is refused; a version the caller may not see is masked in place (ADR-2471).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `studyRef` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Kernel_KernelProtocolVersionResponse`](#schema-kernel-kernelprotocolversionresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `503` | Service Unavailable | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `GET` `/api/v1/kernel/studies/{studyRef}/rule-packs` {#op-kernelliststudyrulepacks}

**Operation ID:** `KernelListStudyRulePacks` — Lists rule-pack library items in the tenant and indicates which versions are pinned on the study.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel validation`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `studyRef` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelStudyRulePacksResponse`](#schema-kernel-kernelstudyrulepacksresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `POST` `/api/v1/kernel/studies/{studyRef}/rule-packs/{itemRef}/preview` {#op-kernelpreviewstudyrulepack}

**Operation ID:** `KernelPreviewStudyRulePack` — Evaluates a draft or approved rule pack in memory over the study's current collection records without mutating state.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel validation`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `itemRef` | `path` | `required` | `string` |  |
| `studyRef` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelRulePackPreviewResponse`](#schema-kernel-kernelrulepackpreviewresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Aggregate.NotFound`, `K.Standards.LibraryItemUnknown`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `404` | `K.Aggregate.NotFound` |
| `404` | `K.Standards.LibraryItemUnknown` |

</details>

### `POST` `/api/v1/kernel/studies/{studyRef}/simulation-reports` {#op-kernelsavesimulationreport}

**Operation ID:** `KernelSaveSimulationReport` — Registers an unmasked SimulationResult as an immutable SimulationReport artifact version for Part 11 review attestation (K-SIM-5).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `studyRef` | `path` | `required` | `string` |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`optional`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_SaveSimulationReportRequest`](#schema-kernel-savesimulationreportrequest) \| `null` |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Kernel_KernelArtifactResponse`](#schema-kernel-kernelartifactresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Evidence.StudyUnknown`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Evidence.ContentInvalid`, `K.Evidence.KindInvalid`, `K.Evidence.SimulationReportInvalid`, `K.Evidence.TitleInvalid`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Evidence.StudyUnknown` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `K.Evidence.ContentInvalid` |
| `422` | `K.Evidence.KindInvalid` |
| `422` | `K.Evidence.SimulationReportInvalid` |
| `422` | `K.Evidence.TitleInvalid` |

</details>

### `GET` `/api/v1/kernel/studies/{studyRef}/sites` {#op-kernelliststudysites}

**Operation ID:** `KernelListStudySites` — Lists visible investigational sites linked to a study together with their activation status and currently effective protocol versions.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `studyRef` | `path` | `required` | `string` |  |
| `known_at` | `query` | `optional` | `string` (`date-time`) |  |
| `valid_at` | `query` | `optional` | `string` (`date-time`) |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Kernel_KernelStudySiteSummaryResponse`](#schema-kernel-kernelstudysitesummaryresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `POST` `/api/v1/kernel/studies/{studyRef}/sites` {#op-kernellinksitetostudy}

**Operation ID:** `KernelLinkSiteToStudy` — Links an investigational site (registering it inline when site details are supplied) to a study in the caller's tenant (site.register, study_site.link).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `studyRef` | `path` | `required` | `string` |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`optional`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_KernelLinkSiteToStudyRequest`](#schema-kernel-kernellinksitetostudyrequest) \| `null` |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Kernel_KernelStudySiteSummaryResponse`](#schema-kernel-kernelstudysitesummaryresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Study.InstitutionUnknown`, `K.Study.SiteUnknown`, `K.Study.StudyUnknown`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Study.AlreadyLinked`, `K.Study.SiteCodeTaken`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Study.CodeRequired`, `K.Study.CountryInvalid`, `K.Study.NameRequired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Study.InstitutionUnknown` |
| `404` | `K.Study.SiteUnknown` |
| `404` | `K.Study.StudyUnknown` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `K.Study.AlreadyLinked` |
| `409` | `K.Study.SiteCodeTaken` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `K.Study.CodeRequired` |
| `422` | `K.Study.CountryInvalid` |
| `422` | `K.Study.NameRequired` |

</details>

### `PUT` `/api/v1/kernel/studies/{studyRef}/standards` {#op-kerneldeclarestudyrulepacks}

**Operation ID:** `KernelDeclareStudyRulePacks` — Declares or updates the approved rule-pack library items pinned on a study (study.declare_standards).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel validation`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `studyRef` | `path` | `required` | `string` |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`optional`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_KernelDeclareStudyRulePacksRequest`](#schema-kernel-kerneldeclarestudyrulepacksrequest) \| `null` |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelStudyRulePacksResponse`](#schema-kernel-kernelstudyrulepacksresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Standards.LibraryItemUnknown`, `K.Standards.StudyUnknown`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Standards.DeclarationInvalid`, `K.Standards.NotApproved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Standards.LibraryItemUnknown` |
| `404` | `K.Standards.StudyUnknown` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `K.Standards.DeclarationInvalid` |
| `422` | `K.Standards.NotApproved` |

</details>

### `GET` `/api/v1/kernel/studies/{studyRef}/validation/findings` {#op-kernelliststudyvalidationfindings}

**Operation ID:** `KernelListStudyValidationFindings` — Lists derived validation findings for a study, optionally filtered by stage, repeatable status, severity, rule pack or dataset.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel validation`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `studyRef` | `path` | `required` | `string` |  |
| `dataset` | `query` | `optional` | `string` |  |
| `limit` | `query` | `optional` | `integer` (`int32`) |  |
| `pack_key` | `query` | `optional` | `string` |  |
| `severity` | `query` | `optional` | `string` |  |
| `stage` | `query` | `optional` | `string` |  |
| `status` | `query` | `optional` | array of `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelValidationFindingPageResponse`](#schema-kernel-kernelvalidationfindingpageresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `POST` `/api/v1/kernel/study-sites/{studySiteRef}/activate` {#op-kernelactivatestudysite}

**Operation ID:** `KernelActivateStudySite` — Activates a kernel study site once at least one protocol version is effective there (study_site.activate, K-TIME-4).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel protocol governance`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `studySiteRef` | `path` | `required` | `string` |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelStudySiteResponse`](#schema-kernel-kernelstudysiteresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Effectivity.StudySiteUnknown`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Effectivity.NoEffectiveVersion`, `K.Effectivity.StudySiteAlreadyActive`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Effectivity.StudySiteUnknown` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `K.Effectivity.NoEffectiveVersion` |
| `409` | `K.Effectivity.StudySiteAlreadyActive` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |

</details>

### `GET` `/api/v1/kernel/validation/findings/{findingRef}` {#op-kernelgetvalidationfinding}

**Operation ID:** `KernelGetValidationFinding` — Returns one derived validation finding and its structured provenance explanation.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel validation`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `findingRef` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelValidationFindingDetailResponse`](#schema-kernel-kernelvalidationfindingdetailresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `POST` `/api/v1/kernel/validation/findings/{findingRef}/accept` {#op-kernelacceptvalidationfinding}

**Operation ID:** `KernelAcceptValidationFinding` — Accepts an open or investigating validation finding with a Responsibility electronic signature (validation_finding.accept).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel validation`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `findingRef` | `path` | `required` | `string` |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |
| `X-Sig-Token` | `header` | `required` | `string` (minLen: 1) | Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). |

**Request Body** (`optional`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_KernelAcceptFindingRequest`](#schema-kernel-kernelacceptfindingrequest) \| `null` |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelValidationFindingResponse`](#schema-kernel-kernelvalidationfindingresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`, `K.Trust.AuthenticationStale`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`, `K.Trust.PersonRequired`, `K.Validation.AttestationRejected`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Trust.TargetUnknown`, `K.Validation.FindingUnknown`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Trust.TargetContentChanged`, `K.Trust.TargetNotSignable`, `K.Validation.InvalidTransition`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Trust.MethodRequired`, `K.Trust.TargetKindUnsupported`, `K.Validation.DecisionTextInvalid`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `401` | `K.Trust.AuthenticationStale` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `403` | `K.Trust.PersonRequired` |
| `403` | `K.Validation.AttestationRejected` |
| `404` | `K.Trust.TargetUnknown` |
| `404` | `K.Validation.FindingUnknown` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `K.Trust.TargetContentChanged` |
| `409` | `K.Trust.TargetNotSignable` |
| `409` | `K.Validation.InvalidTransition` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `K.Trust.MethodRequired` |
| `422` | `K.Trust.TargetKindUnsupported` |
| `422` | `K.Validation.DecisionTextInvalid` |

</details>

### `POST` `/api/v1/kernel/validation/findings/{findingRef}/classify` {#op-kernelclassifyvalidationfinding}

**Operation ID:** `KernelClassifyValidationFinding` — Classifies a detected validation finding (validation_finding.classify).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel validation`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `findingRef` | `path` | `required` | `string` |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`optional`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_KernelClassifyFindingRequest`](#schema-kernel-kernelclassifyfindingrequest) \| `null` |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelValidationFindingResponse`](#schema-kernel-kernelvalidationfindingresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Validation.FindingUnknown`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Validation.InvalidTransition`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Validation.FindingUnknown` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `K.Validation.InvalidTransition` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |

</details>

### `POST` `/api/v1/kernel/validation/findings/{findingRef}/explain` {#op-kernelexplainvalidationfinding}

**Operation ID:** `KernelExplainValidationFinding` — Explains an open or investigating validation finding with an Approved electronic signature (validation_finding.explain).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel validation`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `findingRef` | `path` | `required` | `string` |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |
| `X-Sig-Token` | `header` | `required` | `string` (minLen: 1) | Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). |

**Request Body** (`optional`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_KernelExplainFindingRequest`](#schema-kernel-kernelexplainfindingrequest) \| `null` |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelValidationFindingResponse`](#schema-kernel-kernelvalidationfindingresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`, `K.Trust.AuthenticationStale`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`, `K.Trust.PersonRequired`, `K.Validation.AttestationRejected`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Trust.TargetUnknown`, `K.Validation.FindingUnknown`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Trust.TargetContentChanged`, `K.Trust.TargetNotSignable`, `K.Validation.InvalidTransition`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Trust.MethodRequired`, `K.Trust.TargetKindUnsupported`, `K.Validation.DecisionTextInvalid`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `401` | `K.Trust.AuthenticationStale` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `403` | `K.Trust.PersonRequired` |
| `403` | `K.Validation.AttestationRejected` |
| `404` | `K.Trust.TargetUnknown` |
| `404` | `K.Validation.FindingUnknown` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `K.Trust.TargetContentChanged` |
| `409` | `K.Trust.TargetNotSignable` |
| `409` | `K.Validation.InvalidTransition` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `K.Trust.MethodRequired` |
| `422` | `K.Trust.TargetKindUnsupported` |
| `422` | `K.Validation.DecisionTextInvalid` |

</details>

### `POST` `/api/v1/kernel/validation/findings/{findingRef}/investigate` {#op-kernelinvestigatevalidationfinding}

**Operation ID:** `KernelInvestigateValidationFinding` — Marks an open validation finding as under investigation (validation_finding.investigate).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel validation`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `findingRef` | `path` | `required` | `string` |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelValidationFindingResponse`](#schema-kernel-kernelvalidationfindingresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Validation.FindingUnknown`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Validation.InvalidTransition`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Validation.FindingUnknown` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `K.Validation.InvalidTransition` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |

</details>

### `POST` `/api/v1/kernel/validation/findings/{findingRef}/reopen` {#op-kernelreopenvalidationfinding}

**Operation ID:** `KernelReopenValidationFinding` — Withdraws an investigation, acceptance or explanation on a validation finding (validation_finding.reopen).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel validation`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `findingRef` | `path` | `required` | `string` |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelValidationFindingResponse`](#schema-kernel-kernelvalidationfindingresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Validation.FindingUnknown`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Validation.InvalidTransition`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Validation.FindingUnknown` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `K.Validation.InvalidTransition` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |

</details>

### `GET` `/api/v1/kernel/work-queues` {#op-kernelreadworkqueues}

**Operation ID:** `KernelReadWorkQueues` — Reads the caller's capability-keyed work queues in the current tenant or within a scope.

within is an optional NodeRef or study code (for example study:&lt;uuid&gt;, study_site:&lt;uuid&gt;, or CADENCE-101); code repeats to filter queue codes; limit is 1 to 200 (default 50). Restricted or blinded items remain in place with masked set to NotPermitted or Blinded.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel work queues`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `code` | `query` | `optional` | array of `string` |  |
| `limit` | `query` | `optional` | `integer` (`int32`) |  |
| `within` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelWorkQueuesResponse`](#schema-kernel-kernelworkqueuesresponse) |
| `400` | Bad Request. Kernel errors: `K.WorkQueue.QueryInvalid`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `400` | `K.WorkQueue.QueryInvalid` |
| `401` | `K.Actor.Unresolved` |

</details>

### `POST` `/api/v1/kernel/work-queues` {#op-kerneldefineworkqueue}

**Operation ID:** `KernelDefineWorkQueue` — Defines the next version of a capability-keyed work queue in the caller's tenant (work_queue.define).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel work queues`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`optional`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_KernelDefineWorkQueueRequest`](#schema-kernel-kerneldefineworkqueuerequest) \| `null` |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Kernel_KernelWorkQueueDefinitionResponse`](#schema-kernel-kernelworkqueuedefinitionresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.WorkQueue.CapabilityInvalid`, `K.WorkQueue.CodeInvalid`, `K.WorkQueue.SourceUnknown`, `K.WorkQueue.TitleInvalid`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `K.WorkQueue.CapabilityInvalid` |
| `422` | `K.WorkQueue.CodeInvalid` |
| `422` | `K.WorkQueue.SourceUnknown` |
| `422` | `K.WorkQueue.TitleInvalid` |

</details>

### `GET` `/healthz` {#op-kernelhealthcheck}

**Operation ID:** `KernelHealthCheck` — Clinical Kernel API health probe.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Kernel.Host`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_AnonymousTypeOfstringAndstring`](#schema-kernel-anonymoustypeofstringandstring) |

</details>

## Intelligence Module

Governed AI operations hosted on `kernel-api` are published in the dedicated [intelligence API](intelligence.md) contract (`/api/v1/ai/*`, including `POST /api/v1/ai/mcp`).

_No operations registered in this module group._

## Execution Module

Kernel-backed subject screening, clinical observation capture/correction/verification, assertion retraction, and participant casebook exports. Companion eCRF form and query endpoints hosted on `kernel-api` are documented in [execution_forms API](execution_forms.md).

| Method | Path | Operation | Summary | Contract |
| ------ | ---- | --------- | ------- | -------- |
| `POST` | `/api/v1/kernel/assertions/{assertionRef}/retraction` | KernelRetractAssertion | Retracts a verification, answer or query assertion with the signed reason for change (assertion.retract). | [Details](#op-kernelretractassertion) |
| `GET` | `/api/v1/kernel/enrollments/lookup` | KernelLookupEnrollment | Resolves a study code, subject code, and optional site code to pinned study and enrollment references. | [Details](#op-kernellookupenrollment) |
| `POST` | `/api/v1/kernel/enrollments/screen` | KernelScreenEnrollment | Registers a pseudonymous participant when needed and starts a screening enrollment at a study site (participant.screen). | [Details](#op-kernelscreenenrollment) |
| `GET` | `/api/v1/kernel/enrollments/{enrollmentRef}/casebook` | KernelExportEnrollmentCasebook | Streams a participant casebook with revisions, queries, verifications, attestations and SHA-256 digests. | [Details](#op-kernelexportenrollmentcasebook) |
| `POST` | `/api/v1/kernel/observations` | KernelRecordObservation | Records an observation for the participant of an enrollment (observation.record). | [Details](#op-kernelrecordobservation) |
| `GET` | `/api/v1/kernel/observations/{observationRef}` | KernelGetObservation | An observation with its revisions, the locks covering it, the assertions about it and its kernel audit entries. | [Details](#op-kernelgetobservation) |
| `POST` | `/api/v1/kernel/observations/{observationRef}/corrections` | KernelCorrectObservation | Corrects an observation's value with the signed reason for change (observation.correct). | [Details](#op-kernelcorrectobservation) |
| `POST` | `/api/v1/kernel/observations/{observationRef}/verifications` | KernelVerifyObservation | Verifies the current revision of an observation against source with the signed reason for change (observation.verify). | [Details](#op-kernelverifyobservation) |

### `POST` `/api/v1/kernel/assertions/{assertionRef}/retraction` {#op-kernelretractassertion}

**Operation ID:** `KernelRetractAssertion` — Retracts a verification, answer or query assertion with the signed reason for change (assertion.retract).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel observations`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `assertionRef` | `path` | `required` | `string` |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_KernelRetractAssertionRequest`](#schema-kernel-kernelretractassertionrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelAssertionStateResponse`](#schema-kernel-kernelassertionstateresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Aggregate.NotFound`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Assertion.AlreadyRetracted`, `K.Command.IdempotencyConflict`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Assertion.NotRetractable`, `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `423` | Locked. Kernel errors: `K.Assertion.Locked`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Aggregate.NotFound` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Assertion.AlreadyRetracted` |
| `409` | `K.Command.IdempotencyConflict` |
| `422` | `K.Assertion.NotRetractable` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `423` | `K.Assertion.Locked` |

</details>

### `GET` `/api/v1/kernel/enrollments/lookup` {#op-kernellookupenrollment}

**Operation ID:** `KernelLookupEnrollment` — Resolves a study code, subject code, and optional site code to pinned study and enrollment references.

Reads over the tenant's unique study and subject indexes without listing sites. Returns a non-confirming 404 when the study, site, or enrollment does not exist, mismatches, or is masked for the caller (ADR-2471).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `site_code` | `query` | `optional` | `string` |  |
| `study_code` | `query` | `optional` | `string` |  |
| `subject_code` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelEnrollmentLookupResponse`](#schema-kernel-kernelenrollmentlookupresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `503` | Service Unavailable | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `POST` `/api/v1/kernel/enrollments/screen` {#op-kernelscreenenrollment}

**Operation ID:** `KernelScreenEnrollment` — Registers a pseudonymous participant when needed and starts a screening enrollment at a study site (participant.screen).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel enrollments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_KernelScreenEnrollmentRequest`](#schema-kernel-kernelscreenenrollmentrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelEnrollmentStateResponse`](#schema-kernel-kernelenrollmentstateresponse) |
| `201` | Created | `application/json` | [`Kernel_KernelEnrollmentStateResponse`](#schema-kernel-kernelenrollmentstateresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Execution.ParticipantUnknown`, `K.Execution.StudySiteUnknown`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Execution.AlreadyActiveAtSite`, `K.Execution.SubjectCodeTaken`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Execution.SubjectCodeInvalid`, `K.Execution.TransitionTimeInvalid`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `423` | Locked. Kernel errors: `K.Observation.Locked`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Execution.ParticipantUnknown` |
| `404` | `K.Execution.StudySiteUnknown` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `K.Execution.AlreadyActiveAtSite` |
| `409` | `K.Execution.SubjectCodeTaken` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `K.Execution.SubjectCodeInvalid` |
| `422` | `K.Execution.TransitionTimeInvalid` |
| `423` | `K.Observation.Locked` |

</details>

### `GET` `/api/v1/kernel/enrollments/{enrollmentRef}/casebook` {#op-kernelexportenrollmentcasebook}

**Operation ID:** `KernelExportEnrollmentCasebook` — Streams a participant casebook with revisions, queries, verifications, attestations and SHA-256 digests.

Streams JSON, CDISC ODM v1.3.2 XML, printable HTML, or certified PDF (%PDF-1.7) for one enrollment at as_of(valid_at, known_at), masking unauthorized or blinded content under K-AUTH-4.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `enrollmentRef` | `path` | `required` | `string` |  |
| `Idempotency-Key` | `header` | `optional` | `string` (maxLen: 255) | Optional caller-supplied idempotency key recorded on the ExportEnrollmentCasebook audit command; when omitted, the kernel generates a deterministic v7 export key. |
| `as_of` | `query` | `optional` | `string` (`date-time`) |  |
| `format` | `query` | `optional` | `string` |  |
| `known_at` | `query` | `optional` | `string` (`date-time`) |  |
| `valid_at` | `query` | `optional` | `string` (`date-time`) |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelCasebookResponse`](#schema-kernel-kernelcasebookresponse) |
| `200` | OK | `application/pdf` | `string` (`binary`) |
| `200` | OK | `application/xml` | `string` |
| `200` | OK | `text/html` | `string` |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `POST` `/api/v1/kernel/observations` {#op-kernelrecordobservation}

**Operation ID:** `KernelRecordObservation` — Records an observation for the participant of an enrollment (observation.record).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel observations`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_KernelRecordObservationRequest`](#schema-kernel-kernelrecordobservationrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Kernel_KernelObservationStateResponse`](#schema-kernel-kernelobservationstateresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Observation.EnrollmentUnknown`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Observation.ParticipantWithdrawn`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Observation.ObservedAtInvalid`, `K.Observation.ValueInvalid`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `423` | Locked. Kernel errors: `K.Observation.Locked`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Observation.EnrollmentUnknown` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `K.Observation.ParticipantWithdrawn` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `K.Observation.ObservedAtInvalid` |
| `422` | `K.Observation.ValueInvalid` |
| `423` | `K.Observation.Locked` |

</details>

### `GET` `/api/v1/kernel/observations/{observationRef}` {#op-kernelgetobservation}

**Operation ID:** `KernelGetObservation` — An observation with its revisions, the locks covering it, the assertions about it and its kernel audit entries.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel observations`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `observationRef` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelObservationResponse`](#schema-kernel-kernelobservationresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

</details>

### `POST` `/api/v1/kernel/observations/{observationRef}/corrections` {#op-kernelcorrectobservation}

**Operation ID:** `KernelCorrectObservation` — Corrects an observation's value with the signed reason for change (observation.correct).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel observations`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `observationRef` | `path` | `required` | `string` |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_KernelCorrectObservationRequest`](#schema-kernel-kernelcorrectobservationrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Kernel_KernelObservationStateResponse`](#schema-kernel-kernelobservationstateresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Aggregate.NotFound`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `K.Observation.VersionStale`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Collection.ValuesInvalid`, `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Observation.ValueInvalid`, `K.Observation.ValueUnchanged`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `423` | Locked. Kernel errors: `K.Observation.Locked`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Aggregate.NotFound` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `K.Observation.VersionStale` |
| `422` | `K.Collection.ValuesInvalid` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `K.Observation.ValueInvalid` |
| `422` | `K.Observation.ValueUnchanged` |
| `423` | `K.Observation.Locked` |

</details>

### `POST` `/api/v1/kernel/observations/{observationRef}/verifications` {#op-kernelverifyobservation}

**Operation ID:** `KernelVerifyObservation` — Verifies the current revision of an observation against source with the signed reason for change (observation.verify).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Tags:** `Kernel observations`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `observationRef` | `path` | `required` | `string` |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Kernel_KernelVerifyObservationRequest`](#schema-kernel-kernelverifyobservationrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Kernel_KernelAssertionStateResponse`](#schema-kernel-kernelassertionstateresponse) |
| `400` | Bad Request | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Aggregate.NotFound`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Assertion.AlreadyVerified`, `K.Assertion.QueryOpen`, `K.Assertion.RevisionStale`, `K.Command.IdempotencyConflict`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |
| `423` | Locked. Kernel errors: `K.Assertion.Locked`. | `application/problem+json` | [`Kernel_ProblemDetails`](#schema-kernel-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Aggregate.NotFound` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Assertion.AlreadyVerified` |
| `409` | `K.Assertion.QueryOpen` |
| `409` | `K.Assertion.RevisionStale` |
| `409` | `K.Command.IdempotencyConflict` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `423` | `K.Assertion.Locked` |

</details>

## Schemas

Deduplicated component schemas exported in `components.schemas` (164 total). Operation payloads and responses link here so nested models are defined once per page.

### `Kernel_AnonymousTypeOfstringAndstring` {#schema-kernel-anonymoustypeofstringandstring}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `service` | `string` \| `null` | `required` |  |
| `status` | `string` \| `null` | `required` |  |

</details>

### `Kernel_AssertionId` {#schema-kernel-assertionid}

- **Type:** any

### `Kernel_AssertionKind` {#schema-kernel-assertionkind}

- **Type:** enum
- **Allowed Values:** `Verified`, `Reviewed`, `Queried`, `QueryAnswered`, `CodedAs`, `Adjudicated`, `Frozen`, `Locked`

### `Kernel_AssertionView` {#schema-kernel-assertionview}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (11)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `asserted_at` | `string` (`date-time`) | `required` |  |
| `asserted_by` | `string` | `required` |  |
| `assertion_id` | [`Kernel_AssertionId`](#schema-kernel-assertionid) | `required` |  |
| `kind` | [`Kernel_AssertionKind`](#schema-kernel-assertionkind) | `required` |  |
| `masked` | `boolean` | `optional` |  |
| `reason_for_change` | `string` | `required` |  |
| `retracted_at` | `string` (`date-time`) \| `null` | `required` |  |
| `retracted_by` | [`Kernel_AssertionId`](#schema-kernel-assertionid) \| `null` | `required` |  |
| `revision` | `integer` (`int32`) \| `null` | `required` |  |
| `target` | [`Kernel_NodeRef`](#schema-kernel-noderef) | `required` |  |
| `text` | `string` \| `null` | `required` |  |

</details>

### `Kernel_AttestationId` {#schema-kernel-attestationid}

- **Type:** any

### `Kernel_CodeSystemVersionId` {#schema-kernel-codesystemversionid}

- **Type:** any

### `Kernel_CodelistOptionView` {#schema-kernel-codelistoptionview}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `code` | `string` | `required` |  |
| `display` | `string` | `required` |  |
| `submission_value` | `string` \| `null` | `optional` |  |

</details>

### `Kernel_DateTimePrecision` {#schema-kernel-datetimeprecision}

- **Type:** enum
- **Allowed Values:** `Year`, `Month`, `Day`, `Minute`, `Second`

### `Kernel_EvaluatedCondition` {#schema-kernel-evaluatedcondition}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `code` | `string` | `required` |  |
| `description` | `string` | `required` |  |
| `evidence` | array of `any` | `required` |  |
| `met` | `boolean` | `required` |  |

</details>

### `Kernel_Explanation` {#schema-kernel-explanation}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `claim` | `string` | `required` |  |
| `conditions` | array of [`Kernel_EvaluatedCondition`](#schema-kernel-evaluatedcondition) | `required` |  |
| `next_actions` | array of [`Kernel_NextAction`](#schema-kernel-nextaction) | `required` |  |
| `producer` | `string` | `required` |  |
| `steps` | array of [`Kernel_ExplanationStep`](#schema-kernel-explanationstep) | `required` |  |
| `subject` | [`Kernel_NodeRef`](#schema-kernel-noderef) \| `null` | `required` |  |
| `unmet_conditions` | array of [`Kernel_EvaluatedCondition`](#schema-kernel-evaluatedcondition) \| `null` | `optional` |  |

</details>

### `Kernel_ExplanationStep` {#schema-kernel-explanationstep}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `claim` | `string` | `required` |  |
| `evidence` | array of `any` | `required` |  |

</details>

### `Kernel_HiddenObjects` {#schema-kernel-hiddenobjects}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `more_exist` | `boolean` | `required` |  |
| `reasons` | array of [`Kernel_MaskReason`](#schema-kernel-maskreason) | `required` |  |

</details>

### `Kernel_KernelAcceptFindingRequest` {#schema-kernel-kernelacceptfindingrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `attestation_id` | `string` (`uuid`) \| `null` | `optional` |  |
| `rationale` | `string` | `required` |  |

</details>

### `Kernel_KernelActivateSubscriptionRequest` {#schema-kernel-kernelactivatesubscriptionrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `active_study_count` | `integer` (`int32`) \| `null` | `optional` |  |
| `billing_confirmation` | `string` \| `null` | `optional` |  |
| `countries` | array of `string` \| `null` | `optional` |  |
| `participant_count` | `integer` (`int32`) \| `null` | `optional` |  |
| `site_count` | `integer` (`int32`) \| `null` | `optional` |  |
| `study_duration_months` | `integer` (`int32`) \| `null` | `optional` |  |
| `study_phase` | `string` \| `null` | `optional` |  |
| `tier` | `string` | `required` |  |

</details>

### `Kernel_KernelActivityEntryResponse` {#schema-kernel-kernelactivityentryresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (10)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `actor_id` | `string` | `required` |  |
| `correlation_id` | `string` (`uuid`) | `required` |  |
| `event_id` | `string` (`uuid`) | `required` |  |
| `family` | `string` | `required` |  |
| `mask` | `string` \| `null` | `required` |  |
| `occurred_valid` | `string` (`date-time`) \| `null` | `required` |  |
| `recorded` | `string` (`date-time`) | `required` |  |
| `subject` | `string` \| `null` | `required` |  |
| `subject_kind` | `string` \| `null` | `required` |  |
| `type` | `string` | `required` |  |

</details>

### `Kernel_KernelActivityPageResponse` {#schema-kernel-kernelactivitypageresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `entries` | array of [`Kernel_KernelActivityEntryResponse`](#schema-kernel-kernelactivityentryresponse) | `required` |  |
| `next` | `string` \| `null` | `required` |  |
| `nodes` | `object` \| `null` | `optional` |  |

</details>

### `Kernel_KernelAmendmentImpactResponse` {#schema-kernel-kernelamendmentimpactresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (12)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `active_site_count` | `integer` (`int32`) | `optional` |  |
| `affected_participant_count` | `integer` (`int32`) | `optional` |  |
| `amended` | `string` | `required` |  |
| `changed_elements` | array of [`Kernel_KernelElementChangeResponse`](#schema-kernel-kernelelementchangeresponse) | `required` |  |
| `dependents` | [`Kernel_KernelDependentsResponse`](#schema-kernel-kerneldependentsresponse) | `required` |  |
| `hidden` | [`Kernel_HiddenObjects`](#schema-kernel-hiddenobjects) \| `null` | `optional` |  |
| `impacted_elements` | array of `string` | `required` |  |
| `parent` | `string` | `required` |  |
| `pending_site_count` | `integer` (`int32`) | `optional` |  |
| `reconsent_participant_count` | `integer` (`int32`) | `optional` |  |
| `schedule` | [`Kernel_KernelSoaAmendmentDiffResponse`](#schema-kernel-kernelsoaamendmentdiffresponse) \| `null` | `required` |  |
| `total_participant_count` | `integer` (`int32`) | `optional` |  |

</details>

### `Kernel_KernelApplyLockRequest` {#schema-kernel-kernelapplylockrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `attestation_id` | `string` (`uuid`) \| `null` | `optional` |  |
| `kind` | `string` | `required` |  |
| `lock_id` | `string` (`uuid`) \| `null` | `optional` |  |
| `meaning` | `string` \| `null` | `optional` |  |
| `scope_ref` | `string` | `required` |  |
| `study_ref` | `string` | `required` |  |

</details>

### `Kernel_KernelApproveLibraryItemRequest` {#schema-kernel-kernelapprovelibraryitemrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `attestation_id` | `string` (`uuid`) \| `null` | `optional` |  |
| `meaning` | `string` \| `null` | `optional` |  |

</details>

### `Kernel_KernelApproveProtocolVersionRequest` {#schema-kernel-kernelapproveprotocolversionrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `attestation_id` | `string` (`uuid`) \| `null` | `optional` |  |
| `expected_content_hash` | `string` \| `null` | `optional` |  |
| `meaning` | `string` \| `null` | `optional` |  |
| `review_attestation_ref` | `string` \| `null` | `optional` |  |

</details>

### `Kernel_KernelArtifactResponse` {#schema-kernel-kernelartifactresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (13)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `artifact_id` | `string` (`uuid`) | `required` |  |
| `artifact_ref` | `string` | `required` |  |
| `artifact_version_id` | `string` (`uuid`) | `required` |  |
| `artifact_version_ref` | `string` | `required` |  |
| `content_hash` | `string` | `required` |  |
| `kind` | `string` | `required` |  |
| `media_type` | `string` | `required` |  |
| `recorded_at` | `string` (`date-time`) \| `null` | `required` |  |
| `size_bytes` | `integer` (`int64`) | `required` |  |
| `storage_ref` | `string` | `required` |  |
| `study_ref` | `string` \| `null` | `required` |  |
| `title` | `string` | `required` |  |
| `version_number` | `integer` (`int32`) | `required` |  |

</details>

### `Kernel_KernelAssertionStateResponse` {#schema-kernel-kernelassertionstateresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `asserted_at` | `string` (`date-time`) | `required` |  |
| `asserted_by` | `string` | `required` |  |
| `kind` | `string` | `required` |  |
| `ref` | `string` | `required` |  |
| `retracts` | `string` \| `null` | `required` |  |
| `revision` | `integer` (`int32`) \| `null` | `required` |  |
| `target` | `string` | `required` |  |

</details>

### `Kernel_KernelBatchNodesResponse` {#schema-kernel-kernelbatchnodesresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (1)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `nodes` | `object` | `required` |  |

</details>

### `Kernel_KernelCandidateSummaryResponse` {#schema-kernel-kernelcandidatesummaryresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `base_content_hash` | `string` \| `null` | `required` |  |
| `base_version` | `string` \| `null` | `required` |  |
| `content_hash` | `string` \| `null` | `required` |  |
| `kind` | `string` | `required` |  |
| `version` | `string` \| `null` | `required` |  |

</details>

### `Kernel_KernelCasebookAttestationView` {#schema-kernel-kernelcasebookattestationview}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (9)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `authenticated_at` | `string` (`date-time`) | `required` |  |
| `meaning` | `string` | `required` |  |
| `method` | `string` | `required` |  |
| `reason` | `string` | `required` |  |
| `ref` | `string` | `required` |  |
| `signed_at` | `string` (`date-time`) | `required` |  |
| `signer_id` | `string` | `required` |  |
| `target_content_hash` | `string` | `required` |  |
| `target_ref` | `string` | `required` |  |

</details>

### `Kernel_KernelCasebookFormView` {#schema-kernel-kernelcasebookformview}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (11)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `collected_at` | `string` (`date-time`) \| `null` | `required` |  |
| `collection_spec_id` | `string` | `required` |  |
| `content_hash` | `string` \| `null` | `required` |  |
| `mask` | `string` \| `null` | `required` |  |
| `masked` | `boolean` | `required` |  |
| `opened_at` | `string` (`date-time`) \| `null` | `required` |  |
| `opened_by` | `string` \| `null` | `required` |  |
| `planned_activity_id` | `string` \| `null` | `required` |  |
| `planned_activity_name` | `string` \| `null` | `required` |  |
| `ref` | `string` | `required` |  |
| `title` | `string` | `required` |  |

</details>

### `Kernel_KernelCasebookObservationView` {#schema-kernel-kernelcasebookobservationview}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (17)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `collection_instance_ref` | `string` \| `null` | `required` |  |
| `collection_spec_id` | `string` \| `null` | `required` |  |
| `concept_code` | `string` | `required` |  |
| `concept_label` | `string` | `required` |  |
| `concept_ref` | `string` | `required` |  |
| `current_revision` | `integer` (`int32`) \| `null` | `required` |  |
| `display_value` | `string` | `required` |  |
| `effective_lock` | `string` \| `null` | `required` |  |
| `item_id` | `string` \| `null` | `required` |  |
| `mask` | `string` \| `null` | `required` |  |
| `masked` | `boolean` | `required` |  |
| `numeric_value` | `number` (`double`) \| `null` | `required` |  |
| `observed_at` | `string` (`date-time`) \| `null` | `required` |  |
| `ref` | `string` | `required` |  |
| `revisions` | array of [`Kernel_KernelCasebookRevisionView`](#schema-kernel-kernelcasebookrevisionview) | `required` |  |
| `unit` | `string` \| `null` | `required` |  |
| `value_kind` | `string` | `required` |  |

</details>

### `Kernel_KernelCasebookQueryView` {#schema-kernel-kernelcasebookqueryview}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (10)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `asserted_at` | `string` (`date-time`) | `required` |  |
| `asserted_by` | `string` \| `null` | `required` |  |
| `closed` | `boolean` | `required` |  |
| `closed_at` | `string` (`date-time`) \| `null` | `required` |  |
| `kind` | `string` | `required` |  |
| `reason_for_change` | `string` \| `null` | `required` |  |
| `ref` | `string` | `required` |  |
| `revision` | `integer` (`int32`) \| `null` | `optional` |  |
| `target_ref` | `string` | `required` |  |
| `text` | `string` \| `null` | `required` |  |

</details>

### `Kernel_KernelCasebookResponse` {#schema-kernel-kernelcasebookresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (23)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `attestations` | array of [`Kernel_KernelCasebookAttestationView`](#schema-kernel-kernelcasebookattestationview) | `required` |  |
| `audit_trail` | array of [`Kernel_ObservationAuditView`](#schema-kernel-observationauditview) | `required` |  |
| `casebook_digest_sha256` | `string` | `required` |  |
| `enrollment_ref` | `string` | `required` |  |
| `enrollment_status` | `string` \| `null` | `required` |  |
| `forms` | array of [`Kernel_KernelCasebookFormView`](#schema-kernel-kernelcasebookformview) | `required` |  |
| `generated_at` | `string` (`date-time`) | `required` |  |
| `known_at` | `string` (`date-time`) | `required` |  |
| `locks` | array of [`Kernel_KernelLockResponse`](#schema-kernel-kernellockresponse) | `required` |  |
| `mask` | `string` \| `null` | `required` |  |
| `masked` | `boolean` | `required` |  |
| `observations` | array of [`Kernel_KernelCasebookObservationView`](#schema-kernel-kernelcasebookobservationview) | `required` |  |
| `producer` | `string` | `required` |  |
| `queries` | array of [`Kernel_KernelCasebookQueryView`](#schema-kernel-kernelcasebookqueryview) | `required` |  |
| `requested_by` | `string` | `required` |  |
| `site_code` | `string` | `required` |  |
| `site_ref` | `string` | `required` |  |
| `study_code` | `string` | `required` |  |
| `study_ref` | `string` | `required` |  |
| `study_title` | `string` | `required` |  |
| `subject_code` | `string` | `required` |  |
| `valid_at` | `string` (`date-time`) | `required` |  |
| `verifications` | array of [`Kernel_KernelCasebookVerificationView`](#schema-kernel-kernelcasebookverificationview) | `required` |  |

</details>

### `Kernel_KernelCasebookRevisionView` {#schema-kernel-kernelcasebookrevisionview}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (10)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `display_value` | `string` | `required` |  |
| `numeric_value` | `number` (`double`) \| `null` | `required` |  |
| `reason_for_change` | `string` \| `null` | `required` |  |
| `recorded_at` | `string` (`date-time`) | `required` |  |
| `recorded_by` | `string` \| `null` | `required` |  |
| `revision` | `integer` (`int32`) | `required` |  |
| `superseded` | `boolean` | `required` |  |
| `superseded_at` | `string` (`date-time`) \| `null` | `required` |  |
| `unit` | `string` \| `null` | `required` |  |
| `value_kind` | `string` | `required` |  |

</details>

### `Kernel_KernelCasebookVerificationView` {#schema-kernel-kernelcasebookverificationview}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `observation_ref` | `string` | `required` |  |
| `reason_for_change` | `string` \| `null` | `required` |  |
| `ref` | `string` | `required` |  |
| `retracted` | `boolean` | `required` |  |
| `retracted_at` | `string` (`date-time`) \| `null` | `required` |  |
| `revision` | `integer` (`int32`) \| `null` | `required` |  |
| `verified_at` | `string` (`date-time`) | `required` |  |
| `verified_by` | `string` \| `null` | `required` |  |

</details>

### `Kernel_KernelChangeEffectivityStatusRequest` {#schema-kernel-kernelchangeeffectivitystatusrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `effective_at` | `string` (`date-time`) \| `null` | `optional` |  |
| `evidence_artifact_version_refs` | array of `string` \| `null` | `optional` |  |
| `status` | `string` \| `null` | `required` |  |

</details>

### `Kernel_KernelChildResponse` {#schema-kernel-kernelchildresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `child_count` | `integer` (`int32`) \| `null` | `required` |  |
| `group` | `string` \| `null` | `required` |  |
| `node` | [`Kernel_KernelNodeResponse`](#schema-kernel-kernelnoderesponse) | `required` |  |

</details>

### `Kernel_KernelClassifyFindingRequest` {#schema-kernel-kernelclassifyfindingrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (1)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `classification` | `string` | `required` |  |

</details>

### `Kernel_KernelCommandResponse` {#schema-kernel-kernelcommandresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `command` | `string` | `required` |  |
| `decision` | [`Kernel_KernelDecisionResponse`](#schema-kernel-kerneldecisionresponse) | `required` |  |

</details>

### `Kernel_KernelConditionResponse` {#schema-kernel-kernelconditionresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `code` | `string` | `required` |  |
| `description` | `string` | `required` |  |
| `evidence` | array of `string` | `required` |  |
| `met` | `boolean` | `required` |  |

</details>

### `Kernel_KernelCorrectObservationRequest` {#schema-kernel-kernelcorrectobservationrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `enrollment_ref` | `string` | `required` |  |
| `expected_version` | `integer` (`int64`) | `required` |  |
| `value` | [`Kernel_ObservationValue`](#schema-kernel-observationvalue) \| `null` | `required` |  |

</details>

### `Kernel_KernelCreateOrganizationRequest` {#schema-kernel-kernelcreateorganizationrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `code` | `string` \| `null` | `optional` |  |
| `kind` | `string` \| `null` | `optional` |  |
| `name` | `string` | `required` |  |

</details>

### `Kernel_KernelCreateSiteRequest` {#schema-kernel-kernelcreatesiterequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `country` | `string` | `required` |  |
| `institution_code` | `string` \| `null` | `optional` |  |
| `institution_name` | `string` \| `null` | `optional` |  |
| `institution_ref` | `string` \| `null` | `optional` |  |
| `name` | `string` | `required` |  |
| `site_code` | `string` | `required` |  |

</details>

### `Kernel_KernelCreateStudyRequest` {#schema-kernel-kernelcreatestudyrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (9)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `initial_site_code` | `string` \| `null` | `optional` |  |
| `initial_site_ref` | `string` \| `null` | `optional` |  |
| `link_sandbox_site` | `boolean` | `optional` |  |
| `phase` | `string` | `required` |  |
| `sponsor_code` | `string` \| `null` | `optional` |  |
| `sponsor_name` | `string` \| `null` | `optional` |  |
| `sponsor_ref` | `string` \| `null` | `optional` |  |
| `study_code` | `string` | `required` |  |
| `title` | `string` | `required` |  |

</details>

### `Kernel_KernelDatasetJsonAsOf` {#schema-kernel-kerneldatasetjsonasof}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `known_at` | `string` (`date-time`) | `required` |  |
| `lock_filter` | `string` | `required` |  |
| `valid_at` | `string` (`date-time`) | `required` |  |

</details>

### `Kernel_KernelDatasetJsonExportResponse` {#schema-kernel-kerneldatasetjsonexportresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (10)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `asOf` | [`Kernel_KernelDatasetJsonAsOf`](#schema-kernel-kerneldatasetjsonasof) | `required` |  |
| `datasetJSONCreationDateTime` | `string` (`date-time`) | `required` |  |
| `datasetJSONVersion` | `string` | `required` |  |
| `fileOID` | `string` | `required` |  |
| `itemGroupData` | `object` | `required` |  |
| `metaDataRef` | `string` | `required` |  |
| `metaDataVersionOID` | `string` | `required` |  |
| `originator` | `string` | `required` |  |
| `sourceSystem` | [`Kernel_KernelDatasetJsonSourceSystem`](#schema-kernel-kerneldatasetjsonsourcesystem) | `required` |  |
| `studyOID` | `string` | `required` |  |

</details>

### `Kernel_KernelDatasetJsonSourceSystem` {#schema-kernel-kerneldatasetjsonsourcesystem}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `name` | `string` | `required` |  |
| `version` | `string` | `required` |  |

</details>

### `Kernel_KernelDecisionResponse` {#schema-kernel-kerneldecisionresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `capability` | `string` | `required` |  |
| `explanation` | [`Kernel_KernelExplanationResponse`](#schema-kernel-kernelexplanationresponse) | `required` |  |
| `permitted` | `boolean` | `required` |  |
| `reason` | `string` \| `null` | `required` |  |
| `resource` | `string` | `required` |  |
| `verb_class` | `string` | `required` |  |

</details>

### `Kernel_KernelDeclareEffectivityRequest` {#schema-kernel-kerneldeclareeffectivityrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `country` | `string` \| `null` | `optional` |  |
| `declared_at` | `string` (`date-time`) \| `null` | `optional` |  |
| `effectivity_id` | `string` (`uuid`) \| `null` | `optional` |  |
| `scope_level` | `string` \| `null` | `optional` |  |
| `study_site_ref` | `string` \| `null` | `optional` |  |

</details>

### `Kernel_KernelDeclareStudyRulePacksRequest` {#schema-kernel-kerneldeclarestudyrulepacksrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (1)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `rule_pack_refs` | array of `string` | `required` |  |

</details>

### `Kernel_KernelDefineWorkQueueRequest` {#schema-kernel-kerneldefineworkqueuerequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `capability` | `string` \| `null` | `required` |  |
| `code` | `string` \| `null` | `required` |  |
| `source` | `string` \| `null` | `required` |  |
| `title` | `string` \| `null` | `required` |  |
| `verb_class` | `string` \| `null` | `required` |  |
| `work_queue_definition_id` | `string` (`uuid`) \| `null` | `optional` |  |

</details>

### `Kernel_KernelDependentGroupResponse` {#schema-kernel-kerneldependentgroupresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `count` | `integer` (`int32`) | `required` |  |
| `kind` | `string` | `required` |  |
| `objects` | array of [`Kernel_KernelLineageNodeResponse`](#schema-kernel-kernellineagenoderesponse) | `required` |  |

</details>

### `Kernel_KernelDependentsResponse` {#schema-kernel-kerneldependentsresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `depth` | `integer` (`int32`) | `required` |  |
| `groups` | array of [`Kernel_KernelDependentGroupResponse`](#schema-kernel-kerneldependentgroupresponse) | `required` |  |
| `known_at` | `string` (`date-time`) | `required` |  |
| `start` | `string` | `required` |  |
| `total` | `integer` (`int32`) | `required` |  |
| `truncated` | `boolean` | `required` |  |
| `valid_at` | `string` (`date-time`) | `required` |  |

</details>

### `Kernel_KernelEffectiveVersionResponse` {#schema-kernel-kerneleffectiveversionresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `effectivity_ref` | `string` | `required` |  |
| `number` | `integer` (`int32`) | `required` |  |
| `protocol_version_ref` | `string` | `required` |  |
| `scope_level` | `string` | `required` |  |

</details>

### `Kernel_KernelEffectivityResponse` {#schema-kernel-kerneleffectivityresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (11)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `country` | `string` \| `null` | `required` |  |
| `effectivity_id` | `string` (`uuid`) | `required` |  |
| `effectivity_ref` | `string` | `required` |  |
| `evidence_artifact_version_refs` | array of `string` | `required` |  |
| `protocol_version_number` | `integer` (`int32`) \| `null` | `required` |  |
| `protocol_version_ref` | `string` | `required` |  |
| `scope_level` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `status_since` | `string` (`date-time`) | `required` |  |
| `study_ref` | `string` \| `null` | `required` |  |
| `study_site_ref` | `string` \| `null` | `required` |  |

</details>

### `Kernel_KernelElementChangeResponse` {#schema-kernel-kernelelementchangeresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `change` | `string` | `required` |  |
| `element_id` | `string` | `required` |  |
| `instance_type` | `string` \| `null` | `required` |  |
| `properties` | array of `string` | `required` |  |

</details>

### `Kernel_KernelEngineVersionResponse` {#schema-kernel-kernelengineversionresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `name` | `string` | `required` |  |
| `version` | `string` | `required` |  |

</details>

### `Kernel_KernelEnrollmentLookupResponse` {#schema-kernel-kernelenrollmentlookupresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (9)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `enrollment_ref` | `string` | `required` |  |
| `site_code` | `string` | `required` |  |
| `site_title` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `study_code` | `string` | `required` |  |
| `study_ref` | `string` | `required` |  |
| `study_site_ref` | `string` | `required` |  |
| `study_title` | `string` | `required` |  |
| `subject_code` | `string` | `required` |  |

</details>

### `Kernel_KernelEnrollmentStateResponse` {#schema-kernel-kernelenrollmentstateresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (10)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `content_hash` | `string` | `required` |  |
| `enrollment_id` | `string` (`uuid`) | `required` |  |
| `enrollment_number` | `integer` (`int32`) \| `null` | `required` |  |
| `enrollment_status` | `string` | `required` |  |
| `participant_id` | `string` (`uuid`) | `required` |  |
| `ref` | `string` | `required` |  |
| `status_since` | `string` (`date-time`) | `required` |  |
| `study_id` | `string` (`uuid`) | `required` |  |
| `study_site_id` | `string` (`uuid`) | `required` |  |
| `subject_code` | `string` | `required` |  |

</details>

### `Kernel_KernelEntitlementPostureResponse` {#schema-kernel-kernelentitlementpostureresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `is_read_only` | `boolean` | `required` |  |
| `product_families` | array of `string` | `required` |  |
| `scoping` | [`Kernel_KernelPostureScopingResponse`](#schema-kernel-kernelposturescopingresponse) \| `null` | `optional` |  |

</details>

### `Kernel_KernelEntitlementsResponse` {#schema-kernel-kernelentitlementsresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (12)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `baseline_capabilities` | array of `string` | `required` |  |
| `environment_notice` | `string` | `required` |  |
| `is_read_only` | `boolean` | `required` |  |
| `product_families` | array of `string` | `required` |  |
| `scoping` | [`Kernel_KernelSubscriptionScopingResponse`](#schema-kernel-kernelsubscriptionscopingresponse) \| `null` | `optional` |  |
| `started_at` | `string` (`date-time`) \| `null` | `required` |  |
| `status` | `string` | `required` |  |
| `subscription_ref` | `string` \| `null` | `required` |  |
| `tenant_id` | `string` (`uuid`) | `required` |  |
| `tenant_ref` | `string` | `required` |  |
| `tier` | `string` | `required` |  |
| `trial_ends_at` | `string` (`date-time`) \| `null` | `required` |  |

</details>

### `Kernel_KernelExplainFindingRequest` {#schema-kernel-kernelexplainfindingrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `attestation_id` | `string` (`uuid`) \| `null` | `optional` |  |
| `explanation` | `string` | `required` |  |

</details>

### `Kernel_KernelExplanationResponse` {#schema-kernel-kernelexplanationresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `claim` | `string` | `required` |  |
| `conditions` | array of [`Kernel_KernelConditionResponse`](#schema-kernel-kernelconditionresponse) | `required` |  |
| `next_actions` | array of [`Kernel_KernelNextActionResponse`](#schema-kernel-kernelnextactionresponse) | `required` |  |
| `producer` | `string` | `required` |  |
| `steps` | array of [`Kernel_KernelExplanationStepResponse`](#schema-kernel-kernelexplanationstepresponse) | `required` |  |
| `subject` | `string` \| `null` | `required` |  |

</details>

### `Kernel_KernelExplanationStepResponse` {#schema-kernel-kernelexplanationstepresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `claim` | `string` | `required` |  |
| `evidence` | array of `string` | `required` |  |

</details>

### `Kernel_KernelFindingDecisionResponse` {#schema-kernel-kernelfindingdecisionresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `attestation_ref` | `string` \| `null` | `required` |  |
| `decided_at` | `string` (`date-time`) | `required` |  |
| `decided_by` | `string` | `required` |  |
| `decided_on_run_ref` | `string` \| `null` | `required` |  |
| `status` | `string` | `required` |  |
| `text` | `string` \| `null` | `required` |  |

</details>

### `Kernel_KernelFindingObservedValueResponse` {#schema-kernel-kernelfindingobservedvalueresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `value` | `string` \| `null` | `required` |  |
| `variable` | `string` | `required` |  |

</details>

### `Kernel_KernelHiddenContentResponse` {#schema-kernel-kernelhiddencontentresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `more_exist` | `boolean` | `required` |  |
| `reasons` | array of `string` | `required` |  |

</details>

### `Kernel_KernelHiddenObjectsResponse` {#schema-kernel-kernelhiddenobjectsresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `more_exist` | `boolean` | `required` |  |
| `reasons` | array of `string` | `required` |  |

</details>

### `Kernel_KernelImportProtocolJsonRequest` {#schema-kernel-kernelimportprotocoljsonrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `amendment_classification` | `string` \| `null` | `optional` |  |
| `content` | `string` \| `null` | `optional` |  |
| `file_name` | `string` \| `null` | `optional` |  |
| `format` | `string` \| `null` | `optional` |  |
| `replace_forms` | `boolean` | `optional` |  |
| `template_ids` | array of `string` \| `null` | `optional` |  |
| `transition_policy` | `string` \| `null` | `optional` |  |

</details>

### `Kernel_KernelImportedRulePackSummaryResponse` {#schema-kernel-kernelimportedrulepacksummaryresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `key` | `string` | `required` |  |
| `ref` | `string` | `required` |  |
| `rule_count` | `integer` (`int32`) | `required` |  |
| `status` | `string` | `required` |  |
| `title` | `string` | `required` |  |
| `version` | `integer` (`int32`) | `required` |  |

</details>

### `Kernel_KernelLineageEdgeResponse` {#schema-kernel-kernellineageedgeresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `activity` | `string` (`uuid`) \| `null` | `required` |  |
| `from` | `string` | `required` |  |
| `kind` | `string` | `required` |  |
| `recorded_at` | `string` (`date-time`) | `required` |  |
| `to` | `string` | `required` |  |
| `valid_from` | `string` (`date-time`) \| `null` | `required` |  |

</details>

### `Kernel_KernelLineageNodeResponse` {#schema-kernel-kernellineagenoderesponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `distance` | `integer` (`int32`) | `required` |  |
| `handle` | `string` | `required` |  |
| `kind` | `string` | `required` |  |
| `mask` | `string` \| `null` | `required` |  |
| `ref` | `string` \| `null` | `required` |  |

</details>

### `Kernel_KernelLineageResponse` {#schema-kernel-kernellineageresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `depth` | `integer` (`int32`) | `required` |  |
| `direction` | `string` | `required` |  |
| `edges` | array of [`Kernel_KernelLineageEdgeResponse`](#schema-kernel-kernellineageedgeresponse) | `required` |  |
| `known_at` | `string` (`date-time`) | `required` |  |
| `nodes` | array of [`Kernel_KernelLineageNodeResponse`](#schema-kernel-kernellineagenoderesponse) | `required` |  |
| `start` | `string` | `required` |  |
| `truncated` | `boolean` | `required` |  |
| `valid_at` | `string` (`date-time`) | `required` |  |

</details>

### `Kernel_KernelLinkSiteToStudyRequest` {#schema-kernel-kernellinksitetostudyrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `country` | `string` \| `null` | `optional` |  |
| `institution_code` | `string` \| `null` | `optional` |  |
| `institution_ref` | `string` \| `null` | `optional` |  |
| `site_code` | `string` \| `null` | `optional` |  |
| `site_id` | `string` (`uuid`) \| `null` | `optional` |  |
| `site_name` | `string` \| `null` | `optional` |  |
| `site_ref` | `string` \| `null` | `optional` |  |
| `study_site_id` | `string` (`uuid`) \| `null` | `optional` |  |

</details>

### `Kernel_KernelLockBlockerResponse` {#schema-kernel-kernellockblockerresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (25)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `answered` | `boolean` | `required` |  |
| `collection_spec_id` | `string` \| `null` | `required` |  |
| `collection_title` | `string` \| `null` | `required` |  |
| `current_revision` | `integer` (`int32`) \| `null` | `optional` |  |
| `detail` | `string` \| `null` | `optional` |  |
| `finding_ref` | `string` \| `null` | `optional` |  |
| `item_id` | `string` \| `null` | `required` |  |
| `item_label` | `string` \| `null` | `required` |  |
| `kind` | `string` | `required` |  |
| `mask` | `string` \| `null` | `required` |  |
| `masked` | `boolean` | `required` |  |
| `next_action` | `string` | `required` |  |
| `next_capability` | `string` | `required` |  |
| `owner` | `string` | `required` |  |
| `planned_activity_id` | `string` \| `null` | `optional` |  |
| `planned_activity_name` | `string` \| `null` | `optional` |  |
| `planned_date` | `string` (`date`) \| `null` | `optional` |  |
| `query_ref` | `string` \| `null` | `required` |  |
| `query_text` | `string` \| `null` | `required` |  |
| `rule_id` | `string` \| `null` | `optional` |  |
| `signature_superseded` | `boolean` | `optional` |  |
| `subject_code` | `string` \| `null` | `required` |  |
| `target_kind` | `string` | `required` |  |
| `target_ref` | `string` \| `null` | `required` |  |
| `window_end` | `string` (`date`) \| `null` | `optional` |  |

</details>

### `Kernel_KernelLockReadinessResponse` {#schema-kernel-kernellockreadinessresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (15)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `blocker_count` | `integer` (`int32`) | `required` |  |
| `blockers` | array of [`Kernel_KernelLockBlockerResponse`](#schema-kernel-kernellockblockerresponse) | `required` |  |
| `can_freeze` | `boolean` | `required` |  |
| `explanation` | [`Kernel_KernelExplanationResponse`](#schema-kernel-kernelexplanationresponse) | `required` |  |
| `missing_required_item_count` | `integer` (`int32`) | `required` |  |
| `open_error_finding_count` | `integer` (`int32`) | `required` |  |
| `open_query_count` | `integer` (`int32`) | `required` |  |
| `ready_for_lock` | `boolean` | `required` |  |
| `scope_kind` | `string` | `required` |  |
| `scope_ref` | `string` | `required` |  |
| `study_code` | `string` | `required` |  |
| `study_ref` | `string` | `required` |  |
| `unopened_visit_count` | `integer` (`int32`) | `optional` |  |
| `unsigned_form_count` | `integer` (`int32`) | `optional` |  |
| `unverified_value_count` | `integer` (`int32`) | `optional` |  |

</details>

### `Kernel_KernelLockResponse` {#schema-kernel-kernellockresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (20)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `attestation_meaning` | `string` \| `null` | `required` |  |
| `attestation_ref` | `string` \| `null` | `required` |  |
| `enrollment_ref` | `string` \| `null` | `required` |  |
| `kind` | `string` \| `null` | `required` |  |
| `locked_at` | `string` (`date-time`) \| `null` | `required` |  |
| `locked_by` | `string` \| `null` | `required` |  |
| `mask` | `string` \| `null` | `required` |  |
| `masked` | `boolean` | `required` |  |
| `reason_for_change` | `string` \| `null` | `required` |  |
| `ref` | `string` \| `null` | `required` |  |
| `release_reason` | `string` \| `null` | `required` |  |
| `release_ref` | `string` \| `null` | `required` |  |
| `released` | `boolean` \| `null` | `required` |  |
| `released_at` | `string` (`date-time`) \| `null` | `required` |  |
| `released_by` | `string` \| `null` | `required` |  |
| `scope_kind` | `string` \| `null` | `required` |  |
| `scope_ref` | `string` \| `null` | `required` |  |
| `study_ref` | `string` | `required` |  |
| `study_site_ref` | `string` \| `null` | `required` |  |
| `subject_code` | `string` \| `null` | `required` |  |

</details>

### `Kernel_KernelMaskedPartResponse` {#schema-kernel-kernelmaskedpartresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `part` | `string` | `required` |  |
| `reason` | `string` | `required` |  |

</details>

### `Kernel_KernelMatrixGapFindingResponse` {#schema-kernel-kernelmatrixgapfindingresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `description` | `string` | `required` |  |
| `element_id` | `string` | `required` |  |
| `json_pointer_path` | `string` | `required` |  |
| `kind` | `string` | `required` |  |
| `recommendation` | `string` \| `null` | `required` |  |
| `severity` | `string` | `required` |  |

</details>

### `Kernel_KernelNextActionResponse` {#schema-kernel-kernelnextactionresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `capability` | `string` \| `null` | `required` |  |
| `description` | `string` | `required` |  |
| `target` | `string` \| `null` | `required` |  |

</details>

### `Kernel_KernelNodeResponse` {#schema-kernel-kernelnoderesponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `code` | `string` \| `null` | `required` |  |
| `kind` | `string` | `required` |  |
| `mask` | `string` \| `null` | `required` |  |
| `ref` | `string` \| `null` | `required` |  |
| `status` | `string` \| `null` | `required` |  |
| `title` | `string` \| `null` | `required` |  |

</details>

### `Kernel_KernelObservationResponse` {#schema-kernel-kernelobservationresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (17)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `assertions` | array of [`Kernel_AssertionView`](#schema-kernel-assertionview) | `required` |  |
| `audit` | array of [`Kernel_ObservationAuditView`](#schema-kernel-observationauditview) | `required` |  |
| `concept` | `string` | `required` |  |
| `enrollment_ref` | `string` | `required` |  |
| `expected_code_system_version_id` | `string` (`uuid`) \| `null` | `optional` |  |
| `expected_codelist` | `string` \| `null` | `optional` |  |
| `expected_codelist_options` | array of [`Kernel_CodelistOptionView`](#schema-kernel-codelistoptionview) \| `null` | `optional` |  |
| `expected_unit` | `string` \| `null` | `optional` |  |
| `expected_value_kind` | `string` \| `null` | `optional` |  |
| `form_ref` | `string` \| `null` | `optional` |  |
| `item_id` | `string` \| `null` | `optional` |  |
| `locks` | array of [`Kernel_LockView`](#schema-kernel-lockview) | `required` |  |
| `observed_at` | `string` (`date-time`) | `required` |  |
| `ref` | `string` | `required` |  |
| `revisions` | array of [`Kernel_ObservationRevisionView`](#schema-kernel-observationrevisionview) | `required` |  |
| `value` | [`Kernel_ObservationValue`](#schema-kernel-observationvalue) | `required` |  |
| `version` | `integer` (`int64`) | `required` |  |

</details>

### `Kernel_KernelObservationStateResponse` {#schema-kernel-kernelobservationstateresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `enrollment_ref` | `string` | `required` |  |
| `ref` | `string` | `required` |  |
| `revision` | `integer` (`int32`) | `required` |  |
| `value` | [`Kernel_ObservationValue`](#schema-kernel-observationvalue) | `required` |  |
| `version` | `integer` (`int64`) | `required` |  |

</details>

### `Kernel_KernelOperationalImpactResponse` {#schema-kernel-kerneloperationalimpactresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `active_site_count` | `integer` (`int32`) | `required` |  |
| `affected_participant_count` | `integer` (`int32`) | `required` |  |
| `hidden` | [`Kernel_KernelHiddenObjectsResponse`](#schema-kernel-kernelhiddenobjectsresponse) | `required` |  |
| `participant_impacts` | array of [`Kernel_KernelParticipantSimulationImpactResponse`](#schema-kernel-kernelparticipantsimulationimpactresponse) | `required` |  |
| `pending_site_count` | `integer` (`int32`) | `required` |  |
| `reconsent_participant_count` | `integer` (`int32`) | `required` |  |
| `total_participant_count` | `integer` (`int32`) | `required` |  |

</details>

### `Kernel_KernelOrganizationSummaryResponse` {#schema-kernel-kernelorganizationsummaryresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `code` | `string` | `required` |  |
| `kind` | `string` | `required` |  |
| `name` | `string` | `required` |  |
| `ref` | `string` | `required` |  |

</details>

### `Kernel_KernelParticipantPlannedVisitResponse` {#schema-kernel-kernelparticipantplannedvisitresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `target_date` | `string` (`date`) | `required` |  |
| `visit_id` | `string` | `required` |  |
| `window_end` | `string` (`date`) | `required` |  |
| `window_start` | `string` (`date`) | `required` |  |

</details>

### `Kernel_KernelParticipantScheduleShiftResponse` {#schema-kernel-kernelparticipantscheduleshiftresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `activity_id` | `string` | `required` |  |
| `after` | [`Kernel_KernelParticipantPlannedVisitResponse`](#schema-kernel-kernelparticipantplannedvisitresponse) \| `null` | `required` |  |
| `before` | [`Kernel_KernelParticipantPlannedVisitResponse`](#schema-kernel-kernelparticipantplannedvisitresponse) \| `null` | `required` |  |
| `change` | `string` | `required` |  |

</details>

### `Kernel_KernelParticipantSimulationImpactResponse` {#schema-kernel-kernelparticipantsimulationimpactresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `cohort` | `string` | `required` |  |
| `enrollment` | `string` | `required` |  |
| `requires_reconsent` | `boolean` | `required` |  |
| `schedule_shifts` | array of [`Kernel_KernelParticipantScheduleShiftResponse`](#schema-kernel-kernelparticipantscheduleshiftresponse) | `required` |  |
| `status` | `string` | `required` |  |
| `subject_code` | `string` | `required` |  |

</details>

### `Kernel_KernelPlannedVisitResponse` {#schema-kernel-kernelplannedvisitresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `activity_id` | `string` | `required` |  |
| `epoch_id` | `string` | `required` |  |
| `epoch_order` | `integer` (`int32`) | `required` |  |
| `name` | `string` | `required` |  |
| `study_day` | `integer` (`int32`) | `required` |  |
| `window_after` | `integer` (`int32`) | `required` |  |
| `window_before` | `integer` (`int32`) | `required` |  |

</details>

### `Kernel_KernelPostureScopingResponse` {#schema-kernel-kernelposturescopingresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `active_study_count` | `integer` (`int32`) | `required` |  |
| `countries` | array of `string` | `required` |  |
| `participant_count` | `integer` (`int32`) | `required` |  |
| `site_count` | `integer` (`int32`) | `required` |  |
| `study_duration_months` | `integer` (`int32`) | `required` |  |
| `study_phase` | `string` | `required` |  |

</details>

### `Kernel_KernelProjectionDatasetChangeResponse` {#schema-kernel-kernelprojectiondatasetchangeresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `added_count` | `integer` (`int32`) | `required` |  |
| `changed_count` | `integer` (`int32`) | `required` |  |
| `dataset` | `string` | `required` |  |
| `removed_count` | `integer` (`int32`) | `required` |  |
| `samples` | array of [`Kernel_KernelProjectionRecordChangeSampleResponse`](#schema-kernel-kernelprojectionrecordchangesampleresponse) | `required` |  |

</details>

### `Kernel_KernelProjectionRecordChangeSampleResponse` {#schema-kernel-kernelprojectionrecordchangesampleresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `after` | `object` \| `null` | `required` |  |
| `before` | `object` \| `null` | `required` |  |
| `change` | `string` | `required` |  |
| `record_key` | `string` | `required` |  |

</details>

### `Kernel_KernelProposalAffectedResponse` {#schema-kernel-kernelproposalaffectedresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `mask` | `string` \| `null` | `required` |  |
| `ref` | `string` | `required` |  |

</details>

### `Kernel_KernelProposalPlanResponse` {#schema-kernel-kernelproposalplanresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `content_hash` | `string` \| `null` | `required` |  |
| `mask` | `string` \| `null` | `required` |  |
| `media_type` | `string` \| `null` | `required` |  |
| `number` | `integer` (`int32`) \| `null` | `required` |  |
| `ref` | `string` | `required` |  |
| `title` | `string` \| `null` | `required` |  |

</details>

### `Kernel_KernelProposalResponse` {#schema-kernel-kernelproposalresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (24)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `affected` | array of [`Kernel_KernelProposalAffectedResponse`](#schema-kernel-kernelproposalaffectedresponse) \| `null` | `required` |  |
| `approval_attestation` | `string` \| `null` | `required` |  |
| `confidence_score` | `number` (`double`) \| `null` | `required` |  |
| `content_hash` | `string` \| `null` | `required` |  |
| `decided_at` | `string` (`date-time`) \| `null` | `required` |  |
| `decided_by` | `string` \| `null` | `required` |  |
| `decision_reason` | `string` \| `null` | `required` |  |
| `escalated_to` | `string` \| `null` | `required` |  |
| `escalation_reason` | `string` \| `null` | `required` |  |
| `expires_at` | `string` (`date-time`) \| `null` | `required` |  |
| `intent` | `string` \| `null` | `required` |  |
| `is_escalated` | `boolean` \| `null` | `required` |  |
| `mask` | `string` \| `null` | `required` |  |
| `model_id` | `string` \| `null` | `required` |  |
| `plan` | [`Kernel_KernelProposalPlanResponse`](#schema-kernel-kernelproposalplanresponse) \| `null` | `required` |  |
| `proposed_at` | `string` (`date-time`) \| `null` | `required` |  |
| `proposed_by` | `string` \| `null` | `required` |  |
| `proposed_by_kind` | `string` \| `null` | `required` |  |
| `ref` | `string` \| `null` | `required` |  |
| `requested_by` | `string` \| `null` | `required` |  |
| `requested_capabilities` | array of `string` \| `null` | `required` |  |
| `simulated_impact` | `string` \| `null` | `required` |  |
| `status` | `string` \| `null` | `required` |  |
| `study` | `string` \| `null` | `required` |  |

</details>

### `Kernel_KernelProposalStateResponse` {#schema-kernel-kernelproposalstateresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `content_hash` | `string` | `required` |  |
| `decided_at` | `string` (`date-time`) \| `null` | `required` |  |
| `decided_by` | `string` \| `null` | `required` |  |
| `ref` | `string` | `required` |  |
| `status` | `string` | `required` |  |

</details>

### `Kernel_KernelProtocolAmendmentResponse` {#schema-kernel-kernelprotocolamendmentresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `classification` | `string` | `required` |  |
| `impacted_elements` | array of `string` | `required` |  |
| `intent` | `string` | `required` |  |
| `rationale` | `string` | `required` |  |
| `summary_of_changes` | `string` | `required` |  |
| `transition_policy` | `string` | `required` |  |

</details>

### `Kernel_KernelProtocolDraftSummaryResponse` {#schema-kernel-kernelprotocoldraftsummaryresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `amendment` | [`Kernel_KernelProtocolAmendmentResponse`](#schema-kernel-kernelprotocolamendmentresponse) \| `null` | `optional` |  |
| `form_count` | `integer` (`int32`) | `required` |  |
| `forms` | array of [`Kernel_KernelProtocolFormSummaryResponse`](#schema-kernel-kernelprotocolformsummaryresponse) | `required` |  |
| `import_notes` | array of [`Kernel_KernelProtocolImportNoteResponse`](#schema-kernel-kernelprotocolimportnoteresponse) | `required` |  |
| `imported_rule_pack` | [`Kernel_KernelImportedRulePackSummaryResponse`](#schema-kernel-kernelimportedrulepacksummaryresponse) \| `null` | `optional` |  |
| `item_count` | `integer` (`int32`) | `required` |  |
| `title` | `string` | `required` |  |
| `version` | [`Kernel_KernelProtocolVersionResponse`](#schema-kernel-kernelprotocolversionresponse) | `required` |  |

</details>

### `Kernel_KernelProtocolFormItemResponse` {#schema-kernel-kernelprotocolformitemresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `concept` | `string` | `required` |  |
| `display_order` | `integer` (`int32`) | `required` |  |
| `id` | `string` | `required` |  |
| `prompt` | `string` | `required` |  |
| `required` | `boolean` | `required` |  |
| `unit` | `string` \| `null` | `required` |  |
| `value_kind` | `string` \| `null` | `required` |  |

</details>

### `Kernel_KernelProtocolFormSummaryResponse` {#schema-kernel-kernelprotocolformsummaryresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `display_order` | `integer` (`int32`) | `required` |  |
| `id` | `string` | `required` |  |
| `item_count` | `integer` (`int32`) | `required` |  |
| `items` | array of [`Kernel_KernelProtocolFormItemResponse`](#schema-kernel-kernelprotocolformitemresponse) | `required` |  |
| `name` | `string` | `required` |  |
| `required_item_count` | `integer` (`int32`) | `required` |  |

</details>

### `Kernel_KernelProtocolImportNoteResponse` {#schema-kernel-kernelprotocolimportnoteresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `message` | `string` | `required` |  |
| `source` | `string` | `required` |  |

</details>

### `Kernel_KernelProtocolTemplateResponse` {#schema-kernel-kernelprotocoltemplateresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `category` | `string` | `required` |  |
| `file_name` | `string` | `required` |  |
| `form_name` | `string` | `required` |  |
| `form_oid` | `string` | `required` |  |
| `id` | `string` | `required` |  |
| `item_count` | `integer` (`int32`) | `required` |  |

</details>

### `Kernel_KernelProtocolVersionResponse` {#schema-kernel-kernelprotocolversionresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (10)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `amendment_intent` | `string` \| `null` | `required` |  |
| `approval_attestation` | `string` \| `null` | `required` |  |
| `content_hash` | `string` \| `null` | `required` |  |
| `mask` | `string` \| `null` | `required` |  |
| `number` | `integer` (`int32`) \| `null` | `required` |  |
| `parent_version` | `string` \| `null` | `required` |  |
| `ref` | `string` \| `null` | `required` |  |
| `status` | `string` \| `null` | `required` |  |
| `study` | `string` \| `null` | `required` |  |
| `withdrawal_reason` | `string` \| `null` | `required` |  |

</details>

### `Kernel_KernelRecordObservationRequest` {#schema-kernel-kernelrecordobservationrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `concept` | `string` | `required` |  |
| `enrollment_ref` | `string` | `required` |  |
| `observation_id` | `string` (`uuid`) \| `null` | `required` |  |
| `observed_at` | `string` (`date-time`) | `required` |  |
| `value` | [`Kernel_ObservationValue`](#schema-kernel-observationvalue) \| `null` | `required` |  |

</details>

### `Kernel_KernelRegisterArtifactRequest` {#schema-kernel-kernelregisterartifactrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `artifact_id` | `string` (`uuid`) \| `null` | `optional` |  |
| `artifact_version_id` | `string` (`uuid`) \| `null` | `optional` |  |
| `content_hash` | `string` \| `null` | `required` |  |
| `kind` | `string` \| `null` | `required` |  |
| `media_type` | `string` \| `null` | `required` |  |
| `size_bytes` | `integer` (`int64`) | `required` |  |
| `storage_ref` | `string` \| `null` | `required` |  |
| `title` | `string` \| `null` | `required` |  |

</details>

### `Kernel_KernelReleaseLockRequest` {#schema-kernel-kernelreleaselockrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `assertion_id` | `string` (`uuid`) \| `null` | `optional` |  |
| `attestation_id` | `string` (`uuid`) \| `null` | `optional` |  |
| `meaning` | `string` \| `null` | `optional` |  |
| `study_ref` | `string` \| `null` | `optional` |  |

</details>

### `Kernel_KernelRetractAssertionRequest` {#schema-kernel-kernelretractassertionrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `assertion_id` | `string` (`uuid`) \| `null` | `optional` |  |
| `enrollment_ref` | `string` | `required` |  |

</details>

### `Kernel_KernelRulePackPreviewResponse` {#schema-kernel-kernelrulepackpreviewresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `forms_evaluated` | `integer` (`int32`) | `required` |  |
| `item_ref` | `string` | `required` |  |
| `rules_evaluated` | `integer` (`int32`) | `required` |  |
| `violation_count` | `integer` (`int32`) | `required` |  |
| `violations` | array of [`Kernel_KernelRulePackPreviewViolationResponse`](#schema-kernel-kernelrulepackpreviewviolationresponse) | `required` |  |

</details>

### `Kernel_KernelRulePackPreviewViolationResponse` {#schema-kernel-kernelrulepackpreviewviolationresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (9)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `dataset` | `string` | `required` |  |
| `form_id` | `string` (`uuid`) | `required` |  |
| `item_id` | `string` \| `null` | `required` |  |
| `message` | `string` | `required` |  |
| `pack_key` | `string` | `required` |  |
| `remediation_guidance` | `string` \| `null` | `required` |  |
| `rule_id` | `string` | `required` |  |
| `rule_version` | `integer` (`int32`) | `required` |  |
| `severity` | `string` | `required` |  |

</details>

### `Kernel_KernelRulePackResponse` {#schema-kernel-kernelrulepackresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (10)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `content_hash` | `string` | `required` |  |
| `description` | `string` \| `null` | `required` |  |
| `key` | `string` | `required` |  |
| `pinned` | `boolean` | `required` |  |
| `ref` | `string` | `required` |  |
| `rule_count` | `integer` (`int32`) | `required` |  |
| `rules` | array of [`Kernel_KernelRuleSummaryResponse`](#schema-kernel-kernelrulesummaryresponse) | `required` |  |
| `status` | `string` | `required` |  |
| `title` | `string` | `required` |  |
| `version` | `integer` (`int32`) | `required` |  |

</details>

### `Kernel_KernelRuleSummaryResponse` {#schema-kernel-kernelrulesummaryresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `condition` | `string` \| `null` | `optional` |  |
| `dataset` | `string` \| `null` | `required` |  |
| `explanation` | `string` | `required` |  |
| `id` | `string` | `required` |  |
| `remediation_guidance` | `string` \| `null` | `required` |  |
| `severity` | `string` | `required` |  |
| `stage` | `string` | `required` |  |
| `version` | `integer` (`int32`) | `required` |  |

</details>

### `Kernel_KernelScheduleChangeResponse` {#schema-kernel-kernelschedulechangeresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `activity_id` | `string` | `required` |  |
| `after` | [`Kernel_KernelPlannedVisitResponse`](#schema-kernel-kernelplannedvisitresponse) \| `null` | `required` |  |
| `before` | [`Kernel_KernelPlannedVisitResponse`](#schema-kernel-kernelplannedvisitresponse) \| `null` | `required` |  |
| `change` | `string` | `required` |  |

</details>

### `Kernel_KernelScheduleOfActivitiesResponse` {#schema-kernel-kernelscheduleofactivitiesresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `columns` | array of [`Kernel_KernelSoaColumnResponse`](#schema-kernel-kernelsoacolumnresponse) | `required` |  |
| `content_hash` | `string` \| `null` | `required` |  |
| `epochs` | array of [`Kernel_KernelSoaEpochResponse`](#schema-kernel-kernelsoaepochresponse) | `required` |  |
| `footnotes` | array of [`Kernel_KernelSoaFootnoteResponse`](#schema-kernel-kernelsoafootnoteresponse) | `required` |  |
| `hash` | `string` | `required` |  |
| `rows` | array of [`Kernel_KernelSoaRowResponse`](#schema-kernel-kernelsoarowresponse) | `required` |  |
| `title` | `string` | `required` |  |
| `version` | `string` | `required` |  |

</details>

### `Kernel_KernelScopeWriteLockResponse` {#schema-kernel-kernelscopewritelockresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `lock_kind` | `string` \| `null` | `required` |  |
| `locked` | `boolean` | `required` |  |
| `scope_kind` | `string` \| `null` | `required` |  |
| `study_exists` | `boolean` | `required` |  |

</details>

### `Kernel_KernelScreenEnrollmentRequest` {#schema-kernel-kernelscreenenrollmentrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `enrollment_id` | `string` (`uuid`) \| `null` | `required` |  |
| `participant_id` | `string` (`uuid`) \| `null` | `required` |  |
| `screened_at` | `string` (`date-time`) | `required` |  |
| `site_code` | `string` | `required` |  |
| `study_code` | `string` | `required` |  |
| `subject_code` | `string` | `required` |  |

</details>

### `Kernel_KernelSearchHitResponse` {#schema-kernel-kernelsearchhitresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `code` | `string` \| `null` | `required` |  |
| `detail` | `string` \| `null` | `required` |  |
| `exact_code` | `boolean` | `required` |  |
| `kind` | `string` | `required` |  |
| `rank` | `number` (`double`) | `required` |  |
| `ref` | `string` | `required` |  |
| `title` | `string` | `required` |  |

</details>

### `Kernel_KernelSimulatedValidationFindingResponse` {#schema-kernel-kernelsimulatedvalidationfindingresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `dataset` | `string` | `required` |  |
| `explanation` | `string` | `required` |  |
| `rule_id` | `string` | `required` |  |
| `rule_version` | `integer` (`int32`) | `required` |  |
| `severity` | `string` | `required` |  |
| `stage` | `string` | `required` |  |
| `subject` | `string` | `required` |  |

</details>

### `Kernel_KernelSimulationAsOfResponse` {#schema-kernel-kernelsimulationasofresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `known_at` | `string` (`date-time`) | `required` |  |
| `valid_at` | `string` (`date-time`) | `required` |  |

</details>

### `Kernel_KernelSimulationResponse` {#schema-kernel-kernelsimulationresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (15)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `as_of` | [`Kernel_KernelSimulationAsOfResponse`](#schema-kernel-kernelsimulationasofresponse) | `required` |  |
| `candidate` | [`Kernel_KernelCandidateSummaryResponse`](#schema-kernel-kernelcandidatesummaryresponse) | `required` |  |
| `dependents` | [`Kernel_KernelDependentsResponse`](#schema-kernel-kerneldependentsresponse) \| `null` | `required` |  |
| `engine_versions` | array of [`Kernel_KernelEngineVersionResponse`](#schema-kernel-kernelengineversionresponse) | `required` |  |
| `input_hash` | `string` | `required` |  |
| `is_empty` | `boolean` | `required` |  |
| `masked` | array of [`Kernel_KernelMaskedPartResponse`](#schema-kernel-kernelmaskedpartresponse) | `required` |  |
| `matrix_gaps` | array of [`Kernel_KernelMatrixGapFindingResponse`](#schema-kernel-kernelmatrixgapfindingresponse) | `required` |  |
| `operational_impact` | [`Kernel_KernelOperationalImpactResponse`](#schema-kernel-kerneloperationalimpactresponse) \| `null` | `optional` |  |
| `projection_changes` | array of [`Kernel_KernelProjectionDatasetChangeResponse`](#schema-kernel-kernelprojectiondatasetchangeresponse) \| `null` | `optional` |  |
| `result_hash` | `string` | `required` |  |
| `schedule_changes` | array of [`Kernel_KernelScheduleChangeResponse`](#schema-kernel-kernelschedulechangeresponse) | `required` |  |
| `scope` | `string` | `required` |  |
| `structural_diff` | array of [`Kernel_KernelElementChangeResponse`](#schema-kernel-kernelelementchangeresponse) | `required` |  |
| `validation_delta` | [`Kernel_KernelValidationDeltaResponse`](#schema-kernel-kernelvalidationdeltaresponse) \| `null` | `optional` |  |

</details>

### `Kernel_KernelSiteSummaryResponse` {#schema-kernel-kernelsitesummaryresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (9)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `code` | `string` | `required` |  |
| `country` | `string` | `required` |  |
| `institution_code` | `string` | `required` |  |
| `institution_name` | `string` | `required` |  |
| `institution_ref` | `string` | `required` |  |
| `is_active` | `boolean` | `optional` |  |
| `linked_study_count` | `integer` (`int32`) | `optional` |  |
| `name` | `string` | `required` |  |
| `ref` | `string` | `required` |  |

</details>

### `Kernel_KernelSoaAmendmentDiffResponse` {#schema-kernel-kernelsoaamendmentdiffresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `changes_eligibility` | `boolean` | `required` |  |
| `changes_schedule` | `boolean` | `required` |  |
| `directives` | array of [`Kernel_KernelSoaDirectiveResponse`](#schema-kernel-kernelsoadirectiveresponse) | `required` |  |
| `impacted_concepts` | array of `string` | `required` |  |
| `impacted_elements` | array of `string` | `required` |  |
| `undeclared` | array of `string` | `required` |  |

</details>

### `Kernel_KernelSoaColumnResponse` {#schema-kernel-kernelsoacolumnresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `activity_id` | `string` | `required` |  |
| `epoch_id` | `string` | `required` |  |
| `footnotes` | array of `integer` (`int32`) | `required` |  |
| `locations` | array of `string` | `required` |  |
| `name` | `string` | `required` |  |
| `study_day` | `integer` (`int32`) \| `null` | `required` |  |
| `window_after` | `integer` (`int32`) | `required` |  |
| `window_before` | `integer` (`int32`) | `required` |  |

</details>

### `Kernel_KernelSoaDirectiveResponse` {#schema-kernel-kernelsoadirectiveresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (10)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `after` | [`Kernel_KernelSoaVisitResponse`](#schema-kernel-kernelsoavisitresponse) \| `null` | `required` |  |
| `before` | [`Kernel_KernelSoaVisitResponse`](#schema-kernel-kernelsoavisitresponse) \| `null` | `required` |  |
| `collected_at_added` | array of `string` \| `null` | `required` |  |
| `collected_at_removed` | array of `string` \| `null` | `required` |  |
| `element_id` | `string` | `required` |  |
| `frequency_after` | `integer` (`int32`) \| `null` | `required` |  |
| `frequency_before` | `integer` (`int32`) \| `null` | `required` |  |
| `impacted_concepts` | array of `string` | `required` |  |
| `impacted_elements` | array of `string` | `required` |  |
| `kind` | `string` | `required` |  |

</details>

### `Kernel_KernelSoaEpochResponse` {#schema-kernel-kernelsoaepochresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `column_count` | `integer` (`int32`) | `required` |  |
| `epoch_id` | `string` | `required` |  |
| `first_column` | `integer` (`int32`) | `required` |  |
| `name` | `string` | `required` |  |
| `order` | `integer` (`int32`) | `required` |  |

</details>

### `Kernel_KernelSoaFootnoteResponse` {#schema-kernel-kernelsoafootnoteresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `kind` | `string` | `required` |  |
| `number` | `integer` (`int32`) | `required` |  |
| `text` | `string` | `required` |  |

</details>

### `Kernel_KernelSoaRowResponse` {#schema-kernel-kernelsoarowresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `collection_spec_id` | `string` | `required` |  |
| `columns` | array of `integer` (`int32`) | `required` |  |
| `concepts` | array of `string` | `required` |  |
| `footnotes` | array of `integer` (`int32`) | `required` |  |
| `frequency` | `integer` (`int32`) | `required` |  |
| `item_ids` | array of `string` | `required` |  |
| `name` | `string` | `required` |  |

</details>

### `Kernel_KernelSoaVisitResponse` {#schema-kernel-kernelsoavisitresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `epoch_id` | `string` | `required` |  |
| `locations` | array of `string` | `required` |  |
| `study_day` | `integer` (`int32`) \| `null` | `required` |  |
| `window_after` | `integer` (`int32`) | `required` |  |
| `window_before` | `integer` (`int32`) | `required` |  |

</details>

### `Kernel_KernelStudyAttentionResponse` {#schema-kernel-kernelstudyattentionresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `findings` | array of [`Kernel_KernelValidationFindingResponse`](#schema-kernel-kernelvalidationfindingresponse) | `required` |  |
| `proposals` | array of [`Kernel_KernelProposalResponse`](#schema-kernel-kernelproposalresponse) | `required` |  |
| `study_ref` | `string` | `required` |  |

</details>

### `Kernel_KernelStudyLocksResponse` {#schema-kernel-kernelstudylocksresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `hidden` | [`Kernel_KernelHiddenContentResponse`](#schema-kernel-kernelhiddencontentresponse) | `required` |  |
| `known_at` | `string` (`date-time`) | `required` |  |
| `locks` | array of [`Kernel_KernelLockResponse`](#schema-kernel-kernellockresponse) | `required` |  |
| `study_code` | `string` | `required` |  |
| `study_ref` | `string` | `required` |  |
| `valid_at` | `string` (`date-time`) | `required` |  |

</details>

### `Kernel_KernelStudyRulePacksResponse` {#schema-kernel-kernelstudyrulepacksresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `pinned_rule_pack_refs` | array of `string` | `required` |  |
| `profile_ref` | `string` \| `null` | `required` |  |
| `rule_packs` | array of [`Kernel_KernelRulePackResponse`](#schema-kernel-kernelrulepackresponse) | `required` |  |
| `study_ref` | `string` | `required` |  |

</details>

### `Kernel_KernelStudySiteResponse` {#schema-kernel-kernelstudysiteresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (9)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `country` | `string` \| `null` | `required` |  |
| `effective_versions` | array of [`Kernel_KernelEffectiveVersionResponse`](#schema-kernel-kerneleffectiveversionresponse) | `required` |  |
| `site_code` | `string` \| `null` | `required` |  |
| `site_name` | `string` \| `null` | `required` |  |
| `site_ref` | `string` \| `null` | `required` |  |
| `status` | `string` | `required` |  |
| `study_ref` | `string` \| `null` | `required` |  |
| `study_site_id` | `string` (`uuid`) | `required` |  |
| `study_site_ref` | `string` | `required` |  |

</details>

### `Kernel_KernelStudySiteSummaryResponse` {#schema-kernel-kernelstudysitesummaryresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (15)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `country` | `string` | `required` |  |
| `effective_versions` | array of [`Kernel_KernelEffectiveVersionResponse`](#schema-kernel-kerneleffectiveversionresponse) | `required` |  |
| `institution_code` | `string` | `required` |  |
| `institution_name` | `string` | `required` |  |
| `institution_ref` | `string` | `required` |  |
| `is_active` | `boolean` | `optional` |  |
| `ref` | `string` | `required` |  |
| `site_code` | `string` | `required` |  |
| `site_name` | `string` | `required` |  |
| `site_ref` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `study_code` | `string` | `optional` |  |
| `study_ref` | `string` | `required` |  |
| `study_site_id` | `string` (`uuid`) | `required` |  |
| `study_site_ref` | `string` | `required` |  |

</details>

### `Kernel_KernelStudySummaryResponse` {#schema-kernel-kernelstudysummaryresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (9)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `code` | `string` | `required` |  |
| `linked_site` | [`Kernel_KernelStudySiteSummaryResponse`](#schema-kernel-kernelstudysitesummaryresponse) \| `null` | `optional` |  |
| `phase` | `string` | `required` |  |
| `ref` | `string` | `required` |  |
| `site_count` | `integer` (`int32`) | `required` |  |
| `sponsor_code` | `string` | `required` |  |
| `sponsor_name` | `string` | `required` |  |
| `sponsor_ref` | `string` | `required` |  |
| `title` | `string` | `required` |  |

</details>

### `Kernel_KernelSubscriptionScopingResponse` {#schema-kernel-kernelsubscriptionscopingresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `active_study_count` | `integer` (`int32`) | `required` |  |
| `billing_confirmation` | `string` \| `null` | `optional` |  |
| `countries` | array of `string` | `required` |  |
| `participant_count` | `integer` (`int32`) | `required` |  |
| `site_count` | `integer` (`int32`) | `required` |  |
| `study_duration_months` | `integer` (`int32`) | `required` |  |
| `study_phase` | `string` | `required` |  |

</details>

### `Kernel_KernelTrialOnboardingRequest` {#schema-kernel-kerneltrialonboardingrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `organization_name` | `string` | `required` |  |
| `residency` | `string` \| `null` | `optional` |  |
| `tier` | `string` \| `null` | `optional` |  |

</details>

### `Kernel_KernelTrialOnboardingResponse` {#schema-kernel-kerneltrialonboardingresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (18)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `admin_grant_ref` | `string` | `required` |  |
| `admin_role_ref` | `string` | `required` |  |
| `assigned_roles` | array of `string` | `required` |  |
| `assigned_site_ids` | array of `string` | `required` |  |
| `entitlements` | [`Kernel_KernelEntitlementsResponse`](#schema-kernel-kernelentitlementsresponse) | `required` |  |
| `membership_ref` | `string` | `required` |  |
| `newly_provisioned` | `boolean` | `required` |  |
| `organization_ref` | `string` | `required` |  |
| `person_ref` | `string` | `required` |  |
| `sandbox_institution_code` | `string` | `required` |  |
| `sandbox_institution_ref` | `string` | `required` |  |
| `sandbox_site_code` | `string` | `required` |  |
| `sandbox_site_country` | `string` | `required` |  |
| `sandbox_site_name` | `string` | `required` |  |
| `sandbox_site_ref` | `string` | `required` |  |
| `tenant_code` | `string` | `required` |  |
| `tenant_ref` | `string` | `required` |  |
| `token_refresh_required` | `boolean` | `required` |  |

</details>

### `Kernel_KernelUpgradeSubscriptionRequest` {#schema-kernel-kernelupgradesubscriptionrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (1)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `tier` | `string` | `required` |  |

</details>

### `Kernel_KernelValidationDeltaResponse` {#schema-kernel-kernelvalidationdeltaresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `opening` | array of [`Kernel_KernelSimulatedValidationFindingResponse`](#schema-kernel-kernelsimulatedvalidationfindingresponse) | `required` |  |
| `resolving` | array of [`Kernel_KernelSimulatedValidationFindingResponse`](#schema-kernel-kernelsimulatedvalidationfindingresponse) | `required` |  |
| `unchanged_count` | `integer` (`int32`) | `required` |  |

</details>

### `Kernel_KernelValidationFindingDetailResponse` {#schema-kernel-kernelvalidationfindingdetailresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `explanation` | [`Kernel_Explanation`](#schema-kernel-explanation) | `required` |  |
| `finding` | [`Kernel_KernelValidationFindingResponse`](#schema-kernel-kernelvalidationfindingresponse) | `required` |  |

</details>

### `Kernel_KernelValidationFindingPageResponse` {#schema-kernel-kernelvalidationfindingpageresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `findings` | array of [`Kernel_KernelValidationFindingResponse`](#schema-kernel-kernelvalidationfindingresponse) | `required` |  |
| `total` | `integer` (`int32`) | `required` |  |

</details>

### `Kernel_KernelValidationFindingResponse` {#schema-kernel-kernelvalidationfindingresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (22)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `blocks` | `boolean` | `required` |  |
| `classification` | `string` \| `null` | `required` |  |
| `dataset` | `string` | `required` |  |
| `decision` | [`Kernel_KernelFindingDecisionResponse`](#schema-kernel-kernelfindingdecisionresponse) \| `null` | `optional` |  |
| `expected` | `string` \| `null` | `required` |  |
| `first_detected_run_ref` | `string` | `required` |  |
| `last_detected_run_ref` | `string` | `required` |  |
| `message` | `string` | `required` |  |
| `observed` | array of [`Kernel_KernelFindingObservedValueResponse`](#schema-kernel-kernelfindingobservedvalueresponse) | `required` |  |
| `pack_key` | `string` | `required` |  |
| `record_key` | `string` | `required` |  |
| `ref` | `string` | `required` |  |
| `resolved_by_run_ref` | `string` \| `null` | `required` |  |
| `rule_id` | `string` | `required` |  |
| `rule_pack_ref` | `string` | `required` |  |
| `rule_version` | `integer` (`int32`) | `required` |  |
| `severity` | `string` | `required` |  |
| `stage` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `study_ref` | `string` | `required` |  |
| `target_kind` | `string` | `required` |  |
| `variable` | `string` \| `null` | `required` |  |

</details>

### `Kernel_KernelVerifyObservationRequest` {#schema-kernel-kernelverifyobservationrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `assertion_id` | `string` (`uuid`) \| `null` | `optional` |  |
| `enrollment_ref` | `string` | `required` |  |
| `revision` | `integer` (`int32`) | `required` |  |

</details>

### `Kernel_KernelWorkItemResponse` {#schema-kernel-kernelworkitemresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `masked` | `string` \| `null` | `required` |  |
| `since` | `string` (`date-time`) | `required` |  |
| `subject` | `string` \| `null` | `required` |  |
| `subject_kind` | `string` | `required` |  |
| `target` | [`Kernel_KernelWorkItemTargetResponse`](#schema-kernel-kernelworkitemtargetresponse) \| `null` | `required` |  |
| `title` | `string` \| `null` | `required` |  |

</details>

### `Kernel_KernelWorkItemTargetResponse` {#schema-kernel-kernelworkitemtargetresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `enrollment_id` | `string` (`uuid`) \| `null` | `required` |  |
| `finding_ref` | `string` \| `null` | `required` |  |
| `form_id` | `string` (`uuid`) \| `null` | `required` |  |
| `observation_ref` | `string` \| `null` | `required` |  |
| `proposal_ref` | `string` \| `null` | `optional` |  |
| `protocol_version_ref` | `string` \| `null` | `required` |  |
| `study_id` | `string` (`uuid`) \| `null` | `required` |  |
| `study_ref` | `string` \| `null` | `required` |  |

</details>

### `Kernel_KernelWorkQueueDefinitionResponse` {#schema-kernel-kernelworkqueuedefinitionresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `capability` | `string` | `required` |  |
| `code` | `string` | `required` |  |
| `definition` | `string` | `required` |  |
| `source` | `string` | `required` |  |
| `title` | `string` | `required` |  |
| `verb_class` | `string` | `required` |  |
| `version` | `integer` (`int32`) | `required` |  |

</details>

### `Kernel_KernelWorkQueueResponse` {#schema-kernel-kernelworkqueueresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `capability` | `string` | `required` |  |
| `code` | `string` | `required` |  |
| `definition` | `string` | `required` |  |
| `items` | array of [`Kernel_KernelWorkItemResponse`](#schema-kernel-kernelworkitemresponse) | `required` |  |
| `title` | `string` | `required` |  |
| `truncated` | `boolean` | `required` |  |
| `version` | `integer` (`int32`) | `required` |  |

</details>

### `Kernel_KernelWorkQueuesResponse` {#schema-kernel-kernelworkqueuesresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (1)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `queues` | array of [`Kernel_KernelWorkQueueResponse`](#schema-kernel-kernelworkqueueresponse) | `required` |  |

</details>

### `Kernel_KernelZoomStepResponse` {#schema-kernel-kernelzoomstepresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `label` | `string` \| `null` | `required` |  |
| `level` | `string` | `required` |  |
| `mask` | `string` \| `null` | `required` |  |
| `ref` | `string` \| `null` | `required` |  |

</details>

### `Kernel_LockKind` {#schema-kernel-lockkind}

- **Type:** enum
- **Allowed Values:** `Frozen`, `Locked`

### `Kernel_LockView` {#schema-kernel-lockview}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `attestation_id` | [`Kernel_AttestationId`](#schema-kernel-attestationid) | `required` |  |
| `kind` | [`Kernel_LockKind`](#schema-kernel-lockkind) | `required` |  |
| `lock_id` | [`Kernel_AssertionId`](#schema-kernel-assertionid) | `required` |  |
| `locked_at` | `string` (`date-time`) | `required` |  |
| `locked_by` | `string` | `required` |  |
| `masked` | `boolean` | `optional` |  |
| `reason_for_change` | `string` | `required` |  |
| `scope` | [`Kernel_NodeRef`](#schema-kernel-noderef) | `required` |  |

</details>

### `Kernel_MaskReason` {#schema-kernel-maskreason}

- **Type:** integer

### `Kernel_MatrixGapCheckRequest` {#schema-kernel-matrixgapcheckrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (1)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `protocol_content` | `string` | `required` |  |

</details>

### `Kernel_NextAction` {#schema-kernel-nextaction}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `capability` | `string` \| `null` | `optional` |  |
| `description` | `string` | `required` |  |
| `target` | `any` | `optional` |  |

</details>

### `Kernel_NodeRef` {#schema-kernel-noderef}

- **Type:** any

### `Kernel_NullFlavor` {#schema-kernel-nullflavor}

- **Type:** enum
- **Allowed Values:** `NotDone`, `Unknown`, `AskedButUnknown`, `NotAsked`, `NotApplicable`, `Masked`

### `Kernel_ObservationAuditView` {#schema-kernel-observationauditview}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `action` | `string` | `required` |  |
| `actor_id` | `string` | `required` |  |
| `reason_for_change` | `string` | `required` |  |
| `recorded_at` | `string` (`date-time`) | `required` |  |
| `sequence` | `integer` (`int64`) | `required` |  |
| `subject` | `string` | `required` |  |
| `version` | `integer` (`int64`) \| `null` | `required` |  |

</details>

### `Kernel_ObservationRevisionView` {#schema-kernel-observationrevisionview}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `number` | `integer` (`int32`) | `required` |  |
| `reason_for_change` | `string` | `required` |  |
| `recorded_at` | `string` (`date-time`) | `required` |  |
| `recorded_by` | `string` | `required` |  |
| `recorded_until` | `string` (`date-time`) \| `null` | `required` |  |
| `value` | [`Kernel_ObservationValue`](#schema-kernel-observationvalue) | `required` |  |

</details>

### `Kernel_ObservationValue` {#schema-kernel-observationvalue}

- **Type:** object
- **Composition:** `anyOf`: [`Kernel_ObservationValueQuantityValue`](#schema-kernel-observationvaluequantityvalue), [`Kernel_ObservationValueCodedValue`](#schema-kernel-observationvaluecodedvalue), [`Kernel_ObservationValueTextValue`](#schema-kernel-observationvaluetextvalue), [`Kernel_ObservationValueBooleanValue`](#schema-kernel-observationvaluebooleanvalue), [`Kernel_ObservationValueDateTimeValue`](#schema-kernel-observationvaluedatetimevalue), [`Kernel_ObservationValueMissingValue`](#schema-kernel-observationvaluemissingvalue)

### `Kernel_ObservationValueBooleanValue` {#schema-kernel-observationvaluebooleanvalue}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `type` | `string` (enum: `boolean`) | `optional` |  |
| `value` | `boolean` | `required` |  |

</details>

### `Kernel_ObservationValueCodedValue` {#schema-kernel-observationvaluecodedvalue}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `code` | `string` | `required` |  |
| `code_system_version_id` | [`Kernel_CodeSystemVersionId`](#schema-kernel-codesystemversionid) | `required` |  |
| `type` | `string` (enum: `coded`) | `optional` |  |

</details>

### `Kernel_ObservationValueDateTimeValue` {#schema-kernel-observationvaluedatetimevalue}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `precision` | [`Kernel_DateTimePrecision`](#schema-kernel-datetimeprecision) | `optional` |  |
| `type` | `string` (enum: `dateTime`) | `optional` |  |
| `value` | `string` | `required` |  |

</details>

### `Kernel_ObservationValueMissingValue` {#schema-kernel-observationvaluemissingvalue}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `detail` | `string` \| `null` | `optional` |  |
| `reason` | [`Kernel_NullFlavor`](#schema-kernel-nullflavor) | `required` |  |
| `type` | `string` (enum: `missing`) | `optional` |  |

</details>

### `Kernel_ObservationValueQuantityValue` {#schema-kernel-observationvaluequantityvalue}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `comparator` | [`Kernel_QuantityComparator`](#schema-kernel-quantitycomparator) \| `null` | `optional` |  |
| `type` | `string` (enum: `quantity`) | `optional` |  |
| `unit` | `string` | `required` |  |
| `value` | `number` (`double`) | `required` |  |

</details>

### `Kernel_ObservationValueTextValue` {#schema-kernel-observationvaluetextvalue}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `type` | `string` (enum: `text`) | `optional` |  |
| `value` | `string` | `required` |  |

</details>

### `Kernel_ProblemDetails` {#schema-kernel-problemdetails}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `detail` | `string` \| `null` | `optional` |  |
| `instance` | `string` \| `null` | `optional` |  |
| `status` | `integer` (`int32`) \| `null` | `optional` |  |
| `title` | `string` \| `null` | `optional` |  |
| `type` | `string` \| `null` | `optional` |  |

</details>

### `Kernel_QuantityComparator` {#schema-kernel-quantitycomparator}

- **Type:** enum
- **Allowed Values:** `<`, `<=`, `>`, `>=`

### `Kernel_SaveSimulationReportRequest` {#schema-kernel-savesimulationreportrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `artifact_id` | `string` (`uuid`) \| `null` | `optional` |  |
| `artifact_version_id` | `string` (`uuid`) \| `null` | `optional` |  |
| `result` | [`Kernel_KernelSimulationResponse`](#schema-kernel-kernelsimulationresponse) \| `null` | `required` |  |
| `title` | `string` \| `null` | `required` |  |

</details>

### `Kernel_SimulateProtocolRequest` {#schema-kernel-simulateprotocolrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (10)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `base_library_item_ref` | `string` \| `null` | `optional` |  |
| `base_version_ref` | `string` \| `null` | `required` |  |
| `candidate_content` | `string` \| `null` | `required` |  |
| `kind` | `string` \| `null` | `optional` |  |
| `known_at` | `string` (`date-time`) \| `null` | `optional` |  |
| `library_item_ref` | `string` \| `null` | `optional` |  |
| `standard_version_ref` | `string` \| `null` | `optional` |  |
| `stored_version_ref` | `string` \| `null` | `required` |  |
| `study_ref` | `string` \| `null` | `optional` |  |
| `valid_at` | `string` (`date-time`) \| `null` | `optional` |  |

</details>

