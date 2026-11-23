# notifications API

Generated from `docs/openapi/notifications_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

- **Service Maturity:** <span class="VPBadge warning">Scaffold</span> (Port `8006` · Gateway prefixes: `/api/v1/notifications`)
- **OpenAPI 3.1 Contract:** [`notifications_openapi.json`](../openapi/notifications_openapi.json) (3 operation(s), 5 schema(s))

> **Scaffold Service (`ServiceTopology`):** This host provides production authentication, probes, and OpenAPI contract boundaries, while full domain persistence or downstream workflow coverage remains scheduled on the consolidation roadmap (`ARCHITECTURE.md §3`).

## Operations

| Method | Path | Operation | Summary | Contract |
| ------ | ---- | --------- | ------- | -------- |
| `POST` | `/api/v1/notifications/render` | RenderNotificationTemplate | Renders a Liquid notification template asynchronously. | [Details](#op-rendernotificationtemplate) |
| `GET` | `/api/v1/notifications/status` | NotificationsStatus | Returns Notifications operational status. | [Details](#op-notificationsstatus) |
| `GET` | `/healthz` | NotificationsHealthCheck | Notifications dispatch health probe. | [Details](#op-notificationshealthcheck) |

### `POST` `/api/v1/notifications/render` {#op-rendernotificationtemplate}

**Operation ID:** `RenderNotificationTemplate` — Renders a Liquid notification template asynchronously.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `notification:send`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `NotificationTemplateEndpoints`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `templateName` | `query` | `required` | `string` |  |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Notifications_NotificationRenderModel`](#schema-notifications-notificationrendermodel) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Notifications_RenderedNotification`](#schema-notifications-renderednotification) |
| `400` | Bad Request | `application/problem+json` | [`Notifications_ProblemDetails`](#schema-notifications-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Notifications_ProblemDetails`](#schema-notifications-problemdetails) |

</details>

### `GET` `/api/v1/notifications/status` {#op-notificationsstatus}

**Operation ID:** `NotificationsStatus` — Returns Notifications operational status.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Notifications`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Notifications_AnonymousTypeOfstringAndstring`](#schema-notifications-anonymoustypeofstringandstring) |

</details>

### `GET` `/healthz` {#op-notificationshealthcheck}

**Operation ID:** `NotificationsHealthCheck` — Notifications dispatch health probe.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Notifications`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Notifications_AnonymousTypeOfstringAndstring`](#schema-notifications-anonymoustypeofstringandstring) |

</details>

## Schemas

Deduplicated component schemas exported in `components.schemas` (5 total). Operation payloads and responses link here so nested models are defined once per page.

### `Notifications_AnonymousTypeOfstringAndstring` {#schema-notifications-anonymoustypeofstringandstring}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `service` | `string` \| `null` | `required` |  |
| `status` | `string` \| `null` | `required` |  |

</details>

### `Notifications_NotificationPayload` {#schema-notifications-notificationpayload}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (21)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `adverseEventDescription` | `string` | `optional` |  |
| `amendmentTag` | `string` | `optional` |  |
| `artifactCode` | `string` | `optional` |  |
| `changeReason` | `string` | `optional` |  |
| `changeSummary` | `string` | `optional` |  |
| `diaryName` | `string` | `optional` |  |
| `discrepancySource` | `string` | `optional` |  |
| `documentName` | `string` | `optional` |  |
| `dueDate` | `string` | `optional` |  |
| `expirationDate` | `string` | `optional` |  |
| `fieldName` | `string` | `optional` |  |
| `flagReason` | `string` | `optional` |  |
| `formName` | `string` | `optional` |  |
| `protocolVersion` | `string` | `optional` |  |
| `queryMessage` | `string` | `optional` |  |
| `siteId` | `string` | `optional` |  |
| `subjectId` | `string` | `optional` |  |
| `submittedBy` | `string` | `optional` |  |
| `summaryOfChanges` | `string` | `optional` |  |
| `versionIndex` | `string` | `optional` |  |
| `versionNumber` | `string` | `optional` |  |

</details>

### `Notifications_NotificationRenderModel` {#schema-notifications-notificationrendermodel}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `eventId` | `string` | `required` |  |
| `payload` | [`Notifications_NotificationPayload`](#schema-notifications-notificationpayload) | `required` |  |
| `studyId` | `string` | `required` |  |
| `timestampUtc` | `string` | `required` |  |

</details>

### `Notifications_ProblemDetails` {#schema-notifications-problemdetails}

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

### `Notifications_RenderedNotification` {#schema-notifications-renderednotification}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `content` | `string` | `required` |  |
| `templateName` | `string` | `required` |  |

</details>

