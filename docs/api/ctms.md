# ctms API

Generated from `docs/openapi/ctms_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

- **Service Maturity:** <span class="VPBadge tip">Active</span> (Port `8007` · Gateway prefixes: `/api/v1/ctms`)
- **OpenAPI 3.1 Contract:** [`ctms_openapi.json`](../openapi/ctms_openapi.json) (23 operation(s), 23 schema(s))

## Operations

| Method | Path | Operation | Summary | Contract |
| ------ | ---- | --------- | ------- | -------- |
| `POST` | `/api/v1/ctms/doa/delegate` | DelegateSiteTasks | Assign site trial task delegation requiring Principal Investigator sign-off. | [Details](#op-delegatesitetasks) |
| `POST` | `/api/v1/ctms/doa/revoke` | RevokeSiteTasks | Revoke or end a delegated trial duty with reason for change. | [Details](#op-revokesitetasks) |
| `POST` | `/api/v1/ctms/doa/sign-off` | SignOffDelegation | Endorse Delegation of Authority task assignment with Principal Investigator eSignature. | [Details](#op-signoffdelegation) |
| `GET` | `/api/v1/ctms/doa/sites/{site_id}/export-pdf` | ExportSiteDoaPdf | Export 21 CFR Part 11 signed DOA PDF log. | [Details](#op-exportsitedoapdf) |
| `GET` | `/api/v1/ctms/doa/sites/{site_id}/log` | GetSiteDoaLog | Fetch active and historical DOA log matrix for a site. | [Details](#op-getsitedoalog) |
| `POST` | `/api/v1/ctms/letters/compile` | CompileSiteVisitLetterPdf | Compiles a GxP-compliant PDF visit letter with Part 11 electronic signature, eTMF filing, and automated email dispatch. | [Details](#op-compilesitevisitletterpdf) |
| `POST` | `/api/v1/ctms/letters/render` | RenderSiteVisitLetter | Renders a CTMS site visit monitoring letter via Liquid template. | [Details](#op-rendersitevisitletter) |
| `GET` | `/api/v1/ctms/letters/{letterId}/pdf` | GetSiteVisitLetterPdf | Downloads or views the compiled GxP PDF visit letter. | [Details](#op-getsitevisitletterpdf) |
| `GET` | `/api/v1/ctms/monitoring-visits` | ListMonitoringVisits | List monitoring visits filtered by study, site, CRA, or status. | [Details](#op-listmonitoringvisits) |
| `POST` | `/api/v1/ctms/monitoring-visits` | ScheduleMonitoringVisit | Schedule a new monitoring visit. | [Details](#op-schedulemonitoringvisit) |
| `POST` | `/api/v1/ctms/monitoring-visits/{visit_id}/complete` | CompleteMonitoringVisit | Complete a scheduled monitoring visit with actual date and clinical findings. | [Details](#op-completemonitoringvisit) |
| `GET` | `/api/v1/ctms/monitoring-visits/{visit_id}/letters` | GetMonitoringVisitLetters | Get generated letters for a monitoring visit. | [Details](#op-getmonitoringvisitletters) |
| `GET` | `/api/v1/ctms/monitoring-visits/{visit_id}/letters/{letter_type}` | GetMonitoringVisitLetterByType | Get a generated visit letter by type. | [Details](#op-getmonitoringvisitletterbytype) |
| `POST` | `/api/v1/ctms/monitoring-visits/{visit_id}/sign-off` | SignOffMonitoringVisit | Perform supervisory Part 11 sign-off on a completed monitoring visit. | [Details](#op-signoffmonitoringvisit) |
| `GET` | `/api/v1/ctms/site-milestones` | ListSiteMilestones | List site milestones filtered by study or site. | [Details](#op-listsitemilestones) |
| `POST` | `/api/v1/ctms/site-milestones` | CreateSiteMilestone | Create a new site milestone. | [Details](#op-createsitemilestone) |
| `PUT` | `/api/v1/ctms/site-milestones/{milestone_id}` | UpdateSiteMilestone | Update site milestone status, planned date, or actual date. | [Details](#op-updatesitemilestone) |
| `GET` | `/api/v1/ctms/startup/matrix` | GetCountryGreenlightMatrix | Displays country-level matrix showing 6-facet greenlight readiness for all sites in a selected country. | [Details](#op-getcountrygreenlightmatrix) |
| `POST` | `/api/v1/ctms/startup/matrix/batch-certify` | BatchCertifyCountrySites | Batch-certifies all selected 100% compliant sites in a target country with a single e-signature. | [Details](#op-batchcertifycountrysites) |
| `GET` | `/api/v1/ctms/startup/sites/{siteId}/greenlight` | EvaluateSiteGreenlight | Evaluates 6-facet greenlight readiness for a single trial site. | [Details](#op-evaluatesitegreenlight) |
| `POST` | `/api/v1/ctms/startup/sites/{siteId}/greenlight/certify` | CertifySiteGreenlight | Certifies greenlight activation for a single trial site, issuing an HMAC-SHA256 token. | [Details](#op-certifysitegreenlight) |
| `GET` | `/api/v1/ctms/status` | CtmsStatus | Returns CTMS operational status. | [Details](#op-ctmsstatus) |
| `GET` | `/healthz` | CtmsHealthCheck | CTMS operations health probe. | [Details](#op-ctmshealthcheck) |

### `POST` `/api/v1/ctms/doa/delegate` {#op-delegatesitetasks}

**Operation ID:** `DelegateSiteTasks` — Assign site trial task delegation requiring Principal Investigator sign-off.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `ctms_doa:assign`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `DoaEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `study_id` | `query` | `optional` | `string` |  |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Ctms_DelegationTaskRequest`](#schema-ctms-delegationtaskrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Ctms_DelegationResponseDto`](#schema-ctms-delegationresponsedto) |
| `400` | Bad Request | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |

</details>

### `POST` `/api/v1/ctms/doa/revoke` {#op-revokesitetasks}

**Operation ID:** `RevokeSiteTasks` — Revoke or end a delegated trial duty with reason for change.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `ctms_doa:revoke`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `DoaEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `study_id` | `query` | `optional` | `string` |  |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Ctms_RevokeDelegationRequest`](#schema-ctms-revokedelegationrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Ctms_DelegationResponseDto`](#schema-ctms-delegationresponsedto) |
| `400` | Bad Request | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `409` | Conflict | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |

</details>

### `POST` `/api/v1/ctms/doa/sign-off` {#op-signoffdelegation}

**Operation ID:** `SignOffDelegation` — Endorse Delegation of Authority task assignment with Principal Investigator eSignature.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `ctms_doa:sign_off`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `DoaEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `study_id` | `query` | `optional` | `string` |  |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Ctms_DoaSignOffRequest`](#schema-ctms-doasignoffrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Ctms_DelegationResponseDto`](#schema-ctms-delegationresponsedto) |
| `400` | Bad Request | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `409` | Conflict | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |

</details>

### `GET` `/api/v1/ctms/doa/sites/{site_id}/export-pdf` {#op-exportsitedoapdf}

**Operation ID:** `ExportSiteDoaPdf` — Export 21 CFR Part 11 signed DOA PDF log.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `ctms_doa:read`
- **Clinical Scope Enforcement:** `RouteSite:site_id`
- **Tags:** `DoaEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `site_id` | `path` | `required` | `string` |  |
| `study_id` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/pdf` | `string` (`binary`) |
| `403` | Forbidden | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |

</details>

### `GET` `/api/v1/ctms/doa/sites/{site_id}/log` {#op-getsitedoalog}

**Operation ID:** `GetSiteDoaLog` — Fetch active and historical DOA log matrix for a site.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `ctms_doa:read`
- **Clinical Scope Enforcement:** `RouteSite:site_id`
- **Tags:** `DoaEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `site_id` | `path` | `required` | `string` |  |
| `known_at` | `query` | `optional` | `string` (`date-time`) |  |
| `study_id` | `query` | `optional` | `string` |  |
| `valid_at` | `query` | `optional` | `string` (`date-time`) |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Ctms_DoaLogResponseDto`](#schema-ctms-doalogresponsedto) |
| `403` | Forbidden | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |

</details>

### `POST` `/api/v1/ctms/letters/compile` {#op-compilesitevisitletterpdf}

**Operation ID:** `CompileSiteVisitLetterPdf` — Compiles a GxP-compliant PDF visit letter with Part 11 electronic signature, eTMF filing, and automated email dispatch.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `ctms_monitoring_visit:create`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `LetterTemplateEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `templateName` | `query` | `required` | `string` |  |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Ctms_SiteVisitLetterModel`](#schema-ctms-sitevisitlettermodel) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Ctms_VisitLetterCompilationResultDto`](#schema-ctms-visitlettercompilationresultdto) |
| `400` | Bad Request | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |

</details>

### `POST` `/api/v1/ctms/letters/render` {#op-rendersitevisitletter}

**Operation ID:** `RenderSiteVisitLetter` — Renders a CTMS site visit monitoring letter via Liquid template.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `ctms_monitoring_visit:create`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `LetterTemplateEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `templateName` | `query` | `required` | `string` |  |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Ctms_SiteVisitLetterModel`](#schema-ctms-sitevisitlettermodel) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `text/plain` | `string` |
| `400` | Bad Request | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |

</details>

### `GET` `/api/v1/ctms/letters/{letterId}/pdf` {#op-getsitevisitletterpdf}

**Operation ID:** `GetSiteVisitLetterPdf` — Downloads or views the compiled GxP PDF visit letter.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `ctms_monitoring_visit:create`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `LetterTemplateEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `letterId` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/pdf` | `string` (`binary`) |
| `403` | Forbidden | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |

</details>

### `GET` `/api/v1/ctms/monitoring-visits` {#op-listmonitoringvisits}

**Operation ID:** `ListMonitoringVisits` — List monitoring visits filtered by study, site, CRA, or status.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `ctms_monitoring_visit:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `MonitoringVisitEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `cra_id` | `query` | `optional` | `string` |  |
| `site_id` | `query` | `optional` | `string` |  |
| `status` | `query` | `optional` | `string` |  |
| `study_id` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Ctms_MonitoringVisitResponseDto`](#schema-ctms-monitoringvisitresponsedto) |
| `403` | Forbidden | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |

</details>

### `POST` `/api/v1/ctms/monitoring-visits` {#op-schedulemonitoringvisit}

**Operation ID:** `ScheduleMonitoringVisit` — Schedule a new monitoring visit.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `ctms_monitoring_visit:create`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `MonitoringVisitEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Change-Reason` | `header` | `optional` | `string` | Optional 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Ctms_MonitoringVisitCreateDto`](#schema-ctms-monitoringvisitcreatedto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Ctms_MonitoringVisitResponseDto`](#schema-ctms-monitoringvisitresponsedto) |
| `400` | Bad Request | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |

</details>

### `POST` `/api/v1/ctms/monitoring-visits/{visit_id}/complete` {#op-completemonitoringvisit}

**Operation ID:** `CompleteMonitoringVisit` — Complete a scheduled monitoring visit with actual date and clinical findings.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `ctms_monitoring_visit:update`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `MonitoringVisitEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `visit_id` | `path` | `required` | `string` |  |
| `X-Change-Reason` | `header` | `optional` | `string` | Optional 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Ctms_MonitoringVisitCompleteDto`](#schema-ctms-monitoringvisitcompletedto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Ctms_MonitoringVisitResponseDto`](#schema-ctms-monitoringvisitresponsedto) |
| `400` | Bad Request | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |

</details>

### `GET` `/api/v1/ctms/monitoring-visits/{visit_id}/letters` {#op-getmonitoringvisitletters}

**Operation ID:** `GetMonitoringVisitLetters` — Get generated letters for a monitoring visit.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `ctms_monitoring_letter:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `MonitoringVisitEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `visit_id` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Ctms_GeneratedLetterResponseDto`](#schema-ctms-generatedletterresponsedto) |
| `403` | Forbidden | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |

</details>

### `GET` `/api/v1/ctms/monitoring-visits/{visit_id}/letters/{letter_type}` {#op-getmonitoringvisitletterbytype}

**Operation ID:** `GetMonitoringVisitLetterByType` — Get a generated visit letter by type.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `ctms_monitoring_letter:read_type`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `MonitoringVisitEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `letter_type` | `path` | `required` | `string` |  |
| `visit_id` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Ctms_GeneratedLetterResponseDto`](#schema-ctms-generatedletterresponsedto) |
| `403` | Forbidden | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |

</details>

### `POST` `/api/v1/ctms/monitoring-visits/{visit_id}/sign-off` {#op-signoffmonitoringvisit}

**Operation ID:** `SignOffMonitoringVisit` — Perform supervisory Part 11 sign-off on a completed monitoring visit.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `ctms_monitoring_visit:sign_off`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `MonitoringVisitEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `visit_id` | `path` | `required` | `string` |  |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Ctms_MonitoringVisitResponseDto`](#schema-ctms-monitoringvisitresponsedto) |
| `400` | Bad Request | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |

</details>

### `GET` `/api/v1/ctms/site-milestones` {#op-listsitemilestones}

**Operation ID:** `ListSiteMilestones` — List site milestones filtered by study or site.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `ctms_site_milestone:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `SiteMilestoneEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `site_id` | `query` | `optional` | `string` |  |
| `study_id` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Ctms_SiteMilestoneResponseDto`](#schema-ctms-sitemilestoneresponsedto) |
| `403` | Forbidden | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |

</details>

### `POST` `/api/v1/ctms/site-milestones` {#op-createsitemilestone}

**Operation ID:** `CreateSiteMilestone` — Create a new site milestone.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `ctms_site_milestone:create`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `SiteMilestoneEndpoints`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Ctms_SiteMilestoneCreateDto`](#schema-ctms-sitemilestonecreatedto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Ctms_SiteMilestoneResponseDto`](#schema-ctms-sitemilestoneresponsedto) |
| `400` | Bad Request | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |

</details>

### `PUT` `/api/v1/ctms/site-milestones/{milestone_id}` {#op-updatesitemilestone}

**Operation ID:** `UpdateSiteMilestone` — Update site milestone status, planned date, or actual date.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `ctms_site_milestone:update`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `SiteMilestoneEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `milestone_id` | `path` | `required` | `string` |  |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Ctms_SiteMilestoneUpdateDto`](#schema-ctms-sitemilestoneupdatedto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Ctms_SiteMilestoneResponseDto`](#schema-ctms-sitemilestoneresponsedto) |
| `400` | Bad Request | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |

</details>

### `GET` `/api/v1/ctms/startup/matrix` {#op-getcountrygreenlightmatrix}

**Operation ID:** `GetCountryGreenlightMatrix` — Displays country-level matrix showing 6-facet greenlight readiness for all sites in a selected country.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `ctms_greenlight:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `StartupGreenlightEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `country_code` | `query` | `required` | `string` |  |
| `study_id` | `query` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Ctms_SiteGreenlightGateResponseDto`](#schema-ctms-sitegreenlightgateresponsedto) |
| `400` | Bad Request | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |

</details>

### `POST` `/api/v1/ctms/startup/matrix/batch-certify` {#op-batchcertifycountrysites}

**Operation ID:** `BatchCertifyCountrySites` — Batch-certifies all selected 100% compliant sites in a target country with a single e-signature.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `ctms_greenlight:certify`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `StartupGreenlightEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Change-Reason` | `header` | `optional` | `string` | Optional 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Ctms_BatchCertifyRequestDto`](#schema-ctms-batchcertifyrequestdto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Ctms_BatchCertifyResponseDto`](#schema-ctms-batchcertifyresponsedto) |
| `400` | Bad Request | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |

</details>

### `GET` `/api/v1/ctms/startup/sites/{siteId}/greenlight` {#op-evaluatesitegreenlight}

**Operation ID:** `EvaluateSiteGreenlight` — Evaluates 6-facet greenlight readiness for a single trial site.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `ctms_greenlight:read`
- **Clinical Scope Enforcement:** `RouteSite:siteId`
- **Tags:** `StartupGreenlightEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `siteId` | `path` | `required` | `string` |  |
| `country_code` | `query` | `optional` | `string` |  |
| `study_id` | `query` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Ctms_SiteGreenlightGateResponseDto`](#schema-ctms-sitegreenlightgateresponsedto) |
| `403` | Forbidden | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |

</details>

### `POST` `/api/v1/ctms/startup/sites/{siteId}/greenlight/certify` {#op-certifysitegreenlight}

**Operation ID:** `CertifySiteGreenlight` — Certifies greenlight activation for a single trial site, issuing an HMAC-SHA256 token.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `ctms_greenlight:certify`
- **Clinical Scope Enforcement:** `RouteSite:siteId`
- **Tags:** `StartupGreenlightEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `siteId` | `path` | `required` | `string` |  |
| `X-Change-Reason` | `header` | `optional` | `string` | Optional 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |
| `country_code` | `query` | `optional` | `string` |  |
| `study_id` | `query` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Ctms_SiteGreenlightGateResponseDto`](#schema-ctms-sitegreenlightgateresponsedto) |
| `400` | Bad Request | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Ctms_ProblemDetails`](#schema-ctms-problemdetails) |

</details>

### `GET` `/api/v1/ctms/status` {#op-ctmsstatus}

**Operation ID:** `CtmsStatus` — Returns CTMS operational status.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Ctms`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Ctms_AnonymousTypeOfstringAndstring`](#schema-ctms-anonymoustypeofstringandstring) |

</details>

### `GET` `/healthz` {#op-ctmshealthcheck}

**Operation ID:** `CtmsHealthCheck` — CTMS operations health probe.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Ctms`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Ctms_AnonymousTypeOfstringAndstring`](#schema-ctms-anonymoustypeofstringandstring) |

</details>

## Schemas

Deduplicated component schemas exported in `components.schemas` (23 total). Operation payloads and responses link here so nested models are defined once per page.

### `Ctms_AnonymousTypeOfstringAndstring` {#schema-ctms-anonymoustypeofstringandstring}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `service` | `string` \| `null` | `required` |  |
| `status` | `string` \| `null` | `required` |  |

</details>

### `Ctms_BatchCertifyRequestDto` {#schema-ctms-batchcertifyrequestdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `country_code` | `string` | `required` |  |
| `meaning` | `string` \| `null` | `required` |  |
| `site_ids` | array of `string` | `required` |  |
| `study_id` | `string` | `required` |  |

</details>

### `Ctms_BatchCertifyResponseDto` {#schema-ctms-batchcertifyresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `certified_count` | `integer` (`int32`) | `required` |  |
| `certified_sites` | array of [`Ctms_CertifiedSiteItemDto`](#schema-ctms-certifiedsiteitemdto) | `required` |  |
| `country_code` | `string` | `required` |  |
| `message` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `success` | `boolean` | `required` |  |

</details>

### `Ctms_CertifiedSiteItemDto` {#schema-ctms-certifiedsiteitemdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `certified_at` | `string` | `required` |  |
| `certified_by` | `string` | `required` |  |
| `greenlight_token` | `string` | `required` |  |
| `site_id` | `string` | `required` |  |
| `site_name` | `string` | `required` |  |
| `status` | `string` | `required` |  |

</details>

### `Ctms_DelegationResponseDto` {#schema-ctms-delegationresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (11)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `created_by` | `string` | `required` |  |
| `end_date` | `string` \| `null` | `required` |  |
| `is_active` | `boolean` | `required` |  |
| `reason_for_change` | `string` | `required` |  |
| `record_id` | `string` | `required` |  |
| `signed_off` | `boolean` | `required` |  |
| `site_id` | `string` | `required` |  |
| `staff_user_id` | `string` | `required` |  |
| `start_date` | `string` | `required` |  |
| `task_codes` | array of `string` | `required` |  |
| `version_index` | `integer` (`int64`) | `required` |  |

</details>

### `Ctms_DelegationTaskRequest` {#schema-ctms-delegationtaskrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `reason_for_change` | `string` \| `null` | `required` |  |
| `site_id` | `string` | `required` |  |
| `staff_user_id` | `string` | `required` |  |
| `start_date` | `string` | `required` |  |
| `study_id` | `string` \| `null` | `optional` |  |
| `task_codes` | array of `string` | `required` |  |

</details>

### `Ctms_DoaAuditHistoryItemDto` {#schema-ctms-doaaudithistoryitemdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `action` | `string` | `required` |  |
| `details` | `string` | `required` |  |
| `id` | `string` | `required` |  |
| `timestamp` | `string` (`date-time`) | `required` |  |
| `user_id` | `string` | `required` |  |
| `user_role` | `string` | `required` |  |

</details>

### `Ctms_DoaLogResponseDto` {#schema-ctms-doalogresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `audit_history` | array of [`Ctms_DoaAuditHistoryItemDto`](#schema-ctms-doaaudithistoryitemdto) | `required` |  |
| `delegated_staff` | array of [`Ctms_DelegationResponseDto`](#schema-ctms-delegationresponsedto) | `required` |  |
| `pi_name` | `string` | `required` |  |
| `site_id` | `string` | `required` |  |

</details>

### `Ctms_DoaSignOffRequest` {#schema-ctms-doasignoffrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `expected_version_index` | `integer` (`int64`) \| `null` | `optional` |  |
| `reason_for_change` | `string` \| `null` | `required` |  |
| `record_id` | `string` | `required` |  |
| `study_id` | `string` \| `null` | `optional` |  |

</details>

### `Ctms_FindingCreateDto` {#schema-ctms-findingcreatedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `resolution_status` | `string` \| `null` | `optional` |  |
| `severity` | `string` | `required` |  |
| `text` | `string` | `required` |  |

</details>

### `Ctms_GeneratedLetterResponseDto` {#schema-ctms-generatedletterresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `created_at` | `string` | `required` |  |
| `created_by` | `string` | `required` |  |
| `id` | `string` | `required` |  |
| `letter_type` | `string` | `required` |  |
| `reason_for_change` | `string` | `required` |  |
| `rendered_content` | `string` | `required` |  |
| `version_index` | `integer` (`int64`) | `required` |  |
| `visit_id` | `string` | `required` |  |

</details>

### `Ctms_MonitoringVisitCompleteDto` {#schema-ctms-monitoringvisitcompletedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `actual_date` | `string` (`date-time`) | `required` |  |
| `findings` | array of [`Ctms_FindingCreateDto`](#schema-ctms-findingcreatedto) \| `null` | `optional` |  |
| `reason_for_change` | `string` \| `null` | `optional` |  |

</details>

### `Ctms_MonitoringVisitCreateDto` {#schema-ctms-monitoringvisitcreatedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `cra_id` | `string` | `required` |  |
| `reason_for_change` | `string` \| `null` | `optional` |  |
| `scheduled_date` | `string` (`date-time`) | `required` |  |
| `site_id` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `visit_type` | `string` | `required` |  |

</details>

### `Ctms_MonitoringVisitResponseDto` {#schema-ctms-monitoringvisitresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (15)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `actual_date` | `string` \| `null` | `required` |  |
| `cra_id` | `string` | `required` |  |
| `created_at` | `string` | `required` |  |
| `created_by` | `string` | `required` |  |
| `id` | `string` | `required` |  |
| `reason_for_change` | `string` | `required` |  |
| `scheduled_date` | `string` | `required` |  |
| `signature_manifestation` | `string` \| `null` | `optional` |  |
| `signer` | `string` \| `null` | `optional` |  |
| `signing_timestamp` | `string` \| `null` | `optional` |  |
| `site_id` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `version_index` | `integer` (`int64`) | `required` |  |
| `visit_type` | `string` | `required` |  |

</details>

### `Ctms_ProblemDetails` {#schema-ctms-problemdetails}

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

### `Ctms_RevokeDelegationRequest` {#schema-ctms-revokedelegationrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `expected_version_index` | `integer` (`int64`) \| `null` | `optional` |  |
| `reason_for_change` | `string` \| `null` | `required` |  |
| `record_id` | `string` | `required` |  |
| `study_id` | `string` \| `null` | `optional` |  |

</details>

### `Ctms_SiteGreenlightGateResponseDto` {#schema-ctms-sitegreenlightgateresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (19)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `country_code` | `string` | `required` |  |
| `facet_cta_budget` | `boolean` | `required` |  |
| `facet_doa_log` | `boolean` | `required` |  |
| `facet_eisf_baseline` | `boolean` | `required` |  |
| `facet_ip_release` | `boolean` | `required` |  |
| `facet_regulatory_irb` | `boolean` | `required` |  |
| `facet_staff_training` | `boolean` | `required` |  |
| `greenlight_certified_at` | `string` \| `null` | `required` |  |
| `greenlight_certified_by` | `string` \| `null` | `required` |  |
| `greenlight_token` | `string` \| `null` | `required` |  |
| `id` | `string` | `required` |  |
| `is_compliant` | `boolean` | `required` |  |
| `is_overridden` | `boolean` | `required` |  |
| `overall_status` | `string` | `required` |  |
| `readiness_percentage` | `integer` (`int32`) | `required` |  |
| `site_id` | `string` | `required` |  |
| `site_name` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `unfulfilled_reasons` | array of `string` \| `null` | `required` |  |

</details>

### `Ctms_SiteMilestoneCreateDto` {#schema-ctms-sitemilestonecreatedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `actual_date` | `string` (`date-time`) \| `null` | `optional` |  |
| `milestone_type` | `string` | `required` |  |
| `planned_date` | `string` (`date-time`) \| `null` | `optional` |  |
| `reason_for_change` | `string` \| `null` | `optional` |  |
| `site_id` | `string` | `required` |  |
| `status` | `string` \| `null` | `optional` |  |
| `study_id` | `string` | `required` |  |

</details>

### `Ctms_SiteMilestoneResponseDto` {#schema-ctms-sitemilestoneresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (11)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `actual_date` | `string` \| `null` | `required` |  |
| `created_at` | `string` | `required` |  |
| `created_by` | `string` | `required` |  |
| `id` | `string` | `required` |  |
| `milestone_type` | `string` | `required` |  |
| `planned_date` | `string` \| `null` | `required` |  |
| `reason_for_change` | `string` | `required` |  |
| `site_id` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `version_index` | `integer` (`int64`) | `required` |  |

</details>

### `Ctms_SiteMilestoneUpdateDto` {#schema-ctms-sitemilestoneupdatedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `actual_date` | `string` (`date-time`) \| `null` | `optional` |  |
| `planned_date` | `string` (`date-time`) \| `null` | `optional` |  |
| `reason_for_change` | `string` \| `null` | `optional` |  |
| `status` | `string` \| `null` | `optional` |  |

</details>

### `Ctms_SiteVisitFindingModel` {#schema-ctms-sitevisitfindingmodel}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `resolutionStatus` | `string` | `required` |  |
| `severity` | `string` | `required` |  |
| `text` | `string` | `required` |  |

</details>

### `Ctms_SiteVisitLetterModel` {#schema-ctms-sitevisitlettermodel}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (14)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `actualDate` | `string` \| `null` | `optional` |  |
| `craId` | `string` | `required` |  |
| `createdAt` | `string` | `required` |  |
| `finalize` | `boolean` | `optional` |  |
| `findings` | array of [`Ctms_SiteVisitFindingModel`](#schema-ctms-sitevisitfindingmodel) \| `null` | `optional` |  |
| `investigatorEmail` | `string` \| `null` | `optional` |  |
| `reasonForChange` | `string` \| `null` | `optional` |  |
| `scheduledDate` | `string` \| `null` | `optional` |  |
| `signerName` | `string` \| `null` | `optional` |  |
| `signingPurpose` | `string` \| `null` | `optional` |  |
| `siteId` | `string` | `required` |  |
| `studyId` | `string` | `required` |  |
| `visitId` | `string` \| `null` | `optional` |  |
| `visitType` | `string` | `optional` |  |

</details>

### `Ctms_VisitLetterCompilationResultDto` {#schema-ctms-visitlettercompilationresultdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (12)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `dispatchedAt` | `string` (`date-time`) \| `null` | `required` |  |
| `etmfDocumentId` | `string` \| `null` | `required` |  |
| `isSigned` | `boolean` | `required` |  |
| `letterId` | `string` | `required` |  |
| `letterType` | `string` | `required` |  |
| `pdfAccessUrl` | `string` | `required` |  |
| `pdfBase64` | `string` | `required` |  |
| `pdfObjectKey` | `string` | `required` |  |
| `pdfSha256Hash` | `string` | `required` |  |
| `signatureManifestation` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `visitId` | `string` | `required` |  |

</details>

