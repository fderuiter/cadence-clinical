# safety API

Generated from `docs/openapi/safety_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

- **Service Maturity:** <span class="VPBadge tip">Active</span> (Port `8008` · Gateway prefixes: `/api/v1/safety`)
- **OpenAPI 3.1 Contract:** [`safety_openapi.json`](../openapi/safety_openapi.json) (13 operation(s), 20 schema(s))

## Operations

| Method | Path | Operation | Summary | Contract |
| ------ | ---- | --------- | ------- | -------- |
| `GET` | `/api/v1/safety/dsur` | ListDsurReports | Lists all DSUR periodic safety report records. | [Details](#op-listdsurreports) |
| `POST` | `/api/v1/safety/dsur/compile` | CompileDsurReport | Compiles an aggregate DSUR periodic safety report with patient-years exposure and MedDRA cross-tabulation. | [Details](#op-compiledsurreport) |
| `GET` | `/api/v1/safety/dsur/{id}` | GetDsurReportById | Retrieves a DSUR periodic safety report record by identifier. | [Details](#op-getdsurreportbyid) |
| `PUT` | `/api/v1/safety/dsur/{id}` | UpdateDsurReport | Updates a draft DSUR periodic safety report record. Returns 409 Conflict if LOCKED. | [Details](#op-updatedsurreport) |
| `POST` | `/api/v1/safety/dsur/{id}/approve` | ApproveDsurReport | Signs and locks a DSUR as the verified caller: needs safety_report:approve, a step-up signature token bound to the report and a reason for change; the compiler cannot approve. | [Details](#op-approvedsurreport) |
| `POST` | `/api/v1/safety/icsr/render` | RenderIcsrReport | Renders an ICH E2B(R3) ICSR as an HL7 v3 MCCI_IN200100UV01 message validated against the ICH schema; a report that breaks an E2B(R3) conformance rule is refused with 400 naming each member. | [Details](#op-rendericsrreport) |
| `GET` | `/api/v1/safety/periodic-reports` | ListPeriodicReports | Lists periodic safety report records within the caller's study scope. | [Details](#op-listperiodicreports) |
| `POST` | `/api/v1/safety/periodic-reports/compile` | CompilePeriodicReport | Compiles an aggregate periodic safety report with patient-years exposure and MedDRA cross-tabulation. | [Details](#op-compileperiodicreport) |
| `GET` | `/api/v1/safety/periodic-reports/{id}` | GetPeriodicReportById | Retrieves a periodic safety report record by identifier. | [Details](#op-getperiodicreportbyid) |
| `PUT` | `/api/v1/safety/periodic-reports/{id}` | UpdatePeriodicReport | Updates a draft periodic safety report record; returns 409 Conflict when locked. | [Details](#op-updateperiodicreport) |
| `POST` | `/api/v1/safety/periodic-reports/{id}/approve` | ApprovePeriodicReport | Signs and locks a periodic safety report with a step-up signature token and reason for change. | [Details](#op-approveperiodicreport) |
| `GET` | `/api/v1/safety/status` | SafetyStatus | Returns Safety operational status. | [Details](#op-safetystatus) |
| `GET` | `/healthz` | SafetyHealthCheck | Clinical Safety & Pharmacovigilance health probe. | [Details](#op-safetyhealthcheck) |

### `GET` `/api/v1/safety/dsur` {#op-listdsurreports}

**Operation ID:** `ListDsurReports` — Lists all DSUR periodic safety report records.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `safety_report:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `DsurEndpoints`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Safety_PeriodicSafetyReportRecord`](#schema-safety-periodicsafetyreportrecord) |

</details>

### `POST` `/api/v1/safety/dsur/compile` {#op-compiledsurreport}

**Operation ID:** `CompileDsurReport` — Compiles an aggregate DSUR periodic safety report with patient-years exposure and MedDRA cross-tabulation.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `safety_report:create`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `DsurEndpoints`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Safety_DsurCompileRequest`](#schema-safety-dsurcompilerequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Safety_PeriodicSafetyReportRecord`](#schema-safety-periodicsafetyreportrecord) |
| `400` | Bad Request | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |

</details>

### `GET` `/api/v1/safety/dsur/{id}` {#op-getdsurreportbyid}

**Operation ID:** `GetDsurReportById` — Retrieves a DSUR periodic safety report record by identifier.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `safety_report:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `DsurEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `id` | `path` | `required` | `string` |  |
| `as_of` | `query` | `optional` | `string` (`date-time`) |  |
| `revision` | `query` | `optional` | `integer` (`int32`) |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Safety_PeriodicSafetyReportRecord`](#schema-safety-periodicsafetyreportrecord) |
| `404` | Not Found | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |

</details>

### `PUT` `/api/v1/safety/dsur/{id}` {#op-updatedsurreport}

**Operation ID:** `UpdateDsurReport` — Updates a draft DSUR periodic safety report record. Returns 409 Conflict if LOCKED.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `safety_report:create`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `DsurEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `id` | `path` | `required` | `string` |  |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Safety_DsurUpdateRequest`](#schema-safety-dsurupdaterequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Safety_PeriodicSafetyReportRecord`](#schema-safety-periodicsafetyreportrecord) |
| `400` | Bad Request | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |
| `409` | Conflict | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |

</details>

### `POST` `/api/v1/safety/dsur/{id}/approve` {#op-approvedsurreport}

**Operation ID:** `ApproveDsurReport` — Signs and locks a DSUR as the verified caller: needs safety_report:approve, a step-up signature token bound to the report and a reason for change; the compiler cannot approve.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `safety_report:approve`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `DsurEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `id` | `path` | `required` | `string` |  |
| `X-Sig-Token` | `header` | `required` | `string` (minLen: 1) | Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). |

**Request Body** (`optional`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Safety_DsurApproveRequest`](#schema-safety-dsurapproverequest) \| `null` |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Safety_PeriodicSafetyReportRecord`](#schema-safety-periodicsafetyreportrecord) |
| `400` | Bad Request | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |
| `409` | Conflict | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |

</details>

### `POST` `/api/v1/safety/icsr/render` {#op-rendericsrreport}

**Operation ID:** `RenderIcsrReport` — Renders an ICH E2B(R3) ICSR as an HL7 v3 MCCI_IN200100UV01 message validated against the ICH schema; a report that breaks an E2B(R3) conformance rule is refused with 400 naming each member.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `safety_report:create`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `IcsrTemplateEndpoints`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Safety_IcsrReport`](#schema-safety-icsrreport) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/xml` | `string` |
| `400` | Bad Request | `application/problem+json` | [`Safety_HttpValidationProblemDetails`](#schema-safety-httpvalidationproblemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |
| `500` | Internal Server Error | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |

</details>

### `GET` `/api/v1/safety/periodic-reports` {#op-listperiodicreports}

**Operation ID:** `ListPeriodicReports` — Lists periodic safety report records within the caller's study scope.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `safety_report:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `DsurEndpoints`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Safety_PeriodicSafetyReportRecord`](#schema-safety-periodicsafetyreportrecord) |
| `403` | Forbidden | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |

</details>

### `POST` `/api/v1/safety/periodic-reports/compile` {#op-compileperiodicreport}

**Operation ID:** `CompilePeriodicReport` — Compiles an aggregate periodic safety report with patient-years exposure and MedDRA cross-tabulation.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `safety_report:create`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `DsurEndpoints`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Safety_DsurCompileRequest`](#schema-safety-dsurcompilerequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Safety_PeriodicSafetyReportRecord`](#schema-safety-periodicsafetyreportrecord) |
| `400` | Bad Request | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |

</details>

### `GET` `/api/v1/safety/periodic-reports/{id}` {#op-getperiodicreportbyid}

**Operation ID:** `GetPeriodicReportById` — Retrieves a periodic safety report record by identifier.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `safety_report:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `DsurEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `id` | `path` | `required` | `string` |  |
| `as_of` | `query` | `optional` | `string` (`date-time`) |  |
| `revision` | `query` | `optional` | `integer` (`int32`) |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Safety_PeriodicSafetyReportRecord`](#schema-safety-periodicsafetyreportrecord) |
| `403` | Forbidden | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |

</details>

### `PUT` `/api/v1/safety/periodic-reports/{id}` {#op-updateperiodicreport}

**Operation ID:** `UpdatePeriodicReport` — Updates a draft periodic safety report record; returns 409 Conflict when locked.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `safety_report:create`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `DsurEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `id` | `path` | `required` | `string` |  |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Safety_DsurUpdateRequest`](#schema-safety-dsurupdaterequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Safety_PeriodicSafetyReportRecord`](#schema-safety-periodicsafetyreportrecord) |
| `400` | Bad Request | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |
| `409` | Conflict | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |

</details>

### `POST` `/api/v1/safety/periodic-reports/{id}/approve` {#op-approveperiodicreport}

**Operation ID:** `ApprovePeriodicReport` — Signs and locks a periodic safety report with a step-up signature token and reason for change.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `safety_report:approve`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `DsurEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `id` | `path` | `required` | `string` |  |
| `X-Sig-Token` | `header` | `required` | `string` (minLen: 1) | Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). |

**Request Body** (`optional`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Safety_DsurApproveRequest`](#schema-safety-dsurapproverequest) \| `null` |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Safety_PeriodicSafetyReportRecord`](#schema-safety-periodicsafetyreportrecord) |
| `400` | Bad Request | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |
| `409` | Conflict | `application/problem+json` | [`Safety_ProblemDetails`](#schema-safety-problemdetails) |

</details>

### `GET` `/api/v1/safety/status` {#op-safetystatus}

**Operation ID:** `SafetyStatus` — Returns Safety operational status.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Safety`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Safety_AnonymousTypeOfstringAndstring`](#schema-safety-anonymoustypeofstringandstring) |

</details>

### `GET` `/healthz` {#op-safetyhealthcheck}

**Operation ID:** `SafetyHealthCheck` — Clinical Safety & Pharmacovigilance health probe.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Safety`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Safety_AnonymousTypeOfstringAndstring`](#schema-safety-anonymoustypeofstringandstring) |

</details>

## Schemas

Deduplicated component schemas exported in `components.schemas` (20 total). Operation payloads and responses link here so nested models are defined once per page.

### `Safety_AnonymousTypeOfstringAndstring` {#schema-safety-anonymoustypeofstringandstring}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `service` | `string` \| `null` | `required` |  |
| `status` | `string` \| `null` | `required` |  |

</details>

### `Safety_DsurApproveRequest` {#schema-safety-dsurapproverequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `expectedVersionIndex` | `integer` (`int32`) \| `null` | `optional` |  |
| `reasonForChange` | `string` \| `null` | `required` |  |
| `signingReason` | `string` \| `null` | `required` |  |

</details>

### `Safety_DsurCompileRequest` {#schema-safety-dsurcompilerequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `compoundName` | `string` | `required` |  |
| `periodEnd` | `string` (`date-time`) | `required` |  |
| `periodStart` | `string` (`date-time`) | `required` |  |
| `reasonForChange` | `string` \| `null` | `required` |  |
| `safetyCases` | array of [`Safety_SafetyCaseIcsrInput`](#schema-safety-safetycaseicsrinput) \| `null` | `optional` |  |
| `studyId` | `string` \| `null` | `required` |  |
| `subjectEnrollments` | array of [`Safety_SubjectEnrollmentRecord`](#schema-safety-subjectenrollmentrecord) \| `null` | `optional` |  |

</details>

### `Safety_DsurUpdateRequest` {#schema-safety-dsurupdaterequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `compoundName` | `string` \| `null` | `required` |  |
| `expectedVersionIndex` | `integer` (`int32`) \| `null` | `optional` |  |
| `periodEnd` | `string` (`date-time`) \| `null` | `required` |  |
| `periodStart` | `string` (`date-time`) \| `null` | `required` |  |
| `reasonForChange` | `string` \| `null` | `required` |  |
| `studyId` | `string` \| `null` | `required` |  |

</details>

### `Safety_HttpValidationProblemDetails` {#schema-safety-httpvalidationproblemdetails}

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

### `Safety_IcsrDrug` {#schema-safety-icsrdrug}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `actionTakenWithDrug` | `string` \| `null` | `optional` |  |
| `activeSubstanceName` | `string` \| `null` | `optional` |  |
| `dosageText` | `string` \| `null` | `optional` |  |
| `drugName` | `string` | `required` |  |
| `drugRole` | `string` | `required` |  |
| `routeOfAdministration` | `string` \| `null` | `optional` |  |

</details>

### `Safety_IcsrHeader` {#schema-safety-icsrheader}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `batchNumber` | `string` | `required` |  |
| `batchReceiverIdentifier` | `string` | `required` |  |
| `batchSenderIdentifier` | `string` | `required` |  |
| `batchTransmissionDate` | `string` | `required` |  |
| `messageIdentifier` | `string` | `required` |  |
| `messageReceiverIdentifier` | `string` | `required` |  |
| `messageSenderIdentifier` | `string` | `required` |  |

</details>

### `Safety_IcsrMeddraCoding` {#schema-safety-icsrmeddracoding}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `lltCode` | `string` | `required` |  |
| `version` | `string` | `required` |  |

</details>

### `Safety_IcsrOtherCaseIdentifier` {#schema-safety-icsrothercaseidentifier}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `caseIdentifier` | `string` | `required` |  |
| `source` | `string` | `required` |  |

</details>

### `Safety_IcsrPatient` {#schema-safety-icsrpatient}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `age` | `integer` (`int32`) \| `null` | `optional` |  |
| `ageUnit` | `string` \| `null` | `optional` |  |
| `birthDate` | `string` \| `null` | `optional` |  |
| `patientId` | `string` \| `null` | `optional` |  |
| `sex` | `string` \| `null` | `optional` |  |

</details>

### `Safety_IcsrPrimarySource` {#schema-safety-icsrprimarysource}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `countryCode` | `string` \| `null` | `optional` |  |
| `primaryForRegulatoryPurposes` | `boolean` | `optional` |  |
| `qualification` | `string` | `required` |  |

</details>

### `Safety_IcsrReaction` {#schema-safety-icsrreaction}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (12)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `causedOrProlongedHospitalisation` | `boolean` | `optional` |  |
| `congenitalAnomalyBirthDefect` | `boolean` | `optional` |  |
| `disablingIncapacitating` | `boolean` | `optional` |  |
| `endDate` | `string` \| `null` | `optional` |  |
| `lifeThreatening` | `boolean` | `optional` |  |
| `meddraCoding` | [`Safety_IcsrMeddraCoding`](#schema-safety-icsrmeddracoding) | `required` |  |
| `otherMedicallyImportantCondition` | `boolean` | `optional` |  |
| `outcome` | `string` | `required` |  |
| `reactionTerm` | `string` \| `null` | `optional` |  |
| `reactionTermLanguage` | `string` \| `null` | `optional` |  |
| `resultsInDeath` | `boolean` | `optional` |  |
| `startDate` | `string` \| `null` | `optional` |  |

</details>

### `Safety_IcsrReport` {#schema-safety-icsrreport}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (9)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `drugs` | array of [`Safety_IcsrDrug`](#schema-safety-icsrdrug) | `required` |  |
| `header` | [`Safety_IcsrHeader`](#schema-safety-icsrheader) | `required` |  |
| `narrative` | `string` | `required` |  |
| `patient` | [`Safety_IcsrPatient`](#schema-safety-icsrpatient) | `required` |  |
| `primarySources` | array of [`Safety_IcsrPrimarySource`](#schema-safety-icsrprimarysource) | `required` |  |
| `reactions` | array of [`Safety_IcsrReaction`](#schema-safety-icsrreaction) | `required` |  |
| `safetyReport` | [`Safety_IcsrSafetyReport`](#schema-safety-icsrsafetyreport) | `required` |  |
| `sender` | [`Safety_IcsrSender`](#schema-safety-icsrsender) | `required` |  |
| `study` | [`Safety_IcsrStudy`](#schema-safety-icsrstudy) \| `null` | `optional` |  |

</details>

### `Safety_IcsrSafetyReport` {#schema-safety-icsrsafetyreport}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (10)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `creationDate` | `string` | `required` |  |
| `documentsHeldBySender` | array of `string` \| `null` | `optional` |  |
| `firstReceivedDate` | `string` | `required` |  |
| `firstSender` | `string` | `required` |  |
| `fulfilsLocalCriteriaForExpeditedReport` | `boolean` \| `null` | `required` |  |
| `mostRecentInformationDate` | `string` | `required` |  |
| `otherCaseIdentifiers` | array of [`Safety_IcsrOtherCaseIdentifier`](#schema-safety-icsrothercaseidentifier) \| `null` | `optional` |  |
| `reportType` | `string` | `required` |  |
| `senderSafetyReportId` | `string` | `required` |  |
| `worldwideUniqueCaseId` | `string` | `required` |  |

</details>

### `Safety_IcsrSender` {#schema-safety-icsrsender}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `organisation` | `string` \| `null` | `optional` |  |
| `senderType` | `string` | `required` |  |

</details>

### `Safety_IcsrStudy` {#schema-safety-icsrstudy}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `sponsorStudyNumber` | `string` \| `null` | `optional` |  |
| `studyName` | `string` \| `null` | `optional` |  |
| `studyType` | `string` | `required` |  |

</details>

### `Safety_PeriodicSafetyReportRecord` {#schema-safety-periodicsafetyreportrecord}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (17)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `approvedAt` | `string` (`date-time`) \| `null` | `optional` |  |
| `approvedByUserId` | `string` \| `null` | `optional` |  |
| `compoundName` | `string` | `optional` |  |
| `createdAt` | `string` (`date-time`) | `required` |  |
| `createdBy` | `string` | `optional` |  |
| `esignatureManifestId` | `string` \| `null` | `optional` |  |
| `id` | `string` | `optional` |  |
| `periodEnd` | `string` (`date-time`) | `optional` |  |
| `periodStart` | `string` (`date-time`) | `optional` |  |
| `reasonForChange` | `string` | `optional` |  |
| `reportPayloadJson` | `string` | `optional` |  |
| `reportType` | `string` | `optional` |  |
| `status` | `string` | `optional` |  |
| `studyId` | `string` | `optional` |  |
| `totalPatientYearsExposure` | `number` (`double`) | `optional` |  |
| `totalSaes` | `integer` (`int32`) | `optional` |  |
| `versionIndex` | `integer` (`int32`) | `optional` |  |

</details>

### `Safety_ProblemDetails` {#schema-safety-problemdetails}

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

### `Safety_SafetyCaseIcsrInput` {#schema-safety-safetycaseicsrinput}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (17)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `actionTaken` | `string` | `required` |  |
| `aeTerm` | `string` | `required` |  |
| `aesiCategory` | `string` \| `null` | `required` |  |
| `caseId` | `string` | `required` |  |
| `causality` | `string` | `required` |  |
| `expectedness` | `string` | `required` |  |
| `isAesi` | `boolean` | `required` |  |
| `isFatal` | `boolean` | `required` |  |
| `isSerious` | `boolean` | `required` |  |
| `meddraPt` | `string` | `required` |  |
| `meddraSoc` | `string` | `required` |  |
| `onsetDate` | `string` (`date-time`) | `required` |  |
| `receiptDate` | `string` (`date-time`) | `required` |  |
| `resolutionDate` | `string` (`date-time`) \| `null` | `required` |  |
| `studyId` | `string` | `required` |  |
| `subjectId` | `string` | `required` |  |
| `treatmentArm` | `string` | `required` |  |

</details>

### `Safety_SubjectEnrollmentRecord` {#schema-safety-subjectenrollmentrecord}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `discontinuationDate` | `string` (`date-time`) \| `null` | `required` |  |
| `enrollmentDate` | `string` (`date-time`) | `required` |  |
| `studyId` | `string` | `required` |  |
| `subjectId` | `string` | `required` |  |
| `treatmentArm` | `string` | `required` |  |

</details>

