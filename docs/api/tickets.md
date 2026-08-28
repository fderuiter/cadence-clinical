# Clinical Issue Hub & Query Escalation

> **Functional Domain:** `Clinical Operations & Execution`  
> **Service Host Port:** `localhost:8012`  
> **Gateway Proxy Prefix:** `/api/v1/tickets`  
> **Persistence Backend:** `PostgreSQL / SQLite`  

## Overview

Clinical query escalation, data management issue tracking, dual-target SLA timers, and controlled reopening windows.

---

## Endpoints Summary

| Method | Path | Summary | GxP Auth & Signatures |
| :--- | :--- | :--- | :--- |
| `POST` | [/api/v1/compliance/change-requests/analyze-diff](#post-api-v1-compliance-change-requests-analyze-diff) | Analyze Diff Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/tickets](#post-api-v1-tickets) | Create Ticket | ✍️ Audited Write |
| `GET` | [/api/v1/tickets](#get-api-v1-tickets) | List Tickets | 👀 Read-Only |
| `GET` | [/api/v1/tickets/analytics/kpi](#get-api-v1-tickets-analytics-kpi) | Get Ticket Kpis | 👀 Read-Only |
| `GET` | [/api/v1/tickets/audit-logs](#get-api-v1-tickets-audit-logs) | List Ticket Audit Logs | 👀 Read-Only |
| `POST` | [/api/v1/tickets/cross-app/events](#post-api-v1-tickets-cross-app-events) | Create Cross App Ticket | ✍️ Audited Write |
| `GET` | [/api/v1/tickets/export/audit-trail](#get-api-v1-tickets-export-audit-trail) | Export Audit Trail | 👀 Read-Only |
| `POST` | [/api/v1/tickets/rag-triage/preview](#post-api-v1-tickets-rag-triage-preview) | Preview Grounded Protocol RAG support triage before ticket submission | ✍️ Audited Write |
| `GET` | [/api/v1/tickets/{id}](#get-api-v1-tickets-id) | Get Ticket | 👀 Read-Only |
| `PUT` | [/api/v1/tickets/{id}](#put-api-v1-tickets-id) | Update Ticket | ✍️ Audited Write |
| `POST` | [/api/v1/tickets/{id}/assign](#post-api-v1-tickets-id-assign) | Assign Ticket | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/tickets/{id}/attachments](#post-api-v1-tickets-id-attachments) | Upload Ticket Attachment | ✍️ Audited Write |
| `GET` | [/api/v1/tickets/{id}/attachments](#get-api-v1-tickets-id-attachments) | List Ticket Attachments | 👀 Read-Only |
| `POST` | [/api/v1/tickets/{id}/comments](#post-api-v1-tickets-id-comments) | Create Ticket Comment | ✍️ Audited Write |
| `GET` | [/api/v1/tickets/{id}/comments](#get-api-v1-tickets-id-comments) | List Ticket Comments | 👀 Read-Only |
| `POST` | [/api/v1/tickets/{id}/sign](#post-api-v1-tickets-id-sign) | Sign Ticket | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/tickets/{id}/transition](#post-api-v1-tickets-id-transition) | Transition Ticket | ✍️ Audited Write |
| `POST` | [/api/v1/tickets/{ticket_id}/rag-triage](#post-api-v1-tickets-ticket_id-rag-triage) | Execute Grounded Protocol RAG support triage on an existing ticket | ✍️ Audited Write |
| `GET` | [/health](#get-health) | Health Check | 👀 Read-Only |

---

## Granular Endpoint Specifications

### `POST` `/api/v1/compliance/change-requests/analyze-diff`

**Summary:** Analyze Diff Endpoint  
**Operation ID:** `analyze_diff_endpoint_api_v1_compliance_change_requests_analyze_diff_post`  

**Clinical & GxP Rationale:**  
Analyze proposed setting change and evaluate GxP regulatory risk level.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `SettingDiffEntry`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `setting_key` | `string` | ✔ Yes | — |
| `old_value` | `string` | ✔ Yes | — |
| `new_value` | `string` | ✔ Yes | — |
| `data_type` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `RegulatoryRiskAssessment` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/tickets/api/v1/compliance/change-requests/analyze-diff', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/tickets/api/v1/compliance/change-requests/analyze-diff',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/tickets/api/v1/compliance/change-requests/analyze-diff' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/tickets`

**Summary:** Create Ticket  
**Operation ID:** `create_ticket_api_v1_tickets_post`  

**Clinical & GxP Rationale:**  
Create and persist a new Ticket record with SLA computation and audit logging.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `TicketCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `title` | `string` | ✔ Yes | Title of the support ticket |
| `description` | `string` | ✔ Yes | Detailed description of the issue |
| `category` | `TicketCategory` | No | Category of the ticket |
| `priority` | `TicketPriority` | No | Priority level of the ticket |
| `gxp_severity` | `GxPSeverity` | No | GxP compliance severity impact |
| `reporter` | `string` | `null` | No | Reporter of the ticket |
| `assignee_user` | `string` | `null` | No | Assigned user |
| `assignee_role` | `string` | `null` | No | Assigned role |
| `org_id` | `string` | `null` | No | Scope organization ID |
| `site_id` | `string` | `null` | No | Scope site ID |
| `study_id` | `string` | `null` | No | Scope study ID |
| `related_entity_type` | `string` | `null` | No | Related entity type |
| `related_entity_id` | `string` | `null` | No | Related entity ID |
| `context_payload` | `string` | `null` | No | Serialized contextual metadata JSON |
| `due_date` | `string` | `null` | No | Optional due date |
| `custom_sla_hours` | `integer` | `null` | No | Optional study-specific SLA target override in hours |
| `workflow_type` | `string` | `null` | No | Workflow type |
| `action_type` | `string` | `null` | No | Action type |
| `signature_action` | `string` | `null` | No | Signature action |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `TicketResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/tickets/api/v1/tickets', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/tickets/api/v1/tickets',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/tickets/api/v1/tickets' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/tickets`

**Summary:** List Tickets  
**Operation ID:** `list_tickets_api_v1_tickets_get`  

**Clinical & GxP Rationale:**  
List and filter tickets with pagination and scope-awareness.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `status` | `query` | `TicketStatus` | `null` | No | — |
| `category` | `query` | `TicketCategory` | `null` | No | — |
| `priority` | `query` | `TicketPriority` | `null` | No | — |
| `gxp_severity` | `query` | `GxPSeverity` | `null` | No | — |
| `search` | `query` | `string` | `null` | No | — |
| `reporter` | `query` | `string` | `null` | No | — |
| `assignee` | `query` | `string` | `null` | No | — |
| `org_id` | `query` | `string` | `null` | No | — |
| `site_id` | `query` | `string` | `null` | No | — |
| `study_id` | `query` | `string` | `null` | No | — |
| `include_deleted` | `query` | `boolean` | No | — |
| `limit` | `query` | `integer` | No | — |
| `offset` | `query` | `integer` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<TicketResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/tickets/api/v1/tickets', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/tickets/api/v1/tickets',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/tickets/api/v1/tickets' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/tickets/analytics/kpi`

**Summary:** Get Ticket Kpis  
**Operation ID:** `get_ticket_kpis_api_v1_tickets_analytics_kpi_get`  

**Clinical & GxP Rationale:**  
Retrieve real-time clinical KPI and KRI metrics.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `org_id` | `query` | `string` | `null` | No | — |
| `site_id` | `query` | `string` | `null` | No | — |
| `study_id` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `TicketKPISummaryResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/tickets/api/v1/tickets/analytics/kpi', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/tickets/api/v1/tickets/analytics/kpi',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/tickets/api/v1/tickets/analytics/kpi' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/tickets/audit-logs`

**Summary:** List Ticket Audit Logs  
**Operation ID:** `list_ticket_audit_logs_api_v1_tickets_audit_logs_get`  

**Clinical & GxP Rationale:**  
Retrieve ticket audit logs in descending chronological order.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `ticket_id` | `query` | `string` | `null` | No | — |
| `limit` | `query` | `integer` | No | — |
| `offset` | `query` | `integer` | No | — |
| `start_time` | `query` | `string` | `null` | No | — |
| `end_time` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `PaginatedTicketAuditLogResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/tickets/api/v1/tickets/audit-logs', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/tickets/api/v1/tickets/audit-logs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/tickets/api/v1/tickets/audit-logs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/tickets/cross-app/events`

**Summary:** Create Cross App Ticket  
**Operation ID:** `create_cross_app_ticket_api_v1_tickets_cross_app_events_post`  

**Clinical & GxP Rationale:**  
Automated REST ingestion endpoint for sibling microservices (EDC, Safety, CTMS, Quality).
Creates a ticket linked directly to domain entity with full contextual metadata.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CrossAppEventCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `event_type` | `string` | ✔ Yes | Event type, e.g. PROTOCOL_DEVIATION, DATA_QUERY, SAFETY_ALERT |
| `title` | `string` | ✔ Yes | Ticket title |
| `description` | `string` | ✔ Yes | Ticket description |
| `category` | `TicketCategory` | No | Ticket category |
| `priority` | `TicketPriority` | No | Priority level |
| `gxp_severity` | `GxPSeverity` | No | GxP Severity rating |
| `source_service` | `string` | ✔ Yes | Source microservice, e.g. execution, safety, ctms, quality |
| `study_id` | `string` | `null` | No | Study scope ID |
| `site_id` | `string` | `null` | No | Site scope ID |
| `related_entity_type` | `string` | ✔ Yes | Linked entity type (e.g. Subject, Form, AE, CAPA) |
| `related_entity_id` | `string` | ✔ Yes | Linked entity primary key |
| `context_payload` | `object` | `null` | No | Structured contextual JSON payload |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `TicketResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/tickets/api/v1/tickets/cross-app/events', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/tickets/api/v1/tickets/cross-app/events',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/tickets/api/v1/tickets/cross-app/events' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/tickets/export/audit-trail`

**Summary:** Export Audit Trail  
**Operation ID:** `export_audit_trail_api_v1_tickets_export_audit_trail_get`  

**Clinical & GxP Rationale:**  
Export regulatory Part 11 immutable audit trail records in JSON or CSV format.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `format` | `query` | `string` | No | — |
| `ticket_id` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `any` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/tickets/api/v1/tickets/export/audit-trail', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/tickets/api/v1/tickets/export/audit-trail',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/tickets/api/v1/tickets/export/audit-trail' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/tickets/rag-triage/preview`

**Summary:** Preview Grounded Protocol RAG support triage before ticket submission  
**Operation ID:** `preview_ticket_rag_triage_api_v1_tickets_rag_triage_preview_post`  

**Clinical & GxP Rationale:**  
Preview Grounded Protocol RAG Support Triage before ticket submission.

Requirements: PRD-TCK-005, PRD-SYS-051

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `RAGPreviewRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `query` | `string` | ✔ Yes | Support inquiry text |
| `study_id` | `string` | ✔ Yes | Study scope identifier |
| `protocol_version` | `string` | `null` | No | Optional protocol version filter |
| `top_k` | `integer` | No | Max candidate chunks to evaluate |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/tickets/api/v1/tickets/rag-triage/preview', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/tickets/api/v1/tickets/rag-triage/preview',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/tickets/api/v1/tickets/rag-triage/preview' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/tickets/{id}`

**Summary:** Get Ticket  
**Operation ID:** `get_ticket_api_v1_tickets__id__get`  

**Clinical & GxP Rationale:**  
Retrieve a specific ticket record by its ID or sequential reference.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `TicketResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/tickets/api/v1/tickets/{id}', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/tickets/api/v1/tickets/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/tickets/api/v1/tickets/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/tickets/{id}`

**Summary:** Update Ticket  
**Operation ID:** `update_ticket_api_v1_tickets__id__put`  

**Clinical & GxP Rationale:**  
Update a specific ticket record by its ID or reference with optimistic locking and transitions.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `TicketUpdate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `title` | `string` | `null` | No | Updated title |
| `description` | `string` | `null` | No | Updated description |
| `category` | `TicketCategory` | `null` | No | Updated category |
| `priority` | `TicketPriority` | `null` | No | Updated priority |
| `gxp_severity` | `GxPSeverity` | `null` | No | Updated GxP severity |
| `status` | `TicketStatus` | `null` | No | Updated status |
| `assignee_user` | `string` | `null` | No | Updated assigned user |
| `assignee_role` | `string` | `null` | No | Updated assigned role |
| `org_id` | `string` | `null` | No | Updated organization scope |
| `site_id` | `string` | `null` | No | Updated site scope |
| `study_id` | `string` | `null` | No | Updated study scope |
| `related_entity_type` | `string` | `null` | No | Updated related entity type |
| `related_entity_id` | `string` | `null` | No | Updated related entity ID |
| `context_payload` | `string` | `null` | No | Updated contextual payload |
| `due_date` | `string` | `null` | No | Updated due date |
| `is_deleted` | `boolean` | `null` | No | Soft delete state |
| `version_index` | `integer` | `null` | No | Expected version index for optimistic locking |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `TicketResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/tickets/api/v1/tickets/{id}', {
  method: 'PUT',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.put(
        '/api/v1/tickets/api/v1/tickets/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/tickets/api/v1/tickets/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/tickets/{id}/assign`

**Summary:** Assign Ticket  
**Operation ID:** `assign_ticket_api_v1_tickets__id__assign_post`  

**Clinical & GxP Rationale:**  
Assign a support ticket to an individual user and/or role-based routing target explicitly.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `TicketAssignPayload`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `assignee_user` | `string` | `null` | No | Username of the assignee |
| `assignee_role` | `string` | `null` | No | Role-based routing target |
| `version_index` | `integer` | ✔ Yes | Expected version index for optimistic locking |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `TicketResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/tickets/api/v1/tickets/{id}/assign', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/tickets/api/v1/tickets/{id}/assign',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/tickets/api/v1/tickets/{id}/assign' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/tickets/{id}/attachments`

**Summary:** Upload Ticket Attachment  
**Operation ID:** `upload_ticket_attachment_api_v1_tickets__id__attachments_post`  

**Clinical & GxP Rationale:**  
Upload an audited evidence attachment with SHA-256 integrity hash.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `any`

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `AttachmentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/tickets/api/v1/tickets/{id}/attachments', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/tickets/api/v1/tickets/{id}/attachments',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/tickets/api/v1/tickets/{id}/attachments' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/tickets/{id}/attachments`

**Summary:** List Ticket Attachments  
**Operation ID:** `list_ticket_attachments_api_v1_tickets__id__attachments_get`  

**Clinical & GxP Rationale:**  
List all attachments for a ticket.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<AttachmentResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/tickets/api/v1/tickets/{id}/attachments', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/tickets/api/v1/tickets/{id}/attachments',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/tickets/api/v1/tickets/{id}/attachments' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/tickets/{id}/comments`

**Summary:** Create Ticket Comment  
**Operation ID:** `create_ticket_comment_api_v1_tickets__id__comments_post`  

**Clinical & GxP Rationale:**  
Append an auditable comment/note to a specific ticket with public or internal visibility.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `CommentCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `body` | `string` | ✔ Yes | The comment body text |
| `visibility` | `CommentVisibility` | No | Visibility boundary for comment |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `CommentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/tickets/api/v1/tickets/{id}/comments', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/tickets/api/v1/tickets/{id}/comments',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/tickets/api/v1/tickets/{id}/comments' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/tickets/{id}/comments`

**Summary:** List Ticket Comments  
**Operation ID:** `list_ticket_comments_api_v1_tickets__id__comments_get`  

**Clinical & GxP Rationale:**  
Retrieve comments for a ticket filtered by role persona visibility boundaries.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<CommentResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/tickets/api/v1/tickets/{id}/comments', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/tickets/api/v1/tickets/{id}/comments',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/tickets/api/v1/tickets/{id}/comments' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/tickets/{id}/sign`

**Summary:** Sign Ticket  
**Operation ID:** `sign_ticket_api_v1_tickets__id__sign_post`  

**Clinical & GxP Rationale:**  
Capture a 21 CFR Part 11 Electronic Signature on a ticket.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `TicketSignaturePayload`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `signature_token` | `string` | ✔ Yes | Cryptographic signature token |
| `meaning` | `string` | ✔ Yes | Signature meaning statement |
| `version_index` | `integer` | ✔ Yes | Expected version index for optimistic locking |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `TicketResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/tickets/api/v1/tickets/{id}/sign', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/tickets/api/v1/tickets/{id}/sign',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/tickets/api/v1/tickets/{id}/sign' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/tickets/{id}/transition`

**Summary:** Transition Ticket  
**Operation ID:** `transition_ticket_api_v1_tickets__id__transition_post`  

**Clinical & GxP Rationale:**  
Transition a ticket's status explicitly with optimistic locking, RCA check, and SLA pause handling.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `TicketTransitionPayload`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `status` | `TicketStatus` | ✔ Yes | Target status for the lifecycle transition |
| `version_index` | `integer` | ✔ Yes | Expected version index for optimistic locking |
| `root_cause_category` | `RootCauseCategory` | `null` | No | RCA 5-Whys classification (mandatory for Major/Critical closure) |
| `root_cause_summary` | `string` | `null` | No | Detailed root cause explanation |
| `resolution_code` | `ResolutionCode` | `null` | No | Formal resolution outcome code |
| `signature_token` | `string` | `null` | No | 21 CFR Part 11 signature token |
| `signature_meaning` | `string` | `null` | No | Electronic signature justification meaning |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `TicketResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/tickets/api/v1/tickets/{id}/transition', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/tickets/api/v1/tickets/{id}/transition',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/tickets/api/v1/tickets/{id}/transition' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/tickets/{ticket_id}/rag-triage`

**Summary:** Execute Grounded Protocol RAG support triage on an existing ticket  
**Operation ID:** `triage_ticket_rag_api_v1_tickets__ticket_id__rag_triage_post`  

**Clinical & GxP Rationale:**  
Execute Grounded Protocol RAG Support Triage on an existing ticket.

Applies strict 85% confidence gating:
- >= 85%: attaches DRAFT_AI suggestion with exact page/section citations.
- < 85%: suppresses auto-draft and fails closed to human Data Manager queue.

Requirements: PRD-TCK-005, PRD-SYS-051

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `ticket_id` | `path` | `string` | ✔ Yes | — |
| `top_k` | `query` | `integer` | No | — |
| `protocol_version` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `RAGTriageResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/tickets/api/v1/tickets/{ticket_id}/rag-triage', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
  body: JSON.stringify(payload),
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.post(
        '/api/v1/tickets/api/v1/tickets/{ticket_id}/rag-triage',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/tickets/api/v1/tickets/{ticket_id}/rag-triage' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/health`

**Summary:** Health Check  
**Operation ID:** `health_check_health_get`  

**Clinical & GxP Rationale:**  
Service health check endpoint.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/tickets/health', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
    'X-Change-Reason': 'Clinical routine data capture',
  },
});
const data = await response.json();
```

```python [Python (HTTPX)]
import httpx

async with httpx.AsyncClient(base_url='http://localhost:8000') as client:
    response = await client.get(
        '/api/v1/tickets/health',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/tickets/health' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---
