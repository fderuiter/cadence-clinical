# Electronic Investigator Site Files (eISF)

> **Functional Domain:** `Safety, Quality & Governance`  
> **Service Host Port:** `localhost:8016`  
> **Gateway Proxy Prefix:** `/api/v1/eisf`  
> **Persistence Backend:** `PostgreSQL / SQLite`  

## Overview

Site-scoped regulatory binder management, DIA eISF taxonomy inheritance, and PI certification audit manifests.

---

## Endpoints Summary

| Method | Path | Summary | GxP Auth & Signatures |
| :--- | :--- | :--- | :--- |
| `GET` | [/api/v1/eisf/binders/{site_id}](#get-api-v1-eisf-binders-site_id) | Get Site Binder Endpoint | 👀 Read-Only |
| `GET` | [/api/v1/eisf/completeness](#get-api-v1-eisf-completeness) | Get Binder Completeness | 👀 Read-Only |
| `GET` | [/api/v1/eisf/documents](#get-api-v1-eisf-documents) | List Documents | 👀 Read-Only |
| `POST` | [/api/v1/eisf/documents](#post-api-v1-eisf-documents) | Create Document | ✍️ Audited Write |
| `GET` | [/api/v1/eisf/documents/{document_id}](#get-api-v1-eisf-documents-document_id) | Get Document | 👀 Read-Only |
| `PUT` | [/api/v1/eisf/documents/{document_id}](#put-api-v1-eisf-documents-document_id) | Update Document | ✍️ Audited Write |
| `DELETE` | [/api/v1/eisf/documents/{document_id}](#delete-api-v1-eisf-documents-document_id) | Delete Document | ✍️ Audited Write |
| `GET` | [/api/v1/eisf/documents/{document_id}/download](#get-api-v1-eisf-documents-document_id-download) | Download Document | 👀 Read-Only |
| `POST` | [/api/v1/eisf/ingest](#post-api-v1-eisf-ingest) | Ingest Document | ✍️ Audited Write |
| `POST` | [/api/v1/eisf/intelligence/analyze](#post-api-v1-eisf-intelligence-analyze) | Analyze site document intelligence, recommend eISF section, and cross-map to eTMF | ✍️ Audited Write |
| `GET` | [/api/v1/eisf/sites/{site_id}/binder](#get-api-v1-eisf-sites-site_id-binder) | Get Site Eisf Binder | 👀 Read-Only |
| `POST` | [/api/v1/eisf/sites/{site_id}/documents/upload](#post-api-v1-eisf-sites-site_id-documents-upload) | Upload Site Document | ✍️ Audited Write |
| `GET` | [/api/v1/eisf/sites/{site_id}/documents/{doc_id}](#get-api-v1-eisf-sites-site_id-documents-doc_id) | Get Site Document Detail | 👀 Read-Only |
| `POST` | [/api/v1/eisf/sites/{site_id}/documents/{doc_id}/analyze](#post-api-v1-eisf-sites-site_id-documents-doc_id-analyze) | Run intelligence analysis on an existing site-scoped eISF document | ✍️ Audited Write |
| `GET` | [/api/v1/eisf/sites/{site_id}/documents/{doc_id}/download](#get-api-v1-eisf-sites-site_id-documents-doc_id-download) | Download Site Document | 👀 Read-Only |
| `POST` | [/api/v1/eisf/sync](#post-api-v1-eisf-sync) | Sync Documents | ✍️ Audited Write |
| `POST` | [/events/publish](#post-events-publish) | Ingest Document | ✍️ Audited Write |
| `GET` | [/health](#get-health) | Health Check | 👀 Read-Only |

---

## Granular Endpoint Specifications

### `GET` `/api/v1/eisf/binders/{site_id}`

**Summary:** Get Site Binder Endpoint  
**Operation ID:** `get_site_binder_endpoint_api_v1_eisf_binders__site_id__get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `site_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<DocumentResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/eisf/api/v1/eisf/binders/{site_id}', {
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
        '/api/v1/eisf/api/v1/eisf/binders/{site_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/eisf/api/v1/eisf/binders/{site_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/eisf/completeness`

**Summary:** Get Binder Completeness  
**Operation ID:** `get_binder_completeness_api_v1_eisf_completeness_get`  

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
| `site_id` | `query` | `string` | `null` | No | The site ID |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `BinderCompletenessResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/eisf/api/v1/eisf/completeness', {
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
        '/api/v1/eisf/api/v1/eisf/completeness',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/eisf/api/v1/eisf/completeness' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/eisf/documents`

**Summary:** List Documents  
**Operation ID:** `list_documents_api_v1_eisf_documents_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | — |
| `site_id` | `query` | `string` | `null` | No | — |
| `binder_section` | `query` | `string` | `null` | No | Filter by binder section / classification |
| `binder_classification` | `query` | `string` | `null` | No | Filter by binder section / classification |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<DocumentResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/eisf/api/v1/eisf/documents', {
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
        '/api/v1/eisf/api/v1/eisf/documents',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/eisf/api/v1/eisf/documents' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/eisf/documents`

**Summary:** Create Document  
**Operation ID:** `create_document_api_v1_eisf_documents_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `DocumentCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | The clinical study ID |
| `site_id` | `string` | ✔ Yes | The clinical site ID |
| `binder_classification` | `string` | ✔ Yes | Binder classification |
| `filename` | `string` | ✔ Yes | Document filename |
| `content` | `string` | ✔ Yes | Base64 or raw content |
| `mime_type` | `string` | ✔ Yes | MIME type |
| `metadata_json` | `object` | `null` | No | Metadata JSON |
| `correlation_key` | `string` | `null` | No | Correlation key |
| `content_checksum` | `string` | `null` | No | Content checksum |
| `source_system` | `string` | No | Source system name |
| `reason_for_change` | `string` | ✔ Yes | Part 11 reason for change |
| `issue_date` | `string` | `null` | No | Optional document issue date |
| `expiration_date` | `string` | `null` | No | Optional document expiration date |
| `document_owner_id` | `string` | `null` | No | Optional document owner ID |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `DocumentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/eisf/api/v1/eisf/documents', {
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
        '/api/v1/eisf/api/v1/eisf/documents',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/eisf/api/v1/eisf/documents' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/eisf/documents/{document_id}`

**Summary:** Get Document  
**Operation ID:** `get_document_api_v1_eisf_documents__document_id__get`  

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
const response = await fetch('http://localhost:8000/api/v1/eisf/api/v1/eisf/documents/{document_id}', {
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
        '/api/v1/eisf/api/v1/eisf/documents/{document_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/eisf/api/v1/eisf/documents/{document_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/eisf/documents/{document_id}`

**Summary:** Update Document  
**Operation ID:** `update_document_api_v1_eisf_documents__document_id__put`  

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

**Schema Type:** `DocumentUpdate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | The clinical study ID |
| `site_id` | `string` | ✔ Yes | The clinical site ID |
| `binder_classification` | `string` | ✔ Yes | Binder classification |
| `filename` | `string` | ✔ Yes | Document filename |
| `content` | `string` | ✔ Yes | Base64 or raw content |
| `mime_type` | `string` | ✔ Yes | MIME type |
| `metadata_json` | `object` | `null` | No | Metadata JSON |
| `correlation_key` | `string` | `null` | No | Correlation key |
| `content_checksum` | `string` | `null` | No | Content checksum |
| `source_system` | `string` | No | Source system name |
| `reason_for_change` | `string` | ✔ Yes | Part 11 reason for change |
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
const response = await fetch('http://localhost:8000/api/v1/eisf/api/v1/eisf/documents/{document_id}', {
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
        '/api/v1/eisf/api/v1/eisf/documents/{document_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/eisf/api/v1/eisf/documents/{document_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `DELETE` `/api/v1/eisf/documents/{document_id}`

**Summary:** Delete Document  
**Operation ID:** `delete_document_api_v1_eisf_documents__document_id__delete`  

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
| `reason_for_change` | `query` | `string` | ✔ Yes | Part 11 reason for change |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `204` | Successful Response | `any` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/eisf/api/v1/eisf/documents/{document_id}', {
  method: 'DELETE',
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
    response = await client.delete(
        '/api/v1/eisf/api/v1/eisf/documents/{document_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/eisf/api/v1/eisf/documents/{document_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/eisf/documents/{document_id}/download`

**Summary:** Download Document  
**Operation ID:** `download_document_api_v1_eisf_documents__document_id__download_get`  

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
const response = await fetch('http://localhost:8000/api/v1/eisf/api/v1/eisf/documents/{document_id}/download', {
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
        '/api/v1/eisf/api/v1/eisf/documents/{document_id}/download',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/eisf/api/v1/eisf/documents/{document_id}/download' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/eisf/ingest`

**Summary:** Ingest Document  
**Operation ID:** `ingest_document_api_v1_eisf_ingest_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `EISFIngestionRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | The clinical study ID |
| `site_id` | `string` | ✔ Yes | The clinical site ID |
| `binder_classification` | `string` | `null` | No | Binder classification |
| `artifact_type` | `string` | `null` | No | Artifact classification metadata alias for binder_classification |
| `filename` | `string` | ✔ Yes | Document filename |
| `content` | `string` | ✔ Yes | Base64 or raw content |
| `mime_type` | `string` | ✔ Yes | MIME type |
| `metadata_json` | `object` | `null` | No | Metadata JSON |
| `correlation_key` | `string` | `null` | No | Correlation key |
| `content_checksum` | `string` | `null` | No | Content checksum |
| `source_system` | `string` | No | Source system name |
| `reason_for_change` | `string` | `null` | No | Part 11 reason for change |
| `issue_date` | `string` | `null` | No | Optional document issue date |
| `expiration_date` | `string` | `null` | No | Optional document expiration date |
| `document_owner_id` | `string` | `null` | No | Optional document owner ID |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `DocumentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/eisf/api/v1/eisf/ingest', {
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
        '/api/v1/eisf/api/v1/eisf/ingest',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/eisf/api/v1/eisf/ingest' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/eisf/intelligence/analyze`

**Summary:** Analyze site document intelligence, recommend eISF section, and cross-map to eTMF  
**Operation ID:** `eisf_analyze_document_endpoint_api_v1_eisf_intelligence_analyze_post`  

**Clinical & GxP Rationale:**  
Run multimodal intelligence pipeline for site-level document.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `EISFDocumentIntelligenceRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `content` | `string` | ✔ Yes | Raw text or base64 data |
| `filename` | `string` | ✔ Yes | Document filename |
| `site_id` | `string` | `null` | No | Optional site ID hint |
| `study_id` | `string` | `null` | No | Optional study ID hint |
| `mime_type` | `string` | No | MIME type |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `EISFDocumentIntelligenceResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/eisf/api/v1/eisf/intelligence/analyze', {
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
        '/api/v1/eisf/api/v1/eisf/intelligence/analyze',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/eisf/api/v1/eisf/intelligence/analyze' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/eisf/sites/{site_id}/binder`

**Summary:** Get Site Eisf Binder  
**Operation ID:** `get_site_eisf_binder_api_v1_eisf_sites__site_id__binder_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `site_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<EISFFolderNode>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/eisf/api/v1/eisf/sites/{site_id}/binder', {
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
        '/api/v1/eisf/api/v1/eisf/sites/{site_id}/binder',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/eisf/api/v1/eisf/sites/{site_id}/binder' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/eisf/sites/{site_id}/documents/upload`

**Summary:** Upload Site Document  
**Operation ID:** `upload_site_document_api_v1_eisf_sites__site_id__documents_upload_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `site_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `EISFDocumentUploadRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Unique protocol study identifier |
| `section_code` | `string` | ✔ Yes | Section code classification for the document |
| `filename` | `string` | ✔ Yes | Document file name |
| `content` | `string` | ✔ Yes | Base64 or raw text content of the document |
| `mime_type` | `string` | ✔ Yes | MIME type of the document |
| `reason_for_change` | `string` | ✔ Yes | Part 11 change justification |
| `expiration_date` | `string` | `null` | No | Optional document expiration date (YYYY-MM-DD) |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `EISFDocumentDetail` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/eisf/api/v1/eisf/sites/{site_id}/documents/upload', {
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
        '/api/v1/eisf/api/v1/eisf/sites/{site_id}/documents/upload',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/eisf/api/v1/eisf/sites/{site_id}/documents/upload' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/eisf/sites/{site_id}/documents/{doc_id}`

**Summary:** Get Site Document Detail  
**Operation ID:** `get_site_document_detail_api_v1_eisf_sites__site_id__documents__doc_id__get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `site_id` | `path` | `string` | ✔ Yes | — |
| `doc_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `EISFDocumentDetail` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/eisf/api/v1/eisf/sites/{site_id}/documents/{doc_id}', {
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
        '/api/v1/eisf/api/v1/eisf/sites/{site_id}/documents/{doc_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/eisf/api/v1/eisf/sites/{site_id}/documents/{doc_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/eisf/sites/{site_id}/documents/{doc_id}/analyze`

**Summary:** Run intelligence analysis on an existing site-scoped eISF document  
**Operation ID:** `eisf_analyze_existing_document_endpoint_api_v1_eisf_sites__site_id__documents__doc_id__analyze_post`  

**Clinical & GxP Rationale:**  
Analyze an existing eISF document.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `site_id` | `path` | `string` | ✔ Yes | — |
| `doc_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `EISFDocumentIntelligenceResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/eisf/api/v1/eisf/sites/{site_id}/documents/{doc_id}/analyze', {
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
        '/api/v1/eisf/api/v1/eisf/sites/{site_id}/documents/{doc_id}/analyze',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/eisf/api/v1/eisf/sites/{site_id}/documents/{doc_id}/analyze' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/eisf/sites/{site_id}/documents/{doc_id}/download`

**Summary:** Download Site Document  
**Operation ID:** `download_site_document_api_v1_eisf_sites__site_id__documents__doc_id__download_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `site_id` | `path` | `string` | ✔ Yes | — |
| `doc_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/eisf/api/v1/eisf/sites/{site_id}/documents/{doc_id}/download', {
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
        '/api/v1/eisf/api/v1/eisf/sites/{site_id}/documents/{doc_id}/download',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/eisf/api/v1/eisf/sites/{site_id}/documents/{doc_id}/download' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/eisf/sync`

**Summary:** Sync Documents  
**Operation ID:** `sync_documents_api_v1_eisf_sync_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `EISFSyncRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `submissions` | `Array<EISFSyncItem>` | ✔ Yes | List of sync items |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `EISFSyncResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/eisf/api/v1/eisf/sync', {
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
        '/api/v1/eisf/api/v1/eisf/sync',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/eisf/api/v1/eisf/sync' \
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

**Schema Type:** `EISFIngestionRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | The clinical study ID |
| `site_id` | `string` | ✔ Yes | The clinical site ID |
| `binder_classification` | `string` | `null` | No | Binder classification |
| `artifact_type` | `string` | `null` | No | Artifact classification metadata alias for binder_classification |
| `filename` | `string` | ✔ Yes | Document filename |
| `content` | `string` | ✔ Yes | Base64 or raw content |
| `mime_type` | `string` | ✔ Yes | MIME type |
| `metadata_json` | `object` | `null` | No | Metadata JSON |
| `correlation_key` | `string` | `null` | No | Correlation key |
| `content_checksum` | `string` | `null` | No | Content checksum |
| `source_system` | `string` | No | Source system name |
| `reason_for_change` | `string` | `null` | No | Part 11 reason for change |
| `issue_date` | `string` | `null` | No | Optional document issue date |
| `expiration_date` | `string` | `null` | No | Optional document expiration date |
| `document_owner_id` | `string` | `null` | No | Optional document owner ID |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `DocumentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/eisf/events/publish', {
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
        '/api/v1/eisf/events/publish',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/eisf/events/publish' \
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
const response = await fetch('http://localhost:8000/api/v1/eisf/health', {
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
        '/api/v1/eisf/health',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/eisf/health' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---
