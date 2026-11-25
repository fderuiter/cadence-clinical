# tickets API

Generated from `docs/openapi/tickets_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

- **Service Maturity:** <span class="VPBadge warning">Scaffold</span> (Port `8009` · Gateway prefixes: `/api/v1/tickets`)
- **OpenAPI 3.1 Contract:** [`tickets_openapi.json`](../openapi/tickets_openapi.json) (11 operation(s), 10 schema(s))

> **Scaffold Service (`ServiceTopology`):** This host provides production authentication, probes, and OpenAPI contract boundaries, while full domain persistence or downstream workflow coverage remains scheduled on the consolidation roadmap (`ARCHITECTURE.md §3`).

## Operations

| Method | Path | Operation | Summary | Contract |
| ------ | ---- | --------- | ------- | -------- |
| `GET` | `/api/v1/tickets` | ListTickets | Lists operational tickets filtered by study, site, status, priority, or queue. | [Details](#op-listtickets) |
| `POST` | `/api/v1/tickets` | CreateTicket | Creates a GxP support or operational issue ticket with SLA targets and hash-chained audit entry. | [Details](#op-createticket) |
| `GET` | `/api/v1/tickets/status` | TicketsStatus | Returns Tickets operational status. | [Details](#op-ticketsstatus) |
| `GET` | `/api/v1/tickets/{ticket_id}` | GetTicket | Retrieves a single operational ticket by ID or reference code. | [Details](#op-getticket) |
| `PATCH` | `/api/v1/tickets/{ticket_id}` | UpdateTicket | Updates mutable ticket fields with optimistic concurrency and Part 11 change reason. | [Details](#op-updateticket) |
| `GET` | `/api/v1/tickets/{ticket_id}/audit-trail` | GetTicketAuditTrail | Retrieves the hash-chained GxP audit trail for an operational ticket. | [Details](#op-getticketaudittrail) |
| `GET` | `/api/v1/tickets/{ticket_id}/comments` | ListTicketComments | Lists role-filtered public and sponsor-internal comments on a ticket. | [Details](#op-listticketcomments) |
| `POST` | `/api/v1/tickets/{ticket_id}/comments` | CreateTicketComment | Appends a visibility-scoped comment to an operational ticket. | [Details](#op-createticketcomment) |
| `POST` | `/api/v1/tickets/{ticket_id}/sign` | SignTicket | Applies a 21 CFR Part 11 step-up electronic signature to close or attest a GxP ticket. | [Details](#op-signticket) |
| `POST` | `/api/v1/tickets/{ticket_id}/transitions` | TransitionTicket | Transitions a ticket across workflow states and updates SLA pause timers. | [Details](#op-transitionticket) |
| `GET` | `/healthz` | TicketsHealthCheck | Support tickets health probe. | [Details](#op-ticketshealthcheck) |

### `GET` `/api/v1/tickets` {#op-listtickets}

**Operation ID:** `ListTickets` — Lists operational tickets filtered by study, site, status, priority, or queue.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `tickets:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `Tickets`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `assigned_queue` | `query` | `optional` | `string` |  |
| `category` | `query` | `optional` | `string` |  |
| `gxp_severity` | `query` | `optional` | `string` |  |
| `limit` | `query` | `optional` | `integer` (`int32`) |  |
| `offset` | `query` | `optional` | `integer` (`int32`) |  |
| `priority` | `query` | `optional` | `string` |  |
| `search` | `query` | `optional` | `string` |  |
| `site_id` | `query` | `optional` | `string` |  |
| `status` | `query` | `optional` | `string` |  |
| `study_id` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Tickets_TicketResponseDto`](#schema-tickets-ticketresponsedto) |
| `401` | Unauthorized | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |

</details>

### `POST` `/api/v1/tickets` {#op-createticket}

**Operation ID:** `CreateTicket` — Creates a GxP support or operational issue ticket with SLA targets and hash-chained audit entry.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `tickets:create`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `Tickets`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Tickets_CreateTicketRequestDto`](#schema-tickets-createticketrequestdto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Tickets_TicketResponseDto`](#schema-tickets-ticketresponsedto) |
| `400` | Bad Request | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |

</details>

### `GET` `/api/v1/tickets/status` {#op-ticketsstatus}

**Operation ID:** `TicketsStatus` — Returns Tickets operational status.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Tickets`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Tickets_AnonymousTypeOfstringAndstring`](#schema-tickets-anonymoustypeofstringandstring) |

</details>

### `GET` `/api/v1/tickets/{ticket_id}` {#op-getticket}

**Operation ID:** `GetTicket` — Retrieves a single operational ticket by ID or reference code.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `tickets:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `Tickets`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `ticket_id` | `path` | `required` | `string` |  |
| `as_of` | `query` | `optional` | `string` (`date-time`) |  |
| `revision` | `query` | `optional` | `integer` (`int32`) |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Tickets_TicketResponseDto`](#schema-tickets-ticketresponsedto) |
| `401` | Unauthorized | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |

</details>

### `PATCH` `/api/v1/tickets/{ticket_id}` {#op-updateticket}

**Operation ID:** `UpdateTicket` — Updates mutable ticket fields with optimistic concurrency and Part 11 change reason.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `tickets:create`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `Tickets`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `ticket_id` | `path` | `required` | `string` |  |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Tickets_UpdateTicketRequestDto`](#schema-tickets-updateticketrequestdto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Tickets_TicketResponseDto`](#schema-tickets-ticketresponsedto) |
| `400` | Bad Request | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |
| `409` | Conflict | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |

</details>

### `GET` `/api/v1/tickets/{ticket_id}/audit-trail` {#op-getticketaudittrail}

**Operation ID:** `GetTicketAuditTrail` — Retrieves the hash-chained GxP audit trail for an operational ticket.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `tickets:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `Tickets`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `ticket_id` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Tickets_TicketAuditEntryDto`](#schema-tickets-ticketauditentrydto) |
| `401` | Unauthorized | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |

</details>

### `GET` `/api/v1/tickets/{ticket_id}/comments` {#op-listticketcomments}

**Operation ID:** `ListTicketComments` — Lists role-filtered public and sponsor-internal comments on a ticket.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `tickets:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `Tickets`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `ticket_id` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Tickets_TicketCommentResponseDto`](#schema-tickets-ticketcommentresponsedto) |
| `401` | Unauthorized | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |

</details>

### `POST` `/api/v1/tickets/{ticket_id}/comments` {#op-createticketcomment}

**Operation ID:** `CreateTicketComment` — Appends a visibility-scoped comment to an operational ticket.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `tickets:comment`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `Tickets`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `ticket_id` | `path` | `required` | `string` |  |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Tickets_CreateTicketCommentRequestDto`](#schema-tickets-createticketcommentrequestdto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Tickets_TicketCommentResponseDto`](#schema-tickets-ticketcommentresponsedto) |
| `400` | Bad Request | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |
| `409` | Conflict | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |

</details>

### `POST` `/api/v1/tickets/{ticket_id}/sign` {#op-signticket}

**Operation ID:** `SignTicket` — Applies a 21 CFR Part 11 step-up electronic signature to close or attest a GxP ticket.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `tickets:sign`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `Tickets`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `ticket_id` | `path` | `required` | `string` |  |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |
| `X-Sig-Token` | `header` | `required` | `string` (minLen: 1) | Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Tickets_SignTicketRequestDto`](#schema-tickets-signticketrequestdto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Tickets_TicketResponseDto`](#schema-tickets-ticketresponsedto) |
| `400` | Bad Request | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |
| `409` | Conflict | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |

</details>

### `POST` `/api/v1/tickets/{ticket_id}/transitions` {#op-transitionticket}

**Operation ID:** `TransitionTicket` — Transitions a ticket across workflow states and updates SLA pause timers.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `tickets:transition`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `Tickets`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `ticket_id` | `path` | `required` | `string` |  |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Tickets_TransitionTicketRequestDto`](#schema-tickets-transitionticketrequestdto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Tickets_TicketResponseDto`](#schema-tickets-ticketresponsedto) |
| `400` | Bad Request | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |
| `409` | Conflict | `application/problem+json` | [`Tickets_ProblemDetails`](#schema-tickets-problemdetails) |

</details>

### `GET` `/healthz` {#op-ticketshealthcheck}

**Operation ID:** `TicketsHealthCheck` — Support tickets health probe.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Tickets`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Tickets_AnonymousTypeOfstringAndstring`](#schema-tickets-anonymoustypeofstringandstring) |

</details>

## Schemas

Deduplicated component schemas exported in `components.schemas` (10 total). Operation payloads and responses link here so nested models are defined once per page.

### `Tickets_AnonymousTypeOfstringAndstring` {#schema-tickets-anonymoustypeofstringandstring}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `service` | `string` \| `null` | `required` |  |
| `status` | `string` \| `null` | `required` |  |

</details>

### `Tickets_CreateTicketCommentRequestDto` {#schema-tickets-createticketcommentrequestdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `body` | `string` | `required` |  |
| `visibility` | `string` \| `null` | `optional` |  |

</details>

### `Tickets_CreateTicketRequestDto` {#schema-tickets-createticketrequestdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (12)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `category` | `string` \| `null` | `optional` |  |
| `description` | `string` | `required` |  |
| `entity_id` | `string` \| `null` | `optional` |  |
| `entity_type` | `string` \| `null` | `optional` |  |
| `gxp_severity` | `string` \| `null` | `optional` |  |
| `persona` | `string` \| `null` | `optional` |  |
| `priority` | `string` \| `null` | `optional` |  |
| `site_id` | `string` \| `null` | `optional` |  |
| `source_route` | `string` \| `null` | `optional` |  |
| `study_id` | `string` \| `null` | `optional` |  |
| `subject_id` | `string` \| `null` | `optional` |  |
| `title` | `string` | `required` |  |

</details>

### `Tickets_ProblemDetails` {#schema-tickets-problemdetails}

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

### `Tickets_SignTicketRequestDto` {#schema-tickets-signticketrequestdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `signature_action` | `string` \| `null` | `optional` |  |
| `signature_meaning` | `string` | `required` |  |

</details>

### `Tickets_TicketAuditEntryDto` {#schema-tickets-ticketauditentrydto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (11)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `action` | `string` | `required` |  |
| `block_hash` | `string` | `required` |  |
| `changes_json` | `string` \| `null` | `required` |  |
| `entity_id` | `string` | `required` |  |
| `entity_name` | `string` | `required` |  |
| `id` | `string` (`uuid`) | `required` |  |
| `predecessor_hash` | `string` | `required` |  |
| `reason_for_change` | `string` | `required` |  |
| `timestamp` | `string` (`date-time`) | `required` |  |
| `user_id` | `string` | `required` |  |
| `version_index` | `integer` (`int64`) | `required` |  |

</details>

### `Tickets_TicketCommentResponseDto` {#schema-tickets-ticketcommentresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `body` | `string` | `required` |  |
| `created_at` | `string` (`date-time`) | `required` |  |
| `created_by` | `string` | `required` |  |
| `id` | `string` | `required` |  |
| `reason_for_change` | `string` | `required` |  |
| `ticket_id` | `string` | `required` |  |
| `version_index` | `integer` (`int32`) | `required` |  |
| `visibility` | `string` | `required` |  |

</details>

### `Tickets_TicketResponseDto` {#schema-tickets-ticketresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (39)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `acknowledge_target_at` | `string` (`date-time`) \| `null` | `required` |  |
| `assigned_queue` | `string` | `required` |  |
| `assignee_role` | `string` \| `null` | `required` |  |
| `assignee_user` | `string` \| `null` | `required` |  |
| `category` | `string` | `required` |  |
| `created_at` | `string` (`date-time`) | `required` |  |
| `created_by` | `string` | `required` |  |
| `description` | `string` | `required` |  |
| `due_date` | `string` (`date-time`) \| `null` | `required` |  |
| `escalation_count` | `integer` (`int32`) | `required` |  |
| `gxp_severity` | `string` | `required` |  |
| `id` | `string` | `required` |  |
| `priority` | `string` | `required` |  |
| `reason_for_change` | `string` | `required` |  |
| `reference` | `string` | `required` |  |
| `related_entity_id` | `string` \| `null` | `required` |  |
| `related_entity_type` | `string` \| `null` | `required` |  |
| `reporter` | `string` | `required` |  |
| `resolution_code` | `string` \| `null` | `required` |  |
| `root_cause_category` | `string` \| `null` | `required` |  |
| `root_cause_summary` | `string` \| `null` | `required` |  |
| `signature_action` | `string` \| `null` | `required` |  |
| `signature_meaning` | `string` \| `null` | `required` |  |
| `signature_timestamp` | `string` (`date-time`) \| `null` | `required` |  |
| `signature_token` | `string` \| `null` | `required` |  |
| `signature_user` | `string` \| `null` | `required` |  |
| `site_id` | `string` \| `null` | `required` |  |
| `sla_amber_warned` | `boolean` | `required` |  |
| `sla_breached` | `boolean` | `required` |  |
| `sla_paused_at` | `string` (`date-time`) \| `null` | `required` |  |
| `sla_target_at` | `string` (`date-time`) \| `null` | `required` |  |
| `sla_total_paused_seconds` | `integer` (`int32`) | `required` |  |
| `source_route` | `string` \| `null` | `required` |  |
| `status` | `string` | `required` |  |
| `study_id` | `string` \| `null` | `required` |  |
| `subject_id` | `string` \| `null` | `required` |  |
| `title` | `string` | `required` |  |
| `updated_at` | `string` (`date-time`) | `required` |  |
| `version_index` | `integer` (`int32`) | `required` |  |

</details>

### `Tickets_TransitionTicketRequestDto` {#schema-tickets-transitionticketrequestdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `assignee_user` | `string` \| `null` | `optional` |  |
| `resolution_code` | `string` \| `null` | `optional` |  |
| `root_cause_category` | `string` \| `null` | `optional` |  |
| `root_cause_summary` | `string` \| `null` | `optional` |  |
| `target_status` | `string` | `required` |  |

</details>

### `Tickets_UpdateTicketRequestDto` {#schema-tickets-updateticketrequestdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (9)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `assignee_user` | `string` \| `null` | `optional` |  |
| `description` | `string` \| `null` | `optional` |  |
| `expected_version_index` | `integer` (`int32`) \| `null` | `optional` |  |
| `priority` | `string` \| `null` | `optional` |  |
| `resolution_code` | `string` \| `null` | `optional` |  |
| `root_cause_category` | `string` \| `null` | `optional` |  |
| `root_cause_summary` | `string` \| `null` | `optional` |  |
| `status` | `string` \| `null` | `optional` |  |
| `title` | `string` \| `null` | `optional` |  |

</details>

