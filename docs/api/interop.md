# interop API

Generated from `docs/openapi/interop_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

- **Service Maturity:** <span class="VPBadge tip">Active</span> (Port `8004` · Gateway prefixes: `/api/v1/interop`)
- **OpenAPI 3.1 Contract:** [`interop_openapi.json`](../openapi/interop_openapi.json) (28 operation(s), 46 schema(s))

> **HTTP 501 Not Implemented Stub(s):** `POST /api/v1/interop/esource/poll` (`PollHospitalEhr`) intentionally answers `501 Not Implemented` with an RFC 7807 `ProblemDetails` payload until its external integration backend is wired.

## Operations

| Method | Path | Operation | Summary | Contract |
| ------ | ---- | --------- | ------- | -------- |
| `GET` | `/api/v1/interop/esource/candidates` | ListCandidates | Lists eSource reconciliation candidates filtered by study, site, subject, domain, or status. | [Details](#op-listcandidates) |
| `GET` | `/api/v1/interop/esource/candidates/{candidate_id}` | GetCandidate | Retrieves a single eSource reconciliation candidate record by ID. | [Details](#op-getcandidate) |
| `GET` | `/api/v1/interop/esource/endpoints` | ListEhrEndpoints | Lists registered hospital SMART on FHIR endpoints. | [Details](#op-listehrendpoints) |
| `POST` | `/api/v1/interop/esource/endpoints` | RegisterEhrEndpoint | Registers a hospital SMART on FHIR endpoint configuration and server-side signing key reference. | [Details](#op-registerehrendpoint) |
| `POST` | `/api/v1/interop/esource/ingest` | IngestFhirBundle | Maps an exported HL7 FHIR Observation bundle into side-by-side eSource reconciliation candidates. | [Details](#op-ingestfhirbundle) |
| `POST` | `/api/v1/interop/esource/poll` | PollHospitalEhr | Polls a hospital EHR endpoint for eSource observations (returns 501 Not Implemented; ingest FHIR bundles via POST /ingest). | [Details](#op-pollhospitalehr) · `501 Stub` |
| `GET` | `/api/v1/interop/esource/provenance/{candidate_id}` | GetProvenanceAudit | Retrieves FHIR resource provenance and reconciliation audit lineage for an eSource candidate. | [Details](#op-getprovenanceaudit) |
| `POST` | `/api/v1/interop/esource/reconcile/accept` | AcceptCandidate | Accepts and commits a hospital eSource candidate value into the eCRF with Part 11 audit trail. | [Details](#op-acceptcandidate) |
| `POST` | `/api/v1/interop/esource/reconcile/batch-accept` | BatchAcceptCandidates | Batch-accepts up to 100 hospital eSource candidate values atomically into the eCRF. | [Details](#op-batchacceptcandidates) |
| `POST` | `/api/v1/interop/esource/reconcile/batch-reject` | BatchRejectCandidates | Batch-rejects up to 100 eSource candidate values with mandatory GxP clinical rationale. | [Details](#op-batchrejectcandidates) |
| `POST` | `/api/v1/interop/esource/reconcile/query` | RaiseClinicalQuery | Raises a clinical discrepancy query from an eSource reconciliation candidate. | [Details](#op-raiseclinicalquery) |
| `POST` | `/api/v1/interop/esource/reconcile/reject` | RejectCandidate | Rejects an eSource candidate value with mandatory GxP clinical rationale. | [Details](#op-rejectcandidate) |
| `POST` | `/api/v1/interop/esource/smart/assertion` | GenerateClientAssertion | Signs a SMART Backend Services OAuth 2.0 JWT client assertion for a registered EHR endpoint. | [Details](#op-generateclientassertion) |
| `POST` | `/api/v1/interop/esource/smart/verify` | VerifyClientAssertion | Verifies a SMART Backend Services client assertion JWT signature, audience, and lifetime. | [Details](#op-verifyclientassertion) |
| `POST` | `/api/v1/interop/sce/execute` | ExecuteSceProgram | Executes a statistical program inside an isolated SCE container and seals the run manifest. | [Details](#op-executesceprogram) |
| `GET` | `/api/v1/interop/sce/lock-tokens` | ListSceLockTokens | Lists active database lock authorization tokens for unblinded statistical runs. | [Details](#op-listscelocktokens) |
| `POST` | `/api/v1/interop/sce/lock-tokens` | IssueSceLockToken | Issues a signed database lock authorization token for unblinded statistical execution. | [Details](#op-issuescelocktoken) |
| `POST` | `/api/v1/interop/sce/lock-tokens/{token_id}/revoke` | RevokeSceLockToken | Revokes an active database lock authorization token and logs the revocation. | [Details](#op-revokescelocktoken) |
| `GET` | `/api/v1/interop/sce/programs` | ListScePrograms | Lists statistical analysis programs registered for a study. | [Details](#op-listsceprograms) |
| `POST` | `/api/v1/interop/sce/programs` | CreateSceProgram | Registers a versioned R or Python statistical program with SHA-256 code and lockfile digests. | [Details](#op-createsceprogram) |
| `GET` | `/api/v1/interop/sce/programs/{program_id}` | GetSceProgram | Retrieves a registered statistical program by ID or program name. | [Details](#op-getsceprogram) |
| `GET` | `/api/v1/interop/sce/runs` | ListSceRuns | Lists historical statistical execution runs and manifests for a study. | [Details](#op-listsceruns) |
| `GET` | `/api/v1/interop/sce/runs/{run_id}` | GetSceRun | Retrieves a single statistical execution run manifest by run ID. | [Details](#op-getscerun) |
| `GET` | `/api/v1/interop/sce/runs/{run_id}/artifacts/{artifact_id}` | GetSceRunArtifact | Retrieves metadata, SHA-256 digest, and preview for an output artifact from an SCE run. | [Details](#op-getscerunartifact) |
| `POST` | `/api/v1/interop/sce/runs/{run_id}/verify` | VerifySceRunManifest | Recomputes and verifies the SHA-256 cryptographic seal of an SCE execution run manifest. | [Details](#op-verifyscerunmanifest) |
| `POST` | `/api/v1/interop/sce/validate-lockfile` | ValidateSceLockfile | Validates an R renv.lock or Python uv.lock package lockfile against qualified GxP package digests. | [Details](#op-validatescelockfile) |
| `GET` | `/api/v1/interop/status` | InteropStatus | Returns Interoperability operational status. | [Details](#op-interopstatus) |
| `GET` | `/healthz` | InteropHealthCheck | Interoperability FHIR & CDISC connector health probe. | [Details](#op-interophealthcheck) |

### `GET` `/api/v1/interop/esource/candidates` {#op-listcandidates}

**Operation ID:** `ListCandidates` — Lists eSource reconciliation candidates filtered by study, site, subject, domain, or status.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `esource_candidate:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eSource Reconciliation & SMART on FHIR`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `domain` | `query` | `optional` | `string` |  |
| `limit` | `query` | `required` | `integer` (`int32`) |  |
| `site_id` | `query` | `optional` | `string` |  |
| `skip` | `query` | `required` | `integer` (`int32`) |  |
| `status` | `query` | `optional` | `string` |  |
| `study_id` | `query` | `optional` | `string` |  |
| `subject_id` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Interop_EhrCandidateListResponse`](#schema-interop-ehrcandidatelistresponse) |
| `403` | Forbidden | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |

</details>

### `GET` `/api/v1/interop/esource/candidates/{candidate_id}` {#op-getcandidate}

**Operation ID:** `GetCandidate` — Retrieves a single eSource reconciliation candidate record by ID.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `esource_candidate:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eSource Reconciliation & SMART on FHIR`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `candidate_id` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Interop_EhrCandidateItemDto`](#schema-interop-ehrcandidateitemdto) |
| `403` | Forbidden | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |

</details>

### `GET` `/api/v1/interop/esource/endpoints` {#op-listehrendpoints}

**Operation ID:** `ListEhrEndpoints` — Lists registered hospital SMART on FHIR endpoints.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `esource_endpoint:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eSource Reconciliation & SMART on FHIR`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `active_only` | `query` | `required` | `boolean` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Interop_EhrEndpointListResponse`](#schema-interop-ehrendpointlistresponse) |
| `403` | Forbidden | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |

</details>

### `POST` `/api/v1/interop/esource/endpoints` {#op-registerehrendpoint}

**Operation ID:** `RegisterEhrEndpoint` — Registers a hospital SMART on FHIR endpoint configuration and server-side signing key reference.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `esource_endpoint:configure`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eSource Reconciliation & SMART on FHIR`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Interop_EhrEndpointCreateRequest`](#schema-interop-ehrendpointcreaterequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Interop_EhrEndpointRecord`](#schema-interop-ehrendpointrecord) |
| `400` | Bad Request | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |

</details>

### `POST` `/api/v1/interop/esource/ingest` {#op-ingestfhirbundle}

**Operation ID:** `IngestFhirBundle` — Maps an exported HL7 FHIR Observation bundle into side-by-side eSource reconciliation candidates.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `esource_candidate:ingest`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eSource Reconciliation & SMART on FHIR`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Interop_FhirBundleIngestRequest`](#schema-interop-fhirbundleingestrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Interop_FhirBundleIngestResponse`](#schema-interop-fhirbundleingestresponse) |
| `400` | Bad Request | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `409` | Conflict | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |

</details>

### `POST` `/api/v1/interop/esource/poll` <span class="VPBadge warning">501 Not Implemented</span> {#op-pollhospitalehr}

**Operation ID:** `PollHospitalEhr` — Polls a hospital EHR endpoint for eSource observations (returns 501 Not Implemented; ingest FHIR bundles via POST /ingest).

HTTP 501 Not Implemented stub: live outbound SMART on FHIR EHR polling is not wired, so this route validates study/site scope and returns RFC 7807 ProblemDetails (501) without synthesizing candidate records.

> **Stub Endpoint (`501 Not Implemented`):** Returns an RFC 7807 `ProblemDetails` response indicating the external adapter or inference worker is not wired in this build.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `esource_candidate:ingest`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eSource Reconciliation & SMART on FHIR`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Interop_EhrPollRequest`](#schema-interop-ehrpollrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `400` | Bad Request | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `501` (`Stub`) | Not Implemented | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |

</details>

### `GET` `/api/v1/interop/esource/provenance/{candidate_id}` {#op-getprovenanceaudit}

**Operation ID:** `GetProvenanceAudit` — Retrieves FHIR resource provenance and reconciliation audit lineage for an eSource candidate.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `esource_candidate:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eSource Reconciliation & SMART on FHIR`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `candidate_id` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Interop_EhrProvenanceResponse`](#schema-interop-ehrprovenanceresponse) |
| `403` | Forbidden | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |

</details>

### `POST` `/api/v1/interop/esource/reconcile/accept` {#op-acceptcandidate}

**Operation ID:** `AcceptCandidate` — Accepts and commits a hospital eSource candidate value into the eCRF with Part 11 audit trail.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `esource_candidate:reconcile`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eSource Reconciliation & SMART on FHIR`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Sig-Token` | `header` | `required` | `string` (minLen: 1) | Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Interop_EhrReconcileAcceptRequest`](#schema-interop-ehrreconcileacceptrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Interop_EhrReconcileActionResponse`](#schema-interop-ehrreconcileactionresponse) |
| `400` | Bad Request | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |

</details>

### `POST` `/api/v1/interop/esource/reconcile/batch-accept` {#op-batchacceptcandidates}

**Operation ID:** `BatchAcceptCandidates` — Batch-accepts up to 100 hospital eSource candidate values atomically into the eCRF.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `esource_candidate:reconcile`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eSource Reconciliation & SMART on FHIR`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Sig-Token` | `header` | `required` | `string` (minLen: 1) | Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Interop_EhrReconcileBatchAcceptRequest`](#schema-interop-ehrreconcilebatchacceptrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Interop_EhrReconcileBatchActionResponse`](#schema-interop-ehrreconcilebatchactionresponse) |
| `400` | Bad Request | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |

</details>

### `POST` `/api/v1/interop/esource/reconcile/batch-reject` {#op-batchrejectcandidates}

**Operation ID:** `BatchRejectCandidates` — Batch-rejects up to 100 eSource candidate values with mandatory GxP clinical rationale.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `esource_candidate:reconcile`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eSource Reconciliation & SMART on FHIR`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Sig-Token` | `header` | `required` | `string` (minLen: 1) | Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Interop_EhrReconcileBatchRejectRequest`](#schema-interop-ehrreconcilebatchrejectrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Interop_EhrReconcileBatchActionResponse`](#schema-interop-ehrreconcilebatchactionresponse) |
| `400` | Bad Request | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |

</details>

### `POST` `/api/v1/interop/esource/reconcile/query` {#op-raiseclinicalquery}

**Operation ID:** `RaiseClinicalQuery` — Raises a clinical discrepancy query from an eSource reconciliation candidate.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `esource_candidate:query`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eSource Reconciliation & SMART on FHIR`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Interop_EhrReconcileQueryRequest`](#schema-interop-ehrreconcilequeryrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Interop_EhrReconcileActionResponse`](#schema-interop-ehrreconcileactionresponse) |
| `400` | Bad Request | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |

</details>

### `POST` `/api/v1/interop/esource/reconcile/reject` {#op-rejectcandidate}

**Operation ID:** `RejectCandidate` — Rejects an eSource candidate value with mandatory GxP clinical rationale.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `esource_candidate:reconcile`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eSource Reconciliation & SMART on FHIR`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Sig-Token` | `header` | `required` | `string` (minLen: 1) | Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Interop_EhrReconcileRejectRequest`](#schema-interop-ehrreconcilerejectrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Interop_EhrReconcileActionResponse`](#schema-interop-ehrreconcileactionresponse) |
| `400` | Bad Request | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |

</details>

### `POST` `/api/v1/interop/esource/smart/assertion` {#op-generateclientassertion}

**Operation ID:** `GenerateClientAssertion` — Signs a SMART Backend Services OAuth 2.0 JWT client assertion for a registered EHR endpoint.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `esource_endpoint:configure`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eSource Reconciliation & SMART on FHIR`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Interop_SmartAssertionRequest`](#schema-interop-smartassertionrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Interop_SmartAssertionResponse`](#schema-interop-smartassertionresponse) |
| `400` | Bad Request | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `422` | Unprocessable Entity | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |

</details>

### `POST` `/api/v1/interop/esource/smart/verify` {#op-verifyclientassertion}

**Operation ID:** `VerifyClientAssertion` — Verifies a SMART Backend Services client assertion JWT signature, audience, and lifetime.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `esource_endpoint:configure`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eSource Reconciliation & SMART on FHIR`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Interop_SmartVerifyRequest`](#schema-interop-smartverifyrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Interop_SmartVerifyResponse`](#schema-interop-smartverifyresponse) |
| `400` | Bad Request | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `422` | Unprocessable Entity | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |

</details>

### `POST` `/api/v1/interop/sce/execute` {#op-executesceprogram}

**Operation ID:** `ExecuteSceProgram` — Executes a statistical program inside an isolated SCE container and seals the run manifest.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `sce_run:execute`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Statistical Computing Environment`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Interop_ExecuteProgramRequestDto`](#schema-interop-executeprogramrequestdto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Interop_ExecuteProgramResponseDto`](#schema-interop-executeprogramresponsedto) |
| `403` | Forbidden | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `422` | Unprocessable Entity | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |

</details>

### `GET` `/api/v1/interop/sce/lock-tokens` {#op-listscelocktokens}

**Operation ID:** `ListSceLockTokens` — Lists active database lock authorization tokens for unblinded statistical runs.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `sce_lock_token:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Statistical Computing Environment`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `study_id` | `query` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Interop_LockTokenListResponseDto`](#schema-interop-locktokenlistresponsedto) |
| `403` | Forbidden | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |

</details>

### `POST` `/api/v1/interop/sce/lock-tokens` {#op-issuescelocktoken}

**Operation ID:** `IssueSceLockToken` — Issues a signed database lock authorization token for unblinded statistical execution.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `sce_lock_token:issue`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Statistical Computing Environment`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Interop_IssueLockTokenRequestDto`](#schema-interop-issuelocktokenrequestdto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Interop_LockTokenResponseDto`](#schema-interop-locktokenresponsedto) |
| `400` | Bad Request | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |

</details>

### `POST` `/api/v1/interop/sce/lock-tokens/{token_id}/revoke` {#op-revokescelocktoken}

**Operation ID:** `RevokeSceLockToken` — Revokes an active database lock authorization token and logs the revocation.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `sce_lock_token:revoke`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Statistical Computing Environment`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `token_id` | `path` | `required` | `string` |  |
| `reason` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Interop_RevokeLockTokenResponseDto`](#schema-interop-revokelocktokenresponsedto) |
| `403` | Forbidden | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |

</details>

### `GET` `/api/v1/interop/sce/programs` {#op-listsceprograms}

**Operation ID:** `ListScePrograms` — Lists statistical analysis programs registered for a study.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `sce_program:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Statistical Computing Environment`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `study_id` | `query` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Interop_ProgramListResponseDto`](#schema-interop-programlistresponsedto) |
| `403` | Forbidden | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |

</details>

### `POST` `/api/v1/interop/sce/programs` {#op-createsceprogram}

**Operation ID:** `CreateSceProgram` — Registers a versioned R or Python statistical program with SHA-256 code and lockfile digests.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `sce_program:create`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Statistical Computing Environment`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Interop_CreateProgramRequestDto`](#schema-interop-createprogramrequestdto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Interop_ProgramResponseDto`](#schema-interop-programresponsedto) |
| `403` | Forbidden | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |

</details>

### `GET` `/api/v1/interop/sce/programs/{program_id}` {#op-getsceprogram}

**Operation ID:** `GetSceProgram` — Retrieves a registered statistical program by ID or program name.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `sce_program:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Statistical Computing Environment`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `program_id` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Interop_ProgramResponseDto`](#schema-interop-programresponsedto) |
| `404` | Not Found | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |

</details>

### `GET` `/api/v1/interop/sce/runs` {#op-listsceruns}

**Operation ID:** `ListSceRuns` — Lists historical statistical execution runs and manifests for a study.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `sce_run:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Statistical Computing Environment`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `program_id` | `query` | `optional` | `string` |  |
| `snapshot_id` | `query` | `optional` | `string` |  |
| `study_id` | `query` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Interop_ExecutionRunListResponseDto`](#schema-interop-executionrunlistresponsedto) |
| `403` | Forbidden | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |

</details>

### `GET` `/api/v1/interop/sce/runs/{run_id}` {#op-getscerun}

**Operation ID:** `GetSceRun` — Retrieves a single statistical execution run manifest by run ID.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `sce_run:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Statistical Computing Environment`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `run_id` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Interop_ExecutionRunDetailResponseDto`](#schema-interop-executionrundetailresponsedto) |
| `404` | Not Found | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |

</details>

### `GET` `/api/v1/interop/sce/runs/{run_id}/artifacts/{artifact_id}` {#op-getscerunartifact}

**Operation ID:** `GetSceRunArtifact` — Retrieves metadata, SHA-256 digest, and preview for an output artifact from an SCE run.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `sce_run:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Statistical Computing Environment`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `artifact_id` | `path` | `required` | `string` |  |
| `run_id` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Interop_SceArtifactDetailResponseDto`](#schema-interop-sceartifactdetailresponsedto) |
| `404` | Not Found | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |

</details>

### `POST` `/api/v1/interop/sce/runs/{run_id}/verify` {#op-verifyscerunmanifest}

**Operation ID:** `VerifySceRunManifest` — Recomputes and verifies the SHA-256 cryptographic seal of an SCE execution run manifest.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `sce_run:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Statistical Computing Environment`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `run_id` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Interop_VerifyManifestResponseDto`](#schema-interop-verifymanifestresponsedto) |
| `404` | Not Found | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |

</details>

### `POST` `/api/v1/interop/sce/validate-lockfile` {#op-validatescelockfile}

**Operation ID:** `ValidateSceLockfile` — Validates an R renv.lock or Python uv.lock package lockfile against qualified GxP package digests.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `sce_program:read`
- **Clinical Scope Enforcement:** `Tenant`
- **Tags:** `Statistical Computing Environment`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Interop_ValidateLockfileRequestDto`](#schema-interop-validatelockfilerequestdto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Interop_ValidateLockfileResponseDto`](#schema-interop-validatelockfileresponsedto) |
| `403` | Forbidden | `application/problem+json` | [`Interop_ProblemDetails`](#schema-interop-problemdetails) |

</details>

### `GET` `/api/v1/interop/status` {#op-interopstatus}

**Operation ID:** `InteropStatus` — Returns Interoperability operational status.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Interop`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Interop_AnonymousTypeOfstringAndstring`](#schema-interop-anonymoustypeofstringandstring) |

</details>

### `GET` `/healthz` {#op-interophealthcheck}

**Operation ID:** `InteropHealthCheck` — Interoperability FHIR & CDISC connector health probe.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Interop`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Interop_AnonymousTypeOfstringAndstring`](#schema-interop-anonymoustypeofstringandstring) |

</details>

## Schemas

Deduplicated component schemas exported in `components.schemas` (46 total). Operation payloads and responses link here so nested models are defined once per page.

### `Interop_AnonymousTypeOfstringAndstring` {#schema-interop-anonymoustypeofstringandstring}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `service` | `string` \| `null` | `required` |  |
| `status` | `string` \| `null` | `required` |  |

</details>

### `Interop_CreateProgramRequestDto` {#schema-interop-createprogramrequestdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (11)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `description` | `string` \| `null` | `optional` |  |
| `language` | `string` | `optional` |  |
| `lock_type` | `string` | `optional` |  |
| `lockfile_content` | `string` \| `null` | `optional` |  |
| `name` | `string` | `required` |  |
| `reason_for_change` | `string` | `optional` |  |
| `source_code` | `string` | `optional` |  |
| `study_id` | `string` | `required` |  |
| `target_domain` | `string` \| `null` | `optional` |  |
| `title` | `string` | `required` |  |
| `version` | `string` | `optional` |  |

</details>

### `Interop_DatabaseLockAuthTokenDto` {#schema-interop-databaselockauthtokendto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (10)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `authorized_roles` | array of `string` | `required` |  |
| `expires_at` | `string` | `required` |  |
| `is_revoked` | `boolean` | `required` |  |
| `issued_at` | `string` | `required` |  |
| `issued_to` | `string` | `required` |  |
| `lock_id` | `string` | `required` |  |
| `milestone` | `string` | `required` |  |
| `signature_hash` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `token_id` | `string` | `required` |  |

</details>

### `Interop_EhrCandidateItemDto` {#schema-interop-ehrcandidateitemdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (30)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `candidateUnit` | `string` \| `null` | `required` |  |
| `candidateValue` | `any` | `required` |  |
| `cdashTest` | `string` \| `null` | `required` |  |
| `cdashTestcd` | `string` \| `null` | `required` |  |
| `clinicalQueryId` | `string` \| `null` | `required` |  |
| `confidenceScore` | `number` (`double`) | `required` |  |
| `createdAt` | `string` (`date-time`) | `required` |  |
| `currentEcrfUpdatedAt` | `string` (`date-time`) \| `null` | `required` |  |
| `currentEcrfValue` | `any` | `required` |  |
| `discrepancyType` | `string` | `required` |  |
| `ehrSystemId` | `string` | `required` |  |
| `electronicSignature` | `any` | `required` |  |
| `fhirResourceId` | `string` | `required` |  |
| `fhirResourceType` | `string` | `required` |  |
| `id` | `string` | `required` |  |
| `mappingProvenance` | `string` | `required` |  |
| `rawFhirResource` | `any` | `required` |  |
| `resolutionAction` | `string` \| `null` | `required` |  |
| `resolutionReason` | `string` \| `null` | `required` |  |
| `resolvedAt` | `string` (`date-time`) \| `null` | `required` |  |
| `resolvedBy` | `string` \| `null` | `required` |  |
| `siteId` | `string` | `required` |  |
| `sourceTimestamp` | `string` (`date-time`) | `required` |  |
| `status` | `string` | `required` |  |
| `studyId` | `string` | `required` |  |
| `subjectId` | `string` | `required` |  |
| `subjectPseudonym` | `string` | `required` |  |
| `targetDomain` | `string` | `required` |  |
| `targetVariable` | `string` | `required` |  |
| `versionIndex` | `integer` (`int32`) | `required` |  |

</details>

### `Interop_EhrCandidateListResponse` {#schema-interop-ehrcandidatelistresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `candidates` | array of [`Interop_EhrCandidateItemDto`](#schema-interop-ehrcandidateitemdto) | `required` |  |
| `totalCount` | `integer` (`int32`) | `required` |  |

</details>

### `Interop_EhrEndpointCreateRequest` {#schema-interop-ehrendpointcreaterequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (11)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `clientId` | `string` \| `null` | `required` |  |
| `fhirBaseUrl` | `string` | `required` |  |
| `name` | `string` | `required` |  |
| `pollingIntervalSec` | `integer` (`int32`) | `required` |  |
| `publicKeyJwksUrl` | `string` \| `null` | `required` |  |
| `reasonForChange` | `string` \| `null` | `required` |  |
| `scopes` | array of `string` \| `null` | `required` |  |
| `signingKeyId` | `string` \| `null` | `optional` |  |
| `signingKeyRef` | `string` \| `null` | `optional` |  |
| `systemType` | `string` \| `null` | `required` |  |
| `tokenUrl` | `string` \| `null` | `required` |  |

</details>

### `Interop_EhrEndpointListResponse` {#schema-interop-ehrendpointlistresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (1)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `endpoints` | array of [`Interop_EhrEndpointRecord`](#schema-interop-ehrendpointrecord) | `required` |  |

</details>

### `Interop_EhrEndpointRecord` {#schema-interop-ehrendpointrecord}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (17)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `clientId` | `string` | `optional` |  |
| `createdAt` | `string` (`date-time`) | `required` |  |
| `createdBy` | `string` | `required` |  |
| `fhirBaseUrl` | `string` | `optional` |  |
| `id` | `string` | `optional` |  |
| `isActive` | `boolean` | `optional` |  |
| `lastPolledAt` | `string` (`date-time`) \| `null` | `optional` |  |
| `name` | `string` | `optional` |  |
| `pollingIntervalSec` | `integer` (`int32`) | `optional` |  |
| `publicKeyJwksUrl` | `string` \| `null` | `optional` |  |
| `reasonForChange` | `string` | `optional` |  |
| `scopesJson` | `string` | `optional` |  |
| `signingKeyId` | `string` \| `null` | `optional` |  |
| `signingKeyRef` | `string` \| `null` | `optional` |  |
| `systemType` | `string` | `optional` |  |
| `tokenUrl` | `string` | `optional` |  |
| `versionIndex` | `integer` (`int32`) | `optional` |  |

</details>

### `Interop_EhrPollRequest` {#schema-interop-ehrpollrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `endpointId` | `string` \| `null` | `required` |  |
| `siteId` | `string` \| `null` | `required` |  |
| `studyId` | `string` | `required` |  |
| `subjectId` | `string` | `required` |  |

</details>

### `Interop_EhrProvenanceResponse` {#schema-interop-ehrprovenanceresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (31)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `auditTrail` | array of [`Interop_InteropAuditLog`](#schema-interop-interopauditlog) | `required` |  |
| `candidateId` | `string` | `required` |  |
| `candidateUnit` | `string` \| `null` | `required` |  |
| `candidateValue` | `any` | `required` |  |
| `cdashTest` | `string` \| `null` | `required` |  |
| `cdashTestcd` | `string` \| `null` | `required` |  |
| `clinicalQueryId` | `string` \| `null` | `required` |  |
| `confidenceScore` | `number` (`double`) | `required` |  |
| `createdAt` | `string` (`date-time`) | `required` |  |
| `currentEcrfUpdatedAt` | `string` (`date-time`) \| `null` | `required` |  |
| `currentEcrfValue` | `any` | `required` |  |
| `discrepancyType` | `string` | `required` |  |
| `ehrSystemId` | `string` | `required` |  |
| `electronicSignature` | `any` | `required` |  |
| `fhirResourceId` | `string` | `required` |  |
| `fhirResourceType` | `string` | `required` |  |
| `mappingProvenance` | `string` | `required` |  |
| `rawFhirResource` | `any` | `required` |  |
| `resolutionAction` | `string` \| `null` | `required` |  |
| `resolutionReason` | `string` \| `null` | `required` |  |
| `resolvedAt` | `string` (`date-time`) \| `null` | `required` |  |
| `resolvedBy` | `string` \| `null` | `required` |  |
| `siteId` | `string` | `required` |  |
| `sourceTimestamp` | `string` (`date-time`) \| `null` | `required` |  |
| `status` | `string` | `required` |  |
| `studyId` | `string` | `required` |  |
| `subjectId` | `string` | `required` |  |
| `subjectPseudonym` | `string` | `required` |  |
| `targetDomain` | `string` | `required` |  |
| `targetVariable` | `string` | `required` |  |
| `versionIndex` | `integer` (`int32`) | `required` |  |

</details>

### `Interop_EhrReconcileAcceptRequest` {#schema-interop-ehrreconcileacceptrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `candidateId` | `string` | `required` |  |
| `changeReason` | `string` \| `null` | `required` |  |
| `commitReason` | `string` \| `null` | `required` |  |

</details>

### `Interop_EhrReconcileActionResponse` {#schema-interop-ehrreconcileactionresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `candidate` | [`Interop_EhrCandidateItemDto`](#schema-interop-ehrcandidateitemdto) | `required` |  |
| `clinicalQueryId` | `string` \| `null` | `required` |  |
| `message` | `string` | `required` |  |
| `success` | `boolean` | `required` |  |

</details>

### `Interop_EhrReconcileBatchAcceptRequest` {#schema-interop-ehrreconcilebatchacceptrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `candidateIds` | array of `string` | `required` |  |
| `changeReason` | `string` \| `null` | `required` |  |
| `commitReason` | `string` \| `null` | `required` |  |

</details>

### `Interop_EhrReconcileBatchActionResponse` {#schema-interop-ehrreconcilebatchactionresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `actionCount` | `integer` (`int32`) | `required` |  |
| `candidates` | array of [`Interop_EhrCandidateItemDto`](#schema-interop-ehrcandidateitemdto) | `required` |  |
| `message` | `string` | `required` |  |
| `success` | `boolean` | `required` |  |

</details>

### `Interop_EhrReconcileBatchRejectRequest` {#schema-interop-ehrreconcilebatchrejectrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `candidateIds` | array of `string` | `required` |  |
| `changeReason` | `string` \| `null` | `required` |  |
| `rejectionReason` | `string` | `required` |  |

</details>

### `Interop_EhrReconcileQueryRequest` {#schema-interop-ehrreconcilequeryrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `candidateId` | `string` | `required` |  |
| `changeReason` | `string` \| `null` | `required` |  |
| `discrepancyType` | `string` \| `null` | `required` |  |
| `queryExplanation` | `string` | `required` |  |

</details>

### `Interop_EhrReconcileRejectRequest` {#schema-interop-ehrreconcilerejectrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `candidateId` | `string` | `required` |  |
| `changeReason` | `string` \| `null` | `required` |  |
| `rejectionReason` | `string` | `required` |  |

</details>

### `Interop_ElectronicSignatureBlockDto` {#schema-interop-electronicsignatureblockdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `hmac_signature` | `string` | `required` |  |
| `signature_meaning` | `string` | `required` |  |
| `signed_at` | `string` | `required` |  |
| `signer_id` | `string` | `required` |  |
| `signer_role` | `string` | `required` |  |

</details>

### `Interop_ExecuteProgramRequestDto` {#schema-interop-executeprogramrequestdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `database_lock_token_id` | `string` \| `null` | `optional` |  |
| `is_unblinded` | `boolean` | `optional` |  |
| `program_id` | `string` | `required` |  |
| `reason_for_execution` | `string` | `optional` |  |
| `sandbox_config` | [`Interop_ExecutionSandboxConfigDto`](#schema-interop-executionsandboxconfigdto) \| `null` | `optional` |  |
| `snapshot_id` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |

</details>

### `Interop_ExecuteProgramResponseDto` {#schema-interop-executeprogramresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (1)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `manifest` | [`Interop_ExecutionRunManifestDto`](#schema-interop-executionrunmanifestdto) | `required` |  |

</details>

### `Interop_ExecutionRunDetailResponseDto` {#schema-interop-executionrundetailresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (1)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `run` | [`Interop_ExecutionRunManifestDto`](#schema-interop-executionrunmanifestdto) | `required` |  |

</details>

### `Interop_ExecutionRunListResponseDto` {#schema-interop-executionrunlistresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `runs` | array of [`Interop_ExecutionRunManifestDto`](#schema-interop-executionrunmanifestdto) | `required` |  |
| `study_id` | `string` | `required` |  |
| `total` | `integer` (`int32`) | `required` |  |

</details>

### `Interop_ExecutionRunManifestDto` {#schema-interop-executionrunmanifestdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (28)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `container_image_digest` | `string` | `required` |  |
| `database_lock_token_id` | `string` \| `null` | `required` |  |
| `duration_ms` | `number` (`double`) | `required` |  |
| `executed_by` | `string` | `required` |  |
| `executed_by_role` | `string` | `required` |  |
| `execution_end` | `string` | `required` |  |
| `execution_start` | `string` | `required` |  |
| `exit_code` | `integer` (`int32`) | `required` |  |
| `is_unblinded_run` | `boolean` | `required` |  |
| `language` | `string` | `required` |  |
| `lockfile_digest` | `string` | `required` |  |
| `manifest_id` | `string` | `required` |  |
| `manifest_sha256_seal` | `string` | `required` |  |
| `output_artifacts` | array of [`Interop_OutputArtifactSchemaDto`](#schema-interop-outputartifactschemadto) | `required` |  |
| `output_artifacts_digest` | `string` | `required` |  |
| `program_id` | `string` | `required` |  |
| `program_name` | `string` | `required` |  |
| `program_version` | `string` | `required` |  |
| `run_id` | `string` | `required` |  |
| `script_code_digest` | `string` | `required` |  |
| `signature_block` | [`Interop_ElectronicSignatureBlockDto`](#schema-interop-electronicsignatureblockdto) | `required` |  |
| `snapshot_digest` | `string` | `required` |  |
| `snapshot_id` | `string` | `required` |  |
| `snapshot_version_tag` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `stderr_capture` | `string` | `required` |  |
| `stdout_capture` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |

</details>

### `Interop_ExecutionSandboxConfigDto` {#schema-interop-executionsandboxconfigdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `docker_image_digest` | `string` | `optional` |  |
| `filesystem_readonly_root` | `boolean` | `optional` |  |
| `max_memory_mb` | `integer` (`int32`) | `optional` |  |
| `max_output_bytes` | `integer` (`int64`) | `optional` |  |
| `network_isolated` | `boolean` | `optional` |  |
| `python_version` | `string` | `optional` |  |
| `r_version` | `string` | `optional` |  |
| `timeout_seconds` | `integer` (`int32`) | `optional` |  |

</details>

### `Interop_FhirBundleIngestRequest` {#schema-interop-fhirbundleingestrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `ehrSystemId` | `string` \| `null` | `required` |  |
| `rawFhirBundleJson` | `string` | `required` |  |
| `siteId` | `string` \| `null` | `required` |  |
| `studyId` | `string` | `required` |  |
| `subjectId` | `string` | `required` |  |
| `subjectPseudonym` | `string` \| `null` | `required` |  |

</details>

### `Interop_FhirBundleIngestResponse` {#schema-interop-fhirbundleingestresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `candidateCount` | `integer` (`int32`) | `required` |  |
| `candidates` | array of [`Interop_EhrCandidateItemDto`](#schema-interop-ehrcandidateitemdto) | `required` |  |
| `message` | `string` | `required` |  |
| `success` | `boolean` | `required` |  |

</details>

### `Interop_InteropAuditLog` {#schema-interop-interopauditlog}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `action` | `string` | `optional` |  |
| `changeReason` | `string` \| `null` | `optional` |  |
| `details` | `string` | `optional` |  |
| `id` | `string` | `optional` |  |
| `timestamp` | `string` (`date-time`) | `required` |  |
| `userId` | `string` | `optional` |  |
| `userRole` | `string` | `optional` |  |

</details>

### `Interop_IssueLockTokenRequestDto` {#schema-interop-issuelocktokenrequestdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `expires_in_hours` | `number` (`double`) | `optional` |  |
| `issued_to` | `string` | `required` |  |
| `lock_id` | `string` | `required` |  |
| `milestone` | `string` | `required` |  |
| `reason` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |

</details>

### `Interop_LockTokenListResponseDto` {#schema-interop-locktokenlistresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `study_id` | `string` | `required` |  |
| `tokens` | array of [`Interop_DatabaseLockAuthTokenDto`](#schema-interop-databaselockauthtokendto) | `required` |  |
| `total` | `integer` (`int32`) | `required` |  |

</details>

### `Interop_LockTokenResponseDto` {#schema-interop-locktokenresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (1)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `token` | [`Interop_DatabaseLockAuthTokenDto`](#schema-interop-databaselockauthtokendto) | `required` |  |

</details>

### `Interop_LockfileValidationResultSchemaDto` {#schema-interop-lockfilevalidationresultschemadto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `details` | `string` | `required` |  |
| `is_qualified` | `boolean` | `required` |  |
| `lock_digest` | `string` | `required` |  |
| `lock_type` | `string` | `required` |  |
| `package_count` | `integer` (`int32`) | `required` |  |
| `packages` | array of [`Interop_PackageEntrySchemaDto`](#schema-interop-packageentryschemadto) | `required` |  |
| `status` | `string` | `required` |  |
| `unqualified_packages` | array of `string` | `required` |  |

</details>

### `Interop_OutputArtifactSchemaDto` {#schema-interop-outputartifactschemadto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `artifact_id` | `string` | `required` |  |
| `artifact_type` | `string` | `required` |  |
| `content_preview` | `string` \| `null` | `optional` |  |
| `file_path` | `string` | `required` |  |
| `mime_type` | `string` | `required` |  |
| `name` | `string` | `required` |  |
| `sha256_digest` | `string` | `required` |  |
| `size_bytes` | `integer` (`int64`) | `required` |  |

</details>

### `Interop_PackageEntrySchemaDto` {#schema-interop-packageentryschemadto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `checksum` | `string` \| `null` | `optional` |  |
| `is_qualified` | `boolean` | `optional` |  |
| `name` | `string` | `required` |  |
| `repository` | `string` \| `null` | `optional` |  |
| `version` | `string` | `required` |  |

</details>

### `Interop_ProblemDetails` {#schema-interop-problemdetails}

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

### `Interop_ProgramListResponseDto` {#schema-interop-programlistresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `programs` | array of [`Interop_StatisticalProgramDto`](#schema-interop-statisticalprogramdto) | `required` |  |
| `study_id` | `string` | `required` |  |
| `total` | `integer` (`int32`) | `required` |  |

</details>

### `Interop_ProgramResponseDto` {#schema-interop-programresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (1)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `program` | [`Interop_StatisticalProgramDto`](#schema-interop-statisticalprogramdto) | `required` |  |

</details>

### `Interop_RevokeLockTokenResponseDto` {#schema-interop-revokelocktokenresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `reason` | `string` | `required` |  |
| `revoked_at` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `token_id` | `string` | `required` |  |

</details>

### `Interop_SceArtifactDetailResponseDto` {#schema-interop-sceartifactdetailresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (9)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `artifact_id` | `string` | `required` |  |
| `artifact_type` | `string` | `required` |  |
| `content_preview` | `string` \| `null` | `required` |  |
| `file_path` | `string` | `required` |  |
| `mime_type` | `string` | `required` |  |
| `name` | `string` | `required` |  |
| `run_id` | `string` | `required` |  |
| `sha256_digest` | `string` | `required` |  |
| `size_bytes` | `integer` (`int64`) | `required` |  |

</details>

### `Interop_SmartAssertionRequest` {#schema-interop-smartassertionrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `endpointId` | `string` | `required` |  |
| `expirationSeconds` | `integer` (`int32`) | `optional` |  |

</details>

### `Interop_SmartAssertionResponse` {#schema-interop-smartassertionresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `clientAssertionToken` | `string` | `required` |  |
| `clientAssertionType` | `string` | `required` |  |
| `expiresAt` | `string` (`date-time`) | `required` |  |

</details>

### `Interop_SmartVerifyRequest` {#schema-interop-smartverifyrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `clientAssertionToken` | `string` | `required` |  |
| `endpointId` | `string` | `required` |  |

</details>

### `Interop_SmartVerifyResponse` {#schema-interop-smartverifyresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `audience` | `string` | `required` |  |
| `errorMessage` | `string` \| `null` | `required` |  |
| `expiresAt` | `string` (`date-time`) | `required` |  |
| `isValid` | `boolean` | `required` |  |
| `issuer` | `string` | `required` |  |
| `subject` | `string` | `required` |  |

</details>

### `Interop_StatisticalProgramDto` {#schema-interop-statisticalprogramdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (16)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `code_hash` | `string` | `required` |  |
| `created_at` | `string` | `required` |  |
| `created_by` | `string` | `required` |  |
| `description` | `string` | `required` |  |
| `language` | `string` | `required` |  |
| `lock_digest` | `string` | `required` |  |
| `lock_type` | `string` | `required` |  |
| `lockfile_content` | `string` \| `null` | `required` |  |
| `name` | `string` | `required` |  |
| `program_id` | `string` | `required` |  |
| `reason_for_change` | `string` | `required` |  |
| `source_code` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `target_domain` | `string` \| `null` | `required` |  |
| `title` | `string` | `required` |  |
| `version` | `string` | `required` |  |

</details>

### `Interop_ValidateLockfileRequestDto` {#schema-interop-validatelockfilerequestdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `content` | `string` | `required` |  |
| `expected_digest` | `string` \| `null` | `optional` |  |
| `lock_type` | `string` | `required` |  |
| `strict_qualification` | `boolean` | `optional` |  |

</details>

### `Interop_ValidateLockfileResponseDto` {#schema-interop-validatelockfileresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (1)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `result` | [`Interop_LockfileValidationResultSchemaDto`](#schema-interop-lockfilevalidationresultschemadto) | `required` |  |

</details>

### `Interop_VerifyManifestResponseDto` {#schema-interop-verifymanifestresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (1)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `result` | `object` | `required` |  |

</details>

