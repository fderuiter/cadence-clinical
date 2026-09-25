# Organization Directory

> **Functional Domain:** `Clinical Operations & Execution`  
> **Service Host Port:** `localhost:8008`  
> **Gateway Proxy Prefix:** `/api/v1/org`  
> **Persistence Backend:** `PostgreSQL / SQLite`  

## Overview

Global directory of Sponsor/CRO organizations, clinical trial sites, staff credentials, and Principal Investigator Delegation of Authority (DOA) logs.

---

## Endpoints Summary

| Method | Path | Summary | GxP Auth & Signatures |
| :--- | :--- | :--- | :--- |
| `GET` | [/api/v1/org/assignments/resolve](#get-api-v1-org-assignments-resolve) | Resolve Assignments | 👀 Read-Only |
| `GET` | [/api/v1/org/audit-logs](#get-api-v1-org-audit-logs) | List Org Audit Logs | 👀 Read-Only |
| `POST` | [/api/v1/org/delegations](#post-api-v1-org-delegations) | Create Delegation | ✍️ Audited Write |
| `GET` | [/api/v1/org/delegations](#get-api-v1-org-delegations) | List Delegations | 👀 Read-Only |
| `GET` | [/api/v1/org/delegations/{id}](#get-api-v1-org-delegations-id) | Get Delegation | 👀 Read-Only |
| `GET` | [/api/v1/org/delegations/{id}/history](#get-api-v1-org-delegations-id-history) | Get Delegation History | 👀 Read-Only |
| `POST` | [/api/v1/org/delegations/{id}/revoke](#post-api-v1-org-delegations-id-revoke) | Revoke Delegation | ✍️ Audited Write |
| `POST` | [/api/v1/org/delegations/{id}/sign-off](#post-api-v1-org-delegations-id-sign-off) | Sign Delegation | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/org/organizations](#post-api-v1-org-organizations) | Create Organization | ✍️ Audited Write |
| `GET` | [/api/v1/org/organizations](#get-api-v1-org-organizations) | List Organizations | 👀 Read-Only |
| `GET` | [/api/v1/org/organizations/{id}](#get-api-v1-org-organizations-id) | Get Organization | 👀 Read-Only |
| `PUT` | [/api/v1/org/organizations/{id}](#put-api-v1-org-organizations-id) | Update Organization | ✍️ Audited Write |
| `GET` | [/api/v1/org/organizations/{id}/history](#get-api-v1-org-organizations-id-history) | Get Organization History | 👀 Read-Only |
| `POST` | [/api/v1/org/personnel](#post-api-v1-org-personnel) | Create Personnel | ✍️ Audited Write |
| `GET` | [/api/v1/org/personnel](#get-api-v1-org-personnel) | List Personnel | 👀 Read-Only |
| `PUT` | [/api/v1/org/personnel/assignments/{id}](#put-api-v1-org-personnel-assignments-id) | Update Personnel Assignment | 🔒 Step-Up Sig Token |
| `GET` | [/api/v1/org/personnel/assignments/{id}/history](#get-api-v1-org-personnel-assignments-id-history) | Get Personnel Assignment History | 👀 Read-Only |
| `GET` | [/api/v1/org/personnel/{id}](#get-api-v1-org-personnel-id) | Get Personnel | 👀 Read-Only |
| `PUT` | [/api/v1/org/personnel/{id}](#put-api-v1-org-personnel-id) | Update Personnel | ✍️ Audited Write |
| `GET` | [/api/v1/org/personnel/{id}/history](#get-api-v1-org-personnel-id-history) | Get Personnel History | 👀 Read-Only |
| `POST` | [/api/v1/org/personnel/{personnel_id}/assignments](#post-api-v1-org-personnel-personnel_id-assignments) | Create Personnel Assignment | 🔒 Step-Up Sig Token |
| `GET` | [/api/v1/org/personnel/{personnel_id}/assignments](#get-api-v1-org-personnel-personnel_id-assignments) | List Personnel Assignments | 👀 Read-Only |
| `POST` | [/api/v1/org/sites](#post-api-v1-org-sites) | Create Site | ✍️ Audited Write |
| `GET` | [/api/v1/org/sites](#get-api-v1-org-sites) | List Sites | 👀 Read-Only |
| `GET` | [/api/v1/org/sites/{id}](#get-api-v1-org-sites-id) | Get Site | 👀 Read-Only |
| `PUT` | [/api/v1/org/sites/{id}](#put-api-v1-org-sites-id) | Update Site | ✍️ Audited Write |
| `GET` | [/api/v1/org/sites/{id}/history](#get-api-v1-org-sites-id-history) | Get Site History | 👀 Read-Only |
| `POST` | [/api/v1/org/training-logs](#post-api-v1-org-training-logs) | Create Training Log | ✍️ Audited Write |
| `GET` | [/api/v1/org/training-logs](#get-api-v1-org-training-logs) | List Training Logs | 👀 Read-Only |
| `GET` | [/api/v1/org/training-logs/{id}](#get-api-v1-org-training-logs-id) | Get Training Log | 👀 Read-Only |
| `PUT` | [/api/v1/org/training-logs/{id}](#put-api-v1-org-training-logs-id) | Update Training Log | ✍️ Audited Write |
| `GET` | [/api/v1/org/training-logs/{id}/history](#get-api-v1-org-training-logs-id-history) | Get Training Log History | 👀 Read-Only |
| `POST` | [/api/v1/org/training-logs/{id}/sign](#post-api-v1-org-training-logs-id-sign) | Sign Training Log | 🔒 Step-Up Sig Token |
| `GET` | [/health](#get-health) | Health Check | 👀 Read-Only |

---

## Granular Endpoint Specifications

### `GET` `/api/v1/org/assignments/resolve`

**Summary:** Resolve Assignments  
**Operation ID:** `resolve_assignments_api_v1_org_assignments_resolve_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `keycloak_user_id` | `query` | `string` | ✔ Yes | The Keycloak user ID to resolve |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `AssignmentResolutionResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/assignments/resolve', {
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
        '/api/v1/org/api/v1/org/assignments/resolve',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/org/api/v1/org/assignments/resolve' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/org/audit-logs`

**Summary:** List Org Audit Logs  
**Operation ID:** `list_org_audit_logs_api_v1_org_audit_logs_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<OrgAuditLogResponse>` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/audit-logs', {
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
        '/api/v1/org/api/v1/org/audit-logs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/org/api/v1/org/audit-logs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/org/delegations`

**Summary:** Create Delegation  
**Operation ID:** `create_delegation_api_v1_org_delegations_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `DelegationCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `delegator_id` | `string` | ✔ Yes | The Personnel ID of the delegator (typically the PI) |
| `delegatee_id` | `string` | ✔ Yes | The Personnel ID of the delegatee |
| `site_id` | `string` | ✔ Yes | The site ID where authority is being delegated |
| `study_id` | `string` | ✔ Yes | The study ID |
| `duties` | `Array<string>` | ✔ Yes | List of delegated duties |
| `start_date` | `string` | ✔ Yes | The effective start date of the delegation |
| `end_date` | `string` | `null` | No | Optional effective end date of the delegation |
| `reason_for_change` | `string` | ✔ Yes | Part 11 change justification reason |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `DelegationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/delegations', {
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
        '/api/v1/org/api/v1/org/delegations',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/org/api/v1/org/delegations' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/org/delegations`

**Summary:** List Delegations  
**Operation ID:** `list_delegations_api_v1_org_delegations_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `site_id` | `query` | `string` | `null` | No | Filter by site_id |
| `study_id` | `query` | `string` | `null` | No | Filter by study_id |
| `delegator_id` | `query` | `string` | `null` | No | Filter by delegator personnel ID |
| `delegatee_id` | `query` | `string` | `null` | No | Filter by delegatee personnel ID |
| `is_active` | `query` | `boolean` | `null` | No | Filter by active status |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<DelegationResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/delegations', {
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
        '/api/v1/org/api/v1/org/delegations',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/org/api/v1/org/delegations' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/org/delegations/{id}`

**Summary:** Get Delegation  
**Operation ID:** `get_delegation_api_v1_org_delegations__id__get`  

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
| `version_index` | `query` | `integer` | `null` | No | Optionally retrieve a specific version |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DelegationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/delegations/{id}', {
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
        '/api/v1/org/api/v1/org/delegations/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/org/api/v1/org/delegations/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/org/delegations/{id}/history`

**Summary:** Get Delegation History  
**Operation ID:** `get_delegation_history_api_v1_org_delegations__id__history_get`  

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
| `200` | Successful Response | `Array<DelegationResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/delegations/{id}/history', {
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
        '/api/v1/org/api/v1/org/delegations/{id}/history',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/org/api/v1/org/delegations/{id}/history' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/org/delegations/{id}/revoke`

**Summary:** Revoke Delegation  
**Operation ID:** `revoke_delegation_api_v1_org_delegations__id__revoke_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `DelegationRevoke`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `reason_for_change` | `string` | ✔ Yes | The revocation reason / Part 11 justification |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DelegationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/delegations/{id}/revoke', {
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
        '/api/v1/org/api/v1/org/delegations/{id}/revoke',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/org/api/v1/org/delegations/{id}/revoke' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/org/delegations/{id}/sign-off`

**Summary:** Sign Delegation  
**Operation ID:** `sign_delegation_api_v1_org_delegations__id__sign_off_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `DelegationSign`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `payload` | `object` | ✔ Yes | The canonical delegation payload to sign and verify |
| `signature` | `string` | ✔ Yes | The symmetric HMAC canonical signature of the payload |
| `reason_for_change` | `string` | ✔ Yes | Part 11 change justification reason |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DelegationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/delegations/{id}/sign-off', {
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
        '/api/v1/org/api/v1/org/delegations/{id}/sign-off',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/org/api/v1/org/delegations/{id}/sign-off' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/org/organizations`

**Summary:** Create Organization  
**Operation ID:** `create_organization_api_v1_org_organizations_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `OrganizationCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `name` | `string` | ✔ Yes | Name of the organization |
| `org_type` | `OrganizationType` | ✔ Yes | Type of the organization |
| `reason_for_change` | `string` | ✔ Yes | Part 11 change justification reason |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `OrganizationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/organizations', {
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
        '/api/v1/org/api/v1/org/organizations',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/org/api/v1/org/organizations' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/org/organizations`

**Summary:** List Organizations  
**Operation ID:** `list_organizations_api_v1_org_organizations_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `name` | `query` | `string` | `null` | No | Filter by partial organization name |
| `org_type` | `query` | `OrganizationType` | `null` | No | Filter by organization type |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<OrganizationResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/organizations', {
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
        '/api/v1/org/api/v1/org/organizations',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/org/api/v1/org/organizations' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/org/organizations/{id}`

**Summary:** Get Organization  
**Operation ID:** `get_organization_api_v1_org_organizations__id__get`  

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
| `version_index` | `query` | `integer` | `null` | No | Optionally retrieve a specific version |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `OrganizationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/organizations/{id}', {
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
        '/api/v1/org/api/v1/org/organizations/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/org/api/v1/org/organizations/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/org/organizations/{id}`

**Summary:** Update Organization  
**Operation ID:** `update_organization_api_v1_org_organizations__id__put`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `OrganizationUpdate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `name` | `string` | `null` | No | Updated name of the organization |
| `org_type` | `OrganizationType` | `null` | No | Updated type of the organization |
| `reason_for_change` | `string` | ✔ Yes | Part 11 change justification reason |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `OrganizationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/organizations/{id}', {
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
        '/api/v1/org/api/v1/org/organizations/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/org/api/v1/org/organizations/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/org/organizations/{id}/history`

**Summary:** Get Organization History  
**Operation ID:** `get_organization_history_api_v1_org_organizations__id__history_get`  

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
| `200` | Successful Response | `Array<OrganizationResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/organizations/{id}/history', {
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
        '/api/v1/org/api/v1/org/organizations/{id}/history',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/org/api/v1/org/organizations/{id}/history' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/org/personnel`

**Summary:** Create Personnel  
**Operation ID:** `create_personnel_api_v1_org_personnel_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `PersonnelCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `keycloak_user_id` | `string` | `null` | No | OIDC user ID linked to this staff member |
| `first_name` | `string` | ✔ Yes | First name |
| `last_name` | `string` | ✔ Yes | Last name |
| `email` | `string` | ✔ Yes | Unique email address |
| `role` | `ClinicalStaffRole` | ✔ Yes | Clinical staff role |
| `organization_id` | `string` | `null` | No | Reference to parent organization ID |
| `site_id` | `string` | `null` | No | Reference to parent site_id |
| `study_id` | `string` | `null` | No | Optional clinical study ID |
| `reason_for_change` | `string` | ✔ Yes | Part 11 change justification reason |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `PersonnelResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/personnel', {
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
        '/api/v1/org/api/v1/org/personnel',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/org/api/v1/org/personnel' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/org/personnel`

**Summary:** List Personnel  
**Operation ID:** `list_personnel_api_v1_org_personnel_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `site_id` | `query` | `string` | `null` | No | Filter by site_id |
| `study_id` | `query` | `string` | `null` | No | Filter by study_id |
| `organization_id` | `query` | `string` | `null` | No | Filter by organization_id |
| `role` | `query` | `ClinicalStaffRole` | `null` | No | Filter by exact staff role |
| `email` | `query` | `string` | `null` | No | Filter by exact or partial email |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<PersonnelResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/personnel', {
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
        '/api/v1/org/api/v1/org/personnel',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/org/api/v1/org/personnel' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/org/personnel/assignments/{id}`

**Summary:** Update Personnel Assignment  
**Operation ID:** `update_personnel_assignment_api_v1_org_personnel_assignments__id__put`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `PersonnelAssignmentUpdate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `site_id` | `string` | `null` | No | Updated clinical site ID |
| `study_id` | `string` | `null` | No | Updated clinical study ID |
| `is_active` | `boolean` | `null` | No | Updated active status |
| `reason_for_change` | `string` | ✔ Yes | Part 11 change justification reason |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `PersonnelAssignmentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/personnel/assignments/{id}', {
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
        '/api/v1/org/api/v1/org/personnel/assignments/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/org/api/v1/org/personnel/assignments/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/org/personnel/assignments/{id}/history`

**Summary:** Get Personnel Assignment History  
**Operation ID:** `get_personnel_assignment_history_api_v1_org_personnel_assignments__id__history_get`  

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
| `200` | Successful Response | `Array<PersonnelAssignmentResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/personnel/assignments/{id}/history', {
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
        '/api/v1/org/api/v1/org/personnel/assignments/{id}/history',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/org/api/v1/org/personnel/assignments/{id}/history' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/org/personnel/{id}`

**Summary:** Get Personnel  
**Operation ID:** `get_personnel_api_v1_org_personnel__id__get`  

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
| `version_index` | `query` | `integer` | `null` | No | Optionally retrieve a specific version |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `PersonnelResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/personnel/{id}', {
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
        '/api/v1/org/api/v1/org/personnel/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/org/api/v1/org/personnel/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/org/personnel/{id}`

**Summary:** Update Personnel  
**Operation ID:** `update_personnel_api_v1_org_personnel__id__put`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `PersonnelUpdate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `keycloak_user_id` | `string` | `null` | No | OIDC user ID linked to this staff member |
| `first_name` | `string` | `null` | No | Updated first name |
| `last_name` | `string` | `null` | No | Updated last name |
| `email` | `string` | `null` | No | Updated email address |
| `role` | `ClinicalStaffRole` | `null` | No | Updated clinical staff role |
| `organization_id` | `string` | `null` | No | Updated reference to parent organization ID |
| `site_id` | `string` | `null` | No | Updated reference to parent site_id |
| `study_id` | `string` | `null` | No | Updated clinical study ID |
| `reason_for_change` | `string` | ✔ Yes | Part 11 change justification reason |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `PersonnelResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/personnel/{id}', {
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
        '/api/v1/org/api/v1/org/personnel/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/org/api/v1/org/personnel/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/org/personnel/{id}/history`

**Summary:** Get Personnel History  
**Operation ID:** `get_personnel_history_api_v1_org_personnel__id__history_get`  

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
| `200` | Successful Response | `Array<PersonnelResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/personnel/{id}/history', {
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
        '/api/v1/org/api/v1/org/personnel/{id}/history',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/org/api/v1/org/personnel/{id}/history' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/org/personnel/{personnel_id}/assignments`

**Summary:** Create Personnel Assignment  
**Operation ID:** `create_personnel_assignment_api_v1_org_personnel__personnel_id__assignments_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `personnel_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `PersonnelAssignmentCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `site_id` | `string` | ✔ Yes | The clinical site ID |
| `study_id` | `string` | ✔ Yes | The clinical study ID |
| `is_active` | `boolean` | No | Whether the assignment is active |
| `reason_for_change` | `string` | ✔ Yes | Part 11 change justification reason |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `PersonnelAssignmentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/personnel/{personnel_id}/assignments', {
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
        '/api/v1/org/api/v1/org/personnel/{personnel_id}/assignments',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/org/api/v1/org/personnel/{personnel_id}/assignments' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/org/personnel/{personnel_id}/assignments`

**Summary:** List Personnel Assignments  
**Operation ID:** `list_personnel_assignments_api_v1_org_personnel__personnel_id__assignments_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `personnel_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<PersonnelAssignmentResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/personnel/{personnel_id}/assignments', {
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
        '/api/v1/org/api/v1/org/personnel/{personnel_id}/assignments',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/org/api/v1/org/personnel/{personnel_id}/assignments' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/org/sites`

**Summary:** Create Site  
**Operation ID:** `create_site_api_v1_org_sites_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `SiteCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `site_id` | `string` | ✔ Yes | Unique client-defined identifier for the site |
| `name` | `string` | ✔ Yes | Name of the site |
| `organization_id` | `string` | ✔ Yes | Reference to the parent organization ID |
| `study_id` | `string` | `null` | No | Optional clinical study ID |
| `reason_for_change` | `string` | ✔ Yes | Part 11 change justification reason |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `SiteResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/sites', {
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
        '/api/v1/org/api/v1/org/sites',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/org/api/v1/org/sites' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/org/sites`

**Summary:** List Sites  
**Operation ID:** `list_sites_api_v1_org_sites_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `site_id` | `query` | `string` | `null` | No | Filter by site_id |
| `study_id` | `query` | `string` | `null` | No | Filter by study_id |
| `organization_id` | `query` | `string` | `null` | No | Filter by organization_id |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<SiteResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/sites', {
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
        '/api/v1/org/api/v1/org/sites',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/org/api/v1/org/sites' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/org/sites/{id}`

**Summary:** Get Site  
**Operation ID:** `get_site_api_v1_org_sites__id__get`  

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
| `version_index` | `query` | `integer` | `null` | No | Optionally retrieve a specific version |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SiteResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/sites/{id}', {
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
        '/api/v1/org/api/v1/org/sites/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/org/api/v1/org/sites/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/org/sites/{id}`

**Summary:** Update Site  
**Operation ID:** `update_site_api_v1_org_sites__id__put`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SiteUpdate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `site_id` | `string` | `null` | No | Updated identifier for the site |
| `name` | `string` | `null` | No | Updated name of the site |
| `organization_id` | `string` | `null` | No | Updated reference to parent organization ID |
| `study_id` | `string` | `null` | No | Updated clinical study ID |
| `reason_for_change` | `string` | ✔ Yes | Part 11 change justification reason |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SiteResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/sites/{id}', {
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
        '/api/v1/org/api/v1/org/sites/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/org/api/v1/org/sites/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/org/sites/{id}/history`

**Summary:** Get Site History  
**Operation ID:** `get_site_history_api_v1_org_sites__id__history_get`  

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
| `200` | Successful Response | `Array<SiteResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/sites/{id}/history', {
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
        '/api/v1/org/api/v1/org/sites/{id}/history',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/org/api/v1/org/sites/{id}/history' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/org/training-logs`

**Summary:** Create Training Log  
**Operation ID:** `create_training_log_api_v1_org_training_logs_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `TrainingLogCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `personnel_id` | `string` | ✔ Yes | The clinical personnel ID |
| `site_id` | `string` | ✔ Yes | The clinical site ID |
| `study_id` | `string` | ✔ Yes | The clinical study ID |
| `training_topic` | `string` | ✔ Yes | The training topic or certification name |
| `completion_date` | `string` | ✔ Yes | Date when training was completed |
| `reason_for_change` | `string` | ✔ Yes | Part 11 change justification reason |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `TrainingLogResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/training-logs', {
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
        '/api/v1/org/api/v1/org/training-logs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/org/api/v1/org/training-logs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/org/training-logs`

**Summary:** List Training Logs  
**Operation ID:** `list_training_logs_api_v1_org_training_logs_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `personnel_id` | `query` | `string` | `null` | No | Filter by personnel ID |
| `site_id` | `query` | `string` | `null` | No | Filter by site ID |
| `study_id` | `query` | `string` | `null` | No | Filter by study ID |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<TrainingLogResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/training-logs', {
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
        '/api/v1/org/api/v1/org/training-logs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/org/api/v1/org/training-logs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/org/training-logs/{id}`

**Summary:** Get Training Log  
**Operation ID:** `get_training_log_api_v1_org_training_logs__id__get`  

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
| `version_index` | `query` | `integer` | `null` | No | Optionally retrieve a specific version |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `TrainingLogResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/training-logs/{id}', {
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
        '/api/v1/org/api/v1/org/training-logs/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/org/api/v1/org/training-logs/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/org/training-logs/{id}`

**Summary:** Update Training Log  
**Operation ID:** `update_training_log_api_v1_org_training_logs__id__put`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `TrainingLogUpdate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `personnel_id` | `string` | `null` | No | Updated personnel ID |
| `site_id` | `string` | `null` | No | Updated site ID |
| `study_id` | `string` | `null` | No | Updated study ID |
| `training_topic` | `string` | `null` | No | Updated training topic |
| `completion_date` | `string` | `null` | No | Updated completion date |
| `reason_for_change` | `string` | `null` | No | Part 11 change justification reason |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `TrainingLogResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/training-logs/{id}', {
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
        '/api/v1/org/api/v1/org/training-logs/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/org/api/v1/org/training-logs/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/org/training-logs/{id}/history`

**Summary:** Get Training Log History  
**Operation ID:** `get_training_log_history_api_v1_org_training_logs__id__history_get`  

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
| `200` | Successful Response | `Array<TrainingLogResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/training-logs/{id}/history', {
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
        '/api/v1/org/api/v1/org/training-logs/{id}/history',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/org/api/v1/org/training-logs/{id}/history' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/org/training-logs/{id}/sign`

**Summary:** Sign Training Log  
**Operation ID:** `sign_training_log_api_v1_org_training_logs__id__sign_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `TrainingLogSignRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `payload` | `object` | ✔ Yes | The canonical training log payload to sign |
| `signature` | `string` | ✔ Yes | The HMAC-SHA256 signature validating payload integrity |
| `reason_for_change` | `string` | ✔ Yes | The justification reason for the sign-off action |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `TrainingLogResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/org/api/v1/org/training-logs/{id}/sign', {
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
        '/api/v1/org/api/v1/org/training-logs/{id}/sign',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/org/api/v1/org/training-logs/{id}/sign' \
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
const response = await fetch('http://localhost:8000/api/v1/org/health', {
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
        '/api/v1/org/health',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/org/health' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---
