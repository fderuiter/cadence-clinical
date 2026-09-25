# Compliance & Qualification Engine

> **Functional Domain:** `Platform & Gateway Infrastructure`  
> **Service Host Port:** `localhost:8019`  
> **Gateway Proxy Prefix:** `/api/v1/compliance`  
> **Persistence Backend:** `Pytest + Audit Ledgers`  

## Overview

Centralized regulatory compliance verification, IQ/OQ/PQ execution suites, and automated 21 CFR Part 11 audit trail assertions.

---

## Endpoints Summary

| Method | Path | Summary | GxP Auth & Signatures |
| :--- | :--- | :--- | :--- |
| `GET` | [/api/v1/compliance/health](#get-api-v1-compliance-health) | Compliance & Qualification Engine Health Check | 👀 Read-Only |

---

## Granular Endpoint Specifications

### `GET` `/api/v1/compliance/health`

**Summary:** Compliance & Qualification Engine Health Check  
**Operation ID:** `get_/api/v1/compliance/health`  

**Clinical & GxP Rationale:**  
Verifies live operational status and database connectivity for Compliance & Qualification Engine.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Service is healthy and operational | `any` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/compliance/api/v1/compliance/health', {
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
        '/api/v1/compliance/api/v1/compliance/health',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/compliance/api/v1/compliance/health' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---
