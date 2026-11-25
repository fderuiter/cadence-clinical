# etmf API

Generated from `docs/openapi/etmf_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

- **Service Maturity:** <span class="VPBadge tip">Active</span> (Port `8003` · Gateway prefixes: `/api/v1/etmf`)
- **OpenAPI 3.1 Contract:** [`etmf_openapi.json`](../openapi/etmf_openapi.json) (17 operation(s), 18 schema(s))

## Operations

| Method | Path | Operation | Summary | Contract |
| ------ | ---- | --------- | ------- | -------- |
| `GET` | `/api/v1/etmf/documents` | ListEtmfDocuments | Lists eTMF documents filtered by study, site, or DIA TMF zone. | [Details](#op-listetmfdocuments) |
| `POST` | `/api/v1/etmf/documents/export` | ExportEtmfDocuments | Streams a synchronous study binder ZIP archive or enqueues an asynchronous bulk export job. | [Details](#op-exportetmfdocuments) |
| `GET` | `/api/v1/etmf/documents/{document_id}` | GetEtmfDocumentById | Retrieves an eTMF document metadata record by document ID. | [Details](#op-getetmfdocumentbyid) |
| `GET` | `/api/v1/etmf/documents/{document_id}/content` | DownloadEtmfDocumentContent | Streams SHA-256 integrity-verified binary content for an eTMF document. | [Details](#op-downloadetmfdocumentcontent) |
| `POST` | `/api/v1/etmf/documents/{document_id}/submit-qc` | SubmitEtmfDocumentForQc | Submits a DRAFT eTMF document into TECHNICAL_QC for classification review. | [Details](#op-submitetmfdocumentforqc) |
| `PATCH` | `/api/v1/etmf/documents/{document_id}/taxonomy` | UpdateEtmfDocumentTaxonomy | Updates the DIA TMF zone, section, and artifact classification of an eTMF document. | [Details](#op-updateetmfdocumenttaxonomy) |
| `GET` | `/api/v1/etmf/export-jobs` | ListEtmfExportJobs | Lists eTMF study binder export jobs visible to the caller. | [Details](#op-listetmfexportjobs) |
| `GET` | `/api/v1/etmf/export-jobs/{job_id}` | GetEtmfExportJob | Retrieves progress and download metadata for a background eTMF binder export job. | [Details](#op-getetmfexportjob) |
| `GET` | `/api/v1/etmf/export-jobs/{job_id}/download` | DownloadEtmfExportJob | Downloads the completed study binder ZIP archive for an eTMF export job. | [Details](#op-downloadetmfexportjob) |
| `POST` | `/api/v1/etmf/ingest` | IngestEtmfDocument | Ingests a new draft eTMF document with SHA-256 checksum and revision lineage. | [Details](#op-ingestetmfdocument) |
| `POST` | `/api/v1/etmf/intelligence/classify` | ClassifyEtmfDocument | Classifies a clinical document against the DIA TMF Reference Model v3.2 taxonomy. | [Details](#op-classifyetmfdocument) |
| `GET` | `/api/v1/etmf/intelligence/qc-queue` | ListEtmfQcQueue | Lists trial documents currently staged in TECHNICAL_QC for CRA review. | [Details](#op-listetmfqcqueue) |
| `POST` | `/api/v1/etmf/intelligence/qc-review/{doc_id}` | ReviewEtmfQcDocument | Executes a CRA Technical QC decision (ACCEPT, OVERRIDE, or REJECT) on a staged document. | [Details](#op-reviewetmfqcdocument) |
| `POST` | `/api/v1/etmf/intelligence/stage-qc` | StageEtmfDocumentForQc | Uploads, classifies, and stages a trial document into the CRA Technical QC queue. | [Details](#op-stageetmfdocumentforqc) |
| `GET` | `/api/v1/etmf/status` | EtmfStatus | Returns eTMF operational status. | [Details](#op-etmfstatus) |
| `GET` | `/api/v1/etmf/taxonomy` | GetEtmfTaxonomy | Returns the supported DIA TMF Reference Model v3.2 zone, section, and artifact catalog. | [Details](#op-getetmftaxonomy) |
| `GET` | `/healthz` | EtmfHealthCheck | eTMF operations health probe. | [Details](#op-etmfhealthcheck) |

### `GET` `/api/v1/etmf/documents` {#op-listetmfdocuments}

**Operation ID:** `ListEtmfDocuments` — Lists eTMF documents filtered by study, site, or DIA TMF zone.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `etmf_document:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eTMF Operations & Intelligence`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `site_id` | `query` | `optional` | `string` |  |
| `study_id` | `query` | `optional` | `string` |  |
| `zone` | `query` | `optional` | `integer` (`int32`) |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Etmf_TmfDocument`](#schema-etmf-tmfdocument) |
| `403` | Forbidden | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |

</details>

### `POST` `/api/v1/etmf/documents/export` {#op-exportetmfdocuments}

**Operation ID:** `ExportEtmfDocuments` — Streams a synchronous study binder ZIP archive or enqueues an asynchronous bulk export job.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `etmf_document:export`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eTMF Operations & Intelligence`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Etmf_ExportDocumentsRequest`](#schema-etmf-exportdocumentsrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/zip` | `string` (`binary`) |
| `202` | Accepted | `application/json` | [`Etmf_EtmfExportJobAcceptedResponse`](#schema-etmf-etmfexportjobacceptedresponse) |
| `400` | Bad Request | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |

</details>

### `GET` `/api/v1/etmf/documents/{document_id}` {#op-getetmfdocumentbyid}

**Operation ID:** `GetEtmfDocumentById` — Retrieves an eTMF document metadata record by document ID.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `etmf_document:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eTMF Operations & Intelligence`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `document_id` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Etmf_TmfDocument`](#schema-etmf-tmfdocument) |
| `404` | Not Found | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |

</details>

### `GET` `/api/v1/etmf/documents/{document_id}/content` {#op-downloadetmfdocumentcontent}

**Operation ID:** `DownloadEtmfDocumentContent` — Streams SHA-256 integrity-verified binary content for an eTMF document.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `etmf_document:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eTMF Operations & Intelligence`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `document_id` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/pdf` | `string` (`binary`) |
| `403` | Forbidden | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |
| `409` | Conflict | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |

</details>

### `POST` `/api/v1/etmf/documents/{document_id}/submit-qc` {#op-submitetmfdocumentforqc}

**Operation ID:** `SubmitEtmfDocumentForQc` — Submits a DRAFT eTMF document into TECHNICAL_QC for classification review.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `etmf_document:create`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eTMF Operations & Intelligence`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `document_id` | `path` | `required` | `string` |  |
| `X-Change-Reason` | `header` | `optional` | `string` | Optional 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Etmf_SubmitDocumentForQcRequest`](#schema-etmf-submitdocumentforqcrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Etmf_TmfDocument`](#schema-etmf-tmfdocument) |
| `400` | Bad Request | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |

</details>

### `PATCH` `/api/v1/etmf/documents/{document_id}/taxonomy` {#op-updateetmfdocumenttaxonomy}

**Operation ID:** `UpdateEtmfDocumentTaxonomy` — Updates the DIA TMF zone, section, and artifact classification of an eTMF document.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `etmf_document:tag`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eTMF Operations & Intelligence`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `document_id` | `path` | `required` | `string` |  |
| `X-Change-Reason` | `header` | `optional` | `string` | Optional 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Etmf_UpdateDocumentTaxonomyRequest`](#schema-etmf-updatedocumenttaxonomyrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Etmf_TmfDocument`](#schema-etmf-tmfdocument) |
| `400` | Bad Request | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |

</details>

### `GET` `/api/v1/etmf/export-jobs` {#op-listetmfexportjobs}

**Operation ID:** `ListEtmfExportJobs` — Lists eTMF study binder export jobs visible to the caller.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `etmf_document:export`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eTMF Operations & Intelligence`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `study_id` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Etmf_EtmfExportJobStatusDto`](#schema-etmf-etmfexportjobstatusdto) |
| `403` | Forbidden | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |

</details>

### `GET` `/api/v1/etmf/export-jobs/{job_id}` {#op-getetmfexportjob}

**Operation ID:** `GetEtmfExportJob` — Retrieves progress and download metadata for a background eTMF binder export job.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `etmf_document:export`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eTMF Operations & Intelligence`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `job_id` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Etmf_EtmfExportJobStatusDto`](#schema-etmf-etmfexportjobstatusdto) |
| `404` | Not Found | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |

</details>

### `GET` `/api/v1/etmf/export-jobs/{job_id}/download` {#op-downloadetmfexportjob}

**Operation ID:** `DownloadEtmfExportJob` — Downloads the completed study binder ZIP archive for an eTMF export job.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `etmf_document:export`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eTMF Operations & Intelligence`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `job_id` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/zip` | `string` (`binary`) |
| `400` | Bad Request | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |
| `410` | Gone | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |

</details>

### `POST` `/api/v1/etmf/ingest` {#op-ingestetmfdocument}

**Operation ID:** `IngestEtmfDocument` — Ingests a new draft eTMF document with SHA-256 checksum and revision lineage.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `etmf_document:create`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eTMF Operations & Intelligence`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Change-Reason` | `header` | `optional` | `string` | Optional 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Etmf_IngestDocumentRequest`](#schema-etmf-ingestdocumentrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Etmf_TmfDocument`](#schema-etmf-tmfdocument) |
| `400` | Bad Request | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |

</details>

### `POST` `/api/v1/etmf/intelligence/classify` {#op-classifyetmfdocument}

**Operation ID:** `ClassifyEtmfDocument` — Classifies a clinical document against the DIA TMF Reference Model v3.2 taxonomy.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `etmf_document:tag`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `eTMF Operations & Intelligence`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Etmf_ClassifyRequest`](#schema-etmf-classifyrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Etmf_DocumentIntelligenceReport`](#schema-etmf-documentintelligencereport) |
| `403` | Forbidden | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |

</details>

### `GET` `/api/v1/etmf/intelligence/qc-queue` {#op-listetmfqcqueue}

**Operation ID:** `ListEtmfQcQueue` — Lists trial documents currently staged in TECHNICAL_QC for CRA review.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `etmf_document:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eTMF Operations & Intelligence`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `site_id` | `query` | `optional` | `string` |  |
| `study_id` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Etmf_CraqcStagingItem`](#schema-etmf-craqcstagingitem) |
| `403` | Forbidden | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |

</details>

### `POST` `/api/v1/etmf/intelligence/qc-review/{doc_id}` {#op-reviewetmfqcdocument}

**Operation ID:** `ReviewEtmfQcDocument` — Executes a CRA Technical QC decision (ACCEPT, OVERRIDE, or REJECT) on a staged document.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `etmf_document:transition_clinical_qc`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eTMF Operations & Intelligence`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `doc_id` | `path` | `required` | `string` |  |
| `X-Change-Reason` | `header` | `optional` | `string` | Optional 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Etmf_CraqcReviewRequest`](#schema-etmf-craqcreviewrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Etmf_TmfDocument`](#schema-etmf-tmfdocument) |
| `400` | Bad Request | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |

</details>

### `POST` `/api/v1/etmf/intelligence/stage-qc` {#op-stageetmfdocumentforqc}

**Operation ID:** `StageEtmfDocumentForQc` — Uploads, classifies, and stages a trial document into the CRA Technical QC queue.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `etmf_document:create`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eTMF Operations & Intelligence`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Change-Reason` | `header` | `optional` | `string` | Optional 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Etmf_StageDocumentQCRequest`](#schema-etmf-stagedocumentqcrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Etmf_CraqcStagingItem`](#schema-etmf-craqcstagingitem) |
| `400` | Bad Request | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |

</details>

### `GET` `/api/v1/etmf/status` {#op-etmfstatus}

**Operation ID:** `EtmfStatus` — Returns eTMF operational status.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Etmf`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Etmf_AnonymousTypeOfstringAndstring`](#schema-etmf-anonymoustypeofstringandstring) |

</details>

### `GET` `/api/v1/etmf/taxonomy` {#op-getetmftaxonomy}

**Operation ID:** `GetEtmfTaxonomy` — Returns the supported DIA TMF Reference Model v3.2 zone, section, and artifact catalog.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `etmf_taxonomy:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `eTMF Operations & Intelligence`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `version` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Etmf_EtmfTaxonomyEntryDto`](#schema-etmf-etmftaxonomyentrydto) |
| `403` | Forbidden | `application/problem+json` | [`Etmf_ProblemDetails`](#schema-etmf-problemdetails) |

</details>

### `GET` `/healthz` {#op-etmfhealthcheck}

**Operation ID:** `EtmfHealthCheck` — eTMF operations health probe.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Etmf`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Etmf_AnonymousTypeOfstringAndstring`](#schema-etmf-anonymoustypeofstringandstring) |

</details>

## Schemas

Deduplicated component schemas exported in `components.schemas` (18 total). Operation payloads and responses link here so nested models are defined once per page.

### `Etmf_AnonymousTypeOfstringAndstring` {#schema-etmf-anonymoustypeofstringandstring}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `service` | `string` \| `null` | `required` |  |
| `status` | `string` \| `null` | `required` |  |

</details>

### `Etmf_ClassifyRequest` {#schema-etmf-classifyrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `content` | `string` \| `null` | `optional` |  |
| `contentHint` | `string` \| `null` | `optional` |  |
| `filename` | `string` | `required` |  |
| `mimeType` | `string` \| `null` | `optional` |  |
| `taxonomyVersion` | `string` \| `null` | `optional` |  |

</details>

### `Etmf_CraqcReviewRequest` {#schema-etmf-craqcreviewrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `decision` | `string` | `required` |  |
| `discrepancyComment` | `string` \| `null` | `optional` |  |
| `overrideArtifactCode` | `string` \| `null` | `optional` |  |
| `reasonForChange` | `string` | `required` |  |

</details>

### `Etmf_CraqcStagingItem` {#schema-etmf-craqcstagingitem}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (10)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `discrepancyNotes` | array of `string` | `required` |  |
| `documentId` | `string` | `required` |  |
| `filename` | `string` | `required` |  |
| `intelligenceReport` | [`Etmf_DocumentIntelligenceReport`](#schema-etmf-documentintelligencereport) | `required` |  |
| `qcAssignedTo` | `string` \| `null` | `required` |  |
| `siteId` | `string` \| `null` | `required` |  |
| `stagedAt` | `string` (`date-time`) | `required` |  |
| `stagedBy` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `studyId` | `string` | `required` |  |

</details>

### `Etmf_DocumentIntelligenceReport` {#schema-etmf-documentintelligencereport}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (10)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `aiGenerationManifest` | `object` | `required` |  |
| `alternativeCandidates` | array of [`Etmf_TaxonomyMatchCandidate`](#schema-etmf-taxonomymatchcandidate) | `required` |  |
| `confidenceTier` | `string` | `required` |  |
| `documentId` | `string` \| `null` | `required` |  |
| `filename` | `string` | `required` |  |
| `mimeType` | `string` | `required` |  |
| `modality` | `string` | `required` |  |
| `primaryClassification` | [`Etmf_TaxonomyMatchCandidate`](#schema-etmf-taxonomymatchcandidate) | `required` |  |
| `qcRecommendation` | `string` | `required` |  |
| `sha256Hash` | `string` | `required` |  |

</details>

### `Etmf_EtmfExportJobAcceptedResponse` {#schema-etmf-etmfexportjobacceptedresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (12)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `created_at` | `string` (`date-time`) | `required` |  |
| `download_url` | `string` \| `null` | `required` |  |
| `expires_at` | `string` (`date-time`) \| `null` | `required` |  |
| `job_id` | `string` | `required` |  |
| `message` | `string` | `required` |  |
| `processed_documents` | `integer` (`int32`) | `required` |  |
| `progress_percentage` | `number` (`double`) | `required` |  |
| `requested_by` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `status_url` | `string` | `required` |  |
| `study_id` | [`Etmf_StudyId`](#schema-etmf-studyid) | `required` |  |
| `total_documents` | `integer` (`int32`) | `required` |  |

</details>

### `Etmf_EtmfExportJobStatusDto` {#schema-etmf-etmfexportjobstatusdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (10)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `created_at` | `string` (`date-time`) | `required` |  |
| `download_url` | `string` \| `null` | `required` |  |
| `expires_at` | `string` (`date-time`) \| `null` | `required` |  |
| `job_id` | `string` | `required` |  |
| `processed_documents` | `integer` (`int32`) | `required` |  |
| `progress_percentage` | `number` (`double`) | `required` |  |
| `requested_by` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `study_id` | [`Etmf_StudyId`](#schema-etmf-studyid) | `required` |  |
| `total_documents` | `integer` (`int32`) | `required` |  |

</details>

### `Etmf_EtmfTaxonomyEntryDto` {#schema-etmf-etmftaxonomyentrydto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `artifact_code` | `string` | `required` |  |
| `artifact_name` | `string` | `required` |  |
| `section` | `string` | `required` |  |
| `section_name` | `string` | `required` |  |
| `zone` | `integer` (`int32`) | `required` |  |
| `zone_name` | `string` | `required` |  |

</details>

### `Etmf_ExportDocumentsRequest` {#schema-etmf-exportdocumentsrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `artifact_code` | `string` \| `null` | `optional` |  |
| `document_ids` | array of `string` \| `null` | `optional` |  |
| `is_full_binder` | `boolean` | `optional` |  |
| `section` | `string` \| `null` | `optional` |  |
| `study_id` | `string` \| `null` | `optional` |  |
| `zone` | `integer` (`int32`) \| `null` | `optional` |  |

</details>

### `Etmf_IngestDocumentRequest` {#schema-etmf-ingestdocumentrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (12)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `artifactCode` | `string` \| `null` | `required` |  |
| `artifactType` | `string` \| `null` | `required` |  |
| `content` | `string` \| `null` | `required` |  |
| `contentBase64` | `string` \| `null` | `optional` |  |
| `filename` | `string` | `required` |  |
| `mimeType` | `string` \| `null` | `required` |  |
| `reasonForChange` | `string` \| `null` | `required` |  |
| `section` | `string` \| `null` | `required` |  |
| `siteId` | `string` \| `null` | `required` |  |
| `studyId` | `string` \| `null` | `required` |  |
| `taxonomyVersion` | `string` \| `null` | `required` |  |
| `zone` | `integer` (`int32`) | `required` |  |

</details>

### `Etmf_ProblemDetails` {#schema-etmf-problemdetails}

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

### `Etmf_StageDocumentQCRequest` {#schema-etmf-stagedocumentqcrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (10)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `artifactHint` | `string` \| `null` | `optional` |  |
| `assignedCra` | `string` \| `null` | `optional` |  |
| `content` | `string` \| `null` | `required` |  |
| `contentBase64` | `string` \| `null` | `optional` |  |
| `filename` | `string` | `required` |  |
| `mimeType` | `string` \| `null` | `optional` |  |
| `reasonForChange` | `string` | `optional` |  |
| `siteId` | `string` \| `null` | `optional` |  |
| `studyId` | `string` | `required` |  |
| `taxonomyVersion` | `string` \| `null` | `optional` |  |

</details>

### `Etmf_StudyId` {#schema-etmf-studyid}

- **Type:** any

### `Etmf_SubmitDocumentForQcRequest` {#schema-etmf-submitdocumentforqcrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `assignedCra` | `string` \| `null` | `optional` |  |
| `reasonForChange` | `string` \| `null` | `optional` |  |

</details>

### `Etmf_TaxonomyMatchCandidate` {#schema-etmf-taxonomymatchcandidate}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `artifactCode` | `string` | `required` |  |
| `artifactType` | `string` | `required` |  |
| `confidence` | `number` (`double`) | `required` |  |
| `section` | `string` | `required` |  |
| `zone` | `integer` (`int32`) | `required` |  |

</details>

### `Etmf_TmfDocument` {#schema-etmf-tmfdocument}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (39)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `approvalStatus` | `string` | `optional` |  |
| `artifactCode` | `string` | `optional` |  |
| `artifactType` | `string` | `optional` |  |
| `content` | `string` | `optional` |  |
| `contentChecksum` | `string` \| `null` | `optional` |  |
| `correlationKey` | `string` \| `null` | `optional` |  |
| `createdAt` | `string` (`date-time`) | `optional` |  |
| `createdBy` | `string` | `optional` |  |
| `documentOwnerId` | `string` \| `null` | `optional` |  |
| `documentType` | `string` \| `null` | `optional` |  |
| `expirationDate` | `string` (`date-time`) \| `null` | `optional` |  |
| `filename` | `string` | `optional` |  |
| `id` | `string` \| `null` | `optional` |  |
| `idempotencyKey` | `string` \| `null` | `optional` |  |
| `isRedacted` | `boolean` | `optional` |  |
| `issueDate` | `string` (`date-time`) \| `null` | `optional` |  |
| `metadataJson` | `string` \| `null` | `optional` |  |
| `mimeType` | `string` | `optional` |  |
| `objectKey` | `string` \| `null` | `optional` |  |
| `protocolVersionIndex` | `integer` (`int32`) \| `null` | `optional` |  |
| `protocolVersionStatus` | `string` \| `null` | `optional` |  |
| `protocolVersionTag` | `string` \| `null` | `optional` |  |
| `reasonForChange` | `string` | `optional` |  |
| `redactionManifestJson` | `string` \| `null` | `optional` |  |
| `redactionSourceId` | `string` \| `null` | `optional` |  |
| `section` | `string` | `optional` |  |
| `signatureManifestation` | `string` \| `null` | `optional` |  |
| `signer` | `string` \| `null` | `optional` |  |
| `signingTimestamp` | `string` (`date-time`) \| `null` | `optional` |  |
| `siteId` | `string` \| `null` | `optional` |  |
| `sourceSystem` | `string` \| `null` | `optional` |  |
| `status` | `string` | `optional` |  |
| `studyId` | `string` | `optional` |  |
| `syncStatus` | `string` \| `null` | `optional` |  |
| `taxonomyVersion` | `string` | `optional` |  |
| `transitionSequence` | `integer` (`int32`) | `optional` |  |
| `transitions` | array of [`Etmf_TmfDocumentQcTransition`](#schema-etmf-tmfdocumentqctransition) \| `null` | `optional` |  |
| `versionIndex` | `integer` (`int64`) | `optional` |  |
| `zone` | `integer` (`int32`) | `optional` |  |

</details>

### `Etmf_TmfDocumentQcTransition` {#schema-etmf-tmfdocumentqctransition}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (9)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `actorId` | `string` | `optional` |  |
| `actorRole` | `string` | `optional` |  |
| `documentId` | `string` | `optional` |  |
| `fromStatus` | `string` | `optional` |  |
| `id` | `string` | `optional` |  |
| `reasonForChange` | `string` | `optional` |  |
| `timestamp` | `string` (`date-time`) | `required` |  |
| `toStatus` | `string` | `optional` |  |
| `transitionSequence` | `integer` (`int32`) | `optional` |  |

</details>

### `Etmf_UpdateDocumentTaxonomyRequest` {#schema-etmf-updatedocumenttaxonomyrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `artifactCode` | `string` \| `null` | `optional` |  |
| `artifactType` | `string` \| `null` | `optional` |  |
| `reasonForChange` | `string` \| `null` | `optional` |  |
| `section` | `string` \| `null` | `optional` |  |
| `zone` | `integer` (`int32`) \| `null` | `optional` |  |

</details>

