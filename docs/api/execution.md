# execution API

Generated from `docs/openapi/execution_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

- **Service Maturity:** <span class="VPBadge tip">Active</span> (Port `8002` · Gateway prefixes: `/api/v1/execution`)
- **OpenAPI 3.1 Contract:** [`execution_openapi.json`](../openapi/execution_openapi.json) (65 operation(s), 91 schema(s))

## Operations

| Method | Path | Operation | Summary | Contract |
| ------ | ---- | --------- | ------- | -------- |
| `GET` | `/api/v1/execution/cec/dossiers/{id}` | GetCecDossierById | Retrieves a Clinical Endpoint Adjudication Committee dossier and reviewer votes by dossier ID. | [Details](#op-getcecdossierbyid) |
| `POST` | `/api/v1/execution/cec/dossiers/{id}/chair-adjudicate` | ChairAdjudicateDossier | Records the CEC chair's step-up e-signed clinical endpoint adjudication decision on a dossier. | [Details](#op-chairadjudicatedossier) |
| `GET` | `/api/v1/execution/coding/synonyms` | GetActiveSynonymRules | Retrieve active synonym rules for study auto-coding engine. | [Details](#op-getactivesynonymrules) |
| `POST` | `/api/v1/execution/coding/synonyms/import` | ImportSynonymFile | Ingest validated synonym dictionary rules into persistent store with GxP audit lineage. | [Details](#op-importsynonymfile) |
| `GET` | `/api/v1/execution/coding/synonyms/jobs` | GetSynonymImportJobs | Retrieve GxP audit ledger of bulk synonym import jobs. | [Details](#op-getsynonymimportjobs) |
| `POST` | `/api/v1/execution/coding/synonyms/validate` | ValidateSynonymFile | Pre-flight validation report for bulk synonym dictionary CSV/XLSX file. | [Details](#op-validatesynonymfile) |
| `DELETE` | `/api/v1/execution/coding/synonyms/{id}` | DeactivateSynonymRule | Deactivate an active synonym rule. | [Details](#op-deactivatesynonymrule) |
| `GET` | `/api/v1/execution/devices/deficiencies` | ListDeviceDeficiencies | List Device Deficiencies | [Details](#op-listdevicedeficiencies) |
| `POST` | `/api/v1/execution/devices/deficiencies` | LogDeviceDeficiency | Log Device Deficiency | [Details](#op-logdevicedeficiency) |
| `GET` | `/api/v1/execution/devices/deficiencies/{deficiency_id}` | GetDeviceDeficiencyById | Get Device Deficiency | [Details](#op-getdevicedeficiencybyid) |
| `POST` | `/api/v1/execution/devices/dispense` | DispenseDevice | Dispense Device | [Details](#op-dispensedevice) |
| `GET` | `/api/v1/execution/devices/inventory` | ListDeviceInventory | List Device Inventory | [Details](#op-listdeviceinventory) |
| `POST` | `/api/v1/execution/devices/inventory` | RegisterDeviceInventory | Register Device Inventory | [Details](#op-registerdeviceinventory) |
| `GET` | `/api/v1/execution/devices/investigation-summary` | GetDeviceInvestigationSummary | Get Investigation Summary | [Details](#op-getdeviceinvestigationsummary) |
| `POST` | `/api/v1/execution/devices/parse-udi` | ParseUdi | Parse UDI Barcode | [Details](#op-parseudi) |
| `GET` | `/api/v1/execution/devices/quarantine` | ListDeviceQuarantines | List Device Quarantines | [Details](#op-listdevicequarantines) |
| `POST` | `/api/v1/execution/devices/quarantine` | InitiateDeviceQuarantine | Initiate Quarantine | [Details](#op-initiatedevicequarantine) |
| `POST` | `/api/v1/execution/devices/quarantine/release` | ReleaseDeviceQuarantine | Release Quarantine | [Details](#op-releasedevicequarantine) |
| `GET` | `/api/v1/execution/lab-ranges` | GetReferenceRanges | Get reference ranges for study/site | [Details](#op-getreferenceranges) |
| `POST` | `/api/v1/execution/lab-ranges` | CreateReferenceRange | Create laboratory reference range | [Details](#op-createreferencerange) |
| `GET` | `/api/v1/execution/lab-ranges/{id}` | GetReferenceRangeById | Get reference range by ID | [Details](#op-getreferencerangebyid) |
| `PUT` | `/api/v1/execution/lab-ranges/{id}` | UpdateReferenceRange | Update laboratory reference range | [Details](#op-updatereferencerange) |
| `DELETE` | `/api/v1/execution/lab-ranges/{id}` | DeleteReferenceRange | Deactivate laboratory reference range | [Details](#op-deletereferencerange) |
| `GET` | `/api/v1/execution/labs/batch-status` | GetLabBatchStatus | Retrieve batch status history for bulk lab range imports | [Details](#op-getlabbatchstatus) |
| `GET` | `/api/v1/execution/labs/batch-status/{id}` | GetLabBatchStatusById | Get lab batch import job status by ID | [Details](#op-getlabbatchstatusbyid) |
| `POST` | `/api/v1/execution/labs/convert-unit` | ConvertLabUnit | Convert clinical laboratory unit via UCUM SI formulas | [Details](#op-convertlabunit) |
| `POST` | `/api/v1/execution/labs/import/commit` | CommitLabRangeImport | Commit validated bulk lab ranges into persistent store with Part 11 audit logging | [Details](#op-commitlabrangeimport) |
| `POST` | `/api/v1/execution/labs/import/dry-run` | DryRunLabRangeImport | Pre-flight validation preview for bulk lab range CSV/XLSX file | [Details](#op-dryrunlabrangeimport) |
| `GET` | `/api/v1/execution/labs/loinc-catalog` | GetLoincCatalog | Retrieve standardized LOINC catalog mappings | [Details](#op-getloinccatalog) |
| `POST` | `/api/v1/execution/labs/toxicity/evaluate` | EvaluateLabToxicityEndpoint | Evaluate Lab Toxicity Endpoint | [Details](#op-evaluatelabtoxicityendpoint) |
| `GET` | `/api/v1/execution/labs/toxicity/review-queue` | ListStagedAeReviewQueue | List Staged Ae Review Queue | [Details](#op-liststagedaereviewqueue) |
| `POST` | `/api/v1/execution/labs/toxicity/review/{review_id}/action` | ActionStagedAeReview | Action Staged Ae Review | [Details](#op-actionstagedaereview) |
| `GET` | `/api/v1/execution/randomization/schemes` | ListRandomizationSchemes | Lists a study's scheme versions, newest first, without lists or allocations. | [Details](#op-listrandomizationschemes) |
| `POST` | `/api/v1/execution/randomization/schemes` | DraftRandomizationScheme | Drafts the study's next randomisation scheme version. | [Details](#op-draftrandomizationscheme) |
| `POST` | `/api/v1/execution/randomization/schemes/{schemeId}/approve` | ApproveRandomizationScheme | Approves a draft version for go-live, retiring the previous one and generating its list. | [Details](#op-approverandomizationscheme) |
| `POST` | `/api/v1/execution/rtsm/cgt/apheresis` | InitializeCgtApheresis | Initializes a cell and gene therapy chain of custody at apheresis collection with ISBT-128 DIN validation. | [Details](#op-initializecgtapheresis) |
| `POST` | `/api/v1/execution/rtsm/cgt/bedside-verify` | VerifyCgtBedside | Performs dual-operator bedside barcode verification prior to cell therapy infusion. | [Details](#op-verifycgtbedside) |
| `GET` | `/api/v1/execution/rtsm/cgt/certificates/{studyId}` | ListCgtDestructionCertificates | Lists CGT biohazard destruction certificates for a study within the caller's site scope. | [Details](#op-listcgtdestructioncertificates) |
| `GET` | `/api/v1/execution/rtsm/cgt/chain-of-custody/{studyId}/{subjectId}` | GetCgtFullTraceability | Retrieves end-to-end vein-to-vein CGT chain-of-custody traceability for a study subject. | [Details](#op-getcgtfulltraceability) |
| `POST` | `/api/v1/execution/rtsm/cgt/destruction-certificate` | CreateCgtDestructionCertificate | Generates a dual-witness CGT biohazard destruction certificate. | [Details](#op-createcgtdestructioncertificate) |
| `POST` | `/api/v1/execution/rtsm/cgt/infuse` | ExecuteCgtInfusion | Records autologous cell therapy administration following verified bedside identity match. | [Details](#op-executecgtinfusion) |
| `POST` | `/api/v1/execution/rtsm/cgt/manufacturing-bind` | BindCgtManufacturingBatch | Binds a finished autologous manufacturing lot and SEC/expiry metadata to a chain of custody. | [Details](#op-bindcgtmanufacturingbatch) |
| `POST` | `/api/v1/execution/rtsm/cgt/milestone` | RecordCgtMilestone | Records a tamper-evident manufacturing or cold-chain logistics milestone on a CGT chain of custody. | [Details](#op-recordcgtmilestone) |
| `GET` | `/api/v1/execution/rtsm/cgt/reconciliations/{studyId}` | ListCgtVialReconciliations | Lists CGT vial and bag return accountability reconciliations for a study. | [Details](#op-listcgtvialreconciliations) |
| `POST` | `/api/v1/execution/rtsm/cgt/returns` | LogCgtVialReturn | Logs post-infusion CGT bag or vial return accountability reconciliation. | [Details](#op-logcgtvialreturn) |
| `POST` | `/api/v1/execution/rtsm/cgt/verify-barcode` | VerifyCgtBarcode | Parses and validates an ISBT-128 or GS1 CGT label barcode within the caller's clinical scope. | [Details](#op-verifycgtbarcode) |
| `GET` | `/api/v1/execution/rtsm/kits/certificates/{certificateId}` | GetIpKitCertificate | Retrieves a signed investigational product kit accountability certificate by identifier. | [Details](#op-getipkitcertificate) |
| `GET` | `/api/v1/execution/rtsm/kits/inventory/{studyId}/{siteId}` | GetSiteIpKitInventory | Lists investigational product kit inventory and disposition statuses for a clinical site. | [Details](#op-getsiteipkitinventory) |
| `POST` | `/api/v1/execution/rtsm/kits/reconcile-batch` | ReconcileIpKitBatch | Reconciles a batch of investigational product kits and issues an accountability certificate. | [Details](#op-reconcileipkitbatch) |
| `POST` | `/api/v1/execution/rtsm/kits/seed-inventory` | SeedSiteIpKitInventory | Seeds sponsor investigational product kit inventory records for a clinical site. | [Details](#op-seedsiteipkitinventory) |
| `POST` | `/api/v1/execution/rtsm/kits/validate-scan` | ValidateIpKitBarcodeScan | Validates a scanned investigational product kit GS1 barcode against site inventory. | [Details](#op-validateipkitbarcodescan) |
| `POST` | `/api/v1/execution/sites/{siteId}/activate` | ActivateSite | Evaluates eTMF readiness and activates a clinical trial site for subject enrollment. | [Details](#op-activatesite) |
| `GET` | `/api/v1/execution/sites/{siteId}/compliance-status` | GetSiteComplianceStatus | Retrieves the current eTMF document completeness and activation gate status for a site. | [Details](#op-getsitecompliancestatus) |
| `GET` | `/api/v1/execution/subjects` | ListSubjects | Lists one page of a study's subjects in enrollment order. | [Details](#op-listsubjects) |
| `POST` | `/api/v1/execution/subjects` | CreateSubject | Registers a new trial subject in screening state with AES-GCM encrypted demographics. | [Details](#op-createsubject) |
| `GET` | `/api/v1/execution/subjects/counts` | GetSubjectCounts | Counts a study's subjects grouped by lifecycle status. | [Details](#op-getsubjectcounts) |
| `GET` | `/api/v1/execution/subjects/{subjectId}` | GetSubjectDetail | Retrieves a trial subject's enrollment record, consent history, and role-blinded allocation details. | [Details](#op-getsubjectdetail) |
| `POST` | `/api/v1/execution/subjects/{subjectId}/consent` | RecordSubjectConsent | Records informed consent version attestation for a trial subject. | [Details](#op-recordsubjectconsent) |
| `PUT` | `/api/v1/execution/subjects/{subjectId}/demographics` | UpdateSubjectDemographics | Updates encrypted subject demographic attributes with Part 11 change justification. | [Details](#op-updatesubjectdemographics) |
| `DELETE` | `/api/v1/execution/subjects/{subjectId}/demographics` | DeleteSubjectDemographics | Redacts stored subject demographic attributes under GDPR/privacy erasure with Part 11 audit trail. | [Details](#op-deletesubjectdemographics) |
| `POST` | `/api/v1/execution/subjects/{subjectId}/randomize` | RandomizeSubject | Randomises an enrolled participant under the live scheme and assigns a kit when it needs one. | [Details](#op-randomizesubject) |
| `PATCH` | `/api/v1/execution/subjects/{subjectId}/site` | AssignSubjectSite | Assigns a clinical site to a screening subject and synchronizes screening enrollment with the kernel. | [Details](#op-assignsubjectsite) |
| `PATCH` | `/api/v1/execution/subjects/{subjectId}/state` | UpdateSubjectState | Advances a trial subject's enrollment lifecycle state with Part 11 change justification. | [Details](#op-updatesubjectstate) |
| `POST` | `/api/v1/execution/webhooks/etmf` | EtmfCompletenessWebhook | Applies an eTMF site-activation document completeness webhook to the execution compliance cache. | [Details](#op-etmfcompletenesswebhook) |
| `GET` | `/healthz` | ExecutionHealthCheck | Execution engine health probe. | [Details](#op-executionhealthcheck) |

### `GET` `/api/v1/execution/cec/dossiers/{id}` {#op-getcecdossierbyid}

**Operation ID:** `GetCecDossierById` — Retrieves a Clinical Endpoint Adjudication Committee dossier and reviewer votes by dossier ID.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `cec_adjudication:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `CEC Adjudication`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `id` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_CecDossier`](#schema-execution-cecdossier) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/cec/dossiers/{id}/chair-adjudicate` {#op-chairadjudicatedossier}

**Operation ID:** `ChairAdjudicateDossier` — Records the CEC chair's step-up e-signed clinical endpoint adjudication decision on a dossier.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `cec_adjudication:adjudicate`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `CEC Adjudication`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `id` | `path` | `required` | `string` |  |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |
| `X-Sig-Token` | `header` | `required` | `string` (minLen: 1) | Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_ChairAdjudicateRequest`](#schema-execution-chairadjudicaterequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_ChairAdjudicationResponse`](#schema-execution-chairadjudicationresponse) |
| `400` | Bad Request | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `409` | Conflict | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `GET` `/api/v1/execution/coding/synonyms` {#op-getactivesynonymrules}

**Operation ID:** `GetActiveSynonymRules` — Retrieve active synonym rules for study auto-coding engine.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `medical_coding:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Medical Coding Synonyms`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `studyId` | `query` | `optional` | `string` |  |
| `study_id` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Execution_SynonymRuleResponse`](#schema-execution-synonymruleresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/coding/synonyms/import` {#op-importsynonymfile}

**Operation ID:** `ImportSynonymFile` — Ingest validated synonym dictionary rules into persistent store with GxP audit lineage.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `medical_coding:manage_synonyms`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Medical Coding Synonyms`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_BulkImportExecutionResponse`](#schema-execution-bulkimportexecutionresponse) |
| `400` | Bad Request | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `GET` `/api/v1/execution/coding/synonyms/jobs` {#op-getsynonymimportjobs}

**Operation ID:** `GetSynonymImportJobs` — Retrieve GxP audit ledger of bulk synonym import jobs.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `medical_coding:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Medical Coding Synonyms`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `studyId` | `query` | `optional` | `string` |  |
| `study_id` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Execution_BulkSynonymImportJobResponse`](#schema-execution-bulksynonymimportjobresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/coding/synonyms/validate` {#op-validatesynonymfile}

**Operation ID:** `ValidateSynonymFile` — Pre-flight validation report for bulk synonym dictionary CSV/XLSX file.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `medical_coding:manage_synonyms`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Medical Coding Synonyms`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_PreflightValidationReportResponse`](#schema-execution-preflightvalidationreportresponse) |
| `400` | Bad Request | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `DELETE` `/api/v1/execution/coding/synonyms/{id}` {#op-deactivatesynonymrule}

**Operation ID:** `DeactivateSynonymRule` — Deactivate an active synonym rule.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `medical_coding:manage_synonyms`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Medical Coding Synonyms`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `id` | `path` | `required` | `string` (`uuid`) |  |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_SynonymDeactivateResponse`](#schema-execution-synonymdeactivateresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `GET` `/api/v1/execution/devices/deficiencies` {#op-listdevicedeficiencies}

**Operation ID:** `ListDeviceDeficiencies` — List Device Deficiencies

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `device_accountability:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `DeviceEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `site_id` | `query` | `optional` | `string` |  |
| `status` | `query` | `optional` | `string` |  |
| `study_id` | `query` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Execution_DeviceDeficiencyResponse`](#schema-execution-devicedeficiencyresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/devices/deficiencies` {#op-logdevicedeficiency}

**Operation ID:** `LogDeviceDeficiency` — Log Device Deficiency

Logs a device deficiency/malfunction and triggers automated quarantine + safety cases.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `device_accountability:report_deficiency`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `DeviceEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_DeviceDeficiencyCreateRequest`](#schema-execution-devicedeficiencycreaterequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Execution_DeviceDeficiencyResponse`](#schema-execution-devicedeficiencyresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `422` | Unprocessable Entity | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `GET` `/api/v1/execution/devices/deficiencies/{deficiency_id}` {#op-getdevicedeficiencybyid}

**Operation ID:** `GetDeviceDeficiencyById` — Get Device Deficiency

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `device_accountability:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `DeviceEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `deficiency_id` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_DeviceDeficiencyResponse`](#schema-execution-devicedeficiencyresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/devices/dispense` {#op-dispensedevice}

**Operation ID:** `DispenseDevice` — Dispense Device

Dispenses an investigational device to a study subject while enforcing quarantine and expiration gating rules.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `device_accountability:dispense`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `DeviceEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_DeviceDispensationRequest`](#schema-execution-devicedispensationrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_DeviceDispensationResponse`](#schema-execution-devicedispensationresponse) |
| `400` | Bad Request | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `422` | Unprocessable Entity | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `GET` `/api/v1/execution/devices/inventory` {#op-listdeviceinventory}

**Operation ID:** `ListDeviceInventory` — List Device Inventory

Retrieves investigational medical device inventory matching query filters.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `device_accountability:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `DeviceEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `lot_number` | `query` | `optional` | `string` |  |
| `serial_number` | `query` | `optional` | `string` |  |
| `site_id` | `query` | `optional` | `string` |  |
| `status` | `query` | `optional` | `string` |  |
| `study_id` | `query` | `required` | `string` |  |
| `udi_di` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Execution_DeviceInventoryResponse`](#schema-execution-deviceinventoryresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `422` | Unprocessable Entity | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/devices/inventory` {#op-registerdeviceinventory}

**Operation ID:** `RegisterDeviceInventory` — Register Device Inventory

Registers serialized investigational medical device items in RTSM.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `device_accountability:register`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `DeviceEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_DeviceInventoryCreateRequest`](#schema-execution-deviceinventorycreaterequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | array of [`Execution_DeviceInventoryResponse`](#schema-execution-deviceinventoryresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `422` | Unprocessable Entity | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `GET` `/api/v1/execution/devices/investigation-summary` {#op-getdeviceinvestigationsummary}

**Operation ID:** `GetDeviceInvestigationSummary` — Get Investigation Summary

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `device_accountability:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `DeviceEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `study_id` | `query` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_DeviceInvestigationSummaryResponse`](#schema-execution-deviceinvestigationsummaryresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/devices/parse-udi` {#op-parseudi}

**Operation ID:** `ParseUdi` — Parse UDI Barcode

Parses and validates GS1-128, GS1-DataMatrix, and HIBCC barcode strings into structured UDI attributes.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `device_accountability:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `DeviceEndpoints`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_UdiParseRequest`](#schema-execution-udiparserequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_ParsedUdiResponse`](#schema-execution-parsedudiresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `422` | Unprocessable Entity | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `GET` `/api/v1/execution/devices/quarantine` {#op-listdevicequarantines}

**Operation ID:** `ListDeviceQuarantines` — List Device Quarantines

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `device_accountability:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `DeviceEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `site_id` | `query` | `optional` | `string` |  |
| `status` | `query` | `optional` | `string` |  |
| `study_id` | `query` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Execution_DeviceQuarantineResponse`](#schema-execution-devicequarantineresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/devices/quarantine` {#op-initiatedevicequarantine}

**Operation ID:** `InitiateDeviceQuarantine` — Initiate Quarantine

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `device_accountability:quarantine`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `DeviceEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_DeviceQuarantineCreateRequest`](#schema-execution-devicequarantinecreaterequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Execution_DeviceQuarantineResponse`](#schema-execution-devicequarantineresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/devices/quarantine/release` {#op-releasedevicequarantine}

**Operation ID:** `ReleaseDeviceQuarantine` — Release Quarantine

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `device_accountability:release`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `DeviceEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |
| `X-Sig-Token` | `header` | `required` | `string` (minLen: 1) | Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_DeviceQuarantineReleaseRequest`](#schema-execution-devicequarantinereleaserequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_DeviceQuarantineResponse`](#schema-execution-devicequarantineresponse) |
| `401` | Unauthorized | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `GET` `/api/v1/execution/lab-ranges` {#op-getreferenceranges}

**Operation ID:** `GetReferenceRanges` — Get reference ranges for study/site

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `lab_range:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Laboratory Reference Ranges`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `siteId` | `query` | `optional` | `string` |  |
| `site_id` | `query` | `optional` | `string` |  |
| `studyId` | `query` | `optional` | `string` |  |
| `study_id` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Execution_LabReferenceRangeResponse`](#schema-execution-labreferencerangeresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/lab-ranges` {#op-createreferencerange}

**Operation ID:** `CreateReferenceRange` — Create laboratory reference range

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `lab_range:create`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Laboratory Reference Ranges`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_LabReferenceRangeCreateRequest`](#schema-execution-labreferencerangecreaterequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Execution_LabReferenceRangeResponse`](#schema-execution-labreferencerangeresponse) |
| `400` | Bad Request | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `GET` `/api/v1/execution/lab-ranges/{id}` {#op-getreferencerangebyid}

**Operation ID:** `GetReferenceRangeById` — Get reference range by ID

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `lab_range:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Laboratory Reference Ranges`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `id` | `path` | `required` | `string` (`uuid`) |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_LabReferenceRangeResponse`](#schema-execution-labreferencerangeresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `PUT` `/api/v1/execution/lab-ranges/{id}` {#op-updatereferencerange}

**Operation ID:** `UpdateReferenceRange` — Update laboratory reference range

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `lab_range:update`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Laboratory Reference Ranges`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `id` | `path` | `required` | `string` (`uuid`) |  |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_LabReferenceRangeUpdateRequest`](#schema-execution-labreferencerangeupdaterequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_LabReferenceRangeResponse`](#schema-execution-labreferencerangeresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `DELETE` `/api/v1/execution/lab-ranges/{id}` {#op-deletereferencerange}

**Operation ID:** `DeleteReferenceRange` — Deactivate laboratory reference range

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `lab_range:delete`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Laboratory Reference Ranges`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `id` | `path` | `required` | `string` (`uuid`) |  |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | `any` |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `GET` `/api/v1/execution/labs/batch-status` {#op-getlabbatchstatus}

**Operation ID:** `GetLabBatchStatus` — Retrieve batch status history for bulk lab range imports

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `lab_range_import:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Laboratory Import & Catalog`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `studyId` | `query` | `optional` | `string` |  |
| `study_id` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Execution_BulkLabRangeImportJobResponse`](#schema-execution-bulklabrangeimportjobresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `GET` `/api/v1/execution/labs/batch-status/{id}` {#op-getlabbatchstatusbyid}

**Operation ID:** `GetLabBatchStatusById` — Get lab batch import job status by ID

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `lab_range_import:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Laboratory Import & Catalog`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `id` | `path` | `required` | `string` (`uuid`) |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_BulkLabRangeImportJobResponse`](#schema-execution-bulklabrangeimportjobresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/labs/convert-unit` {#op-convertlabunit}

**Operation ID:** `ConvertLabUnit` — Convert clinical laboratory unit via UCUM SI formulas

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `lab_range:read`
- **Clinical Scope Enforcement:** `Tenant`
- **Tags:** `Laboratory Import & Catalog`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_UnitConversionRequest`](#schema-execution-unitconversionrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_UnitConversionResponse`](#schema-execution-unitconversionresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/labs/import/commit` {#op-commitlabrangeimport}

**Operation ID:** `CommitLabRangeImport` — Commit validated bulk lab ranges into persistent store with Part 11 audit logging

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `lab_range_import:commit`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Laboratory Import & Catalog`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_BulkLabRangeImportJobResponse`](#schema-execution-bulklabrangeimportjobresponse) |
| `400` | Bad Request | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/labs/import/dry-run` {#op-dryrunlabrangeimport}

**Operation ID:** `DryRunLabRangeImport` — Pre-flight validation preview for bulk lab range CSV/XLSX file

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `lab_range_import:preview`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Laboratory Import & Catalog`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_BulkLabRangeDryRunResponse`](#schema-execution-bulklabrangedryrunresponse) |
| `400` | Bad Request | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `GET` `/api/v1/execution/labs/loinc-catalog` {#op-getloinccatalog}

**Operation ID:** `GetLoincCatalog` — Retrieve standardized LOINC catalog mappings

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `lab_range:read`
- **Clinical Scope Enforcement:** `Tenant`
- **Tags:** `Laboratory Import & Catalog`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_LoincCatalogResponse`](#schema-execution-loinccatalogresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/labs/toxicity/evaluate` {#op-evaluatelabtoxicityendpoint}

**Operation ID:** `EvaluateLabToxicityEndpoint` — Evaluate Lab Toxicity Endpoint

Evaluate CTCAE v5.0 Grade, Hy's Law criteria, and delta checks for a single lab observation.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `lab_range:alert`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Laboratory Governance & Ingestion`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_LabToxicityEvaluateRequest`](#schema-execution-labtoxicityevaluaterequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_LabToxicityEvaluateResponse`](#schema-execution-labtoxicityevaluateresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `422` | Unprocessable Entity | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `GET` `/api/v1/execution/labs/toxicity/review-queue` {#op-liststagedaereviewqueue}

**Operation ID:** `ListStagedAeReviewQueue` — List Staged Ae Review Queue

Retrieve laboratory Grade 3/4 toxicity triggers staged for Investigator review.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `lab_toxicity_review:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Laboratory Governance & Ingestion`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `review_status` | `query` | `optional` | `string` |  |
| `site_id` | `query` | `optional` | `string` |  |
| `study_id` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Execution_StagedAdverseEventReviewResponse`](#schema-execution-stagedadverseeventreviewresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `422` | Unprocessable Entity | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/labs/toxicity/review/{review_id}/action` {#op-actionstagedaereview}

**Operation ID:** `ActionStagedAeReview` — Action Staged Ae Review

Perform Investigator workflow action on a staged Grade 3/4 AE (Confirm, Dismiss, Escalate).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `lab_toxicity_review:review`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Laboratory Governance & Ingestion`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `review_id` | `path` | `required` | `string` |  |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |
| `X-Sig-Token` | `header` | `required` | `string` (minLen: 1) | Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_StagedAdverseEventActionRequest`](#schema-execution-stagedadverseeventactionrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_StagedAdverseEventActionResponse`](#schema-execution-stagedadverseeventactionresponse) |
| `401` | Unauthorized | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `422` | Unprocessable Entity | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `GET` `/api/v1/execution/randomization/schemes` {#op-listrandomizationschemes}

**Operation ID:** `ListRandomizationSchemes` — Lists a study's scheme versions, newest first, without lists or allocations.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `randomization_scheme:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Randomization`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `study_id` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Execution_RandomizationSchemeResponse`](#schema-execution-randomizationschemeresponse) |
| `400` | Bad Request | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/randomization/schemes` {#op-draftrandomizationscheme}

**Operation ID:** `DraftRandomizationScheme` — Drafts the study's next randomisation scheme version.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `randomization_scheme:draft`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Randomization`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_RandomizationSchemeDraftRequest`](#schema-execution-randomizationschemedraftrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Execution_RandomizationSchemeResponse`](#schema-execution-randomizationschemeresponse) |
| `400` | Bad Request | `application/problem+json` | [`Execution_HttpValidationProblemDetails`](#schema-execution-httpvalidationproblemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/randomization/schemes/{schemeId}/approve` {#op-approverandomizationscheme}

**Operation ID:** `ApproveRandomizationScheme` — Approves a draft version for go-live, retiring the previous one and generating its list.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `randomization_scheme:approve`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Randomization`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `schemeId` | `path` | `required` | `string` (`uuid`) |  |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |
| `X-Sig-Token` | `header` | `required` | `string` (minLen: 1) | Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_RandomizationSchemeResponse`](#schema-execution-randomizationschemeresponse) |
| `401` | Unauthorized | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `409` | Conflict | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/rtsm/cgt/apheresis` {#op-initializecgtapheresis}

**Operation ID:** `InitializeCgtApheresis` — Initializes a cell and gene therapy chain of custody at apheresis collection with ISBT-128 DIN validation.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `cgt_chain_of_custody:write`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `CGT`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_CgtApheresisCreateRequest`](#schema-execution-cgtapheresiscreaterequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_CgtChainOfCustodyResponse`](#schema-execution-cgtchainofcustodyresponse) |
| `400` | Bad Request | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/rtsm/cgt/bedside-verify` {#op-verifycgtbedside}

**Operation ID:** `VerifyCgtBedside` — Performs dual-operator bedside barcode verification prior to cell therapy infusion.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `cgt_chain_of_custody:write`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `CGT`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |
| `X-Sig-Token` | `header` | `optional` | `string` | Conditional single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification when this request exercises a signature-gated transition (21 CFR 11.200(a)(1)). |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_CgtBedsideVerificationRequest`](#schema-execution-cgtbedsideverificationrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_CgtBedsideVerificationResponse`](#schema-execution-cgtbedsideverificationresponse) |
| `400` | Bad Request | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `422` | Unprocessable Entity | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `GET` `/api/v1/execution/rtsm/cgt/certificates/{studyId}` {#op-listcgtdestructioncertificates}

**Operation ID:** `ListCgtDestructionCertificates` — Lists CGT biohazard destruction certificates for a study within the caller's site scope.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `cgt_chain_of_custody:read`
- **Clinical Scope Enforcement:** `RouteStudy:studyId`
- **Tags:** `CGT`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `studyId` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Execution_CgtDestructionCertificateResponse`](#schema-execution-cgtdestructioncertificateresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `GET` `/api/v1/execution/rtsm/cgt/chain-of-custody/{studyId}/{subjectId}` {#op-getcgtfulltraceability}

**Operation ID:** `GetCgtFullTraceability` — Retrieves end-to-end vein-to-vein CGT chain-of-custody traceability for a study subject.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `cgt_chain_of_custody:read`
- **Clinical Scope Enforcement:** `RouteStudy:studyId`
- **Tags:** `CGT`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `studyId` | `path` | `required` | `string` |  |
| `subjectId` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_CgtFullTraceabilityResponse`](#schema-execution-cgtfulltraceabilityresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/rtsm/cgt/destruction-certificate` {#op-createcgtdestructioncertificate}

**Operation ID:** `CreateCgtDestructionCertificate` — Generates a dual-witness CGT biohazard destruction certificate.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `cgt_chain_of_custody:write`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `CGT`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |
| `X-Sig-Token` | `header` | `optional` | `string` | Conditional single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification when this request exercises a signature-gated transition (21 CFR 11.200(a)(1)). |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_CgtDestructionCertificateCreateRequest`](#schema-execution-cgtdestructioncertificatecreaterequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_CgtDestructionCertificateResponse`](#schema-execution-cgtdestructioncertificateresponse) |
| `401` | Unauthorized | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `422` | Unprocessable Entity | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/rtsm/cgt/infuse` {#op-executecgtinfusion}

**Operation ID:** `ExecuteCgtInfusion` — Records autologous cell therapy administration following verified bedside identity match.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `cgt_chain_of_custody:write`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `CGT`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_CgtInfusionExecuteRequest`](#schema-execution-cgtinfusionexecuterequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_CgtChainOfCustodyResponse`](#schema-execution-cgtchainofcustodyresponse) |
| `400` | Bad Request | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/rtsm/cgt/manufacturing-bind` {#op-bindcgtmanufacturingbatch}

**Operation ID:** `BindCgtManufacturingBatch` — Binds a finished autologous manufacturing lot and SEC/expiry metadata to a chain of custody.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `cgt_chain_of_custody:write`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `CGT`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_CgtManufacturingBindRequest`](#schema-execution-cgtmanufacturingbindrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_CgtChainOfCustodyResponse`](#schema-execution-cgtchainofcustodyresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/rtsm/cgt/milestone` {#op-recordcgtmilestone}

**Operation ID:** `RecordCgtMilestone` — Records a tamper-evident manufacturing or cold-chain logistics milestone on a CGT chain of custody.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `cgt_chain_of_custody:write`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `CGT`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_CgtMilestoneCreateRequest`](#schema-execution-cgtmilestonecreaterequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_CgtChainOfCustodyResponse`](#schema-execution-cgtchainofcustodyresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `GET` `/api/v1/execution/rtsm/cgt/reconciliations/{studyId}` {#op-listcgtvialreconciliations}

**Operation ID:** `ListCgtVialReconciliations` — Lists CGT vial and bag return accountability reconciliations for a study.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `cgt_chain_of_custody:read`
- **Clinical Scope Enforcement:** `RouteStudy:studyId`
- **Tags:** `CGT`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `studyId` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Execution_CgtVialReturnReconciliationResponse`](#schema-execution-cgtvialreturnreconciliationresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/rtsm/cgt/returns` {#op-logcgtvialreturn}

**Operation ID:** `LogCgtVialReturn` — Logs post-infusion CGT bag or vial return accountability reconciliation.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `cgt_chain_of_custody:write`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `CGT`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_CgtVialReturnReconciliationRequest`](#schema-execution-cgtvialreturnreconciliationrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_CgtVialReturnReconciliationResponse`](#schema-execution-cgtvialreturnreconciliationresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/rtsm/cgt/verify-barcode` {#op-verifycgtbarcode}

**Operation ID:** `VerifyCgtBarcode` — Parses and validates an ISBT-128 or GS1 CGT label barcode within the caller's clinical scope.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `cgt_chain_of_custody:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `CGT`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_CgtBarcodeVerifyRequest`](#schema-execution-cgtbarcodeverifyrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_CgtBarcodeVerifyResponse`](#schema-execution-cgtbarcodeverifyresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `GET` `/api/v1/execution/rtsm/kits/certificates/{certificateId}` {#op-getipkitcertificate}

**Operation ID:** `GetIpKitCertificate` — Retrieves a signed investigational product kit accountability certificate by identifier.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `rtsm_supply:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `IP Kit Reconciliation`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `certificateId` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_IpKitCertificateResponse`](#schema-execution-ipkitcertificateresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `GET` `/api/v1/execution/rtsm/kits/inventory/{studyId}/{siteId}` {#op-getsiteipkitinventory}

**Operation ID:** `GetSiteIpKitInventory` — Lists investigational product kit inventory and disposition statuses for a clinical site.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `rtsm_supply:read`
- **Clinical Scope Enforcement:** `RouteStudy:studyId`, `RouteSite:siteId`
- **Tags:** `IP Kit Reconciliation`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `siteId` | `path` | `required` | `string` |  |
| `studyId` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Execution_IpKitInventoryResponse`](#schema-execution-ipkitinventoryresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/rtsm/kits/reconcile-batch` {#op-reconcileipkitbatch}

**Operation ID:** `ReconcileIpKitBatch` — Reconciles a batch of investigational product kits and issues an accountability certificate.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `rtsm_supply:write`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `IP Kit Reconciliation`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |
| `X-Sig-Token` | `header` | `required` | `string` (minLen: 1) | Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_IpKitBatchReconciliationRequest`](#schema-execution-ipkitbatchreconciliationrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_IpKitReconciliationBatchResponse`](#schema-execution-ipkitreconciliationbatchresponse) |
| `400` | Bad Request | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/rtsm/kits/seed-inventory` {#op-seedsiteipkitinventory}

**Operation ID:** `SeedSiteIpKitInventory` — Seeds sponsor investigational product kit inventory records for a clinical site.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `ip_kit_inventory:seed`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `IP Kit Reconciliation`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_SeedInventoryRequest`](#schema-execution-seedinventoryrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Execution_IpKitInventoryResponse`](#schema-execution-ipkitinventoryresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/rtsm/kits/validate-scan` {#op-validateipkitbarcodescan}

**Operation ID:** `ValidateIpKitBarcodeScan` — Validates a scanned investigational product kit GS1 barcode against site inventory.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `rtsm_supply:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `IP Kit Reconciliation`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_IpKitScanValidateRequest`](#schema-execution-ipkitscanvalidaterequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_IpKitScanValidateResponse`](#schema-execution-ipkitscanvalidateresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/sites/{siteId}/activate` {#op-activatesite}

**Operation ID:** `ActivateSite` — Evaluates eTMF readiness and activates a clinical trial site for subject enrollment.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `site_compliance:activate`
- **Clinical Scope Enforcement:** `RouteSite:siteId`
- **Tags:** `SiteCompliance`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `siteId` | `path` | `required` | `string` |  |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_SiteActivationRequest`](#schema-execution-siteactivationrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_OperationEnvelopeResponse`](#schema-execution-operationenveloperesponse) |
| `400` | Bad Request | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `GET` `/api/v1/execution/sites/{siteId}/compliance-status` {#op-getsitecompliancestatus}

**Operation ID:** `GetSiteComplianceStatus` — Retrieves the current eTMF document completeness and activation gate status for a site.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `site_compliance:read`
- **Clinical Scope Enforcement:** `RouteSite:siteId`
- **Tags:** `SiteCompliance`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `siteId` | `path` | `required` | `string` |  |
| `known_at` | `query` | `optional` | `string` (`date-time`) |  |
| `milestone` | `query` | `optional` | `string` |  |
| `study_id` | `query` | `required` | `string` |  |
| `valid_at` | `query` | `optional` | `string` (`date-time`) |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_ComplianceStatusResponse`](#schema-execution-compliancestatusresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `GET` `/api/v1/execution/subjects` {#op-listsubjects}

**Operation ID:** `ListSubjects` — Lists one page of a study's subjects in enrollment order.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `subject_enrollment:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Subjects`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `limit` | `query` | `optional` | `integer` (`int32`) |  |
| `offset` | `query` | `optional` | `integer` (`int32`) |  |
| `q` | `query` | `optional` | `string` |  |
| `site_id` | `query` | `optional` | `string` |  |
| `status` | `query` | `optional` | `string` |  |
| `study_id` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_SubjectListResponse`](#schema-execution-subjectlistresponse) |
| `400` | Bad Request | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/subjects` {#op-createsubject}

**Operation ID:** `CreateSubject` — Registers a new trial subject in screening state with AES-GCM encrypted demographics.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `subject_enrollment:create`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Subjects`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_SubjectCreateRequest`](#schema-execution-subjectcreaterequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Execution_SubjectResponse`](#schema-execution-subjectresponse) |
| `400` | Bad Request | `application/problem+json` | [`Execution_HttpValidationProblemDetails`](#schema-execution-httpvalidationproblemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `GET` `/api/v1/execution/subjects/counts` {#op-getsubjectcounts}

**Operation ID:** `GetSubjectCounts` — Counts a study's subjects grouped by lifecycle status.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `subject_enrollment:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Subjects`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `site_id` | `query` | `optional` | `string` |  |
| `study_id` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_SubjectCountsResponse`](#schema-execution-subjectcountsresponse) |
| `400` | Bad Request | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `GET` `/api/v1/execution/subjects/{subjectId}` {#op-getsubjectdetail}

**Operation ID:** `GetSubjectDetail` — Retrieves a trial subject's enrollment record, consent history, and role-blinded allocation details.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `subject_enrollment:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Subjects`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `subjectId` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_SubjectDetailResponse`](#schema-execution-subjectdetailresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/subjects/{subjectId}/consent` {#op-recordsubjectconsent}

**Operation ID:** `RecordSubjectConsent` — Records informed consent version attestation for a trial subject.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `subject_enrollment:update`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Subjects`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `subjectId` | `path` | `required` | `string` |  |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_SubjectConsentRequest`](#schema-execution-subjectconsentrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_SubjectConsentResponse`](#schema-execution-subjectconsentresponse) |
| `400` | Bad Request | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `PUT` `/api/v1/execution/subjects/{subjectId}/demographics` {#op-updatesubjectdemographics}

**Operation ID:** `UpdateSubjectDemographics` — Updates encrypted subject demographic attributes with Part 11 change justification.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `subject_enrollment:update`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Subjects`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `subjectId` | `path` | `required` | `string` |  |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_SubjectDemographicsUpdateRequest`](#schema-execution-subjectdemographicsupdaterequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_SubjectResponse`](#schema-execution-subjectresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `422` | Unprocessable Entity | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `DELETE` `/api/v1/execution/subjects/{subjectId}/demographics` {#op-deletesubjectdemographics}

**Operation ID:** `DeleteSubjectDemographics` — Redacts stored subject demographic attributes under GDPR/privacy erasure with Part 11 audit trail.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `subject_enrollment:update`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Subjects`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `subjectId` | `path` | `required` | `string` |  |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_SubjectResponse`](#schema-execution-subjectresponse) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/subjects/{subjectId}/randomize` {#op-randomizesubject}

**Operation ID:** `RandomizeSubject` — Randomises an enrolled participant under the live scheme and assigns a kit when it needs one.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `subject_enrollment:randomize`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Subjects`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `subjectId` | `path` | `required` | `string` |  |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |
| `X-Sig-Token` | `header` | `required` | `string` (minLen: 1) | Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_RandomizeSubjectRequest`](#schema-execution-randomizesubjectrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_RandomizationResponse`](#schema-execution-randomizationresponse) |
| `400` | Bad Request | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `409` | Conflict | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `PATCH` `/api/v1/execution/subjects/{subjectId}/site` {#op-assignsubjectsite}

**Operation ID:** `AssignSubjectSite` — Assigns a clinical site to a screening subject and synchronizes screening enrollment with the kernel.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `subject_enrollment:update`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Subjects`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `subjectId` | `path` | `required` | `string` |  |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_SubjectSiteAssignmentRequest`](#schema-execution-subjectsiteassignmentrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_SubjectResponse`](#schema-execution-subjectresponse) |
| `400` | Bad Request | `application/problem+json` | [`Execution_HttpValidationProblemDetails`](#schema-execution-httpvalidationproblemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `409` | Conflict | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `PATCH` `/api/v1/execution/subjects/{subjectId}/state` {#op-updatesubjectstate}

**Operation ID:** `UpdateSubjectState` — Advances a trial subject's enrollment lifecycle state with Part 11 change justification.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `subject_enrollment:update`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Subjects`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `subjectId` | `path` | `required` | `string` |  |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |
| `X-Sig-Token` | `header` | `optional` | `string` | Conditional single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification when this request exercises a signature-gated transition (21 CFR 11.200(a)(1)). |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_SubjectStateUpdateRequest`](#schema-execution-subjectstateupdaterequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_SubjectResponse`](#schema-execution-subjectresponse) |
| `400` | Bad Request | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |
| `422` | Unprocessable Entity | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `POST` `/api/v1/execution/webhooks/etmf` {#op-etmfcompletenesswebhook}

**Operation ID:** `EtmfCompletenessWebhook` — Applies an eTMF site-activation document completeness webhook to the execution compliance cache.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `site_compliance:etmf_sync`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `SiteCompliance`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Execution_EtmfCompletenessWebhookPayload`](#schema-execution-etmfcompletenesswebhookpayload) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_OperationEnvelopeResponse`](#schema-execution-operationenveloperesponse) |
| `400` | Bad Request | `application/problem+json` | [`Execution_HttpValidationProblemDetails`](#schema-execution-httpvalidationproblemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Execution_ProblemDetails`](#schema-execution-problemdetails) |

</details>

### `GET` `/healthz` {#op-executionhealthcheck}

**Operation ID:** `ExecutionHealthCheck` — Execution engine health probe.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Execution`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Execution_AnonymousTypeOfstringAndstringAndstring`](#schema-execution-anonymoustypeofstringandstringandstring) |

</details>

## Schemas

Deduplicated component schemas exported in `components.schemas` (91 total). Operation payloads and responses link here so nested models are defined once per page.

### `Execution_AnonymousTypeOfstringAndstringAndstring` {#schema-execution-anonymoustypeofstringandstringandstring}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `engine` | `string` \| `null` | `required` |  |
| `service` | `string` \| `null` | `required` |  |
| `status` | `string` \| `null` | `required` |  |

</details>

### `Execution_BulkImportExecutionResponse` {#schema-execution-bulkimportexecutionresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `imported_rules` | array of [`Execution_SynonymRuleResponse`](#schema-execution-synonymruleresponse) | `required` |  |
| `job` | [`Execution_BulkSynonymImportJobResponse`](#schema-execution-bulksynonymimportjobresponse) | `required` |  |

</details>

### `Execution_BulkLabRangeDryRunResponse` {#schema-execution-bulklabrangedryrunresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (9)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `dry_run_id` | `string` | `required` |  |
| `invalid_rows` | `integer` (`int32`) | `required` |  |
| `is_success` | `boolean` | `required` |  |
| `loinc_mapped_count` | `integer` (`int32`) | `required` |  |
| `rows` | array of [`Execution_BulkLabRangeRowPreview`](#schema-execution-bulklabrangerowpreview) | `required` |  |
| `summary` | `string` | `required` |  |
| `total_rows` | `integer` (`int32`) | `required` |  |
| `unit_converted_count` | `integer` (`int32`) | `required` |  |
| `valid_rows` | `integer` (`int32`) | `required` |  |

</details>

### `Execution_BulkLabRangeImportJobResponse` {#schema-execution-bulklabrangeimportjobresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (15)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `completed_at` | `string` (`date-time`) \| `null` | `required` |  |
| `error_rows` | `integer` (`int32`) | `required` |  |
| `error_summary_json` | `string` \| `null` | `required` |  |
| `file_name` | `string` | `required` |  |
| `file_type` | `string` | `required` |  |
| `job_id` | `string` | `required` |  |
| `preview_summary_json` | `string` \| `null` | `required` |  |
| `reason_for_change` | `string` | `required` |  |
| `site_id` | `string` \| `null` | `required` |  |
| `status` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `submitted_at` | `string` (`date-time`) | `required` |  |
| `submitted_by` | `string` | `required` |  |
| `success_rows` | `integer` (`int32`) | `required` |  |
| `total_rows` | `integer` (`int32`) | `required` |  |

</details>

### `Execution_BulkLabRangeRowPreview` {#schema-execution-bulklabrangerowpreview}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (21)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `age_high` | `number` (`double`) \| `null` | `required` |  |
| `age_low` | `number` (`double`) \| `null` | `required` |  |
| `conversion_factor` | `number` (`double`) \| `null` | `required` |  |
| `converted_high_bound` | `number` (`double`) \| `null` | `required` |  |
| `converted_low_bound` | `number` (`double`) \| `null` | `required` |  |
| `critical_high` | `number` (`double`) \| `null` | `required` |  |
| `critical_low` | `number` (`double`) \| `null` | `required` |  |
| `errors` | array of `string` | `required` |  |
| `high_bound` | `number` (`double`) \| `null` | `required` |  |
| `is_valid` | `boolean` | `required` |  |
| `loinc_code` | `string` \| `null` | `required` |  |
| `low_bound` | `number` (`double`) \| `null` | `required` |  |
| `normalized_unit` | `string` | `required` |  |
| `row_number` | `integer` (`int32`) | `required` |  |
| `sex_applicability` | `string` | `required` |  |
| `site_id` | `string` \| `null` | `required` |  |
| `source` | `string` | `required` |  |
| `test_code` | `string` | `required` |  |
| `test_name` | `string` | `required` |  |
| `unit` | `string` | `required` |  |
| `warnings` | array of `string` | `required` |  |

</details>

### `Execution_BulkSynonymImportJobResponse` {#schema-execution-bulksynonymimportjobresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (13)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `created_at` | `string` (`date-time`) | `required` |  |
| `created_by` | `string` | `required` |  |
| `duplicate_rows` | `integer` (`int32`) | `required` |  |
| `file_hash` | `string` | `required` |  |
| `file_name` | `string` | `required` |  |
| `id` | `string` (`uuid`) | `required` |  |
| `invalid_rows` | `integer` (`int32`) | `required` |  |
| `reason_for_change` | `string` | `required` |  |
| `scope` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `study_id` | `string` \| `null` | `required` |  |
| `total_rows` | `integer` (`int32`) | `required` |  |
| `valid_rows` | `integer` (`int32`) | `required` |  |

</details>

### `Execution_CecDossier` {#schema-execution-cecdossier}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (25)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `adjudication_rationale` | `string` \| `null` | `optional` |  |
| `candidate_event_type` | `string` | `optional` |  |
| `created_at` | `string` (`date-time`) | `optional` |  |
| `created_by` | `string` | `optional` |  |
| `discordance_reasons` | array of `string` | `optional` |  |
| `event_id` | `string` | `optional` |  |
| `final_adjudicated_onset_date` | `string` \| `null` | `optional` |  |
| `final_consensus_classification` | `string` \| `null` | `optional` |  |
| `final_consensus_subtype` | `string` \| `null` | `optional` |  |
| `finalized_at` | `string` (`date-time`) \| `null` | `optional` |  |
| `id` | `string` \| `null` | `optional` |  |
| `is_discordant` | `boolean` | `optional` |  |
| `masked_site_id` | `string` \| `null` | `optional` |  |
| `masked_subject_id` | `string` | `optional` |  |
| `reader1_status` | `string` | `optional` |  |
| `reader2_status` | `string` | `optional` |  |
| `reason_for_change` | `string` | `optional` |  |
| `reported_onset_date` | `string` | `optional` |  |
| `reported_term` | `string` | `optional` |  |
| `signature_manifest` | [`Execution_SignatureManifest`](#schema-execution-signaturemanifest) \| `null` | `optional` |  |
| `site_id` | `string` \| `null` | `optional` |  |
| `study_id` | `string` | `optional` |  |
| `subject_id` | `string` | `optional` |  |
| `version_index` | `integer` (`int64`) | `optional` |  |
| `workflow_status` | `string` | `optional` |  |

</details>

### `Execution_CgtApheresisCreateRequest` {#schema-execution-cgtapheresiscreaterequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (12)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `apheresis_bag_identifier` | `string` \| `null` | `optional` |  |
| `cell_count_billions` | `number` (`double`) \| `null` | `optional` |  |
| `collection_site_id` | `string` | `required` |  |
| `collection_timestamp` | `string` (`date-time`) \| `null` | `optional` |  |
| `custodian_id` | `string` \| `null` | `optional` |  |
| `custodian_name` | `string` \| `null` | `optional` |  |
| `custodian_role` | `string` \| `null` | `optional` |  |
| `din` | `string` \| `null` | `optional` |  |
| `notes` | `string` \| `null` | `optional` |  |
| `study_id` | `string` | `required` |  |
| `subject_id` | `string` | `required` |  |
| `volume_ml` | `number` (`double`) \| `null` | `optional` |  |

</details>

### `Execution_CgtBarcodeVerifyRequest` {#schema-execution-cgtbarcodeverifyrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `barcode_payload` | `string` | `required` |  |
| `expected_batch_id` | `string` \| `null` | `optional` |  |
| `expected_din` | `string` \| `null` | `optional` |  |
| `expected_subject_id` | `string` \| `null` | `optional` |  |

</details>

### `Execution_CgtBarcodeVerifyResponse` {#schema-execution-cgtbarcodeverifyresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `barcode_id` | `string` \| `null` | `required` |  |
| `batch_id` | `string` \| `null` | `required` |  |
| `din` | `string` \| `null` | `required` |  |
| `failure_reason` | `string` \| `null` | `required` |  |
| `is_valid` | `boolean` | `required` |  |
| `site_id` | `string` \| `null` | `required` |  |
| `study_id` | `string` \| `null` | `required` |  |
| `subject_id` | `string` \| `null` | `required` |  |

</details>

### `Execution_CgtBedsideVerificationRequest` {#schema-execution-cgtbedsideverificationrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (16)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `bag_barcode_payload` | `string` | `required` |  |
| `batch_id` | `string` \| `null` | `required` |  |
| `din` | `string` | `required` |  |
| `notes` | `string` \| `null` | `optional` |  |
| `patient_wristband_id` | `string` | `required` |  |
| `primary_signature_token` | `string` | `optional` |  |
| `primary_verifier_id` | `string` | `required` |  |
| `primary_verifier_name` | `string` \| `null` | `optional` |  |
| `primary_verifier_role` | `string` \| `null` | `optional` |  |
| `secondary_signature_token` | `string` | `optional` |  |
| `secondary_verifier_id` | `string` | `optional` |  |
| `secondary_verifier_name` | `string` \| `null` | `optional` |  |
| `secondary_verifier_role` | `string` \| `null` | `optional` |  |
| `site_id` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `subject_id` | `string` | `required` |  |

</details>

### `Execution_CgtBedsideVerificationResponse` {#schema-execution-cgtbedsideverificationresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (13)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `batch_id` | `string` \| `null` | `required` |  |
| `din` | `string` | `required` |  |
| `is_match` | `boolean` | `required` |  |
| `patient_wristband_id` | `string` | `required` |  |
| `primary_verifier_id` | `string` | `required` |  |
| `secondary_verifier_id` | `string` | `required` |  |
| `site_id` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `subject_id` | `string` | `required` |  |
| `unlock_token` | `string` | `required` |  |
| `verification_id` | `string` (`uuid`) | `required` |  |
| `verified_at` | `string` (`date-time`) | `required` |  |

</details>

### `Execution_CgtChainOfCustodyResponse` {#schema-execution-cgtchainofcustodyresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (18)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `batch_id` | `string` \| `null` | `required` |  |
| `collection_site_id` | `string` \| `null` | `required` |  |
| `created_at` | `string` (`date-time`) | `required` |  |
| `created_by` | `string` | `required` |  |
| `cryo_temp_excursion_count` | `integer` (`int32`) | `required` |  |
| `current_stage` | `string` | `required` |  |
| `din` | `string` | `required` |  |
| `harvested_at` | `string` (`date-time`) \| `null` | `required` |  |
| `id` | `string` (`uuid`) | `required` |  |
| `infused_at` | `string` (`date-time`) \| `null` | `required` |  |
| `lowest_temp_celsius` | `number` (`double`) \| `null` | `required` |  |
| `manufacturing_facility_id` | `string` \| `null` | `required` |  |
| `reconciled_at` | `string` (`date-time`) \| `null` | `required` |  |
| `site_id` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `subject_id` | `string` | `required` |  |
| `unlock_token` | `string` \| `null` | `required` |  |

</details>

### `Execution_CgtDestructionCertificateCreateRequest` {#schema-execution-cgtdestructioncertificatecreaterequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (22)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `coc_id` | `string` (`uuid`) \| `null` | `optional` |  |
| `destruction_facility` | `string` | `required` |  |
| `destruction_method` | `string` | `required` |  |
| `destruction_reason` | `string` | `required` |  |
| `din` | `string` \| `null` | `optional` |  |
| `kit_numbers` | array of `string` | `required` |  |
| `lot_number` | `string` | `required` |  |
| `notes` | `string` \| `null` | `optional` |  |
| `quantity_destroyed` | `integer` (`int32`) | `optional` |  |
| `residual_volume_ml` | `number` (`double`) \| `null` | `optional` |  |
| `site_id` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `subject_id` | `string` \| `null` | `optional` |  |
| `vial_identifiers` | array of `string` | `required` |  |
| `witness_1_id` | `string` | `required` |  |
| `witness_1_name` | `string` \| `null` | `optional` |  |
| `witness_1_role` | `string` \| `null` | `optional` |  |
| `witness_1_signature_token` | `string` | `optional` |  |
| `witness_2_id` | `string` | `optional` |  |
| `witness_2_name` | `string` \| `null` | `optional` |  |
| `witness_2_role` | `string` \| `null` | `optional` |  |
| `witness_2_signature_token` | `string` | `optional` |  |

</details>

### `Execution_CgtDestructionCertificateResponse` {#schema-execution-cgtdestructioncertificateresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (22)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `certificate_number` | `string` | `required` |  |
| `chain_of_custody_id` | `string` (`uuid`) \| `null` | `required` |  |
| `cryptographic_seal` | `string` | `required` |  |
| `destruction_facility` | `string` | `required` |  |
| `destruction_method` | `string` | `required` |  |
| `destruction_reason` | `string` | `required` |  |
| `destruction_timestamp` | `string` (`date-time`) | `required` |  |
| `din` | `string` \| `null` | `required` |  |
| `id` | `string` (`uuid`) | `required` |  |
| `kit_numbers` | array of `string` | `required` |  |
| `lot_number` | `string` | `required` |  |
| `notes` | `string` \| `null` | `required` |  |
| `quantity_destroyed` | `integer` (`int32`) | `required` |  |
| `residual_volume_ml` | `number` (`double`) \| `null` | `required` |  |
| `site_id` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `subject_id` | `string` \| `null` | `required` |  |
| `unit_of_measure` | `string` | `required` |  |
| `vial_identifiers` | array of `string` | `required` |  |
| `witness_1_id` | `string` | `required` |  |
| `witness_2_id` | `string` | `required` |  |

</details>

### `Execution_CgtFullTraceabilityResponse` {#schema-execution-cgtfulltraceabilityresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `bedside_verification` | [`Execution_CgtBedsideVerificationResponse`](#schema-execution-cgtbedsideverificationresponse) \| `null` | `required` |  |
| `chain_of_custody` | [`Execution_CgtChainOfCustodyResponse`](#schema-execution-cgtchainofcustodyresponse) \| `null` | `required` |  |
| `destruction_certificate` | [`Execution_CgtDestructionCertificateResponse`](#schema-execution-cgtdestructioncertificateresponse) \| `null` | `required` |  |
| `milestones` | array of [`Execution_CgtMilestoneResponse`](#schema-execution-cgtmilestoneresponse) | `required` |  |

</details>

### `Execution_CgtInfusionExecuteRequest` {#schema-execution-cgtinfusionexecuterequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (11)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `adverse_event_observed` | `boolean` | `optional` |  |
| `chain_of_custody_id` | `string` (`uuid`) | `required` |  |
| `infusion_end` | `string` (`date-time`) \| `null` | `optional` |  |
| `infusion_start` | `string` (`date-time`) \| `null` | `optional` |  |
| `notes` | `string` \| `null` | `optional` |  |
| `primary_verifier_id` | `string` | `required` |  |
| `site_id` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `subject_id` | `string` | `required` |  |
| `unlock_token` | `string` | `required` |  |
| `volume_infused_ml` | `number` (`double`) \| `null` | `optional` |  |

</details>

### `Execution_CgtManufacturingBindRequest` {#schema-execution-cgtmanufacturingbindrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (9)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `batch_id` | `string` | `required` |  |
| `chain_of_custody_id` | `string` (`uuid`) | `required` |  |
| `manufacturing_facility_id` | `string` | `required` |  |
| `notes` | `string` \| `null` | `optional` |  |
| `qualified_person_signoff_id` | `string` \| `null` | `optional` |  |
| `release_qc_status` | `string` \| `null` | `optional` |  |
| `study_id` | `string` | `required` |  |
| `synthesis_end` | `string` (`date-time`) \| `null` | `optional` |  |
| `synthesis_start` | `string` (`date-time`) \| `null` | `optional` |  |

</details>

### `Execution_CgtMilestoneCreateRequest` {#schema-execution-cgtmilestonecreaterequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (19)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `carrier` | `string` \| `null` | `optional` |  |
| `chain_of_custody_id` | `string` (`uuid`) | `required` |  |
| `custodian_id` | `string` \| `null` | `optional` |  |
| `custodian_name` | `string` \| `null` | `optional` |  |
| `custodian_role` | `string` \| `null` | `optional` |  |
| `dry_shipper_id` | `string` \| `null` | `optional` |  |
| `facility_id` | `string` \| `null` | `optional` |  |
| `latitude` | `number` (`double`) \| `null` | `optional` |  |
| `location_name` | `string` \| `null` | `optional` |  |
| `longitude` | `number` (`double`) \| `null` | `optional` |  |
| `milestone_type` | `string` | `required` |  |
| `notes` | `string` \| `null` | `optional` |  |
| `seal_id` | `string` \| `null` | `optional` |  |
| `seal_intact` | `boolean` | `optional` |  |
| `stage` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `temperature_celsius` | `number` (`double`) \| `null` | `optional` |  |
| `timestamp` | `string` (`date-time`) \| `null` | `optional` |  |
| `tracking_number` | `string` \| `null` | `optional` |  |

</details>

### `Execution_CgtMilestoneResponse` {#schema-execution-cgtmilestoneresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (21)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `carrier` | `string` \| `null` | `required` |  |
| `chain_of_custody_id` | `string` (`uuid`) | `required` |  |
| `custodian_id` | `string` \| `null` | `required` |  |
| `custodian_name` | `string` \| `null` | `required` |  |
| `custodian_role` | `string` \| `null` | `required` |  |
| `dry_shipper_id` | `string` \| `null` | `required` |  |
| `facility_id` | `string` \| `null` | `required` |  |
| `id` | `string` (`uuid`) | `required` |  |
| `latitude` | `number` (`double`) \| `null` | `required` |  |
| `location_name` | `string` \| `null` | `required` |  |
| `longitude` | `number` (`double`) \| `null` | `required` |  |
| `milestone_type` | `string` | `required` |  |
| `notes` | `string` \| `null` | `required` |  |
| `seal_id` | `string` \| `null` | `required` |  |
| `seal_intact` | `boolean` | `required` |  |
| `stage` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `temperature_celsius` | `number` (`double`) \| `null` | `required` |  |
| `temperature_status` | `string` \| `null` | `required` |  |
| `timestamp` | `string` (`date-time`) | `required` |  |
| `tracking_number` | `string` \| `null` | `required` |  |

</details>

### `Execution_CgtVialReturnReconciliationRequest` {#schema-execution-cgtvialreturnreconciliationrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (11)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `intact_vials` | `integer` (`int32`) | `optional` |  |
| `kit_number` | `string` | `required` |  |
| `logged_by` | `string` \| `null` | `optional` |  |
| `notes` | `string` \| `null` | `optional` |  |
| `residual_volume_ml` | `number` (`double`) \| `null` | `optional` |  |
| `returned_quantity` | `integer` (`int32`) | `optional` |  |
| `site_id` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `subject_id` | `string` | `required` |  |
| `used_vials` | `integer` (`int32`) | `optional` |  |
| `vial_identifier` | `string` | `required` |  |

</details>

### `Execution_CgtVialReturnReconciliationResponse` {#schema-execution-cgtvialreturnreconciliationresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (16)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `adherence_percentage` | `number` (`double`) | `required` |  |
| `certificate_id` | `string` \| `null` | `required` |  |
| `disposition` | `string` | `required` |  |
| `id` | `string` (`uuid`) | `required` |  |
| `intact_vials` | `integer` (`int32`) | `required` |  |
| `kit_number` | `string` | `required` |  |
| `logged_at` | `string` (`date-time`) | `required` |  |
| `logged_by` | `string` | `required` |  |
| `notes` | `string` \| `null` | `required` |  |
| `residual_volume_ml` | `number` (`double`) \| `null` | `required` |  |
| `returned_quantity` | `integer` (`int32`) | `required` |  |
| `site_id` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `subject_id` | `string` | `required` |  |
| `used_vials` | `integer` (`int32`) | `required` |  |
| `vial_identifier` | `string` | `required` |  |

</details>

### `Execution_ChairAdjudicateRequest` {#schema-execution-chairadjudicaterequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (10)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `adjudication_rationale` | `string` | `optional` |  |
| `binding_reader` | `string` \| `null` | `optional` |  |
| `criteria_confirmed` | array of `string` \| `null` | `optional` |  |
| `final_adjudicated_onset_date` | `string` | `required` |  |
| `final_classification` | `string` | `required` |  |
| `final_subtype` | `string` \| `null` | `required` |  |
| `password` | `string` \| `null` | `optional` |  |
| `signature_meaning` | `string` \| `null` | `optional` |  |
| `signature_token` | `string` \| `null` | `optional` |  |
| `signer_name` | `string` \| `null` | `optional` |  |

</details>

### `Execution_ChairAdjudicationResponse` {#schema-execution-chairadjudicationresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (12)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `adjudication_rationale` | `string` | `required` |  |
| `binding_reader` | `string` | `required` |  |
| `chair_id` | `string` | `required` |  |
| `chair_name` | `string` | `required` |  |
| `criteria_confirmed` | array of `string` | `required` |  |
| `determination_id` | `string` | `required` |  |
| `dossier_id` | `string` | `required` |  |
| `electronic_signature` | [`Execution_SignatureManifest`](#schema-execution-signaturemanifest) | `required` |  |
| `final_adjudicated_onset_date` | `string` | `required` |  |
| `final_classification` | `string` | `required` |  |
| `final_subtype` | `string` \| `null` | `required` |  |
| `finalized_at` | `string` | `required` |  |

</details>

### `Execution_ComplianceStatusResponse` {#schema-execution-compliancestatusresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `activated_at` | `string` (`date-time`) \| `null` | `optional` |  |
| `activated_by` | `string` \| `null` | `optional` |  |
| `is_activated` | `boolean` | `optional` |  |
| `is_complete` | `boolean` | `required` |  |
| `milestone` | `string` | `required` |  |
| `missing_documents` | array of `string` | `required` |  |
| `site_id` | [`Execution_SiteCode`](#schema-execution-sitecode) \| `null` | `required` |  |
| `study_id` | [`Execution_StudyCode`](#schema-execution-studycode) | `required` |  |

</details>

### `Execution_DemographicsPayload` {#schema-execution-demographicspayload}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `birthdate` | `string` \| `null` | `required` |  |
| `gender` | `string` \| `null` | `required` |  |
| `name` | `string` \| `null` | `required` |  |
| `race` | `string` \| `null` | `required` |  |

</details>

### `Execution_DeviceDeficiencyCreateRequest` {#schema-execution-devicedeficiencycreaterequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (21)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `action_taken_device` | `string` | `optional` |  |
| `awareness_date` | `string` (`date-time`) | `required` |  |
| `could_lead_to_sade` | `boolean` | `optional` |  |
| `deficiency_description` | `string` | `required` |  |
| `deficiency_type` | `string` | `optional` |  |
| `device_inventory_id` | `string` \| `null` | `optional` |  |
| `device_name` | `string` | `optional` |  |
| `device_returned_to_sponsor` | `boolean` | `optional` |  |
| `health_impact` | `string` | `optional` |  |
| `incident_date` | `string` (`date-time`) | `required` |  |
| `lot_number` | `string` | `required` |  |
| `patient_involved` | `boolean` | `optional` |  |
| `quarantine_scope` | `string` | `optional` |  |
| `reason_for_change` | `string` | `optional` |  |
| `return_tracking_number` | `string` \| `null` | `optional` |  |
| `serial_number` | `string` \| `null` | `optional` |  |
| `signature_token` | `string` \| `null` | `optional` |  |
| `site_id` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `subject_id` | `string` \| `null` | `optional` |  |
| `udi_di` | `string` | `required` |  |

</details>

### `Execution_DeviceDeficiencyResponse` {#schema-execution-devicedeficiencyresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (30)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `action_taken_device` | `string` | `required` |  |
| `awareness_date` | `string` (`date-time`) | `required` |  |
| `could_lead_to_sade` | `boolean` | `required` |  |
| `created_at` | `string` (`date-time`) | `required` |  |
| `created_by` | `string` | `required` |  |
| `deficiency_description` | `string` | `required` |  |
| `deficiency_type` | `string` | `required` |  |
| `device_inventory_id` | `string` \| `null` | `required` |  |
| `device_name` | `string` | `required` |  |
| `device_returned_to_sponsor` | `boolean` | `required` |  |
| `health_impact` | `string` | `required` |  |
| `id` | `string` | `required` |  |
| `incident_date` | `string` (`date-time`) | `required` |  |
| `investigation_status` | `string` | `required` |  |
| `lot_number` | `string` | `required` |  |
| `patient_involved` | `boolean` | `required` |  |
| `quarantine_scope` | `string` | `required` |  |
| `quarantine_triggered` | `boolean` | `required` |  |
| `reason_for_change` | `string` | `required` |  |
| `return_tracking_number` | `string` \| `null` | `required` |  |
| `root_cause_analysis` | `string` \| `null` | `required` |  |
| `safety_case_id` | `string` \| `null` | `required` |  |
| `safety_classification` | `string` \| `null` | `required` |  |
| `serial_number` | `string` \| `null` | `required` |  |
| `site_id` | `string` | `required` |  |
| `sponsor_evaluation` | `string` \| `null` | `required` |  |
| `study_id` | `string` | `required` |  |
| `subject_id` | `string` \| `null` | `required` |  |
| `udi_di` | `string` | `required` |  |
| `version_index` | `integer` (`int64`) | `required` |  |

</details>

### `Execution_DeviceDispensationRequest` {#schema-execution-devicedispensationrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `device_inventory_id` | `string` \| `null` | `optional` |  |
| `reason_for_change` | `string` | `optional` |  |
| `scanned_barcode` | `string` \| `null` | `optional` |  |
| `serial_number` | `string` \| `null` | `optional` |  |
| `site_id` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `subject_id` | `string` | `required` |  |

</details>

### `Execution_DeviceDispensationResponse` {#schema-execution-devicedispensationresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `device` | [`Execution_DeviceInventoryResponse`](#schema-execution-deviceinventoryresponse) | `required` |  |
| `dispensed_at` | `string` (`date-time`) | `required` |  |
| `message` | `string` | `required` |  |
| `status` | `string` | `required` |  |

</details>

### `Execution_DeviceInventoryCreateRequest` {#schema-execution-deviceinventorycreaterequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (16)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `brand_name` | `string` | `required` |  |
| `device_classification` | `string` | `optional` |  |
| `device_identifier` | `string` \| `null` | `optional` |  |
| `device_model` | `string` \| `null` | `optional` |  |
| `device_type` | `string` | `optional` |  |
| `expiration_date` | `string` (`date-time`) \| `null` | `optional` |  |
| `lot_number` | `string` | `required` |  |
| `manufacturing_date` | `string` (`date-time`) \| `null` | `optional` |  |
| `quantity` | `integer` (`int32`) | `optional` |  |
| `raw_barcode` | `string` \| `null` | `optional` |  |
| `reason_for_change` | `string` | `optional` |  |
| `serial_number` | `string` \| `null` | `optional` |  |
| `site_id` | `string` | `required` |  |
| `software_version` | `string` \| `null` | `optional` |  |
| `study_id` | `string` | `required` |  |
| `udi_di` | `string` | `required` |  |

</details>

### `Execution_DeviceInventoryResponse` {#schema-execution-deviceinventoryresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (28)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `allocated_subject_id` | `string` \| `null` | `required` |  |
| `brand_name` | `string` | `required` |  |
| `created_at` | `string` (`date-time`) | `required` |  |
| `created_by` | `string` | `required` |  |
| `device_classification` | `string` | `required` |  |
| `device_identifier` | `string` | `required` |  |
| `device_model` | `string` \| `null` | `required` |  |
| `device_type` | `string` | `required` |  |
| `dispensed_at` | `string` (`date-time`) \| `null` | `required` |  |
| `dispensed_by` | `string` \| `null` | `required` |  |
| `expiration_date` | `string` (`date-time`) \| `null` | `required` |  |
| `id` | `string` | `required` |  |
| `is_dispensation_blocked` | `boolean` | `required` |  |
| `lot_number` | `string` | `required` |  |
| `manufacturing_date` | `string` (`date-time`) \| `null` | `required` |  |
| `quarantine_reason` | `string` \| `null` | `required` |  |
| `quarantined_at` | `string` (`date-time`) \| `null` | `required` |  |
| `quarantined_by` | `string` \| `null` | `required` |  |
| `reason_for_change` | `string` | `required` |  |
| `serial_number` | `string` \| `null` | `required` |  |
| `site_id` | `string` | `required` |  |
| `software_version` | `string` \| `null` | `required` |  |
| `status` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `udi_carrier_format` | `string` | `required` |  |
| `udi_di` | `string` | `required` |  |
| `udi_pi` | `string` \| `null` | `required` |  |
| `version_index` | `integer` (`int64`) | `required` |  |

</details>

### `Execution_DeviceInvestigationSummaryResponse` {#schema-execution-deviceinvestigationsummaryresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (9)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `active_quarantines` | `integer` (`int32`) | `required` |  |
| `available_devices` | `integer` (`int32`) | `required` |  |
| `dispensed_devices` | `integer` (`int32`) | `required` |  |
| `open_safety_cases` | `integer` (`int32`) | `required` |  |
| `quarantined_devices` | `integer` (`int32`) | `required` |  |
| `sade_count` | `integer` (`int32`) | `required` |  |
| `study_id` | `string` | `required` |  |
| `total_deficiencies` | `integer` (`int32`) | `required` |  |
| `total_devices` | `integer` (`int32`) | `required` |  |

</details>

### `Execution_DeviceQuarantineCreateRequest` {#schema-execution-devicequarantinecreaterequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `device_inventory_id` | `string` \| `null` | `optional` |  |
| `lot_number` | `string` \| `null` | `optional` |  |
| `reason` | `string` | `required` |  |
| `reason_for_change` | `string` | `optional` |  |
| `scope` | `string` | `optional` |  |
| `serial_number` | `string` \| `null` | `optional` |  |
| `site_id` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |

</details>

### `Execution_DeviceQuarantineReleaseRequest` {#schema-execution-devicequarantinereleaserequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `quarantine_id` | `string` | `required` |  |
| `reason_for_change` | `string` | `optional` |  |
| `release_justification` | `string` | `required` |  |
| `signature_token` | `string` | `required` |  |

</details>

### `Execution_DeviceQuarantineResponse` {#schema-execution-devicequarantineresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (19)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `created_at` | `string` (`date-time`) | `required` |  |
| `created_by` | `string` | `required` |  |
| `deficiency_id` | `string` \| `null` | `required` |  |
| `device_inventory_id` | `string` \| `null` | `required` |  |
| `id` | `string` | `required` |  |
| `lot_number` | `string` \| `null` | `required` |  |
| `quarantined_at` | `string` (`date-time`) | `required` |  |
| `quarantined_by` | `string` | `required` |  |
| `reason` | `string` | `required` |  |
| `reason_for_change` | `string` | `required` |  |
| `release_justification` | `string` \| `null` | `required` |  |
| `released_at` | `string` (`date-time`) \| `null` | `required` |  |
| `released_by` | `string` \| `null` | `required` |  |
| `scope` | `string` | `required` |  |
| `serial_number` | `string` \| `null` | `required` |  |
| `site_id` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `version_index` | `integer` (`int64`) | `required` |  |

</details>

### `Execution_EtmfCompletenessWebhookPayload` {#schema-execution-etmfcompletenesswebhookpayload}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `is_complete` | `boolean` | `required` |  |
| `milestone` | `string` | `required` |  |
| `missing_artifacts` | array of `string` \| `null` | `required` |  |
| `site_id` | `string` \| `null` | `required` |  |
| `study_id` | `string` | `required` |  |

</details>

### `Execution_HttpValidationProblemDetails` {#schema-execution-httpvalidationproblemdetails}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `detail` | `string` \| `null` | `optional` |  |
| `errors` | `object` | `optional` |  |
| `instance` | `string` \| `null` | `optional` |  |
| `status` | `integer` (`int32`) \| `null` | `optional` |  |
| `title` | `string` \| `null` | `optional` |  |
| `type` | `string` \| `null` | `optional` |  |

</details>

### `Execution_InvalidRowDetailResponse` {#schema-execution-invalidrowdetailresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `dictionary_type` | `string` | `required` |  |
| `error_reason` | `string` | `required` |  |
| `row_number` | `integer` (`int32`) | `required` |  |
| `target_code` | `string` | `required` |  |
| `verbatim_text` | `string` | `required` |  |

</details>

### `Execution_IpKitBatchReconciliationRequest` {#schema-execution-ipkitbatchreconciliationrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `items` | array of [`Execution_IpKitItemReconcileRequest`](#schema-execution-ipkititemreconcilerequest) | `required` |  |
| `notes` | `string` \| `null` | `optional` |  |
| `processed_by` | `string` \| `null` | `optional` |  |
| `reason_for_change` | `string` | `optional` |  |
| `site_id` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |

</details>

### `Execution_IpKitCertificateResponse` {#schema-execution-ipkitcertificateresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (9)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `batch_id` | `string` | `required` |  |
| `batch_number` | `string` | `required` |  |
| `certificate_id` | `string` | `required` |  |
| `digital_signature` | `string` | `required` |  |
| `issued_at` | `string` (`date-time`) | `required` |  |
| `issued_by` | `string` | `required` |  |
| `kit_numbers` | array of `string` | `required` |  |
| `site_id` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |

</details>

### `Execution_IpKitInventoryResponse` {#schema-execution-ipkitinventoryresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `expiration_date` | `string` (`date-time`) | `required` |  |
| `kit_number` | `string` | `required` |  |
| `lot_number` | `string` | `required` |  |
| `residual_volume_ml` | `number` (`double`) \| `null` | `optional` |  |
| `residual_volume_percentage` | `number` (`double`) \| `null` | `optional` |  |
| `site_id` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |

</details>

### `Execution_IpKitItemReconcileRequest` {#schema-execution-ipkititemreconcilerequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `disposition` | `string` | `required` |  |
| `disposition_reason` | `string` | `required` |  |
| `expected_lot_number` | `string` | `required` |  |
| `kit_number` | `string` | `required` |  |
| `lot_number` | `string` | `required` |  |
| `notes` | `string` \| `null` | `optional` |  |
| `residual_volume_ml` | `number` (`double`) \| `null` | `required` |  |
| `residual_volume_percentage` | `number` (`double`) \| `null` | `required` |  |

</details>

### `Execution_IpKitItemResponse` {#schema-execution-ipkititemresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `disposition` | `string` | `required` |  |
| `disposition_reason` | `string` | `required` |  |
| `expected_lot_number` | `string` | `required` |  |
| `is_quarantined` | `boolean` | `required` |  |
| `kit_number` | `string` | `required` |  |
| `lot_number` | `string` | `required` |  |
| `residual_volume_ml` | `number` (`double`) \| `null` | `required` |  |
| `residual_volume_percentage` | `number` (`double`) \| `null` | `required` |  |

</details>

### `Execution_IpKitReconciliationBatchResponse` {#schema-execution-ipkitreconciliationbatchresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (11)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `batch_id` | `string` | `required` |  |
| `batch_number` | `string` | `required` |  |
| `certificate` | [`Execution_IpKitCertificateResponse`](#schema-execution-ipkitcertificateresponse) | `required` |  |
| `certificate_id` | `string` | `required` |  |
| `items` | array of [`Execution_IpKitItemResponse`](#schema-execution-ipkititemresponse) | `required` |  |
| `processed_at` | `string` (`date-time`) | `required` |  |
| `processed_by` | `string` | `required` |  |
| `quarantined_kits` | `integer` (`int32`) | `required` |  |
| `site_id` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `total_kits` | `integer` (`int32`) | `required` |  |

</details>

### `Execution_IpKitScanValidateRequest` {#schema-execution-ipkitscanvalidaterequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `kit_number` | `string` | `required` |  |
| `scanned_lot_number` | `string` \| `null` | `optional` |  |
| `site_id` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |

</details>

### `Execution_IpKitScanValidateResponse` {#schema-execution-ipkitscanvalidateresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `expected_lot_number` | `string` | `required` |  |
| `is_lot_match` | `boolean` | `required` |  |
| `is_valid` | `boolean` | `required` |  |
| `kit_number` | `string` | `required` |  |
| `message` | `string` | `required` |  |
| `scanned_lot_number` | `string` \| `null` | `required` |  |
| `status` | `string` | `required` |  |

</details>

### `Execution_IpKitSeedItem` {#schema-execution-ipkitseeditem}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `expiration_date` | `string` (`date-time`) | `required` |  |
| `kit_number` | `string` | `required` |  |
| `lot_number` | `string` | `required` |  |
| `residual_volume_ml` | `number` (`double`) \| `null` | `optional` |  |
| `residual_volume_percentage` | `number` (`double`) \| `null` | `optional` |  |
| `status` | `string` \| `null` | `optional` |  |
| `treatment_arm` | `string` \| `null` | `optional` |  |

</details>

### `Execution_LabReferenceRangeCreateRequest` {#schema-execution-labreferencerangecreaterequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (16)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `age_high` | `number` (`double`) \| `null` | `optional` |  |
| `age_low` | `number` (`double`) \| `null` | `optional` |  |
| `critical_high` | `number` (`double`) \| `null` | `optional` |  |
| `critical_low` | `number` (`double`) \| `null` | `optional` |  |
| `high_bound` | `number` (`double`) \| `null` | `optional` |  |
| `loinc_code` | `string` \| `null` | `optional` |  |
| `low_bound` | `number` (`double`) \| `null` | `optional` |  |
| `normalized_unit` | `string` \| `null` | `optional` |  |
| `reason_for_change` | `string` | `optional` |  |
| `sex_applicability` | `string` | `optional` |  |
| `site_id` | `string` \| `null` | `optional` |  |
| `source` | `string` | `optional` |  |
| `study_id` | `string` | `required` |  |
| `test_code` | `string` | `required` |  |
| `test_name` | `string` | `required` |  |
| `unit` | `string` | `optional` |  |

</details>

### `Execution_LabReferenceRangeResponse` {#schema-execution-labreferencerangeresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (20)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `age_high` | `number` (`double`) \| `null` | `required` |  |
| `age_low` | `number` (`double`) \| `null` | `required` |  |
| `created_at` | `string` (`date-time`) | `required` |  |
| `created_by` | `string` | `required` |  |
| `critical_high` | `number` (`double`) \| `null` | `required` |  |
| `critical_low` | `number` (`double`) \| `null` | `required` |  |
| `high_bound` | `number` (`double`) \| `null` | `required` |  |
| `id` | `string` | `required` |  |
| `is_active` | `boolean` | `required` |  |
| `loinc_code` | `string` \| `null` | `required` |  |
| `low_bound` | `number` (`double`) \| `null` | `required` |  |
| `normalized_unit` | `string` | `required` |  |
| `sex_applicability` | `string` | `required` |  |
| `site_id` | `string` \| `null` | `required` |  |
| `source` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `test_code` | `string` | `required` |  |
| `test_name` | `string` | `required` |  |
| `unit` | `string` | `required` |  |
| `version` | `integer` (`int64`) | `required` |  |

</details>

### `Execution_LabReferenceRangeUpdateRequest` {#schema-execution-labreferencerangeupdaterequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (12)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `age_high` | `number` (`double`) \| `null` | `optional` |  |
| `age_low` | `number` (`double`) \| `null` | `optional` |  |
| `critical_high` | `number` (`double`) \| `null` | `optional` |  |
| `critical_low` | `number` (`double`) \| `null` | `optional` |  |
| `high_bound` | `number` (`double`) \| `null` | `optional` |  |
| `loinc_code` | `string` \| `null` | `optional` |  |
| `low_bound` | `number` (`double`) \| `null` | `optional` |  |
| `normalized_unit` | `string` \| `null` | `optional` |  |
| `reason_for_change` | `string` | `optional` |  |
| `sex_applicability` | `string` \| `null` | `optional` |  |
| `test_name` | `string` \| `null` | `optional` |  |
| `unit` | `string` \| `null` | `optional` |  |

</details>

### `Execution_LabToxicityEvaluateRequest` {#schema-execution-labtoxicityevaluaterequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `baseline_value` | `number` (`double`) \| `null` | `optional` |  |
| `evaluated_at` | `string` (`date-time`) \| `null` | `optional` |  |
| `ref_high` | `number` (`double`) \| `null` | `optional` |  |
| `ref_low` | `number` (`double`) \| `null` | `optional` |  |
| `test_code` | `string` | `required` |  |
| `unit` | `string` | `required` |  |
| `value` | `number` (`double`) | `required` |  |

</details>

### `Execution_LabToxicityEvaluateResponse` {#schema-execution-labtoxicityevaluateresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (11)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `ctcae_grade` | `integer` (`int32`) | `required` |  |
| `delta_percentage` | `number` (`double`) \| `null` | `required` |  |
| `grading_scale` | `string` | `required` |  |
| `is_critical_ae_trigger` | `boolean` | `required` |  |
| `is_delta_check_alert` | `boolean` | `required` |  |
| `is_hys_law_warning` | `boolean` | `required` |  |
| `observed_value` | `number` (`double`) | `required` |  |
| `test_code` | `string` | `required` |  |
| `test_name` | `string` | `required` |  |
| `toxicity_description` | `string` | `required` |  |
| `unit` | `string` | `required` |  |

</details>

### `Execution_LoincCatalogItemResponse` {#schema-execution-loinccatalogitemresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `category` | `string` | `required` |  |
| `loinc_code` | `string` | `required` |  |
| `molecular_weight` | `number` (`double`) \| `null` | `required` |  |
| `standard_unit` | `string` | `required` |  |
| `test_code` | `string` | `required` |  |
| `test_name` | `string` | `required` |  |

</details>

### `Execution_LoincCatalogResponse` {#schema-execution-loinccatalogresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `items` | array of [`Execution_LoincCatalogItemResponse`](#schema-execution-loinccatalogitemresponse) | `required` |  |
| `total_count` | `integer` (`int32`) | `required` |  |

</details>

### `Execution_OperationEnvelopeResponse` {#schema-execution-operationenveloperesponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `message` | `string` | `required` |  |
| `status` | `string` | `required` |  |

</details>

### `Execution_ParsedUdiResponse` {#schema-execution-parsedudiresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (10)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `expiration_date` | `string` \| `null` | `required` |  |
| `is_valid` | `boolean` | `required` |  |
| `lot_number` | `string` \| `null` | `required` |  |
| `manufacturing_date` | `string` \| `null` | `required` |  |
| `raw_barcode` | `string` | `required` |  |
| `serial_number` | `string` \| `null` | `required` |  |
| `software_version` | `string` \| `null` | `required` |  |
| `udi_carrier_format` | `string` | `required` |  |
| `udi_di` | `string` | `required` |  |
| `validation_errors` | array of `string` | `required` |  |

</details>

### `Execution_PreflightValidationReportResponse` {#schema-execution-preflightvalidationreportresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `can_import` | `boolean` | `required` |  |
| `duplicate_count` | `integer` (`int32`) | `required` |  |
| `invalid_count` | `integer` (`int32`) | `required` |  |
| `invalid_rows` | array of [`Execution_InvalidRowDetailResponse`](#schema-execution-invalidrowdetailresponse) | `required` |  |
| `total_rows` | `integer` (`int32`) | `required` |  |
| `valid_count` | `integer` (`int32`) | `required` |  |

</details>

### `Execution_ProblemDetails` {#schema-execution-problemdetails}

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

### `Execution_ProtocolVersionPayload` {#schema-execution-protocolversionpayload}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `study_id` | `string` | `required` |  |
| `version_index` | `integer` (`int32`) | `required` |  |
| `version_tag` | `string` | `required` |  |

</details>

### `Execution_RandomizationResponse` {#schema-execution-randomizationresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `kit_number` | `string` \| `null` | `required` |  |
| `randomization_id` | `string` (`uuid`) | `required` |  |
| `randomized_at` | `string` (`date-time`) | `required` |  |
| `scheme_version` | `integer` (`int32`) | `required` |  |
| `status` | `string` | `required` |  |
| `stratum_key` | `string` | `required` |  |
| `study_id` | [`Execution_StudyCode`](#schema-execution-studycode) | `required` |  |
| `subject_id` | [`Execution_SubjectCode`](#schema-execution-subjectcode) | `required` |  |

</details>

### `Execution_RandomizationSchemeDraftRequest` {#schema-execution-randomizationschemedraftrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `arms` | `object` | `required` |  |
| `block_sizes` | array of `integer` (`int32`) \| `null` | `required` |  |
| `method` | `string` | `required` |  |
| `preferred_arm_probability` | `number` (`double`) \| `null` | `required` |  |
| `requires_kit` | `boolean` | `required` |  |
| `slots_per_stratum` | `integer` (`int32`) \| `null` | `required` |  |
| `stratification_factors` | `object` \| `null` | `required` |  |
| `study_id` | [`Execution_StudyCode`](#schema-execution-studycode) | `required` |  |

</details>

### `Execution_RandomizationSchemeResponse` {#schema-execution-randomizationschemeresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (18)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `approved_at` | `string` (`date-time`) \| `null` | `required` |  |
| `approved_by` | `string` \| `null` | `required` |  |
| `arms` | `object` | `required` |  |
| `block_sizes` | array of `integer` (`int32`) | `required` |  |
| `configuration_hash` | `string` | `required` |  |
| `drafted_at` | `string` (`date-time`) | `required` |  |
| `drafted_by` | `string` | `required` |  |
| `id` | `string` (`uuid`) | `required` |  |
| `list_size` | `integer` (`int32`) | `required` |  |
| `method` | `string` | `required` |  |
| `preferred_arm_probability` | `number` (`double`) | `required` |  |
| `requires_kit` | `boolean` | `required` |  |
| `retired_at` | `string` (`date-time`) \| `null` | `required` |  |
| `slots_per_stratum` | `integer` (`int32`) | `required` |  |
| `status` | `string` | `required` |  |
| `stratification_factors` | `object` | `required` |  |
| `study_id` | [`Execution_StudyCode`](#schema-execution-studycode) | `required` |  |
| `version` | `integer` (`int32`) | `required` |  |

</details>

### `Execution_RandomizeSubjectRequest` {#schema-execution-randomizesubjectrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (1)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `stratification_factors` | `object` \| `null` | `required` |  |

</details>

### `Execution_SeedInventoryRequest` {#schema-execution-seedinventoryrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `kits` | array of [`Execution_IpKitSeedItem`](#schema-execution-ipkitseeditem) | `required` |  |
| `site_id` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |

</details>

### `Execution_SignatureManifest` {#schema-execution-signaturemanifest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `hmac_signature` | `string` \| `null` | `required` |  |
| `payload_hash` | `string` | `required` |  |
| `signed_at` | `string` (`date-time`) | `required` |  |
| `signer_id` | `string` | `required` |  |
| `signer_name` | `string` | `required` |  |
| `signer_role` | `string` | `required` |  |
| `signing_reason` | `string` | `required` |  |

</details>

### `Execution_SiteActivationRequest` {#schema-execution-siteactivationrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (1)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `study_id` | `string` | `required` |  |

</details>

### `Execution_SiteCode` {#schema-execution-sitecode}

- **Type:** string

### `Execution_StagedAdverseEventActionRequest` {#schema-execution-stagedadverseeventactionrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `action` | `string` | `required` |  |
| `investigator_notes` | `string` \| `null` | `optional` |  |
| `reason_for_change` | `string` | `required` |  |

</details>

### `Execution_StagedAdverseEventActionResponse` {#schema-execution-stagedadverseeventactionresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `action_taken` | `string` | `required` |  |
| `id` | `string` | `required` |  |
| `message` | `string` | `required` |  |
| `review_status` | `string` | `required` |  |
| `timestamp` | `string` (`date-time`) | `required` |  |

</details>

### `Execution_StagedAdverseEventReviewResponse` {#schema-execution-stagedadverseeventreviewresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (24)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `clinical_description` | `string` | `required` |  |
| `created_at` | `string` (`date-time`) \| `null` | `required` |  |
| `ctcae_grade` | `integer` (`int32`) | `required` |  |
| `delta_percentage` | `number` (`double`) \| `null` | `required` |  |
| `grading_scale` | `string` | `required` |  |
| `id` | `string` | `required` |  |
| `investigator_notes` | `string` \| `null` | `required` |  |
| `is_deleted` | `boolean` | `required` |  |
| `is_delta_check_alert` | `boolean` | `required` |  |
| `is_hys_law_warning` | `boolean` | `required` |  |
| `lab_value` | `number` (`double`) | `required` |  |
| `observation_id` | `string` \| `null` | `required` |  |
| `review_status` | `string` | `required` |  |
| `reviewed_at` | `string` (`date-time`) \| `null` | `required` |  |
| `reviewed_by` | `string` \| `null` | `required` |  |
| `site_id` | `string` \| `null` | `required` |  |
| `study_id` | `string` | `required` |  |
| `subject_id` | `string` | `required` |  |
| `test_code` | `string` | `required` |  |
| `test_name` | `string` | `required` |  |
| `toxicity_term` | `string` | `required` |  |
| `unit` | `string` | `required` |  |
| `version` | `integer` (`int32`) | `required` |  |
| `visit_id` | `string` \| `null` | `required` |  |

</details>

### `Execution_StudyCode` {#schema-execution-studycode}

- **Type:** string

### `Execution_SubjectCode` {#schema-execution-subjectcode}

- **Type:** string

### `Execution_SubjectConsentId` {#schema-execution-subjectconsentid}

- **Type:** string

### `Execution_SubjectConsentRequest` {#schema-execution-subjectconsentrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `icf_signed` | `boolean` | `required` |  |
| `icf_signed_date` | `string` (`date-time`) \| `null` | `required` |  |
| `is_paper_override` | `boolean` | `optional` |  |
| `protocol_version` | [`Execution_ProtocolVersionPayload`](#schema-execution-protocolversionpayload) | `required` |  |
| `requires_reconsent` | `boolean` | `optional` |  |

</details>

### `Execution_SubjectConsentResponse` {#schema-execution-subjectconsentresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (10)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `icf_signed` | `boolean` | `required` |  |
| `icf_signed_date` | `string` (`date-time`) \| `null` | `required` |  |
| `id` | [`Execution_SubjectConsentId`](#schema-execution-subjectconsentid) | `required` |  |
| `is_paper_override` | `boolean` | `required` |  |
| `requires_reconsent` | `boolean` | `required` |  |
| `study_id` | [`Execution_StudyCode`](#schema-execution-studycode) | `required` |  |
| `subject_id` | [`Execution_SubjectCode`](#schema-execution-subjectcode) | `required` |  |
| `version` | `integer` (`int64`) | `required` |  |
| `version_index` | `integer` (`int32`) | `required` |  |
| `version_tag` | `string` | `required` |  |

</details>

### `Execution_SubjectCountsResponse` {#schema-execution-subjectcountsresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `counts` | `object` | `required` |  |
| `total` | `integer` (`int32`) | `required` |  |

</details>

### `Execution_SubjectCreateRequest` {#schema-execution-subjectcreaterequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `demographics` | [`Execution_DemographicsPayload`](#schema-execution-demographicspayload) \| `null` | `required` |  |
| `site_id` | `string` \| `null` | `required` |  |
| `study_id` | `string` | `required` |  |
| `subject_id` | `string` | `required` |  |

</details>

### `Execution_SubjectDemographicsUpdateRequest` {#schema-execution-subjectdemographicsupdaterequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `demographics` | [`Execution_DemographicsPayload`](#schema-execution-demographicspayload) \| `null` | `required` |  |
| `strat_factors` | `object` \| `null` | `required` |  |

</details>

### `Execution_SubjectDetailResponse` {#schema-execution-subjectdetailresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (11)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `active_protocol_version` | `string` | `required` |  |
| `enrollment_index` | `integer` (`int32`) | `required` |  |
| `enrollment_ref` | `string` \| `null` | `optional` |  |
| `id` | [`Execution_SubjectId`](#schema-execution-subjectid) | `required` |  |
| `investigational_product_id` | `string` \| `null` | `required` |  |
| `randomization_seed` | `string` \| `null` | `required` |  |
| `site_id` | [`Execution_SiteCode`](#schema-execution-sitecode) \| `null` | `required` |  |
| `status` | `string` | `required` |  |
| `study_id` | [`Execution_StudyCode`](#schema-execution-studycode) | `required` |  |
| `subject_id` | [`Execution_SubjectCode`](#schema-execution-subjectcode) | `required` |  |
| `treatment_group` | `string` \| `null` | `required` |  |

</details>

### `Execution_SubjectId` {#schema-execution-subjectid}

- **Type:** string

### `Execution_SubjectListResponse` {#schema-execution-subjectlistresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `items` | array of [`Execution_SubjectSummaryResponse`](#schema-execution-subjectsummaryresponse) | `required` |  |
| `limit` | `integer` (`int32`) | `required` |  |
| `offset` | `integer` (`int32`) | `required` |  |
| `total` | `integer` (`int32`) | `required` |  |

</details>

### `Execution_SubjectResponse` {#schema-execution-subjectresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `active_protocol_version` | `string` \| `null` | `required` |  |
| `encrypted_demographics` | `string` \| `null` | `required` |  |
| `enrollment_ref` | `string` \| `null` | `optional` |  |
| `id` | [`Execution_SubjectId`](#schema-execution-subjectid) | `required` |  |
| `site_id` | [`Execution_SiteCode`](#schema-execution-sitecode) \| `null` | `required` |  |
| `status` | `string` | `required` |  |
| `study_id` | [`Execution_StudyCode`](#schema-execution-studycode) | `required` |  |
| `subject_id` | [`Execution_SubjectCode`](#schema-execution-subjectcode) | `required` |  |

</details>

### `Execution_SubjectSiteAssignmentRequest` {#schema-execution-subjectsiteassignmentrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (1)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `site_id` | `string` \| `null` | `required` |  |

</details>

### `Execution_SubjectStateUpdateRequest` {#schema-execution-subjectstateupdaterequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `state` | `string` \| `null` | `required` |  |
| `status` | `string` \| `null` | `required` |  |
| `target` | `string` \| `null` | `optional` |  |

</details>

### `Execution_SubjectSummaryResponse` {#schema-execution-subjectsummaryresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `active_protocol_version` | `string` | `required` |  |
| `enrollment_index` | `integer` (`int32`) | `required` |  |
| `enrollment_ref` | `string` \| `null` | `optional` |  |
| `id` | [`Execution_SubjectId`](#schema-execution-subjectid) | `required` |  |
| `site_id` | [`Execution_SiteCode`](#schema-execution-sitecode) \| `null` | `required` |  |
| `status` | `string` | `required` |  |
| `study_id` | [`Execution_StudyCode`](#schema-execution-studycode) | `required` |  |
| `subject_id` | [`Execution_SubjectCode`](#schema-execution-subjectcode) | `required` |  |

</details>

### `Execution_SynonymDeactivateResponse` {#schema-execution-synonymdeactivateresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `id` | `string` (`uuid`) | `required` |  |
| `message` | `string` | `required` |  |

</details>

### `Execution_SynonymRuleResponse` {#schema-execution-synonymruleresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (14)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `created_at` | `string` (`date-time`) | `required` |  |
| `created_by` | `string` | `required` |  |
| `dictionary_type` | `string` | `required` |  |
| `dictionary_version` | `string` | `required` |  |
| `id` | `string` (`uuid`) | `required` |  |
| `import_job_id` | `string` (`uuid`) | `required` |  |
| `reason_for_change` | `string` | `required` |  |
| `scope` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `study_id` | `string` \| `null` | `required` |  |
| `target_code` | `string` | `required` |  |
| `target_level` | `string` \| `null` | `required` |  |
| `target_term` | `string` | `required` |  |
| `verbatim_text` | `string` | `required` |  |

</details>

### `Execution_UdiParseRequest` {#schema-execution-udiparserequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (1)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `raw_barcode` | `string` | `required` |  |

</details>

### `Execution_UnitConversionRequest` {#schema-execution-unitconversionrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `from_unit` | `string` | `required` |  |
| `test_code` | `string` | `required` |  |
| `to_unit` | `string` | `required` |  |
| `value` | `number` (`double`) | `required` |  |

</details>

### `Execution_UnitConversionResponse` {#schema-execution-unitconversionresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `converted_value` | `number` (`double`) | `required` |  |
| `factor` | `number` (`double`) | `required` |  |
| `from_unit` | `string` | `required` |  |
| `input_value` | `number` (`double`) | `required` |  |
| `loinc_code` | `string` \| `null` | `required` |  |
| `test_code` | `string` | `required` |  |
| `to_unit` | `string` | `required` |  |

</details>

