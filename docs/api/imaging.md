# imaging API

Generated from `docs/openapi/imaging_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

- **Service Maturity:** <span class="VPBadge tip">Active</span> (Port `8014` · Gateway prefixes: `/api/v1/imaging`)
- **OpenAPI 3.1 Contract:** [`imaging_openapi.json`](../openapi/imaging_openapi.json) (11 operation(s), 17 schema(s))

> **HTTP 501 Not Implemented Stub(s):** `POST /api/v1/imaging/studies/{studyUid}/series/{seriesUid}/segment` (`SegmentRoiSeed`) intentionally answers `501 Not Implemented` with an RFC 7807 `ProblemDetails` payload until its external integration backend is wired.

## Operations

| Method | Path | Operation | Summary | Contract |
| ------ | ---- | --------- | ------- | -------- |
| `POST` | `/api/v1/imaging/adjudications` | SubmitOrEvaluateAdjudicationSession | Submits dual-reader RECIST assessments and evaluates discordance for adjudication. | [Details](#op-submitorevaluateadjudicationsession) |
| `GET` | `/api/v1/imaging/adjudications/{sessionId}` | GetImagingAdjudicationSession | Retrieves an imaging adjudication session and reader assessments by session ID. | [Details](#op-getimagingadjudicationsession) |
| `POST` | `/api/v1/imaging/adjudications/{sessionId}/chair-adjudicate` | ChairAdjudicateImagingSession | Records the adjudication chair's binding consensus response and rationale for a discordant session. | [Details](#op-chairadjudicateimagingsession) |
| `GET` | `/api/v1/imaging/certificates/{certificateId}` | GetDeidCertificate | Retrieves a DICOM PS3.15 de-identification audit certificate by certificate ID. | [Details](#op-getdeidcertificate) |
| `GET` | `/api/v1/imaging/status` | ImagingStatus | Returns Medical Imaging operational status. | [Details](#op-imagingstatus) |
| `POST` | `/api/v1/imaging/studies` | StowRsStoreInstances | Ingests a DICOM instance via STOW-RS with PS3.15 de-identification and SHA-256 certificate generation. | [Details](#op-stowrsstoreinstances) |
| `POST` | `/api/v1/imaging/studies/batch` | BatchIngestStudyFolder | Ingests a multi-file DICOM study folder in batch with PS3.15 de-identification. | [Details](#op-batchingeststudyfolder) |
| `GET` | `/api/v1/imaging/studies/{studyUid}` | WadoRsGetStudy | Retrieves WADO-RS study, series, and de-identified instance metadata by Study Instance UID. | [Details](#op-wadorsgetstudy) |
| `GET` | `/api/v1/imaging/studies/{studyUid}/series/{seriesUid}/instances/{instanceUid}/frames/{frameList}` | WadoRsGetInstanceFrames | Streams WADO-RS binary pixel frame bytes for a de-identified DICOM instance. | [Details](#op-wadorsgetinstanceframes) |
| `POST` | `/api/v1/imaging/studies/{studyUid}/series/{seriesUid}/segment` | SegmentRoiSeed | Requests automatic ROI lesion segmentation for a DICOM series (returns 501 Not Implemented until a segmentation model is wired). | [Details](#op-segmentroiseed) · `501 Stub` |
| `GET` | `/healthz` | ImagingHealthCheck | Medical Imaging DICOM service health probe. | [Details](#op-imaginghealthcheck) |

### `POST` `/api/v1/imaging/adjudications` {#op-submitorevaluateadjudicationsession}

**Operation ID:** `SubmitOrEvaluateAdjudicationSession` — Submits dual-reader RECIST assessments and evaluates discordance for adjudication.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `imaging_adjudication:submit`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `Imaging Adjudication`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Imaging_SubmitAdjudicationSessionRequest`](#schema-imaging-submitadjudicationsessionrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Imaging_AdjudicationSessionResponse`](#schema-imaging-adjudicationsessionresponse) |
| `400` | Bad Request | `application/problem+json` | [`Imaging_ProblemDetails`](#schema-imaging-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Imaging_ProblemDetails`](#schema-imaging-problemdetails) |

</details>

### `GET` `/api/v1/imaging/adjudications/{sessionId}` {#op-getimagingadjudicationsession}

**Operation ID:** `GetImagingAdjudicationSession` — Retrieves an imaging adjudication session and reader assessments by session ID.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `imaging_adjudication:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `Imaging Adjudication`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `sessionId` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Imaging_ImagingAdjudicationSession`](#schema-imaging-imagingadjudicationsession) |
| `403` | Forbidden | `application/problem+json` | [`Imaging_ProblemDetails`](#schema-imaging-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Imaging_ProblemDetails`](#schema-imaging-problemdetails) |

</details>

### `POST` `/api/v1/imaging/adjudications/{sessionId}/chair-adjudicate` {#op-chairadjudicateimagingsession}

**Operation ID:** `ChairAdjudicateImagingSession` — Records the adjudication chair's binding consensus response and rationale for a discordant session.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `imaging_adjudication:adjudicate`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `Imaging Adjudication`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `sessionId` | `path` | `required` | `string` |  |
| `X-Sig-Token` | `header` | `required` | `string` (minLen: 1) | Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Imaging_ChairAdjudicateImagingRequest`](#schema-imaging-chairadjudicateimagingrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Imaging_AdjudicationSessionResponse`](#schema-imaging-adjudicationsessionresponse) |
| `400` | Bad Request | `application/problem+json` | [`Imaging_ProblemDetails`](#schema-imaging-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Imaging_ProblemDetails`](#schema-imaging-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Imaging_ProblemDetails`](#schema-imaging-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Imaging_ProblemDetails`](#schema-imaging-problemdetails) |
| `409` | Conflict | `application/problem+json` | [`Imaging_ProblemDetails`](#schema-imaging-problemdetails) |

</details>

### `GET` `/api/v1/imaging/certificates/{certificateId}` {#op-getdeidcertificate}

**Operation ID:** `GetDeidCertificate` — Retrieves a DICOM PS3.15 de-identification audit certificate by certificate ID.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `imaging_study:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `DICOMweb Integration`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `certificateId` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Imaging_CertificateResponse`](#schema-imaging-certificateresponse) |
| `403` | Forbidden | `application/problem+json` | [`Imaging_ProblemDetails`](#schema-imaging-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Imaging_ProblemDetails`](#schema-imaging-problemdetails) |

</details>

### `GET` `/api/v1/imaging/status` {#op-imagingstatus}

**Operation ID:** `ImagingStatus` — Returns Medical Imaging operational status.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Imaging`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Imaging_AnonymousTypeOfstringAndstring`](#schema-imaging-anonymoustypeofstringandstring) |

</details>

### `POST` `/api/v1/imaging/studies` {#op-stowrsstoreinstances}

**Operation ID:** `StowRsStoreInstances` — Ingests a DICOM instance via STOW-RS with PS3.15 de-identification and SHA-256 certificate generation.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `imaging_study:upload`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `DICOMweb Integration`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Imaging_IngestionResult`](#schema-imaging-ingestionresult) |
| `400` | Bad Request | `application/problem+json` | [`Imaging_ProblemDetails`](#schema-imaging-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Imaging_ProblemDetails`](#schema-imaging-problemdetails) |
| `413` | Payload Too Large | `application/problem+json` | [`Imaging_ProblemDetails`](#schema-imaging-problemdetails) |

</details>

### `POST` `/api/v1/imaging/studies/batch` {#op-batchingeststudyfolder}

**Operation ID:** `BatchIngestStudyFolder` — Ingests a multi-file DICOM study folder in batch with PS3.15 de-identification.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `imaging_study:upload`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `DICOMweb Integration`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Imaging_BatchIngestionResult`](#schema-imaging-batchingestionresult) |
| `400` | Bad Request | `application/problem+json` | [`Imaging_ProblemDetails`](#schema-imaging-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Imaging_ProblemDetails`](#schema-imaging-problemdetails) |
| `413` | Payload Too Large | `application/problem+json` | [`Imaging_ProblemDetails`](#schema-imaging-problemdetails) |

</details>

### `GET` `/api/v1/imaging/studies/{studyUid}` {#op-wadorsgetstudy}

**Operation ID:** `WadoRsGetStudy` — Retrieves WADO-RS study, series, and de-identified instance metadata by Study Instance UID.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `imaging_study:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `DICOMweb Integration`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `studyUid` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Imaging_ImagingStudyEnvelope`](#schema-imaging-imagingstudyenvelope) |
| `403` | Forbidden | `application/problem+json` | [`Imaging_ProblemDetails`](#schema-imaging-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Imaging_ProblemDetails`](#schema-imaging-problemdetails) |

</details>

### `GET` `/api/v1/imaging/studies/{studyUid}/series/{seriesUid}/instances/{instanceUid}/frames/{frameList}` {#op-wadorsgetinstanceframes}

**Operation ID:** `WadoRsGetInstanceFrames` — Streams WADO-RS binary pixel frame bytes for a de-identified DICOM instance.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `imaging_study:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `DICOMweb Integration`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `frameList` | `path` | `required` | `string` |  |
| `instanceUid` | `path` | `required` | `string` |  |
| `seriesUid` | `path` | `required` | `string` |  |
| `studyUid` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/octet-stream` | `string` (`binary`) |
| `403` | Forbidden | `application/problem+json` | [`Imaging_ProblemDetails`](#schema-imaging-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Imaging_ProblemDetails`](#schema-imaging-problemdetails) |
| `422` | Unprocessable Entity | `application/problem+json` | [`Imaging_ProblemDetails`](#schema-imaging-problemdetails) |

</details>

### `POST` `/api/v1/imaging/studies/{studyUid}/series/{seriesUid}/segment` <span class="VPBadge warning">501 Not Implemented</span> {#op-segmentroiseed}

**Operation ID:** `SegmentRoiSeed` — Requests automatic ROI lesion segmentation for a DICOM series (returns 501 Not Implemented until a segmentation model is wired).

HTTP 501 Not Implemented stub: no automatic segmentation model is wired, so this route validates study/site scope and returns RFC 7807 ProblemDetails (501) without storing synthetic measurements.

> **Stub Endpoint (`501 Not Implemented`):** Returns an RFC 7807 `ProblemDetails` response indicating the external adapter or inference worker is not wired in this build.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `imaging_adjudication:submit`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `Imaging Segmentation`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `seriesUid` | `path` | `required` | `string` |  |
| `studyUid` | `path` | `required` | `string` |  |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Imaging_SegmentRoiRequest`](#schema-imaging-segmentroirequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `400` | Bad Request | `application/problem+json` | [`Imaging_ProblemDetails`](#schema-imaging-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Imaging_ProblemDetails`](#schema-imaging-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Imaging_ProblemDetails`](#schema-imaging-problemdetails) |
| `501` (`Stub`) | Not Implemented | `application/problem+json` | [`Imaging_ProblemDetails`](#schema-imaging-problemdetails) |

</details>

### `GET` `/healthz` {#op-imaginghealthcheck}

**Operation ID:** `ImagingHealthCheck` — Medical Imaging DICOM service health probe.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Imaging`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Imaging_AnonymousTypeOfstringAndstring`](#schema-imaging-anonymoustypeofstringandstring) |

</details>

## Schemas

Deduplicated component schemas exported in `components.schemas` (17 total). Operation payloads and responses link here so nested models are defined once per page.

### `Imaging_AdjudicationSessionResponse` {#schema-imaging-adjudicationsessionresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (13)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `adjudicationRationale` | `string` \| `null` | `required` |  |
| `bindingReader` | `string` \| `null` | `required` |  |
| `discordanceReasons` | array of `string` | `required` |  |
| `finalConsensusResponse` | `string` \| `null` | `required` |  |
| `finalConsensusSldMm` | `number` (`double`) \| `null` | `required` |  |
| `isResponseDiscordant` | `boolean` | `required` |  |
| `sessionDigest` | `string` | `required` |  |
| `sessionId` | `string` | `required` |  |
| `sldDiscordanceDelta` | `number` (`double`) | `required` |  |
| `studyId` | `string` | `required` |  |
| `subjectId` | `string` | `required` |  |
| `timepointName` | `string` | `required` |  |
| `workflowStatus` | `string` | `required` |  |

</details>

### `Imaging_AnonymousTypeOfstringAndstring` {#schema-imaging-anonymoustypeofstringandstring}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `service` | `string` \| `null` | `required` |  |
| `status` | `string` \| `null` | `required` |  |

</details>

### `Imaging_BatchDeidentificationManifest` {#schema-imaging-batchdeidentificationmanifest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (16)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `ingested_slice_count` | `integer` (`int32`) | `optional` |  |
| `manifest_id` | `string` | `optional` |  |
| `operator_identity` | `string` | `optional` |  |
| `pixel_mask_applied` | `boolean` | `optional` |  |
| `pixel_masks` | array of [`Imaging_PixelMaskBoundingBox`](#schema-imaging-pixelmaskboundingbox) | `optional` |  |
| `scrubbed_tags` | array of `string` | `optional` |  |
| `scrubbed_tags_count` | `integer` (`int32`) | `optional` |  |
| `signature` | `string` \| `null` | `optional` |  |
| `site_id` | `string` | `optional` |  |
| `skipped_non_dicom_count` | `integer` (`int32`) | `optional` |  |
| `skipped_non_dicom_files` | array of `string` | `optional` |  |
| `study_envelope_id` | `string` | `optional` |  |
| `study_id` | `string` | `optional` |  |
| `subject_id` | `string` | `optional` |  |
| `timestamp` | `string` | `optional` |  |
| `total_files_processed` | `integer` (`int32`) | `optional` |  |

</details>

### `Imaging_BatchIngestionResult` {#schema-imaging-batchingestionresult}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (10)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `anonymizedStudyUid` | `string` | `required` |  |
| `certificateId` | `string` | `required` |  |
| `ingestedSliceCount` | `integer` (`int32`) | `required` |  |
| `manifest` | [`Imaging_BatchDeidentificationManifest`](#schema-imaging-batchdeidentificationmanifest) | `required` |  |
| `seriesSummary` | array of [`Imaging_SeriesIngestionSummary`](#schema-imaging-seriesingestionsummary) | `required` |  |
| `signature` | `string` | `required` |  |
| `skippedNonDicomCount` | `integer` (`int32`) | `required` |  |
| `skippedNonDicomFiles` | array of `string` | `required` |  |
| `studyEnvelopeId` | `string` | `required` |  |
| `totalFilesReceived` | `integer` (`int32`) | `required` |  |

</details>

### `Imaging_BoundingBoxDto` {#schema-imaging-boundingboxdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `xMax` | `number` (`double`) | `required` |  |
| `xMin` | `number` (`double`) | `required` |  |
| `yMax` | `number` (`double`) | `required` |  |
| `yMin` | `number` (`double`) | `required` |  |

</details>

### `Imaging_CertificateResponse` {#schema-imaging-certificateresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `certificate` | [`Imaging_DeidentificationCertificate`](#schema-imaging-deidentificationcertificate) | `required` |  |
| `isSignatureValid` | `boolean` | `required` |  |

</details>

### `Imaging_ChairAdjudicateImagingRequest` {#schema-imaging-chairadjudicateimagingrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `adjudicationRationale` | `string` | `required` |  |
| `bindingReader` | `string` | `required` |  |
| `finalConsensusResponse` | `string` | `required` |  |
| `finalConsensusSldMm` | `number` (`double`) | `required` |  |
| `signatureMeaning` | `string` | `required` |  |
| `signerName` | `string` | `required` |  |

</details>

### `Imaging_DeidentificationCertificate` {#schema-imaging-deidentificationcertificate}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (19)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `anonymizedSopInstanceUid` | `string` | `optional` |  |
| `anonymizedStudyInstanceUid` | `string` | `optional` |  |
| `createdAt` | `string` (`date-time`) | `required` |  |
| `id` | `string` | `optional` |  |
| `operatorIdentity` | `string` | `optional` |  |
| `originalSopInstanceUidHash` | `string` | `optional` |  |
| `originalStudyInstanceUidHash` | `string` | `optional` |  |
| `pixelMaskApplied` | `boolean` | `optional` |  |
| `pixelMasksJson` | `string` | `optional` |  |
| `scrubbedTagsCount` | `integer` (`int32`) | `optional` |  |
| `scrubbedTagsJson` | `string` | `optional` |  |
| `signature` | `string` | `optional` |  |
| `siteId` | `string` | `optional` |  |
| `sourceSha256` | `string` | `optional` |  |
| `studyEnvelopeId` | `string` \| `null` | `optional` |  |
| `studyId` | `string` | `optional` |  |
| `subjectId` | `string` | `optional` |  |
| `targetSha256` | `string` | `optional` |  |
| `timestamp` | `string` | `optional` |  |

</details>

### `Imaging_ImagingAdjudicationSession` {#schema-imaging-imagingadjudicationsession}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (36)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `adjudicationRationale` | `string` \| `null` | `optional` |  |
| `adjudicatorAssessmentJson` | `string` \| `null` | `optional` |  |
| `adjudicatorId` | `string` \| `null` | `optional` |  |
| `adjudicatorName` | `string` \| `null` | `optional` |  |
| `assessmentCriteria` | `string` | `optional` |  |
| `bindingReader` | `string` \| `null` | `optional` |  |
| `createdAt` | `string` (`date-time`) | `required` |  |
| `createdBy` | `string` | `required` |  |
| `discordanceReasonsJson` | `string` | `optional` |  |
| `electronicSignatureJson` | `string` \| `null` | `optional` |  |
| `finalConsensusResponse` | `string` \| `null` | `optional` |  |
| `finalConsensusSldMm` | `number` (`double`) \| `null` | `optional` |  |
| `id` | `string` | `optional` |  |
| `isResponseDiscordant` | `boolean` | `optional` |  |
| `lockedAt` | `string` (`date-time`) \| `null` | `optional` |  |
| `lockedBy` | `string` \| `null` | `optional` |  |
| `reader1AssessmentJson` | `string` \| `null` | `optional` |  |
| `reader1Id` | `string` | `optional` |  |
| `reader1Name` | `string` | `optional` |  |
| `reader1Status` | `string` | `optional` |  |
| `reader2AssessmentJson` | `string` \| `null` | `optional` |  |
| `reader2Id` | `string` | `optional` |  |
| `reader2Name` | `string` | `optional` |  |
| `reader2Status` | `string` | `optional` |  |
| `reasonForChange` | `string` \| `null` | `optional` |  |
| `sessionDigest` | `string` | `optional` |  |
| `siteId` | `string` | `optional` |  |
| `sldDiscordanceDelta` | `number` (`double`) | `optional` |  |
| `studyEnvelopeId` | `string` \| `null` | `optional` |  |
| `studyId` | `string` | `optional` |  |
| `subjectId` | `string` | `optional` |  |
| `tenantId` | `string` | `optional` |  |
| `timepointName` | `string` | `optional` |  |
| `updatedAt` | `string` (`date-time`) | `required` |  |
| `versionIndex` | `integer` (`int32`) | `optional` |  |
| `workflowStatus` | `string` | `optional` |  |

</details>

### `Imaging_ImagingStudyEnvelope` {#schema-imaging-imagingstudyenvelope}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (19)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `adjudicationStatus` | `string` | `optional` |  |
| `createdAt` | `string` (`date-time`) | `required` |  |
| `createdBy` | `string` | `required` |  |
| `id` | `string` | `optional` |  |
| `instanceCount` | `integer` (`int32`) | `optional` |  |
| `modalitiesJson` | `string` | `optional` |  |
| `reasonForChange` | `string` \| `null` | `optional` |  |
| `seriesCount` | `integer` (`int32`) | `optional` |  |
| `siteId` | `string` | `optional` |  |
| `status` | `string` | `optional` |  |
| `storagePrefix` | `string` \| `null` | `optional` |  |
| `studyDate` | `string` \| `null` | `optional` |  |
| `studyDescription` | `string` \| `null` | `optional` |  |
| `studyId` | `string` | `optional` |  |
| `studyInstanceUid` | `string` | `optional` |  |
| `subjectId` | `string` | `optional` |  |
| `tenantId` | `string` | `optional` |  |
| `updatedAt` | `string` (`date-time`) | `required` |  |
| `versionIndex` | `integer` (`int32`) | `optional` |  |

</details>

### `Imaging_IngestionResult` {#schema-imaging-ingestionresult}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (11)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `anonymizedSeriesUid` | `string` | `required` |  |
| `anonymizedSopUid` | `string` | `required` |  |
| `anonymizedStudyUid` | `string` | `required` |  |
| `bitsAllocated` | `integer` (`int32`) | `required` |  |
| `certificateId` | `string` | `required` |  |
| `columns` | `integer` (`int32`) | `required` |  |
| `modality` | `string` | `required` |  |
| `pixelMaskApplied` | `boolean` | `required` |  |
| `rows` | `integer` (`int32`) | `required` |  |
| `signature` | `string` | `required` |  |
| `studyEnvelopeId` | `string` | `required` |  |

</details>

### `Imaging_PixelMaskBoundingBox` {#schema-imaging-pixelmaskboundingbox}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `x_max` | `integer` (`int32`) | `required` |  |
| `x_min` | `integer` (`int32`) | `required` |  |
| `y_max` | `integer` (`int32`) | `required` |  |
| `y_min` | `integer` (`int32`) | `required` |  |

</details>

### `Imaging_ProblemDetails` {#schema-imaging-problemdetails}

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

### `Imaging_ReaderAssessmentDto` {#schema-imaging-readerassessmentdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `newLesionsPresent` | `boolean` | `required` |  |
| `notes` | array of `string` \| `null` | `optional` |  |
| `overallResponse` | `string` | `required` |  |
| `readerId` | `string` | `required` |  |
| `readerName` | `string` | `required` |  |
| `sumLongestDiametersMm` | `number` (`double`) | `required` |  |
| `targetResponse` | `string` | `required` |  |

</details>

### `Imaging_SegmentRoiRequest` {#schema-imaging-segmentroirequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (9)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `boundingBox` | [`Imaging_BoundingBoxDto`](#schema-imaging-boundingboxdto) \| `null` | `optional` |  |
| `lesionId` | `string` \| `null` | `optional` |  |
| `lesionType` | `string` \| `null` | `optional` |  |
| `organSite` | `string` \| `null` | `optional` |  |
| `plane` | `string` \| `null` | `optional` |  |
| `seedX` | `number` (`double`) | `required` |  |
| `seedY` | `number` (`double`) | `required` |  |
| `sliceIndex` | `integer` (`int32`) \| `null` | `optional` |  |
| `timepointName` | `string` \| `null` | `optional` |  |

</details>

### `Imaging_SeriesIngestionSummary` {#schema-imaging-seriesingestionsummary}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `anonymizedSeriesUid` | `string` | `required` |  |
| `instanceCount` | `integer` (`int32`) | `required` |  |
| `modality` | `string` | `required` |  |
| `seriesEnvelopeId` | `string` | `required` |  |

</details>

### `Imaging_SubmitAdjudicationSessionRequest` {#schema-imaging-submitadjudicationsessionrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `assessmentCriteria` | `string` | `optional` |  |
| `reader1Assessment` | [`Imaging_ReaderAssessmentDto`](#schema-imaging-readerassessmentdto) | `required` |  |
| `reader2Assessment` | [`Imaging_ReaderAssessmentDto`](#schema-imaging-readerassessmentdto) | `required` |  |
| `siteId` | `string` | `required` |  |
| `studyId` | `string` | `required` |  |
| `subjectId` | `string` | `required` |  |
| `timepointName` | `string` | `required` |  |

</details>

