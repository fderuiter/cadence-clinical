# Site Payments & Participant Wallet

> **Functional Domain:** `Clinical Operations & Execution`  
> **Service Host Port:** `localhost:8014`  
> **Gateway Proxy Prefix:** `/api/v1/payments`  
> **Persistence Backend:** `PostgreSQL / SQLite`  

## Overview

Investigator grant contracts, procedure-fee rules, multi-payee splits, closeout holdbacks, participant expense OCR, and self-billing invoices.

---

## Endpoints Summary

| Method | Path | Summary | GxP Auth & Signatures |
| :--- | :--- | :--- | :--- |
| `GET` | [/api/v1/payments/audit-logs](#get-api-v1-payments-audit-logs) | List Payment Audit Logs | 👀 Read-Only |
| `POST` | [/api/v1/payments/contracts](#post-api-v1-payments-contracts) | Create Budget Contract | ✍️ Audited Write |
| `GET` | [/api/v1/payments/contracts](#get-api-v1-payments-contracts) | List Budget Contracts | 👀 Read-Only |
| `GET` | [/api/v1/payments/contracts/{contract_id}](#get-api-v1-payments-contracts-contract_id) | Get Budget Contract | 👀 Read-Only |
| `POST` | [/api/v1/payments/contracts/{contract_id}/fee-schedules](#post-api-v1-payments-contracts-contract_id-fee-schedules) | Add Fee Schedule | ✍️ Audited Write |
| `GET` | [/api/v1/payments/contracts/{contract_id}/fee-schedules](#get-api-v1-payments-contracts-contract_id-fee-schedules) | List Fee Schedules | 👀 Read-Only |
| `POST` | [/api/v1/payments/contracts/{contract_id}/split-payees](#post-api-v1-payments-contracts-contract_id-split-payees) | Configure Split Payees | ✍️ Audited Write |
| `GET` | [/api/v1/payments/contracts/{contract_id}/split-payees](#get-api-v1-payments-contracts-contract_id-split-payees) | List Split Payees | 👀 Read-Only |
| `POST` | [/api/v1/payments/erp/webhook](#post-api-v1-payments-erp-webhook) | Process Erp Webhook | ✍️ Audited Write |
| `POST` | [/api/v1/payments/events/batch](#post-api-v1-payments-events-batch) | Ingest Batch Events | ✍️ Audited Write |
| `POST` | [/api/v1/payments/events/cra-sdv-verification](#post-api-v1-payments-events-cra-sdv-verification) | Ingest Cra Sdv Verification Event | ✍️ Audited Write |
| `POST` | [/api/v1/payments/events/ecrf-submission](#post-api-v1-payments-events-ecrf-submission) | Ingest Ecrf Submission Event | ✍️ Audited Write |
| `POST` | [/api/v1/payments/events/milestone-completion](#post-api-v1-payments-events-milestone-completion) | Ingest Milestone Completion Event | ✍️ Audited Write |
| `GET` | [/api/v1/payments/holdbacks](#get-api-v1-payments-holdbacks) | List Holdback Reserves | 👀 Read-Only |
| `POST` | [/api/v1/payments/holdbacks/release](#post-api-v1-payments-holdbacks-release) | Release Holdback Reserves | ✍️ Audited Write |
| `GET` | [/api/v1/payments/invoices](#get-api-v1-payments-invoices) | List Invoices | 👀 Read-Only |
| `POST` | [/api/v1/payments/invoices/compile](#post-api-v1-payments-invoices-compile) | Compile Billing Cycle | ✍️ Audited Write |
| `GET` | [/api/v1/payments/invoices/{invoice_id}](#get-api-v1-payments-invoices-invoice_id) | Get Invoice | 👀 Read-Only |
| `POST` | [/api/v1/payments/invoices/{invoice_id}/approve](#post-api-v1-payments-invoices-invoice_id-approve) | Approve Self Billing Invoice | ✍️ Audited Write |
| `POST` | [/api/v1/payments/invoices/{invoice_id}/erp-export](#post-api-v1-payments-invoices-invoice_id-erp-export) | Export Invoice To Erp | ✍️ Audited Write |
| `GET` | [/api/v1/payments/invoices/{invoice_id}/erp-logs](#get-api-v1-payments-invoices-invoice_id-erp-logs) | List Erp Sync Logs | 👀 Read-Only |
| `POST` | [/api/v1/payments/invoices/{invoice_id}/reconcile](#post-api-v1-payments-invoices-invoice_id-reconcile) | Reconcile Invoice Payment | ✍️ Audited Write |
| `GET` | [/api/v1/payments/invoices/{invoice_id}/remittance-advice](#get-api-v1-payments-invoices-invoice_id-remittance-advice) | Get Remittance Advice | 👀 Read-Only |
| `POST` | [/api/v1/payments/invoices/{invoice_id}/remittance-advice/dispatch](#post-api-v1-payments-invoices-invoice_id-remittance-advice-dispatch) | Dispatch Remittance Advice | ✍️ Audited Write |
| `GET` | [/api/v1/payments/invoices/{invoice_id}/remittance-advice/pdf](#get-api-v1-payments-invoices-invoice_id-remittance-advice-pdf) | Download Remittance Advice Pdf | 👀 Read-Only |
| `POST` | [/api/v1/payments/invoices/{invoice_id}/remittance-advice/sign](#post-api-v1-payments-invoices-invoice_id-remittance-advice-sign) | Sign Remittance Advice | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/payments/invoices/{invoice_id}/remittance-advice/verify-signature](#post-api-v1-payments-invoices-invoice_id-remittance-advice-verify-signature) | Verify Remittance Advice Signature | 🔒 Step-Up Sig Token |
| `POST` | [/api/v1/payments/lay-summaries](#post-api-v1-payments-lay-summaries) | Create Lay Summary | ✍️ Audited Write |
| `GET` | [/api/v1/payments/lay-summaries](#get-api-v1-payments-lay-summaries) | List Lay Summaries | 👀 Read-Only |
| `GET` | [/api/v1/payments/lay-summaries/{summary_id}](#get-api-v1-payments-lay-summaries-summary_id) | Get Lay Summary | 👀 Read-Only |
| `POST` | [/api/v1/payments/lay-summaries/{summary_id}/confirm-receipt](#post-api-v1-payments-lay-summaries-summary_id-confirm-receipt) | Confirm Lay Summary Receipt | ✍️ Audited Write |
| `POST` | [/api/v1/payments/lay-summaries/{summary_id}/publish](#post-api-v1-payments-lay-summaries-summary_id-publish) | Publish Lay Summary | ✍️ Audited Write |
| `GET` | [/api/v1/payments/lay-summaries/{summary_id}/receipts](#get-api-v1-payments-lay-summaries-summary_id-receipts) | List Lay Summary Receipts | 👀 Read-Only |
| `GET` | [/api/v1/payments/participant/disbursements](#get-api-v1-payments-participant-disbursements) | List Participant Disbursements | 👀 Read-Only |
| `POST` | [/api/v1/payments/participant/events/visit-verified](#post-api-v1-payments-participant-events-visit-verified) | Ingest Visit Verified Event | ✍️ Audited Write |
| `POST` | [/api/v1/payments/participant/expenses](#post-api-v1-payments-participant-expenses) | Submit Participant Expense Claim | ✍️ Audited Write |
| `GET` | [/api/v1/payments/participant/expenses](#get-api-v1-payments-participant-expenses) | List Participant Expense Claims | 👀 Read-Only |
| `POST` | [/api/v1/payments/participant/expenses/ocr-extract](#post-api-v1-payments-participant-expenses-ocr-extract) | Extract Receipt Ocr | ✍️ Audited Write |
| `POST` | [/api/v1/payments/participant/expenses/{claim_id}/review](#post-api-v1-payments-participant-expenses-claim_id-review) | Review Participant Expense Claim | ✍️ Audited Write |
| `POST` | [/api/v1/payments/participant/stipend-rules](#post-api-v1-payments-participant-stipend-rules) | Create Participant Stipend Rule | ✍️ Audited Write |
| `GET` | [/api/v1/payments/participant/stipend-rules](#get-api-v1-payments-participant-stipend-rules) | List Participant Stipend Rules | 👀 Read-Only |
| `POST` | [/api/v1/payments/participant/wallets](#post-api-v1-payments-participant-wallets) | Get Or Create Participant Wallet | ✍️ Audited Write |
| `GET` | [/api/v1/payments/participant/wallets](#get-api-v1-payments-participant-wallets) | List Participant Wallets | 👀 Read-Only |
| `GET` | [/api/v1/payments/participant/wallets/subject/{study_id}/{subject_id}](#get-api-v1-payments-participant-wallets-subject-study_id-subject_id) | Get Participant Wallet By Subject | 👀 Read-Only |
| `GET` | [/api/v1/payments/participant/wallets/{wallet_id}](#get-api-v1-payments-participant-wallets-wallet_id) | Get Participant Wallet By Id | 👀 Read-Only |
| `GET` | [/api/v1/payments/participant/wallets/{wallet_id}/balance](#get-api-v1-payments-participant-wallets-wallet_id-balance) | Get Participant Balance Inquiry | 👀 Read-Only |
| `POST` | [/api/v1/payments/participant/wallets/{wallet_id}/disburse](#post-api-v1-payments-participant-wallets-wallet_id-disburse) | Disburse Participant Funds | ✍️ Audited Write |
| `POST` | [/api/v1/payments/participant/wallets/{wallet_id}/issue-card](#post-api-v1-payments-participant-wallets-wallet_id-issue-card) | Issue Participant Reloadable Card | ✍️ Audited Write |
| `PUT` | [/api/v1/payments/participant/wallets/{wallet_id}/payout-methods](#put-api-v1-payments-participant-wallets-wallet_id-payout-methods) | Update Participant Wallet Payout Methods | ✍️ Audited Write |
| `POST` | [/api/v1/payments/participant/wallets/{wallet_id}/submit-w9](#post-api-v1-payments-participant-wallets-wallet_id-submit-w9) | Submit Participant W9 Form | ✍️ Audited Write |
| `GET` | [/api/v1/payments/participant/wallets/{wallet_id}/tax-1099-status](#get-api-v1-payments-participant-wallets-wallet_id-tax-1099-status) | Get Participant 1099 Tax Status | 👀 Read-Only |
| `GET` | [/api/v1/payments/participant/wallets/{wallet_id}/transactions](#get-api-v1-payments-participant-wallets-wallet_id-transactions) | List Wallet Transactions | 👀 Read-Only |
| `POST` | [/api/v1/payments/participant/wallets/{wallet_id}/verify-w9](#post-api-v1-payments-participant-wallets-wallet_id-verify-w9) | Verify Participant W9 Form | ✍️ Audited Write |
| `GET` | [/api/v1/payments/payables](#get-api-v1-payments-payables) | List Payables | 👀 Read-Only |
| `GET` | [/api/v1/payments/payables/{payable_id}](#get-api-v1-payments-payables-payable_id) | Get Payable | 👀 Read-Only |
| `POST` | [/api/v1/payments/payables/{payable_id}/approve](#post-api-v1-payments-payables-payable_id-approve) | Approve Payable | ✍️ Audited Write |
| `GET` | [/health](#get-health) | Health Check | 👀 Read-Only |

---

## Granular Endpoint Specifications

### `GET` `/api/v1/payments/audit-logs`

**Summary:** List Payment Audit Logs  
**Operation ID:** `list_payment_audit_logs_api_v1_payments_audit_logs_get`  

**Clinical & GxP Rationale:**  
Queries immutable 21 CFR Part 11 payment transaction audit logs.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `entity_type` | `query` | `string` | `null` | No | — |
| `entity_id` | `query` | `string` | `null` | No | — |
| `user_id` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<PaymentAuditLogResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/audit-logs', {
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
        '/api/v1/payments/api/v1/payments/audit-logs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/payments/api/v1/payments/audit-logs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/contracts`

**Summary:** Create Budget Contract  
**Operation ID:** `create_budget_contract_api_v1_payments_contracts_post`  

**Clinical & GxP Rationale:**  
Creates or versions an active investigator grant contract schedule.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `BudgetContractCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Target Study Protocol UUID or Identifier |
| `site_id` | `string` | ✔ Yes | Target Trial Site UUID or Identifier |
| `contract_name` | `string` | ✔ Yes | Descriptive contract schedule title |
| `tier_level` | `string` | No | Contract tier level (TIER_1_ACADEMIC, TIER_2_COMMUNITY, TIER_3_GLOBAL) |
| `currency` | `string` | No | ISO-4217 Currency code |
| `total_budget` | `number` | No | Total contracted grant budget cap |
| `overhead_percentage` | `number` | No | Default institutional overhead percentage |
| `withholding_percentage` | `number` | No | Default holdback retention reserve percentage |
| `effective_date` | `string` | `null` | No | Contract effective execution date |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `BudgetContractResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/contracts', {
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
        '/api/v1/payments/api/v1/payments/contracts',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/contracts' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/payments/contracts`

**Summary:** List Budget Contracts  
**Operation ID:** `list_budget_contracts_api_v1_payments_contracts_get`  

**Clinical & GxP Rationale:**  
Lists budget contracts matching filters.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | — |
| `site_id` | `query` | `string` | `null` | No | — |
| `is_active_only` | `query` | `boolean` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<BudgetContractResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/contracts', {
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
        '/api/v1/payments/api/v1/payments/contracts',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/payments/api/v1/payments/contracts' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/payments/contracts/{contract_id}`

**Summary:** Get Budget Contract  
**Operation ID:** `get_budget_contract_api_v1_payments_contracts__contract_id__get`  

**Clinical & GxP Rationale:**  
Retrieves a single budget contract by ID.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `contract_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `BudgetContractResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/contracts/{contract_id}', {
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
        '/api/v1/payments/api/v1/payments/contracts/{contract_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/payments/api/v1/payments/contracts/{contract_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/contracts/{contract_id}/fee-schedules`

**Summary:** Add Fee Schedule  
**Operation ID:** `add_fee_schedule_api_v1_payments_contracts__contract_id__fee_schedules_post`  

**Clinical & GxP Rationale:**  
Adds a procedure reimbursement rate item to the contract fee schedule.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `contract_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `FeeScheduleCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `visit_code` | `string` | ✔ Yes | Visit name or identifier (e.g. SCREENING, CYCLE_1_DAY_1) |
| `activity_id` | `string` | `null` | No | CDISC USDM Activity UUID |
| `form_id` | `string` | `null` | No | Linked eCRF Form UUID or ID |
| `procedure_code` | `string` | ✔ Yes | Procedure code (e.g. PROC-ECG-01) |
| `procedure_name` | `string` | ✔ Yes | Human readable procedure title |
| `base_amount` | `number` | ✔ Yes | Base reimbursement fee |
| `overhead_percentage` | `number` | `null` | No | Override institutional overhead % (inherits from contract if omitted) |
| `withholding_percentage` | `number` | `null` | No | Override withholding holdback % (inherits from contract if omitted) |
| `patient_stipend` | `number` | No | Patient reimbursement stipend |
| `travel_reimbursement` | `number` | No | Travel reimbursement allowance |
| `requires_sdv` | `boolean` | No | True if CRA SDV verification gates payment |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `FeeScheduleResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/contracts/{contract_id}/fee-schedules', {
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
        '/api/v1/payments/api/v1/payments/contracts/{contract_id}/fee-schedules',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/contracts/{contract_id}/fee-schedules' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/payments/contracts/{contract_id}/fee-schedules`

**Summary:** List Fee Schedules  
**Operation ID:** `list_fee_schedules_api_v1_payments_contracts__contract_id__fee_schedules_get`  

**Clinical & GxP Rationale:**  
Lists procedure fee schedules configured for a contract.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `contract_id` | `path` | `string` | ✔ Yes | — |
| `visit_code` | `query` | `string` | `null` | No | — |
| `form_id` | `query` | `string` | `null` | No | — |
| `is_active_only` | `query` | `boolean` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<FeeScheduleResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/contracts/{contract_id}/fee-schedules', {
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
        '/api/v1/payments/api/v1/payments/contracts/{contract_id}/fee-schedules',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/payments/api/v1/payments/contracts/{contract_id}/fee-schedules' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/contracts/{contract_id}/split-payees`

**Summary:** Configure Split Payees  
**Operation ID:** `configure_split_payees_api_v1_payments_contracts__contract_id__split_payees_post`  

**Clinical & GxP Rationale:**  
Configures multi-party split payee allocation percentages (must sum to 100%).

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `contract_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `SplitPayeeConfigBulkUpdate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `payees` | `Array<SplitPayeeConfigItem>` | ✔ Yes | List of recipient payees (must sum to 100%) |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `Array<SplitPayeeConfigResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/contracts/{contract_id}/split-payees', {
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
        '/api/v1/payments/api/v1/payments/contracts/{contract_id}/split-payees',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/contracts/{contract_id}/split-payees' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/payments/contracts/{contract_id}/split-payees`

**Summary:** List Split Payees  
**Operation ID:** `list_split_payees_api_v1_payments_contracts__contract_id__split_payees_get`  

**Clinical & GxP Rationale:**  
Lists configured split payee shares for a contract.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `contract_id` | `path` | `string` | ✔ Yes | — |
| `is_active_only` | `query` | `boolean` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<SplitPayeeConfigResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/contracts/{contract_id}/split-payees', {
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
        '/api/v1/payments/api/v1/payments/contracts/{contract_id}/split-payees',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/payments/api/v1/payments/contracts/{contract_id}/split-payees' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/erp/webhook`

**Summary:** Process Erp Webhook  
**Operation ID:** `process_erp_webhook_api_v1_payments_erp_webhook_post`  

**Clinical & GxP Rationale:**  
Inbound enterprise ERP webhook receiver for payment execution and reconciliation status updates.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ErpWebhookPayload`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `erp_system` | `string` | No | Source ERP system (SAP, NETSUITE, ORACLE_FINANCIALS) |
| `invoice_number` | `string` | `null` | No | Cadence invoice number |
| `invoice_id` | `string` | `null` | No | Cadence invoice UUID |
| `erp_reference_id` | `string` | ✔ Yes | ERP Document Number / Transaction ID |
| `status` | `string` | ✔ Yes | ERP execution status (PENDING, APPROVED, PAID, RECONCILED) |
| `payment_date` | `string` | `null` | No | Bank settlement date (YYYY-MM-DD) |
| `amount_paid` | `number` | `null` | No | Amount disbursed by ERP |
| `currency` | `string` | No | Currency of payment |
| `bank_reference` | `string` | `null` | No | Bank wire reference or clearing check number |
| `notes` | `string` | `null` | No | ERP reconciliation processing notes |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ErpWebhookResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/erp/webhook', {
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
        '/api/v1/payments/api/v1/payments/erp/webhook',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/erp/webhook' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/events/batch`

**Summary:** Ingest Batch Events  
**Operation ID:** `ingest_batch_events_api_v1_payments_events_batch_post`  

**Clinical & GxP Rationale:**  
Ingests batch of mixed clinical financial events.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `BatchEventIngestDto`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `ecrf_submissions` | `Array<EcrfSubmissionEventDto>` | No | — |
| `sdv_verifications` | `Array<CraSdvVerificationEventDto>` | No | — |
| `milestones` | `Array<MilestoneCompletionEventDto>` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/events/batch', {
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
        '/api/v1/payments/api/v1/payments/events/batch',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/events/batch' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/events/cra-sdv-verification`

**Summary:** Ingest Cra Sdv Verification Event  
**Operation ID:** `ingest_cra_sdv_verification_event_api_v1_payments_events_cra_sdv_verification_post`  

**Clinical & GxP Rationale:**  
Real-time event listener ingesting CRA SDV verification to unlock payables.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `CraSdvVerificationEventDto`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | — |
| `site_id` | `string` | ✔ Yes | — |
| `subject_id` | `string` | ✔ Yes | — |
| `visit_code` | `string` | ✔ Yes | — |
| `form_id` | `string` | `null` | No | — |
| `verified_by` | `string` | ✔ Yes | CRA User ID performing the verification |
| `event_id` | `string` | ✔ Yes | Unique event UUID for deterministic idempotency |
| `timestamp` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `PayableResponse` | `null` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/events/cra-sdv-verification', {
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
        '/api/v1/payments/api/v1/payments/events/cra-sdv-verification',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/events/cra-sdv-verification' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/events/ecrf-submission`

**Summary:** Ingest Ecrf Submission Event  
**Operation ID:** `ingest_ecrf_submission_event_api_v1_payments_events_ecrf_submission_post`  

**Clinical & GxP Rationale:**  
Real-time event listener ingesting eCRF submission to trigger payables.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `EcrfSubmissionEventDto`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | — |
| `site_id` | `string` | ✔ Yes | — |
| `subject_id` | `string` | ✔ Yes | — |
| `visit_code` | `string` | ✔ Yes | — |
| `form_id` | `string` | ✔ Yes | — |
| `event_id` | `string` | ✔ Yes | Unique event UUID for deterministic idempotency |
| `timestamp` | `string` | `null` | No | — |
| `actor_id` | `string` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `Array<PayableResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/events/ecrf-submission', {
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
        '/api/v1/payments/api/v1/payments/events/ecrf-submission',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/events/ecrf-submission' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/events/milestone-completion`

**Summary:** Ingest Milestone Completion Event  
**Operation ID:** `ingest_milestone_completion_event_api_v1_payments_events_milestone_completion_post`  

**Clinical & GxP Rationale:**  
Real-time event listener ingesting CTMS milestone completion.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `MilestoneCompletionEventDto`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | — |
| `site_id` | `string` | ✔ Yes | — |
| `milestone_code` | `string` | ✔ Yes | — |
| `milestone_name` | `string` | ✔ Yes | — |
| `amount` | `number` | ✔ Yes | — |
| `verified_by` | `string` | ✔ Yes | — |
| `event_id` | `string` | ✔ Yes | Unique event UUID for deterministic idempotency |
| `timestamp` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `PayableResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/events/milestone-completion', {
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
        '/api/v1/payments/api/v1/payments/events/milestone-completion',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/events/milestone-completion' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/payments/holdbacks`

**Summary:** List Holdback Reserves  
**Operation ID:** `list_holdback_reserves_api_v1_payments_holdbacks_get`  

**Clinical & GxP Rationale:**  
Lists 10% holdback retention reserves.

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
| `200` | Successful Response | `Array<HoldbackReserveResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/holdbacks', {
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
        '/api/v1/payments/api/v1/payments/holdbacks',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/payments/api/v1/payments/holdbacks' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/holdbacks/release`

**Summary:** Release Holdback Reserves  
**Operation ID:** `release_holdback_reserves_api_v1_payments_holdbacks_release_post`  

**Clinical & GxP Rationale:**  
Releases holdback retention reserves with dual DM and Quality oversight sign-offs.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `HoldbackReleaseRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | — |
| `site_id` | `string` | ✔ Yes | — |
| `release_reason` | `string` | ✔ Yes | Clinical justification (e.g. Study closeout & query resolution) |
| `dm_signoff_by` | `string` | ✔ Yes | Data Management Lead electronic signature ID |
| `quality_signoff_by` | `string` | ✔ Yes | Quality Oversight electronic signature ID |
| `holdback_ids` | `Array<string>` | `null` | No | Optional specific holdback IDs to release (releases all site reserves if omitted) |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `HoldbackReleaseResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/holdbacks/release', {
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
        '/api/v1/payments/api/v1/payments/holdbacks/release',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/holdbacks/release' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/payments/invoices`

**Summary:** List Invoices  
**Operation ID:** `list_invoices_api_v1_payments_invoices_get`  

**Clinical & GxP Rationale:**  
Lists compiled self-billing invoices matching filter criteria.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | — |
| `site_id` | `query` | `string` | `null` | No | — |
| `billing_cycle` | `query` | `string` | `null` | No | — |
| `status` | `query` | `string` | `null` | No | — |
| `erp_sync_status` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<SelfBillingInvoiceResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/invoices', {
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
        '/api/v1/payments/api/v1/payments/invoices',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/payments/api/v1/payments/invoices' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/invoices/compile`

**Summary:** Compile Billing Cycle  
**Operation ID:** `compile_billing_cycle_api_v1_payments_invoices_compile_post`  

**Clinical & GxP Rationale:**  
Automates monthly or quarterly billing cycle compilation into a self-billing invoice.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `BillingCycleCompilationRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Target Study Protocol UUID or Identifier |
| `site_id` | `string` | ✔ Yes | Target Trial Site UUID or Identifier |
| `billing_cycle` | `string` | No | Billing cycle type (MONTHLY, QUARTERLY, MANUAL) |
| `period_start` | `string` | ✔ Yes | Billing cycle start date (YYYY-MM-DD) |
| `period_end` | `string` | ✔ Yes | Billing cycle end date (YYYY-MM-DD) |
| `auto_approve` | `boolean` | No | Automatically approve invoice upon compilation |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `SelfBillingInvoiceResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/invoices/compile', {
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
        '/api/v1/payments/api/v1/payments/invoices/compile',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/invoices/compile' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/payments/invoices/{invoice_id}`

**Summary:** Get Invoice  
**Operation ID:** `get_invoice_api_v1_payments_invoices__invoice_id__get`  

**Clinical & GxP Rationale:**  
Retrieves full self-billing invoice details with line items and remittance advice.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `invoice_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `SelfBillingInvoiceResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/invoices/{invoice_id}', {
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
        '/api/v1/payments/api/v1/payments/invoices/{invoice_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/payments/api/v1/payments/invoices/{invoice_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/invoices/{invoice_id}/approve`

**Summary:** Approve Self Billing Invoice  
**Operation ID:** `approve_self_billing_invoice_api_v1_payments_invoices__invoice_id__approve_post`  

**Clinical & GxP Rationale:**  
Approves a draft self-billing invoice for ERP synchronization and disbursement.

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
| `200` | Successful Response | `SelfBillingInvoiceResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/invoices/{invoice_id}/approve', {
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
        '/api/v1/payments/api/v1/payments/invoices/{invoice_id}/approve',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/invoices/{invoice_id}/approve' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/invoices/{invoice_id}/erp-export`

**Summary:** Export Invoice To Erp  
**Operation ID:** `export_invoice_to_erp_api_v1_payments_invoices__invoice_id__erp_export_post`  

**Clinical & GxP Rationale:**  
Exports invoice into standardized ERP transmission format (SAP/NetSuite).

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `invoice_id` | `path` | `string` | ✔ Yes | — |
| `erp_system` | `query` | `string` | No | Target ERP (SAP, NETSUITE) |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ErpExportResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/invoices/{invoice_id}/erp-export', {
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
        '/api/v1/payments/api/v1/payments/invoices/{invoice_id}/erp-export',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/invoices/{invoice_id}/erp-export' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/payments/invoices/{invoice_id}/erp-logs`

**Summary:** List Erp Sync Logs  
**Operation ID:** `list_erp_sync_logs_api_v1_payments_invoices__invoice_id__erp_logs_get`  

**Clinical & GxP Rationale:**  
Lists ERP synchronization history and webhook audit records for an invoice.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `invoice_id` | `path` | `string` | ✔ Yes | — |
| `erp_system` | `query` | `string` | `null` | No | — |
| `status` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<ErpSyncLogResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/invoices/{invoice_id}/erp-logs', {
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
        '/api/v1/payments/api/v1/payments/invoices/{invoice_id}/erp-logs',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/payments/api/v1/payments/invoices/{invoice_id}/erp-logs' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/invoices/{invoice_id}/reconcile`

**Summary:** Reconcile Invoice Payment  
**Operation ID:** `reconcile_invoice_payment_api_v1_payments_invoices__invoice_id__reconcile_post`  

**Clinical & GxP Rationale:**  
Reconciles payment execution against self-billing invoice and underlying payables.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `invoice_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `PaymentReconciliationRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `payment_reference` | `string` | ✔ Yes | Bank Wire / Check / ERP Settlement Document Number |
| `erp_system` | `string` | No | ERP system identifier (SAP, NETSUITE, etc.) |
| `payment_date` | `string` | `null` | No | Payment execution date (YYYY-MM-DD) |
| `amount_paid` | `number` | ✔ Yes | Actual disbursed net amount |
| `notes` | `string` | `null` | No | Financial reconciliation notes |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `PaymentReconciliationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/invoices/{invoice_id}/reconcile', {
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
        '/api/v1/payments/api/v1/payments/invoices/{invoice_id}/reconcile',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/invoices/{invoice_id}/reconcile' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/payments/invoices/{invoice_id}/remittance-advice`

**Summary:** Get Remittance Advice  
**Operation ID:** `get_remittance_advice_api_v1_payments_invoices__invoice_id__remittance_advice_get`  

**Clinical & GxP Rationale:**  
Retrieves remittance advice statement record for an invoice.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `invoice_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `RemittanceAdviceResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/invoices/{invoice_id}/remittance-advice', {
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
        '/api/v1/payments/api/v1/payments/invoices/{invoice_id}/remittance-advice',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/payments/api/v1/payments/invoices/{invoice_id}/remittance-advice' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/invoices/{invoice_id}/remittance-advice/dispatch`

**Summary:** Dispatch Remittance Advice  
**Operation ID:** `dispatch_remittance_advice_api_v1_payments_invoices__invoice_id__remittance_advice_dispatch_post`  

**Clinical & GxP Rationale:**  
Dispatches signed remittance advice statement via automated email notification.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `invoice_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `RemittanceAdviceDispatchRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `recipient_email` | `string` | ✔ Yes | Site financial contact destination email address |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `RemittanceAdviceResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/invoices/{invoice_id}/remittance-advice/dispatch', {
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
        '/api/v1/payments/api/v1/payments/invoices/{invoice_id}/remittance-advice/dispatch',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/invoices/{invoice_id}/remittance-advice/dispatch' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/payments/invoices/{invoice_id}/remittance-advice/pdf`

**Summary:** Download Remittance Advice Pdf  
**Operation ID:** `download_remittance_advice_pdf_api_v1_payments_invoices__invoice_id__remittance_advice_pdf_get`  

**Clinical & GxP Rationale:**  
Downloads binary PDF remittance advice statement.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `invoice_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/invoices/{invoice_id}/remittance-advice/pdf', {
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
        '/api/v1/payments/api/v1/payments/invoices/{invoice_id}/remittance-advice/pdf',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/payments/api/v1/payments/invoices/{invoice_id}/remittance-advice/pdf' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/invoices/{invoice_id}/remittance-advice/sign`

**Summary:** Sign Remittance Advice  
**Operation ID:** `sign_remittance_advice_api_v1_payments_invoices__invoice_id__remittance_advice_sign_post`  

**Clinical & GxP Rationale:**  
Applies 21 CFR Part 11 cryptographic digital signature to remittance advice statement.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `invoice_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `RemittanceAdviceSignRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `signer_name` | `string` | ✔ Yes | Authorized Grants Lead Name |
| `signer_role` | `string` | No | Signer corporate or clinical title |
| `signature_reason` | `string` | No | 21 CFR Part 11 Electronic Signature manifestation statement |
| `cert_pem` | `string` | `null` | No | Optional custom X.509 Certificate PEM |
| `private_key_pem` | `string` | `null` | No | Optional custom RSA Private Key PEM |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `RemittanceAdviceResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/invoices/{invoice_id}/remittance-advice/sign', {
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
        '/api/v1/payments/api/v1/payments/invoices/{invoice_id}/remittance-advice/sign',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/invoices/{invoice_id}/remittance-advice/sign' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/invoices/{invoice_id}/remittance-advice/verify-signature`

**Summary:** Verify Remittance Advice Signature  
**Operation ID:** `verify_remittance_advice_signature_api_v1_payments_invoices__invoice_id__remittance_advice_verify_signature_post`  

**Clinical & GxP Rationale:**  
Verifies cryptographic signature validity and tamper resistance of remittance advice statement.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Mandatory 60s `sig_token` required for Part 11 sign-off

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `invoice_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `RemittanceSignatureVerificationResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/invoices/{invoice_id}/remittance-advice/verify-signature', {
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
        '/api/v1/payments/api/v1/payments/invoices/{invoice_id}/remittance-advice/verify-signature',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/invoices/{invoice_id}/remittance-advice/verify-signature' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/lay-summaries`

**Summary:** Create Lay Summary  
**Operation ID:** `create_lay_summary_api_v1_payments_lay_summaries_post`  

**Clinical & GxP Rationale:**  
Drafts or creates an EU CTR Regulation No 536/2014 Article 37 Plain Language Lay Summary.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `LaySummaryCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Study Protocol ID |
| `protocol_id` | `string` | `null` | No | Protocol Code |
| `title` | `string` | ✔ Yes | Lay Summary title |
| `trial_phase` | `string` | No | Clinical trial phase |
| `therapeutic_area` | `string` | ✔ Yes | Therapeutic indication |
| `sponsor_name` | `string` | ✔ Yes | Sponsor organization |
| `summary_text` | `string` | ✔ Yes | Plain-language narrative overview |
| `plain_language_sections` | `object` | `null` | No | Structured patient sections (objective, treatments, results, side_effects) |
| `irb_approval_number` | `string` | ✔ Yes | IRB/Ethics Committee approval number |
| `irb_approval_date` | `string` | ✔ Yes | IRB approval date (YYYY-MM-DD) |
| `language` | `string` | No | Language code (en, es, de, fr) |
| `status` | `string` | No | Publication status (DRAFT, IRB_APPROVED, PUBLISHED) |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `LaySummaryResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/lay-summaries', {
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
        '/api/v1/payments/api/v1/payments/lay-summaries',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/lay-summaries' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/payments/lay-summaries`

**Summary:** List Lay Summaries  
**Operation ID:** `list_lay_summaries_api_v1_payments_lay_summaries_get`  

**Clinical & GxP Rationale:**  
Lists published and drafted plain-language trial summaries.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | — |
| `language` | `query` | `string` | `null` | No | — |
| `status` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<LaySummaryResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/lay-summaries', {
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
        '/api/v1/payments/api/v1/payments/lay-summaries',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/payments/api/v1/payments/lay-summaries' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/payments/lay-summaries/{summary_id}`

**Summary:** Get Lay Summary  
**Operation ID:** `get_lay_summary_api_v1_payments_lay_summaries__summary_id__get`  

**Clinical & GxP Rationale:**  
Retrieves details of a plain-language trial summary.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `summary_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `LaySummaryResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/lay-summaries/{summary_id}', {
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
        '/api/v1/payments/api/v1/payments/lay-summaries/{summary_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/payments/api/v1/payments/lay-summaries/{summary_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/lay-summaries/{summary_id}/confirm-receipt`

**Summary:** Confirm Lay Summary Receipt  
**Operation ID:** `confirm_lay_summary_receipt_api_v1_payments_lay_summaries__summary_id__confirm_receipt_post`  

**Clinical & GxP Rationale:**  
Records participant confirmation of lay summary receipt with comprehension score and SHA-256 seal.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `summary_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `LaySummaryReceiptConfirmRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `subject_id` | `string` | ✔ Yes | Participant subject ID |
| `study_id` | `string` | ✔ Yes | Study ID |
| `comprehension_score` | `integer` | No | Comprehension rating (1=Very Difficult to 5=Very Clear) |
| `comprehension_feedback` | `string` | `null` | No | Participant feedback or comments on the summary |
| `confirmation_statement` | `string` | No | Formal acknowledgment statement |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `LaySummaryReceiptResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/lay-summaries/{summary_id}/confirm-receipt', {
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
        '/api/v1/payments/api/v1/payments/lay-summaries/{summary_id}/confirm-receipt',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/lay-summaries/{summary_id}/confirm-receipt' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/lay-summaries/{summary_id}/publish`

**Summary:** Publish Lay Summary  
**Operation ID:** `publish_lay_summary_api_v1_payments_lay_summaries__summary_id__publish_post`  

**Clinical & GxP Rationale:**  
Formally publishes an IRB-approved lay summary for participant access.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `summary_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `LaySummaryPublishRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `irb_approval_number` | `string` | `null` | No | Confirmed IRB approval identifier |
| `irb_approval_date` | `string` | `null` | No | Confirmed IRB approval date |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `LaySummaryResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/lay-summaries/{summary_id}/publish', {
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
        '/api/v1/payments/api/v1/payments/lay-summaries/{summary_id}/publish',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/lay-summaries/{summary_id}/publish' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/payments/lay-summaries/{summary_id}/receipts`

**Summary:** List Lay Summary Receipts  
**Operation ID:** `list_lay_summary_receipts_api_v1_payments_lay_summaries__summary_id__receipts_get`  

**Clinical & GxP Rationale:**  
Lists confirmation receipts for a lay summary for regulatory compliance audits.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `summary_id` | `path` | `string` | ✔ Yes | — |
| `study_id` | `query` | `string` | `null` | No | — |
| `subject_id` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<LaySummaryReceiptResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/lay-summaries/{summary_id}/receipts', {
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
        '/api/v1/payments/api/v1/payments/lay-summaries/{summary_id}/receipts',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/payments/api/v1/payments/lay-summaries/{summary_id}/receipts' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/payments/participant/disbursements`

**Summary:** List Participant Disbursements  
**Operation ID:** `list_participant_disbursements_api_v1_payments_participant_disbursements_get`  

**Clinical & GxP Rationale:**  
Lists all digital disbursements dispatched across participant wallets.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `wallet_id` | `query` | `string` | `null` | No | — |
| `study_id` | `query` | `string` | `null` | No | — |
| `subject_id` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<DisbursementResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/participant/disbursements', {
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
        '/api/v1/payments/api/v1/payments/participant/disbursements',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/payments/api/v1/payments/participant/disbursements' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/participant/events/visit-verified`

**Summary:** Ingest Visit Verified Event  
**Operation ID:** `ingest_visit_verified_event_api_v1_payments_participant_events_visit_verified_post`  

**Clinical & GxP Rationale:**  
Receives verified clinic visit completion from EDC and credits stipend to participant wallet.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `VisitVerificationEventDto`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Study Protocol ID |
| `site_id` | `string` | ✔ Yes | Site ID |
| `subject_id` | `string` | ✔ Yes | Subject identifier |
| `visit_code` | `string` | ✔ Yes | Completed visit code |
| `event_id` | `string` | ✔ Yes | Unique EDC trigger event identifier |
| `verified_by` | `string` | No | User identity confirming visit |
| `verification_timestamp` | `string` | `null` | No | ISO timestamp of visit completion |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `object` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/participant/events/visit-verified', {
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
        '/api/v1/payments/api/v1/payments/participant/events/visit-verified',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/participant/events/visit-verified' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/participant/expenses`

**Summary:** Submit Participant Expense Claim  
**Operation ID:** `submit_participant_expense_claim_api_v1_payments_participant_expenses_post`  

**Clinical & GxP Rationale:**  
Submits a mobile travel/meal expense reimbursement claim for coordinator review.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ExpenseClaimSubmitRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `wallet_id` | `string` | `null` | No | Participant wallet ID (optional if subject_id supplied) |
| `subject_id` | `string` | ✔ Yes | Subject unique identifier |
| `study_id` | `string` | ✔ Yes | Study ID |
| `site_id` | `string` | ✔ Yes | Site ID |
| `expense_date` | `string` | ✔ Yes | Expense date (YYYY-MM-DD) |
| `category` | `string` | No | Expense category (MILEAGE, PARKING, MEAL, LODGING, TRANSIT, OTHER) |
| `merchant_name` | `string` | ✔ Yes | Merchant or vendor name |
| `gross_amount` | `number` | ✔ Yes | Gross receipt expense amount |
| `tax_deduction` | `number` | No | Calculated tax deduction / withholding |
| `net_amount` | `number` | `null` | No | Net reimbursement amount (calculated if omitted) |
| `currency` | `string` | No | Currency ISO code |
| `is_meal_allowance` | `boolean` | No | Whether claim is a per diem meal allowance |
| `per_diem_cap` | `number` | `null` | No | Optional per diem meal cap limit |
| `receipt_image_name` | `string` | `null` | No | Original receipt filename |
| `receipt_image_base64` | `string` | `null` | No | Base64 encoded receipt image or preview |
| `receipt_ocr_data` | `object` | `null` | No | Raw/extracted OCR metadata |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `ExpenseClaimResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/participant/expenses', {
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
        '/api/v1/payments/api/v1/payments/participant/expenses',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/participant/expenses' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/payments/participant/expenses`

**Summary:** List Participant Expense Claims  
**Operation ID:** `list_participant_expense_claims_api_v1_payments_participant_expenses_get`  

**Clinical & GxP Rationale:**  
Lists submitted expense claims filtered by study, site, subject, or review status.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | — |
| `site_id` | `query` | `string` | `null` | No | — |
| `subject_id` | `query` | `string` | `null` | No | — |
| `status` | `query` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<ExpenseClaimResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/participant/expenses', {
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
        '/api/v1/payments/api/v1/payments/participant/expenses',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/payments/api/v1/payments/participant/expenses' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/participant/expenses/ocr-extract`

**Summary:** Extract Receipt Ocr  
**Operation ID:** `extract_receipt_ocr_api_v1_payments_participant_expenses_ocr_extract_post`  

**Clinical & GxP Rationale:**  
Performs OCR metadata extraction on a travel/meal expense receipt.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ReceiptOcrExtractRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `image_base64` | `string` | `null` | No | Base64 encoded receipt image |
| `raw_text` | `string` | `null` | No | Raw OCR text fallback |
| `category_hint` | `string` | `null` | No | Optional category hint |
| `filename` | `string` | `null` | No | Uploaded file name |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ReceiptOcrExtractResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/participant/expenses/ocr-extract', {
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
        '/api/v1/payments/api/v1/payments/participant/expenses/ocr-extract',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/participant/expenses/ocr-extract' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/participant/expenses/{claim_id}/review`

**Summary:** Review Participant Expense Claim  
**Operation ID:** `review_participant_expense_claim_api_v1_payments_participant_expenses__claim_id__review_post`  

**Clinical & GxP Rationale:**  
Processes 1-click coordinator review and approval/rejection of a travel expense claim.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `claim_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ExpenseClaimReviewRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `status` | `string` | ✔ Yes | Review outcome (APPROVED or REJECTED) |
| `review_notes` | `string` | `null` | No | Coordinator review comments |
| `adjusted_amount` | `number` | `null` | No | Optional adjusted gross amount |
| `adjusted_category` | `string` | `null` | No | Optional corrected category |
| `adjusted_merchant` | `string` | `null` | No | Optional corrected merchant name |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ExpenseClaimResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/participant/expenses/{claim_id}/review', {
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
        '/api/v1/payments/api/v1/payments/participant/expenses/{claim_id}/review',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/participant/expenses/{claim_id}/review' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/participant/stipend-rules`

**Summary:** Create Participant Stipend Rule  
**Operation ID:** `create_participant_stipend_rule_api_v1_payments_participant_stipend_rules_post`  

**Clinical & GxP Rationale:**  
Configures participant visit compensation rules linked to the Schedule of Activities.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ParticipantStipendRuleCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | `string` | ✔ Yes | Study Protocol ID |
| `visit_code` | `string` | ✔ Yes | Schedule of Activities visit code |
| `visit_name` | `string` | ✔ Yes | Descriptive visit name |
| `procedure_code` | `string` | `null` | No | Optional procedure identifier |
| `amount` | `number` | ✔ Yes | Stipend compensation amount |
| `currency` | `string` | No | Currency ISO code |
| `requires_verification` | `boolean` | No | Whether clinic visit completion must be verified in EDC |
| `taxable` | `boolean` | No | Whether stipend is taxable |
| `anti_coercion_cap` | `number` | `null` | No | Ethics/IRB anti-coercion cap limit |
| `is_milestone` | `boolean` | No | Whether visit is a major study milestone |
| `milestone_code` | `string` | `null` | No | Milestone code (e.g. MILESTONE_SCREENING) |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `ParticipantStipendRuleResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/participant/stipend-rules', {
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
        '/api/v1/payments/api/v1/payments/participant/stipend-rules',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/participant/stipend-rules' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/payments/participant/stipend-rules`

**Summary:** List Participant Stipend Rules  
**Operation ID:** `list_participant_stipend_rules_api_v1_payments_participant_stipend_rules_get`  

**Clinical & GxP Rationale:**  
Lists SoA protocol stipend compensation rules for a study.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | — |
| `is_active_only` | `query` | `boolean` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<ParticipantStipendRuleResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/participant/stipend-rules', {
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
        '/api/v1/payments/api/v1/payments/participant/stipend-rules',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/payments/api/v1/payments/participant/stipend-rules' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/participant/wallets`

**Summary:** Get Or Create Participant Wallet  
**Operation ID:** `get_or_create_participant_wallet_api_v1_payments_participant_wallets_post`  

**Clinical & GxP Rationale:**  
Provisions or retrieves a participant financial wallet.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Payload Schema (`application/json`)

**Schema Type:** `ParticipantWalletCreate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `subject_id` | `string` | ✔ Yes | Subject unique identifier / MRN |
| `study_id` | `string` | ✔ Yes | Study Protocol ID |
| `site_id` | `string` | ✔ Yes | Site ID |
| `currency` | `string` | No | Currency ISO code |
| `card_type` | `string` | No | Card type (RELOADABLE_DEBIT, VIRTUAL) |
| `virtual_card_token` | `string` | `null` | No | Virtual card token |
| `virtual_card_mask` | `string` | `null` | No | Masked virtual card display number |
| `virtual_card_expiry` | `string` | `null` | No | Virtual card expiry MM/YY |
| `anti_coercion_cap` | `number` | `null` | No | Ethics/IRB anti-coercion cap limit |
| `ach_routing_number` | `string` | `null` | No | Bank routing transit number |
| `ach_account_mask` | `string` | `null` | No | Masked bank account number |
| `ach_account_holder` | `string` | `null` | No | Account holder name |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `ParticipantWalletResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/participant/wallets', {
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
        '/api/v1/payments/api/v1/payments/participant/wallets',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/participant/wallets' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/payments/participant/wallets`

**Summary:** List Participant Wallets  
**Operation ID:** `list_participant_wallets_api_v1_payments_participant_wallets_get`  

**Clinical & GxP Rationale:**  
Lists participant financial wallets filtered by study and/or site.

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
| `200` | Successful Response | `Array<ParticipantWalletResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/participant/wallets', {
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
        '/api/v1/payments/api/v1/payments/participant/wallets',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/payments/api/v1/payments/participant/wallets' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/payments/participant/wallets/subject/{study_id}/{subject_id}`

**Summary:** Get Participant Wallet By Subject  
**Operation ID:** `get_participant_wallet_by_subject_api_v1_payments_participant_wallets_subject__study_id___subject_id__get`  

**Clinical & GxP Rationale:**  
Retrieves participant wallet by study ID and subject ID.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `path` | `string` | ✔ Yes | — |
| `subject_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ParticipantWalletResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/participant/wallets/subject/{study_id}/{subject_id}', {
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
        '/api/v1/payments/api/v1/payments/participant/wallets/subject/{study_id}/{subject_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/payments/api/v1/payments/participant/wallets/subject/{study_id}/{subject_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/payments/participant/wallets/{wallet_id}`

**Summary:** Get Participant Wallet By Id  
**Operation ID:** `get_participant_wallet_by_id_api_v1_payments_participant_wallets__wallet_id__get`  

**Clinical & GxP Rationale:**  
Retrieves participant wallet details and current balance.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `wallet_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ParticipantWalletResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}', {
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
        '/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/payments/participant/wallets/{wallet_id}/balance`

**Summary:** Get Participant Balance Inquiry  
**Operation ID:** `get_participant_balance_inquiry_api_v1_payments_participant_wallets__wallet_id__balance_get`  

**Clinical & GxP Rationale:**  
Performs real-time multi-channel balance inquiry.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `wallet_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `BalanceInquiryResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}/balance', {
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
        '/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}/balance',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}/balance' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/participant/wallets/{wallet_id}/disburse`

**Summary:** Disburse Participant Funds  
**Operation ID:** `disburse_participant_funds_api_v1_payments_participant_wallets__wallet_id__disburse_post`  

**Clinical & GxP Rationale:**  
Dispatches digital funds payout (Virtual Card / Direct ACH) with 21 CFR Part 11 ledger recording.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `wallet_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `DisbursementRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `disbursement_type` | `string` | No | Payout method (VIRTUAL_CARD or DIRECT_ACH) |
| `amount` | `number` | `null` | No | Amount to disburse (defaults to full available balance) |
| `destination_mask` | `string` | `null` | No | Custom masked card / bank account display descriptor |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `DisbursementResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}/disburse', {
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
        '/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}/disburse',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}/disburse' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/participant/wallets/{wallet_id}/issue-card`

**Summary:** Issue Participant Reloadable Card  
**Operation ID:** `issue_participant_reloadable_card_api_v1_payments_participant_wallets__wallet_id__issue_card_post`  

**Clinical & GxP Rationale:**  
Provisions an automated reloadable debit card linked to the participant wallet.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `wallet_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `IssueReloadableCardRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `cardholder_name` | `string` | `null` | No | Name on card |
| `card_type` | `string` | No | Card type (RELOADABLE_DEBIT, VIRTUAL) |
| `initial_load` | `number` | No | Initial funds loaded onto the card |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `201` | Successful Response | `IssueReloadableCardResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}/issue-card', {
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
        '/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}/issue-card',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}/issue-card' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `PUT` `/api/v1/payments/participant/wallets/{wallet_id}/payout-methods`

**Summary:** Update Participant Wallet Payout Methods  
**Operation ID:** `update_participant_wallet_payout_methods_api_v1_payments_participant_wallets__wallet_id__payout_methods_put`  

**Clinical & GxP Rationale:**  
Updates payout credentials (virtual card / direct ACH) for a participant wallet.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `wallet_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `ParticipantWalletPayoutMethodsUpdate`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `virtual_card_token` | `string` | `null` | No | — |
| `virtual_card_mask` | `string` | `null` | No | — |
| `virtual_card_expiry` | `string` | `null` | No | — |
| `card_status` | `string` | `null` | No | — |
| `ach_routing_number` | `string` | `null` | No | — |
| `ach_account_mask` | `string` | `null` | No | — |
| `ach_account_holder` | `string` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ParticipantWalletResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}/payout-methods', {
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
        '/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}/payout-methods',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X PUT 'http://localhost:8000/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}/payout-methods' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/participant/wallets/{wallet_id}/submit-w9`

**Summary:** Submit Participant W9 Form  
**Operation ID:** `submit_participant_w9_form_api_v1_payments_participant_wallets__wallet_id__submit_w9_post`  

**Clinical & GxP Rationale:**  
Submits IRS Form W-9 certification for participant 1099 compliance.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `wallet_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `W9SubmitRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `tax_id_last_four` | `string` | ✔ Yes | Last 4 digits of SSN/ITIN |
| `full_legal_name` | `string` | ✔ Yes | Full legal taxpayer name |
| `certify_accurate` | `boolean` | No | Taxpayer certification of accurate TIN |
| `address` | `string` | `null` | No | Taxpayer mailing address |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `W9SubmitResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}/submit-w9', {
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
        '/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}/submit-w9',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}/submit-w9' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/payments/participant/wallets/{wallet_id}/tax-1099-status`

**Summary:** Get Participant 1099 Tax Status  
**Operation ID:** `get_participant_1099_tax_status_api_v1_payments_participant_wallets__wallet_id__tax_1099_status_get`  

**Clinical & GxP Rationale:**  
Retrieves real-time IRS Form 1099 tax threshold analysis and W-9 validation state.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `wallet_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Tax1099ThresholdResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}/tax-1099-status', {
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
        '/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}/tax-1099-status',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}/tax-1099-status' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/payments/participant/wallets/{wallet_id}/transactions`

**Summary:** List Wallet Transactions  
**Operation ID:** `list_wallet_transactions_api_v1_payments_participant_wallets__wallet_id__transactions_get`  

**Clinical & GxP Rationale:**  
Retrieves immutable financial transaction history for a participant wallet.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `wallet_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<ParticipantTransactionResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}/transactions', {
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
        '/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}/transactions',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}/transactions' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/participant/wallets/{wallet_id}/verify-w9`

**Summary:** Verify Participant W9 Form  
**Operation ID:** `verify_participant_w9_form_api_v1_payments_participant_wallets__wallet_id__verify_w9_post`  

**Clinical & GxP Rationale:**  
Performs coordinator verification on submitted Form W-9.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `wallet_id` | `path` | `string` | ✔ Yes | — |

#### Request Payload Schema (`application/json`)

**Schema Type:** `W9VerifyRequest`

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `notes` | `string` | `null` | No | Coordinator W-9 verification notes |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `ParticipantWalletResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}/verify-w9', {
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
        '/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}/verify-w9',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/participant/wallets/{wallet_id}/verify-w9' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/payments/payables`

**Summary:** List Payables  
**Operation ID:** `list_payables_api_v1_payments_payables_get`  

**Clinical & GxP Rationale:**  
Lists payables with split allocations and holdback reserve status.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `study_id` | `query` | `string` | `null` | No | — |
| `site_id` | `query` | `string` | `null` | No | — |
| `subject_id` | `query` | `string` | `null` | No | — |
| `visit_code` | `query` | `string` | `null` | No | — |
| `form_id` | `query` | `string` | `null` | No | — |
| `status` | `query` | `string` | `null` | No | — |
| `is_sdv_gated` | `query` | `boolean` | `null` | No | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `Array<PayableResponse>` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/payables', {
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
        '/api/v1/payments/api/v1/payments/payables',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/payments/api/v1/payments/payables' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `GET` `/api/v1/payments/payables/{payable_id}`

**Summary:** Get Payable  
**Operation ID:** `get_payable_api_v1_payments_payables__payable_id__get`  

**Clinical & GxP Rationale:**  
Retrieves payable details by ID.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Not required (read-only query)

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `payable_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `PayableResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/payables/{payable_id}', {
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
        '/api/v1/payments/api/v1/payments/payables/{payable_id}',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/payments/api/v1/payments/payables/{payable_id}' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---

### `POST` `/api/v1/payments/payables/{payable_id}/approve`

**Summary:** Approve Payable  
**Operation ID:** `approve_payable_api_v1_payments_payables__payable_id__approve_post`  

**Clinical & GxP Rationale:**  
Approves a payable for disbursement.

#### Security & Access Control (21 CFR Part 11)

- **Authentication:** OIDC Bearer Token (Keycloak)
- **Gateway Headers:** Verified `X-Gateway-Signature` (HMAC-SHA256)
- **Step-Up Electronic Signature:** Required if mutating locked records

#### Request Parameters

| Name | In | Type | Required | Description |
| :--- | :--- | :--- | :--- | :--- |
| `payable_id` | `path` | `string` | ✔ Yes | — |

#### Response Codes & Schemas

| HTTP Status | Description | Response Schema |
| :--- | :--- | :--- |
| `200` | Successful Response | `PayableResponse` |
| `422` | Validation Error | `HTTPValidationError` |

#### Client Code Examples

::: code-group
```typescript [TypeScript (Fetch)]
const response = await fetch('http://localhost:8000/api/v1/payments/api/v1/payments/payables/{payable_id}/approve', {
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
        '/api/v1/payments/api/v1/payments/payables/{payable_id}/approve',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
        json=payload,
    )
    data = response.json()
```

```bash [cURL]
curl -X POST 'http://localhost:8000/api/v1/payments/api/v1/payments/payables/{payable_id}/approve' \
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
const response = await fetch('http://localhost:8000/api/v1/payments/health', {
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
        '/api/v1/payments/health',
        headers={
            'Authorization': f'Bearer {user_token}',
            'X-Change-Reason': 'Clinical routine data capture',
        },
    )
    data = response.json()
```

```bash [cURL]
curl -X GET 'http://localhost:8000/api/v1/payments/health' \
  -H 'Authorization: Bearer <TOKEN>' \
  -H 'Content-Type: application/json' \
  -H 'X-Change-Reason: Routine clinical mutation'
```
:::

---
