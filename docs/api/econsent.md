# econsent API

Generated from `docs/openapi/econsent_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

- **Service Maturity:** <span class="VPBadge tip">Active</span> (Port `8011` · Gateway prefixes: `/api/v1/econsent`)
- **OpenAPI 3.1 Contract:** [`econsent_openapi.json`](../openapi/econsent_openapi.json) (6 operation(s), 6 schema(s))

## Operations

| Method | Path | Operation | Summary | Contract |
| ------ | ---- | --------- | ------- | -------- |
| `GET` | `/api/v1/econsent/status` | EconsentStatus | Returns eConsent operational status. | [Details](#op-econsentstatus) |
| `POST` | `/api/v1/econsent/templates/{template_id}/capture-consent` | CaptureConsentLegacy | Captures a participant's 21 CFR Part 11 electronic consent signature against the latest published template. | [Details](#op-captureconsentlegacy) |
| `POST` | `/api/v1/econsent/templates/{template_id}/publish` | PublishTemplate | Publishes the next version of an eConsent template for the specified protocol version. | [Details](#op-publishtemplate) |
| `POST` | `/api/v1/econsent/templates/{template_id}/versions/{version_index}/capture-consent` | CaptureConsentVersioned | Captures a participant's 21 CFR Part 11 electronic consent signature against a versioned template. | [Details](#op-captureconsentversioned) |
| `POST` | `/api/v1/econsent/templates/{template_id}/versions/{version_index}/publish` | PublishTemplateVersioned | Publishes a specific eConsent template version and records whether participant re-consent is required. | [Details](#op-publishtemplateversioned) |
| `GET` | `/healthz` | EconsentHealthCheck | eConsent service health probe. | [Details](#op-econsenthealthcheck) |

### `GET` `/api/v1/econsent/status` {#op-econsentstatus}

**Operation ID:** `EconsentStatus` — Returns eConsent operational status.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Econsent`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Econsent_AnonymousTypeOfstringAndstring`](#schema-econsent-anonymoustypeofstringandstring) |

</details>

### `POST` `/api/v1/econsent/templates/{template_id}/capture-consent` {#op-captureconsentlegacy}

**Operation ID:** `CaptureConsentLegacy` — Captures a participant's 21 CFR Part 11 electronic consent signature against the latest published template.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `econsent_consent:capture`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eConsent`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `template_id` | `path` | `required` | `string` |  |
| `X-Sig-Token` | `header` | `required` | `string` (minLen: 1) | Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Econsent_CaptureConsentRequest`](#schema-econsent-captureconsentrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Econsent_CaptureConsentResponse`](#schema-econsent-captureconsentresponse) |
| `400` | Bad Request | `application/problem+json` | [`Econsent_ProblemDetails`](#schema-econsent-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Econsent_ProblemDetails`](#schema-econsent-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Econsent_ProblemDetails`](#schema-econsent-problemdetails) |

</details>

### `POST` `/api/v1/econsent/templates/{template_id}/publish` {#op-publishtemplate}

**Operation ID:** `PublishTemplate` — Publishes the next version of an eConsent template for the specified protocol version.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `econsent_template:publish`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eConsent`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `template_id` | `path` | `required` | `string` |  |

**Request Body** (`optional`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Econsent_PublishTemplateRequest`](#schema-econsent-publishtemplaterequest) \| `null` |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Econsent_PublishTemplateResponse`](#schema-econsent-publishtemplateresponse) |
| `403` | Forbidden | `application/problem+json` | [`Econsent_ProblemDetails`](#schema-econsent-problemdetails) |

</details>

### `POST` `/api/v1/econsent/templates/{template_id}/versions/{version_index}/capture-consent` {#op-captureconsentversioned}

**Operation ID:** `CaptureConsentVersioned` — Captures a participant's 21 CFR Part 11 electronic consent signature against a versioned template.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `econsent_consent:capture`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eConsent`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `template_id` | `path` | `required` | `string` |  |
| `version_index` | `path` | `required` | `integer` (`int32`) |  |
| `X-Sig-Token` | `header` | `required` | `string` (minLen: 1) | Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Econsent_CaptureConsentRequest`](#schema-econsent-captureconsentrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Econsent_CaptureConsentResponse`](#schema-econsent-captureconsentresponse) |
| `400` | Bad Request | `application/problem+json` | [`Econsent_ProblemDetails`](#schema-econsent-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Econsent_ProblemDetails`](#schema-econsent-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Econsent_ProblemDetails`](#schema-econsent-problemdetails) |

</details>

### `POST` `/api/v1/econsent/templates/{template_id}/versions/{version_index}/publish` {#op-publishtemplateversioned}

**Operation ID:** `PublishTemplateVersioned` — Publishes a specific eConsent template version and records whether participant re-consent is required.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `econsent_template:publish`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `eConsent`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `template_id` | `path` | `required` | `string` |  |
| `version_index` | `path` | `required` | `integer` (`int32`) |  |

**Request Body** (`optional`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Econsent_PublishTemplateRequest`](#schema-econsent-publishtemplaterequest) \| `null` |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Econsent_PublishTemplateResponse`](#schema-econsent-publishtemplateresponse) |
| `403` | Forbidden | `application/problem+json` | [`Econsent_ProblemDetails`](#schema-econsent-problemdetails) |

</details>

### `GET` `/healthz` {#op-econsenthealthcheck}

**Operation ID:** `EconsentHealthCheck` — eConsent service health probe.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Econsent`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Econsent_AnonymousTypeOfstringAndstring`](#schema-econsent-anonymoustypeofstringandstring) |

</details>

## Schemas

Deduplicated component schemas exported in `components.schemas` (6 total). Operation payloads and responses link here so nested models are defined once per page.

### `Econsent_AnonymousTypeOfstringAndstring` {#schema-econsent-anonymoustypeofstringandstring}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `service` | `string` \| `null` | `required` |  |
| `status` | `string` \| `null` | `required` |  |

</details>

### `Econsent_CaptureConsentRequest` {#schema-econsent-captureconsentrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (13)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `device_timestamp` | `string` (`date-time`) \| `null` | `required` |  |
| `digest_sha256` | `string` \| `null` | `required` |  |
| `liveness_token` | `string` \| `null` | `required` |  |
| `meaning` | `string` \| `null` | `required` |  |
| `protocol_version` | `string` \| `null` | `required` |  |
| `signature_data` | `string` \| `null` | `required` |  |
| `signer_email` | `string` \| `null` | `required` |  |
| `signer_name` | `string` \| `null` | `required` |  |
| `signer_role` | `string` \| `null` | `required` |  |
| `signing_reason` | `string` \| `null` | `required` |  |
| `site_id` | `string` \| `null` | `required` |  |
| `study_id` | `string` \| `null` | `required` |  |
| `subject_pseudonym` | `string` \| `null` | `required` |  |

</details>

### `Econsent_CaptureConsentResponse` {#schema-econsent-captureconsentresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (10)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `id` | `string` | `required` |  |
| `meaning` | `string` | `required` |  |
| `server_timestamp` | `string` (`date-time`) | `required` |  |
| `sha256_hash` | `string` | `required` |  |
| `signer_name` | `string` | `required` |  |
| `signer_role` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `subject_pseudonym` | `string` | `required` |  |
| `template_id` | `string` | `required` |  |
| `version_index` | `integer` (`int32`) | `required` |  |

</details>

### `Econsent_ProblemDetails` {#schema-econsent-problemdetails}

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

### `Econsent_PublishTemplateRequest` {#schema-econsent-publishtemplaterequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `change_reason` | `string` \| `null` | `required` |  |
| `change_summary` | `string` \| `null` | `required` |  |
| `channels` | array of `string` \| `null` | `required` |  |
| `protocol_version` | `string` \| `null` | `required` |  |
| `requires_reconsent` | `boolean` \| `null` | `required` |  |
| `study_id` | `string` \| `null` | `required` |  |

</details>

### `Econsent_PublishTemplateResponse` {#schema-econsent-publishtemplateresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `is_published` | `boolean` | `required` |  |
| `published_at` | `string` (`date-time`) | `required` |  |
| `requires_reconsent` | `boolean` | `required` |  |
| `template_id` | `string` | `required` |  |
| `version_index` | `integer` (`int32`) | `required` |  |

</details>

