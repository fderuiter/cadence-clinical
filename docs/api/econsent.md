# Electronic Consent (eConsent)

> **Functional Domain:** `Patient Engagement & Interoperability`  
> **Service Host Port:** `localhost:8017`  
> **Gateway Proxy Prefix:** `/api/v1/econsent`  
> **Persistence Backend:** `PostgreSQL / SQLite`  

## Overview

Interactive informed consent authoring, dynamic amendment re-consent gating, readability metrics, and digital signature sealing.

---

## Endpoints Summary

| Method | Path | Summary | GxP Auth & Signatures |
| :--- | :--- | :--- | :--- |
| `GET` | [/api/v1/econsent/archival-status](#get-api-v1-econsent-archival-status) | Get Archival Status Query Endpoint | 👀 Read-Only |
| `GET` | [/api/v1/econsent/archival-status/{correlation_id}](#get-api-v1-econsent-archival-status-correlation_id) | Get Archival Status Endpoint | 👀 Read-Only |
| `GET` | [/api/v1/econsent/audit](#get-api-v1-econsent-audit) | List Audit Logs | 👀 Read-Only |
| `POST` | [/api/v1/econsent/clauses](#post-api-v1-econsent-clauses) | Create Consent Clause | ✍️ Audited Write |
| `GET` | [/api/v1/econsent/clauses](#get-api-v1-econsent-clauses) | List Consent Clauses | 👀 Read-Only |
| `PUT` | [/api/v1/econsent/clauses/{clause_id}](#put-api-v1-econsent-clauses-clause_id) | Update Consent Clause | ✍️ Audited Write |
| `GET` | [/api/v1/econsent/clauses/{clause_id}](#get-api-v1-econsent-clauses-clause_id) | Get Consent Clause | 👀 Read-Only |
| `POST` | [/api/v1/econsent/clauses/{clause_id}/harmonize](#post-api-v1-econsent-clauses-clause_id-harmonize) | Apply plain-language readability harmonization to a clause with IRB amendment linking | ✍️ Audited Write |
| `POST` | [/api/v1/econsent/documents](#post-api-v1-econsent-documents) | Create Consent Document | ✍️ Audited Write |
| `GET` | [/api/v1/econsent/documents/{id}](#get-api-v1-econsent-documents-id) | Get Consent Document | 👀 Read-Only |
| `GET` | [/api/v1/econsent/export/cdisc-odm/{study_id}/{subject_pseudonym}/{template_id}/{version_index}](#get-api-v1-econsent-export-cdisc-odm-study_id-subject_pseudonym-template_id-version_index) | Export Subject Cdisc Odm | 👀 Read-Only |
| `GET` | [/api/v1/econsent/export/certificate/{study_id}/{subject_pseudonym}/{template_id}/{version_index}](#get-api-v1-econsent-export-certificate-study_id-subject_pseudonym-template_id-version_index) | Export Verifiable Certificate | 👀 Read-Only |
| `POST` | [/api/v1/econsent/options/{template_id}/{version_index}](#post-api-v1-econsent-options-template_id-version_index) | Create Granular Option | ✍️ Audited Write |
| `GET` | [/api/v1/econsent/options/{template_id}/{version_index}](#get-api-v1-econsent-options-template_id-version_index) | List Granular Options | 👀 Read-Only |
| `POST` | [/api/v1/econsent/readability/analyze](#post-api-v1-econsent-readability-analyze) | Compute real-time readability metrics for consent text | ✍️ Audited Write |
| `POST` | [/api/v1/econsent/readability/harmonize](#post-api-v1-econsent-readability-harmonize) | Generate patient-friendly medical jargon substitutions via AI Gateway Tier 2 | ✍️ Audited Write |
| `POST` | [/api/v1/econsent/reconsent/complete/{requirement_id}](#post-api-v1-econsent-reconsent-complete-requirement_id) | Complete Reconsent | ✍️ Audited Write |
| `POST` | [/api/v1/econsent/reconsent/minor-milestones/evaluate](#post-api-v1-econsent-reconsent-minor-milestones-evaluate) | Evaluate Minor Milestones | ✍️ Audited Write |
| `GET` | [/api/v1/econsent/reconsent/minor-milestones/pending/{study_id}](#get-api-v1-econsent-reconsent-minor-milestones-pending-study_id) | Get Pending Majority Reconsents | 👀 Read-Only |
| `POST` | [/api/v1/econsent/reconsent/minor-milestones/trigger](#post-api-v1-econsent-reconsent-minor-milestones-trigger) | Trigger Majority Reconsent | ✍️ Audited Write |
| `GET` | [/api/v1/econsent/reconsent/pending/{study_id}](#get-api-v1-econsent-reconsent-pending-study_id) | Get Pending Reconsents | 👀 Read-Only |
| `POST` | [/api/v1/econsent/reconsent/trigger/{template_id}](#post-api-v1-econsent-reconsent-trigger-template_id) | Trigger Reconsent | ✍️ Audited Write |
| `GET` | [/api/v1/econsent/subjects/{subject_pseudonym}/consent-status](#get-api-v1-econsent-subjects-subject_pseudonym-consent-status) | Get Subject Consent Status Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/econsent/templates](#post-api-v1-econsent-templates) | Create Consent Template | ✍️ Audited Write |
| `GET` | [/api/v1/econsent/templates](#get-api-v1-econsent-templates) | List Consent Templates | 👀 Read-Only |
| `PUT` | [/api/v1/econsent/templates/{template_id}](#put-api-v1-econsent-templates-template_id) | Update Consent Template | ✍️ Audited Write |
| `GET` | [/api/v1/econsent/templates/{template_id}](#get-api-v1-econsent-templates-template_id) | Get Consent Template | 👀 Read-Only |
| `GET` | [/api/v1/econsent/templates/{template_id}/approved-content](#get-api-v1-econsent-templates-template_id-approved-content) | Get Approved Composed Template | 👀 Read-Only |
| `GET` | [/api/v1/econsent/templates/{template_id}/compose](#get-api-v1-econsent-templates-template_id-compose) | Compose Consent Template | 👀 Read-Only |
| `GET` | [/api/v1/econsent/templates/{template_id}/diff/{base_version}/{target_version}](#get-api-v1-econsent-templates-template_id-diff-base_version-target_version) | Diff Template Versions | 👀 Read-Only |
| `POST` | [/api/v1/econsent/templates/{template_id}/publish](#post-api-v1-econsent-templates-template_id-publish) | Publish Consent Template | ✍️ Audited Write |
| `POST` | [/api/v1/econsent/templates/{template_id}/versions/{version_index}/capture-consent](#post-api-v1-econsent-templates-template_id-versions-version_index-capture-consent) | Capture Subject Consent | ✍️ Audited Write |
| `POST` | [/api/v1/econsent/templates/{template_id}/versions/{version_index}/comprehension-checks](#post-api-v1-econsent-templates-template_id-versions-version_index-comprehension-checks) | Create Or Update Comprehension Check | ✍️ Audited Write |
| `GET` | [/api/v1/econsent/templates/{template_id}/versions/{version_index}/comprehension-checks](#get-api-v1-econsent-templates-template_id-versions-version_index-comprehension-checks) | Get Comprehension Check | 👀 Read-Only |
| `POST` | [/api/v1/econsent/templates/{template_id}/versions/{version_index}/sign](#post-api-v1-econsent-templates-template_id-versions-version_index-sign) | Sign Consent Template Endpoint | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/econsent/templates/{template_id}/versions/{version_index}/submit-answers](#post-api-v1-econsent-templates-template_id-versions-version_index-submit-answers) | Submit Comprehension Answers Endpoint | ✍️ Audited Write |
| `POST` | [/api/v1/econsent/translations](#post-api-v1-econsent-translations) | Create Consent Translation | ✍️ Audited Write |
| `GET` | [/api/v1/econsent/translations](#get-api-v1-econsent-translations) | List Consent Translations | 👀 Read-Only |
| `PUT` | [/api/v1/econsent/translations/{translation_id}](#put-api-v1-econsent-translations-translation_id) | Update Consent Translation | ✍️ Audited Write |
| `GET` | [/api/v1/econsent/translations/{translation_id}](#get-api-v1-econsent-translations-translation_id) | Get Consent Translation | 👀 Read-Only |
| `POST` | [/api/v1/econsent/translations/{translation_id}/transition](#post-api-v1-econsent-translations-translation_id-transition) | Transition Consent Translation | ✍️ Audited Write |
| `POST` | [/api/v1/econsent/withdrawal](#post-api-v1-econsent-withdrawal) | Withdraw Subject Consent | ✍️ Audited Write |
| `GET` | [/api/v1/econsent/withdrawal/{study_id}/{subject_pseudonym}](#get-api-v1-econsent-withdrawal-study_id-subject_pseudonym) | Get Subject Withdrawal Status | 👀 Read-Only |
| `GET` | [/health](#get-health) | Health Check | 👀 Read-Only |

---

## Granular Endpoint Specifications

### `GET` `/api/v1/econsent/archival-status`

**Summary:** Get Archival Status Query Endpoint  
**Operation ID:** `get_archival_status_query_endpoint_api_v1_econsent_archival_status_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `correlation_id` | `query` | `string` | `null` | No | — |
| `template_id` | `query` | `string` | `null` | No | — |
| `version_index` | `query` | `integer` | `null` | No | — |
| `subject_pseudonym` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ArchivalDeliveryResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/archival-status', {
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
        '/api/v1/econsent/api/v1/econsent/archival-status',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/econsent/api/v1/econsent/archival-status' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/econsent/archival-status/{correlation_id}`

**Summary:** Get Archival Status Endpoint  
**Operation ID:** `get_archival_status_endpoint_api_v1_econsent_archival_status__correlation_id__get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `correlation_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ArchivalDeliveryResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/archival-status/{correlation_id}', {
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
        '/api/v1/econsent/api/v1/econsent/archival-status/{correlation_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/econsent/api/v1/econsent/archival-status/{correlation_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/econsent/audit`

**Summary:** List Audit Logs  
**Operation ID:** `list_audit_logs_api_v1_econsent_audit_get`  

**Clinical & GxP Rationale:**  
Retrieves 21 CFR Part 11 immutable audit trail records.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `document_id` | `query` | `string` | `null` | No | Filter by document / record UUID |
| `actor_id` | `query` | `string` | `null` | No | Filter by actor ID |
| `limit` | `query` | `integer` | No | Max logs to return |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<ConsentAuditLogResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/audit', {
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
        '/api/v1/econsent/api/v1/econsent/audit',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/econsent/api/v1/econsent/audit' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/econsent/clauses`

**Summary:** Create Consent Clause  
**Operation ID:** `create_consent_clause_api_v1_econsent_clauses_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ConsentClauseCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `created_at` | `string` | No | Chronological UTC timestamp when the record was created. |
| `created_by` | `string` | ✔ Yes | Unique identifier of the user who created the record. |
| `reason_for_change` | `string` | ✔ Yes | Mandatory explanation or audit justification for creating or mutating this record. |
| `version_index` | `integer` | No | Row version counter or index. |
| `clause_id` | `string` | `null` | No | Unique clause identifier across versions. Generated if not provided. |
| `study_id` | `string` | ✔ Yes | Unique clinical study identifier |
| `title` | `string` | ✔ Yes | Title of the clause |
| `text` | `string` | ✔ Yes | Content of the clause |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `ConsentClauseResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/clauses', {
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
        '/api/v1/econsent/api/v1/econsent/clauses',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/econsent/api/v1/econsent/clauses' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/econsent/clauses`

**Summary:** List Consent Clauses  
**Operation ID:** `list_consent_clauses_api_v1_econsent_clauses_get`  

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
| `clause_id` | `query` | `string` | `null` | No | — |
| `all_versions` | `query` | `boolean` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<ConsentClauseResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/clauses', {
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
        '/api/v1/econsent/api/v1/econsent/clauses',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/econsent/api/v1/econsent/clauses' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/econsent/clauses/{clause_id}`

**Summary:** Update Consent Clause  
**Operation ID:** `update_consent_clause_api_v1_econsent_clauses__clause_id__put`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `clause_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ConsentClauseUpdate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `created_at` | `string` | No | Chronological UTC timestamp when the record was created. |
| `created_by` | `string` | ✔ Yes | Unique identifier of the user who created the record. |
| `reason_for_change` | `string` | ✔ Yes | Mandatory explanation or audit justification for creating or mutating this record. |
| `version_index` | `integer` | No | Row version counter or index. |
| `study_id` | `string` | ✔ Yes | Unique clinical study identifier |
| `title` | `string` | ✔ Yes | Title of the clause |
| `text` | `string` | ✔ Yes | Content of the clause |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ConsentClauseResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/clauses/{clause_id}', {
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
        '/api/v1/econsent/api/v1/econsent/clauses/{clause_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/econsent/api/v1/econsent/clauses/{clause_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/econsent/clauses/{clause_id}`

**Summary:** Get Consent Clause  
**Operation ID:** `get_consent_clause_api_v1_econsent_clauses__clause_id__get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `clause_id` | `path` | `string` | ✔ Yes | — |
| `version_index` | `query` | `integer` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ConsentClauseResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/clauses/{clause_id}', {
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
        '/api/v1/econsent/api/v1/econsent/clauses/{clause_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/econsent/api/v1/econsent/clauses/{clause_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/econsent/clauses/{clause_id}/harmonize`

**Summary:** Apply plain-language readability harmonization to a clause with IRB amendment linking  
**Operation ID:** `apply_clause_harmonization_api_v1_econsent_clauses__clause_id__harmonize_post`  

**Clinical & GxP Rationale:**  
Applies harmonized text to an ICF clause, incrementing version index and logging 21 CFR Part 11 audit trail.

Args:
    request: Request object for extracting user_id and change_reason.
    clause_id: Identifier of the consent clause being harmonized.
    payload: ClauseHarmonizationApplyRequest with harmonized text and audit reason.
    session: Active asynchronous database session.

Returns:
    ClauseHarmonizationApplyResponse with updated version index and new readability metrics.

Raises:
    HTTPException: If clause_id is not found or auditor role attempts mutation.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `clause_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ClauseHarmonizationApplyRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `harmonized_text` | `string` | ✔ Yes | Harmonized plain-language clause text |
| `reason_for_change` | `string` | ✔ Yes | 21 CFR Part 11 audit reason and protocol amendment reference |
| `protocol_version` | `string` | `null` | No | Protocol amendment version reference tag |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ClauseHarmonizationApplyResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/clauses/{clause_id}/harmonize', {
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
        '/api/v1/econsent/api/v1/econsent/clauses/{clause_id}/harmonize',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/econsent/api/v1/econsent/clauses/{clause_id}/harmonize' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/econsent/documents`

**Summary:** Create Consent Document  
**Operation ID:** `create_consent_document_api_v1_econsent_documents_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ConsentDocumentCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `created_at` | `string` | No | Chronological UTC timestamp when the record was created. |
| `created_by` | `string` | ✔ Yes | Unique identifier of the user who created the record. |
| `reason_for_change` | `string` | ✔ Yes | Mandatory explanation or audit justification for creating or mutating this record. |
| `version_index` | `integer` | No | Row version counter or index. |
| `study_id` | `string` | ✔ Yes | Unique clinical study identifier |
| `site_id` | `string` | ✔ Yes | Unique clinical site identifier |
| `document_name` | `string` | ✔ Yes | Name of the eConsent form/document |
| `content` | `string` | ✔ Yes | Full text/content of the consent form |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `ConsentDocumentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/documents', {
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
        '/api/v1/econsent/api/v1/econsent/documents',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/econsent/api/v1/econsent/documents' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/econsent/documents/{id}`

**Summary:** Get Consent Document  
**Operation ID:** `get_consent_document_api_v1_econsent_documents__id__get`  

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
| `200` | Successful Response | `ConsentDocumentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/documents/{id}', {
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
        '/api/v1/econsent/api/v1/econsent/documents/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/econsent/api/v1/econsent/documents/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/econsent/export/cdisc-odm/{study_id}/{subject_pseudonym}/{template_id}/{version_index}`

**Summary:** Export Subject Cdisc Odm  
**Operation ID:** `export_subject_cdisc_odm_api_v1_econsent_export_cdisc_odm__study_id___subject_pseudonym___template_id___version_index__get`  

**Clinical & GxP Rationale:**  
Exports clinical eConsent records in standard CDISC ODM v1.3.2/v2.0 XML.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `subject_pseudonym` | `path` | `string` | ✔ Yes | — |
| `template_id` | `path` | `string` | ✔ Yes | — |
| `version_index` | `path` | `integer` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `CdiscOdmExportResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/export/cdisc-odm/{study_id}/{subject_pseudonym}/{template_id}/{version_index}', {
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
        '/api/v1/econsent/api/v1/econsent/export/cdisc-odm/{study_id}/{subject_pseudonym}/{template_id}/{version_index}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/econsent/api/v1/econsent/export/cdisc-odm/{study_id}/{subject_pseudonym}/{template_id}/{version_index}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/econsent/export/certificate/{study_id}/{subject_pseudonym}/{template_id}/{version_index}`

**Summary:** Export Verifiable Certificate  
**Operation ID:** `export_verifiable_certificate_api_v1_econsent_export_certificate__study_id___subject_pseudonym___template_id___version_index__get`  

**Clinical & GxP Rationale:**  
Generates a standalone, tamper-evident HTML consent certificate.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `subject_pseudonym` | `path` | `string` | ✔ Yes | — |
| `template_id` | `path` | `string` | ✔ Yes | — |
| `version_index` | `path` | `integer` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `VerifiableCertificateResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/export/certificate/{study_id}/{subject_pseudonym}/{template_id}/{version_index}', {
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
        '/api/v1/econsent/api/v1/econsent/export/certificate/{study_id}/{subject_pseudonym}/{template_id}/{version_index}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/econsent/api/v1/econsent/export/certificate/{study_id}/{subject_pseudonym}/{template_id}/{version_index}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/econsent/options/{template_id}/{version_index}`

**Summary:** Create Granular Option  
**Operation ID:** `create_granular_option_api_v1_econsent_options__template_id___version_index__post`  

**Clinical & GxP Rationale:**  
Configures a granular optional research choice on a template version.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `template_id` | `path` | `string` | ✔ Yes | — |
| `version_index` | `path` | `integer` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `GranularOptionCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `option_code` | `string` | ✔ Yes | Unique key for the option, e.g. OPT_GENETICS |
| `title` | `string` | ✔ Yes | Title of the optional choice |
| `description` | `string` | ✔ Yes | Detailed description of optional procedure |
| `category` | `string` | No | Category: GENETICS, BIOBANKING, DATA_SHARING, FUTURE_CONTACT, SUBSTUDY |
| `is_mandatory` | `boolean` | No | Whether selection is mandatory |
| `default_selected` | `boolean` | No | Default initial state |
| `reason_for_change` | `string` | ✔ Yes | Mandatory change justification |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `GranularOptionResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/options/{template_id}/{version_index}', {
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
        '/api/v1/econsent/api/v1/econsent/options/{template_id}/{version_index}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/econsent/api/v1/econsent/options/{template_id}/{version_index}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/econsent/options/{template_id}/{version_index}`

**Summary:** List Granular Options  
**Operation ID:** `list_granular_options_api_v1_econsent_options__template_id___version_index__get`  

**Clinical & GxP Rationale:**  
Lists all configured granular research options for a template version.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `template_id` | `path` | `string` | ✔ Yes | — |
| `version_index` | `path` | `integer` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<GranularOptionResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/options/{template_id}/{version_index}', {
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
        '/api/v1/econsent/api/v1/econsent/options/{template_id}/{version_index}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/econsent/api/v1/econsent/options/{template_id}/{version_index}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/econsent/readability/analyze`

**Summary:** Compute real-time readability metrics for consent text  
**Operation ID:** `analyze_readability_api_v1_econsent_readability_analyze_post`  

**Clinical & GxP Rationale:**  
Computes Flesch-Kincaid Grade Level, Flesch Reading Ease, and Dale-Chall readability indices.

Args:
    payload: ReadabilityAnalysisRequest containing narrative or clause text.

Returns:
    ReadabilityAnalysisResponse with calculated grade levels, word counts, and target indicators.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ReadabilityAnalysisRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `text` | `string` | ✔ Yes | Consent narrative or clause text to analyze |
| `study_id` | `string` | `null` | No | Optional associated clinical study identifier |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ReadabilityAnalysisResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/readability/analyze', {
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
        '/api/v1/econsent/api/v1/econsent/readability/analyze',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/econsent/api/v1/econsent/readability/analyze' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/econsent/readability/harmonize`

**Summary:** Generate patient-friendly medical jargon substitutions via AI Gateway Tier 2  
**Operation ID:** `harmonize_readability_api_v1_econsent_readability_harmonize_post`  

**Clinical & GxP Rationale:**  
Analyzes text for complex clinical jargon and suggests plain-language substitutions.

Args:
    payload: ReadabilityHarmonizationRequest containing original text and target reading level.

Returns:
    ReadabilityHarmonizationResponse with candidate substitutions, harmonized text, and delta scores.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ReadabilityHarmonizationRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `text` | `string` | ✔ Yes | Original consent clause or text to harmonize |
| `study_id` | `string` | `null` | No | Optional associated clinical study identifier |
| `target_grade_level` | `number` | No | Target reading grade level |
| `protocol_version` | `string` | `null` | No | Protocol amendment version tag |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ReadabilityHarmonizationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/readability/harmonize', {
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
        '/api/v1/econsent/api/v1/econsent/readability/harmonize',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/econsent/api/v1/econsent/readability/harmonize' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/econsent/reconsent/complete/{requirement_id}`

**Summary:** Complete Reconsent  
**Operation ID:** `complete_reconsent_api_v1_econsent_reconsent_complete__requirement_id__post`  

**Clinical & GxP Rationale:**  
Marks a re-consent requirement as completed following subject e-signature.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `requirement_id` | `path` | `string` | ✔ Yes | — |
| `completed_consent_id` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ReconsentRequirementResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/reconsent/complete/{requirement_id}', {
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
        '/api/v1/econsent/api/v1/econsent/reconsent/complete/{requirement_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/econsent/api/v1/econsent/reconsent/complete/{requirement_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/econsent/reconsent/minor-milestones/evaluate`

**Summary:** Evaluate Minor Milestones  
**Operation ID:** `evaluate_minor_milestones_api_v1_econsent_reconsent_minor_milestones_evaluate_post`  

**Clinical & GxP Rationale:**  
Evaluates minor subjects against jurisdiction legal age of majority rules.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `MinorMilestoneEvaluationRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | — |
| `subjects` | `Array<SubjectMilestoneInput>` | ✔ Yes | — |
| `as_of_date` | `string` | `null` | No | Evaluation reference date (defaults to today) |
| `lead_time_days` | `integer` | No | Lead time window for approaching majority |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `MinorMilestoneEvaluationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/reconsent/minor-milestones/evaluate', {
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
        '/api/v1/econsent/api/v1/econsent/reconsent/minor-milestones/evaluate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/econsent/api/v1/econsent/reconsent/minor-milestones/evaluate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/econsent/reconsent/minor-milestones/pending/{study_id}`

**Summary:** Get Pending Majority Reconsents  
**Operation ID:** `get_pending_majority_reconsents_api_v1_econsent_reconsent_minor_milestones_pending__study_id__get`  

**Clinical & GxP Rationale:**  
Retrieves pending adult re-consent requirements resulting from age-of-majority transitions.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `subject_pseudonym` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<ReconsentRequirementResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/reconsent/minor-milestones/pending/{study_id}', {
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
        '/api/v1/econsent/api/v1/econsent/reconsent/minor-milestones/pending/{study_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/econsent/api/v1/econsent/reconsent/minor-milestones/pending/{study_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/econsent/reconsent/minor-milestones/trigger`

**Summary:** Trigger Majority Reconsent  
**Operation ID:** `trigger_majority_reconsent_api_v1_econsent_reconsent_minor_milestones_trigger_post`  

**Clinical & GxP Rationale:**  
Generates mandatory adult re-consent tasks for minor subjects who turned legal age.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `TriggerMajorityReconsentRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | — |
| `site_id` | `string` | `null` | No | — |
| `template_id` | `string` | ✔ Yes | — |
| `adult_version_index` | `integer` | No | Adult consent template version |
| `subjects` | `Array<SubjectMilestoneInput>` | ✔ Yes | — |
| `lead_time_days` | `integer` | No | Lead time days (0 = only subjects who reached legal majority) |
| `reason_for_change` | `string` | No | GxP change reason |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `Array<ReconsentRequirementResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/reconsent/minor-milestones/trigger', {
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
        '/api/v1/econsent/api/v1/econsent/reconsent/minor-milestones/trigger',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/econsent/api/v1/econsent/reconsent/minor-milestones/trigger' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/econsent/reconsent/pending/{study_id}`

**Summary:** Get Pending Reconsents  
**Operation ID:** `get_pending_reconsents_api_v1_econsent_reconsent_pending__study_id__get`  

**Clinical & GxP Rationale:**  
Retrieves all pending re-consent requirements for a study or subject.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `subject_pseudonym` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<ReconsentRequirementResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/reconsent/pending/{study_id}', {
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
        '/api/v1/econsent/api/v1/econsent/reconsent/pending/{study_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/econsent/api/v1/econsent/reconsent/pending/{study_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/econsent/reconsent/trigger/{template_id}`

**Summary:** Trigger Reconsent  
**Operation ID:** `trigger_reconsent_api_v1_econsent_reconsent_trigger__template_id__post`  

**Clinical & GxP Rationale:**  
Triggers re-consent requirements for all active enrolled subjects under an amended ICF.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `template_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ReconsentTriggerRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | — |
| `site_id` | `string` | `null` | No | — |
| `prior_version_index` | `integer` | ✔ Yes | — |
| `new_version_index` | `integer` | ✔ Yes | — |
| `change_summary` | `string` | ✔ Yes | — |
| `substantive_changes` | `Array<object>` | No | — |
| `reason_for_change` | `string` | ✔ Yes | Mandatory change justification |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `Array<ReconsentRequirementResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/reconsent/trigger/{template_id}', {
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
        '/api/v1/econsent/api/v1/econsent/reconsent/trigger/{template_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/econsent/api/v1/econsent/reconsent/trigger/{template_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/econsent/subjects/{subject_pseudonym}/consent-status`

**Summary:** Get Subject Consent Status Endpoint  
**Operation ID:** `get_subject_consent_status_endpoint_api_v1_econsent_subjects__subject_pseudonym__consent_status_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `subject_pseudonym` | `path` | `string` | ✔ Yes | — |
| `study_id` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SubjectConsentStatusResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/subjects/{subject_pseudonym}/consent-status', {
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
        '/api/v1/econsent/api/v1/econsent/subjects/{subject_pseudonym}/consent-status',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/econsent/api/v1/econsent/subjects/{subject_pseudonym}/consent-status' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/econsent/templates`

**Summary:** Create Consent Template  
**Operation ID:** `create_consent_template_api_v1_econsent_templates_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ConsentTemplateCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `created_at` | `string` | No | Chronological UTC timestamp when the record was created. |
| `created_by` | `string` | ✔ Yes | Unique identifier of the user who created the record. |
| `reason_for_change` | `string` | ✔ Yes | Mandatory explanation or audit justification for creating or mutating this record. |
| `version_index` | `integer` | No | Row version counter or index. |
| `template_id` | `string` | `null` | No | Unique template identifier across versions. Generated if not provided. |
| `study_id` | `string` | ✔ Yes | Unique clinical study identifier |
| `template_name` | `string` | ✔ Yes | Name of the template |
| `protocol_version` | `string` | ✔ Yes | Associated clinical protocol version |
| `requires_reconsent` | `boolean` | No | Requires re-consent on change |
| `clauses` | `Array<string>` | No | Ordered clause_ids referenced by this template |
| `workflow_steps` | `Array<object>` | No | Workflow steps config |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `ConsentTemplateResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/templates', {
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
        '/api/v1/econsent/api/v1/econsent/templates',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/econsent/api/v1/econsent/templates' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/econsent/templates`

**Summary:** List Consent Templates  
**Operation ID:** `list_consent_templates_api_v1_econsent_templates_get`  

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
| `template_id` | `query` | `string` | `null` | No | — |
| `all_versions` | `query` | `boolean` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<ConsentTemplateResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/templates', {
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
        '/api/v1/econsent/api/v1/econsent/templates',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/econsent/api/v1/econsent/templates' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/econsent/templates/{template_id}`

**Summary:** Update Consent Template  
**Operation ID:** `update_consent_template_api_v1_econsent_templates__template_id__put`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `template_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ConsentTemplateUpdate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `created_at` | `string` | No | Chronological UTC timestamp when the record was created. |
| `created_by` | `string` | ✔ Yes | Unique identifier of the user who created the record. |
| `reason_for_change` | `string` | ✔ Yes | Mandatory explanation or audit justification for creating or mutating this record. |
| `version_index` | `integer` | No | Row version counter or index. |
| `study_id` | `string` | ✔ Yes | Unique clinical study identifier |
| `template_name` | `string` | ✔ Yes | Name of the template |
| `protocol_version` | `string` | ✔ Yes | Associated clinical protocol version |
| `requires_reconsent` | `boolean` | No | Requires re-consent on change |
| `clauses` | `Array<string>` | No | Ordered clause_ids referenced by this template |
| `workflow_steps` | `Array<object>` | No | Workflow steps config |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ConsentTemplateResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/templates/{template_id}', {
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
        '/api/v1/econsent/api/v1/econsent/templates/{template_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/econsent/api/v1/econsent/templates/{template_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/econsent/templates/{template_id}`

**Summary:** Get Consent Template  
**Operation ID:** `get_consent_template_api_v1_econsent_templates__template_id__get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `template_id` | `path` | `string` | ✔ Yes | — |
| `version_index` | `query` | `integer` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ConsentTemplateResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/templates/{template_id}', {
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
        '/api/v1/econsent/api/v1/econsent/templates/{template_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/econsent/api/v1/econsent/templates/{template_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/econsent/templates/{template_id}/approved-content`

**Summary:** Get Approved Composed Template  
**Operation ID:** `get_approved_composed_template_api_v1_econsent_templates__template_id__approved_content_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `template_id` | `path` | `string` | ✔ Yes | — |
| `language_code` | `query` | `string` | ✔ Yes | — |
| `version_index` | `query` | `integer` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/templates/{template_id}/approved-content', {
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
        '/api/v1/econsent/api/v1/econsent/templates/{template_id}/approved-content',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/econsent/api/v1/econsent/templates/{template_id}/approved-content' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/econsent/templates/{template_id}/compose`

**Summary:** Compose Consent Template  
**Operation ID:** `compose_consent_template_api_v1_econsent_templates__template_id__compose_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `template_id` | `path` | `string` | ✔ Yes | — |
| `version_index` | `query` | `integer` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ComposedTemplateResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/templates/{template_id}/compose', {
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
        '/api/v1/econsent/api/v1/econsent/templates/{template_id}/compose',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/econsent/api/v1/econsent/templates/{template_id}/compose' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/econsent/templates/{template_id}/diff/{base_version}/{target_version}`

**Summary:** Diff Template Versions  
**Operation ID:** `diff_template_versions_api_v1_econsent_templates__template_id__diff__base_version___target_version__get`  

**Clinical & GxP Rationale:**  
Computes semantic delta and substantive change analysis between two template versions.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `template_id` | `path` | `string` | ✔ Yes | — |
| `base_version` | `path` | `integer` | ✔ Yes | — |
| `target_version` | `path` | `integer` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `TemplateDiffResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/templates/{template_id}/diff/{base_version}/{target_version}', {
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
        '/api/v1/econsent/api/v1/econsent/templates/{template_id}/diff/{base_version}/{target_version}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/econsent/api/v1/econsent/templates/{template_id}/diff/{base_version}/{target_version}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/econsent/templates/{template_id}/publish`

**Summary:** Publish Consent Template  
**Operation ID:** `publish_consent_template_api_v1_econsent_templates__template_id__publish_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `template_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ConsentTemplateResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/templates/{template_id}/publish', {
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
        '/api/v1/econsent/api/v1/econsent/templates/{template_id}/publish',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/econsent/api/v1/econsent/templates/{template_id}/publish' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/econsent/templates/{template_id}/versions/{version_index}/capture-consent`

**Summary:** Capture Subject Consent  
**Operation ID:** `capture_subject_consent_api_v1_econsent_templates__template_id__versions__version_index__capture_consent_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `template_id` | `path` | `string` | ✔ Yes | — |
| `version_index` | `path` | `integer` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SubjectConsentCaptureRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `subject_pseudonym` | `string` | ✔ Yes | Pseudonym identifier of the subject |
| `site_id` | `string` | ✔ Yes | Unique clinical site identifier |
| `device_timestamp` | `string` | `null` | No | Device-side timestamp |
| `source_content_identity` | `string` | ✔ Yes | Hash/clause-set identifier at capture time |
| `reason_for_change` | `string` | ✔ Yes | Part 11 rationale/change justification |
| `signatures` | `Array<object>` | `null` | No | Optional multi-party signature list |
| `granular_selections` | `Array<object>` | `null` | No | Optional granular opt-in selections |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `SubjectConsentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/templates/{template_id}/versions/{version_index}/capture-consent', {
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
        '/api/v1/econsent/api/v1/econsent/templates/{template_id}/versions/{version_index}/capture-consent',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/econsent/api/v1/econsent/templates/{template_id}/versions/{version_index}/capture-consent' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/econsent/templates/{template_id}/versions/{version_index}/comprehension-checks`

**Summary:** Create Or Update Comprehension Check  
**Operation ID:** `create_or_update_comprehension_check_api_v1_econsent_templates__template_id__versions__version_index__comprehension_checks_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `template_id` | `path` | `string` | ✔ Yes | — |
| `version_index` | `path` | `integer` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ComprehensionCheckCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `created_at` | `string` | No | Chronological UTC timestamp when the record was created. |
| `created_by` | `string` | ✔ Yes | Unique identifier of the user who created the record. |
| `reason_for_change` | `string` | ✔ Yes | Mandatory explanation or audit justification for creating or mutating this record. |
| `version_index` | `integer` | No | Row version counter or index. |
| `questions` | `Array<object>` | ✔ Yes | List of question dicts |
| `expected_answers` | `object` | ✔ Yes | Mapping of question_id to expected answer |
| `threshold_policy` | `object` | ✔ Yes | Evaluation threshold policy, e.g. {'min_correct': 2} |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `ComprehensionCheckResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/templates/{template_id}/versions/{version_index}/comprehension-checks', {
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
        '/api/v1/econsent/api/v1/econsent/templates/{template_id}/versions/{version_index}/comprehension-checks',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/econsent/api/v1/econsent/templates/{template_id}/versions/{version_index}/comprehension-checks' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/econsent/templates/{template_id}/versions/{version_index}/comprehension-checks`

**Summary:** Get Comprehension Check  
**Operation ID:** `get_comprehension_check_api_v1_econsent_templates__template_id__versions__version_index__comprehension_checks_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `template_id` | `path` | `string` | ✔ Yes | — |
| `version_index` | `path` | `integer` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ComprehensionCheckResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/templates/{template_id}/versions/{version_index}/comprehension-checks', {
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
        '/api/v1/econsent/api/v1/econsent/templates/{template_id}/versions/{version_index}/comprehension-checks',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/econsent/api/v1/econsent/templates/{template_id}/versions/{version_index}/comprehension-checks' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/econsent/templates/{template_id}/versions/{version_index}/sign`

**Summary:** Sign Consent Template Endpoint  
**Operation ID:** `sign_consent_template_endpoint_api_v1_econsent_templates__template_id__versions__version_index__sign_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `template_id` | `path` | `string` | ✔ Yes | — |
| `version_index` | `path` | `integer` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ConsentSignatureRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `subject_pseudonym` | `string` | ✔ Yes | Pseudonym identifier of the subject |
| `signature_data` | `string` | `null` | No | Electronic signature data (drawing or string) |
| `reason_for_change` | `string` | ✔ Yes | Change reason for signing |
| `site_id` | `string` | `null` | No | Optional site identifier |
| `role` | `string` | No | Signer role: SUBJECT, LAR, MINOR_ASSENT, INVESTIGATOR, WITNESS |
| `signer_name` | `string` | `null` | No | Printed legal name of signer |
| `signer_email` | `string` | `null` | No | Contact email of signer |
| `meaning` | `string` | `null` | No | 21 CFR Part 11 signing intent |
| `lar_relationship` | `string` | `null` | No | Relationship if role is LAR |
| `lar_authority_basis` | `string` | `null` | No | Legal basis if role is LAR |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ConsentSignatureResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/templates/{template_id}/versions/{version_index}/sign', {
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
        '/api/v1/econsent/api/v1/econsent/templates/{template_id}/versions/{version_index}/sign',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/econsent/api/v1/econsent/templates/{template_id}/versions/{version_index}/sign' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/econsent/templates/{template_id}/versions/{version_index}/submit-answers`

**Summary:** Submit Comprehension Answers Endpoint  
**Operation ID:** `submit_comprehension_answers_endpoint_api_v1_econsent_templates__template_id__versions__version_index__submit_answers_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `template_id` | `path` | `string` | ✔ Yes | — |
| `version_index` | `path` | `integer` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ComprehensionSubmissionRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `subject_pseudonym` | `string` | ✔ Yes | Pseudonym identifier of the subject |
| `submitted_answers` | `object` | ✔ Yes | Mapping of question_id to submitted answer |
| `reason_for_change` | `string` | ✔ Yes | Part 11 signature/evaluation change reason |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ComprehensionSubmissionResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/templates/{template_id}/versions/{version_index}/submit-answers', {
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
        '/api/v1/econsent/api/v1/econsent/templates/{template_id}/versions/{version_index}/submit-answers',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/econsent/api/v1/econsent/templates/{template_id}/versions/{version_index}/submit-answers' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/econsent/translations`

**Summary:** Create Consent Translation  
**Operation ID:** `create_consent_translation_api_v1_econsent_translations_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ConsentTranslationCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `created_at` | `string` | No | Chronological UTC timestamp when the record was created. |
| `created_by` | `string` | ✔ Yes | Unique identifier of the user who created the record. |
| `reason_for_change` | `string` | ✔ Yes | Mandatory explanation or audit justification for creating or mutating this record. |
| `version_index` | `integer` | No | Row version counter or index. |
| `translation_id` | `string` | `null` | No | Unique translation identifier across versions. Generated if not provided. |
| `source_id` | `string` | ✔ Yes | Unique source clause_id or template_id being translated |
| `source_type` | `string` | ✔ Yes | The type of the source: 'clause' or 'template' |
| `source_version_index` | `integer` | ✔ Yes | The version of the source being translated |
| `language_code` | `string` | ✔ Yes | Validated ISO 639-1 language code |
| `translated_title` | `string` | ✔ Yes | Translated title of the clause/template |
| `translated_text` | `string` | ✔ Yes | Translated text/content |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `ConsentTranslationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/translations', {
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
        '/api/v1/econsent/api/v1/econsent/translations',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/econsent/api/v1/econsent/translations' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/econsent/translations`

**Summary:** List Consent Translations  
**Operation ID:** `list_consent_translations_api_v1_econsent_translations_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `source_id` | `query` | `string` | `null` | No | — |
| `source_type` | `query` | `string` | `null` | No | — |
| `language_code` | `query` | `string` | `null` | No | — |
| `status` | `query` | `string` | `null` | No | — |
| `all_versions` | `query` | `boolean` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<ConsentTranslationResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/translations', {
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
        '/api/v1/econsent/api/v1/econsent/translations',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/econsent/api/v1/econsent/translations' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/econsent/translations/{translation_id}`

**Summary:** Update Consent Translation  
**Operation ID:** `update_consent_translation_api_v1_econsent_translations__translation_id__put`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `translation_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ConsentTranslationUpdate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `created_at` | `string` | No | Chronological UTC timestamp when the record was created. |
| `created_by` | `string` | ✔ Yes | Unique identifier of the user who created the record. |
| `reason_for_change` | `string` | ✔ Yes | Mandatory explanation or audit justification for creating or mutating this record. |
| `version_index` | `integer` | No | Row version counter or index. |
| `source_id` | `string` | ✔ Yes | Unique source clause_id or template_id being translated |
| `source_type` | `string` | ✔ Yes | The type of the source: 'clause' or 'template' |
| `source_version_index` | `integer` | ✔ Yes | The version of the source being translated |
| `language_code` | `string` | ✔ Yes | Validated ISO 639-1 language code |
| `translated_title` | `string` | ✔ Yes | Translated title of the clause/template |
| `translated_text` | `string` | ✔ Yes | Translated text/content |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ConsentTranslationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/translations/{translation_id}', {
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
        '/api/v1/econsent/api/v1/econsent/translations/{translation_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/econsent/api/v1/econsent/translations/{translation_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/econsent/translations/{translation_id}`

**Summary:** Get Consent Translation  
**Operation ID:** `get_consent_translation_api_v1_econsent_translations__translation_id__get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `translation_id` | `path` | `string` | ✔ Yes | — |
| `version_index` | `query` | `integer` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ConsentTranslationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/translations/{translation_id}', {
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
        '/api/v1/econsent/api/v1/econsent/translations/{translation_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/econsent/api/v1/econsent/translations/{translation_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/econsent/translations/{translation_id}/transition`

**Summary:** Transition Consent Translation  
**Operation ID:** `transition_consent_translation_api_v1_econsent_translations__translation_id__transition_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `translation_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `TranslationTransitionRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `status` | `string` | ✔ Yes | Target status: 'DRAFT', 'IN_REVIEW', or 'APPROVED' |
| `reason_for_change` | `string` | ✔ Yes | Explanation of transition |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ConsentTranslationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/translations/{translation_id}/transition', {
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
        '/api/v1/econsent/api/v1/econsent/translations/{translation_id}/transition',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/econsent/api/v1/econsent/translations/{translation_id}/transition' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/econsent/withdrawal`

**Summary:** Withdraw Subject Consent  
**Operation ID:** `withdraw_subject_consent_api_v1_econsent_withdrawal_post`  

**Clinical & GxP Rationale:**  
Formally revokes/withdraws a subject's consent and records 21 CFR Part 11 audit records.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ConsentWithdrawalRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | — |
| `site_id` | `string` | ✔ Yes | — |
| `subject_pseudonym` | `string` | ✔ Yes | — |
| `template_id` | `string` | ✔ Yes | — |
| `withdrawal_date` | `string` | `null` | No | — |
| `reason_category` | `string` | ✔ Yes | Category: Adverse Event, Personal Choice, Relocation, Other |
| `reason_detail` | `string` | ✔ Yes | Specific explanation for withdrawal |
| `scope` | `string` | No | STOP_INTERVENTIONS_ONLY, STOP_ALL_DATA_COLLECTION, DESTROY_SAMPLES |
| `investigator_id` | `string` | `null` | No | — |
| `reason_for_change` | `string` | ✔ Yes | Mandatory change justification |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `ConsentWithdrawalResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/withdrawal', {
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
        '/api/v1/econsent/api/v1/econsent/withdrawal',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/econsent/api/v1/econsent/withdrawal' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/econsent/withdrawal/{study_id}/{subject_pseudonym}`

**Summary:** Get Subject Withdrawal Status  
**Operation ID:** `get_subject_withdrawal_status_api_v1_econsent_withdrawal__study_id___subject_pseudonym__get`  

**Clinical & GxP Rationale:**  
Retrieves the withdrawal record for a subject if one exists.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `subject_pseudonym` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ConsentWithdrawalResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/econsent/api/v1/econsent/withdrawal/{study_id}/{subject_pseudonym}', {
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
        '/api/v1/econsent/api/v1/econsent/withdrawal/{study_id}/{subject_pseudonym}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/econsent/api/v1/econsent/withdrawal/{study_id}/{subject_pseudonym}' \
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
const response = await fetch('http://localhost:8000/api/v1/econsent/health', {
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
        '/api/v1/econsent/health',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/econsent/health' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---
