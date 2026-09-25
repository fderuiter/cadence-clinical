# Electronic Trial Master File (eTMF)

> **Functional Domain:** `Safety, Quality & Governance`  
> **Service Host Port:** `localhost:8004`  
> **Gateway Proxy Prefix:** `/api/v1/etmf`  
> **Persistence Backend:** `PostgreSQL / SQLite`  

## Overview

DIA TMF Reference Model v3.2.0 document classification, Expected Document Lists (EDLs), completeness scoring, and automated QC transitions.

---

## Endpoints Summary

| Method | Path | Summary | GxP Auth & Signatures |
| :--- | :--- | :--- | :--- |
| `GET` | [/api/v1/admin/outbox](#get-api-v1-admin-outbox) | Etmf Admin Outbox Endpoint | 👀 Read-Only |
| `GET` | [/api/v1/archive/download/{job_id}](#get-api-v1-archive-download-job_id) | Download Archive Package | 👀 Read-Only |
| `GET` | [/api/v1/archive/jobs/{job_id}](#get-api-v1-archive-jobs-job_id) | Get Archive Job Status | 👀 Read-Only |
| `POST` | [/api/v1/archive/studies/{study_id}/export](#post-api-v1-archive-studies-study_id-export) | Initiate Study Archival | ✍️ Audited Write |
| `GET` | [/api/v1/etmf/audit-logs](#get-api-v1-etmf-audit-logs) | Get Audit Trail | 👀 Read-Only |
| `POST` | [/api/v1/etmf/audit-logs/verify-chain](#post-api-v1-etmf-audit-logs-verify-chain) | Verify Audit Ledger Chain | ✍️ Audited Write |
| `POST` | [/api/v1/etmf/auto-file](#post-api-v1-etmf-auto-file) | Auto File Suggestion | ✍️ Audited Write |
| `POST` | [/api/v1/etmf/classify](#post-api-v1-etmf-classify) | Suggest Classification | ✍️ Audited Write |
| `GET` | [/api/v1/etmf/completeness](#get-api-v1-etmf-completeness) | Check Completeness | 👀 Read-Only |
| `GET` | [/api/v1/etmf/documents](#get-api-v1-etmf-documents) | List Documents | 👀 Read-Only |
| `GET` | [/api/v1/etmf/documents/{document_id}](#get-api-v1-etmf-documents-document_id) | View Document | 👀 Read-Only |
| `POST` | [/api/v1/etmf/documents/{document_id}/approve](#post-api-v1-etmf-documents-document_id-approve) | Sign Document Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/etmf/documents/{document_id}/auto-redact](#post-api-v1-etmf-documents-document_id-auto-redact) | Auto Redact Document Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/etmf/documents/{document_id}/download](#get-api-v1-etmf-documents-document_id-download) | Download Document | 👀 Read-Only |
| `PUT` | [/api/v1/etmf/documents/{document_id}/expiration](#put-api-v1-etmf-documents-document_id-expiration) | Update Document Expiration Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/etmf/documents/{document_id}/manual-redact](#post-api-v1-etmf-documents-document_id-manual-redact) | Manual Redact Document Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/etmf/documents/{document_id}/qc-history](#get-api-v1-etmf-documents-document_id-qc-history) | Get Document Qc History | 👀 Read-Only |
| `POST` | [/api/v1/etmf/documents/{document_id}/redact](#post-api-v1-etmf-documents-document_id-redact) | Redact Document Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/etmf/documents/{document_id}/sign-off](#post-api-v1-etmf-documents-document_id-sign-off) | Sign Document Endpoint | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/etmf/documents/{document_id}/transition](#post-api-v1-etmf-documents-document_id-transition) | Transition Document Status Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/etmf/documents/{document_id}/transitions](#get-api-v1-etmf-documents-document_id-transitions) | Get Document Transition History | 👀 Read-Only |
| `POST` | [/api/v1/etmf/documents/{document_id}/verify-signature](#post-api-v1-etmf-documents-document_id-verify-signature) | Verify Document Signature Endpoint | 🔒 Step-Up Sig Token |
| `GET` | [/api/v1/etmf/documents/{document_id}/versions](#get-api-v1-etmf-documents-document_id-versions) | Get Document Versions | 👀 Read-Only |
| `GET` | [/api/v1/etmf/documents/{document_id}/watermark](#get-api-v1-etmf-documents-document_id-watermark) | Download Watermarked Document | 👀 Read-Only |
| `GET` | [/api/v1/etmf/edl](#get-api-v1-etmf-edl) | List Expectations | 👀 Read-Only |
| `POST` | [/api/v1/etmf/edl](#post-api-v1-etmf-edl) | Create Expectation | ✍️ Audited Write |
| `PUT` | [/api/v1/etmf/edl/{edl_id}](#put-api-v1-etmf-edl-edl_id) | Update Expectation | ✍️ Audited Write |
| `POST` | [/api/v1/etmf/inbound-email](#post-api-v1-etmf-inbound-email) | Receive Inbound Email | ✍️ Audited Write |
| `POST` | [/api/v1/etmf/ingest](#post-api-v1-etmf-ingest) | Ingest Document | ✍️ Audited Write |
| `POST` | [/api/v1/etmf/intelligence/analyze](#post-api-v1-etmf-intelligence-analyze) | Execute end-to-end multimodal document intelligence pipeline | ✍️ Audited Write |
| `POST` | [/api/v1/etmf/intelligence/classify](#post-api-v1-etmf-intelligence-classify) | Execute multi-signal DIA Reference Model taxonomy classification | ✍️ Audited Write |
| `POST` | [/api/v1/etmf/intelligence/extract-metadata](#post-api-v1-etmf-intelligence-extract-metadata) | Extract regulatory and trial metadata from document | ✍️ Audited Write |
| `GET` | [/api/v1/etmf/intelligence/qc-queue](#get-api-v1-etmf-intelligence-qc-queue) | List pending documents staged in CRA Quality Control queue | 👀 Read-Only |
| `POST` | [/api/v1/etmf/intelligence/qc-review/{doc_id}](#post-api-v1-etmf-intelligence-qc-review-doc_id) | Execute CRA Quality Control review decision (Accept, Override, Reject) | ✍️ Audited Write |
| `POST` | [/api/v1/etmf/intelligence/stage-qc](#post-api-v1-etmf-intelligence-stage-qc) | Ingest and stage classified artifact into CRA Quality Control queue | ✍️ Audited Write |
| `POST` | [/api/v1/etmf/intelligence/verify-signatures](#post-api-v1-etmf-intelligence-verify-signatures) | Verify regulatory signature completeness | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/etmf/locks/trial/lock](#post-api-v1-etmf-locks-trial-lock) | Etmf Lock Trial Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/etmf/locks/trial/unlock](#post-api-v1-etmf-locks-trial-unlock) | Etmf Unlock Trial Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/etmf/studies/{study_id}/archive](#post-api-v1-etmf-studies-study_id-archive) | Bulk Archive Study Documents | ✍️ Audited Write |
| `GET` | [/api/v1/etmf/studies/{study_id}/artifacts/{artifact_type}/history](#get-api-v1-etmf-studies-study_id-artifacts-artifact_type-history) | Get Artifact History | 👀 Read-Only |
| `GET` | [/api/v1/etmf/studies/{study_id}/binder](#get-api-v1-etmf-studies-study_id-binder) | Export Regulatory Binder | 👀 Read-Only |
| `GET` | [/api/v1/etmf/studies/{study_id}/binder/structure](#get-api-v1-etmf-studies-study_id-binder-structure) | Get Binder Structure | 👀 Read-Only |
| `GET` | [/api/v1/etmf/studies/{study_id}/ems-export](#get-api-v1-etmf-studies-study_id-ems-export) | Export Study Tmf Ems | 👀 Read-Only |
| `GET` | [/api/v1/etmf/studies/{study_id}/inspection-readiness](#get-api-v1-etmf-studies-study_id-inspection-readiness) | Get Study Inspection Readiness | 👀 Read-Only |
| `POST` | [/api/v1/etmf/studies/{study_id}/seed-edl](#post-api-v1-etmf-studies-study_id-seed-edl) | Seed Edl Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/etmf/taxonomy](#get-api-v1-etmf-taxonomy) | Get Taxonomy Catalog | 👀 Read-Only |
| `POST` | [/api/v1/etmf/taxonomy/classify](#post-api-v1-etmf-taxonomy-classify) | Suggest Classification | ✍️ Audited Write |
| `GET` | [/api/v1/etmf/test-exception](#get-api-v1-etmf-test-exception) | Test Exception Route | 👀 Read-Only |
| `POST` | [/events/publish](#post-events-publish) | Ingest Document | ✍️ Audited Write |
| `GET` | [/health](#get-health) | Health Check | 👀 Read-Only |

---

## Granular Endpoint Specifications

### `GET` `/api/v1/admin/outbox`

**Summary:** Etmf Admin Outbox Endpoint  
**Operation ID:** `etmf_admin_outbox_endpoint_api_v1_admin_outbox_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `status` | `query` | `string` | `null` | No | — |
| `event_type` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<object>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/admin/outbox', {
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
        '/api/v1/etmf/api/v1/admin/outbox',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/etmf/api/v1/admin/outbox' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/archive/download/{job_id}`

**Summary:** Download Archive Package  
**Operation ID:** `download_archive_package_api_v1_archive_download__job_id__get`  

**Clinical & GxP Rationale:**  
Download the generated study archival ZIP package.

Requirements: PRD-SYS-001

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
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/archive/download/{job_id}', {
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
        '/api/v1/etmf/api/v1/archive/download/{job_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/etmf/api/v1/archive/download/{job_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/archive/jobs/{job_id}`

**Summary:** Get Archive Job Status  
**Operation ID:** `get_archive_job_status_api_v1_archive_jobs__job_id__get`  

**Clinical & GxP Rationale:**  
Check archive package build status.

Requirements: PRD-SYS-001

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
| `200` | Successful Response | `ArchiveJobResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/archive/jobs/{job_id}', {
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
        '/api/v1/etmf/api/v1/archive/jobs/{job_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/etmf/api/v1/archive/jobs/{job_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/archive/studies/{study_id}/export`

**Summary:** Initiate Study Archival  
**Operation ID:** `initiate_study_archival_api_v1_archive_studies__study_id__export_post`  

**Clinical & GxP Rationale:**  
Initiate background ZIP packaging task.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ArchiveJobResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/archive/studies/{study_id}/export', {
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
        '/api/v1/etmf/api/v1/archive/studies/{study_id}/export',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/etmf/api/v1/archive/studies/{study_id}/export' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/etmf/audit-logs`

**Summary:** Get Audit Trail  
**Operation ID:** `get_audit_trail_api_v1_etmf_audit_logs_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `user_id` | `query` | `string` | `null` | No | Filter logs by user ID |
| `action` | `query` | `string` | `null` | No | Filter logs by action |
| `document_id` | `query` | `string` | `null` | No | Filter logs by document ID |
| `start_time` | `query` | `string` | `null` | No | Filter logs starting from this timestamp (inclusive) |
| `end_time` | `query` | `string` | `null` | No | Filter logs up to this timestamp (inclusive) |
| `limit` | `query` | `integer` | No | Limit the number of audit log records returned |
| `offset` | `query` | `integer` | No | Offset for pagination |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `PaginatedAuditLogResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/audit-logs', {
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
        '/api/v1/etmf/api/v1/etmf/audit-logs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/etmf/api/v1/etmf/audit-logs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/etmf/audit-logs/verify-chain`

**Summary:** Verify Audit Ledger Chain  
**Operation ID:** `verify_audit_ledger_chain_api_v1_etmf_audit_logs_verify_chain_post`  

**Clinical & GxP Rationale:**  
Cryptographically inspects and verifies the full Merkle block ledger chain for tampering detection.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `AuditChainVerificationResponse` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/audit-logs/verify-chain', {
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
        '/api/v1/etmf/api/v1/etmf/audit-logs/verify-chain',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/etmf/api/v1/etmf/audit-logs/verify-chain' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/etmf/auto-file`

**Summary:** Auto File Suggestion  
**Operation ID:** `auto_file_suggestion_api_v1_etmf_auto_file_post`  

**Clinical & GxP Rationale:**  
Provide automatic classification/auto-filing suggestions for a document with study scope.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `AutoFileRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `filename` | `string` | ✔ Yes | — |
| `artifact_type` | `string` | `null` | No | Optional artifact type hint |
| `free_text` | `string` | `null` | No | Optional free-text hint |
| `study_id` | `string` | `null` | No | Optional study ID for scope enforcement |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `AutoFileResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/auto-file', {
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
        '/api/v1/etmf/api/v1/etmf/auto-file',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/etmf/api/v1/etmf/auto-file' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/etmf/classify`

**Summary:** Suggest Classification  
**Operation ID:** `suggest_classification_api_v1_etmf_classify_post`  

**Clinical & GxP Rationale:**  
Provide automatic classification/auto-filing suggestions for a document.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `AutoFileRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `filename` | `string` | ✔ Yes | — |
| `artifact_type` | `string` | `null` | No | Optional artifact type hint |
| `free_text` | `string` | `null` | No | Optional free-text hint |
| `study_id` | `string` | `null` | No | Optional study ID for scope enforcement |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `AutoFileResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/classify', {
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
        '/api/v1/etmf/api/v1/etmf/classify',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/etmf/api/v1/etmf/classify' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/etmf/completeness`

**Summary:** Check Completeness  
**Operation ID:** `check_completeness_api_v1_etmf_completeness_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | The clinical study ID |
| `milestone` | `query` | `string` | ✔ Yes | The transition milestone to check |
| `site_id` | `query` | `string` | `null` | No | Optional clinical site ID |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `CompletenessResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/completeness', {
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
        '/api/v1/etmf/api/v1/etmf/completeness',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/etmf/api/v1/etmf/completeness' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/etmf/documents`

**Summary:** List Documents  
**Operation ID:** `list_documents_api_v1_etmf_documents_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | Filter by study ID |
| `zone` | `query` | `integer` | `null` | No | Filter by TMF Zone |
| `search` | `query` | `string` | `null` | No | Search document content |
| `status` | `query` | `string` | `null` | No | Filter by status |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<DocumentResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/documents', {
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
        '/api/v1/etmf/api/v1/etmf/documents',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/etmf/api/v1/etmf/documents' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/etmf/documents/{document_id}`

**Summary:** View Document  
**Operation ID:** `view_document_api_v1_etmf_documents__document_id__get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `document_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DocumentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}', {
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
        '/api/v1/etmf/api/v1/etmf/documents/{document_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/etmf/documents/{document_id}/approve`

**Summary:** Sign Document Endpoint  
**Operation ID:** `sign_document_endpoint_api_v1_etmf_documents__document_id__approve_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `document_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SignDocumentRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `signing_reason` | `SigningReason` | ✔ Yes | Controlled reason for creating this electronic signature in compliance with 21 CFR Part 11 |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DocumentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}/approve', {
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
        '/api/v1/etmf/api/v1/etmf/documents/{document_id}/approve',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}/approve' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/etmf/documents/{document_id}/auto-redact`

**Summary:** Auto Redact Document Endpoint  
**Operation ID:** `auto_redact_document_endpoint_api_v1_etmf_documents__document_id__auto_redact_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `document_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `AutomatedRedactRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `profile` | `ComplianceProfile` | No | The compliance profile governing active detection categories (e.g., HIPAA, GDPR, EU_CTR) |
| `custom_terms` | `Array<string>` | `null` | No | Optional list of custom/literal terms to scan and redact |
| `strategies` | `object` | `null` | No | Optional custom mapping of category to specific strategy (e.g., mask, pseudonymize, date_shift, age_cap) |
| `redacted_filename` | `string` | `null` | No | Optional new filename for the redacted successor document |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `AutomatedRedactResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}/auto-redact', {
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
        '/api/v1/etmf/api/v1/etmf/documents/{document_id}/auto-redact',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}/auto-redact' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/etmf/documents/{document_id}/download`

**Summary:** Download Document  
**Operation ID:** `download_document_api_v1_etmf_documents__document_id__download_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `document_id` | `path` | `string` | ✔ Yes | — |
| `watermark` | `query` | `boolean` | No | Request watermarked document |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}/download', {
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
        '/api/v1/etmf/api/v1/etmf/documents/{document_id}/download',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}/download' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/etmf/documents/{document_id}/expiration`

**Summary:** Update Document Expiration Endpoint  
**Operation ID:** `update_document_expiration_endpoint_api_v1_etmf_documents__document_id__expiration_put`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `document_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `DocumentExpirationUpdate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `issue_date` | `string` | `null` | No | Optional document issue date |
| `expiration_date` | `string` | `null` | No | Optional document expiration date |
| `document_owner_id` | `string` | `null` | No | Optional document owner ID |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DocumentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}/expiration', {
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
        '/api/v1/etmf/api/v1/etmf/documents/{document_id}/expiration',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}/expiration' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/etmf/documents/{document_id}/manual-redact`

**Summary:** Manual Redact Document Endpoint  
**Operation ID:** `manual_redact_document_endpoint_api_v1_etmf_documents__document_id__manual_redact_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `document_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ManualRedactRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `spans` | `Array<SpanItem>` | `null` | No | Explicit character spans to redact |
| `terms` | `Array<string>` | `null` | No | Literal terms to search and redact |
| `redacted_filename` | `string` | `null` | No | Optional new filename for the redacted successor document |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `ManualRedactResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}/manual-redact', {
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
        '/api/v1/etmf/api/v1/etmf/documents/{document_id}/manual-redact',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}/manual-redact' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/etmf/documents/{document_id}/qc-history`

**Summary:** Get Document Qc History  
**Operation ID:** `get_document_qc_history_api_v1_etmf_documents__document_id__qc_history_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `document_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<TransitionResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}/qc-history', {
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
        '/api/v1/etmf/api/v1/etmf/documents/{document_id}/qc-history',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}/qc-history' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/etmf/documents/{document_id}/redact`

**Summary:** Redact Document Endpoint  
**Operation ID:** `redact_document_endpoint_api_v1_etmf_documents__document_id__redact_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `document_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `RedactRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `redacted_content` | `string` | ✔ Yes | The redacted text content |
| `redacted_filename` | `string` | `null` | No | Optional new filename for the redacted document |
| `manifest` | `object` | ✔ Yes | The signed redaction manifest and provenance data |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `DocumentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}/redact', {
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
        '/api/v1/etmf/api/v1/etmf/documents/{document_id}/redact',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}/redact' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/etmf/documents/{document_id}/sign-off`

**Summary:** Sign Document Endpoint  
**Operation ID:** `sign_document_endpoint_api_v1_etmf_documents__document_id__sign_off_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `document_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SignDocumentRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `signing_reason` | `SigningReason` | ✔ Yes | Controlled reason for creating this electronic signature in compliance with 21 CFR Part 11 |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DocumentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}/sign-off', {
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
        '/api/v1/etmf/api/v1/etmf/documents/{document_id}/sign-off',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}/sign-off' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/etmf/documents/{document_id}/transition`

**Summary:** Transition Document Status Endpoint  
**Operation ID:** `transition_document_status_endpoint_api_v1_etmf_documents__document_id__transition_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `document_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `TransitionRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `to_status` | `string` | ✔ Yes | Target status (e.g. TECHNICAL_QC, CLINICAL_QC, APPROVED, ARCHIVED, REJECTED) |
| `reason_for_change` | `string` | ✔ Yes | Part 11 change justification reason |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}/transition', {
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
        '/api/v1/etmf/api/v1/etmf/documents/{document_id}/transition',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}/transition' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/etmf/documents/{document_id}/transitions`

**Summary:** Get Document Transition History  
**Operation ID:** `get_document_transition_history_api_v1_etmf_documents__document_id__transitions_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `document_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<TransitionResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}/transitions', {
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
        '/api/v1/etmf/api/v1/etmf/documents/{document_id}/transitions',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}/transitions' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/etmf/documents/{document_id}/verify-signature`

**Summary:** Verify Document Signature Endpoint  
**Operation ID:** `verify_document_signature_endpoint_api_v1_etmf_documents__document_id__verify_signature_post`  

**Clinical & GxP Rationale:**  
Verifies the electronic signature and SHA-256 cryptographic digest integrity on a document.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `document_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SignatureVerificationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}/verify-signature', {
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
        '/api/v1/etmf/api/v1/etmf/documents/{document_id}/verify-signature',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}/verify-signature' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/etmf/documents/{document_id}/versions`

**Summary:** Get Document Versions  
**Operation ID:** `get_document_versions_api_v1_etmf_documents__document_id__versions_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `document_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DocumentVersionsResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}/versions', {
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
        '/api/v1/etmf/api/v1/etmf/documents/{document_id}/versions',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}/versions' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/etmf/documents/{document_id}/watermark`

**Summary:** Download Watermarked Document  
**Operation ID:** `download_watermarked_document_api_v1_etmf_documents__document_id__watermark_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `document_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}/watermark', {
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
        '/api/v1/etmf/api/v1/etmf/documents/{document_id}/watermark',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/etmf/api/v1/etmf/documents/{document_id}/watermark' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/etmf/edl`

**Summary:** List Expectations  
**Operation ID:** `list_expectations_api_v1_etmf_edl_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | The clinical study ID |
| `site_id` | `query` | `string` | `null` | No | Optional clinical site ID |
| `milestone` | `query` | `string` | `null` | No | Optional milestone |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<ExpectedDocumentResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/edl', {
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
        '/api/v1/etmf/api/v1/etmf/edl',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/etmf/api/v1/etmf/edl' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/etmf/edl`

**Summary:** Create Expectation  
**Operation ID:** `create_expectation_api_v1_etmf_edl_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ExpectedDocumentCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Unique identifier of the clinical study |
| `site_id` | `string` | `null` | No | Optional site identifier (null = study-scope) |
| `milestone` | `string` | ✔ Yes | Milestone name (e.g. INITIATION, CONDUCT, CLOSEOUT) |
| `artifact_type` | `string` | ✔ Yes | Mandatory artifact type |
| `zone` | `integer` | `null` | No | Optional DIA TMF Zone |
| `section` | `string` | `null` | No | Optional DIA TMF Section |
| `metadata_json` | `object` | `null` | No | Optional metadata rules or notes |
| `reason_for_change` | `string` | ✔ Yes | Part 11 justification reason |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `ExpectedDocumentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/edl', {
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
        '/api/v1/etmf/api/v1/etmf/edl',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/etmf/api/v1/etmf/edl' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/etmf/edl/{edl_id}`

**Summary:** Update Expectation  
**Operation ID:** `update_expectation_api_v1_etmf_edl__edl_id__put`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `edl_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ExpectedDocumentCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Unique identifier of the clinical study |
| `site_id` | `string` | `null` | No | Optional site identifier (null = study-scope) |
| `milestone` | `string` | ✔ Yes | Milestone name (e.g. INITIATION, CONDUCT, CLOSEOUT) |
| `artifact_type` | `string` | ✔ Yes | Mandatory artifact type |
| `zone` | `integer` | `null` | No | Optional DIA TMF Zone |
| `section` | `string` | `null` | No | Optional DIA TMF Section |
| `metadata_json` | `object` | `null` | No | Optional metadata rules or notes |
| `reason_for_change` | `string` | ✔ Yes | Part 11 justification reason |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ExpectedDocumentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/edl/{edl_id}', {
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
        '/api/v1/etmf/api/v1/etmf/edl/{edl_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/etmf/api/v1/etmf/edl/{edl_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/etmf/inbound-email`

**Summary:** Receive Inbound Email  
**Operation ID:** `receive_inbound_email_api_v1_etmf_inbound_email_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `object` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/inbound-email', {
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
        '/api/v1/etmf/api/v1/etmf/inbound-email',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/etmf/api/v1/etmf/inbound-email' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/etmf/ingest`

**Summary:** Ingest Document  
**Operation ID:** `ingest_document_api_v1_etmf_ingest_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `IngestionRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Unique identifier of the clinical study |
| `site_id` | `string` | `null` | No | Optional site identifier |
| `idempotency_key` | `string` | `null` | No | Optional idempotency key for deduplication |
| `artifact_type` | `string` | ✔ Yes | Type of artifact (e.g. Approved Protocol, Define-XML) |
| `filename` | `string` | ✔ Yes | Document filename |
| `content` | `string` | ✔ Yes | Indexed, searchable content of the document |
| `mime_type` | `string` | ✔ Yes | MIME type of the document |
| `zone` | `integer` | `null` | No | Optional expected DIA TMF Zone |
| `section` | `string` | `null` | No | Optional expected DIA TMF Section |
| `artifact_code` | `string` | `null` | No | Optional canonical artifact code |
| `taxonomy_version` | `string` | `null` | No | Optional taxonomy version |
| `metadata_json` | `object` | `null` | No | Optional metadata fields |
| `protocol_version` | `ProtocolVersionRefDTO` | `null` | No | Optional shared protocol version reference |
| `issue_date` | `string` | `null` | No | Optional document issue date |
| `expiration_date` | `string` | `null` | No | Optional document expiration date |
| `document_owner_id` | `string` | `null` | No | Optional document owner ID |
| `correlation_key` | `string` | `null` | No | Optional stable correlation key for synchronized documents |
| `content_checksum` | `string` | `null` | No | Optional deterministic checksum of the content |
| `source_system` | `string` | `null` | No | Optional originating source system |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/ingest', {
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
        '/api/v1/etmf/api/v1/etmf/ingest',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/etmf/api/v1/etmf/ingest' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/etmf/intelligence/analyze`

**Summary:** Execute end-to-end multimodal document intelligence pipeline  
**Operation ID:** `analyze_document_endpoint_api_v1_etmf_intelligence_analyze_post`  

**Clinical & GxP Rationale:**  
Run layout parsing, DIA taxonomy classification, metadata extraction, and signature verification.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `DocumentIntelligenceAnalyzeRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `content` | `string` | ✔ Yes | Raw text, base64 data, or text stream |
| `filename` | `string` | ✔ Yes | Document filename |
| `mime_type` | `string` | No | MIME type |
| `study_id` | `string` | `null` | No | Optional study ID hint |
| `site_id` | `string` | `null` | No | Optional site ID hint |
| `artifact_hint` | `string` | `null` | No | Optional artifact type or code hint |
| `free_text` | `string` | `null` | No | Optional free-text hint |
| `taxonomy_version` | `string` | `null` | No | Optional taxonomy version |
| `document_id` | `string` | `null` | No | Optional document ID if already in eTMF |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DocumentIntelligenceReport` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/intelligence/analyze', {
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
        '/api/v1/etmf/api/v1/etmf/intelligence/analyze',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/etmf/api/v1/etmf/intelligence/analyze' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/etmf/intelligence/classify`

**Summary:** Execute multi-signal DIA Reference Model taxonomy classification  
**Operation ID:** `classify_document_endpoint_api_v1_etmf_intelligence_classify_post`  

**Clinical & GxP Rationale:**  
Classify document content against DIA TMF Reference Model v3.2.0.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `DocumentIntelligenceClassifyRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `content` | `string` | `null` | No | Optional document content |
| `filename` | `string` | ✔ Yes | Document filename |
| `artifact_hint` | `string` | `null` | No | Optional artifact type or code hint |
| `free_text` | `string` | `null` | No | Optional free-text hint |
| `taxonomy_version` | `string` | `null` | No | Optional taxonomy version |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/intelligence/classify', {
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
        '/api/v1/etmf/api/v1/etmf/intelligence/classify',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/etmf/api/v1/etmf/intelligence/classify' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/etmf/intelligence/extract-metadata`

**Summary:** Extract regulatory and trial metadata from document  
**Operation ID:** `extract_metadata_endpoint_api_v1_etmf_intelligence_extract_metadata_post`  

**Clinical & GxP Rationale:**  
Extract protocol, site, investigator, and date metadata.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `DocumentIntelligenceMetadataRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `content` | `string` | ✔ Yes | Document content |
| `filename` | `string` | No | Document filename |
| `study_id` | `string` | `null` | No | Optional study ID hint |
| `site_id` | `string` | `null` | No | Optional site ID hint |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ExtractedRegulatoryMetadata` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/intelligence/extract-metadata', {
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
        '/api/v1/etmf/api/v1/etmf/intelligence/extract-metadata',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/etmf/api/v1/etmf/intelligence/extract-metadata' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/etmf/intelligence/qc-queue`

**Summary:** List pending documents staged in CRA Quality Control queue  
**Operation ID:** `get_qc_queue_endpoint_api_v1_etmf_intelligence_qc_queue_get`  

**Clinical & GxP Rationale:**  
Retrieve staged QC items.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | Optional study ID filter |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<CRAQCStagingItem>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/intelligence/qc-queue', {
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
        '/api/v1/etmf/api/v1/etmf/intelligence/qc-queue',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/etmf/api/v1/etmf/intelligence/qc-queue' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/etmf/intelligence/qc-review/{doc_id}`

**Summary:** Execute CRA Quality Control review decision (Accept, Override, Reject)  
**Operation ID:** `cra_qc_review_endpoint_api_v1_etmf_intelligence_qc_review__doc_id__post`  

**Clinical & GxP Rationale:**  
Adjudicate staged document classification with 21 CFR Part 11 audit trail.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `doc_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `CRAQCReviewRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `decision` | `string` | ✔ Yes | QC action: ACCEPT, OVERRIDE, or REJECT |
| `reason_for_change` | `string` | ✔ Yes | Part 11 change justification |
| `override_artifact_code` | `string` | `null` | No | Target artifact code if decision is OVERRIDE |
| `discrepancy_comment` | `string` | `null` | No | Optional discrepancy note or rejection comment |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DocumentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/intelligence/qc-review/{doc_id}', {
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
        '/api/v1/etmf/api/v1/etmf/intelligence/qc-review/{doc_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/etmf/api/v1/etmf/intelligence/qc-review/{doc_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/etmf/intelligence/stage-qc`

**Summary:** Ingest and stage classified artifact into CRA Quality Control queue  
**Operation ID:** `stage_classified_artifact_endpoint_api_v1_etmf_intelligence_stage_qc_post`  

**Clinical & GxP Rationale:**  
Analyze, ingest, and stage document with TECHNICAL_QC status for CRA review.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `StageDocumentQCRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `content` | `string` | ✔ Yes | Document content or base64 data |
| `filename` | `string` | ✔ Yes | Document filename |
| `mime_type` | `string` | No | MIME type |
| `study_id` | `string` | ✔ Yes | Study ID |
| `site_id` | `string` | `null` | No | Optional site ID |
| `artifact_hint` | `string` | `null` | No | Optional artifact type/code hint |
| `taxonomy_version` | `string` | `null` | No | Optional taxonomy version |
| `reason_for_change` | `string` | ✔ Yes | Part 11 change justification |
| `assigned_cra` | `string` | `null` | No | Optional assigned CRA reviewer |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/intelligence/stage-qc', {
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
        '/api/v1/etmf/api/v1/etmf/intelligence/stage-qc',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/etmf/api/v1/etmf/intelligence/stage-qc' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/etmf/intelligence/verify-signatures`

**Summary:** Verify regulatory signature completeness  
**Operation ID:** `verify_signatures_endpoint_api_v1_etmf_intelligence_verify_signatures_post`  

**Clinical & GxP Rationale:**  
Analyze signature lines, manifestations, and completeness for an artifact.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Payload Schema (`application/json`)

**Schema Type:** `DocumentIntelligenceSignatureRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `content` | `string` | ✔ Yes | Document content |
| `artifact_code` | `string` | ✔ Yes | DIA artifact code |
| `filename` | `string` | No | Document filename |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SignatureAnalysisResult` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/intelligence/verify-signatures', {
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
        '/api/v1/etmf/api/v1/etmf/intelligence/verify-signatures',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/etmf/api/v1/etmf/intelligence/verify-signatures' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/etmf/locks/trial/lock`

**Summary:** Etmf Lock Trial Endpoint  
**Operation ID:** `etmf_lock_trial_endpoint_api_v1_etmf_locks_trial_lock_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/locks/trial/lock', {
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
        '/api/v1/etmf/api/v1/etmf/locks/trial/lock',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/etmf/api/v1/etmf/locks/trial/lock' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/etmf/locks/trial/unlock`

**Summary:** Etmf Unlock Trial Endpoint  
**Operation ID:** `etmf_unlock_trial_endpoint_api_v1_etmf_locks_trial_unlock_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/locks/trial/unlock', {
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
        '/api/v1/etmf/api/v1/etmf/locks/trial/unlock',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/etmf/api/v1/etmf/locks/trial/unlock' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/etmf/studies/{study_id}/archive`

**Summary:** Bulk Archive Study Documents  
**Operation ID:** `bulk_archive_study_documents_api_v1_etmf_studies__study_id__archive_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `StudyArchiveRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `reason_for_change` | `string` | ✔ Yes | Part 11 change justification reason for bulk study archive |
| `all_or_nothing` | `boolean` | No | If True, rolling back the entire operation if any eligible document fails to transition. |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `StudyArchiveResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/studies/{study_id}/archive', {
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
        '/api/v1/etmf/api/v1/etmf/studies/{study_id}/archive',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/etmf/api/v1/etmf/studies/{study_id}/archive' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/etmf/studies/{study_id}/artifacts/{artifact_type}/history`

**Summary:** Get Artifact History  
**Operation ID:** `get_artifact_history_api_v1_etmf_studies__study_id__artifacts__artifact_type__history_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `artifact_type` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<DocumentResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/studies/{study_id}/artifacts/{artifact_type}/history', {
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
        '/api/v1/etmf/api/v1/etmf/studies/{study_id}/artifacts/{artifact_type}/history',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/etmf/api/v1/etmf/studies/{study_id}/artifacts/{artifact_type}/history' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/etmf/studies/{study_id}/binder`

**Summary:** Export Regulatory Binder  
**Operation ID:** `export_regulatory_binder_api_v1_etmf_studies__study_id__binder_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `include_history` | `query` | `boolean` | No | Include full version history of documents |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/studies/{study_id}/binder', {
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
        '/api/v1/etmf/api/v1/etmf/studies/{study_id}/binder',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/etmf/api/v1/etmf/studies/{study_id}/binder' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/etmf/studies/{study_id}/binder/structure`

**Summary:** Get Binder Structure  
**Operation ID:** `get_binder_structure_api_v1_etmf_studies__study_id__binder_structure_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `milestone` | `query` | `string` | `null` | No | Optional clinical study milestone |
| `site_id` | `query` | `string` | `null` | No | Optional clinical site ID |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `BinderStructureResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/studies/{study_id}/binder/structure', {
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
        '/api/v1/etmf/api/v1/etmf/studies/{study_id}/binder/structure',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/etmf/api/v1/etmf/studies/{study_id}/binder/structure' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/etmf/studies/{study_id}/ems-export`

**Summary:** Export Study Tmf Ems  
**Operation ID:** `export_study_tmf_ems_api_v1_etmf_studies__study_id__ems_export_get`  

**Clinical & GxP Rationale:**  
Exports the eTMF in standard DIA TMF Exchange Mechanism Standard (EMS) format.

Generates a compliant ZIP package containing `tmf-ems.xml`, `tmf-ems.json`,
`checksums.sha256`, and all document version assets organized in standard DIA hierarchy.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `study_title` | `query` | `string` | `null` | No | Optional study title |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/studies/{study_id}/ems-export', {
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
        '/api/v1/etmf/api/v1/etmf/studies/{study_id}/ems-export',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/etmf/api/v1/etmf/studies/{study_id}/ems-export' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/etmf/studies/{study_id}/inspection-readiness`

**Summary:** Get Study Inspection Readiness  
**Operation ID:** `get_study_inspection_readiness_api_v1_etmf_studies__study_id__inspection_readiness_get`  

**Clinical & GxP Rationale:**  
Calculates and returns a comprehensive inspection readiness assessment for a study.

Evaluates Expected Document List (EDL) completion, zone matrix distribution,
QC review bottlenecks, Part 11 electronic signature compliance, and expiration risks.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `InspectionReadinessResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/studies/{study_id}/inspection-readiness', {
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
        '/api/v1/etmf/api/v1/etmf/studies/{study_id}/inspection-readiness',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/etmf/api/v1/etmf/studies/{study_id}/inspection-readiness' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/etmf/studies/{study_id}/seed-edl`

**Summary:** Seed Edl Endpoint  
**Operation ID:** `seed_edl_endpoint_api_v1_etmf_studies__study_id__seed_edl_post`  

**Clinical & GxP Rationale:**  
Seed Expected Document List (EDL) for a study across DIA TMF zones and milestones.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SeedEDLRequest` | `null`

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `Array<ExpectedDocumentResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/studies/{study_id}/seed-edl', {
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
        '/api/v1/etmf/api/v1/etmf/studies/{study_id}/seed-edl',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/etmf/api/v1/etmf/studies/{study_id}/seed-edl' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/etmf/taxonomy`

**Summary:** Get Taxonomy Catalog  
**Operation ID:** `get_taxonomy_catalog_api_v1_etmf_taxonomy_get`  

**Clinical & GxP Rationale:**  
Expose the full static DIA TMF catalog as a browsable tree.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `version` | `query` | `string` | `null` | No | Optional taxonomy version |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `TaxonomyCatalogResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/taxonomy', {
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
        '/api/v1/etmf/api/v1/etmf/taxonomy',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/etmf/api/v1/etmf/taxonomy' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/etmf/taxonomy/classify`

**Summary:** Suggest Classification  
**Operation ID:** `suggest_classification_api_v1_etmf_taxonomy_classify_post`  

**Clinical & GxP Rationale:**  
Provide automatic classification/auto-filing suggestions for a document.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `AutoFileRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `filename` | `string` | ✔ Yes | — |
| `artifact_type` | `string` | `null` | No | Optional artifact type hint |
| `free_text` | `string` | `null` | No | Optional free-text hint |
| `study_id` | `string` | `null` | No | Optional study ID for scope enforcement |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `AutoFileResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/taxonomy/classify', {
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
        '/api/v1/etmf/api/v1/etmf/taxonomy/classify',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/etmf/api/v1/etmf/taxonomy/classify' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/etmf/test-exception`

**Summary:** Test Exception Route  
**Operation ID:** `test_exception_route_api_v1_etmf_test_exception_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

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
const response = await fetch('http://localhost:8000/api/v1/etmf/api/v1/etmf/test-exception', {
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
        '/api/v1/etmf/api/v1/etmf/test-exception',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/etmf/api/v1/etmf/test-exception' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/events/publish`

**Summary:** Ingest Document  
**Operation ID:** `ingest_document_events_publish_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `IngestionRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Unique identifier of the clinical study |
| `site_id` | `string` | `null` | No | Optional site identifier |
| `idempotency_key` | `string` | `null` | No | Optional idempotency key for deduplication |
| `artifact_type` | `string` | ✔ Yes | Type of artifact (e.g. Approved Protocol, Define-XML) |
| `filename` | `string` | ✔ Yes | Document filename |
| `content` | `string` | ✔ Yes | Indexed, searchable content of the document |
| `mime_type` | `string` | ✔ Yes | MIME type of the document |
| `zone` | `integer` | `null` | No | Optional expected DIA TMF Zone |
| `section` | `string` | `null` | No | Optional expected DIA TMF Section |
| `artifact_code` | `string` | `null` | No | Optional canonical artifact code |
| `taxonomy_version` | `string` | `null` | No | Optional taxonomy version |
| `metadata_json` | `object` | `null` | No | Optional metadata fields |
| `protocol_version` | `ProtocolVersionRefDTO` | `null` | No | Optional shared protocol version reference |
| `issue_date` | `string` | `null` | No | Optional document issue date |
| `expiration_date` | `string` | `null` | No | Optional document expiration date |
| `document_owner_id` | `string` | `null` | No | Optional document owner ID |
| `correlation_key` | `string` | `null` | No | Optional stable correlation key for synchronized documents |
| `content_checksum` | `string` | `null` | No | Optional deterministic checksum of the content |
| `source_system` | `string` | `null` | No | Optional originating source system |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/etmf/events/publish', {
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
        '/api/v1/etmf/events/publish',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/etmf/events/publish' \
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
No detailed description provided.

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
const response = await fetch('http://localhost:8000/api/v1/etmf/health', {
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
        '/api/v1/etmf/health',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/etmf/health' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---
