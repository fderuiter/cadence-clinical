# Object Storage & FileShare Gateway

> **Functional Domain:** `Safety, Quality & Governance`  
> **Service Host Port:** `localhost:8018`  
> **Gateway Proxy Prefix:** `/api/v1/fileshare`  
> **Persistence Backend:** `MinIO / S3 & SQL Envelope`  

## Overview

StoragePort abstraction over MinIO/S3, presigned transfer URLs, cryptographic guest links, and 21 CFR Part 11 document envelopes.

---

## Endpoints Summary

| Method | Path | Summary | GxP Auth & Signatures |
| :--- | :--- | :--- | :--- |
| `GET` | [/api/v1/files](#get-api-v1-files) | List Files | 👀 Read-Only |
| `POST` | [/api/v1/files/audit/playback](#post-api-v1-files-audit-playback) | Record Playback Checkpoint | ✍️ Audited Write |
| `GET` | [/api/v1/files/guest/{token}](#get-api-v1-files-guest-token) | Get External Guest File Download | 👀 Read-Only |
| `POST` | [/api/v1/files/upload-url](#post-api-v1-files-upload-url) | Get Upload Url | ✍️ Audited Write |
| `POST` | [/api/v1/files/upload/presigned-url](#post-api-v1-files-upload-presigned-url) | Get Upload Url | 🔒 Step-Up Sig Token |
| `GET` | [/api/v1/files/{file_id}](#get-api-v1-files-file_id) | Get File Record | 👀 Read-Only |
| `DELETE` | [/api/v1/files/{file_id}](#delete-api-v1-files-file_id) | Delete File Record | ✍️ Audited Write |
| `POST` | [/api/v1/files/{file_id}/abort-upload](#post-api-v1-files-file_id-abort-upload) | Abort File Upload | ✍️ Audited Write |
| `GET` | [/api/v1/files/{file_id}/activity](#get-api-v1-files-file_id-activity) | Get File Activity Feed | 👀 Read-Only |
| `POST` | [/api/v1/files/{file_id}/approve](#post-api-v1-files-file_id-approve) | Approve Document Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/files/{file_id}/audit-logs](#get-api-v1-files-file_id-audit-logs) | Get File Audit Logs | 👀 Read-Only |
| `POST` | [/api/v1/files/{file_id}/complete](#post-api-v1-files-file_id-complete) | Complete File Upload | ✍️ Audited Write |
| `POST` | [/api/v1/files/{file_id}/complete-upload](#post-api-v1-files-file_id-complete-upload) | Complete File Upload | ✍️ Audited Write |
| `GET` | [/api/v1/files/{file_id}/download-url](#get-api-v1-files-file_id-download-url) | Get Download Url | 👀 Read-Only |
| `GET` | [/api/v1/files/{file_id}/grants](#get-api-v1-files-file_id-grants) | List Share Grants | 👀 Read-Only |
| `POST` | [/api/v1/files/{file_id}/grants](#post-api-v1-files-file_id-grants) | Create Share Grant | ✍️ Audited Write |
| `DELETE` | [/api/v1/files/{file_id}/grants/{grant_id}](#delete-api-v1-files-file_id-grants-grant_id) | Delete Share Grant | ✍️ Audited Write |
| `GET` | [/api/v1/files/{file_id}/guest-links](#get-api-v1-files-file_id-guest-links) | List Guest Links | 👀 Read-Only |
| `POST` | [/api/v1/files/{file_id}/guest-links](#post-api-v1-files-file_id-guest-links) | Create Guest Link | ✍️ Audited Write |
| `DELETE` | [/api/v1/files/{file_id}/guest-links/{guest_link_id}](#delete-api-v1-files-file_id-guest-links-guest_link_id) | Revoke Guest Link | ✍️ Audited Write |
| `POST` | [/api/v1/files/{file_id}/hold](#post-api-v1-files-file_id-hold) | Place File On Hold | ✍️ Audited Write |
| `DELETE` | [/api/v1/files/{file_id}/hold](#delete-api-v1-files-file_id-hold) | Remove File From Hold | ✍️ Audited Write |
| `GET` | [/api/v1/files/{file_id}/preview-url](#get-api-v1-files-file_id-preview-url) | Get Preview Url | 👀 Read-Only |
| `POST` | [/api/v1/files/{file_id}/reject](#post-api-v1-files-file_id-reject) | Reject Document Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/files/{file_id}/rescind-approval](#post-api-v1-files-file_id-rescind-approval) | Rescind Approval Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/files/{file_id}/stream-url](#get-api-v1-files-file_id-stream-url) | Get Stream Url | 👀 Read-Only |
| `POST` | [/api/v1/files/{file_id}/upload-complete](#post-api-v1-files-file_id-upload-complete) | Complete File Upload | ✍️ Audited Write |
| `POST` | [/api/v1/files/{file_id}/upload-revision](#post-api-v1-files-file_id-upload-revision) | Upload Revision Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/fileshare/audit/playback](#post-api-v1-fileshare-audit-playback) | Record Playback Checkpoint | ✍️ Audited Write |
| `GET` | [/api/v1/fileshare/files](#get-api-v1-fileshare-files) | List Files | 👀 Read-Only |
| `GET` | [/api/v1/fileshare/files/guest/{token}](#get-api-v1-fileshare-files-guest-token) | Get Guest File Download | 👀 Read-Only |
| `POST` | [/api/v1/fileshare/files/upload-url](#post-api-v1-fileshare-files-upload-url) | Get Upload Url | ✍️ Audited Write |
| `POST` | [/api/v1/fileshare/files/upload/presigned-url](#post-api-v1-fileshare-files-upload-presigned-url) | Get Upload Url | 🔒 Step-Up Sig Token |
| `GET` | [/api/v1/fileshare/files/{file_id}](#get-api-v1-fileshare-files-file_id) | Get File Record | 👀 Read-Only |
| `DELETE` | [/api/v1/fileshare/files/{file_id}](#delete-api-v1-fileshare-files-file_id) | Delete File Record | ✍️ Audited Write |
| `POST` | [/api/v1/fileshare/files/{file_id}/abort-upload](#post-api-v1-fileshare-files-file_id-abort-upload) | Abort File Upload | ✍️ Audited Write |
| `GET` | [/api/v1/fileshare/files/{file_id}/activity](#get-api-v1-fileshare-files-file_id-activity) | Get File Activity Feed | 👀 Read-Only |
| `POST` | [/api/v1/fileshare/files/{file_id}/approve](#post-api-v1-fileshare-files-file_id-approve) | Approve Document Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/fileshare/files/{file_id}/audit-logs](#get-api-v1-fileshare-files-file_id-audit-logs) | Get File Audit Logs | 👀 Read-Only |
| `POST` | [/api/v1/fileshare/files/{file_id}/complete](#post-api-v1-fileshare-files-file_id-complete) | Complete File Upload | ✍️ Audited Write |
| `POST` | [/api/v1/fileshare/files/{file_id}/complete-upload](#post-api-v1-fileshare-files-file_id-complete-upload) | Complete File Upload | ✍️ Audited Write |
| `GET` | [/api/v1/fileshare/files/{file_id}/download-url](#get-api-v1-fileshare-files-file_id-download-url) | Get Download Url | 👀 Read-Only |
| `GET` | [/api/v1/fileshare/files/{file_id}/grants](#get-api-v1-fileshare-files-file_id-grants) | List Share Grants | 👀 Read-Only |
| `POST` | [/api/v1/fileshare/files/{file_id}/grants](#post-api-v1-fileshare-files-file_id-grants) | Create Share Grant | ✍️ Audited Write |
| `DELETE` | [/api/v1/fileshare/files/{file_id}/grants/{grant_id}](#delete-api-v1-fileshare-files-file_id-grants-grant_id) | Delete Share Grant | ✍️ Audited Write |
| `GET` | [/api/v1/fileshare/files/{file_id}/guest-links](#get-api-v1-fileshare-files-file_id-guest-links) | List Guest Links | 👀 Read-Only |
| `POST` | [/api/v1/fileshare/files/{file_id}/guest-links](#post-api-v1-fileshare-files-file_id-guest-links) | Create Guest Link | ✍️ Audited Write |
| `DELETE` | [/api/v1/fileshare/files/{file_id}/guest-links/{guest_link_id}](#delete-api-v1-fileshare-files-file_id-guest-links-guest_link_id) | Revoke Guest Link | ✍️ Audited Write |
| `POST` | [/api/v1/fileshare/files/{file_id}/hold](#post-api-v1-fileshare-files-file_id-hold) | Place File On Hold | ✍️ Audited Write |
| `DELETE` | [/api/v1/fileshare/files/{file_id}/hold](#delete-api-v1-fileshare-files-file_id-hold) | Remove File From Hold | ✍️ Audited Write |
| `GET` | [/api/v1/fileshare/files/{file_id}/preview-url](#get-api-v1-fileshare-files-file_id-preview-url) | Get Preview Url | 👀 Read-Only |
| `POST` | [/api/v1/fileshare/files/{file_id}/reject](#post-api-v1-fileshare-files-file_id-reject) | Reject Document Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/fileshare/files/{file_id}/rescind-approval](#post-api-v1-fileshare-files-file_id-rescind-approval) | Rescind Approval Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/fileshare/files/{file_id}/stream-url](#get-api-v1-fileshare-files-file_id-stream-url) | Get Stream Url | 👀 Read-Only |
| `POST` | [/api/v1/fileshare/files/{file_id}/upload-complete](#post-api-v1-fileshare-files-file_id-upload-complete) | Complete File Upload | ✍️ Audited Write |
| `POST` | [/api/v1/fileshare/files/{file_id}/upload-revision](#post-api-v1-fileshare-files-file_id-upload-revision) | Upload Revision Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/fileshare/guest/{token}](#get-api-v1-fileshare-guest-token) | Get External Guest File Download | 👀 Read-Only |
| `GET` | [/health](#get-health) | Health Check | 👀 Read-Only |

---

## Granular Endpoint Specifications

### `GET` `/api/v1/files`

**Summary:** List Files  
**Operation ID:** `list_files_api_v1_files_get`  

**Clinical & GxP Rationale:**  
List active file records for an optional study, site, and search query.

Requirements: PRD-SYS-001, PRD-DOC-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | — |
| `site_id` | `query` | `string` | `null` | No | — |
| `q` | `query` | `string` | `null` | No | — |
| `page` | `query` | `integer` | No | — |
| `page_size` | `query` | `integer` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<FileRecordResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files', {
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
        '/api/v1/fileshare/api/v1/files',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/fileshare/api/v1/files' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/files/audit/playback`

**Summary:** Record Playback Checkpoint  
**Operation ID:** `record_playback_checkpoint_api_v1_files_audit_playback_post`  

**Clinical & GxP Rationale:**  
Record a viewer session video playback checkpoint audit event.

Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `PlaybackAuditCreateRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `file_record_id` | `string` | ✔ Yes | — |
| `event` | `PlaybackEventType` | ✔ Yes | — |
| `source` | `PlaybackEventSource` | No | — |
| `session_id` | `string` | ✔ Yes | — |
| `guest_token` | `string` | `null` | No | — |
| `reason_for_change` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `PlaybackAuditResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/audit/playback', {
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
        '/api/v1/fileshare/api/v1/files/audit/playback',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/files/audit/playback' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/files/guest/{token}`

**Summary:** Get External Guest File Download  
**Operation ID:** `get_external_guest_file_download_api_v1_files_guest__token__get`  

**Clinical & GxP Rationale:**  
Public resolution endpoint for external guest access tokens.

Requirements: PRD-SYS-001, PRD-DOC-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `token` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileDownloadUrlResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/guest/{token}', {
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
        '/api/v1/fileshare/api/v1/files/guest/{token}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/fileshare/api/v1/files/guest/{token}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/files/upload-url`

**Summary:** Get Upload Url  
**Operation ID:** `get_upload_url_api_v1_files_upload_url_post`  

**Clinical & GxP Rationale:**  
Allocate draft file record and generate presigned PUT or multipart upload URLs.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `FileUploadUrlRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | — |
| `site_id` | `string` | `null` | No | — |
| `filename` | `string` | ✔ Yes | — |
| `mime_type` | `string` | ✔ Yes | — |
| `size_bytes` | `integer` | ✔ Yes | — |
| `is_multipart` | `boolean` | No | — |
| `parts_count` | `integer` | No | — |
| `checksum_sha256` | `string` | `null` | No | — |
| `reason_for_change` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `FileUploadUrlResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/upload-url', {
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
        '/api/v1/fileshare/api/v1/files/upload-url',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/files/upload-url' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/files/upload/presigned-url`

**Summary:** Get Upload Url  
**Operation ID:** `get_upload_url_api_v1_files_upload_presigned_url_post`  

**Clinical & GxP Rationale:**  
Allocate draft file record and generate presigned PUT or multipart upload URLs.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Payload Schema (`application/json`)

**Schema Type:** `FileUploadUrlRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | — |
| `site_id` | `string` | `null` | No | — |
| `filename` | `string` | ✔ Yes | — |
| `mime_type` | `string` | ✔ Yes | — |
| `size_bytes` | `integer` | ✔ Yes | — |
| `is_multipart` | `boolean` | No | — |
| `parts_count` | `integer` | No | — |
| `checksum_sha256` | `string` | `null` | No | — |
| `reason_for_change` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `FileUploadUrlResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/upload/presigned-url', {
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
        '/api/v1/fileshare/api/v1/files/upload/presigned-url',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/files/upload/presigned-url' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/files/{file_id}`

**Summary:** Get File Record  
**Operation ID:** `get_file_record_api_v1_files__file_id__get`  

**Clinical & GxP Rationale:**  
Retrieve file record metadata.

Requirements: PRD-SYS-001, PRD-DOC-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileRecordResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}', {
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
        '/api/v1/fileshare/api/v1/files/{file_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `DELETE` `/api/v1/files/{file_id}`

**Summary:** Delete File Record  
**Operation ID:** `delete_file_record_api_v1_files__file_id__delete`  

**Clinical & GxP Rationale:**  
Soft delete a file record.

Gated on FileRecord.is_on_hold being False; returns 409 if file is on hold.
Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `FileDeleteRequest` | `null`

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileRecordResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}', {
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
        '/api/v1/fileshare/api/v1/files/{file_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/files/{file_id}/abort-upload`

**Summary:** Abort File Upload  
**Operation ID:** `abort_file_upload_api_v1_files__file_id__abort_upload_post`  

**Clinical & GxP Rationale:**  
Abort multipart upload session and mark file record deleted.

Requirements: PRD-SYS-001, PRD-DOC-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |
| `upload_id` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileRecordResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/abort-upload', {
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
        '/api/v1/fileshare/api/v1/files/{file_id}/abort-upload',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/abort-upload' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/files/{file_id}/activity`

**Summary:** Get File Activity Feed  
**Operation ID:** `get_file_activity_feed_api_v1_files__file_id__activity_get`  

**Clinical & GxP Rationale:**  
Retrieve paginated per-document activity feed audit log entries for inspection readiness.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, 21 CFR Part 11

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |
| `event_type` | `query` | `string` | `null` | No | — |
| `from_date` | `query` | `string` | `null` | No | — |
| `to_date` | `query` | `string` | `null` | No | — |
| `page` | `query` | `integer` | No | — |
| `page_size` | `query` | `integer` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ActivityFeedResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/activity', {
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
        '/api/v1/fileshare/api/v1/files/{file_id}/activity',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/activity' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/files/{file_id}/approve`

**Summary:** Approve Document Endpoint  
**Operation ID:** `approve_document_endpoint_api_v1_files__file_id__approve_post`  

**Clinical & GxP Rationale:**  
Execute 21 CFR Part 11 electronic signature document approval with in-product re-authentication.

Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `FileApproveRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `signing_reason` | `string` | ✔ Yes | — |
| `credential` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileRecordResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/approve', {
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
        '/api/v1/fileshare/api/v1/files/{file_id}/approve',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/approve' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/files/{file_id}/audit-logs`

**Summary:** Get File Audit Logs  
**Operation ID:** `get_file_audit_logs_api_v1_files__file_id__audit_logs_get`  

**Clinical & GxP Rationale:**  
Retrieve immutable Part 11 audit log entries for a file record.

Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<FileAuditLogResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/audit-logs', {
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
        '/api/v1/fileshare/api/v1/files/{file_id}/audit-logs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/audit-logs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/files/{file_id}/complete`

**Summary:** Complete File Upload  
**Operation ID:** `complete_file_upload_api_v1_files__file_id__complete_post`  

**Clinical & GxP Rationale:**  
Finalize singlepart or multipart upload and trigger asynchronous video transcoding if applicable.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `UploadCompleteRequest` | `null`

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileRecordResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/complete', {
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
        '/api/v1/fileshare/api/v1/files/{file_id}/complete',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/complete' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/files/{file_id}/complete-upload`

**Summary:** Complete File Upload  
**Operation ID:** `complete_file_upload_api_v1_files__file_id__complete_upload_post`  

**Clinical & GxP Rationale:**  
Finalize singlepart or multipart upload and trigger asynchronous video transcoding if applicable.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `UploadCompleteRequest` | `null`

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileRecordResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/complete-upload', {
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
        '/api/v1/fileshare/api/v1/files/{file_id}/complete-upload',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/complete-upload' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/files/{file_id}/download-url`

**Summary:** Get Download Url  
**Operation ID:** `get_download_url_api_v1_files__file_id__download_url_get`  

**Clinical & GxP Rationale:**  
Retrieve short-lived presigned GET download URL with permission and watermark verification.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-003

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |
| `original` | `query` | `boolean` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileDownloadUrlResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/download-url', {
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
        '/api/v1/fileshare/api/v1/files/{file_id}/download-url',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/download-url' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/files/{file_id}/grants`

**Summary:** List Share Grants  
**Operation ID:** `list_share_grants_api_v1_files__file_id__grants_get`  

**Clinical & GxP Rationale:**  
Retrieve active internal share grants for a file record.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-003

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<ShareGrantResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/grants', {
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
        '/api/v1/fileshare/api/v1/files/{file_id}/grants',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/grants' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/files/{file_id}/grants`

**Summary:** Create Share Grant  
**Operation ID:** `create_share_grant_api_v1_files__file_id__grants_post`  

**Clinical & GxP Rationale:**  
Delegate file permissions via an internal share grant.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-003

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ShareGrantCreateRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `granted_to_user_id` | `string` | `null` | No | — |
| `scope` | `ShareScope` | No | — |
| `permission_level` | `PermissionLevel` | No | — |
| `expires_at` | `string` | `null` | No | — |
| `reason_for_change` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `ShareGrantResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/grants', {
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
        '/api/v1/fileshare/api/v1/files/{file_id}/grants',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/grants' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `DELETE` `/api/v1/files/{file_id}/grants/{grant_id}`

**Summary:** Delete Share Grant  
**Operation ID:** `delete_share_grant_api_v1_files__file_id__grants__grant_id__delete`  

**Clinical & GxP Rationale:**  
Revoke an internal share grant delegation.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-003

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |
| `grant_id` | `path` | `string` | ✔ Yes | — |
| `reason_for_change` | `query` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ShareGrantResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/grants/{grant_id}', {
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
        '/api/v1/fileshare/api/v1/files/{file_id}/grants/{grant_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/grants/{grant_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/files/{file_id}/guest-links`

**Summary:** List Guest Links  
**Operation ID:** `list_guest_links_api_v1_files__file_id__guest_links_get`  

**Clinical & GxP Rationale:**  
Retrieve active guest links for a file record.

Requirements: PRD-SYS-001, PRD-DOC-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<GuestLinkResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/guest-links', {
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
        '/api/v1/fileshare/api/v1/files/{file_id}/guest-links',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/guest-links' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/files/{file_id}/guest-links`

**Summary:** Create Guest Link  
**Operation ID:** `create_guest_link_api_v1_files__file_id__guest_links_post`  

**Clinical & GxP Rationale:**  
Generate a time-bounded external guest link.

Requirements: PRD-SYS-001, PRD-DOC-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `GuestLinkCreateRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `expires_in_hours` | `integer` | No | — |
| `reason_for_change` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `GuestLinkResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/guest-links', {
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
        '/api/v1/fileshare/api/v1/files/{file_id}/guest-links',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/guest-links' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `DELETE` `/api/v1/files/{file_id}/guest-links/{guest_link_id}`

**Summary:** Revoke Guest Link  
**Operation ID:** `revoke_guest_link_api_v1_files__file_id__guest_links__guest_link_id__delete`  

**Clinical & GxP Rationale:**  
Revoke an active external guest link.

Requirements: PRD-SYS-001, PRD-DOC-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |
| `guest_link_id` | `path` | `string` | ✔ Yes | — |
| `reason_for_change` | `query` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `GuestLinkResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/guest-links/{guest_link_id}', {
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
        '/api/v1/fileshare/api/v1/files/{file_id}/guest-links/{guest_link_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/guest-links/{guest_link_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/files/{file_id}/hold`

**Summary:** Place File On Hold  
**Operation ID:** `place_file_on_hold_api_v1_files__file_id__hold_post`  

**Clinical & GxP Rationale:**  
Place a file under retention/legal hold preventing deletion or archival.

Requires super_admin or data_manager role.
Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `FileHoldRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `reason_for_change` | `string` | ✔ Yes | — |
| `hold_reason` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileRecordResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/hold', {
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
        '/api/v1/fileshare/api/v1/files/{file_id}/hold',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/hold' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `DELETE` `/api/v1/files/{file_id}/hold`

**Summary:** Remove File From Hold  
**Operation ID:** `remove_file_from_hold_api_v1_files__file_id__hold_delete`  

**Clinical & GxP Rationale:**  
Remove retention/legal hold from a file record.

Requires super_admin or data_manager role.
Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `FileHoldClearRequest` | `null`

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileRecordResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/hold', {
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
        '/api/v1/fileshare/api/v1/files/{file_id}/hold',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/hold' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/files/{file_id}/preview-url`

**Summary:** Get Preview Url  
**Operation ID:** `get_preview_url_api_v1_files__file_id__preview_url_get`  

**Clinical & GxP Rationale:**  
Retrieve short-lived presigned preview URL (PDF rendition if available, otherwise original).

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |
| `original` | `query` | `boolean` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileDownloadUrlResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/preview-url', {
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
        '/api/v1/fileshare/api/v1/files/{file_id}/preview-url',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/preview-url' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/files/{file_id}/reject`

**Summary:** Reject Document Endpoint  
**Operation ID:** `reject_document_endpoint_api_v1_files__file_id__reject_post`  

**Clinical & GxP Rationale:**  
Reject a document for approval.

Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `FileRejectRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `reason_for_change` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileRecordResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/reject', {
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
        '/api/v1/fileshare/api/v1/files/{file_id}/reject',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/reject' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/files/{file_id}/rescind-approval`

**Summary:** Rescind Approval Endpoint  
**Operation ID:** `rescind_approval_endpoint_api_v1_files__file_id__rescind_approval_post`  

**Clinical & GxP Rationale:**  
Rescind a previous document approval. Requires super_admin.

Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `FileRescindApprovalRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `reason_for_change` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileRecordResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/rescind-approval', {
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
        '/api/v1/fileshare/api/v1/files/{file_id}/rescind-approval',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/rescind-approval' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/files/{file_id}/stream-url`

**Summary:** Get Stream Url  
**Operation ID:** `get_stream_url_api_v1_files__file_id__stream_url_get`  

**Clinical & GxP Rationale:**  
Issue short-lived presigned HLS master manifest stream URL for video playback.

Returns HTTP 200 with presigned URL when transcoding is READY,
HTTP 202 Accepted when transcoding is PENDING or IN_PROGRESS,
HTTP 409 Conflict when transcoding has FAILED,
and HTTP 400 Bad Request when transcoding was NOT_REQUIRED.

Requirements: PRD-SYS-001, PRD-DOC-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | HLS master manifest presigned URL is ready for streaming. | `StreamUrlResponse` |
| `202` | Video transcoding is pending or in progress. | `any` |
| `400` | Transcoding is not required for non-video file. | `any` |
| `409` | Video transcoding failed. | `any` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/stream-url', {
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
        '/api/v1/fileshare/api/v1/files/{file_id}/stream-url',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/stream-url' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/files/{file_id}/upload-complete`

**Summary:** Complete File Upload  
**Operation ID:** `complete_file_upload_api_v1_files__file_id__upload_complete_post`  

**Clinical & GxP Rationale:**  
Finalize singlepart or multipart upload and trigger asynchronous video transcoding if applicable.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `UploadCompleteRequest` | `null`

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileRecordResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/upload-complete', {
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
        '/api/v1/fileshare/api/v1/files/{file_id}/upload-complete',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/upload-complete' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/files/{file_id}/upload-revision`

**Summary:** Upload Revision Endpoint  
**Operation ID:** `upload_revision_endpoint_api_v1_files__file_id__upload_revision_post`  

**Clinical & GxP Rationale:**  
Initiate upload of a new version/revision for a file record. Blocks if approved and immutable.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, 21 CFR Part 11

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `UploadRevisionRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `filename` | `string` | ✔ Yes | — |
| `mime_type` | `string` | ✔ Yes | — |
| `size_bytes` | `integer` | ✔ Yes | — |
| `checksum_sha256` | `string` | `null` | No | — |
| `reason_for_change` | `string` | ✔ Yes | — |
| `is_multipart` | `boolean` | No | — |
| `parts_count` | `integer` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `FileUploadUrlResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/upload-revision', {
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
        '/api/v1/fileshare/api/v1/files/{file_id}/upload-revision',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/files/{file_id}/upload-revision' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/fileshare/audit/playback`

**Summary:** Record Playback Checkpoint  
**Operation ID:** `record_playback_checkpoint_api_v1_fileshare_audit_playback_post`  

**Clinical & GxP Rationale:**  
Record a viewer session video playback checkpoint audit event.

Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `PlaybackAuditCreateRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `file_record_id` | `string` | ✔ Yes | — |
| `event` | `PlaybackEventType` | ✔ Yes | — |
| `source` | `PlaybackEventSource` | No | — |
| `session_id` | `string` | ✔ Yes | — |
| `guest_token` | `string` | `null` | No | — |
| `reason_for_change` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `PlaybackAuditResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/audit/playback', {
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
        '/api/v1/fileshare/api/v1/fileshare/audit/playback',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/audit/playback' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/fileshare/files`

**Summary:** List Files  
**Operation ID:** `list_files_api_v1_fileshare_files_get`  

**Clinical & GxP Rationale:**  
List active file records for an optional study, site, and search query.

Requirements: PRD-SYS-001, PRD-DOC-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | — |
| `site_id` | `query` | `string` | `null` | No | — |
| `q` | `query` | `string` | `null` | No | — |
| `page` | `query` | `integer` | No | — |
| `page_size` | `query` | `integer` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<FileRecordResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files', {
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
        '/api/v1/fileshare/api/v1/fileshare/files',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/fileshare/files/guest/{token}`

**Summary:** Get Guest File Download  
**Operation ID:** `get_guest_file_download_api_v1_fileshare_files_guest__token__get`  

**Clinical & GxP Rationale:**  
Resolve an external guest link token and issue presigned GET URL.

Requirements: PRD-SYS-001, PRD-DOC-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `token` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileDownloadUrlResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/guest/{token}', {
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
        '/api/v1/fileshare/api/v1/fileshare/files/guest/{token}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/guest/{token}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/fileshare/files/upload-url`

**Summary:** Get Upload Url  
**Operation ID:** `get_upload_url_api_v1_fileshare_files_upload_url_post`  

**Clinical & GxP Rationale:**  
Allocate draft file record and generate presigned PUT or multipart upload URLs.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `FileUploadUrlRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | — |
| `site_id` | `string` | `null` | No | — |
| `filename` | `string` | ✔ Yes | — |
| `mime_type` | `string` | ✔ Yes | — |
| `size_bytes` | `integer` | ✔ Yes | — |
| `is_multipart` | `boolean` | No | — |
| `parts_count` | `integer` | No | — |
| `checksum_sha256` | `string` | `null` | No | — |
| `reason_for_change` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `FileUploadUrlResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/upload-url', {
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
        '/api/v1/fileshare/api/v1/fileshare/files/upload-url',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/upload-url' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/fileshare/files/upload/presigned-url`

**Summary:** Get Upload Url  
**Operation ID:** `get_upload_url_api_v1_fileshare_files_upload_presigned_url_post`  

**Clinical & GxP Rationale:**  
Allocate draft file record and generate presigned PUT or multipart upload URLs.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Payload Schema (`application/json`)

**Schema Type:** `FileUploadUrlRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | — |
| `site_id` | `string` | `null` | No | — |
| `filename` | `string` | ✔ Yes | — |
| `mime_type` | `string` | ✔ Yes | — |
| `size_bytes` | `integer` | ✔ Yes | — |
| `is_multipart` | `boolean` | No | — |
| `parts_count` | `integer` | No | — |
| `checksum_sha256` | `string` | `null` | No | — |
| `reason_for_change` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `FileUploadUrlResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/upload/presigned-url', {
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
        '/api/v1/fileshare/api/v1/fileshare/files/upload/presigned-url',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/upload/presigned-url' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/fileshare/files/{file_id}`

**Summary:** Get File Record  
**Operation ID:** `get_file_record_api_v1_fileshare_files__file_id__get`  

**Clinical & GxP Rationale:**  
Retrieve file record metadata.

Requirements: PRD-SYS-001, PRD-DOC-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileRecordResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}', {
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
        '/api/v1/fileshare/api/v1/fileshare/files/{file_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `DELETE` `/api/v1/fileshare/files/{file_id}`

**Summary:** Delete File Record  
**Operation ID:** `delete_file_record_api_v1_fileshare_files__file_id__delete`  

**Clinical & GxP Rationale:**  
Soft delete a file record.

Gated on FileRecord.is_on_hold being False; returns 409 if file is on hold.
Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `FileDeleteRequest` | `null`

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileRecordResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}', {
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
        '/api/v1/fileshare/api/v1/fileshare/files/{file_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/fileshare/files/{file_id}/abort-upload`

**Summary:** Abort File Upload  
**Operation ID:** `abort_file_upload_api_v1_fileshare_files__file_id__abort_upload_post`  

**Clinical & GxP Rationale:**  
Abort multipart upload session and mark file record deleted.

Requirements: PRD-SYS-001, PRD-DOC-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |
| `upload_id` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileRecordResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/abort-upload', {
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
        '/api/v1/fileshare/api/v1/fileshare/files/{file_id}/abort-upload',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/abort-upload' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/fileshare/files/{file_id}/activity`

**Summary:** Get File Activity Feed  
**Operation ID:** `get_file_activity_feed_api_v1_fileshare_files__file_id__activity_get`  

**Clinical & GxP Rationale:**  
Retrieve paginated per-document activity feed audit log entries for inspection readiness.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, 21 CFR Part 11

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |
| `event_type` | `query` | `string` | `null` | No | — |
| `from_date` | `query` | `string` | `null` | No | — |
| `to_date` | `query` | `string` | `null` | No | — |
| `page` | `query` | `integer` | No | — |
| `page_size` | `query` | `integer` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ActivityFeedResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/activity', {
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
        '/api/v1/fileshare/api/v1/fileshare/files/{file_id}/activity',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/activity' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/fileshare/files/{file_id}/approve`

**Summary:** Approve Document Endpoint  
**Operation ID:** `approve_document_endpoint_api_v1_fileshare_files__file_id__approve_post`  

**Clinical & GxP Rationale:**  
Execute 21 CFR Part 11 electronic signature document approval with in-product re-authentication.

Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `FileApproveRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `signing_reason` | `string` | ✔ Yes | — |
| `credential` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileRecordResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/approve', {
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
        '/api/v1/fileshare/api/v1/fileshare/files/{file_id}/approve',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/approve' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/fileshare/files/{file_id}/audit-logs`

**Summary:** Get File Audit Logs  
**Operation ID:** `get_file_audit_logs_api_v1_fileshare_files__file_id__audit_logs_get`  

**Clinical & GxP Rationale:**  
Retrieve immutable Part 11 audit log entries for a file record.

Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<FileAuditLogResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/audit-logs', {
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
        '/api/v1/fileshare/api/v1/fileshare/files/{file_id}/audit-logs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/audit-logs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/fileshare/files/{file_id}/complete`

**Summary:** Complete File Upload  
**Operation ID:** `complete_file_upload_api_v1_fileshare_files__file_id__complete_post`  

**Clinical & GxP Rationale:**  
Finalize singlepart or multipart upload and trigger asynchronous video transcoding if applicable.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `UploadCompleteRequest` | `null`

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileRecordResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/complete', {
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
        '/api/v1/fileshare/api/v1/fileshare/files/{file_id}/complete',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/complete' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/fileshare/files/{file_id}/complete-upload`

**Summary:** Complete File Upload  
**Operation ID:** `complete_file_upload_api_v1_fileshare_files__file_id__complete_upload_post`  

**Clinical & GxP Rationale:**  
Finalize singlepart or multipart upload and trigger asynchronous video transcoding if applicable.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `UploadCompleteRequest` | `null`

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileRecordResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/complete-upload', {
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
        '/api/v1/fileshare/api/v1/fileshare/files/{file_id}/complete-upload',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/complete-upload' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/fileshare/files/{file_id}/download-url`

**Summary:** Get Download Url  
**Operation ID:** `get_download_url_api_v1_fileshare_files__file_id__download_url_get`  

**Clinical & GxP Rationale:**  
Retrieve short-lived presigned GET download URL with permission and watermark verification.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-003

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |
| `original` | `query` | `boolean` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileDownloadUrlResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/download-url', {
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
        '/api/v1/fileshare/api/v1/fileshare/files/{file_id}/download-url',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/download-url' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/fileshare/files/{file_id}/grants`

**Summary:** List Share Grants  
**Operation ID:** `list_share_grants_api_v1_fileshare_files__file_id__grants_get`  

**Clinical & GxP Rationale:**  
Retrieve active internal share grants for a file record.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-003

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<ShareGrantResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/grants', {
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
        '/api/v1/fileshare/api/v1/fileshare/files/{file_id}/grants',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/grants' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/fileshare/files/{file_id}/grants`

**Summary:** Create Share Grant  
**Operation ID:** `create_share_grant_api_v1_fileshare_files__file_id__grants_post`  

**Clinical & GxP Rationale:**  
Delegate file permissions via an internal share grant.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-003

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ShareGrantCreateRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `granted_to_user_id` | `string` | `null` | No | — |
| `scope` | `ShareScope` | No | — |
| `permission_level` | `PermissionLevel` | No | — |
| `expires_at` | `string` | `null` | No | — |
| `reason_for_change` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `ShareGrantResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/grants', {
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
        '/api/v1/fileshare/api/v1/fileshare/files/{file_id}/grants',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/grants' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `DELETE` `/api/v1/fileshare/files/{file_id}/grants/{grant_id}`

**Summary:** Delete Share Grant  
**Operation ID:** `delete_share_grant_api_v1_fileshare_files__file_id__grants__grant_id__delete`  

**Clinical & GxP Rationale:**  
Revoke an internal share grant delegation.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-003

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |
| `grant_id` | `path` | `string` | ✔ Yes | — |
| `reason_for_change` | `query` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ShareGrantResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/grants/{grant_id}', {
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
        '/api/v1/fileshare/api/v1/fileshare/files/{file_id}/grants/{grant_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/grants/{grant_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/fileshare/files/{file_id}/guest-links`

**Summary:** List Guest Links  
**Operation ID:** `list_guest_links_api_v1_fileshare_files__file_id__guest_links_get`  

**Clinical & GxP Rationale:**  
Retrieve active guest links for a file record.

Requirements: PRD-SYS-001, PRD-DOC-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<GuestLinkResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/guest-links', {
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
        '/api/v1/fileshare/api/v1/fileshare/files/{file_id}/guest-links',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/guest-links' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/fileshare/files/{file_id}/guest-links`

**Summary:** Create Guest Link  
**Operation ID:** `create_guest_link_api_v1_fileshare_files__file_id__guest_links_post`  

**Clinical & GxP Rationale:**  
Generate a time-bounded external guest link.

Requirements: PRD-SYS-001, PRD-DOC-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `GuestLinkCreateRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `expires_in_hours` | `integer` | No | — |
| `reason_for_change` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `GuestLinkResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/guest-links', {
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
        '/api/v1/fileshare/api/v1/fileshare/files/{file_id}/guest-links',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/guest-links' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `DELETE` `/api/v1/fileshare/files/{file_id}/guest-links/{guest_link_id}`

**Summary:** Revoke Guest Link  
**Operation ID:** `revoke_guest_link_api_v1_fileshare_files__file_id__guest_links__guest_link_id__delete`  

**Clinical & GxP Rationale:**  
Revoke an active external guest link.

Requirements: PRD-SYS-001, PRD-DOC-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |
| `guest_link_id` | `path` | `string` | ✔ Yes | — |
| `reason_for_change` | `query` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `GuestLinkResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/guest-links/{guest_link_id}', {
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
        '/api/v1/fileshare/api/v1/fileshare/files/{file_id}/guest-links/{guest_link_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/guest-links/{guest_link_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/fileshare/files/{file_id}/hold`

**Summary:** Place File On Hold  
**Operation ID:** `place_file_on_hold_api_v1_fileshare_files__file_id__hold_post`  

**Clinical & GxP Rationale:**  
Place a file under retention/legal hold preventing deletion or archival.

Requires super_admin or data_manager role.
Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `FileHoldRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `reason_for_change` | `string` | ✔ Yes | — |
| `hold_reason` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileRecordResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/hold', {
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
        '/api/v1/fileshare/api/v1/fileshare/files/{file_id}/hold',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/hold' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `DELETE` `/api/v1/fileshare/files/{file_id}/hold`

**Summary:** Remove File From Hold  
**Operation ID:** `remove_file_from_hold_api_v1_fileshare_files__file_id__hold_delete`  

**Clinical & GxP Rationale:**  
Remove retention/legal hold from a file record.

Requires super_admin or data_manager role.
Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `FileHoldClearRequest` | `null`

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileRecordResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/hold', {
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
        '/api/v1/fileshare/api/v1/fileshare/files/{file_id}/hold',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/hold' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/fileshare/files/{file_id}/preview-url`

**Summary:** Get Preview Url  
**Operation ID:** `get_preview_url_api_v1_fileshare_files__file_id__preview_url_get`  

**Clinical & GxP Rationale:**  
Retrieve short-lived presigned preview URL (PDF rendition if available, otherwise original).

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |
| `original` | `query` | `boolean` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileDownloadUrlResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/preview-url', {
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
        '/api/v1/fileshare/api/v1/fileshare/files/{file_id}/preview-url',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/preview-url' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/fileshare/files/{file_id}/reject`

**Summary:** Reject Document Endpoint  
**Operation ID:** `reject_document_endpoint_api_v1_fileshare_files__file_id__reject_post`  

**Clinical & GxP Rationale:**  
Reject a document for approval.

Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `FileRejectRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `reason_for_change` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileRecordResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/reject', {
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
        '/api/v1/fileshare/api/v1/fileshare/files/{file_id}/reject',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/reject' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/fileshare/files/{file_id}/rescind-approval`

**Summary:** Rescind Approval Endpoint  
**Operation ID:** `rescind_approval_endpoint_api_v1_fileshare_files__file_id__rescind_approval_post`  

**Clinical & GxP Rationale:**  
Rescind a previous document approval. Requires super_admin.

Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `FileRescindApprovalRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `reason_for_change` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileRecordResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/rescind-approval', {
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
        '/api/v1/fileshare/api/v1/fileshare/files/{file_id}/rescind-approval',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/rescind-approval' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/fileshare/files/{file_id}/stream-url`

**Summary:** Get Stream Url  
**Operation ID:** `get_stream_url_api_v1_fileshare_files__file_id__stream_url_get`  

**Clinical & GxP Rationale:**  
Issue short-lived presigned HLS master manifest stream URL for video playback.

Returns HTTP 200 with presigned URL when transcoding is READY,
HTTP 202 Accepted when transcoding is PENDING or IN_PROGRESS,
HTTP 409 Conflict when transcoding has FAILED,
and HTTP 400 Bad Request when transcoding was NOT_REQUIRED.

Requirements: PRD-SYS-001, PRD-DOC-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | HLS master manifest presigned URL is ready for streaming. | `StreamUrlResponse` |
| `202` | Video transcoding is pending or in progress. | `any` |
| `400` | Transcoding is not required for non-video file. | `any` |
| `409` | Video transcoding failed. | `any` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/stream-url', {
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
        '/api/v1/fileshare/api/v1/fileshare/files/{file_id}/stream-url',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/stream-url' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/fileshare/files/{file_id}/upload-complete`

**Summary:** Complete File Upload  
**Operation ID:** `complete_file_upload_api_v1_fileshare_files__file_id__upload_complete_post`  

**Clinical & GxP Rationale:**  
Finalize singlepart or multipart upload and trigger asynchronous video transcoding if applicable.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `UploadCompleteRequest` | `null`

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileRecordResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/upload-complete', {
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
        '/api/v1/fileshare/api/v1/fileshare/files/{file_id}/upload-complete',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/upload-complete' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/fileshare/files/{file_id}/upload-revision`

**Summary:** Upload Revision Endpoint  
**Operation ID:** `upload_revision_endpoint_api_v1_fileshare_files__file_id__upload_revision_post`  

**Clinical & GxP Rationale:**  
Initiate upload of a new version/revision for a file record. Blocks if approved and immutable.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, 21 CFR Part 11

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `file_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `UploadRevisionRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `filename` | `string` | ✔ Yes | — |
| `mime_type` | `string` | ✔ Yes | — |
| `size_bytes` | `integer` | ✔ Yes | — |
| `checksum_sha256` | `string` | `null` | No | — |
| `reason_for_change` | `string` | ✔ Yes | — |
| `is_multipart` | `boolean` | No | — |
| `parts_count` | `integer` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `FileUploadUrlResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/upload-revision', {
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
        '/api/v1/fileshare/api/v1/fileshare/files/{file_id}/upload-revision',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/files/{file_id}/upload-revision' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/fileshare/guest/{token}`

**Summary:** Get External Guest File Download  
**Operation ID:** `get_external_guest_file_download_api_v1_fileshare_guest__token__get`  

**Clinical & GxP Rationale:**  
Public resolution endpoint for external guest access tokens.

Requirements: PRD-SYS-001, PRD-DOC-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `token` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FileDownloadUrlResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/fileshare/api/v1/fileshare/guest/{token}', {
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
        '/api/v1/fileshare/api/v1/fileshare/guest/{token}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/fileshare/api/v1/fileshare/guest/{token}' \
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
const response = await fetch('http://localhost:8000/api/v1/fileshare/health', {
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
        '/api/v1/fileshare/health',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/fileshare/health' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---
