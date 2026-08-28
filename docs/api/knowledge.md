# Knowledge & SOP Engine

> **Functional Domain:** `Protocol & Study Design`  
> **Service Host Port:** `localhost:8011`  
> **Gateway Proxy Prefix:** `/api/v1/knowledge`  
> **Persistence Backend:** `PostgreSQL / SQLite`  

## Overview

Contextual SOP delivery, dynamic help drawer routing, role-specific clinical guidance, and controlled document versioning.

---

## Endpoints Summary

| Method | Path | Summary | GxP Auth & Signatures |
| :--- | :--- | :--- | :--- |
| `POST` | [/api/v1/knowledge/articles](#post-api-v1-knowledge-articles) | Create Article | ✍️ Audited Write |
| `GET` | [/api/v1/knowledge/articles](#get-api-v1-knowledge-articles) | List Articles | 👀 Read-Only |
| `GET` | [/api/v1/knowledge/articles/search](#get-api-v1-knowledge-articles-search) | Search Articles | 👀 Read-Only |
| `PUT` | [/api/v1/knowledge/articles/{article_id}](#put-api-v1-knowledge-articles-article_id) | Update Article Draft | ✍️ Audited Write |
| `GET` | [/api/v1/knowledge/articles/{article_id}](#get-api-v1-knowledge-articles-article_id) | Get Article | 👀 Read-Only |
| `POST` | [/api/v1/knowledge/articles/{article_id}/approve](#post-api-v1-knowledge-articles-article_id-approve) | Approve Article | ✍️ Audited Write |
| `GET` | [/api/v1/knowledge/articles/{article_id}/audit-log](#get-api-v1-knowledge-articles-article_id-audit-log) | Get Article Audit Trail | 👀 Read-Only |
| `GET` | [/api/v1/knowledge/articles/{article_id}/audit-trail](#get-api-v1-knowledge-articles-article_id-audit-trail) | Get Article Audit Trail | 👀 Read-Only |
| `PATCH` | [/api/v1/knowledge/articles/{article_id}/draft](#patch-api-v1-knowledge-articles-article_id-draft) | Save Article Draft | ✍️ Audited Write |
| `PUT` | [/api/v1/knowledge/articles/{article_id}/draft](#put-api-v1-knowledge-articles-article_id-draft) | Save Article Draft | ✍️ Audited Write |
| `POST` | [/api/v1/knowledge/articles/{article_id}/publish](#post-api-v1-knowledge-articles-article_id-publish) | Publish Article | ✍️ Audited Write |
| `POST` | [/api/v1/knowledge/articles/{article_id}/reject](#post-api-v1-knowledge-articles-article_id-reject) | Reject Article | ✍️ Audited Write |
| `POST` | [/api/v1/knowledge/articles/{article_id}/submit-review](#post-api-v1-knowledge-articles-article_id-submit-review) | Submit Article Review | ✍️ Audited Write |
| `POST` | [/api/v1/knowledge/articles/{article_id}/transition](#post-api-v1-knowledge-articles-article_id-transition) | Transition Article | ✍️ Audited Write |
| `GET` | [/api/v1/knowledge/articles/{article_id}/verify-audit-chain](#get-api-v1-knowledge-articles-article_id-verify-audit-chain) | Verify Article Audit Chain | 👀 Read-Only |
| `GET` | [/api/v1/knowledge/articles/{article_id}/versions](#get-api-v1-knowledge-articles-article_id-versions) | List Article Versions | 👀 Read-Only |
| `GET` | [/api/v1/knowledge/articles/{article_id}/versions/{version_id}](#get-api-v1-knowledge-articles-article_id-versions-version_id) | Get Article Version | 👀 Read-Only |
| `GET` | [/api/v1/knowledge/categories](#get-api-v1-knowledge-categories) | List Categories | 👀 Read-Only |
| `POST` | [/api/v1/knowledge/categories](#post-api-v1-knowledge-categories) | Create Category | ✍️ Audited Write |
| `GET` | [/api/v1/knowledge/categories/{category_id}](#get-api-v1-knowledge-categories-category_id) | Get Category | 👀 Read-Only |
| `DELETE` | [/api/v1/knowledge/categories/{category_id}](#delete-api-v1-knowledge-categories-category_id) | Delete Category | ✍️ Audited Write |
| `POST` | [/api/v1/knowledge/contextual-help](#post-api-v1-knowledge-contextual-help) | Create Contextual Help Mapping | ✍️ Audited Write |
| `GET` | [/api/v1/knowledge/contextual-help](#get-api-v1-knowledge-contextual-help) | Resolve Contextual Help | 👀 Read-Only |
| `GET` | [/api/v1/knowledge/contextual-help/lookup](#get-api-v1-knowledge-contextual-help-lookup) | Resolve Contextual Help | 👀 Read-Only |
| `POST` | [/api/v1/knowledge/contextual-help/mappings](#post-api-v1-knowledge-contextual-help-mappings) | Create Contextual Help Mapping | ✍️ Audited Write |
| `GET` | [/api/v1/knowledge/contextual-help/mappings](#get-api-v1-knowledge-contextual-help-mappings) | List Contextual Help Mappings | 👀 Read-Only |
| `GET` | [/api/v1/knowledge/contextual-help/mappings/{mapping_id}](#get-api-v1-knowledge-contextual-help-mappings-mapping_id) | Get Contextual Help Mapping | 👀 Read-Only |
| `PUT` | [/api/v1/knowledge/contextual-help/mappings/{mapping_id}](#put-api-v1-knowledge-contextual-help-mappings-mapping_id) | Update Contextual Help Mapping | ✍️ Audited Write |
| `DELETE` | [/api/v1/knowledge/contextual-help/mappings/{mapping_id}](#delete-api-v1-knowledge-contextual-help-mappings-mapping_id) | Delete Contextual Help Mapping | ✍️ Audited Write |
| `GET` | [/api/v1/knowledge/protocols/chunks/{chunk_id}](#get-api-v1-knowledge-protocols-chunks-chunk_id) | Retrieve full details for a single protocol chunk by ID | 👀 Read-Only |
| `POST` | [/api/v1/knowledge/protocols/ingest](#post-api-v1-knowledge-protocols-ingest) | Ingest a protocol or SOP document and index dense vector chunks | ✍️ Audited Write |
| `POST` | [/api/v1/knowledge/protocols/search](#post-api-v1-knowledge-protocols-search) | Perform dense vector semantic search over protocol chunks | ✍️ Audited Write |
| `POST` | [/api/v1/knowledge/protocols/upload](#post-api-v1-knowledge-protocols-upload) | Upload and ingest a protocol PDF/document binary file | ✍️ Audited Write |
| `GET` | [/api/v1/knowledge/search](#get-api-v1-knowledge-search) | Search Articles | 👀 Read-Only |
| `GET` | [/api/v1/training/assignments](#get-api-v1-training-assignments) | List Assignments Endpoint | 👀 Read-Only |
| `GET` | [/api/v1/training/assignments/{assignment_id}](#get-api-v1-training-assignments-assignment_id) | Get Assignment Detail Endpoint | 👀 Read-Only |
| `GET` | [/api/v1/training/assignments/{assignment_id}/certificate](#get-api-v1-training-assignments-assignment_id-certificate) | Get Certificate Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/training/assignments/{assignment_id}/progress](#post-api-v1-training-assignments-assignment_id-progress) | Record Progress Endpoint | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/training/assignments/{assignment_id}/sign-off](#post-api-v1-training-assignments-assignment_id-sign-off) | Sign Off Training Endpoint | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/training/assignments/{assignment_id}/submit-quiz](#post-api-v1-training-assignments-assignment_id-submit-quiz) | Submit Quiz Endpoint | 🔒 Step-Up Sig Token |
| `GET` | [/api/v1/training/curricula](#get-api-v1-training-curricula) | List Curricula Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/training/curricula/generate-amendment](#post-api-v1-training-curricula-generate-amendment) | Generate Amendment Curriculum Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/training/curricula/{curriculum_id}](#get-api-v1-training-curricula-curriculum_id) | Get Curriculum Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/training/curricula/{curriculum_id}/assign](#post-api-v1-training-curricula-curriculum_id-assign) | Assign Curriculum Endpoint | 🔒 Step-Up Sig Token |
| `GET` | [/api/v1/training/gating-status](#get-api-v1-training-gating-status) | Get Gating Status Endpoint | 👀 Read-Only |
| `POST` | [/api/v1/training/verify-gate](#post-api-v1-training-verify-gate) | Verify Gate Endpoint | ✍️ Audited Write |
| `GET` | [/health](#get-health) | Health Check | 👀 Read-Only |

---

## Granular Endpoint Specifications

### `POST` `/api/v1/knowledge/articles`

**Summary:** Create Article  
**Operation ID:** `create_article_api_v1_knowledge_articles_post`  

**Clinical & GxP Rationale:**  
Creates a new KnowledgeArticle in DRAFT status with an initial working draft version.
Requires super_admin role.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ArticleCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `title` | `string` | ✔ Yes | — |
| `slug` | `string` | ✔ Yes | — |
| `category_id` | `string` | ✔ Yes | — |
| `body_markdown` | `string` | ✔ Yes | — |
| `version_label` | `string` | No | — |
| `tags` | `Array<string>` | `string` | `null` | No | — |
| `reason_for_change` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `ArticleResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles', {
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
        '/api/v1/knowledge/api/v1/knowledge/articles',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/knowledge/articles`

**Summary:** List Articles  
**Operation ID:** `list_articles_api_v1_knowledge_articles_get`  

**Clinical & GxP Rationale:**  
Lists knowledge articles, optionally filtered by status and/or category.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `status_filter` | `query` | `ArticleStatus` | `null` | No | — |
| `category_id` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<ArticleResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles', {
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
        '/api/v1/knowledge/api/v1/knowledge/articles',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/knowledge/articles/search`

**Summary:** Search Articles  
**Operation ID:** `search_articles_api_v1_knowledge_articles_search_get`  

**Clinical & GxP Rationale:**  
Full-text searches published knowledge articles across titles, tags, and Markdown bodies (Issue #4329).

- Returns relevance-ranked articles with contextual highlighted snippets (<mark>...</mark>).
- Scopes visibility to categories accessible by the caller's persona roles.
- PostgreSQL tsvector-indexed in production; multi-dialect lexical fallback in SQLite test harnesses.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `q` | `query` | `string` | No | — |
| `category_id` | `query` | `string` | `null` | No | — |
| `limit` | `query` | `integer` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<ArticleSearchResult>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/search', {
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
        '/api/v1/knowledge/api/v1/knowledge/articles/search',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/search' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/knowledge/articles/{article_id}`

**Summary:** Update Article Draft  
**Operation ID:** `update_article_draft_api_v1_knowledge_articles__article_id__put`  

**Clinical & GxP Rationale:**  
Updates the working draft of an article (Issue #4325).

Updates the single KnowledgeArticleVersion row during DRAFT status with markdown
body, auto-rendered HTML, and GxP audit fields (created_by, reason_for_change, version_index).
Requires super_admin role.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `article_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ArticleUpdate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `title` | `string` | `null` | No | — |
| `slug` | `string` | `null` | No | — |
| `category_id` | `string` | `null` | No | — |
| `body_markdown` | `string` | ✔ Yes | — |
| `tags` | `Array<string>` | `string` | `null` | No | — |
| `reason_for_change` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ArticleResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}', {
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
        '/api/v1/knowledge/api/v1/knowledge/articles/{article_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/knowledge/articles/{article_id}`

**Summary:** Get Article  
**Operation ID:** `get_article_api_v1_knowledge_articles__article_id__get`  

**Clinical & GxP Rationale:**  
Retrieves a single KnowledgeArticle by ID or unique slug along with its active body content.
If the requesting user has an auditor role, records a READ_BY_AUDITOR audit event.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `article_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ArticleResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}', {
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
        '/api/v1/knowledge/api/v1/knowledge/articles/{article_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/knowledge/articles/{article_id}/approve`

**Summary:** Approve Article  
**Operation ID:** `approve_article_api_v1_knowledge_articles__article_id__approve_post`  

**Clinical & GxP Rationale:**  
Approves an article in IN_REVIEW status (IN_REVIEW -> APPROVED).

Enforces Four-Eyes check: approver cannot be author_user_id or last_edited_by.
Locks current KnowledgeArticleVersion record as permanently immutable.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `article_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ArticleApproveRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `reason_for_change` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ArticleResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/approve', {
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
        '/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/approve',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/approve' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/knowledge/articles/{article_id}/audit-log`

**Summary:** Get Article Audit Trail  
**Operation ID:** `get_article_audit_trail_api_v1_knowledge_articles__article_id__audit_log_get`  

**Clinical & GxP Rationale:**  
Returns the immutable SHA-256 digest chained audit trail for a specific article.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `article_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<AuditLogResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/audit-log', {
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
        '/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/audit-log',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/audit-log' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/knowledge/articles/{article_id}/audit-trail`

**Summary:** Get Article Audit Trail  
**Operation ID:** `get_article_audit_trail_api_v1_knowledge_articles__article_id__audit_trail_get`  

**Clinical & GxP Rationale:**  
Returns the immutable SHA-256 digest chained audit trail for a specific article.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `article_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<AuditLogResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/audit-trail', {
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
        '/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/audit-trail',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/audit-trail' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PATCH` `/api/v1/knowledge/articles/{article_id}/draft`

**Summary:** Save Article Draft  
**Operation ID:** `save_article_draft_api_v1_knowledge_articles__article_id__draft_patch`  

**Clinical & GxP Rationale:**  
Saves updated body content to a DRAFT article (Issue #4325). Requires super_admin role.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `article_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ArticleDraftSave`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `body_markdown` | `string` | ✔ Yes | — |
| `title` | `string` | `null` | No | — |
| `slug` | `string` | `null` | No | — |
| `category_id` | `string` | `null` | No | — |
| `tags` | `Array<string>` | `string` | `null` | No | — |
| `reason_for_change` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ArticleVersionResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/draft', {
  method: 'PATCH',
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
    response = await client.patch(
        '/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/draft',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PATCH 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/draft' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/knowledge/articles/{article_id}/draft`

**Summary:** Save Article Draft  
**Operation ID:** `save_article_draft_api_v1_knowledge_articles__article_id__draft_put`  

**Clinical & GxP Rationale:**  
Saves updated body content to a DRAFT article (Issue #4325). Requires super_admin role.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `article_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ArticleDraftSave`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `body_markdown` | `string` | ✔ Yes | — |
| `title` | `string` | `null` | No | — |
| `slug` | `string` | `null` | No | — |
| `category_id` | `string` | `null` | No | — |
| `tags` | `Array<string>` | `string` | `null` | No | — |
| `reason_for_change` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ArticleVersionResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/draft', {
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
        '/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/draft',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/draft' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/knowledge/articles/{article_id}/publish`

**Summary:** Publish Article  
**Operation ID:** `publish_article_api_v1_knowledge_articles__article_id__publish_post`  

**Clinical & GxP Rationale:**  
Publishes an APPROVED article (APPROVED -> PUBLISHED).

- Updates fast O(1) lookup pointer KnowledgeArticle.current_published_version_id.
- Publishing version N+1 automatically sets prior active version N to SUPERSEDED.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `article_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ArticlePublishRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `reason_for_change` | `string` | ✔ Yes | — |
| `version_label` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ArticleResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/publish', {
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
        '/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/publish',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/publish' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/knowledge/articles/{article_id}/reject`

**Summary:** Reject Article  
**Operation ID:** `reject_article_api_v1_knowledge_articles__article_id__reject_post`  

**Clinical & GxP Rationale:**  
Rejects an article in IN_REVIEW status (IN_REVIEW -> REJECTED).

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `article_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ArticleRejectRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `reason_for_change` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ArticleResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/reject', {
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
        '/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/reject',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/reject' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/knowledge/articles/{article_id}/submit-review`

**Summary:** Submit Article Review  
**Operation ID:** `submit_article_review_api_v1_knowledge_articles__article_id__submit_review_post`  

**Clinical & GxP Rationale:**  
Submits a DRAFT article for peer review (DRAFT -> IN_REVIEW).

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `article_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ArticleSubmitReviewRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `reason_for_change` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ArticleResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/submit-review', {
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
        '/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/submit-review',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/submit-review' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/knowledge/articles/{article_id}/transition`

**Summary:** Transition Article  
**Operation ID:** `transition_article_api_v1_knowledge_articles__article_id__transition_post`  

**Clinical & GxP Rationale:**  
Generic lifecycle state machine transition endpoint.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `article_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ArticleTransitionRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `target_status` | `ArticleStatus` | ✔ Yes | — |
| `reason_for_change` | `string` | `null` | No | — |
| `version_label` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ArticleResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/transition', {
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
        '/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/transition',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/transition' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/knowledge/articles/{article_id}/verify-audit-chain`

**Summary:** Verify Article Audit Chain  
**Operation ID:** `verify_article_audit_chain_api_v1_knowledge_articles__article_id__verify_audit_chain_get`  

**Clinical & GxP Rationale:**  
Cryptographically verifies the SHA-256 digest chain integrity for an article's audit trail.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `article_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `AuditChainVerificationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/verify-audit-chain', {
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
        '/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/verify-audit-chain',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/verify-audit-chain' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/knowledge/articles/{article_id}/versions`

**Summary:** List Article Versions  
**Operation ID:** `list_article_versions_api_v1_knowledge_articles__article_id__versions_get`  

**Clinical & GxP Rationale:**  
Lists all immutable version snapshots for an article (chronological order).

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `article_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<ArticleVersionResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/versions', {
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
        '/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/versions',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/versions' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/knowledge/articles/{article_id}/versions/{version_id}`

**Summary:** Get Article Version  
**Operation ID:** `get_article_version_api_v1_knowledge_articles__article_id__versions__version_id__get`  

**Clinical & GxP Rationale:**  
Retrieves a specific immutable version snapshot for an article.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `article_id` | `path` | `string` | ✔ Yes | — |
| `version_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ArticleVersionResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/versions/{version_id}', {
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
        '/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/versions/{version_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/articles/{article_id}/versions/{version_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/knowledge/categories`

**Summary:** List Categories  
**Operation ID:** `list_categories_api_v1_knowledge_categories_get`  

**Clinical & GxP Rationale:**  
Lists active knowledge categories, filtered by caller's persona visibility.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<CategoryResponse>` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/categories', {
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
        '/api/v1/knowledge/api/v1/knowledge/categories',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/categories' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/knowledge/categories`

**Summary:** Create Category  
**Operation ID:** `create_category_api_v1_knowledge_categories_post`  

**Clinical & GxP Rationale:**  
Creates a new knowledge article category. Requires super_admin role.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CategoryCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `name` | `string` | ✔ Yes | — |
| `slug` | `string` | ✔ Yes | — |
| `description` | `string` | `null` | No | — |
| `persona_visibility` | `string` | `null` | No | — |
| `parent_id` | `string` | `null` | No | — |
| `reason_for_change` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `CategoryResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/categories', {
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
        '/api/v1/knowledge/api/v1/knowledge/categories',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/categories' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/knowledge/categories/{category_id}`

**Summary:** Get Category  
**Operation ID:** `get_category_api_v1_knowledge_categories__category_id__get`  

**Clinical & GxP Rationale:**  
Retrieves a single KnowledgeCategory by ID.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `category_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `CategoryResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/categories/{category_id}', {
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
        '/api/v1/knowledge/api/v1/knowledge/categories/{category_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/categories/{category_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `DELETE` `/api/v1/knowledge/categories/{category_id}`

**Summary:** Delete Category  
**Operation ID:** `delete_category_api_v1_knowledge_categories__category_id__delete`  

**Clinical & GxP Rationale:**  
Soft-deletes a KnowledgeCategory by ID. Requires super_admin or sysadmin role.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `category_id` | `path` | `string` | ✔ Yes | — |
| `reason_for_change` | `query` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `CategoryResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/categories/{category_id}', {
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
        '/api/v1/knowledge/api/v1/knowledge/categories/{category_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/categories/{category_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/knowledge/contextual-help`

**Summary:** Create Contextual Help Mapping  
**Operation ID:** `create_contextual_help_mapping_api_v1_knowledge_contextual_help_post`  

**Clinical & GxP Rationale:**  
Creates a new contextual help route mapping. Requires super_admin role.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ContextualHelpMappingCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `route_pattern` | `string` | ✔ Yes | — |
| `persona` | `string` | `null` | No | — |
| `article_id` | `string` | ✔ Yes | — |
| `section_anchor` | `string` | `null` | No | — |
| `priority` | `integer` | No | — |
| `is_active` | `boolean` | No | — |
| `reason_for_change` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `ContextualHelpMappingResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/contextual-help', {
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
        '/api/v1/knowledge/api/v1/knowledge/contextual-help',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/contextual-help' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/knowledge/contextual-help`

**Summary:** Resolve Contextual Help  
**Operation ID:** `resolve_contextual_help_api_v1_knowledge_contextual_help_get`  

**Clinical & GxP Rationale:**  
Dynamically resolves in-page contextual SOP guidance and relevant articles
for a given clinical screen route (e.g. '/ecrf/site-101/subjects') and persona role.

Resolution algorithm:
- Matches exact routes, parameterized routes, prefix wildcards, and global catch-alls.
- Resolves via hierarchical specificity: priority ASC, persona match, pattern specificity, length, and recency.
- Surfaces 1 primary spotlight article + up to 3 secondary related guides.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `route` | `query` | `string` | ✔ Yes | — |
| `persona` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ContextualHelpResolutionResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/contextual-help', {
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
        '/api/v1/knowledge/api/v1/knowledge/contextual-help',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/contextual-help' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/knowledge/contextual-help/lookup`

**Summary:** Resolve Contextual Help  
**Operation ID:** `resolve_contextual_help_api_v1_knowledge_contextual_help_lookup_get`  

**Clinical & GxP Rationale:**  
Dynamically resolves in-page contextual SOP guidance and relevant articles
for a given clinical screen route (e.g. '/ecrf/site-101/subjects') and persona role.

Resolution algorithm:
- Matches exact routes, parameterized routes, prefix wildcards, and global catch-alls.
- Resolves via hierarchical specificity: priority ASC, persona match, pattern specificity, length, and recency.
- Surfaces 1 primary spotlight article + up to 3 secondary related guides.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `route` | `query` | `string` | ✔ Yes | — |
| `persona` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ContextualHelpResolutionResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/contextual-help/lookup', {
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
        '/api/v1/knowledge/api/v1/knowledge/contextual-help/lookup',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/contextual-help/lookup' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/knowledge/contextual-help/mappings`

**Summary:** Create Contextual Help Mapping  
**Operation ID:** `create_contextual_help_mapping_api_v1_knowledge_contextual_help_mappings_post`  

**Clinical & GxP Rationale:**  
Creates a new contextual help route mapping. Requires super_admin role.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ContextualHelpMappingCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `route_pattern` | `string` | ✔ Yes | — |
| `persona` | `string` | `null` | No | — |
| `article_id` | `string` | ✔ Yes | — |
| `section_anchor` | `string` | `null` | No | — |
| `priority` | `integer` | No | — |
| `is_active` | `boolean` | No | — |
| `reason_for_change` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `ContextualHelpMappingResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/contextual-help/mappings', {
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
        '/api/v1/knowledge/api/v1/knowledge/contextual-help/mappings',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/contextual-help/mappings' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/knowledge/contextual-help/mappings`

**Summary:** List Contextual Help Mappings  
**Operation ID:** `list_contextual_help_mappings_api_v1_knowledge_contextual_help_mappings_get`  

**Clinical & GxP Rationale:**  
Lists all contextual help mappings with optional filters. Requires super_admin role.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `route_pattern` | `query` | `string` | `null` | No | — |
| `persona` | `query` | `string` | `null` | No | — |
| `is_active` | `query` | `boolean` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<ContextualHelpMappingResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/contextual-help/mappings', {
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
        '/api/v1/knowledge/api/v1/knowledge/contextual-help/mappings',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/contextual-help/mappings' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/knowledge/contextual-help/mappings/{mapping_id}`

**Summary:** Get Contextual Help Mapping  
**Operation ID:** `get_contextual_help_mapping_api_v1_knowledge_contextual_help_mappings__mapping_id__get`  

**Clinical & GxP Rationale:**  
Retrieves a single contextual help mapping by ID. Requires super_admin role.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `mapping_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ContextualHelpMappingResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/contextual-help/mappings/{mapping_id}', {
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
        '/api/v1/knowledge/api/v1/knowledge/contextual-help/mappings/{mapping_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/contextual-help/mappings/{mapping_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/knowledge/contextual-help/mappings/{mapping_id}`

**Summary:** Update Contextual Help Mapping  
**Operation ID:** `update_contextual_help_mapping_api_v1_knowledge_contextual_help_mappings__mapping_id__put`  

**Clinical & GxP Rationale:**  
Updates an existing contextual help mapping. Requires super_admin role.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `mapping_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ContextualHelpMappingUpdate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `route_pattern` | `string` | `null` | No | — |
| `persona` | `string` | `null` | No | — |
| `article_id` | `string` | `null` | No | — |
| `section_anchor` | `string` | `null` | No | — |
| `priority` | `integer` | `null` | No | — |
| `is_active` | `boolean` | `null` | No | — |
| `reason_for_change` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ContextualHelpMappingResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/contextual-help/mappings/{mapping_id}', {
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
        '/api/v1/knowledge/api/v1/knowledge/contextual-help/mappings/{mapping_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/contextual-help/mappings/{mapping_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `DELETE` `/api/v1/knowledge/contextual-help/mappings/{mapping_id}`

**Summary:** Delete Contextual Help Mapping  
**Operation ID:** `delete_contextual_help_mapping_api_v1_knowledge_contextual_help_mappings__mapping_id__delete`  

**Clinical & GxP Rationale:**  
Deletes a contextual help mapping by ID. Requires super_admin role.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `mapping_id` | `path` | `string` | ✔ Yes | — |
| `reason_for_change` | `query` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/contextual-help/mappings/{mapping_id}', {
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
        '/api/v1/knowledge/api/v1/knowledge/contextual-help/mappings/{mapping_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/contextual-help/mappings/{mapping_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/knowledge/protocols/chunks/{chunk_id}`

**Summary:** Retrieve full details for a single protocol chunk by ID  
**Operation ID:** `get_protocol_chunk_api_v1_knowledge_protocols_chunks__chunk_id__get`  

**Clinical & GxP Rationale:**  
Retrieves full chunk text and metadata for interactive UI document preview.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `chunk_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ProtocolSearchResult` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/protocols/chunks/{chunk_id}', {
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
        '/api/v1/knowledge/api/v1/knowledge/protocols/chunks/{chunk_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/protocols/chunks/{chunk_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/knowledge/protocols/ingest`

**Summary:** Ingest a protocol or SOP document and index dense vector chunks  
**Operation ID:** `ingest_protocol_document_api_v1_knowledge_protocols_ingest_post`  

**Clinical & GxP Rationale:**  
Ingests protocol text, extracts section/page coordinates, and indexes vector chunks.

Requires sponsor_designer, data_manager, or super_admin role.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ProtocolIngestTextRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Clinical study scope identifier. |
| `protocol_version` | `string` | ✔ Yes | Version label, e.g. 'v2.1'. |
| `raw_text` | `string` | ✔ Yes | Protocol or SOP text content. |
| `filename` | `string` | No | Document file name. |
| `document_id` | `string` | `null` | No | Optional custom document ID. |
| `document_type` | `string` | No | 'PROTOCOL' or 'SOP'. |
| `is_approved` | `boolean` | No | Whether this is an approved version. |
| `reason_for_change` | `string` | No | 21 CFR Part 11 justification. |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `Array<ProtocolChunkSummary>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/protocols/ingest', {
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
        '/api/v1/knowledge/api/v1/knowledge/protocols/ingest',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/protocols/ingest' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/knowledge/protocols/search`

**Summary:** Perform dense vector semantic search over protocol chunks  
**Operation ID:** `search_protocol_chunks_api_v1_knowledge_protocols_search_post`  

**Clinical & GxP Rationale:**  
Searches protocol chunks via dense vector cosine similarity.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ProtocolSearchRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `query` | `string` | ✔ Yes | Inquiry search query. |
| `study_id` | `string` | ✔ Yes | Study scope filter. |
| `protocol_version` | `string` | `null` | No | Optional protocol version filter. |
| `only_approved` | `boolean` | No | Only search approved protocol versions. |
| `top_k` | `integer` | No | Maximum matching chunks to return. |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<ProtocolSearchResult>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/protocols/search', {
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
        '/api/v1/knowledge/api/v1/knowledge/protocols/search',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/protocols/search' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/knowledge/protocols/upload`

**Summary:** Upload and ingest a protocol PDF/document binary file  
**Operation ID:** `upload_protocol_file_api_v1_knowledge_protocols_upload_post`  

**Clinical & GxP Rationale:**  
Uploads a PDF or document binary, parses pages, and indexes dense vector chunks.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `any`

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `Array<ProtocolChunkSummary>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/protocols/upload', {
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
        '/api/v1/knowledge/api/v1/knowledge/protocols/upload',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/protocols/upload' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/knowledge/search`

**Summary:** Search Articles  
**Operation ID:** `search_articles_api_v1_knowledge_search_get`  

**Clinical & GxP Rationale:**  
Full-text searches published knowledge articles across titles, tags, and Markdown bodies (Issue #4329).

- Returns relevance-ranked articles with contextual highlighted snippets (<mark>...</mark>).
- Scopes visibility to categories accessible by the caller's persona roles.
- PostgreSQL tsvector-indexed in production; multi-dialect lexical fallback in SQLite test harnesses.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `q` | `query` | `string` | No | — |
| `category_id` | `query` | `string` | `null` | No | — |
| `limit` | `query` | `integer` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<ArticleSearchResult>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/knowledge/search', {
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
        '/api/v1/knowledge/api/v1/knowledge/search',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/knowledge/api/v1/knowledge/search' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/training/assignments`

**Summary:** List Assignments Endpoint  
**Operation ID:** `list_assignments_endpoint_api_v1_training_assignments_get`  

**Clinical & GxP Rationale:**  
List training assignments for a user or site.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | — |
| `site_id` | `query` | `string` | `null` | No | — |
| `user_id` | `query` | `string` | `null` | No | — |
| `status` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<TrainingAssignmentResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/training/assignments', {
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
        '/api/v1/knowledge/api/v1/training/assignments',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/knowledge/api/v1/training/assignments' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/training/assignments/{assignment_id}`

**Summary:** Get Assignment Detail Endpoint  
**Operation ID:** `get_assignment_detail_endpoint_api_v1_training_assignments__assignment_id__get`  

**Clinical & GxP Rationale:**  
Retrieve full details of an assignment including curriculum modules.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `assignment_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `TrainingAssignmentDetailResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/training/assignments/{assignment_id}', {
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
        '/api/v1/knowledge/api/v1/training/assignments/{assignment_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/knowledge/api/v1/training/assignments/{assignment_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/training/assignments/{assignment_id}/certificate`

**Summary:** Get Certificate Endpoint  
**Operation ID:** `get_certificate_endpoint_api_v1_training_assignments__assignment_id__certificate_get`  

**Clinical & GxP Rationale:**  
Retrieve completion certificate for an assignment.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `assignment_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `CertificateResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/training/assignments/{assignment_id}/certificate', {
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
        '/api/v1/knowledge/api/v1/training/assignments/{assignment_id}/certificate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/knowledge/api/v1/training/assignments/{assignment_id}/certificate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/training/assignments/{assignment_id}/progress`

**Summary:** Record Progress Endpoint  
**Operation ID:** `record_progress_endpoint_api_v1_training_assignments__assignment_id__progress_post`  

**Clinical & GxP Rationale:**  
Record reading duration and time-on-page tracking.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `assignment_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `RecordProgressRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `time_spent_delta_seconds` | `integer` | ✔ Yes | Seconds spent in active training |
| `reason_for_change` | `string` | No | GxP reason for change |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `TrainingAssignmentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/training/assignments/{assignment_id}/progress', {
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
        '/api/v1/knowledge/api/v1/training/assignments/{assignment_id}/progress',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/knowledge/api/v1/training/assignments/{assignment_id}/progress' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/training/assignments/{assignment_id}/sign-off`

**Summary:** Sign Off Training Endpoint  
**Operation ID:** `sign_off_training_endpoint_api_v1_training_assignments__assignment_id__sign_off_post`  

**Clinical & GxP Rationale:**  
Capture 21 CFR Part 11 electronic signature, generate sealed PDF certificate, and push to eTMF & eISF.

Requirements: PRD-TRN-003, PRD-TRN-004

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `assignment_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SignTrainingRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `signer_name` | `string` | ✔ Yes | Legal printed name of signer |
| `signer_role` | `string` | ✔ Yes | Clinical role (e.g. Principal Investigator, CRC) |
| `meaning` | `string` | No | 21 CFR Part 11 signature intent statement |
| `reason_for_change` | `string` | No | GxP reason for change |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `CertificateResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/training/assignments/{assignment_id}/sign-off', {
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
        '/api/v1/knowledge/api/v1/training/assignments/{assignment_id}/sign-off',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/knowledge/api/v1/training/assignments/{assignment_id}/sign-off' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/training/assignments/{assignment_id}/submit-quiz`

**Summary:** Submit Quiz Endpoint  
**Operation ID:** `submit_quiz_endpoint_api_v1_training_assignments__assignment_id__submit_quiz_post`  

**Clinical & GxP Rationale:**  
Submit comprehension quiz answers and evaluate score (>= 80% passing).

Requirements: PRD-TRN-003

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `assignment_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SubmitQuizRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `answers` | `object` | ✔ Yes | Dictionary mapping question ID to selected 0-indexed option |
| `reason_for_change` | `string` | No | GxP reason for change |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `QuizEvaluationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/training/assignments/{assignment_id}/submit-quiz', {
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
        '/api/v1/knowledge/api/v1/training/assignments/{assignment_id}/submit-quiz',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/knowledge/api/v1/training/assignments/{assignment_id}/submit-quiz' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/training/curricula`

**Summary:** List Curricula Endpoint  
**Operation ID:** `list_curricula_endpoint_api_v1_training_curricula_get`  

**Clinical & GxP Rationale:**  
List active training curricula.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | Study scope filter |
| `protocol_version` | `query` | `string` | `null` | No | Protocol version filter |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<TrainingCurriculumResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/training/curricula', {
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
        '/api/v1/knowledge/api/v1/training/curricula',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/knowledge/api/v1/training/curricula' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/training/curricula/generate-amendment`

**Summary:** Generate Amendment Curriculum Endpoint  
**Operation ID:** `generate_amendment_curriculum_endpoint_api_v1_training_curricula_generate_amendment_post`  

**Clinical & GxP Rationale:**  
Generate a delta-training curriculum upon protocol amendment publication.

Requirements: PRD-TRN-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `GenerateAmendmentCurriculumRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Unique clinical study identifier |
| `protocol_version` | `string` | ✔ Yes | Amended protocol version string (e.g. '2.0') |
| `baseline_snapshot` | `object` | ✔ Yes | Baseline USDM protocol snapshot |
| `amended_snapshot` | `object` | ✔ Yes | Amended USDM protocol snapshot |
| `target_roles` | `Array<string>` | `null` | No | Optional target roles list |
| `site_assignments` | `Array<object>` | `null` | No | Optional list of site personnel dictionaries to auto-assign |
| `reason_for_change` | `string` | No | 21 CFR Part 11 justification |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `TrainingCurriculumResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/training/curricula/generate-amendment', {
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
        '/api/v1/knowledge/api/v1/training/curricula/generate-amendment',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/knowledge/api/v1/training/curricula/generate-amendment' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/training/curricula/{curriculum_id}`

**Summary:** Get Curriculum Endpoint  
**Operation ID:** `get_curriculum_endpoint_api_v1_training_curricula__curriculum_id__get`  

**Clinical & GxP Rationale:**  
Retrieve details for a single training curriculum.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `curriculum_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `TrainingCurriculumResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/training/curricula/{curriculum_id}', {
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
        '/api/v1/knowledge/api/v1/training/curricula/{curriculum_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/knowledge/api/v1/training/curricula/{curriculum_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/training/curricula/{curriculum_id}/assign`

**Summary:** Assign Curriculum Endpoint  
**Operation ID:** `assign_curriculum_endpoint_api_v1_training_curricula__curriculum_id__assign_post`  

**Clinical & GxP Rationale:**  
Assign a training curriculum to a site user.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `curriculum_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `AssignCurriculumRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Clinical study ID |
| `site_id` | `string` | ✔ Yes | Clinical site ID |
| `user_id` | `string` | ✔ Yes | Participant user ID |
| `user_name` | `string` | ✔ Yes | Participant legal printed name |
| `user_role` | `string` | No | Participant clinical role |
| `reason_for_change` | `string` | No | GxP reason for change |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `TrainingAssignmentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/training/curricula/{curriculum_id}/assign', {
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
        '/api/v1/knowledge/api/v1/training/curricula/{curriculum_id}/assign',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/knowledge/api/v1/training/curricula/{curriculum_id}/assign' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/training/gating-status`

**Summary:** Get Gating Status Endpoint  
**Operation ID:** `get_gating_status_endpoint_api_v1_training_gating_status_get`  

**Clinical & GxP Rationale:**  
Check whether a user is qualified for EDC write permissions on a protocol version.

Requirements: PRD-TRN-002

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `user_id` | `query` | `string` | ✔ Yes | User identifier |
| `study_id` | `query` | `string` | ✔ Yes | Study identifier |
| `protocol_version` | `query` | `string` | ✔ Yes | Active protocol version |
| `site_id` | `query` | `string` | `null` | No | Optional site identifier |
| `user_role` | `query` | `string` | `null` | No | Optional clinical role |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `QualificationGateResult` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/training/gating-status', {
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
        '/api/v1/knowledge/api/v1/training/gating-status',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/knowledge/api/v1/training/gating-status' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/training/verify-gate`

**Summary:** Verify Gate Endpoint  
**Operation ID:** `verify_gate_endpoint_api_v1_training_verify_gate_post`  

**Clinical & GxP Rationale:**  
Programmatically verify qualification gate for EDC write operations.

Requirements: PRD-TRN-002

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `VerifyGateRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `user_id` | `string` | ✔ Yes | User ID to verify |
| `study_id` | `string` | ✔ Yes | Clinical study identifier |
| `protocol_version` | `string` | ✔ Yes | Active protocol version tag |
| `site_id` | `string` | `null` | No | Optional clinical site ID |
| `user_role` | `string` | `null` | No | Optional user clinical role |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `QualificationGateResult` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/knowledge/api/v1/training/verify-gate', {
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
        '/api/v1/knowledge/api/v1/training/verify-gate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/knowledge/api/v1/training/verify-gate' \
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
const response = await fetch('http://localhost:8000/api/v1/knowledge/health', {
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
        '/api/v1/knowledge/health',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/knowledge/health' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---
