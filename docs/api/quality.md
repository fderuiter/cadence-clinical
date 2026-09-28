# Quality & CAPA Management

> **Functional Domain:** `Safety, Quality & Governance`  
> **Service Host Port:** `localhost:8006`  
> **Gateway Proxy Prefix:** `/api/v1/quality`  
> **Persistence Backend:** `PostgreSQL / SQLite`  

## Overview

Protocol deviation tracking, Root Cause Analysis (RCA), CAPA workflows, Centralized Statistical Monitoring (CSM forensics), and pHash image fraud detection.

---

## Endpoints Summary

| Method | Path | Summary | GxP Auth & Signatures |
| :--- | :--- | :--- | :--- |
| `GET` | [/api/v1/quality/audit-logs](#get-api-v1-quality-audit-logs) | List Audit Logs | 👀 Read-Only |
| `POST` | [/api/v1/quality/audits](#post-api-v1-quality-audits) | Create Audit | ✍️ Audited Write |
| `GET` | [/api/v1/quality/audits](#get-api-v1-quality-audits) | List Audits | 👀 Read-Only |
| `POST` | [/api/v1/quality/audits/findings/{finding_id}/promote-capa](#post-api-v1-quality-audits-findings-finding_id-promote-capa) | Promote Finding To Capa | ✍️ Audited Write |
| `GET` | [/api/v1/quality/audits/inspection-dossier/{study_id}](#get-api-v1-quality-audits-inspection-dossier-study_id) | Get Inspection Readiness Dossier | 👀 Read-Only |
| `GET` | [/api/v1/quality/audits/{id}](#get-api-v1-quality-audits-id) | Get Audit | 👀 Read-Only |
| `POST` | [/api/v1/quality/audits/{id}/findings](#post-api-v1-quality-audits-id-findings) | Create Audit Finding | ✍️ Audited Write |
| `GET` | [/api/v1/quality/audits/{id}/findings](#get-api-v1-quality-audits-id-findings) | List Audit Findings | 👀 Read-Only |
| `PUT` | [/api/v1/quality/audits/{id}/status](#put-api-v1-quality-audits-id-status) | Update Audit Status | ✍️ Audited Write |
| `POST` | [/api/v1/quality/capas](#post-api-v1-quality-capas) | Create Capa | ✍️ Audited Write |
| `GET` | [/api/v1/quality/capas](#get-api-v1-quality-capas) | List Capas | 👀 Read-Only |
| `PUT` | [/api/v1/quality/capas/action-items/{item_id}](#put-api-v1-quality-capas-action-items-item_id) | Update Action Item Status | ✍️ Audited Write |
| `GET` | [/api/v1/quality/capas/{id}](#get-api-v1-quality-capas-id) | View Capa | 👀 Read-Only |
| `PUT` | [/api/v1/quality/capas/{id}](#put-api-v1-quality-capas-id) | Update Capa | ✍️ Audited Write |
| `POST` | [/api/v1/quality/capas/{id}/action-items](#post-api-v1-quality-capas-id-action-items) | Create Action Item | ✍️ Audited Write |
| `POST` | [/api/v1/quality/capas/{id}/effectiveness](#post-api-v1-quality-capas-id-effectiveness) | Record Effectiveness Check | ✍️ Audited Write |
| `POST` | [/api/v1/quality/capas/{id}/transition](#post-api-v1-quality-capas-id-transition) | Transition Capa | ✍️ Audited Write |
| `POST` | [/api/v1/quality/deviations](#post-api-v1-quality-deviations) | Create Deviation | ✍️ Audited Write |
| `GET` | [/api/v1/quality/deviations](#get-api-v1-quality-deviations) | List Deviations | 👀 Read-Only |
| `GET` | [/api/v1/quality/deviations/{id}](#get-api-v1-quality-deviations-id) | View Deviation | 👀 Read-Only |
| `PUT` | [/api/v1/quality/deviations/{id}/rca](#put-api-v1-quality-deviations-id-rca) | Create Or Update Rca | ✍️ Audited Write |
| `POST` | [/api/v1/quality/deviations/{id}/rca](#post-api-v1-quality-deviations-id-rca) | Create Or Update Rca | ✍️ Audited Write |
| `POST` | [/api/v1/quality/forensics/analyze](#post-api-v1-quality-forensics-analyze) | Execute Forensics Analysis | ✍️ Audited Write |
| `GET` | [/api/v1/quality/forensics/runs](#get-api-v1-quality-forensics-runs) | List Forensics Runs | 👀 Read-Only |
| `GET` | [/api/v1/quality/forensics/runs/{run_id}](#get-api-v1-quality-forensics-runs-run_id) | Get Forensics Run | 👀 Read-Only |
| `POST` | [/api/v1/quality/forensics/runs/{run_id}/trigger-kri-alerts](#post-api-v1-quality-forensics-runs-run_id-trigger-kri-alerts) | Trigger Run Kri Alerts | ✍️ Audited Write |
| `POST` | [/api/v1/quality/forensics/simulate](#post-api-v1-quality-forensics-simulate) | Simulate Forensics Dataset | ✍️ Audited Write |
| `GET` | [/api/v1/quality/forensics/sites/{site_id}](#get-api-v1-quality-forensics-sites-site_id) | Get Site Forensics History | 👀 Read-Only |
| `POST` | [/api/v1/quality/ingest/event](#post-api-v1-quality-ingest-event) | Ingest Quality Event | ✍️ Audited Write |
| `GET` | [/api/v1/quality/rbqm/csm/dossiers](#get-api-v1-quality-rbqm-csm-dossiers) | List Csm Dossiers | 👀 Read-Only |
| `POST` | [/api/v1/quality/rbqm/csm/dossiers/compile](#post-api-v1-quality-rbqm-csm-dossiers-compile) | Compile Csm Dossier | ✍️ Audited Write |
| `GET` | [/api/v1/quality/rbqm/csm/dossiers/{dossier_id}](#get-api-v1-quality-rbqm-csm-dossiers-dossier_id) | Get Csm Dossier | 👀 Read-Only |
| `POST` | [/api/v1/quality/rbqm/ctq](#post-api-v1-quality-rbqm-ctq) | Create Ctq Factor | ✍️ Audited Write |
| `GET` | [/api/v1/quality/rbqm/ctq](#get-api-v1-quality-rbqm-ctq) | List Ctq Factors | 👀 Read-Only |
| `GET` | [/api/v1/quality/rbqm/evaluations](#get-api-v1-quality-rbqm-evaluations) | List Kri Evaluations | 👀 Read-Only |
| `GET` | [/api/v1/quality/rbqm/kris](#get-api-v1-quality-rbqm-kris) | List Kri Definitions | 👀 Read-Only |
| `POST` | [/api/v1/quality/rbqm/kris](#post-api-v1-quality-rbqm-kris) | Create Kri Definition | ✍️ Audited Write |
| `POST` | [/api/v1/quality/rbqm/kris/evaluate-batch](#post-api-v1-quality-rbqm-kris-evaluate-batch) | Evaluate Kri Batch | ✍️ Audited Write |
| `POST` | [/api/v1/quality/rbqm/qtls](#post-api-v1-quality-rbqm-qtls) | Create Qtl | ✍️ Audited Write |
| `GET` | [/api/v1/quality/rbqm/qtls](#get-api-v1-quality-rbqm-qtls) | List Qtls | 👀 Read-Only |
| `GET` | [/api/v1/quality/rbqm/qtls/breaches](#get-api-v1-quality-rbqm-qtls-breaches) | List Qtl Breaches | 👀 Read-Only |
| `POST` | [/api/v1/quality/rbqm/qtls/{id}/evaluate-breach](#post-api-v1-quality-rbqm-qtls-id-evaluate-breach) | Evaluate Qtl Breach | ✍️ Audited Write |
| `POST` | [/api/v1/quality/rbqm/ract/factors](#post-api-v1-quality-rbqm-ract-factors) | Create Ract Risk Factor | ✍️ Audited Write |
| `GET` | [/api/v1/quality/rbqm/ract/factors](#get-api-v1-quality-rbqm-ract-factors) | List Ract Risk Factors | 👀 Read-Only |
| `PUT` | [/api/v1/quality/rbqm/ract/factors/{factor_id}](#put-api-v1-quality-rbqm-ract-factors-factor_id) | Update Ract Risk Factor | ✍️ Audited Write |
| `DELETE` | [/api/v1/quality/rbqm/ract/factors/{factor_id}](#delete-api-v1-quality-rbqm-ract-factors-factor_id) | Delete Ract Risk Factor | ✍️ Audited Write |
| `POST` | [/api/v1/quality/rbqm/ract/seed-default](#post-api-v1-quality-rbqm-ract-seed-default) | Seed Default Transcelerate Ract | ✍️ Audited Write |
| `GET` | [/api/v1/quality/rbqm/ract/summary](#get-api-v1-quality-rbqm-ract-summary) | Get Ract Matrix Summary | 👀 Read-Only |
| `GET` | [/api/v1/quality/rbqm/site-risk-profiles](#get-api-v1-quality-rbqm-site-risk-profiles) | Get Site Risk Profiles | 👀 Read-Only |
| `POST` | [/api/v1/quality/rbqm/site-risk-profiles/compute](#post-api-v1-quality-rbqm-site-risk-profiles-compute) | Compute Site Risk Profiles | ✍️ Audited Write |
| `POST` | [/api/v1/quality/sentinels/data-integrity/comprehensive-scan](#post-api-v1-quality-sentinels-data-integrity-comprehensive-scan) | Execute Comprehensive Data Integrity Scan | ✍️ Audited Write |
| `POST` | [/api/v1/quality/sentinels/duplicate-patients/scan](#post-api-v1-quality-sentinels-duplicate-patients-scan) | Execute Duplicate Patient Scan | ✍️ Audited Write |
| `POST` | [/api/v1/quality/sentinels/findings/{finding_id}/dispatch-investigation](#post-api-v1-quality-sentinels-findings-finding_id-dispatch-investigation) | Dispatch Manual Investigation | ✍️ Audited Write |
| `POST` | [/api/v1/quality/sentinels/image-phash/scan](#post-api-v1-quality-sentinels-image-phash-scan) | Execute Image Phash Scan | ✍️ Audited Write |
| `GET` | [/api/v1/quality/sentinels/runs](#get-api-v1-quality-sentinels-runs) | List Data Integrity Runs | 👀 Read-Only |
| `GET` | [/api/v1/quality/sentinels/runs/{run_id}](#get-api-v1-quality-sentinels-runs-run_id) | Get Data Integrity Run | 👀 Read-Only |
| `GET` | [/api/v1/quality/sentinels/runs/{run_id}/findings](#get-api-v1-quality-sentinels-runs-run_id-findings) | Get Data Integrity Run Findings | 👀 Read-Only |
| `POST` | [/api/v1/quality/sentinels/simulate](#post-api-v1-quality-sentinels-simulate) | Simulate Sentinel Dataset | ✍️ Audited Write |
| `POST` | [/api/v1/quality/sentinels/temporal-anomalies/scan](#post-api-v1-quality-sentinels-temporal-anomalies-scan) | Execute Temporal Anomaly Scan | ✍️ Audited Write |
| `POST` | [/api/v1/quality/serious-breaches](#post-api-v1-quality-serious-breaches) | Report Serious Breach | ✍️ Audited Write |
| `GET` | [/api/v1/quality/serious-breaches](#get-api-v1-quality-serious-breaches) | List Serious Breaches | 👀 Read-Only |
| `GET` | [/api/v1/quality/serious-breaches/{id}](#get-api-v1-quality-serious-breaches-id) | Get Serious Breach | 👀 Read-Only |
| `GET` | [/api/v1/quality/serious-breaches/{id}/clock](#get-api-v1-quality-serious-breaches-id-clock) | Get Regulatory Clock Status | 👀 Read-Only |
| `POST` | [/api/v1/quality/serious-breaches/{id}/confirm](#post-api-v1-quality-serious-breaches-id-confirm) | Confirm Serious Breach | ✍️ Audited Write |
| `PUT` | [/api/v1/quality/serious-breaches/{id}/status](#put-api-v1-quality-serious-breaches-id-status) | Update Serious Breach Status | ✍️ Audited Write |
| `POST` | [/api/v1/quality/surveillance/run](#post-api-v1-quality-surveillance-run) | Execute Quality Surveillance Run | ✍️ Audited Write |
| `GET` | [/health](#get-health) | Health Check | 👀 Read-Only |

---

## Granular Endpoint Specifications

### `GET` `/api/v1/quality/audit-logs`

**Summary:** List Audit Logs  
**Operation ID:** `list_audit_logs_api_v1_quality_audit_logs_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `user_id` | `query` | `string` | `null` | No | Filter logs by actor/user ID |
| `action` | `query` | `string` | `null` | No | Filter logs by action type |
| `entity_type` | `query` | `string` | `null` | No | Filter logs by entity type |
| `start_date` | `query` | `string` | `string` | `null` | No | Filter logs from start date |
| `end_date` | `query` | `string` | `string` | `null` | No | Filter logs up to end date |
| `record_id` | `query` | `string` | `null` | No | Filter logs by record ID |
| `limit` | `query` | `integer` | No | Max logs to return |
| `offset` | `query` | `integer` | No | Pagination offset |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<AuditLogResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/audit-logs', {
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
        '/api/v1/quality/api/v1/quality/audit-logs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/audit-logs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/audits`

**Summary:** Create Audit  
**Operation ID:** `create_audit_api_v1_quality_audits_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `AuditCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `audit_number` | `string` | ✔ Yes | Unique audit engagement identifier (e.g. AUD-2026-001) |
| `study_id` | `string` | ✔ Yes | Clinical study ID |
| `site_id` | `string` | `null` | No | Optional clinical site ID |
| `vendor_name` | `string` | `null` | No | Optional CRO/Vendor name |
| `audit_type` | `string` | No | Audit type: SITE_AUDIT, VENDOR_QUALIFICATION, PROCESS_AUDIT, TMF_AUDIT |
| `lead_auditor` | `string` | ✔ Yes | Lead auditor name / ID |
| `planned_start_date` | `string` | ✔ Yes | Scheduled audit start |
| `planned_end_date` | `string` | ✔ Yes | Scheduled audit end |
| `scope_summary` | `string` | ✔ Yes | Scope and objectives of the audit |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `AuditResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/audits', {
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
        '/api/v1/quality/api/v1/quality/audits',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/audits' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/audits`

**Summary:** List Audits  
**Operation ID:** `list_audits_api_v1_quality_audits_get`  

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
| `site_id` | `query` | `string` | `null` | No | Filter by site ID |
| `audit_type` | `query` | `string` | `null` | No | Filter by audit type |
| `status` | `query` | `string` | `null` | No | Filter by audit status |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<AuditResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/audits', {
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
        '/api/v1/quality/api/v1/quality/audits',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/audits' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/audits/findings/{finding_id}/promote-capa`

**Summary:** Promote Finding To Capa  
**Operation ID:** `promote_finding_to_capa_api_v1_quality_audits_findings__finding_id__promote_capa_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `finding_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `PromoteFindingToCAPARequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `action_plan` | `string` | ✔ Yes | Corrective action plan to remediate the finding |
| `preventive_measures` | `string` | `null` | No | Preventive measures to prevent recurrence |
| `target_completion_date` | `string` | `null` | No | Target completion date |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `CAPAResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/audits/findings/{finding_id}/promote-capa', {
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
        '/api/v1/quality/api/v1/quality/audits/findings/{finding_id}/promote-capa',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/audits/findings/{finding_id}/promote-capa' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/audits/inspection-dossier/{study_id}`

**Summary:** Get Inspection Readiness Dossier  
**Operation ID:** `get_inspection_readiness_dossier_api_v1_quality_audits_inspection_dossier__study_id__get`  

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

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/audits/inspection-dossier/{study_id}', {
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
        '/api/v1/quality/api/v1/quality/audits/inspection-dossier/{study_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/audits/inspection-dossier/{study_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/audits/{id}`

**Summary:** Get Audit  
**Operation ID:** `get_audit_api_v1_quality_audits__id__get`  

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
| `200` | Successful Response | `AuditResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/audits/{id}', {
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
        '/api/v1/quality/api/v1/quality/audits/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/audits/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/audits/{id}/findings`

**Summary:** Create Audit Finding  
**Operation ID:** `create_audit_finding_api_v1_quality_audits__id__findings_post`  

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

**Schema Type:** `AuditFindingCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `finding_number` | `string` | ✔ Yes | Finding sequence (e.g. F-01) |
| `severity` | `string` | No | Severity: CRITICAL, MAJOR, MINOR, OBSERVATION |
| `category` | `string` | ✔ Yes | Finding category (Informed Consent, Investigational Product, Data Integrity) |
| `condition` | `string` | ✔ Yes | Observed non-compliance or defect |
| `criteria` | `string` | ✔ Yes | Applicable GCP or protocol requirement |
| `cause` | `string` | ✔ Yes | Why the condition occurred |
| `effect` | `string` | ✔ Yes | Impact on subject safety, rights, or trial reliability |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `AuditFindingResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/audits/{id}/findings', {
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
        '/api/v1/quality/api/v1/quality/audits/{id}/findings',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/audits/{id}/findings' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/audits/{id}/findings`

**Summary:** List Audit Findings  
**Operation ID:** `list_audit_findings_api_v1_quality_audits__id__findings_get`  

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
| `200` | Successful Response | `Array<AuditFindingResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/audits/{id}/findings', {
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
        '/api/v1/quality/api/v1/quality/audits/{id}/findings',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/audits/{id}/findings' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/quality/audits/{id}/status`

**Summary:** Update Audit Status  
**Operation ID:** `update_audit_status_api_v1_quality_audits__id__status_put`  

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

**Schema Type:** `AuditStatusUpdate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `status` | `AuditStatus` | ✔ Yes | Updated audit status |
| `actual_start_date` | `string` | `null` | No | Actual start timestamp |
| `actual_end_date` | `string` | `null` | No | Actual completion timestamp |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `AuditResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/audits/{id}/status', {
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
        '/api/v1/quality/api/v1/quality/audits/{id}/status',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/quality/api/v1/quality/audits/{id}/status' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/capas`

**Summary:** Create Capa  
**Operation ID:** `create_capa_api_v1_quality_capas_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CAPACreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `deviation_id` | `string` | ✔ Yes | Reference to the parent deviation ID |
| `rca_id` | `string` | `null` | No | Optional reference to the Root Cause Analysis ID |
| `capa_type` | `string` | No | Type of CAPA: CORRECTIVE, PREVENTIVE, BOTH |
| `action_plan` | `string` | ✔ Yes | The planned corrective/preventive action steps |
| `preventive_measures` | `string` | `null` | No | Specific measures to prevent recurrence |
| `risk_level` | `string` | No | Risk level: LOW, MEDIUM, HIGH, CRITICAL |
| `lead_investigator_id` | `string` | `null` | No | Lead investigator user ID |
| `target_completion_date` | `string` | `null` | No | Optional expected completion timestamp |
| `effectiveness_interval_days` | `integer` | No | Scheduled interval in days before post-closure effectiveness check |
| `audit_finding_id` | `string` | `null` | No | Optional linked audit finding ID |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `CAPAResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/capas', {
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
        '/api/v1/quality/api/v1/quality/capas',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/capas' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/capas`

**Summary:** List Capas  
**Operation ID:** `list_capas_api_v1_quality_capas_get`  

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
| `status` | `query` | `CAPAStatus` | `null` | No | Filter by status |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<CAPAResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/capas', {
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
        '/api/v1/quality/api/v1/quality/capas',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/capas' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/quality/capas/action-items/{item_id}`

**Summary:** Update Action Item Status  
**Operation ID:** `update_action_item_status_api_v1_quality_capas_action_items__item_id__put`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `item_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `CAPAActionItemUpdate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `status` | `ActionItemStatus` | ✔ Yes | Target status: OPEN, IN_PROGRESS, COMPLETED, CANCELLED |
| `evidence_url` | `string` | `null` | No | URL or reference to completion verification evidence |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `CAPAActionItemResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/capas/action-items/{item_id}', {
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
        '/api/v1/quality/api/v1/quality/capas/action-items/{item_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/quality/api/v1/quality/capas/action-items/{item_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/capas/{id}`

**Summary:** View Capa  
**Operation ID:** `view_capa_api_v1_quality_capas__id__get`  

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
| `200` | Successful Response | `CAPAResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/capas/{id}', {
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
        '/api/v1/quality/api/v1/quality/capas/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/capas/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/quality/capas/{id}`

**Summary:** Update Capa  
**Operation ID:** `update_capa_api_v1_quality_capas__id__put`  

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

**Schema Type:** `CAPAUpdate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `action_plan` | `string` | `null` | No | The planned corrective/preventive action steps |
| `preventive_measures` | `string` | `null` | No | Specific measures to prevent recurrence |
| `risk_level` | `string` | `null` | No | Risk level |
| `target_completion_date` | `string` | `null` | No | Optional expected completion timestamp |
| `effectiveness_interval_days` | `integer` | `null` | No | Effectiveness interval in days |
| `version_index` | `integer` | `null` | No | Current expected version index for optimistic locking |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `CAPAResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/capas/{id}', {
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
        '/api/v1/quality/api/v1/quality/capas/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/quality/api/v1/quality/capas/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/capas/{id}/action-items`

**Summary:** Create Action Item  
**Operation ID:** `create_action_item_api_v1_quality_capas__id__action_items_post`  

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

**Schema Type:** `CAPAActionItemCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `title` | `string` | ✔ Yes | Action item title |
| `description` | `string` | ✔ Yes | Detailed instructions for the action |
| `action_type` | `string` | No | Action type: CORRECTIVE or PREVENTIVE |
| `assigned_to` | `string` | ✔ Yes | User ID assigned to complete this action |
| `due_date` | `string` | `null` | No | Target completion timestamp |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `CAPAActionItemResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/capas/{id}/action-items', {
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
        '/api/v1/quality/api/v1/quality/capas/{id}/action-items',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/capas/{id}/action-items' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/capas/{id}/effectiveness`

**Summary:** Record Effectiveness Check  
**Operation ID:** `record_effectiveness_check_api_v1_quality_capas__id__effectiveness_post`  

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

**Schema Type:** `CAPAEffectivenessCheckCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `planned_date` | `string` | ✔ Yes | Scheduled verification date |
| `metric_evaluated` | `string` | ✔ Yes | Metric or condition being verified |
| `baseline_value` | `string` | ✔ Yes | Baseline measurement before CAPA implementation |
| `target_value` | `string` | ✔ Yes | Target success threshold |
| `actual_value` | `string` | ✔ Yes | Observed measurement during effectiveness check |
| `outcome` | `EffectivenessOutcome` | ✔ Yes | Evaluation outcome: EFFECTIVE, INEFFECTIVE, PARTIALLY_EFFECTIVE |
| `comments` | `string` | `null` | No | Evaluator rationale and notes |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `CAPAEffectivenessCheckResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/capas/{id}/effectiveness', {
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
        '/api/v1/quality/api/v1/quality/capas/{id}/effectiveness',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/capas/{id}/effectiveness' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/capas/{id}/transition`

**Summary:** Transition Capa  
**Operation ID:** `transition_capa_api_v1_quality_capas__id__transition_post`  

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

**Schema Type:** `CAPATransitionRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `to_status` | `CAPAStatus` | ✔ Yes | Target CAPA Status to transition to |
| `version_index` | `integer` | `null` | No | Expected version index for optimistic locking |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `CAPAResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/capas/{id}/transition', {
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
        '/api/v1/quality/api/v1/quality/capas/{id}/transition',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/capas/{id}/transition' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/deviations`

**Summary:** Create Deviation  
**Operation ID:** `create_deviation_api_v1_quality_deviations_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `DeviationCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Unique identifier of the clinical study |
| `site_id` | `string` | `null` | No | Optional clinical site ID |
| `title` | `string` | ✔ Yes | A short summary of the deviation |
| `description` | `string` | ✔ Yes | Detailed explanation of the deviation |
| `severity` | `DeviationSeverity` | ✔ Yes | Severity level: MINOR, MAJOR, CRITICAL, SYSTEMATIC_TREND |
| `type` | `DeviationType` | ✔ Yes | Type of deviation, e.g., INFORMED_CONSENT |
| `category` | `string` | `null` | No | Optional granular category |
| `is_protocol_violation` | `boolean` | No | Whether this constitutes a protocol violation |
| `impact_safety` | `boolean` | No | Potential or actual impact on subject safety |
| `impact_data` | `boolean` | No | Potential or actual impact on study data reliability |
| `impact_compliance` | `boolean` | No | Potential or actual impact on GCP regulatory compliance |
| `source_system` | `string` | No | Source of deviation (MANUAL, EDC, CTMS, eTMF) |
| `source_reference_id` | `string` | `null` | No | External reference ID from originating system |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `DeviationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/deviations', {
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
        '/api/v1/quality/api/v1/quality/deviations',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/deviations' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/deviations`

**Summary:** List Deviations  
**Operation ID:** `list_deviations_api_v1_quality_deviations_get`  

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
| `site_id` | `query` | `string` | `null` | No | Filter by site ID |
| `status` | `query` | `DeviationStatus` | `null` | No | Filter by status |
| `severity` | `query` | `DeviationSeverity` | `null` | No | Filter by severity |
| `type` | `query` | `DeviationType` | `null` | No | Filter by deviation type |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<DeviationResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/deviations', {
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
        '/api/v1/quality/api/v1/quality/deviations',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/deviations' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/deviations/{id}`

**Summary:** View Deviation  
**Operation ID:** `view_deviation_api_v1_quality_deviations__id__get`  

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
| `200` | Successful Response | `DeviationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/deviations/{id}', {
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
        '/api/v1/quality/api/v1/quality/deviations/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/deviations/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/quality/deviations/{id}/rca`

**Summary:** Create Or Update Rca  
**Operation ID:** `create_or_update_rca_api_v1_quality_deviations__id__rca_put`  

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

**Schema Type:** `RCACreateOrUpdate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `methodology` | `string` | No | RCA methodology used (FIVE_WHYS, ISHIKAWA_FISHBONE, HUMAN_FACTORS) |
| `investigation_details` | `string` | ✔ Yes | Full details of the investigation |
| `root_cause_summary` | `string` | ✔ Yes | Summary of the determined root cause |
| `five_whys_chain` | `object` | `Array<object>` | `null` | No | Structured 5-Whys causal hierarchy (why_1 through why_5) |
| `fishbone_categories` | `object` | `null` | No | Ishikawa 6M categories (Man, Machine, Material, Method, Measurement, Milieu) |
| `contributing_factors` | `Array<object>` | `null` | No | Contributing factor tags and classifications |
| `version_index` | `integer` | `null` | No | Current expected version index for optimistic locking |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `RCAResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/deviations/{id}/rca', {
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
        '/api/v1/quality/api/v1/quality/deviations/{id}/rca',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/quality/api/v1/quality/deviations/{id}/rca' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/deviations/{id}/rca`

**Summary:** Create Or Update Rca  
**Operation ID:** `create_or_update_rca_api_v1_quality_deviations__id__rca_post`  

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

**Schema Type:** `RCACreateOrUpdate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `methodology` | `string` | No | RCA methodology used (FIVE_WHYS, ISHIKAWA_FISHBONE, HUMAN_FACTORS) |
| `investigation_details` | `string` | ✔ Yes | Full details of the investigation |
| `root_cause_summary` | `string` | ✔ Yes | Summary of the determined root cause |
| `five_whys_chain` | `object` | `Array<object>` | `null` | No | Structured 5-Whys causal hierarchy (why_1 through why_5) |
| `fishbone_categories` | `object` | `null` | No | Ishikawa 6M categories (Man, Machine, Material, Method, Measurement, Milieu) |
| `contributing_factors` | `Array<object>` | `null` | No | Contributing factor tags and classifications |
| `version_index` | `integer` | `null` | No | Current expected version index for optimistic locking |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `RCAResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/deviations/{id}/rca', {
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
        '/api/v1/quality/api/v1/quality/deviations/{id}/rca',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/deviations/{id}/rca' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/forensics/analyze`

**Summary:** Execute Forensics Analysis  
**Operation ID:** `execute_forensics_analysis_api_v1_quality_forensics_analyze_post`  

**Clinical & GxP Rationale:**  
Executes automated multi-site statistical forensics surveillance.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `StatisticalForensicsAnalyzeRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Clinical study ID |
| `target_variables` | `Array<string>` | No | Continuous clinical variables for multivariate surveillance |
| `site_observations` | `object` | `null` | No | Mapping of site_id to list of observation dictionaries |
| `site_data` | `Array<SiteObservationPayload>` | `null` | No | Alternative list format of site observation payloads |
| `run_name` | `string` | `null` | No | Custom name for the surveillance run |
| `auto_trigger_kri` | `boolean` | No | Whether to automatically dispatch KRI breach alerts for flagged sites |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `StatisticalForensicsRunResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/forensics/analyze', {
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
        '/api/v1/quality/api/v1/quality/forensics/analyze',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/forensics/analyze' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/forensics/runs`

**Summary:** List Forensics Runs  
**Operation ID:** `list_forensics_runs_api_v1_quality_forensics_runs_get`  

**Clinical & GxP Rationale:**  
Lists historical statistical forensics surveillance runs.

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
| `200` | Successful Response | `Array<StatisticalForensicsRunResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/forensics/runs', {
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
        '/api/v1/quality/api/v1/quality/forensics/runs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/forensics/runs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/forensics/runs/{run_id}`

**Summary:** Get Forensics Run  
**Operation ID:** `get_forensics_run_api_v1_quality_forensics_runs__run_id__get`  

**Clinical & GxP Rationale:**  
Retrieves detailed results for a specific forensics surveillance run.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `run_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `StatisticalForensicsRunResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/forensics/runs/{run_id}', {
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
        '/api/v1/quality/api/v1/quality/forensics/runs/{run_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/forensics/runs/{run_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/forensics/runs/{run_id}/trigger-kri-alerts`

**Summary:** Trigger Run Kri Alerts  
**Operation ID:** `trigger_run_kri_alerts_api_v1_quality_forensics_runs__run_id__trigger_kri_alerts_post`  

**Clinical & GxP Rationale:**  
Dispatches KRI breach events for all flagged anomalous sites in a forensics run.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `run_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<KRIMetricEvaluationResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/forensics/runs/{run_id}/trigger-kri-alerts', {
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
        '/api/v1/quality/api/v1/quality/forensics/runs/{run_id}/trigger-kri-alerts',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/forensics/runs/{run_id}/trigger-kri-alerts' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/forensics/simulate`

**Summary:** Simulate Forensics Dataset  
**Operation ID:** `simulate_forensics_dataset_api_v1_quality_forensics_simulate_post`  

**Clinical & GxP Rationale:**  
Generates synthetic multi-site clinical datasets for testing and calibration.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ForensicsSimulationRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | No | Study ID for simulation |
| `num_normal_sites` | `integer` | No | Count of compliant benchmark sites |
| `include_benford_anomaly` | `boolean` | No | Include site violating Benford's Law |
| `include_rounding_bias` | `boolean` | No | Include site with excessive terminal digit rounding |
| `include_variance_shrinkage` | `boolean` | No | Include site with fabricated variance shrinkage |
| `include_multivariate_outlier` | `boolean` | No | Include site with shifted multivariate correlation centroid |
| `samples_per_site` | `integer` | No | Observations per site |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ForensicsSimulationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/forensics/simulate', {
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
        '/api/v1/quality/api/v1/quality/forensics/simulate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/forensics/simulate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/forensics/sites/{site_id}`

**Summary:** Get Site Forensics History  
**Operation ID:** `get_site_forensics_history_api_v1_quality_forensics_sites__site_id__get`  

**Clinical & GxP Rationale:**  
Retrieves chronological forensics surveillance results for an individual site.

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
| `200` | Successful Response | `Array<SiteForensicsResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/forensics/sites/{site_id}', {
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
        '/api/v1/quality/api/v1/quality/forensics/sites/{site_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/forensics/sites/{site_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/ingest/event`

**Summary:** Ingest Quality Event  
**Operation ID:** `ingest_quality_event_api_v1_quality_ingest_event_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `DeviationIngestRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Unique identifier of the clinical study |
| `site_id` | `string` | `null` | No | Optional clinical site ID |
| `title` | `string` | ✔ Yes | Summary of the quality event |
| `description` | `string` | ✔ Yes | Detailed event log |
| `severity` | `DeviationSeverity` | No | Severity grade |
| `type` | `DeviationType` | No | Deviation classification |
| `category` | `string` | `null` | No | Sub-category |
| `is_protocol_violation` | `boolean` | No | Violation flag |
| `impact_safety` | `boolean` | No | Safety impact |
| `impact_data` | `boolean` | No | Data impact |
| `impact_compliance` | `boolean` | No | Compliance impact |
| `source_system` | `string` | ✔ Yes | Source system: EDC, CTMS, eTMF |
| `source_reference_id` | `string` | `null` | No | Idempotency key / foreign entity reference |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `DeviationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/ingest/event', {
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
        '/api/v1/quality/api/v1/quality/ingest/event',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/ingest/event' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/rbqm/csm/dossiers`

**Summary:** List Csm Dossiers  
**Operation ID:** `list_csm_dossiers_api_v1_quality_rbqm_csm_dossiers_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | Study ID |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<CSMDossierResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/csm/dossiers', {
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
        '/api/v1/quality/api/v1/quality/rbqm/csm/dossiers',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/csm/dossiers' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/rbqm/csm/dossiers/compile`

**Summary:** Compile Csm Dossier  
**Operation ID:** `compile_csm_dossier_api_v1_quality_rbqm_csm_dossiers_compile_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CompileCSMDossierRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Clinical study ID |
| `dossier_name` | `string` | `null` | No | Custom name for the compiled inspection dossier |
| `signer_name` | `string` | ✔ Yes | Full legal name of the reviewing signer |
| `signer_role` | `string` | ✔ Yes | Role of the signer (e.g. Lead Central Monitor / QA Lead) |
| `signature_meaning` | `string` | No | 21 CFR Part 11 controlled signing statement |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `CSMDossierResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/csm/dossiers/compile', {
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
        '/api/v1/quality/api/v1/quality/rbqm/csm/dossiers/compile',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/csm/dossiers/compile' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/rbqm/csm/dossiers/{dossier_id}`

**Summary:** Get Csm Dossier  
**Operation ID:** `get_csm_dossier_api_v1_quality_rbqm_csm_dossiers__dossier_id__get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `dossier_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `CSMDossierResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/csm/dossiers/{dossier_id}', {
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
        '/api/v1/quality/api/v1/quality/rbqm/csm/dossiers/{dossier_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/csm/dossiers/{dossier_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/rbqm/ctq`

**Summary:** Create Ctq Factor  
**Operation ID:** `create_ctq_factor_api_v1_quality_rbqm_ctq_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CtQFactorCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Study ID |
| `category` | `string` | No | Risk category |
| `critical_aspect` | `string` | ✔ Yes | Critical process or data parameter |
| `risk_description` | `string` | ✔ Yes | Potential threat to quality |
| `impact_area` | `string` | ✔ Yes | Primary endpoint, patient safety, etc. |
| `mitigation_strategy` | `string` | ✔ Yes | Operational control measures |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `CtQFactorResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/ctq', {
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
        '/api/v1/quality/api/v1/quality/rbqm/ctq',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/ctq' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/rbqm/ctq`

**Summary:** List Ctq Factors  
**Operation ID:** `list_ctq_factors_api_v1_quality_rbqm_ctq_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | Study ID |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<CtQFactorResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/ctq', {
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
        '/api/v1/quality/api/v1/quality/rbqm/ctq',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/ctq' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/rbqm/evaluations`

**Summary:** List Kri Evaluations  
**Operation ID:** `list_kri_evaluations_api_v1_quality_rbqm_evaluations_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | Study ID |
| `site_id` | `query` | `string` | `null` | No | Optional Site ID |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<KRIMetricEvaluationResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/evaluations', {
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
        '/api/v1/quality/api/v1/quality/rbqm/evaluations',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/evaluations' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/rbqm/kris`

**Summary:** List Kri Definitions  
**Operation ID:** `list_kri_definitions_api_v1_quality_rbqm_kris_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<KRIDefinitionResponse>` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/kris', {
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
        '/api/v1/quality/api/v1/quality/rbqm/kris',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/kris' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/rbqm/kris`

**Summary:** Create Kri Definition  
**Operation ID:** `create_kri_definition_api_v1_quality_rbqm_kris_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `KRIDefinitionCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `code` | `string` | ✔ Yes | Unique KRI identifier code (e.g. KRI_AE_RATE) |
| `name` | `string` | ✔ Yes | Human-readable KRI title |
| `category` | `string` | No | Risk category |
| `description` | `string` | ✔ Yes | Metric description |
| `calculation_formula` | `string` | ✔ Yes | Formula definition |
| `green_threshold` | `number` | No | Green / Low risk boundary |
| `amber_threshold` | `number` | No | Amber / Moderate risk boundary |
| `red_threshold` | `number` | No | Red / High risk boundary |
| `weight` | `number` | No | Weight factor for Site Risk Index |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `KRIDefinitionResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/kris', {
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
        '/api/v1/quality/api/v1/quality/rbqm/kris',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/kris' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/rbqm/kris/evaluate-batch`

**Summary:** Evaluate Kri Batch  
**Operation ID:** `evaluate_kri_batch_api_v1_quality_rbqm_kris_evaluate_batch_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `KRIBatchEvaluationRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Study ID |
| `kri_code` | `string` | ✔ Yes | KRI Code to evaluate across sites |
| `site_raw_values` | `object` | ✔ Yes | Dictionary mapping site_id -> raw metric value |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<KRIMetricEvaluationResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/kris/evaluate-batch', {
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
        '/api/v1/quality/api/v1/quality/rbqm/kris/evaluate-batch',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/kris/evaluate-batch' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/rbqm/qtls`

**Summary:** Create Qtl  
**Operation ID:** `create_qtl_api_v1_quality_rbqm_qtls_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `QTLCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Study ID |
| `parameter_name` | `string` | ✔ Yes | Parameter name (e.g. Lost to Follow-up Rate) |
| `target_value` | `number` | ✔ Yes | Expected target value |
| `tolerance_limit` | `number` | ✔ Yes | Maximum allowable threshold before breach |
| `unit` | `string` | No | Measurement unit (%, count, days) |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `QTLResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/qtls', {
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
        '/api/v1/quality/api/v1/quality/rbqm/qtls',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/qtls' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/rbqm/qtls`

**Summary:** List Qtls  
**Operation ID:** `list_qtls_api_v1_quality_rbqm_qtls_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | Study ID |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<QTLResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/qtls', {
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
        '/api/v1/quality/api/v1/quality/rbqm/qtls',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/qtls' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/rbqm/qtls/breaches`

**Summary:** List Qtl Breaches  
**Operation ID:** `list_qtl_breaches_api_v1_quality_rbqm_qtls_breaches_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | Study ID |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<QTLBreachEventResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/qtls/breaches', {
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
        '/api/v1/quality/api/v1/quality/rbqm/qtls/breaches',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/qtls/breaches' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/rbqm/qtls/{id}/evaluate-breach`

**Summary:** Evaluate Qtl Breach  
**Operation ID:** `evaluate_qtl_breach_api_v1_quality_rbqm_qtls__id__evaluate_breach_post`  

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

**Schema Type:** `QTLEvaluateBreachRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `observed_value` | `number` | ✔ Yes | Current observed metric value |
| `root_cause` | `string` | ✔ Yes | Investigation of why the QTL was breached |
| `corrective_action_summary` | `string` | ✔ Yes | Mitigation and corrective actions planned |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/qtls/{id}/evaluate-breach', {
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
        '/api/v1/quality/api/v1/quality/rbqm/qtls/{id}/evaluate-breach',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/qtls/{id}/evaluate-breach' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/rbqm/ract/factors`

**Summary:** Create Ract Risk Factor  
**Operation ID:** `create_ract_risk_factor_api_v1_quality_rbqm_ract_factors_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `RACTRiskFactorCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Study ID |
| `category` | `RACTCategory` | ✔ Yes | Protocol risk category: SAFETY, CONSENT, STUDY_CONDUCT, DATA_INTEGRITY, IP_MANAGEMENT, TECHNOLOGY |
| `risk_topic` | `string` | ✔ Yes | Concise risk factor title / topic (e.g. Complex Dosing) |
| `risk_description` | `string` | ✔ Yes | Detailed description of the potential risk |
| `probability_score` | `integer` | ✔ Yes | Likelihood score: 1=Low, 2=Medium, 3=High |
| `impact_score` | `integer` | ✔ Yes | Impact severity score: 1=Low, 2=Medium, 3=High |
| `detectability_score` | `integer` | ✔ Yes | Detectability score: 1=High (Easy), 2=Medium, 3=Low (Hard) |
| `mitigation_strategy` | `string` | ✔ Yes | Operational risk mitigation and control plan |
| `linked_kri_code` | `string` | `null` | No | Optional code of linked automated Key Risk Indicator |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `RACTRiskFactorResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/ract/factors', {
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
        '/api/v1/quality/api/v1/quality/rbqm/ract/factors',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/ract/factors' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/rbqm/ract/factors`

**Summary:** List Ract Risk Factors  
**Operation ID:** `list_ract_risk_factors_api_v1_quality_rbqm_ract_factors_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | Study ID |
| `category` | `query` | `string` | `null` | No | Optional RACT Category filter |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<RACTRiskFactorResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/ract/factors', {
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
        '/api/v1/quality/api/v1/quality/rbqm/ract/factors',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/ract/factors' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/quality/rbqm/ract/factors/{factor_id}`

**Summary:** Update Ract Risk Factor  
**Operation ID:** `update_ract_risk_factor_api_v1_quality_rbqm_ract_factors__factor_id__put`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `factor_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `RACTRiskFactorUpdate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `risk_topic` | `string` | `null` | No | Updated risk topic |
| `risk_description` | `string` | `null` | No | Updated risk description |
| `probability_score` | `integer` | `null` | No | Updated probability score |
| `impact_score` | `integer` | `null` | No | Updated impact score |
| `detectability_score` | `integer` | `null` | No | Updated detectability score |
| `mitigation_strategy` | `string` | `null` | No | Updated mitigation strategy |
| `linked_kri_code` | `string` | `null` | No | Updated linked KRI code |
| `is_active` | `boolean` | `null` | No | Active status flag |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `RACTRiskFactorResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/ract/factors/{factor_id}', {
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
        '/api/v1/quality/api/v1/quality/rbqm/ract/factors/{factor_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/ract/factors/{factor_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `DELETE` `/api/v1/quality/rbqm/ract/factors/{factor_id}`

**Summary:** Delete Ract Risk Factor  
**Operation ID:** `delete_ract_risk_factor_api_v1_quality_rbqm_ract_factors__factor_id__delete`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `factor_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/ract/factors/{factor_id}', {
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
        '/api/v1/quality/api/v1/quality/rbqm/ract/factors/{factor_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X DELETE 'http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/ract/factors/{factor_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/rbqm/ract/seed-default`

**Summary:** Seed Default Transcelerate Ract  
**Operation ID:** `seed_default_transcelerate_ract_api_v1_quality_rbqm_ract_seed_default_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | Study ID |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `Array<RACTRiskFactorResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/ract/seed-default', {
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
        '/api/v1/quality/api/v1/quality/rbqm/ract/seed-default',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/ract/seed-default' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/rbqm/ract/summary`

**Summary:** Get Ract Matrix Summary  
**Operation ID:** `get_ract_matrix_summary_api_v1_quality_rbqm_ract_summary_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | Study ID |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `RACTMatrixSummaryResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/ract/summary', {
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
        '/api/v1/quality/api/v1/quality/rbqm/ract/summary',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/ract/summary' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/rbqm/site-risk-profiles`

**Summary:** Get Site Risk Profiles  
**Operation ID:** `get_site_risk_profiles_api_v1_quality_rbqm_site_risk_profiles_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | Study ID |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<SiteRiskProfileResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/site-risk-profiles', {
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
        '/api/v1/quality/api/v1/quality/rbqm/site-risk-profiles',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/site-risk-profiles' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/rbqm/site-risk-profiles/compute`

**Summary:** Compute Site Risk Profiles  
**Operation ID:** `compute_site_risk_profiles_api_v1_quality_rbqm_site_risk_profiles_compute_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | Study ID |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<SiteRiskProfileResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/site-risk-profiles/compute', {
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
        '/api/v1/quality/api/v1/quality/rbqm/site-risk-profiles/compute',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/rbqm/site-risk-profiles/compute' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/sentinels/data-integrity/comprehensive-scan`

**Summary:** Execute Comprehensive Data Integrity Scan  
**Operation ID:** `execute_comprehensive_data_integrity_scan_api_v1_quality_sentinels_data_integrity_comprehensive_scan_post`  

**Clinical & GxP Rationale:**  
Executes a unified comprehensive data integrity scan across duplicate records, timing, and images.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `DataIntegrityComprehensiveScanRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Study ID |
| `subjects` | `Array<SubjectClinicalVectorPayload>` | `null` | No | Baseline clinical subject vectors |
| `audit_events` | `Array<AuditEventPayload>` | `null` | No | Data entry audit events |
| `image_attachments` | `Array<ImageAttachmentPayload>` | `null` | No | Medical image attachments |
| `auto_dispatch_investigation` | `boolean` | No | Automatically dispatch Part 11 investigation deviation for critical findings |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `DataIntegritySentinelRunResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/sentinels/data-integrity/comprehensive-scan', {
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
        '/api/v1/quality/api/v1/quality/sentinels/data-integrity/comprehensive-scan',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/sentinels/data-integrity/comprehensive-scan' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/sentinels/duplicate-patients/scan`

**Summary:** Execute Duplicate Patient Scan  
**Operation ID:** `execute_duplicate_patient_scan_api_v1_quality_sentinels_duplicate_patients_scan_post`  

**Clinical & GxP Rationale:**  
Executes cross-site similarity matching across baseline clinical feature vectors.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `DuplicatePatientScanRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Study ID |
| `subjects` | `Array<SubjectClinicalVectorPayload>` | `null` | No | List of baseline subject clinical vector payloads |
| `raw_subjects` | `Array<object>` | `null` | No | Optional raw dictionary format for subject vectors |
| `similarity_threshold` | `number` | No | Minimum composite similarity threshold to flag duplicate |
| `auto_dispatch_investigation` | `boolean` | No | Automatically dispatch Part 11 investigation deviation if critical match |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `DataIntegritySentinelRunResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/sentinels/duplicate-patients/scan', {
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
        '/api/v1/quality/api/v1/quality/sentinels/duplicate-patients/scan',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/sentinels/duplicate-patients/scan' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/sentinels/findings/{finding_id}/dispatch-investigation`

**Summary:** Dispatch Manual Investigation  
**Operation ID:** `dispatch_manual_investigation_api_v1_quality_sentinels_findings__finding_id__dispatch_investigation_post`  

**Clinical & GxP Rationale:**  
Dispatches a formal 21 CFR Part 11 investigation deviation for a flagged finding.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `finding_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `DataIntegrityInvestigationDispatchRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `finding_id` | `string` | ✔ Yes | Data integrity finding ID to dispatch investigation for |
| `rationale` | `string` | No | Reason for change / investigation rationale |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DataIntegrityFindingResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/sentinels/findings/{finding_id}/dispatch-investigation', {
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
        '/api/v1/quality/api/v1/quality/sentinels/findings/{finding_id}/dispatch-investigation',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/sentinels/findings/{finding_id}/dispatch-investigation' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/sentinels/image-phash/scan`

**Summary:** Execute Image Phash Scan  
**Operation ID:** `execute_image_phash_scan_api_v1_quality_sentinels_image_phash_scan_post`  

**Clinical & GxP Rationale:**  
Computes DCT-based perceptual image hashes to detect reused and transformed radiological scans.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ImageHashScanRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Study ID |
| `image_attachments` | `Array<ImageAttachmentPayload>` | `null` | No | List of medical image attachments |
| `raw_images` | `Array<object>` | `null` | No | Optional raw dictionary format for image attachments |
| `hamming_threshold` | `integer` | No | Maximum Hamming distance threshold to classify duplicate (<=10) |
| `auto_dispatch_investigation` | `boolean` | No | Automatically dispatch Part 11 investigation deviation if duplicate found |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `DataIntegritySentinelRunResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/sentinels/image-phash/scan', {
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
        '/api/v1/quality/api/v1/quality/sentinels/image-phash/scan',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/sentinels/image-phash/scan' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/sentinels/runs`

**Summary:** List Data Integrity Runs  
**Operation ID:** `list_data_integrity_runs_api_v1_quality_sentinels_runs_get`  

**Clinical & GxP Rationale:**  
Lists historical data integrity sentinel runs.

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
| `200` | Successful Response | `Array<DataIntegritySentinelRunResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/sentinels/runs', {
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
        '/api/v1/quality/api/v1/quality/sentinels/runs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/sentinels/runs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/sentinels/runs/{run_id}`

**Summary:** Get Data Integrity Run  
**Operation ID:** `get_data_integrity_run_api_v1_quality_sentinels_runs__run_id__get`  

**Clinical & GxP Rationale:**  
Retrieves full details and findings for a specific data integrity sentinel run.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `run_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DataIntegritySentinelRunResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/sentinels/runs/{run_id}', {
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
        '/api/v1/quality/api/v1/quality/sentinels/runs/{run_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/sentinels/runs/{run_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/sentinels/runs/{run_id}/findings`

**Summary:** Get Data Integrity Run Findings  
**Operation ID:** `get_data_integrity_run_findings_api_v1_quality_sentinels_runs__run_id__findings_get`  

**Clinical & GxP Rationale:**  
Retrieves all anomaly findings detected within a sentinel run.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `run_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<DataIntegrityFindingResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/sentinels/runs/{run_id}/findings', {
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
        '/api/v1/quality/api/v1/quality/sentinels/runs/{run_id}/findings',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/sentinels/runs/{run_id}/findings' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/sentinels/simulate`

**Summary:** Simulate Sentinel Dataset  
**Operation ID:** `simulate_sentinel_dataset_api_v1_quality_sentinels_simulate_post`  

**Clinical & GxP Rationale:**  
Generates synthetic multi-site clinical datasets with injected fraud anomalies.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `DataIntegritySimulationRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | No | Study ID for simulation |
| `num_subjects_per_site` | `integer` | No | Subjects per benchmark site |
| `include_duplicate_patient` | `boolean` | No | Inject cross-site duplicate subject vector |
| `include_temporal_burst` | `boolean` | No | Inject Sunday off-hours rapid burst cluster |
| `include_identical_intervals` | `boolean` | No | Inject identical procedure elapsed intervals |
| `include_image_duplication` | `boolean` | No | Inject rotated 90 deg duplicate image scan |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DataIntegritySimulationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/sentinels/simulate', {
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
        '/api/v1/quality/api/v1/quality/sentinels/simulate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/sentinels/simulate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/sentinels/temporal-anomalies/scan`

**Summary:** Execute Temporal Anomaly Scan  
**Operation ID:** `execute_temporal_anomaly_scan_api_v1_quality_sentinels_temporal_anomalies_scan_post`  

**Clinical & GxP Rationale:**  
Detects off-hours entry clusters, rapid burst submissions, and identical procedure intervals.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `TemporalAnomalyScanRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Study ID |
| `audit_events` | `Array<AuditEventPayload>` | `null` | No | List of data entry audit events |
| `raw_events` | `Array<object>` | `null` | No | Optional raw dictionary format of audit events |
| `work_hour_start` | `integer` | No | Clinic opening hour (0-23) |
| `work_hour_end` | `integer` | No | Clinic closing hour (0-23) |
| `auto_dispatch_investigation` | `boolean` | No | Automatically dispatch Part 11 investigation deviation if critical anomaly |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `DataIntegritySentinelRunResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/sentinels/temporal-anomalies/scan', {
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
        '/api/v1/quality/api/v1/quality/sentinels/temporal-anomalies/scan',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/sentinels/temporal-anomalies/scan' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/serious-breaches`

**Summary:** Report Serious Breach  
**Operation ID:** `report_serious_breach_api_v1_quality_serious_breaches_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `SeriousBreachReportRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Clinical study ID |
| `site_id` | `string` | `null` | No | Optional clinical site ID |
| `title` | `string` | ✔ Yes | Title of serious breach event |
| `summary` | `string` | ✔ Yes | Comprehensive description of the breach and suspected impact |
| `event_date` | `string` | ✔ Yes | Date breach occurred |
| `discovery_date` | `string` | ✔ Yes | Date breach was discovered |
| `affected_authorities` | `Array<string>` | No | List of regulatory authorities to notify |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `SeriousBreachResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/serious-breaches', {
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
        '/api/v1/quality/api/v1/quality/serious-breaches',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/serious-breaches' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/serious-breaches`

**Summary:** List Serious Breaches  
**Operation ID:** `list_serious_breaches_api_v1_quality_serious_breaches_get`  

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
| `status` | `query` | `string` | `null` | No | Filter by status |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<SeriousBreachResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/serious-breaches', {
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
        '/api/v1/quality/api/v1/quality/serious-breaches',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/serious-breaches' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/serious-breaches/{id}`

**Summary:** Get Serious Breach  
**Operation ID:** `get_serious_breach_api_v1_quality_serious_breaches__id__get`  

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
| `200` | Successful Response | `SeriousBreachResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/serious-breaches/{id}', {
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
        '/api/v1/quality/api/v1/quality/serious-breaches/{id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/serious-breaches/{id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/quality/serious-breaches/{id}/clock`

**Summary:** Get Regulatory Clock Status  
**Operation ID:** `get_regulatory_clock_status_api_v1_quality_serious_breaches__id__clock_get`  

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
| `200` | Successful Response | `RegulatoryClockStatusResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/serious-breaches/{id}/clock', {
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
        '/api/v1/quality/api/v1/quality/serious-breaches/{id}/clock',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/api/v1/quality/serious-breaches/{id}/clock' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/serious-breaches/{id}/confirm`

**Summary:** Confirm Serious Breach  
**Operation ID:** `confirm_serious_breach_api_v1_quality_serious_breaches__id__confirm_post`  

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

**Schema Type:** `SeriousBreachConfirmRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `affected_authorities` | `Array<string>` | ✔ Yes | Confirmed list of health authorities to notify |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SeriousBreachResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/serious-breaches/{id}/confirm', {
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
        '/api/v1/quality/api/v1/quality/serious-breaches/{id}/confirm',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/serious-breaches/{id}/confirm' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/quality/serious-breaches/{id}/status`

**Summary:** Update Serious Breach Status  
**Operation ID:** `update_serious_breach_status_api_v1_quality_serious_breaches__id__status_put`  

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

**Schema Type:** `SeriousBreachStatusUpdate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `status` | `BreachStatus` | ✔ Yes | Target status: CONFIRMED_BREACH, AUTHORITY_NOTIFIED, RESOLVED, DISMISSED |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SeriousBreachResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/serious-breaches/{id}/status', {
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
        '/api/v1/quality/api/v1/quality/serious-breaches/{id}/status',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/quality/api/v1/quality/serious-breaches/{id}/status' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/quality/surveillance/run`

**Summary:** Execute Quality Surveillance Run  
**Operation ID:** `execute_quality_surveillance_run_api_v1_quality_surveillance_run_post`  

**Clinical & GxP Rationale:**  
Executes a complete, autonomous cross-domain quality surveillance run.

Harvests clinical data across domains, enforces sample power gating,
evaluates statistical forensics & computer vision sentinels,
and idempotently dispatches 21 CFR Part 11 Deviations.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `QualitySurveillanceRunRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Target study ID for autonomous surveillance |
| `run_name` | `string` | `null` | No | Optional custom name for the surveillance sweep |
| `auto_dispatch_deviations` | `boolean` | No | Whether to automatically dispatch 21 CFR Part 11 Deviation records for high-risk anomalies |
| `target_variables` | `Array<string>` | `null` | No | Optional list of target continuous variables for statistical CSM |
| `min_subjects_power` | `integer` | No | Minimum subjects required per site for statistical distribution gating |
| `min_observations_power` | `integer` | No | Minimum observations required per site for statistical distribution gating |
| `similarity_threshold` | `number` | No | Cosine/Manhattan similarity threshold for duplicate patient detection |
| `hamming_threshold` | `integer` | No | Maximum perceptual image Hamming distance threshold |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `QualitySurveillanceRunResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/quality/api/v1/quality/surveillance/run', {
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
        '/api/v1/quality/api/v1/quality/surveillance/run',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/quality/api/v1/quality/surveillance/run' \
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
const response = await fetch('http://localhost:8000/api/v1/quality/health', {
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
        '/api/v1/quality/health',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/quality/health' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---
