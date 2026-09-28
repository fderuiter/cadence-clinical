# Safety & Pharmacovigilance Gateway

> **Functional Domain:** `Safety, Quality & Governance`  
> **Service Host Port:** `localhost:8009`  
> **Gateway Proxy Prefix:** `/api/v1/safety`  
> **Persistence Backend:** `PostgreSQL / SQLite`  

## Overview

ICH E2B(R3) Individual Case Safety Reports (ICSR) XML compilation, NCI-CTCAE v5.0 toxicity grading, and B2B AS2 S/MIME regulatory EDI gateway.

---

## Endpoints Summary

| Method | Path | Summary | GxP Auth & Signatures |
| :--- | :--- | :--- | :--- |
| `POST` | [/api/v1/safety/as2/ack](#post-api-v1-safety-as2-ack) | Process Regulatory Ack Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/safety/as2/mdn](#post-api-v1-safety-as2-mdn) | Process Inbound Mdn Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/safety/as2/transmissions](#post-api-v1-safety-as2-transmissions) | Send As2 Transmission Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/safety/as2/transmissions](#get-api-v1-safety-as2-transmissions) | List As2 Transmissions Endpoint | 👀 Read-Only |
| `GET` | [/api/v1/safety/as2/transmissions/{id}](#get-api-v1-safety-as2-transmissions-id) | Get As2 Transmission Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/safety/as2/transmissions/{id}/retry](#post-api-v1-safety-as2-transmissions-id-retry) | Retry As2 Transmission Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/safety/audit-logs](#get-api-v1-safety-audit-logs) | List Safety Audit Logs | 👀 Read-Only |
| `POST` | [/api/v1/safety/cases](#post-api-v1-safety-cases) | Create Safety Case | ✍️ Audited Write |
| `GET` | [/api/v1/safety/cases](#get-api-v1-safety-cases) | List Safety Cases | 👀 Read-Only |
| `GET` | [/api/v1/safety/cases/{id}](#get-api-v1-safety-cases-id) | Get Safety Case | 👀 Read-Only |
| `POST` | [/api/v1/safety/device-investigations](#post-api-v1-safety-device-investigations) | Open Device Safety Investigation Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/safety/device-investigations](#get-api-v1-safety-device-investigations) | List Device Safety Investigations Endpoint | 👀 Read-Only |
| `GET` | [/api/v1/safety/device-investigations/{investigation_id}](#get-api-v1-safety-device-investigations-investigation_id) | Get Device Safety Investigation Endpoint | 👀 Read-Only |
| `PUT` | [/api/v1/safety/device-investigations/{investigation_id}/adjudicate](#put-api-v1-safety-device-investigations-investigation_id-adjudicate) | Adjudicate Device Safety Investigation Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/safety/device-investigations/{investigation_id}/fda-medwatch-report](#post-api-v1-safety-device-investigations-investigation_id-fda-medwatch-report) | Generate Device Fda Medwatch Report Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/safety/device-investigations/{investigation_id}/regulatory-report](#post-api-v1-safety-device-investigations-investigation_id-regulatory-report) | Generate Device Vigilance Report Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/safety/export](#post-api-v1-safety-export) | Export Safety Case | ✍️ Audited Write |
| `GET` | [/api/v1/safety/export-jobs](#get-api-v1-safety-export-jobs) | List Export Jobs | 👀 Read-Only |
| `POST` | [/api/v1/safety/export-jobs](#post-api-v1-safety-export-jobs) | Create Export Job | ✍️ Audited Write |
| `GET` | [/api/v1/safety/export-jobs/{id}](#get-api-v1-safety-export-jobs-id) | Get Export Job | 👀 Read-Only |
| `POST` | [/api/v1/safety/exposure/calculate](#post-api-v1-safety-exposure-calculate) | Calculate Subject Exposure Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/safety/narratives](#get-api-v1-safety-narratives) | List Safety Narratives Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/safety/narratives/generate](#post-api-v1-safety-narratives-generate) | Generate Safety Narrative Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/safety/narratives/{id}](#get-api-v1-safety-narratives-id) | Get Safety Narrative Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/safety/narratives/{id}/export-e2b](#post-api-v1-safety-narratives-id-export-e2b) | Export Safety Narrative E2B Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/safety/narratives/{id}/sign](#post-api-v1-safety-narratives-id-sign) | Sign Safety Narrative Endpoint | 🔒 Step-Up Sig Token |
| `GET` | [/api/v1/safety/periodic-reports](#get-api-v1-safety-periodic-reports) | List Periodic Safety Reports Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/safety/periodic-reports/compile](#post-api-v1-safety-periodic-reports-compile) | Compile Periodic Safety Report Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/safety/periodic-reports/{id}](#get-api-v1-safety-periodic-reports-id) | Get Periodic Safety Report Endpoint | 👀 Read-Only |
| `GET` | [/api/v1/safety/periodic-reports/{id}/export](#get-api-v1-safety-periodic-reports-id-export) | Export Periodic Safety Report Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/safety/periodic-reports/{id}/qppv-sign](#post-api-v1-safety-periodic-reports-id-qppv-sign) | Qppv Sign Periodic Safety Report Endpoint | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/safety/periodic-reports/{id}/sign](#post-api-v1-safety-periodic-reports-id-sign) | Sign Periodic Safety Report Endpoint | 🔒 Step-Up Sig Token |
| `GET` | [/api/v1/safety/periodic-reports/{id}/xml](#get-api-v1-safety-periodic-reports-id-xml) | Export Periodic Safety Report Xml Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/safety/reconciliation/jobs](#post-api-v1-safety-reconciliation-jobs) | Trigger Sae Reconciliation Job | ✍️ Audited Write |
| `GET` | [/api/v1/safety/reconciliation/jobs](#get-api-v1-safety-reconciliation-jobs) | List Reconciliation Jobs | 👀 Read-Only |
| `GET` | [/api/v1/safety/reconciliation/jobs/{job_id}](#get-api-v1-safety-reconciliation-jobs-job_id) | Get Reconciliation Job | 👀 Read-Only |
| `GET` | [/api/v1/safety/reconciliation/runs](#get-api-v1-safety-reconciliation-runs) | List Reconciliation Runs | 👀 Read-Only |
| `POST` | [/api/v1/safety/reconciliation/runs](#post-api-v1-safety-reconciliation-runs) | Trigger Sae Reconciliation | ✍️ Audited Write |
| `GET` | [/api/v1/safety/reconciliation/runs/{id}](#get-api-v1-safety-reconciliation-runs-id) | Get Reconciliation Run | 👀 Read-Only |
| `GET` | [/api/v1/safety/rules/countdown-alerts](#get-api-v1-safety-rules-countdown-alerts) | Get Safety Countdown Alerts Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/safety/rules/evaluate](#post-api-v1-safety-rules-evaluate) | Evaluate Expedited Safety Rules Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/safety/rules/evaluations](#get-api-v1-safety-rules-evaluations) | List Expedited Safety Evaluations Endpoint | 👀 Read-Only |
| `GET` | [/api/v1/safety/rules/evaluations/{id}](#get-api-v1-safety-rules-evaluations-id) | Get Expedited Safety Evaluation Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/safety/rules/evaluations/{id}/sign](#post-api-v1-safety-rules-evaluations-id-sign) | Sign Expedited Safety Evaluation Endpoint | 🔒 Step-Up Sig Token |
| `GET` | [/api/v1/safety/signals/alerts](#get-api-v1-safety-signals-alerts) | List Signal Alerts Endpoint | 👀 Read-Only |
| `GET` | [/api/v1/safety/signals/alerts/{id}](#get-api-v1-safety-signals-alerts-id) | Get Signal Alert Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/safety/signals/alerts/{id}/review](#post-api-v1-safety-signals-alerts-id-review) | Review Signal Alert Endpoint | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/safety/signals/alerts/{id}/sign](#post-api-v1-safety-signals-alerts-id-sign) | Sign Signal Alert Endpoint | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/safety/signals/detect](#post-api-v1-safety-signals-detect) | Execute Signal Detection Scan Endpoint | 🔒 Step-Up Sig Token |
| `GET` | [/api/v1/safety/signals/runs](#get-api-v1-safety-signals-runs) | List Signal Detection Runs Endpoint | 👀 Read-Only |
| `GET` | [/api/v1/safety/signals/runs/{id}](#get-api-v1-safety-signals-runs-id) | Get Signal Detection Run Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/safety/snapshots/lock](#post-api-v1-safety-snapshots-lock) | Lock Regulatory Snapshot Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/safety/snapshots/verify](#post-api-v1-safety-snapshots-verify) | Verify Regulatory Snapshot Endpoint | ✍️ Audited Write |
| `GET` | [/health](#get-health) | Health Check | 👀 Read-Only |

---

## Granular Endpoint Specifications

### `POST` `/api/v1/safety/as2/ack`

**Summary:** Process Regulatory Ack Endpoint  
**Operation ID:** `process_regulatory_ack_endpoint_api_v1_safety_as2_ack_post`  

**Clinical & GxP Rationale:**  
Process an inbound Regulatory ACK B (Syntactic) or ACK C (Authority Ingestion) receipt.

Requirements: PRD-PVG-002, PRD-ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `RegulatoryAckProcessRequestDTO`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `ack_payload` | `string` | ✔ Yes | ACK B or ACK C XML payload string |
| `ack_type` | `AckType` | ✔ Yes | Acknowledgement type: ACK_B (syntactic) or ACK_C (authority acceptance) |
| `transmission_id` | `string` | `null` | No | Optional transmission ID to associate directly |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `RegulatoryAckProcessResponseDTO` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/as2/ack', {
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
        '/api/v1/safety/api/v1/safety/as2/ack',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/safety/api/v1/safety/as2/ack' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/safety/as2/mdn`

**Summary:** Process Inbound Mdn Endpoint  
**Operation ID:** `process_inbound_mdn_endpoint_api_v1_safety_as2_mdn_post`  

**Clinical & GxP Rationale:**  
Process an asynchronous Message Disposition Notification (MDN) callback.

Requirements: PRD-PVG-002, PRD-ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `MDNProcessRequestDTO`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `mdn_payload` | `string` | ✔ Yes | Raw MDN multipart MIME or text body |
| `authority_cert_pem` | `string` | `null` | No | Optional sender certificate for signature verification |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `MDNProcessResponseDTO` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/as2/mdn', {
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
        '/api/v1/safety/api/v1/safety/as2/mdn',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/safety/api/v1/safety/as2/mdn' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/safety/as2/transmissions`

**Summary:** Send As2 Transmission Endpoint  
**Operation ID:** `send_as2_transmission_endpoint_api_v1_safety_as2_transmissions_post`  

**Clinical & GxP Rationale:**  
Dispatch an outbound ICH E2B(R3) XML payload directly to a Regulatory Health Authority AS2 Gateway.

Requirements: PRD-PVG-002, PRD-ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `AS2OutboundTransmissionRequestDTO`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `case_id` | `string` | `null` | No | Internal Safety Case ID |
| `study_id` | `string` | `null` | No | Clinical Study Protocol ID |
| `icsr_xml_payload` | `string` | ✔ Yes | ICH E2B(R3) ICSR XML payload string |
| `target_authority` | `AS2Authority` | ✔ Yes | Target Regulatory Health Authority (e.g. FDA_ESG, EMA_GATEWAY) |
| `sender_as2_id` | `string` | No | Sender AS2 Identifier |
| `receiver_as2_id` | `string` | `null` | No | Receiver AS2 Identifier |
| `environment` | `string` | No | Environment (PRODUCTION / TEST) |
| `custom_headers` | `object` | No | Custom AS2 / HTTP headers |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `AS2TransmissionResponseDTO` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/as2/transmissions', {
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
        '/api/v1/safety/api/v1/safety/as2/transmissions',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/safety/api/v1/safety/as2/transmissions' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/safety/as2/transmissions`

**Summary:** List As2 Transmissions Endpoint  
**Operation ID:** `list_as2_transmissions_endpoint_api_v1_safety_as2_transmissions_get`  

**Clinical & GxP Rationale:**  
List append-only AS2 B2B transmission ledger records with optional filters.

Requirements: PRD-PVG-002, PRD-ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `target_authority` | `query` | `string` | `null` | No | — |
| `status` | `query` | `string` | `null` | No | — |
| `study_id` | `query` | `string` | `null` | No | — |
| `case_id` | `query` | `string` | `null` | No | — |
| `limit` | `query` | `integer` | No | — |
| `offset` | `query` | `integer` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<AS2TransmissionResponseDTO>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/as2/transmissions', {
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
        '/api/v1/safety/api/v1/safety/as2/transmissions',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/api/v1/safety/as2/transmissions' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/safety/as2/transmissions/{id}`

**Summary:** Get As2 Transmission Endpoint  
**Operation ID:** `get_as2_transmission_endpoint_api_v1_safety_as2_transmissions__id__get`  

**Clinical & GxP Rationale:**  
Retrieve full audit history and status of an AS2 B2B transmission by ID.

Requirements: PRD-PVG-002, PRD-ADR-2199

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
| `200` | Successful Response | `AS2TransmissionResponseDTO` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/as2/transmissions/{id}', {
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
        '/api/v1/safety/api/v1/safety/as2/transmissions/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/api/v1/safety/as2/transmissions/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/safety/as2/transmissions/{id}/retry`

**Summary:** Retry As2 Transmission Endpoint  
**Operation ID:** `retry_as2_transmission_endpoint_api_v1_safety_as2_transmissions__id__retry_post`  

**Clinical & GxP Rationale:**  
Retry a failed AS2 transmission.

Requirements: PRD-PVG-002, PRD-ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `AS2TransmissionResponseDTO` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/as2/transmissions/{id}/retry', {
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
        '/api/v1/safety/api/v1/safety/as2/transmissions/{id}/retry',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/safety/api/v1/safety/as2/transmissions/{id}/retry' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/safety/audit-logs`

**Summary:** List Safety Audit Logs  
**Operation ID:** `list_safety_audit_logs_api_v1_safety_audit_logs_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<SafetyAuditLogResponse>` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/audit-logs', {
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
        '/api/v1/safety/api/v1/safety/audit-logs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/api/v1/safety/audit-logs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/safety/cases`

**Summary:** Create Safety Case  
**Operation ID:** `create_safety_case_api_v1_safety_cases_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `SafetyCaseICSRCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `worldwide_unique_case_id` | `string` | ✔ Yes | Worldwide unique identifier for this safety case |
| `patient_id` | `string` | ✔ Yes | Unique subject/patient identifier |
| `case_data` | `object` | ✔ Yes | The structured ICSR case JSON payload |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `SafetyCaseICSRResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/cases', {
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
        '/api/v1/safety/api/v1/safety/cases',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/safety/api/v1/safety/cases' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/safety/cases`

**Summary:** List Safety Cases  
**Operation ID:** `list_safety_cases_api_v1_safety_cases_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `patient_id` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<SafetyCaseICSRResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/cases', {
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
        '/api/v1/safety/api/v1/safety/cases',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/api/v1/safety/cases' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/safety/cases/{id}`

**Summary:** Get Safety Case  
**Operation ID:** `get_safety_case_api_v1_safety_cases__id__get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

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
| `200` | Successful Response | `SafetyCaseICSRResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/cases/{id}', {
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
        '/api/v1/safety/api/v1/safety/cases/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/api/v1/safety/cases/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/safety/device-investigations`

**Summary:** Open Device Safety Investigation Endpoint  
**Operation ID:** `open_device_safety_investigation_endpoint_api_v1_safety_device_investigations_post`  

**Clinical & GxP Rationale:**  
Opens a new Adverse Device Effect (ADE / SADE / USADE) safety investigation case.

Calculates vigilance reporting deadlines according to ISO 14155 and EU MDR / IVDR.

Requirements: PRD-SYS-126, PRD-DEV-003, ISO 14155:2020, PRD-ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `DeviceSafetyInvestigationCreateDTO`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `case_number` | `string` | `null` | No | — |
| `study_id` | `string` | ✔ Yes | — |
| `site_id` | `string` | ✔ Yes | — |
| `subject_id` | `string` | `null` | No | — |
| `deficiency_id` | `string` | ✔ Yes | — |
| `device_name` | `string` | No | — |
| `device_udi_di` | `string` | ✔ Yes | — |
| `lot_number` | `string` | ✔ Yes | — |
| `serial_number` | `string` | `null` | No | — |
| `classification` | `string` | No | — |
| `causality` | `string` | No | — |
| `is_usade` | `boolean` | No | — |
| `investigation_notes` | `string` | `null` | No | — |
| `reason_for_change` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `DeviceSafetyInvestigationResponseDTO` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/device-investigations', {
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
        '/api/v1/safety/api/v1/safety/device-investigations',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/safety/api/v1/safety/device-investigations' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/safety/device-investigations`

**Summary:** List Device Safety Investigations Endpoint  
**Operation ID:** `list_device_safety_investigations_endpoint_api_v1_safety_device_investigations_get`  

**Clinical & GxP Rationale:**  
Lists device safety investigation cases matching query parameters.

Requirements: PRD-SYS-126, PRD-DEV-003, PRD-ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | — |
| `site_id` | `query` | `string` | `null` | No | — |
| `classification` | `query` | `string` | `null` | No | — |
| `status` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<DeviceSafetyInvestigationResponseDTO>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/device-investigations', {
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
        '/api/v1/safety/api/v1/safety/device-investigations',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/api/v1/safety/device-investigations' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/safety/device-investigations/{investigation_id}`

**Summary:** Get Device Safety Investigation Endpoint  
**Operation ID:** `get_device_safety_investigation_endpoint_api_v1_safety_device_investigations__investigation_id__get`  

**Clinical & GxP Rationale:**  
Retrieves a single device safety investigation case by ID.

Requirements: PRD-SYS-126, PRD-DEV-003, PRD-ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `investigation_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DeviceSafetyInvestigationResponseDTO` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/device-investigations/{investigation_id}', {
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
        '/api/v1/safety/api/v1/safety/device-investigations/{investigation_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/api/v1/safety/device-investigations/{investigation_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/safety/device-investigations/{investigation_id}/adjudicate`

**Summary:** Adjudicate Device Safety Investigation Endpoint  
**Operation ID:** `adjudicate_device_safety_investigation_endpoint_api_v1_safety_device_investigations__investigation_id__adjudicate_put`  

**Clinical & GxP Rationale:**  
Formally adjudicates Adverse Device Effect causality and SADE determination with Part 11 e-signature.

Requirements: PRD-SYS-126, PRD-DEV-003, 21 CFR Part 11, ISO 14155:2020, PRD-ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `investigation_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `DeviceSafetyInvestigationAdjudicateDTO`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `classification` | `string` | ✔ Yes | — |
| `causality` | `string` | ✔ Yes | — |
| `is_usade` | `boolean` | No | — |
| `investigation_notes` | `string` | `null` | No | — |
| `root_cause_summary` | `string` | `null` | No | — |
| `capa_required` | `boolean` | No | — |
| `capa_reference` | `string` | `null` | No | — |
| `fsca_required` | `boolean` | No | — |
| `signature_token` | `string` | ✔ Yes | — |
| `reason_for_change` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DeviceSafetyInvestigationResponseDTO` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/device-investigations/{investigation_id}/adjudicate', {
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
        '/api/v1/safety/api/v1/safety/device-investigations/{investigation_id}/adjudicate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/safety/api/v1/safety/device-investigations/{investigation_id}/adjudicate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/safety/device-investigations/{investigation_id}/fda-medwatch-report`

**Summary:** Generate Device Fda Medwatch Report Endpoint  
**Operation ID:** `generate_device_fda_medwatch_report_endpoint_api_v1_safety_device_investigations__investigation_id__fda_medwatch_report_post`  

**Clinical & GxP Rationale:**  
Generates an FDA 21 CFR Part 803 Form 3500A (MedWatch) Electronic Report.

Requirements: PRD-SYS-187, PRD-SYS-053, FDA 21 CFR Part 803, 21 CFR Part 11

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `investigation_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DeviceMedWatchReportResponseDTO` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/device-investigations/{investigation_id}/fda-medwatch-report', {
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
        '/api/v1/safety/api/v1/safety/device-investigations/{investigation_id}/fda-medwatch-report',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/safety/api/v1/safety/device-investigations/{investigation_id}/fda-medwatch-report' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/safety/device-investigations/{investigation_id}/regulatory-report`

**Summary:** Generate Device Vigilance Report Endpoint  
**Operation ID:** `generate_device_vigilance_report_endpoint_api_v1_safety_device_investigations__investigation_id__regulatory_report_post`  

**Clinical & GxP Rationale:**  
Generates an EU MDR 2017/745 / EU IVDR 2017/746 Manufacturer Incident Report (MIR v7.2 JSON/XML).

Requirements: PRD-SYS-126, PRD-DEV-004, EU MDR 2017/745, EU IVDR 2017/746, PRD-ADR-2209

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `investigation_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DeviceVigilanceReportResponseDTO` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/device-investigations/{investigation_id}/regulatory-report', {
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
        '/api/v1/safety/api/v1/safety/device-investigations/{investigation_id}/regulatory-report',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/safety/api/v1/safety/device-investigations/{investigation_id}/regulatory-report' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/safety/export`

**Summary:** Export Safety Case  
**Operation ID:** `export_safety_case_api_v1_safety_export_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ICSRDataExportRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `job_name` | `string` | ✔ Yes | The descriptive name of the export job |
| `icsr` | `IndividualCaseSafetyReport` | ✔ Yes | The E2B ICSR report data |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `SafetyExportJobResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/export', {
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
        '/api/v1/safety/api/v1/safety/export',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/safety/api/v1/safety/export' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/safety/export-jobs`

**Summary:** List Export Jobs  
**Operation ID:** `list_export_jobs_api_v1_safety_export_jobs_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<SafetyExportJobResponse>` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/export-jobs', {
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
        '/api/v1/safety/api/v1/safety/export-jobs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/api/v1/safety/export-jobs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/safety/export-jobs`

**Summary:** Create Export Job  
**Operation ID:** `create_export_job_api_v1_safety_export_jobs_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `SafetyExportJobCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `job_name` | `string` | ✔ Yes | The descriptive name of the export job |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `SafetyExportJobResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/export-jobs', {
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
        '/api/v1/safety/api/v1/safety/export-jobs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/safety/api/v1/safety/export-jobs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/safety/export-jobs/{id}`

**Summary:** Get Export Job  
**Operation ID:** `get_export_job_api_v1_safety_export_jobs__id__get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

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
| `200` | Successful Response | `SafetyExportJobResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/export-jobs/{id}', {
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
        '/api/v1/safety/api/v1/safety/export-jobs/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/api/v1/safety/export-jobs/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/safety/exposure/calculate`

**Summary:** Calculate Subject Exposure Endpoint  
**Operation ID:** `calculate_subject_exposure_endpoint_api_v1_safety_exposure_calculate_post`  

**Clinical & GxP Rationale:**  
Calculate cumulative and interval patient-years of exposure.

Requirements: PRD-PVG-004, PRD-AGG-001, PRD-ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `SubjectExposureCalculationRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | — |
| `reporting_period_start` | `string` | ✔ Yes | — |
| `reporting_period_end` | `string` | ✔ Yes | — |
| `subjects_exposure_data` | `Array<object>` | `null` | No | — |
| `cases` | `Array<object>` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SubjectExposureCalculationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/exposure/calculate', {
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
        '/api/v1/safety/api/v1/safety/exposure/calculate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/safety/api/v1/safety/exposure/calculate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/safety/narratives`

**Summary:** List Safety Narratives Endpoint  
**Operation ID:** `list_safety_narratives_endpoint_api_v1_safety_narratives_get`  

**Clinical & GxP Rationale:**  
List safety narratives filtered by study, subject, or review status.

Requirements: PRD-SYS-052

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | — |
| `subject_id` | `query` | `string` | `null` | No | — |
| `review_status` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<SafetyNarrativeDTO>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/narratives', {
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
        '/api/v1/safety/api/v1/safety/narratives',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/api/v1/safety/narratives' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/safety/narratives/generate`

**Summary:** Generate Safety Narrative Endpoint  
**Operation ID:** `generate_safety_narrative_endpoint_api_v1_safety_narratives_generate_post`  

**Clinical & GxP Rationale:**  
Draft a new FDA MedWatch 3500A / CIOMS-I Serious Adverse Event narrative via Tier 3 model.

Requirements: PRD-SYS-051, PRD-SYS-052

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `NarrativeGenerateRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Clinical study identifier. |
| `subject_id` | `string` | ✔ Yes | De-identified subject key. |
| `sae_event_key` | `string` | ✔ Yes | Unique SAE event key (e.g. SUBJ-001:SEQ-1). |
| `worldwide_unique_case_id` | `string` | `null` | No | Optional external worldwide unique case ID. |
| `reason_for_change` | `string` | ✔ Yes | Mandatory GxP audit justification reason. |
| `additional_context` | `string` | `null` | No | Optional clinical guidance or focus instructions. |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `SafetyNarrativeDTO` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/narratives/generate', {
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
        '/api/v1/safety/api/v1/safety/narratives/generate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/safety/api/v1/safety/narratives/generate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/safety/narratives/{id}`

**Summary:** Get Safety Narrative Endpoint  
**Operation ID:** `get_safety_narrative_endpoint_api_v1_safety_narratives__id__get`  

**Clinical & GxP Rationale:**  
Retrieve safety narrative details, grounded claims, and review status.

Requirements: PRD-SYS-052

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
| `200` | Successful Response | `SafetyNarrativeDTO` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/narratives/{id}', {
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
        '/api/v1/safety/api/v1/safety/narratives/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/api/v1/safety/narratives/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/safety/narratives/{id}/export-e2b`

**Summary:** Export Safety Narrative E2B Endpoint  
**Operation ID:** `export_safety_narrative_e2b_endpoint_api_v1_safety_narratives__id__export_e2b_post`  

**Clinical & GxP Rationale:**  
Export an approved safety narrative into ICH E2B(R3) XML format.

Requires 21 CFR Part 11 approval status.

Requirements: PRD-SYS-052

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/narratives/{id}/export-e2b', {
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
        '/api/v1/safety/api/v1/safety/narratives/{id}/export-e2b',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/safety/api/v1/safety/narratives/{id}/export-e2b' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/safety/narratives/{id}/sign`

**Summary:** Sign Safety Narrative Endpoint  
**Operation ID:** `sign_safety_narrative_endpoint_api_v1_safety_narratives__id__sign_post`  

**Clinical & GxP Rationale:**  
Apply a 21 CFR Part 11 cryptographic electronic signature to approve a safety narrative.

Requirements: PRD-SYS-051, PRD-SYS-052

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `NarrativeSignRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `narrative_id` | `string` | ✔ Yes | Unique narrative record ID to sign. |
| `reason_for_change` | `string` | ✔ Yes | Electronic signature intent / reason for change. |
| `signature_secret` | `string` | `null` | No | Optional signer secret or signing credential. |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `NarrativeSignResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/narratives/{id}/sign', {
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
        '/api/v1/safety/api/v1/safety/narratives/{id}/sign',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/safety/api/v1/safety/narratives/{id}/sign' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/safety/periodic-reports`

**Summary:** List Periodic Safety Reports Endpoint  
**Operation ID:** `list_periodic_safety_reports_endpoint_api_v1_safety_periodic_reports_get`  

**Clinical & GxP Rationale:**  
List compiled periodic regulatory safety reports with optional filters.

Requirements: PRD-PVG-004, PRD-ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | — |
| `compound_name` | `query` | `string` | `null` | No | — |
| `report_type` | `query` | `string` | `null` | No | — |
| `status` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<PeriodicReportResponseDTO>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/periodic-reports', {
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
        '/api/v1/safety/api/v1/safety/periodic-reports',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/api/v1/safety/periodic-reports' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/safety/periodic-reports/compile`

**Summary:** Compile Periodic Safety Report Endpoint  
**Operation ID:** `compile_periodic_safety_report_endpoint_api_v1_safety_periodic_reports_compile_post`  

**Clinical & GxP Rationale:**  
Compile an aggregate periodic safety report (ICH E2F DSUR, ICH E2C(R2) PSUR/PBRER).

Requirements: PRD-PVG-004, PRD-AGG-001, PRD-ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `PeriodicReportCompilationRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `report_type` | `PeriodicSafetyReportType` | No | — |
| `study_id` | `string` | ✔ Yes | — |
| `compound_name` | `string` | ✔ Yes | — |
| `reporting_period_start` | `string` | ✔ Yes | — |
| `reporting_period_end` | `string` | ✔ Yes | — |
| `dibd_anniversary` | `string` | `null` | No | — |
| `sponsor_name` | `string` | No | — |
| `include_line_listings` | `boolean` | No | — |
| `include_summary_tabulations` | `boolean` | No | — |
| `include_signal_analyses` | `boolean` | No | — |
| `benefit_risk_narrative` | `string` | `null` | No | — |
| `cumulative_exposure_subjects` | `integer` | No | — |
| `rsi_config` | `ReferenceSafetyInformationConfig` | `null` | No | — |
| `aesi_terms` | `Array<string>` | `null` | No | — |
| `cases` | `Array<object>` | `null` | No | — |
| `subjects_exposure_data` | `Array<object>` | `null` | No | — |
| `auto_lock_snapshot` | `boolean` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `PeriodicReportResponseDTO` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/periodic-reports/compile', {
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
        '/api/v1/safety/api/v1/safety/periodic-reports/compile',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/safety/api/v1/safety/periodic-reports/compile' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/safety/periodic-reports/{id}`

**Summary:** Get Periodic Safety Report Endpoint  
**Operation ID:** `get_periodic_safety_report_endpoint_api_v1_safety_periodic_reports__id__get`  

**Clinical & GxP Rationale:**  
Retrieve full structured JSON payload of a periodic regulatory safety report by ID.

Requirements: PRD-PVG-004, PRD-ADR-2199

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
| `200` | Successful Response | `PeriodicReportResponseDTO` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/periodic-reports/{id}', {
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
        '/api/v1/safety/api/v1/safety/periodic-reports/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/api/v1/safety/periodic-reports/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/safety/periodic-reports/{id}/export`

**Summary:** Export Periodic Safety Report Endpoint  
**Operation ID:** `export_periodic_safety_report_endpoint_api_v1_safety_periodic_reports__id__export_get`  

**Clinical & GxP Rationale:**  
Export a periodic safety report as HTML, XML, or JSON.

Requirements: PRD-PVG-004, PRD-AGG-001, PRD-ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |
| `format` | `query` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `any` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/periodic-reports/{id}/export', {
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
        '/api/v1/safety/api/v1/safety/periodic-reports/{id}/export',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/api/v1/safety/periodic-reports/{id}/export' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/safety/periodic-reports/{id}/qppv-sign`

**Summary:** Qppv Sign Periodic Safety Report Endpoint  
**Operation ID:** `qppv_sign_periodic_safety_report_endpoint_api_v1_safety_periodic_reports__id__qppv_sign_post`  

**Clinical & GxP Rationale:**  
Executive QPPV Sign-off endpoint for aggregate regulatory safety submissions.

Requirements: PRD-AGG-001, 21 CFR Part 11, PRD-ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `PeriodicReportSignRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `user_id` | `string` | ✔ Yes | — |
| `meaning_of_signature` | `string` | No | — |
| `signature_secret` | `string` | ✔ Yes | — |
| `role` | `string` | `null` | No | — |
| `comments` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `PeriodicReportSignResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/periodic-reports/{id}/qppv-sign', {
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
        '/api/v1/safety/api/v1/safety/periodic-reports/{id}/qppv-sign',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/safety/api/v1/safety/periodic-reports/{id}/qppv-sign' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/safety/periodic-reports/{id}/sign`

**Summary:** Sign Periodic Safety Report Endpoint  
**Operation ID:** `sign_periodic_safety_report_endpoint_api_v1_safety_periodic_reports__id__sign_post`  

**Clinical & GxP Rationale:**  
Apply a 21 CFR Part 11 Electronic Signature to approve a Periodic Safety Report.

Requirements: PRD-PVG-004, PRD-PVG-008, PRD-AGG-001, PRD-ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `PeriodicReportSignRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `user_id` | `string` | ✔ Yes | — |
| `meaning_of_signature` | `string` | No | — |
| `signature_secret` | `string` | ✔ Yes | — |
| `role` | `string` | `null` | No | — |
| `comments` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `PeriodicReportSignResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/periodic-reports/{id}/sign', {
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
        '/api/v1/safety/api/v1/safety/periodic-reports/{id}/sign',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/safety/api/v1/safety/periodic-reports/{id}/sign' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/safety/periodic-reports/{id}/xml`

**Summary:** Export Periodic Safety Report Xml Endpoint  
**Operation ID:** `export_periodic_safety_report_xml_endpoint_api_v1_safety_periodic_reports__id__xml_get`  

**Clinical & GxP Rationale:**  
Export the structured ICH E2F / E2C(R2) XML submission package.

Requirements: PRD-AGG-001, ICH E2F, ICH E2C(R2)

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
| `200` | Successful Response | `any` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/periodic-reports/{id}/xml', {
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
        '/api/v1/safety/api/v1/safety/periodic-reports/{id}/xml',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/api/v1/safety/periodic-reports/{id}/xml' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/safety/reconciliation/jobs`

**Summary:** Trigger Sae Reconciliation Job  
**Operation ID:** `trigger_sae_reconciliation_job_api_v1_safety_reconciliation_jobs_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `SAEReconciliationJobRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | The study identifier for reconciliation |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `202` | Successful Response | `SAEReconciliationJobResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/reconciliation/jobs', {
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
        '/api/v1/safety/api/v1/safety/reconciliation/jobs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/safety/api/v1/safety/reconciliation/jobs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/safety/reconciliation/jobs`

**Summary:** List Reconciliation Jobs  
**Operation ID:** `list_reconciliation_jobs_api_v1_safety_reconciliation_jobs_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `limit` | `query` | `integer` | No | — |
| `offset` | `query` | `integer` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<SAEReconciliationJobResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/reconciliation/jobs', {
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
        '/api/v1/safety/api/v1/safety/reconciliation/jobs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/api/v1/safety/reconciliation/jobs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/safety/reconciliation/jobs/{job_id}`

**Summary:** Get Reconciliation Job  
**Operation ID:** `get_reconciliation_job_api_v1_safety_reconciliation_jobs__job_id__get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `job_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SAEReconciliationJobResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/reconciliation/jobs/{job_id}', {
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
        '/api/v1/safety/api/v1/safety/reconciliation/jobs/{job_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/api/v1/safety/reconciliation/jobs/{job_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/safety/reconciliation/runs`

**Summary:** List Reconciliation Runs  
**Operation ID:** `list_reconciliation_runs_api_v1_safety_reconciliation_runs_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<SAEReconciliationRunResponse>` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/reconciliation/runs', {
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
        '/api/v1/safety/api/v1/safety/reconciliation/runs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/api/v1/safety/reconciliation/runs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/safety/reconciliation/runs`

**Summary:** Trigger Sae Reconciliation  
**Operation ID:** `trigger_sae_reconciliation_api_v1_safety_reconciliation_runs_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `SAEReconciliationRunRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | The study identifier for reconciliation |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `SAEReconciliationRunResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/reconciliation/runs', {
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
        '/api/v1/safety/api/v1/safety/reconciliation/runs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/safety/api/v1/safety/reconciliation/runs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/safety/reconciliation/runs/{id}`

**Summary:** Get Reconciliation Run  
**Operation ID:** `get_reconciliation_run_api_v1_safety_reconciliation_runs__id__get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

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
| `200` | Successful Response | `SAEReconciliationRunResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/reconciliation/runs/{id}', {
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
        '/api/v1/safety/api/v1/safety/reconciliation/runs/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/api/v1/safety/reconciliation/runs/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/safety/rules/countdown-alerts`

**Summary:** Get Safety Countdown Alerts Endpoint  
**Operation ID:** `get_safety_countdown_alerts_endpoint_api_v1_safety_rules_countdown_alerts_get`  

**Clinical & GxP Rationale:**  
Retrieve all active expedited safety evaluations with WARNING (>=75%), CRITICAL (>=90%), or OVERDUE countdown clocks.

Requirements: PRD-PVG-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<ICSRRuleEvaluationSummary>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/rules/countdown-alerts', {
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
        '/api/v1/safety/api/v1/safety/rules/countdown-alerts',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/api/v1/safety/rules/countdown-alerts' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/safety/rules/evaluate`

**Summary:** Evaluate Expedited Safety Rules Endpoint  
**Operation ID:** `evaluate_expedited_safety_rules_endpoint_api_v1_safety_rules_evaluate_post`  

**Clinical & GxP Rationale:**  
Evaluate adverse event against global expedited safety reporting rules and persist evaluation.

Requirements: PRD-PVG-001, PRD-PVG-008, PRD-ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ICSRRuleEvaluationInput`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `case_id` | `string` | ✔ Yes | Worldwide unique case ID or ICSR ID |
| `study_id` | `string` | ✔ Yes | Protocol / Study identifier |
| `subject_id` | `string` | ✔ Yes | Subject / USUBJID identifier |
| `receipt_date` | `string` | ✔ Yes | Day 0 receipt date/time in ISO 8601 or CDISC DTC format |
| `seriousness_death` | `boolean` | No | Results in death |
| `seriousness_life_threatening` | `boolean` | No | Is life-threatening |
| `seriousness_hospitalization` | `boolean` | No | Requires or prolongs hospitalization |
| `seriousness_disability` | `boolean` | No | Results in persistent or significant disability |
| `seriousness_congenital_anomaly` | `boolean` | No | Results in congenital anomaly or birth defect |
| `seriousness_other` | `boolean` | No | Other medically important serious event |
| `causality` | `CausalityCategory` | No | Medical causality / relatedness |
| `expectedness` | `ExpectednessCategory` | No | Expectedness per Reference Safety Information (RSI) |
| `target_authorities` | `Array<ReportingAuthority>` | `null` | No | List of target authorities to evaluate (evaluates all if omitted) |
| `reason_for_change` | `string` | `null` | No | GxP reason for evaluation / change |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `ICSRRuleEvaluationSummary` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/rules/evaluate', {
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
        '/api/v1/safety/api/v1/safety/rules/evaluate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/safety/api/v1/safety/rules/evaluate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/safety/rules/evaluations`

**Summary:** List Expedited Safety Evaluations Endpoint  
**Operation ID:** `list_expedited_safety_evaluations_endpoint_api_v1_safety_rules_evaluations_get`  

**Clinical & GxP Rationale:**  
List safety evaluations filtered by study, case, or expedited status with live countdowns.

Requirements: PRD-PVG-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | — |
| `case_id` | `query` | `string` | `null` | No | — |
| `subject_id` | `query` | `string` | `null` | No | — |
| `approval_status` | `query` | `string` | `null` | No | — |
| `any_expedited` | `query` | `boolean` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<ICSRRuleEvaluationSummary>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/rules/evaluations', {
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
        '/api/v1/safety/api/v1/safety/rules/evaluations',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/api/v1/safety/rules/evaluations' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/safety/rules/evaluations/{id}`

**Summary:** Get Expedited Safety Evaluation Endpoint  
**Operation ID:** `get_expedited_safety_evaluation_endpoint_api_v1_safety_rules_evaluations__id__get`  

**Clinical & GxP Rationale:**  
Retrieve safety reporting evaluation with updated real-time Day 0 countdown clocks.

Requirements: PRD-PVG-001

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
| `200` | Successful Response | `ICSRRuleEvaluationSummary` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/rules/evaluations/{id}', {
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
        '/api/v1/safety/api/v1/safety/rules/evaluations/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/api/v1/safety/rules/evaluations/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/safety/rules/evaluations/{id}/sign`

**Summary:** Sign Expedited Safety Evaluation Endpoint  
**Operation ID:** `sign_expedited_safety_evaluation_endpoint_api_v1_safety_rules_evaluations__id__sign_post`  

**Clinical & GxP Rationale:**  
Apply a 21 CFR Part 11 electronic signature by a qualified Safety Physician to approve an ICSR evaluation.

Requirements: PRD-PVG-008

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SignICSRRuleEvaluationRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `reason_for_change` | `string` | ✔ Yes | Mandatory 21 CFR Part 11 reason for electronic signature |
| `signature_secret` | `string` | `null` | No | Physician re-authentication signing secret / credential |
| `meaning_of_signature` | `string` | No | Part 11 declaration of signature intent |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SignICSRRuleEvaluationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/rules/evaluations/{id}/sign', {
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
        '/api/v1/safety/api/v1/safety/rules/evaluations/{id}/sign',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/safety/api/v1/safety/rules/evaluations/{id}/sign' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/safety/signals/alerts`

**Summary:** List Signal Alerts Endpoint  
**Operation ID:** `list_signal_alerts_endpoint_api_v1_safety_signals_alerts_get`  

**Clinical & GxP Rationale:**  
List safety signal review alerts with optional filtering by status, severity, or study.

Requirements: PRD-PVG-003, PRD-ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | — |
| `target_drug` | `query` | `string` | `null` | No | — |
| `status` | `query` | `string` | `null` | No | — |
| `severity` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<SafetySignalAlertDTO>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/signals/alerts', {
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
        '/api/v1/safety/api/v1/safety/signals/alerts',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/api/v1/safety/signals/alerts' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/safety/signals/alerts/{id}`

**Summary:** Get Signal Alert Endpoint  
**Operation ID:** `get_signal_alert_endpoint_api_v1_safety_signals_alerts__id__get`  

**Clinical & GxP Rationale:**  
Retrieve details of an individual safety signal review alert.

Requirements: PRD-PVG-003, PRD-ADR-2199

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
| `200` | Successful Response | `SafetySignalAlertDTO` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/signals/alerts/{id}', {
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
        '/api/v1/safety/api/v1/safety/signals/alerts/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/api/v1/safety/signals/alerts/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/safety/signals/alerts/{id}/review`

**Summary:** Review Signal Alert Endpoint  
**Operation ID:** `review_signal_alert_endpoint_api_v1_safety_signals_alerts__id__review_post`  

**Clinical & GxP Rationale:**  
Review and update the clinical triage disposition of a safety signal alert.

Requirements: PRD-PVG-003, PRD-ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SafetySignalAlertReviewRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `decision` | `SafetySignalReviewDecision` | ✔ Yes | — |
| `justification` | `string` | ✔ Yes | Medical rationale for the review decision |
| `clinical_summary` | `string` | `null` | No | — |
| `assigned_safety_officer` | `string` | `null` | No | — |
| `recommended_action` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SafetySignalAlertDTO` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/signals/alerts/{id}/review', {
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
        '/api/v1/safety/api/v1/safety/signals/alerts/{id}/review',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/safety/api/v1/safety/signals/alerts/{id}/review' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/safety/signals/alerts/{id}/sign`

**Summary:** Sign Signal Alert Endpoint  
**Operation ID:** `sign_signal_alert_endpoint_api_v1_safety_signals_alerts__id__sign_post`  

**Clinical & GxP Rationale:**  
Apply a 21 CFR Part 11 Electronic Signature to approve a safety signal disposition.

Requirements: PRD-PVG-003, PRD-PVG-008, PRD-ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SafetySignalAlertSignRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `user_id` | `string` | ✔ Yes | — |
| `meaning_of_signature` | `string` | No | — |
| `signature_secret` | `string` | ✔ Yes | — |
| `decision` | `SafetySignalReviewDecision` | ✔ Yes | — |
| `justification` | `string` | ✔ Yes | Medical rationale for signature disposition |
| `clinical_summary` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SafetySignalAlertSignResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/signals/alerts/{id}/sign', {
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
        '/api/v1/safety/api/v1/safety/signals/alerts/{id}/sign',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/safety/api/v1/safety/signals/alerts/{id}/sign' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/safety/signals/detect`

**Summary:** Execute Signal Detection Scan Endpoint  
**Operation ID:** `execute_signal_detection_scan_endpoint_api_v1_safety_signals_detect_post`  

**Clinical & GxP Rationale:**  
Execute statistical disproportionality signal detection (PRR, ROR, EBGM/MGPS) across PT and SOC pairs.

Requirements: PRD-PVG-003, PRD-ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Payload Schema (`application/json`)

**Schema Type:** `SignalDetectionScanRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `target_drug` | `string` | ✔ Yes | — |
| `study_id` | `string` | `null` | No | — |
| `comparator_drugs` | `Array<string>` | No | — |
| `benchmark_dataset` | `BenchmarkReferenceDataset` | No | — |
| `stratification_levels` | `Array<StratificationLevel>` | No | — |
| `min_case_count` | `integer` | No | — |
| `prr_threshold` | `number` | No | — |
| `chi_square_threshold` | `number` | No | — |
| `ror_ci_lower_threshold` | `number` | No | — |
| `eb05_threshold` | `number` | No | — |
| `auto_trigger_alerts` | `boolean` | No | — |
| `cases` | `Array<object>` | `null` | No | — |
| `benchmark_cases` | `Array<object>` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SignalDetectionScanResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/signals/detect', {
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
        '/api/v1/safety/api/v1/safety/signals/detect',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/safety/api/v1/safety/signals/detect' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/safety/signals/runs`

**Summary:** List Signal Detection Runs Endpoint  
**Operation ID:** `list_signal_detection_runs_endpoint_api_v1_safety_signals_runs_get`  

**Clinical & GxP Rationale:**  
List historical disproportionality signal detection scan runs.

Requirements: PRD-PVG-003, PRD-ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | — |
| `target_drug` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<object>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/signals/runs', {
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
        '/api/v1/safety/api/v1/safety/signals/runs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/api/v1/safety/signals/runs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/safety/signals/runs/{id}`

**Summary:** Get Signal Detection Run Endpoint  
**Operation ID:** `get_signal_detection_run_endpoint_api_v1_safety_signals_runs__id__get`  

**Clinical & GxP Rationale:**  
Retrieve full metric tables and details for a signal detection scan run.

Requirements: PRD-PVG-003, PRD-ADR-2199

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
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/signals/runs/{id}', {
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
        '/api/v1/safety/api/v1/safety/signals/runs/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/api/v1/safety/signals/runs/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/safety/snapshots/lock`

**Summary:** Lock Regulatory Snapshot Endpoint  
**Operation ID:** `lock_regulatory_snapshot_endpoint_api_v1_safety_snapshots_lock_post`  

**Clinical & GxP Rationale:**  
Lock an immutable point-in-time regulatory dataset snapshot with SHA-256 seal.

Requirements: PRD-AGG-001, 21 CFR Part 11, PRD-ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `RegulatorySnapshotLockRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | — |
| `compound_name` | `string` | ✔ Yes | — |
| `reporting_period_start` | `string` | ✔ Yes | — |
| `reporting_period_end` | `string` | ✔ Yes | — |
| `dibd_anniversary` | `string` | `null` | No | — |
| `lock_reason` | `string` | No | — |
| `cases` | `Array<object>` | `null` | No | — |
| `subjects_data` | `Array<object>` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `RegulatorySnapshotLockResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/snapshots/lock', {
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
        '/api/v1/safety/api/v1/safety/snapshots/lock',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/safety/api/v1/safety/snapshots/lock' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/safety/snapshots/verify`

**Summary:** Verify Regulatory Snapshot Endpoint  
**Operation ID:** `verify_regulatory_snapshot_endpoint_api_v1_safety_snapshots_verify_post`  

**Clinical & GxP Rationale:**  
Cryptographically verify the bitwise integrity of a locked regulatory snapshot.

Requirements: PRD-AGG-001, 21 CFR Part 11, PRD-ADR-2199

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `RegulatorySnapshotVerifyRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `snapshot_id` | `string` | ✔ Yes | — |
| `snapshot_hash` | `string` | ✔ Yes | — |
| `dataset_manifest` | `object` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `RegulatorySnapshotVerifyResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/safety/api/v1/safety/snapshots/verify', {
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
        '/api/v1/safety/api/v1/safety/snapshots/verify',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/safety/api/v1/safety/snapshots/verify' \
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
const response = await fetch('http://localhost:8000/api/v1/safety/health', {
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
        '/api/v1/safety/health',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/safety/health' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---
