# Clinical Trial Management System (CTMS)

> **Functional Domain:** `Clinical Operations & Execution`  
> **Service Host Port:** `localhost:8005`  
> **Gateway Proxy Prefix:** `/api/v1/ctms`  
> **Persistence Backend:** `PostgreSQL / SQLite`  

## Overview

Site operational tracking, milestone governance, CRA monitoring visit reports (MVR), 6-facet site greenlight, and investigator grant budgets.

---

## Endpoints Summary

| Method | Path | Summary | GxP Auth & Signatures |
| :--- | :--- | :--- | :--- |
| `GET` | [/api/v1/ctms/audit-logs](#get-api-v1-ctms-audit-logs) | Get Audit Trail | 👀 Read-Only |
| `POST` | [/api/v1/ctms/cra-allocations](#post-api-v1-ctms-cra-allocations) | Allocate Cra | ✍️ Audited Write |
| `GET` | [/api/v1/ctms/cra-allocations](#get-api-v1-ctms-cra-allocations) | List Cra Allocations | 👀 Read-Only |
| `GET` | [/api/v1/ctms/cra-allocations/workload](#get-api-v1-ctms-cra-allocations-workload) | Retrieve Workload Summaries | 👀 Read-Only |
| `PUT` | [/api/v1/ctms/cra-allocations/{allocation_id}](#put-api-v1-ctms-cra-allocations-allocation_id) | Update Cra Allocation | ✍️ Audited Write |
| `POST` | [/api/v1/ctms/deviations](#post-api-v1-ctms-deviations) | Log Protocol Deviation | ✍️ Audited Write |
| `GET` | [/api/v1/ctms/deviations](#get-api-v1-ctms-deviations) | List Protocol Deviations | 👀 Read-Only |
| `GET` | [/api/v1/ctms/deviations/action-items](#get-api-v1-ctms-deviations-action-items) | List Deviation Action Items | 👀 Read-Only |
| `POST` | [/api/v1/ctms/deviations/action-items/{action_item_id}/complete](#post-api-v1-ctms-deviations-action-items-action_item_id-complete) | Complete Deviation Action Item | ✍️ Audited Write |
| `POST` | [/api/v1/ctms/deviations/{deviation_id}/action-items](#post-api-v1-ctms-deviations-deviation_id-action-items) | Create Deviation Action Item | ✍️ Audited Write |
| `POST` | [/api/v1/ctms/deviations/{deviation_id}/escalate-capa](#post-api-v1-ctms-deviations-deviation_id-escalate-capa) | Escalate To Quality Capa | ✍️ Audited Write |
| `POST` | [/api/v1/ctms/deviations/{deviation_id}/rca](#post-api-v1-ctms-deviations-deviation_id-rca) | Perform Root Cause Analysis | ✍️ Audited Write |
| `POST` | [/api/v1/ctms/deviations/{deviation_id}/resolve](#post-api-v1-ctms-deviations-deviation_id-resolve) | Resolve Protocol Deviation | ✍️ Audited Write |
| `POST` | [/api/v1/ctms/doa/delegate](#post-api-v1-ctms-doa-delegate) | Delegate Site Tasks | ✍️ Audited Write |
| `POST` | [/api/v1/ctms/doa/revoke](#post-api-v1-ctms-doa-revoke) | Revoke Site Tasks | ✍️ Audited Write |
| `POST` | [/api/v1/ctms/doa/sign-off](#post-api-v1-ctms-doa-sign-off) | Sign Off Delegation | 🔒 Step-Up Sig Token |
| `GET` | [/api/v1/ctms/doa/sites/{site_id}/export-pdf](#get-api-v1-ctms-doa-sites-site_id-export-pdf) | Export Site Doa Pdf | 👀 Read-Only |
| `GET` | [/api/v1/ctms/doa/sites/{site_id}/log](#get-api-v1-ctms-doa-sites-site_id-log) | Get Site Doa Log | 👀 Read-Only |
| `POST` | [/api/v1/ctms/etmf-sync](#post-api-v1-ctms-etmf-sync) | Sync Artifact To Etmf | ✍️ Audited Write |
| `GET` | [/api/v1/ctms/etmf-sync](#get-api-v1-ctms-etmf-sync) | List Etmf Sync Records | 👀 Read-Only |
| `POST` | [/api/v1/ctms/financials/adjustments](#post-api-v1-ctms-financials-adjustments) | Create Budget Adjustment | ✍️ Audited Write |
| `GET` | [/api/v1/ctms/financials/adjustments](#get-api-v1-ctms-financials-adjustments) | List Budget Adjustments | 👀 Read-Only |
| `POST` | [/api/v1/ctms/financials/adjustments/{adjustment_id}/approve](#post-api-v1-ctms-financials-adjustments-adjustment_id-approve) | Approve Budget Adjustment | ✍️ Audited Write |
| `GET` | [/api/v1/ctms/financials/amendments/{amendment_id}](#get-api-v1-ctms-financials-amendments-amendment_id) | Get Grant Amendment | 👀 Read-Only |
| `POST` | [/api/v1/ctms/financials/convert](#post-api-v1-ctms-financials-convert) | Convert Currency | ✍️ Audited Write |
| `GET` | [/api/v1/ctms/financials/exchange-rates](#get-api-v1-ctms-financials-exchange-rates) | List Exchange Rates | 👀 Read-Only |
| `POST` | [/api/v1/ctms/financials/exchange-rates](#post-api-v1-ctms-financials-exchange-rates) | Set Exchange Rate | ✍️ Audited Write |
| `POST` | [/api/v1/ctms/financials/grants/{grant_id}/amend](#post-api-v1-ctms-financials-grants-grant_id-amend) | Execute Contract Amendment | ✍️ Audited Write |
| `GET` | [/api/v1/ctms/financials/grants/{grant_id}/amendments](#get-api-v1-ctms-financials-grants-grant_id-amendments) | List Grant Amendments | 👀 Read-Only |
| `POST` | [/api/v1/ctms/financials/grants/{grant_id}/apply-template](#post-api-v1-ctms-financials-grants-grant_id-apply-template) | Apply Template To Grant | ✍️ Audited Write |
| `GET` | [/api/v1/ctms/financials/grants/{grant_id}/calculate-visit](#get-api-v1-ctms-financials-grants-grant_id-calculate-visit) | Calculate Visit Payable | 👀 Read-Only |
| `POST` | [/api/v1/ctms/financials/invoices](#post-api-v1-ctms-financials-invoices) | Create Batch Invoice | ✍️ Audited Write |
| `GET` | [/api/v1/ctms/financials/invoices](#get-api-v1-ctms-financials-invoices) | List Invoices | 👀 Read-Only |
| `POST` | [/api/v1/ctms/financials/invoices/{invoice_id}/approve](#post-api-v1-ctms-financials-invoices-invoice_id-approve) | Approve And Disburse Invoice | ✍️ Audited Write |
| `POST` | [/api/v1/ctms/financials/procedure-grids](#post-api-v1-ctms-financials-procedure-grids) | Add Procedure Payment Grid | ✍️ Audited Write |
| `GET` | [/api/v1/ctms/financials/procedure-grids](#get-api-v1-ctms-financials-procedure-grids) | List Procedure Grids | 👀 Read-Only |
| `POST` | [/api/v1/ctms/financials/templates](#post-api-v1-ctms-financials-templates) | Create Budget Template | ✍️ Audited Write |
| `GET` | [/api/v1/ctms/financials/templates](#get-api-v1-ctms-financials-templates) | List Budget Templates | 👀 Read-Only |
| `GET` | [/api/v1/ctms/financials/templates/{template_id}](#get-api-v1-ctms-financials-templates-template_id) | Get Budget Template | 👀 Read-Only |
| `POST` | [/api/v1/ctms/grants](#post-api-v1-ctms-grants) | Create Grant | ✍️ Audited Write |
| `GET` | [/api/v1/ctms/grants](#get-api-v1-ctms-grants) | List Grants | 👀 Read-Only |
| `GET` | [/api/v1/ctms/grants/{grant_id}](#get-api-v1-ctms-grants-grant_id) | Get Grant | 👀 Read-Only |
| `PUT` | [/api/v1/ctms/grants/{grant_id}](#put-api-v1-ctms-grants-grant_id) | Update Grant | ✍️ Audited Write |
| `POST` | [/api/v1/ctms/grants/{grant_id}/budget-items](#post-api-v1-ctms-grants-grant_id-budget-items) | Create Budget Line Item | ✍️ Audited Write |
| `GET` | [/api/v1/ctms/grants/{grant_id}/budget-items](#get-api-v1-ctms-grants-grant_id-budget-items) | List Budget Line Items | 👀 Read-Only |
| `POST` | [/api/v1/ctms/grants/{grant_id}/evaluate](#post-api-v1-ctms-grants-grant_id-evaluate) | Evaluate Grant Milestones | ✍️ Audited Write |
| `POST` | [/api/v1/ctms/grants/{grant_id}/milestones](#post-api-v1-ctms-grants-grant_id-milestones) | Create Payment Milestone | ✍️ Audited Write |
| `GET` | [/api/v1/ctms/grants/{grant_id}/milestones](#get-api-v1-ctms-grants-grant_id-milestones) | List Payment Milestones | 👀 Read-Only |
| `POST` | [/api/v1/ctms/grants/{grant_id}/milestones/{milestone_id}/trigger](#post-api-v1-ctms-grants-grant_id-milestones-milestone_id-trigger) | Trigger Manual Milestone | ✍️ Audited Write |
| `GET` | [/api/v1/ctms/grants/{grant_id}/payables](#get-api-v1-ctms-grants-grant_id-payables) | List Investigator Payables | 👀 Read-Only |
| `POST` | [/api/v1/ctms/ip/destruction-certificates](#post-api-v1-ctms-ip-destruction-certificates) | Generate Destruction Certificate | ✍️ Audited Write |
| `POST` | [/api/v1/ctms/ip/excursions](#post-api-v1-ctms-ip-excursions) | Log Temperature Excursion | ✍️ Audited Write |
| `POST` | [/api/v1/ctms/ip/excursions/{excursion_id}/disposition](#post-api-v1-ctms-ip-excursions-excursion_id-disposition) | Disposition Temperature Excursion | ✍️ Audited Write |
| `GET` | [/api/v1/ctms/ip/kits](#get-api-v1-ctms-ip-kits) | List Ip Kits | 👀 Read-Only |
| `POST` | [/api/v1/ctms/ip/kits/{kit_id}/dispense](#post-api-v1-ctms-ip-kits-kit_id-dispense) | Dispense Ip Kit | ✍️ Audited Write |
| `POST` | [/api/v1/ctms/ip/kits/{kit_id}/reconcile](#post-api-v1-ctms-ip-kits-kit_id-reconcile) | Reconcile Returned Kit | ✍️ Audited Write |
| `POST` | [/api/v1/ctms/ip/shipments/receive](#post-api-v1-ctms-ip-shipments-receive) | Receive Ip Shipment | ✍️ Audited Write |
| `POST` | [/api/v1/ctms/monitoring-visits](#post-api-v1-ctms-monitoring-visits) | Schedule Monitoring Visit | ✍️ Audited Write |
| `GET` | [/api/v1/ctms/monitoring-visits](#get-api-v1-ctms-monitoring-visits) | List Monitoring Visits | 👀 Read-Only |
| `POST` | [/api/v1/ctms/monitoring-visits/bulk-sync](#post-api-v1-ctms-monitoring-visits-bulk-sync) | Bulk Sync Monitoring Visits | ✍️ Audited Write |
| `POST` | [/api/v1/ctms/monitoring-visits/sync](#post-api-v1-ctms-monitoring-visits-sync) | Sync Monitoring Visit | ✍️ Audited Write |
| `POST` | [/api/v1/ctms/monitoring-visits/{visit_id}/complete](#post-api-v1-ctms-monitoring-visits-visit_id-complete) | Complete Monitoring Visit | ✍️ Audited Write |
| `GET` | [/api/v1/ctms/monitoring-visits/{visit_id}/letters](#get-api-v1-ctms-monitoring-visits-visit_id-letters) | Get Monitoring Visit Letters | 👀 Read-Only |
| `GET` | [/api/v1/ctms/monitoring-visits/{visit_id}/letters/{letter_type}](#get-api-v1-ctms-monitoring-visits-visit_id-letters-letter_type) | Get Monitoring Visit Letter By Type | 👀 Read-Only |
| `POST` | [/api/v1/ctms/monitoring-visits/{visit_id}/sign-off](#post-api-v1-ctms-monitoring-visits-visit_id-sign-off) | Sign Off Monitoring Visit | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/ctms/rbqm/kri](#post-api-v1-ctms-rbqm-kri) | Record Kri Metric | ✍️ Audited Write |
| `GET` | [/api/v1/ctms/rbqm/kri](#get-api-v1-ctms-rbqm-kri) | List Kri Metrics | 👀 Read-Only |
| `POST` | [/api/v1/ctms/rbqm/sites/{site_id}/evaluate-risk](#post-api-v1-ctms-rbqm-sites-site_id-evaluate-risk) | Evaluate Site Risk Score | ✍️ Audited Write |
| `GET` | [/api/v1/ctms/rbqm/sites/{site_id}/risk-score](#get-api-v1-ctms-rbqm-sites-site_id-risk-score) | Get Site Risk Score | 👀 Read-Only |
| `POST` | [/api/v1/ctms/recruitment](#post-api-v1-ctms-recruitment) | Record Recruitment | ✍️ Audited Write |
| `GET` | [/api/v1/ctms/recruitment](#get-api-v1-ctms-recruitment) | List Recruitment Records | 👀 Read-Only |
| `GET` | [/api/v1/ctms/resupply-events](#get-api-v1-ctms-resupply-events) | List Ctms Resupply Events | 👀 Read-Only |
| `POST` | [/api/v1/ctms/resupply-events/{event_id}/confirm](#post-api-v1-ctms-resupply-events-event_id-confirm) | Approve Ctms Resupply Event | ✍️ Audited Write |
| `POST` | [/api/v1/ctms/resupply-events/{event_id}/reject](#post-api-v1-ctms-resupply-events-event_id-reject) | Reject Ctms Resupply Event | ✍️ Audited Write |
| `POST` | [/api/v1/ctms/site-milestones](#post-api-v1-ctms-site-milestones) | Create Site Milestone | ✍️ Audited Write |
| `GET` | [/api/v1/ctms/site-milestones](#get-api-v1-ctms-site-milestones) | List Site Milestones | 👀 Read-Only |
| `PUT` | [/api/v1/ctms/site-milestones/{milestone_id}](#put-api-v1-ctms-site-milestones-milestone_id) | Update Site Milestone | ✍️ Audited Write |
| `POST` | [/api/v1/ctms/startup/documents](#post-api-v1-ctms-startup-documents) | Submit Essential Document | ✍️ Audited Write |
| `GET` | [/api/v1/ctms/startup/documents](#get-api-v1-ctms-startup-documents) | List Essential Documents | 👀 Read-Only |
| `POST` | [/api/v1/ctms/startup/documents/{document_id}/review](#post-api-v1-ctms-startup-documents-document_id-review) | Review Essential Document | ✍️ Audited Write |
| `PUT` | [/api/v1/ctms/startup/documents/{document_id}/review](#put-api-v1-ctms-startup-documents-document_id-review) | Review Essential Document | ✍️ Audited Write |
| `POST` | [/api/v1/ctms/startup/milestones](#post-api-v1-ctms-startup-milestones) | Create Or Update Country Milestone | ✍️ Audited Write |
| `GET` | [/api/v1/ctms/startup/milestones](#get-api-v1-ctms-startup-milestones) | List Country Milestones | 👀 Read-Only |
| `POST` | [/api/v1/ctms/startup/rules](#post-api-v1-ctms-startup-rules) | Create Or Update Country Prerequisite Rule | ✍️ Audited Write |
| `GET` | [/api/v1/ctms/startup/rules](#get-api-v1-ctms-startup-rules) | List Country Prerequisite Rules | 👀 Read-Only |
| `GET` | [/api/v1/ctms/startup/rules/presets](#get-api-v1-ctms-startup-rules-presets) | Get Jurisdiction Rule Presets | 👀 Read-Only |
| `GET` | [/api/v1/ctms/startup/sites/{site_id}/greenlight](#get-api-v1-ctms-startup-sites-site_id-greenlight) | Evaluate Site Greenlight | 👀 Read-Only |
| `POST` | [/api/v1/ctms/startup/sites/{site_id}/greenlight/certify](#post-api-v1-ctms-startup-sites-site_id-greenlight-certify) | Certify Site Greenlight | ✍️ Audited Write |
| `POST` | [/api/v1/ctms/startup/sites/{site_id}/greenlight/override](#post-api-v1-ctms-startup-sites-site_id-greenlight-override) | Override Site Greenlight | ✍️ Audited Write |
| `POST` | [/api/v1/ctms/startup/sites/{site_id}/greenlight/verify-token](#post-api-v1-ctms-startup-sites-site_id-greenlight-verify-token) | Verify Site Greenlight Token Endpoint | ✍️ Audited Write |
| `GET` | [/api/v1/ctms/studies](#get-api-v1-ctms-studies) | List Studies | 👀 Read-Only |
| `POST` | [/api/v1/ctms/studies](#post-api-v1-ctms-studies) | Create Study | ✍️ Audited Write |
| `GET` | [/health](#get-health) | Health Check | 👀 Read-Only |

---

## Granular Endpoint Specifications

### `GET` `/api/v1/ctms/audit-logs`

**Summary:** Get Audit Trail  
**Operation ID:** `get_audit_trail_api_v1_ctms_audit_logs_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<CTMSAuditLogResponse>` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/audit-logs', {
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
        '/api/v1/ctms/api/v1/ctms/audit-logs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/audit-logs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/cra-allocations`

**Summary:** Allocate Cra  
**Operation ID:** `allocate_cra_api_v1_ctms_cra_allocations_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CRAAllocationCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `cra_id` | `string` | ✔ Yes | CRA ID being allocated |
| `site_id` | `string` | ✔ Yes | Site ID |
| `study_id` | `string` | ✔ Yes | Study ID |
| `status` | `string` | `null` | No | Allocation status |
| `effective_start_date` | `string` | `null` | No | Effective start date |
| `effective_end_date` | `string` | `null` | No | Effective end date |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `CRAAllocationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/cra-allocations', {
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
        '/api/v1/ctms/api/v1/ctms/cra-allocations',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/cra-allocations' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/cra-allocations`

**Summary:** List Cra Allocations  
**Operation ID:** `list_cra_allocations_api_v1_ctms_cra_allocations_get`  

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
| `cra_id` | `query` | `string` | `null` | No | — |
| `status` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<CRAAllocationResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/cra-allocations', {
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
        '/api/v1/ctms/api/v1/ctms/cra-allocations',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/cra-allocations' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/cra-allocations/workload`

**Summary:** Retrieve Workload Summaries  
**Operation ID:** `retrieve_workload_summaries_api_v1_ctms_cra_allocations_workload_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<CRAWorkloadItem>` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/cra-allocations/workload', {
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
        '/api/v1/ctms/api/v1/ctms/cra-allocations/workload',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/cra-allocations/workload' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/ctms/cra-allocations/{allocation_id}`

**Summary:** Update Cra Allocation  
**Operation ID:** `update_cra_allocation_api_v1_ctms_cra_allocations__allocation_id__put`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `allocation_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `CRAAllocationUpdate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `cra_id` | `string` | `null` | No | CRA ID being allocated |
| `status` | `string` | `null` | No | Allocation status |
| `effective_start_date` | `string` | `null` | No | Effective start date |
| `effective_end_date` | `string` | `null` | No | Effective end date |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `CRAAllocationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/cra-allocations/{allocation_id}', {
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
        '/api/v1/ctms/api/v1/ctms/cra-allocations/{allocation_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/ctms/api/v1/ctms/cra-allocations/{allocation_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/deviations`

**Summary:** Log Protocol Deviation  
**Operation ID:** `log_protocol_deviation_api_v1_ctms_deviations_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ProtocolDeviationCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Study ID |
| `site_id` | `string` | ✔ Yes | Site ID |
| `subject_id` | `string` | `null` | No | Subject ID if applicable |
| `visit_id` | `string` | `null` | No | Visit ID if applicable |
| `deviation_category` | `string` | ✔ Yes | Category (ELIGIBILITY, INFORMED_CONSENT, DOSING_IP, SAFETY_REPORTING, VISIT_WINDOW, OTHER) |
| `severity` | `string` | ✔ Yes | Severity (MINOR, MAJOR, CRITICAL) |
| `title` | `string` | ✔ Yes | Brief title of deviation |
| `description` | `string` | ✔ Yes | Detailed description of protocol deviation |
| `date_occurred` | `string` | ✔ Yes | Date deviation occurred (YYYY-MM-DD) |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `ProtocolDeviationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/deviations', {
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
        '/api/v1/ctms/api/v1/ctms/deviations',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/deviations' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/deviations`

**Summary:** List Protocol Deviations  
**Operation ID:** `list_protocol_deviations_api_v1_ctms_deviations_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | — |
| `site_id` | `query` | `string` | `null` | No | — |
| `severity` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<ProtocolDeviationResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/deviations', {
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
        '/api/v1/ctms/api/v1/ctms/deviations',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/deviations' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/deviations/action-items`

**Summary:** List Deviation Action Items  
**Operation ID:** `list_deviation_action_items_api_v1_ctms_deviations_action_items_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `deviation_id` | `query` | `string` | `null` | No | — |
| `site_id` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<DeviationActionItemResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/deviations/action-items', {
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
        '/api/v1/ctms/api/v1/ctms/deviations/action-items',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/deviations/action-items' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/deviations/action-items/{action_item_id}/complete`

**Summary:** Complete Deviation Action Item  
**Operation ID:** `complete_deviation_action_item_api_v1_ctms_deviations_action_items__action_item_id__complete_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `action_item_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `DeviationActionItemComplete`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `resolution_notes` | `string` | ✔ Yes | Resolution summary |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `DeviationActionItemResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/deviations/action-items/{action_item_id}/complete', {
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
        '/api/v1/ctms/api/v1/ctms/deviations/action-items/{action_item_id}/complete',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/deviations/action-items/{action_item_id}/complete' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/deviations/{deviation_id}/action-items`

**Summary:** Create Deviation Action Item  
**Operation ID:** `create_deviation_action_item_api_v1_ctms_deviations__deviation_id__action_items_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `deviation_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `DeviationActionItemCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `deviation_id` | `string` | ✔ Yes | Deviation ID |
| `site_id` | `string` | ✔ Yes | Site ID |
| `description` | `string` | ✔ Yes | Description of the action item task |
| `assignee_user_id` | `string` | ✔ Yes | User ID of assignee |
| `assignee_role` | `string` | ✔ Yes | Role of assignee |
| `due_date` | `string` | ✔ Yes | Due date (YYYY-MM-DD) |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `DeviationActionItemResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/deviations/{deviation_id}/action-items', {
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
        '/api/v1/ctms/api/v1/ctms/deviations/{deviation_id}/action-items',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/deviations/{deviation_id}/action-items' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/deviations/{deviation_id}/escalate-capa`

**Summary:** Escalate To Quality Capa  
**Operation ID:** `escalate_to_quality_capa_api_v1_ctms_deviations__deviation_id__escalate_capa_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `deviation_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ProtocolDeviationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/deviations/{deviation_id}/escalate-capa', {
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
        '/api/v1/ctms/api/v1/ctms/deviations/{deviation_id}/escalate-capa',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/deviations/{deviation_id}/escalate-capa' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/deviations/{deviation_id}/rca`

**Summary:** Perform Root Cause Analysis  
**Operation ID:** `perform_root_cause_analysis_api_v1_ctms_deviations__deviation_id__rca_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `deviation_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ProtocolDeviationRCA`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `root_cause_5whys` | `Array<string>` | No | 5-Why questions and answers |
| `root_cause_summary` | `string` | ✔ Yes | Summary of root cause analysis |
| `corrective_action_plan` | `string` | ✔ Yes | Corrective action plan (CAP) |
| `preventive_action_plan` | `string` | ✔ Yes | Preventive action plan (PAP) |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ProtocolDeviationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/deviations/{deviation_id}/rca', {
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
        '/api/v1/ctms/api/v1/ctms/deviations/{deviation_id}/rca',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/deviations/{deviation_id}/rca' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/deviations/{deviation_id}/resolve`

**Summary:** Resolve Protocol Deviation  
**Operation ID:** `resolve_protocol_deviation_api_v1_ctms_deviations__deviation_id__resolve_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `deviation_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ProtocolDeviationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/deviations/{deviation_id}/resolve', {
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
        '/api/v1/ctms/api/v1/ctms/deviations/{deviation_id}/resolve',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/deviations/{deviation_id}/resolve' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/doa/delegate`

**Summary:** Delegate Site Tasks  
**Operation ID:** `delegate_site_tasks_api_v1_ctms_doa_delegate_post`  

**Clinical & GxP Rationale:**  
Assign site trial task delegation requiring Principal Investigator sign-off.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `DelegationTaskRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `site_id` | `string` | ✔ Yes | The unique investigator site ID |
| `staff_user_id` | `string` | ✔ Yes | Unique user Keycloak subject ID of the staff member |
| `task_codes` | `Array<string>` | ✔ Yes | List of delegated trial duty task codes |
| `start_date` | `string` | ✔ Yes | Effective start date of the delegation (YYYY-MM-DD) |
| `reason_for_change` | `string` | ✔ Yes | Mandatory GxP justification reason for delegation |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/doa/delegate', {
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
        '/api/v1/ctms/api/v1/ctms/doa/delegate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/doa/delegate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/doa/revoke`

**Summary:** Revoke Site Tasks  
**Operation ID:** `revoke_site_tasks_api_v1_ctms_doa_revoke_post`  

**Clinical & GxP Rationale:**  
Revoke or end a delegated trial duty with reason for change.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `RevokeDelegationRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `record_id` | `string` | ✔ Yes | The unique delegation record ID to revoke |
| `reason_for_change` | `string` | ✔ Yes | Mandatory justification reason for revocation |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/doa/revoke', {
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
        '/api/v1/ctms/api/v1/ctms/doa/revoke',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/doa/revoke' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/doa/sign-off`

**Summary:** Sign Off Delegation  
**Operation ID:** `sign_off_delegation_api_v1_ctms_doa_sign_off_post`  

**Clinical & GxP Rationale:**  
Endorse Delegation of Authority task assignment with Principal Investigator eSignature.

Requirements: PRD-SYS-001

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Payload Schema (`application/json`)

**Schema Type:** `DOASignOffRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `record_id` | `string` | ✔ Yes | The unique delegation record ID to sign off |
| `reason_for_change` | `string` | ✔ Yes | Mandatory GxP 21 CFR Part 11 justification reason |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/doa/sign-off', {
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
        '/api/v1/ctms/api/v1/ctms/doa/sign-off',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/doa/sign-off' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/doa/sites/{site_id}/export-pdf`

**Summary:** Export Site Doa Pdf  
**Operation ID:** `export_site_doa_pdf_api_v1_ctms_doa_sites__site_id__export_pdf_get`  

**Clinical & GxP Rationale:**  
Export 21 CFR Part 11 signed DOA PDF log.

Requirements: PRD-SYS-001

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
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/doa/sites/{site_id}/export-pdf', {
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
        '/api/v1/ctms/api/v1/ctms/doa/sites/{site_id}/export-pdf',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/doa/sites/{site_id}/export-pdf' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/doa/sites/{site_id}/log`

**Summary:** Get Site Doa Log  
**Operation ID:** `get_site_doa_log_api_v1_ctms_doa_sites__site_id__log_get`  

**Clinical & GxP Rationale:**  
Fetch active and historical DOA log matrix for a site.

Requirements: PRD-SYS-001

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
| `200` | Successful Response | `DOALogResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/doa/sites/{site_id}/log', {
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
        '/api/v1/ctms/api/v1/ctms/doa/sites/{site_id}/log',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/doa/sites/{site_id}/log' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/etmf-sync`

**Summary:** Sync Artifact To Etmf  
**Operation ID:** `sync_artifact_to_etmf_api_v1_ctms_etmf_sync_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ETMFSyncRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Study ID |
| `site_id` | `string` | ✔ Yes | Site ID |
| `artifact_type` | `string` | ✔ Yes | Artifact type (MVR_REPORT, DOA_LOG, GREENLIGHT_PACKAGE, DEVIATION_REPORT, IP_DESTRUCTION_CERT) |
| `source_record_id` | `string` | ✔ Yes | Source record ID in CTMS |
| `title` | `string` | ✔ Yes | Document Title |
| `content_text` | `string` | ✔ Yes | Full text or markdown content of artifact |
| `dia_zone` | `string` | No | DIA TMF Reference Model Zone |
| `dia_section` | `string` | No | DIA TMF Section |
| `dia_artifact` | `string` | No | DIA TMF Artifact Name |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `ETMFSyncRecordResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/etmf-sync', {
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
        '/api/v1/ctms/api/v1/ctms/etmf-sync',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/etmf-sync' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/etmf-sync`

**Summary:** List Etmf Sync Records  
**Operation ID:** `list_etmf_sync_records_api_v1_ctms_etmf_sync_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | — |
| `site_id` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<ETMFSyncRecordResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/etmf-sync', {
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
        '/api/v1/ctms/api/v1/ctms/etmf-sync',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/etmf-sync' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/financials/adjustments`

**Summary:** Create Budget Adjustment  
**Operation ID:** `create_budget_adjustment_api_v1_ctms_financials_adjustments_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `BudgetAdjustmentCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `grant_id` | `string` | ✔ Yes | Investigator grant ID |
| `site_id` | `string` | ✔ Yes | Site ID |
| `study_id` | `string` | ✔ Yes | Study ID |
| `adjustment_type` | `string` | ✔ Yes | Type (PASSTHROUGH_REIMBURSEMENT, PROTOCOL_DEVIATION_PENALTY, OUT_OF_WINDOW_PENALTY, SITE_BONUS, DISCRETIONARY_CREDIT) |
| `description` | `string` | ✔ Yes | Clinical/operational justification |
| `amount` | `number` | ✔ Yes | Adjustment value (positive credit or negative deduction) |
| `currency` | `string` | No | Currency code |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `BudgetAdjustmentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/adjustments', {
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
        '/api/v1/ctms/api/v1/ctms/financials/adjustments',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/adjustments' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/financials/adjustments`

**Summary:** List Budget Adjustments  
**Operation ID:** `list_budget_adjustments_api_v1_ctms_financials_adjustments_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `grant_id` | `query` | `string` | `null` | No | — |
| `study_id` | `query` | `string` | `null` | No | — |
| `site_id` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<BudgetAdjustmentResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/adjustments', {
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
        '/api/v1/ctms/api/v1/ctms/financials/adjustments',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/adjustments' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/financials/adjustments/{adjustment_id}/approve`

**Summary:** Approve Budget Adjustment  
**Operation ID:** `approve_budget_adjustment_api_v1_ctms_financials_adjustments__adjustment_id__approve_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `adjustment_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `BudgetAdjustmentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/adjustments/{adjustment_id}/approve', {
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
        '/api/v1/ctms/api/v1/ctms/financials/adjustments/{adjustment_id}/approve',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/adjustments/{adjustment_id}/approve' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/financials/amendments/{amendment_id}`

**Summary:** Get Grant Amendment  
**Operation ID:** `get_grant_amendment_api_v1_ctms_financials_amendments__amendment_id__get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `amendment_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ContractAmendmentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/amendments/{amendment_id}', {
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
        '/api/v1/ctms/api/v1/ctms/financials/amendments/{amendment_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/amendments/{amendment_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/financials/convert`

**Summary:** Convert Currency  
**Operation ID:** `convert_currency_api_v1_ctms_financials_convert_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CurrencyConvertRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `amount` | `number` | ✔ Yes | Monetary amount to convert |
| `from_currency` | `string` | No | Source ISO currency |
| `to_currency` | `string` | ✔ Yes | Target ISO currency |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `CurrencyConvertResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/convert', {
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
        '/api/v1/ctms/api/v1/ctms/financials/convert',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/convert' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/financials/exchange-rates`

**Summary:** List Exchange Rates  
**Operation ID:** `list_exchange_rates_api_v1_ctms_financials_exchange_rates_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<CurrencyExchangeRateResponse>` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/exchange-rates', {
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
        '/api/v1/ctms/api/v1/ctms/financials/exchange-rates',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/exchange-rates' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/financials/exchange-rates`

**Summary:** Set Exchange Rate  
**Operation ID:** `set_exchange_rate_api_v1_ctms_financials_exchange_rates_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CurrencyExchangeRateCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `base_currency` | `string` | No | Base ISO currency (e.g. USD) |
| `target_currency` | `string` | ✔ Yes | Target ISO currency (e.g. EUR) |
| `rate` | `number` | ✔ Yes | Exchange multiplier from base to target |
| `expiry_date` | `string` | `null` | No | Optional expiry ISO date |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `CurrencyExchangeRateResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/exchange-rates', {
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
        '/api/v1/ctms/api/v1/ctms/financials/exchange-rates',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/exchange-rates' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/financials/grants/{grant_id}/amend`

**Summary:** Execute Contract Amendment  
**Operation ID:** `execute_contract_amendment_api_v1_ctms_financials_grants__grant_id__amend_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `grant_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ContractAmendmentCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `summary_of_changes` | `string` | ✔ Yes | Detailed summary of contract changes |
| `revised_budget` | `number` | ✔ Yes | New amended total grant budget |
| `updated_procedures` | `Array<object>` | `null` | No | Optional updated fee schedule items |
| `signature_manifestation` | `object` | `null` | No | Electronic signature payload |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `ContractAmendmentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/grants/{grant_id}/amend', {
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
        '/api/v1/ctms/api/v1/ctms/financials/grants/{grant_id}/amend',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/grants/{grant_id}/amend' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/financials/grants/{grant_id}/amendments`

**Summary:** List Grant Amendments  
**Operation ID:** `list_grant_amendments_api_v1_ctms_financials_grants__grant_id__amendments_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `grant_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<ContractAmendmentResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/grants/{grant_id}/amendments', {
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
        '/api/v1/ctms/api/v1/ctms/financials/grants/{grant_id}/amendments',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/grants/{grant_id}/amendments' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/financials/grants/{grant_id}/apply-template`

**Summary:** Apply Template To Grant  
**Operation ID:** `apply_template_to_grant_api_v1_ctms_financials_grants__grant_id__apply_template_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `grant_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ApplyTemplateRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `template_id` | `string` | ✔ Yes | Site budget template ID to apply |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `Array<ProcedurePaymentGridResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/grants/{grant_id}/apply-template', {
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
        '/api/v1/ctms/api/v1/ctms/financials/grants/{grant_id}/apply-template',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/grants/{grant_id}/apply-template' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/financials/grants/{grant_id}/calculate-visit`

**Summary:** Calculate Visit Payable  
**Operation ID:** `calculate_visit_payable_api_v1_ctms_financials_grants__grant_id__calculate_visit_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `grant_id` | `path` | `string` | ✔ Yes | — |
| `visit_name` | `query` | `string` | ✔ Yes | — |
| `version_index` | `query` | `integer` | `null` | No | — |
| `currency` | `query` | `string` | No | — |
| `target_currency` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `VisitPayableCalculationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/grants/{grant_id}/calculate-visit', {
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
        '/api/v1/ctms/api/v1/ctms/financials/grants/{grant_id}/calculate-visit',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/grants/{grant_id}/calculate-visit' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/financials/invoices`

**Summary:** Create Batch Invoice  
**Operation ID:** `create_batch_invoice_api_v1_ctms_financials_invoices_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `FinancialInvoiceCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Study ID |
| `site_id` | `string` | ✔ Yes | Site ID |
| `grant_id` | `string` | ✔ Yes | Grant ID |
| `invoice_type` | `string` | No | Type of invoice |
| `gross_amount` | `number` | ✔ Yes | Gross payable amount |
| `withholding_amount` | `number` | No | Withheld amount |
| `currency` | `string` | No | Currency code |
| `payable_ids` | `Array<string>` | No | List of included payable IDs |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `FinancialInvoiceResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/invoices', {
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
        '/api/v1/ctms/api/v1/ctms/financials/invoices',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/invoices' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/financials/invoices`

**Summary:** List Invoices  
**Operation ID:** `list_invoices_api_v1_ctms_financials_invoices_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | — |
| `site_id` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<FinancialInvoiceResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/invoices', {
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
        '/api/v1/ctms/api/v1/ctms/financials/invoices',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/invoices' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/financials/invoices/{invoice_id}/approve`

**Summary:** Approve And Disburse Invoice  
**Operation ID:** `approve_and_disburse_invoice_api_v1_ctms_financials_invoices__invoice_id__approve_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `invoice_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `FinancialInvoiceResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/invoices/{invoice_id}/approve', {
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
        '/api/v1/ctms/api/v1/ctms/financials/invoices/{invoice_id}/approve',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/invoices/{invoice_id}/approve' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/financials/procedure-grids`

**Summary:** Add Procedure Payment Grid  
**Operation ID:** `add_procedure_payment_grid_api_v1_ctms_financials_procedure_grids_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ProcedurePaymentGridCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `grant_id` | `string` | ✔ Yes | Grant ID |
| `visit_name` | `string` | ✔ Yes | Visit name (e.g. SCREENING, WEEK_4, END_OF_TREATMENT) |
| `procedure_code` | `string` | ✔ Yes | CPT/Procedure code |
| `procedure_name` | `string` | ✔ Yes | Descriptive procedure name |
| `base_amount` | `number` | ✔ Yes | Base fee amount |
| `overhead_percentage` | `number` | No | Institutional overhead % |
| `withholding_percentage` | `number` | No | Retention/Withholding % (e.g. 10%) |
| `activity_id` | `string` | `null` | No | Optional CDISC USDM protocol activity ID |
| `form_id` | `string` | `null` | No | Optional eCRF form ID linkage |
| `patient_stipend` | `number` | No | Per-procedure patient stipend |
| `travel_reimbursement` | `number` | No | Per-procedure travel reimbursement |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `ProcedurePaymentGridResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/procedure-grids', {
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
        '/api/v1/ctms/api/v1/ctms/financials/procedure-grids',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/procedure-grids' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/financials/procedure-grids`

**Summary:** List Procedure Grids  
**Operation ID:** `list_procedure_grids_api_v1_ctms_financials_procedure_grids_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `grant_id` | `query` | `string` | ✔ Yes | — |
| `is_active_only` | `query` | `boolean` | No | — |
| `version_index` | `query` | `integer` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<ProcedurePaymentGridResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/procedure-grids', {
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
        '/api/v1/ctms/api/v1/ctms/financials/procedure-grids',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/procedure-grids' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/financials/templates`

**Summary:** Create Budget Template  
**Operation ID:** `create_budget_template_api_v1_ctms_financials_templates_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `SiteBudgetTemplateCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `template_name` | `string` | ✔ Yes | Template unique title |
| `study_id` | `string` | `null` | No | Optional study ID scope |
| `tier_level` | `string` | No | Tier classification (TIER_1_ACADEMIC, TIER_2_COMMUNITY, TIER_3_REGIONAL) |
| `currency` | `string` | No | Currency (USD, EUR, GBP, etc.) |
| `overhead_percentage` | `number` | No | Default institutional overhead % |
| `withholding_percentage` | `number` | No | Default withholding % |
| `fixed_startup_fee` | `number` | No | Fixed site startup fee |
| `fixed_closeout_fee` | `number` | No | Fixed site closeout fee |
| `irb_fee` | `number` | No | Initial IRB/Ethics review fee |
| `patient_stipend_amount` | `number` | No | Per-visit patient stipend |
| `travel_reimbursement_rate` | `number` | No | Per-visit travel reimbursement |
| `fixed_fees` | `Array<object>` | No | List of fixed milestone/setup fees |
| `procedure_items` | `Array<object>` | No | List of procedure schedule items |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `SiteBudgetTemplateResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/templates', {
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
        '/api/v1/ctms/api/v1/ctms/financials/templates',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/templates' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/financials/templates`

**Summary:** List Budget Templates  
**Operation ID:** `list_budget_templates_api_v1_ctms_financials_templates_get`  

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

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<SiteBudgetTemplateResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/templates', {
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
        '/api/v1/ctms/api/v1/ctms/financials/templates',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/templates' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/financials/templates/{template_id}`

**Summary:** Get Budget Template  
**Operation ID:** `get_budget_template_api_v1_ctms_financials_templates__template_id__get`  

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

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SiteBudgetTemplateResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/templates/{template_id}', {
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
        '/api/v1/ctms/api/v1/ctms/financials/templates/{template_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/financials/templates/{template_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/grants`

**Summary:** Create Grant  
**Operation ID:** `create_grant_api_v1_ctms_grants_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `InvestigatorGrantCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Clinical study ID |
| `site_id` | `string` | ✔ Yes | Site ID |
| `total_budget` | `number` | No | Overall budget allocated for the site |
| `currency` | `string` | `null` | No | Currency code |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `InvestigatorGrantResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/grants', {
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
        '/api/v1/ctms/api/v1/ctms/grants',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/grants' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/grants`

**Summary:** List Grants  
**Operation ID:** `list_grants_api_v1_ctms_grants_get`  

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

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<InvestigatorGrantResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/grants', {
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
        '/api/v1/ctms/api/v1/ctms/grants',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/grants' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/grants/{grant_id}`

**Summary:** Get Grant  
**Operation ID:** `get_grant_api_v1_ctms_grants__grant_id__get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `grant_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `InvestigatorGrantResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/grants/{grant_id}', {
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
        '/api/v1/ctms/api/v1/ctms/grants/{grant_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/grants/{grant_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/ctms/grants/{grant_id}`

**Summary:** Update Grant  
**Operation ID:** `update_grant_api_v1_ctms_grants__grant_id__put`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `grant_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `InvestigatorGrantUpdate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `total_budget` | `number` | `null` | No | Updated budget |
| `currency` | `string` | `null` | No | Updated currency |
| `status` | `string` | `null` | No | Updated status (DRAFT, APPROVED) |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `InvestigatorGrantResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/grants/{grant_id}', {
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
        '/api/v1/ctms/api/v1/ctms/grants/{grant_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/ctms/api/v1/ctms/grants/{grant_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/grants/{grant_id}/budget-items`

**Summary:** Create Budget Line Item  
**Operation ID:** `create_budget_line_item_api_v1_ctms_grants__grant_id__budget_items_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `grant_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `BudgetLineItemCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `category` | `string` | ✔ Yes | Category of budget item (VISIT_COST, EQUIPMENT, etc.) |
| `description` | `string` | ✔ Yes | Description of budget line item |
| `amount` | `number` | ✔ Yes | Budget item cost |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `BudgetLineItemResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/grants/{grant_id}/budget-items', {
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
        '/api/v1/ctms/api/v1/ctms/grants/{grant_id}/budget-items',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/grants/{grant_id}/budget-items' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/grants/{grant_id}/budget-items`

**Summary:** List Budget Line Items  
**Operation ID:** `list_budget_line_items_api_v1_ctms_grants__grant_id__budget_items_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `grant_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<BudgetLineItemResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/grants/{grant_id}/budget-items', {
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
        '/api/v1/ctms/api/v1/ctms/grants/{grant_id}/budget-items',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/grants/{grant_id}/budget-items' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/grants/{grant_id}/evaluate`

**Summary:** Evaluate Grant Milestones  
**Operation ID:** `evaluate_grant_milestones_api_v1_ctms_grants__grant_id__evaluate_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `grant_id` | `path` | `string` | ✔ Yes | — |
| `condition` | `query` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/grants/{grant_id}/evaluate', {
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
        '/api/v1/ctms/api/v1/ctms/grants/{grant_id}/evaluate',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/grants/{grant_id}/evaluate' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/grants/{grant_id}/milestones`

**Summary:** Create Payment Milestone  
**Operation ID:** `create_payment_milestone_api_v1_ctms_grants__grant_id__milestones_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `grant_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `PaymentMilestoneCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `milestone_name` | `string` | ✔ Yes | Descriptive name of the payment milestone |
| `trigger_condition` | `string` | ✔ Yes | Trigger condition (VISIT_COMPLETED, STUDY_APPROVED, MANUAL) |
| `amount` | `number` | ✔ Yes | Payment amount associated with the milestone |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `PaymentMilestoneResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/grants/{grant_id}/milestones', {
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
        '/api/v1/ctms/api/v1/ctms/grants/{grant_id}/milestones',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/grants/{grant_id}/milestones' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/grants/{grant_id}/milestones`

**Summary:** List Payment Milestones  
**Operation ID:** `list_payment_milestones_api_v1_ctms_grants__grant_id__milestones_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `grant_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<PaymentMilestoneResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/grants/{grant_id}/milestones', {
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
        '/api/v1/ctms/api/v1/ctms/grants/{grant_id}/milestones',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/grants/{grant_id}/milestones' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/grants/{grant_id}/milestones/{milestone_id}/trigger`

**Summary:** Trigger Manual Milestone  
**Operation ID:** `trigger_manual_milestone_api_v1_ctms_grants__grant_id__milestones__milestone_id__trigger_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `grant_id` | `path` | `string` | ✔ Yes | — |
| `milestone_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `PaymentMilestoneResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/grants/{grant_id}/milestones/{milestone_id}/trigger', {
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
        '/api/v1/ctms/api/v1/ctms/grants/{grant_id}/milestones/{milestone_id}/trigger',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/grants/{grant_id}/milestones/{milestone_id}/trigger' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/grants/{grant_id}/payables`

**Summary:** List Investigator Payables  
**Operation ID:** `list_investigator_payables_api_v1_ctms_grants__grant_id__payables_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `grant_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<InvestigatorPayableResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/grants/{grant_id}/payables', {
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
        '/api/v1/ctms/api/v1/ctms/grants/{grant_id}/payables',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/grants/{grant_id}/payables' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/ip/destruction-certificates`

**Summary:** Generate Destruction Certificate  
**Operation ID:** `generate_destruction_certificate_api_v1_ctms_ip_destruction_certificates_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `IPDestructionCertificateCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Study ID |
| `site_id` | `string` | ✔ Yes | Site ID |
| `kit_ids` | `Array<string>` | ✔ Yes | List of kit IDs destroyed |
| `destruction_method` | `string` | No | Destruction method |
| `witness_user_id` | `string` | ✔ Yes | User ID of the witness |
| `witness_role` | `string` | ✔ Yes | Role of the witness |
| `pi_signature_hash` | `string` | ✔ Yes | Cryptographic signature hash of the PI |
| `reason_for_destruction` | `string` | ✔ Yes | Clinical rationale for destruction |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `IPDestructionCertificateResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/ip/destruction-certificates', {
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
        '/api/v1/ctms/api/v1/ctms/ip/destruction-certificates',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/ip/destruction-certificates' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/ip/excursions`

**Summary:** Log Temperature Excursion  
**Operation ID:** `log_temperature_excursion_api_v1_ctms_ip_excursions_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `IPTemperatureExcursionCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Study ID |
| `site_id` | `string` | ✔ Yes | Site ID |
| `kit_ids` | `Array<string>` | ✔ Yes | List of affected IP kit record IDs |
| `excursion_type` | `string` | No | Excursion type (TRANSIT, STORAGE) |
| `min_temp_celsius` | `number` | ✔ Yes | Minimum recorded temperature |
| `max_temp_celsius` | `number` | ✔ Yes | Maximum recorded temperature |
| `duration_hours` | `number` | ✔ Yes | Duration in hours outside standard range |
| `occurred_at` | `string` | ✔ Yes | Timestamp when excursion was detected |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `IPTemperatureExcursionResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/ip/excursions', {
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
        '/api/v1/ctms/api/v1/ctms/ip/excursions',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/ip/excursions' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/ip/excursions/{excursion_id}/disposition`

**Summary:** Disposition Temperature Excursion  
**Operation ID:** `disposition_temperature_excursion_api_v1_ctms_ip_excursions__excursion_id__disposition_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `excursion_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `IPTemperatureExcursionDisposition`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `disposition_status` | `string` | ✔ Yes | QA disposition (QA_APPROVED_USE, QA_REJECTED_DESTROY) |
| `qa_rationale` | `string` | ✔ Yes | Justification and stability data reference |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `IPTemperatureExcursionResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/ip/excursions/{excursion_id}/disposition', {
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
        '/api/v1/ctms/api/v1/ctms/ip/excursions/{excursion_id}/disposition',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/ip/excursions/{excursion_id}/disposition' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/ip/kits`

**Summary:** List Ip Kits  
**Operation ID:** `list_ip_kits_api_v1_ctms_ip_kits_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | — |
| `site_id` | `query` | `string` | `null` | No | — |
| `status` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<IPKitRecordResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/ip/kits', {
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
        '/api/v1/ctms/api/v1/ctms/ip/kits',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/ip/kits' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/ip/kits/{kit_id}/dispense`

**Summary:** Dispense Ip Kit  
**Operation ID:** `dispense_ip_kit_api_v1_ctms_ip_kits__kit_id__dispense_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `kit_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `IPKitDispenseRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `subject_id` | `string` | ✔ Yes | Subject ID |
| `visit_id` | `string` | ✔ Yes | Visit ID |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `IPKitRecordResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/ip/kits/{kit_id}/dispense', {
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
        '/api/v1/ctms/api/v1/ctms/ip/kits/{kit_id}/dispense',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/ip/kits/{kit_id}/dispense' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/ip/kits/{kit_id}/reconcile`

**Summary:** Reconcile Returned Kit  
**Operation ID:** `reconcile_returned_kit_api_v1_ctms_ip_kits__kit_id__reconcile_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `kit_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `IPKitReconcileRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `returned_units_count` | `integer` | ✔ Yes | Number of returned units/tablets |
| `expected_units_count` | `integer` | ✔ Yes | Expected returned units/tablets |
| `notes` | `string` | `null` | No | Discrepancy notes |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `IPKitRecordResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/ip/kits/{kit_id}/reconcile', {
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
        '/api/v1/ctms/api/v1/ctms/ip/kits/{kit_id}/reconcile',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/ip/kits/{kit_id}/reconcile' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/ip/shipments/receive`

**Summary:** Receive Ip Shipment  
**Operation ID:** `receive_ip_shipment_api_v1_ctms_ip_shipments_receive_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `IPShipmentReceiveRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Study ID |
| `site_id` | `string` | ✔ Yes | Site ID |
| `kit_numbers` | `Array<string>` | ✔ Yes | List of kit serial identifiers |
| `lot_number` | `string` | ✔ Yes | Manufacturing lot number |
| `kit_type` | `string` | No | Kit type (ACTIVE_DRUG, PLACEBO, COMPARATOR) |
| `shipment_tracking_number` | `string` | ✔ Yes | Courier tracking number |
| `expiration_date` | `string` | ✔ Yes | Expiration date (YYYY-MM-DD) |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `Array<IPKitRecordResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/ip/shipments/receive', {
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
        '/api/v1/ctms/api/v1/ctms/ip/shipments/receive',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/ip/shipments/receive' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/monitoring-visits`

**Summary:** Schedule Monitoring Visit  
**Operation ID:** `schedule_monitoring_visit_api_v1_ctms_monitoring_visits_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `MonitoringVisitCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Study ID associated with the visit |
| `site_id` | `string` | ✔ Yes | Site ID where the monitoring visit occurs |
| `cra_id` | `string` | ✔ Yes | CRA performing the monitoring visit |
| `visit_type` | `string` | ✔ Yes | Type of monitoring visit (e.g. SIV, IMV, COV) |
| `scheduled_date` | `string` | ✔ Yes | Scheduled date/time of the visit |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `MonitoringVisitResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/monitoring-visits', {
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
        '/api/v1/ctms/api/v1/ctms/monitoring-visits',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/monitoring-visits' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/monitoring-visits`

**Summary:** List Monitoring Visits  
**Operation ID:** `list_monitoring_visits_api_v1_ctms_monitoring_visits_get`  

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
| `cra_id` | `query` | `string` | `null` | No | — |
| `status` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<MonitoringVisitResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/monitoring-visits', {
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
        '/api/v1/ctms/api/v1/ctms/monitoring-visits',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/monitoring-visits' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/monitoring-visits/bulk-sync`

**Summary:** Bulk Sync Monitoring Visits  
**Operation ID:** `bulk_sync_monitoring_visits_api_v1_ctms_monitoring_visits_bulk_sync_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `Array<MonitoringVisitOfflineSync>`

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/monitoring-visits/bulk-sync', {
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
        '/api/v1/ctms/api/v1/ctms/monitoring-visits/bulk-sync',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/monitoring-visits/bulk-sync' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/monitoring-visits/sync`

**Summary:** Sync Monitoring Visit  
**Operation ID:** `sync_monitoring_visit_api_v1_ctms_monitoring_visits_sync_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `MonitoringVisitOfflineSync`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `visit_id` | `string` | ✔ Yes | Target visit ID for offline update |
| `study_id` | `string` | `null` | No | Optional study ID |
| `site_id` | `string` | `null` | No | Optional site ID |
| `actual_date` | `string` | ✔ Yes | Actual date/time when the visit was conducted |
| `device_timestamp` | `string` | ✔ Yes | Device timestamp when payload was captured |
| `findings` | `Array<FindingCreate>` | No | List of recorded findings |
| `offline_sync_markers` | `OfflineSyncMarkers` | ✔ Yes | Mandatory offline sync markers |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/monitoring-visits/sync', {
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
        '/api/v1/ctms/api/v1/ctms/monitoring-visits/sync',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/monitoring-visits/sync' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/monitoring-visits/{visit_id}/complete`

**Summary:** Complete Monitoring Visit  
**Operation ID:** `complete_monitoring_visit_api_v1_ctms_monitoring_visits__visit_id__complete_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `visit_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `MonitoringVisitComplete`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `actual_date` | `string` | ✔ Yes | Actual date/time when the visit was conducted |
| `findings` | `Array<FindingCreate>` | No | List of recorded findings |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `MonitoringVisitResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/monitoring-visits/{visit_id}/complete', {
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
        '/api/v1/ctms/api/v1/ctms/monitoring-visits/{visit_id}/complete',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/monitoring-visits/{visit_id}/complete' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/monitoring-visits/{visit_id}/letters`

**Summary:** Get Monitoring Visit Letters  
**Operation ID:** `get_monitoring_visit_letters_api_v1_ctms_monitoring_visits__visit_id__letters_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `visit_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<GeneratedLetterResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/monitoring-visits/{visit_id}/letters', {
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
        '/api/v1/ctms/api/v1/ctms/monitoring-visits/{visit_id}/letters',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/monitoring-visits/{visit_id}/letters' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/monitoring-visits/{visit_id}/letters/{letter_type}`

**Summary:** Get Monitoring Visit Letter By Type  
**Operation ID:** `get_monitoring_visit_letter_by_type_api_v1_ctms_monitoring_visits__visit_id__letters__letter_type__get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `visit_id` | `path` | `string` | ✔ Yes | — |
| `letter_type` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `GeneratedLetterResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/monitoring-visits/{visit_id}/letters/{letter_type}', {
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
        '/api/v1/ctms/api/v1/ctms/monitoring-visits/{visit_id}/letters/{letter_type}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/monitoring-visits/{visit_id}/letters/{letter_type}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/monitoring-visits/{visit_id}/sign-off`

**Summary:** Sign Off Monitoring Visit  
**Operation ID:** `sign_off_monitoring_visit_api_v1_ctms_monitoring_visits__visit_id__sign_off_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `visit_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `MonitoringVisitResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/monitoring-visits/{visit_id}/sign-off', {
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
        '/api/v1/ctms/api/v1/ctms/monitoring-visits/{visit_id}/sign-off',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/monitoring-visits/{visit_id}/sign-off' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/rbqm/kri`

**Summary:** Record Kri Metric  
**Operation ID:** `record_kri_metric_api_v1_ctms_rbqm_kri_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `RBQMKRIMetricCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Study ID |
| `site_id` | `string` | ✔ Yes | Site ID |
| `metric_type` | `string` | ✔ Yes | KRI metric type (QUERY_VELOCITY, SAE_REPORTING_LAG_DAYS, PROTOCOL_DEVIATION_RATE, FORM_ENTRY_LAG_DAYS, SDV_BACKLOG_RATE) |
| `metric_value` | `number` | ✔ Yes | Calculated metric value |
| `threshold_low` | `number` | ✔ Yes | Lower normal threshold |
| `threshold_high` | `number` | ✔ Yes | Upper normal threshold |
| `notes` | `string` | `null` | No | Analytical context or observation notes |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `RBQMKRIMetricResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/rbqm/kri', {
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
        '/api/v1/ctms/api/v1/ctms/rbqm/kri',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/rbqm/kri' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/rbqm/kri`

**Summary:** List Kri Metrics  
**Operation ID:** `list_kri_metrics_api_v1_ctms_rbqm_kri_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | — |
| `site_id` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<RBQMKRIMetricResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/rbqm/kri', {
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
        '/api/v1/ctms/api/v1/ctms/rbqm/kri',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/rbqm/kri' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/rbqm/sites/{site_id}/evaluate-risk`

**Summary:** Evaluate Site Risk Score  
**Operation ID:** `evaluate_site_risk_score_api_v1_ctms_rbqm_sites__site_id__evaluate_risk_post`  

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
| `study_id` | `query` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SiteRiskScoreResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/rbqm/sites/{site_id}/evaluate-risk', {
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
        '/api/v1/ctms/api/v1/ctms/rbqm/sites/{site_id}/evaluate-risk',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/rbqm/sites/{site_id}/evaluate-risk' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/rbqm/sites/{site_id}/risk-score`

**Summary:** Get Site Risk Score  
**Operation ID:** `get_site_risk_score_api_v1_ctms_rbqm_sites__site_id__risk_score_get`  

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
| `200` | Successful Response | `SiteRiskScoreResponse` | `null` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/rbqm/sites/{site_id}/risk-score', {
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
        '/api/v1/ctms/api/v1/ctms/rbqm/sites/{site_id}/risk-score',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/rbqm/sites/{site_id}/risk-score' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/recruitment`

**Summary:** Record Recruitment  
**Operation ID:** `record_recruitment_api_v1_ctms_recruitment_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `RecruitmentRecordCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `site_id` | `string` | ✔ Yes | Site ID being tracked |
| `study_id` | `string` | ✔ Yes | Study ID associated with the site |
| `screened_count` | `integer` | No | Total number of screened subjects |
| `enrolled_count` | `integer` | No | Total number of enrolled subjects |
| `target_count` | `integer` | No | Target enrollment count |
| `as_of_date` | `string` | `null` | No | The date/time as of which metrics apply |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `RecruitmentRecordResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/recruitment', {
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
        '/api/v1/ctms/api/v1/ctms/recruitment',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/recruitment' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/recruitment`

**Summary:** List Recruitment Records  
**Operation ID:** `list_recruitment_records_api_v1_ctms_recruitment_get`  

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

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<RecruitmentRecordResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/recruitment', {
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
        '/api/v1/ctms/api/v1/ctms/recruitment',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/recruitment' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/resupply-events`

**Summary:** List Ctms Resupply Events  
**Operation ID:** `list_ctms_resupply_events_api_v1_ctms_resupply_events_get`  

**Clinical & GxP Rationale:**  
List pending/all resupply events from downstream execution, applying site boundaries and manager-level role checks.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | — |
| `site_id` | `query` | `string` | `null` | No | — |
| `status` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<CTMSResupplyEventResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/resupply-events', {
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
        '/api/v1/ctms/api/v1/ctms/resupply-events',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/resupply-events' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/resupply-events/{event_id}/confirm`

**Summary:** Approve Ctms Resupply Event  
**Operation ID:** `approve_ctms_resupply_event_api_v1_ctms_resupply_events__event_id__confirm_post`  

**Clinical & GxP Rationale:**  
Approve resupply event, securely signing and delegating downstream.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `event_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `CTMSResupplyApprovalRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `change_justification` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `CTMSResupplyEventResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/resupply-events/{event_id}/confirm', {
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
        '/api/v1/ctms/api/v1/ctms/resupply-events/{event_id}/confirm',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/resupply-events/{event_id}/confirm' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/resupply-events/{event_id}/reject`

**Summary:** Reject Ctms Resupply Event  
**Operation ID:** `reject_ctms_resupply_event_api_v1_ctms_resupply_events__event_id__reject_post`  

**Clinical & GxP Rationale:**  
Reject resupply event, securely signing and delegating downstream.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `event_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `CTMSResupplyApprovalRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `change_justification` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `CTMSResupplyEventResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/resupply-events/{event_id}/reject', {
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
        '/api/v1/ctms/api/v1/ctms/resupply-events/{event_id}/reject',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/resupply-events/{event_id}/reject' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/site-milestones`

**Summary:** Create Site Milestone  
**Operation ID:** `create_site_milestone_api_v1_ctms_site_milestones_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `SiteMilestoneCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `site_id` | `string` | ✔ Yes | Site ID |
| `study_id` | `string` | ✔ Yes | Study ID |
| `milestone_type` | `string` | ✔ Yes | The type of milestone |
| `planned_date` | `string` | `null` | No | Planned milestone date |
| `actual_date` | `string` | `null` | No | Actual milestone date |
| `status` | `string` | `null` | No | Status of the milestone |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `SiteMilestoneResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/site-milestones', {
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
        '/api/v1/ctms/api/v1/ctms/site-milestones',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/site-milestones' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/site-milestones`

**Summary:** List Site Milestones  
**Operation ID:** `list_site_milestones_api_v1_ctms_site_milestones_get`  

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

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<SiteMilestoneResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/site-milestones', {
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
        '/api/v1/ctms/api/v1/ctms/site-milestones',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/site-milestones' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/ctms/site-milestones/{milestone_id}`

**Summary:** Update Site Milestone  
**Operation ID:** `update_site_milestone_api_v1_ctms_site_milestones__milestone_id__put`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `milestone_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SiteMilestoneUpdate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `planned_date` | `string` | `null` | No | Planned milestone date |
| `actual_date` | `string` | `null` | No | Actual milestone date |
| `status` | `string` | `null` | No | Status of the milestone |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SiteMilestoneResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/site-milestones/{milestone_id}', {
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
        '/api/v1/ctms/api/v1/ctms/site-milestones/{milestone_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/ctms/api/v1/ctms/site-milestones/{milestone_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/startup/documents`

**Summary:** Submit Essential Document  
**Operation ID:** `submit_essential_document_api_v1_ctms_startup_documents_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `EssentialDocumentCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Clinical study ID |
| `site_id` | `string` | ✔ Yes | Site ID |
| `document_type` | `string` | ✔ Yes | Type of document (e.g. FDA_1572, SITE_CONTRACT, LOCAL_IRB_APPROVAL, GCP_CERTIFICATE) |
| `file_name` | `string` | ✔ Yes | Filename of the uploaded document |
| `file_hash` | `string` | ✔ Yes | SHA-256 hash of the uploaded document |
| `expiration_date` | `string` | `null` | No | Document expiration date |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `EssentialDocumentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/startup/documents', {
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
        '/api/v1/ctms/api/v1/ctms/startup/documents',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/startup/documents' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/startup/documents`

**Summary:** List Essential Documents  
**Operation ID:** `list_essential_documents_api_v1_ctms_startup_documents_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | — |
| `site_id` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<EssentialDocumentResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/startup/documents', {
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
        '/api/v1/ctms/api/v1/ctms/startup/documents',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/startup/documents' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/startup/documents/{document_id}/review`

**Summary:** Review Essential Document  
**Operation ID:** `review_essential_document_api_v1_ctms_startup_documents__document_id__review_post`  

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

**Schema Type:** `EssentialDocumentReview`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `status` | `string` | ✔ Yes | Review disposition (APPROVED, REJECTED, EXPIRED) |
| `review_notes` | `string` | `null` | No | Reviewer feedback or rationale |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `EssentialDocumentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/startup/documents/{document_id}/review', {
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
        '/api/v1/ctms/api/v1/ctms/startup/documents/{document_id}/review',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/startup/documents/{document_id}/review' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/ctms/startup/documents/{document_id}/review`

**Summary:** Review Essential Document  
**Operation ID:** `review_essential_document_api_v1_ctms_startup_documents__document_id__review_put`  

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

**Schema Type:** `EssentialDocumentReview`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `status` | `string` | ✔ Yes | Review disposition (APPROVED, REJECTED, EXPIRED) |
| `review_notes` | `string` | `null` | No | Reviewer feedback or rationale |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `EssentialDocumentResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/startup/documents/{document_id}/review', {
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
        '/api/v1/ctms/api/v1/ctms/startup/documents/{document_id}/review',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/ctms/api/v1/ctms/startup/documents/{document_id}/review' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/startup/milestones`

**Summary:** Create Or Update Country Milestone  
**Operation ID:** `create_or_update_country_milestone_api_v1_ctms_startup_milestones_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CountryMilestoneCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Clinical study ID |
| `country_code` | `string` | ✔ Yes | ISO 2-letter or 3-letter country code |
| `milestone_type` | `string` | ✔ Yes | Milestone type (e.g. CTA_SUBMISSION, CTA_APPROVAL, ETHICS_APPROVAL) |
| `status` | `string` | No | Status (PLANNED, IN_PROGRESS, APPROVED, REJECTED) |
| `planned_date` | `string` | `null` | No | Planned date ISO string |
| `actual_date` | `string` | `null` | No | Actual date ISO string |
| `approval_number` | `string` | `null` | No | Regulatory authority approval identifier |
| `authority_name` | `string` | `null` | No | Name of the competent authority / ethics body |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `CountryMilestoneResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/startup/milestones', {
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
        '/api/v1/ctms/api/v1/ctms/startup/milestones',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/startup/milestones' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/startup/milestones`

**Summary:** List Country Milestones  
**Operation ID:** `list_country_milestones_api_v1_ctms_startup_milestones_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | — |
| `country_code` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<CountryMilestoneResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/startup/milestones', {
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
        '/api/v1/ctms/api/v1/ctms/startup/milestones',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/startup/milestones' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/startup/rules`

**Summary:** Create Or Update Country Prerequisite Rule  
**Operation ID:** `create_or_update_country_prerequisite_rule_api_v1_ctms_startup_rules_post`  

**Clinical & GxP Rationale:**  
Configures a country-specific prerequisite criterion for site activation.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CountryPrerequisiteRuleCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | — |
| `country_code` | `string` | ✔ Yes | — |
| `facet` | `string` | ✔ Yes | — |
| `criterion_code` | `string` | ✔ Yes | — |
| `criterion_name` | `string` | ✔ Yes | — |
| `is_mandatory` | `boolean` | No | — |
| `description` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `CountryPrerequisiteRuleResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/startup/rules', {
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
        '/api/v1/ctms/api/v1/ctms/startup/rules',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/startup/rules' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/startup/rules`

**Summary:** List Country Prerequisite Rules  
**Operation ID:** `list_country_prerequisite_rules_api_v1_ctms_startup_rules_get`  

**Clinical & GxP Rationale:**  
Retrieves country-specific prerequisite rules matrix for a study.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | ✔ Yes | — |
| `country_code` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<CountryPrerequisiteRuleResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/startup/rules', {
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
        '/api/v1/ctms/api/v1/ctms/startup/rules',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/startup/rules' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/startup/rules/presets`

**Summary:** Get Jurisdiction Rule Presets  
**Operation ID:** `get_jurisdiction_rule_presets_api_v1_ctms_startup_rules_presets_get`  

**Clinical & GxP Rationale:**  
Returns statutory startup prerequisite rule presets for US, EU, JP, UK, and Global jurisdictions.

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
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/startup/rules/presets', {
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
        '/api/v1/ctms/api/v1/ctms/startup/rules/presets',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/startup/rules/presets' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/startup/sites/{site_id}/greenlight`

**Summary:** Evaluate Site Greenlight  
**Operation ID:** `evaluate_site_greenlight_api_v1_ctms_startup_sites__site_id__greenlight_get`  

**Clinical & GxP Rationale:**  
Evaluates real-time site activation readiness across the 6 key operational facets.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `site_id` | `path` | `string` | ✔ Yes | — |
| `study_id` | `query` | `string` | ✔ Yes | — |
| `country_code` | `query` | `string` | No | ISO-2 Country code or jurisdiction (e.g. US, EU, JP, GB) |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SiteGreenlightGateResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/startup/sites/{site_id}/greenlight', {
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
        '/api/v1/ctms/api/v1/ctms/startup/sites/{site_id}/greenlight',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/startup/sites/{site_id}/greenlight' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/startup/sites/{site_id}/greenlight/certify`

**Summary:** Certify Site Greenlight  
**Operation ID:** `certify_site_greenlight_api_v1_ctms_startup_sites__site_id__greenlight_certify_post`  

**Clinical & GxP Rationale:**  
Formally certifies greenlight activation, issuing an HMAC-SHA256 token and un-gating trial execution.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `site_id` | `path` | `string` | ✔ Yes | — |
| `study_id` | `query` | `string` | ✔ Yes | — |
| `country_code` | `query` | `string` | No | ISO-2 Country code or jurisdiction |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SiteGreenlightGateResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/startup/sites/{site_id}/greenlight/certify', {
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
        '/api/v1/ctms/api/v1/ctms/startup/sites/{site_id}/greenlight/certify',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/startup/sites/{site_id}/greenlight/certify' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/startup/sites/{site_id}/greenlight/override`

**Summary:** Override Site Greenlight  
**Operation ID:** `override_site_greenlight_api_v1_ctms_startup_sites__site_id__greenlight_override_post`  

**Clinical & GxP Rationale:**  
Executes dual-signature executive sponsor GxP override with mandatory Part 11 justification logging.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `site_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SiteGreenlightOverrideRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | — |
| `sponsor_user_id` | `string` | ✔ Yes | — |
| `medical_user_id` | `string` | ✔ Yes | — |
| `sponsor_signature` | `string` | ✔ Yes | — |
| `medical_signature` | `string` | ✔ Yes | — |
| `justification` | `string` | ✔ Yes | — |
| `country_code` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SiteGreenlightGateResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/startup/sites/{site_id}/greenlight/override', {
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
        '/api/v1/ctms/api/v1/ctms/startup/sites/{site_id}/greenlight/override',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/startup/sites/{site_id}/greenlight/override' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/startup/sites/{site_id}/greenlight/verify-token`

**Summary:** Verify Site Greenlight Token Endpoint  
**Operation ID:** `verify_site_greenlight_token_endpoint_api_v1_ctms_startup_sites__site_id__greenlight_verify_token_post`  

**Clinical & GxP Rationale:**  
Verifies cryptographic validity and binding of a Site Greenlight Activation Token.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `site_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `GreenlightTokenVerifyRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `token` | `string` | ✔ Yes | — |
| `site_id` | `string` | `null` | No | — |
| `study_id` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `GreenlightTokenVerifyResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/startup/sites/{site_id}/greenlight/verify-token', {
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
        '/api/v1/ctms/api/v1/ctms/startup/sites/{site_id}/greenlight/verify-token',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/startup/sites/{site_id}/greenlight/verify-token' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/ctms/studies`

**Summary:** List Studies  
**Operation ID:** `list_studies_api_v1_ctms_studies_get`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<CTMSStudyResponse>` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/studies', {
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
        '/api/v1/ctms/api/v1/ctms/studies',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/api/v1/ctms/studies' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/ctms/studies`

**Summary:** Create Study  
**Operation ID:** `create_study_api_v1_ctms_studies_post`  

**Clinical & GxP Rationale:**  
No detailed description provided.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CTMSStudyCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Unique study ID |
| `name` | `string` | ✔ Yes | Descriptive study title/name |
| `status` | `string` | `null` | No | Initial study status |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `CTMSStudyResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/ctms/api/v1/ctms/studies', {
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
        '/api/v1/ctms/api/v1/ctms/studies',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/ctms/api/v1/ctms/studies' \
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
const response = await fetch('http://localhost:8000/api/v1/ctms/health', {
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
        '/api/v1/ctms/health',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/ctms/health' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---
