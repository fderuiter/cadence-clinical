# payments API

Generated from `docs/openapi/payments_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

- **Service Maturity:** <span class="VPBadge tip">Active</span> (Port `8016` · Gateway prefixes: `/api/v1/payments`)
- **OpenAPI 3.1 Contract:** [`payments_openapi.json`](../openapi/payments_openapi.json) (38 operation(s), 40 schema(s))

## Operations

| Method | Path | Operation | Summary | Contract |
| ------ | ---- | --------- | ------- | -------- |
| `GET` | `/api/v1/payments/audit-logs` | GetPaymentAuditLogs | Retrieve payment audit ledger records. | [Details](#op-getpaymentauditlogs) |
| `POST` | `/api/v1/payments/disburse` | DisburseStipendDirect | Direct disburse ePRO stipend to participant wallet with strict idempotency and audit trail. | [Details](#op-disbursestipenddirect) |
| `GET` | `/api/v1/payments/invoices` | ListInvoices | Lists compiled self-billing invoices matching filter criteria. | [Details](#op-listinvoices) |
| `POST` | `/api/v1/payments/invoices/batch-compile` | BatchCompileBillingCycles | Executes study-wide batch milestone invoice compilation across all active trial sites. | [Details](#op-batchcompilebillingcycles) |
| `POST` | `/api/v1/payments/invoices/compile` | CompileBillingCycle | Automates single-site billing cycle compilation into a self-billing invoice. | [Details](#op-compilebillingcycle) |
| `GET` | `/api/v1/payments/invoices/{id}` | GetInvoiceById | Retrieve single self-billing invoice details with line items. | [Details](#op-getinvoicebyid) |
| `POST` | `/api/v1/payments/invoices/{id}/approve` | ApproveInvoice | Approve draft self-billing invoice for ERP export and settlement. | [Details](#op-approveinvoice) |
| `POST` | `/api/v1/payments/invoices/{id}/erp-export` | ExportInvoiceToErp | Exports self-billing invoice to ERP gateway (e.g. SAP). | [Details](#op-exportinvoicetoerp) |
| `POST` | `/api/v1/payments/invoices/{id}/reconcile` | ReconcileInvoice | Reconciles invoice settlement against bank wire or ERP clearing statement. | [Details](#op-reconcileinvoice) |
| `GET` | `/api/v1/payments/invoices/{id}/remittance-advice/pdf` | DownloadRemittancePdf | Downloads cryptographic PDF remittance statement. | [Details](#op-downloadremittancepdf) |
| `POST` | `/api/v1/payments/invoices/{id}/remittance-advice/sign` | SignRemittanceAdvice | Applies 21 CFR Part 11 electronic signature to cryptographic remittance advice. | [Details](#op-signremittanceadvice) |
| `GET` | `/api/v1/payments/participant/disbursements` | ListDisbursements | List digital disbursement dispatches. | [Details](#op-listdisbursements) |
| `POST` | `/api/v1/payments/participant/disbursements/{id}/settle` | SettleDisbursement | Marks a dispatched digital disbursement as settled upon confirmation. | [Details](#op-settledisbursement) |
| `POST` | `/api/v1/payments/participant/events/visit-verified` | IngestVisitVerifiedEvent | Receives verified eCRF visit completion event and evaluates automated stipend disbursement rules. | [Details](#op-ingestvisitverifiedevent) |
| `GET` | `/api/v1/payments/participant/expenses` | ListExpenseClaims | List travel and meal expense reimbursement claims. | [Details](#op-listexpenseclaims) |
| `POST` | `/api/v1/payments/participant/expenses` | SubmitExpenseClaim | Submit travel/meal expense claim for coordinator review. | [Details](#op-submitexpenseclaim) |
| `POST` | `/api/v1/payments/participant/expenses/ocr-extract` | ExtractReceiptOcr | Extract metadata from receipt image/text using rule-based OCR engine. | [Details](#op-extractreceiptocr) |
| `POST` | `/api/v1/payments/participant/expenses/{claimId}/review` | ReviewExpenseClaim | Coordinator 1-click review and approval/rejection of expense claim. | [Details](#op-reviewexpenseclaim) |
| `POST` | `/api/v1/payments/participant/me/diary-stipend` | CreditSelfDiaryStipend | Credits protocol stipend for a synchronized participant diary strictly from server rules. | [Details](#op-creditselfdiarystipend) |
| `POST` | `/api/v1/payments/participant/me/disburse` | DisburseSelfWalletBalance | Triggers an on-demand payout of the signed participant's available wallet balance. | [Details](#op-disburseselfwalletbalance) |
| `POST` | `/api/v1/payments/participant/me/expenses` | SubmitSelfExpenseClaim | Submits a travel or meal expense claim for the signed participant with policy auto-approval and direct ACH dispatch. | [Details](#op-submitselfexpenseclaim) |
| `POST` | `/api/v1/payments/participant/me/expenses/ocr-extract` | ExtractSelfReceiptOcr | Extracts merchant, date, category, and amount from an uploaded receipt image for the signed participant. | [Details](#op-extractselfreceiptocr) |
| `GET` | `/api/v1/payments/participant/me/wallet` | GetSelfParticipantWallet | Retrieves the authenticated participant's own study wallet balances, masks, and ledger activity. | [Details](#op-getselfparticipantwallet) |
| `GET` | `/api/v1/payments/participant/stipend-rules` | ListStipendRules | List SoA protocol visit stipend compensation rules. | [Details](#op-liststipendrules) |
| `POST` | `/api/v1/payments/participant/stipend-rules` | CreateStipendRule | Configure participant visit compensation rules linked to Schedule of Activities. | [Details](#op-createstipendrule) |
| `GET` | `/api/v1/payments/participant/transactions` | ListTransactions | List participant wallet transactions ledger. | [Details](#op-listtransactions) |
| `GET` | `/api/v1/payments/participant/wallets` | ListWallets | List all participant wallets. | [Details](#op-listwallets) |
| `POST` | `/api/v1/payments/participant/wallets/disburse` | DisburseStipendWallet | Disburse stipend to participant wallet. | [Details](#op-disbursestipendwallet) |
| `GET` | `/api/v1/payments/participant/wallets/subject/{subjectId}` | GetSubjectWallet | Retrieve participant wallet balance and details. | [Details](#op-getsubjectwallet) |
| `GET` | `/api/v1/payments/participant/wallets/{walletId}` | GetWalletById | Retrieve participant wallet details by wallet ID. | [Details](#op-getwalletbyid) |
| `GET` | `/api/v1/payments/participant/wallets/{walletId}/balance` | GetBalanceInquiry | Real-time balance inquiry for participant wallet. | [Details](#op-getbalanceinquiry) |
| `POST` | `/api/v1/payments/participant/wallets/{walletId}/disburse` | DisburseParticipantFunds | Disburse digital funds from participant wallet to debit card or bank account. | [Details](#op-disburseparticipantfunds) |
| `POST` | `/api/v1/payments/participant/wallets/{walletId}/issue-card` | IssueReloadableCard | Issue or re-issue reloadable debit card for participant wallet. | [Details](#op-issuereloadablecard) |
| `POST` | `/api/v1/payments/participant/wallets/{walletId}/submit-w9` | SubmitW9Form | Submit Form W-9 taxpayer identification certification. | [Details](#op-submitw9form) |
| `GET` | `/api/v1/payments/participant/wallets/{walletId}/tax-1099-status` | GetTax1099Status | Retrieve IRS 1099 tax status and threshold monitoring tracer. | [Details](#op-gettax1099status) |
| `POST` | `/api/v1/payments/participant/wallets/{walletId}/verify-w9` | VerifyW9Form | Verify participant Form W-9 submission. | [Details](#op-verifyw9form) |
| `GET` | `/api/v1/payments/status` | PaymentsStatus | Returns Payments operational status. | [Details](#op-paymentsstatus) |
| `GET` | `/healthz` | PaymentsHealthCheck | Site and participant payments health probe. | [Details](#op-paymentshealthcheck) |

### `GET` `/api/v1/payments/audit-logs` {#op-getpaymentauditlogs}

**Operation ID:** `GetPaymentAuditLogs` — Retrieve payment audit ledger records.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_audit_log:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `subjectId` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Payments_PaymentAuditLog`](#schema-payments-paymentauditlog) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `POST` `/api/v1/payments/disburse` {#op-disbursestipenddirect}

**Operation ID:** `DisburseStipendDirect` — Direct disburse ePRO stipend to participant wallet with strict idempotency and audit trail.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_stipend:disburse`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Payments_DisburseStipendRequest`](#schema-payments-disbursestipendrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_DisburseStipendResponse`](#schema-payments-disbursestipendresponse) |
| `400` | Bad Request | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `GET` `/api/v1/payments/invoices` {#op-listinvoices}

**Operation ID:** `ListInvoices` — Lists compiled self-billing invoices matching filter criteria.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_invoice:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `batch_execution_id` | `query` | `optional` | `string` |  |
| `billing_cycle` | `query` | `optional` | `string` |  |
| `erp_sync_status` | `query` | `optional` | `string` |  |
| `site_id` | `query` | `optional` | `string` |  |
| `status` | `query` | `optional` | `string` |  |
| `study_id` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Payments_SelfBillingInvoiceResponse`](#schema-payments-selfbillinginvoiceresponse) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `POST` `/api/v1/payments/invoices/batch-compile` {#op-batchcompilebillingcycles}

**Operation ID:** `BatchCompileBillingCycles` — Executes study-wide batch milestone invoice compilation across all active trial sites.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_invoice:compile`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Payments_BatchBillingCycleCompilationRequest`](#schema-payments-batchbillingcyclecompilationrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Payments_BatchBillingCompilationResponse`](#schema-payments-batchbillingcompilationresponse) |
| `400` | Bad Request | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `POST` `/api/v1/payments/invoices/compile` {#op-compilebillingcycle}

**Operation ID:** `CompileBillingCycle` — Automates single-site billing cycle compilation into a self-billing invoice.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_invoice:compile`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Payments_BillingCycleCompilationRequest`](#schema-payments-billingcyclecompilationrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Payments_SelfBillingInvoiceResponse`](#schema-payments-selfbillinginvoiceresponse) |
| `400` | Bad Request | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `GET` `/api/v1/payments/invoices/{id}` {#op-getinvoicebyid}

**Operation ID:** `GetInvoiceById` — Retrieve single self-billing invoice details with line items.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_invoice:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `id` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_SelfBillingInvoiceResponse`](#schema-payments-selfbillinginvoiceresponse) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `POST` `/api/v1/payments/invoices/{id}/approve` {#op-approveinvoice}

**Operation ID:** `ApproveInvoice` — Approve draft self-billing invoice for ERP export and settlement.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_invoice:approve`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `id` | `path` | `required` | `string` |  |
| `X-Sig-Token` | `header` | `required` | `string` (minLen: 1) | Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_SelfBillingInvoiceResponse`](#schema-payments-selfbillinginvoiceresponse) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `409` | Conflict | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `POST` `/api/v1/payments/invoices/{id}/erp-export` {#op-exportinvoicetoerp}

**Operation ID:** `ExportInvoiceToErp` — Exports self-billing invoice to ERP gateway (e.g. SAP).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_invoice:export_erp`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `id` | `path` | `required` | `string` |  |
| `erp_system` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_SelfBillingInvoiceResponse`](#schema-payments-selfbillinginvoiceresponse) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `409` | Conflict | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `POST` `/api/v1/payments/invoices/{id}/reconcile` {#op-reconcileinvoice}

**Operation ID:** `ReconcileInvoice` — Reconciles invoice settlement against bank wire or ERP clearing statement.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_invoice:reconcile`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `id` | `path` | `required` | `string` |  |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Payments_ReconcileInvoiceRequest`](#schema-payments-reconcileinvoicerequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_SelfBillingInvoiceResponse`](#schema-payments-selfbillinginvoiceresponse) |
| `400` | Bad Request | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `409` | Conflict | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `GET` `/api/v1/payments/invoices/{id}/remittance-advice/pdf` {#op-downloadremittancepdf}

**Operation ID:** `DownloadRemittancePdf` — Downloads cryptographic PDF remittance statement.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_invoice:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `id` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/pdf` | `string` (`binary`) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `POST` `/api/v1/payments/invoices/{id}/remittance-advice/sign` {#op-signremittanceadvice}

**Operation ID:** `SignRemittanceAdvice` — Applies 21 CFR Part 11 electronic signature to cryptographic remittance advice.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_invoice:sign_remittance`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `id` | `path` | `required` | `string` |  |
| `X-Sig-Token` | `header` | `required` | `string` (minLen: 1) | Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Payments_SignRemittanceRequest`](#schema-payments-signremittancerequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_RemittanceAdviceDto`](#schema-payments-remittanceadvicedto) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `409` | Conflict | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `GET` `/api/v1/payments/participant/disbursements` {#op-listdisbursements}

**Operation ID:** `ListDisbursements` — List digital disbursement dispatches.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_wallet:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `subjectId` | `query` | `optional` | `string` |  |
| `walletId` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Payments_ParticipantDisbursement`](#schema-payments-participantdisbursement) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `POST` `/api/v1/payments/participant/disbursements/{id}/settle` {#op-settledisbursement}

**Operation ID:** `SettleDisbursement` — Marks a dispatched digital disbursement as settled upon confirmation.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_wallet:disburse`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `id` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_ParticipantDisbursement`](#schema-payments-participantdisbursement) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `409` | Conflict | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `POST` `/api/v1/payments/participant/events/visit-verified` {#op-ingestvisitverifiedevent}

**Operation ID:** `IngestVisitVerifiedEvent` — Receives verified eCRF visit completion event and evaluates automated stipend disbursement rules.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_visit_event:record`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Payments_VisitVerificationEventDto`](#schema-payments-visitverificationeventdto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_VisitVerificationResponseDto`](#schema-payments-visitverificationresponsedto) |
| `400` | Bad Request | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `GET` `/api/v1/payments/participant/expenses` {#op-listexpenseclaims}

**Operation ID:** `ListExpenseClaims` — List travel and meal expense reimbursement claims.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_expense:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `status` | `query` | `optional` | `string` |  |
| `subjectId` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Payments_ParticipantExpenseClaim`](#schema-payments-participantexpenseclaim) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `POST` `/api/v1/payments/participant/expenses` {#op-submitexpenseclaim}

**Operation ID:** `SubmitExpenseClaim` — Submit travel/meal expense claim for coordinator review.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_expense:submit`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Payments_SubmitExpenseClaimRequestDto`](#schema-payments-submitexpenseclaimrequestdto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_ParticipantExpenseClaim`](#schema-payments-participantexpenseclaim) |
| `400` | Bad Request | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `POST` `/api/v1/payments/participant/expenses/ocr-extract` {#op-extractreceiptocr}

**Operation ID:** `ExtractReceiptOcr` — Extract metadata from receipt image/text using rule-based OCR engine.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_expense:submit`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Payments_OcrExtractRequestDto`](#schema-payments-ocrextractrequestdto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_ReceiptOcrExtractResponseDto`](#schema-payments-receiptocrextractresponsedto) |
| `400` | Bad Request | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `POST` `/api/v1/payments/participant/expenses/{claimId}/review` {#op-reviewexpenseclaim}

**Operation ID:** `ReviewExpenseClaim` — Coordinator 1-click review and approval/rejection of expense claim.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_expense:review`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `claimId` | `path` | `required` | `string` |  |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Payments_ReviewExpenseClaimRequestDto`](#schema-payments-reviewexpenseclaimrequestdto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_ParticipantExpenseClaim`](#schema-payments-participantexpenseclaim) |
| `400` | Bad Request | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `409` | Conflict | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `POST` `/api/v1/payments/participant/me/diary-stipend` {#op-creditselfdiarystipend}

**Operation ID:** `CreditSelfDiaryStipend` — Credits protocol stipend for a synchronized participant diary strictly from server rules.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_self_stipend:credit`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `Idempotency-Key` | `header` | `optional` | `string` (maxLen: 255) | Optional caller-supplied idempotency key (max 255 characters) deduplicating participant diary stipend credits. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Payments_SelfServiceDiaryStipendRequestDto`](#schema-payments-selfservicediarystipendrequestdto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_SelfServiceDiaryStipendResponseDto`](#schema-payments-selfservicediarystipendresponsedto) |
| `400` | Bad Request | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `POST` `/api/v1/payments/participant/me/disburse` {#op-disburseselfwalletbalance}

**Operation ID:** `DisburseSelfWalletBalance` — Triggers an on-demand payout of the signed participant's available wallet balance.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_self_wallet:disburse`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `Idempotency-Key` | `header` | `optional` | `string` (maxLen: 255) | Optional caller-supplied idempotency key (max 255 characters) ensuring safe replay across retries. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Payments_SelfServiceDisburseRequestDto`](#schema-payments-selfservicedisburserequestdto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_SelfServiceDisburseResponseDto`](#schema-payments-selfservicedisburseresponsedto) |
| `400` | Bad Request | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `409` | Conflict | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `POST` `/api/v1/payments/participant/me/expenses` {#op-submitselfexpenseclaim}

**Operation ID:** `SubmitSelfExpenseClaim` — Submits a travel or meal expense claim for the signed participant with policy auto-approval and direct ACH dispatch.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_self_expense:submit`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `Idempotency-Key` | `header` | `optional` | `string` (maxLen: 255) | Optional caller-supplied idempotency key (max 255 characters) ensuring safe replay across retries. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Payments_SelfServiceSubmitExpenseRequestDto`](#schema-payments-selfservicesubmitexpenserequestdto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_SelfServiceExpenseClaimResponseDto`](#schema-payments-selfserviceexpenseclaimresponsedto) |
| `400` | Bad Request | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `POST` `/api/v1/payments/participant/me/expenses/ocr-extract` {#op-extractselfreceiptocr}

**Operation ID:** `ExtractSelfReceiptOcr` — Extracts merchant, date, category, and amount from an uploaded receipt image for the signed participant.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_self_expense:ocr_scan`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Payments_OcrExtractRequestDto`](#schema-payments-ocrextractrequestdto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_ReceiptOcrExtractResponseDto`](#schema-payments-receiptocrextractresponsedto) |
| `400` | Bad Request | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `GET` `/api/v1/payments/participant/me/wallet` {#op-getselfparticipantwallet}

**Operation ID:** `GetSelfParticipantWallet` — Retrieves the authenticated participant's own study wallet balances, masks, and ledger activity.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_self_wallet:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `studyId` | `query` | `optional` | `string` |  |
| `subjectId` | `query` | `optional` | `string` |  |
| `walletId` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_SelfServiceWalletSummaryResponseDto`](#schema-payments-selfservicewalletsummaryresponsedto) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `GET` `/api/v1/payments/participant/stipend-rules` {#op-liststipendrules}

**Operation ID:** `ListStipendRules` — List SoA protocol visit stipend compensation rules.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_stipend_rule:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `studyId` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Payments_ParticipantStipendRule`](#schema-payments-participantstipendrule) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `POST` `/api/v1/payments/participant/stipend-rules` {#op-createstipendrule}

**Operation ID:** `CreateStipendRule` — Configure participant visit compensation rules linked to Schedule of Activities.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_stipend_rule:configure`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Payments_ParticipantStipendRuleCreateDto`](#schema-payments-participantstipendrulecreatedto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_ParticipantStipendRule`](#schema-payments-participantstipendrule) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `422` | Unprocessable Entity | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `GET` `/api/v1/payments/participant/transactions` {#op-listtransactions}

**Operation ID:** `ListTransactions` — List participant wallet transactions ledger.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_wallet:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `subjectId` | `query` | `optional` | `string` |  |
| `walletId` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Payments_ParticipantTransaction`](#schema-payments-participanttransaction) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `GET` `/api/v1/payments/participant/wallets` {#op-listwallets}

**Operation ID:** `ListWallets` — List all participant wallets.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_wallet:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Payments_WalletResponse`](#schema-payments-walletresponse) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `POST` `/api/v1/payments/participant/wallets/disburse` {#op-disbursestipendwallet}

**Operation ID:** `DisburseStipendWallet` — Disburse stipend to participant wallet.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_stipend:disburse`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Payments_DisburseStipendRequest`](#schema-payments-disbursestipendrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_DisburseStipendResponse`](#schema-payments-disbursestipendresponse) |
| `400` | Bad Request | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `GET` `/api/v1/payments/participant/wallets/subject/{subjectId}` {#op-getsubjectwallet}

**Operation ID:** `GetSubjectWallet` — Retrieve participant wallet balance and details.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_wallet:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `subjectId` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_WalletResponse`](#schema-payments-walletresponse) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `GET` `/api/v1/payments/participant/wallets/{walletId}` {#op-getwalletbyid}

**Operation ID:** `GetWalletById` — Retrieve participant wallet details by wallet ID.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_wallet:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `walletId` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_WalletResponse`](#schema-payments-walletresponse) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `GET` `/api/v1/payments/participant/wallets/{walletId}/balance` {#op-getbalanceinquiry}

**Operation ID:** `GetBalanceInquiry` — Real-time balance inquiry for participant wallet.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_wallet:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `walletId` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_WalletBalanceInquiryResponseDto`](#schema-payments-walletbalanceinquiryresponsedto) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `POST` `/api/v1/payments/participant/wallets/{walletId}/disburse` {#op-disburseparticipantfunds}

**Operation ID:** `DisburseParticipantFunds` — Disburse digital funds from participant wallet to debit card or bank account.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_wallet:disburse`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `walletId` | `path` | `required` | `string` |  |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Payments_DisburseFundsRequestDto`](#schema-payments-disbursefundsrequestdto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_ParticipantDisbursement`](#schema-payments-participantdisbursement) |
| `400` | Bad Request | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `409` | Conflict | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `POST` `/api/v1/payments/participant/wallets/{walletId}/issue-card` {#op-issuereloadablecard}

**Operation ID:** `IssueReloadableCard` — Issue or re-issue reloadable debit card for participant wallet.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_wallet:issue_card`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `walletId` | `path` | `required` | `string` |  |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Payments_IssueCardRequestDto`](#schema-payments-issuecardrequestdto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_WalletResponse`](#schema-payments-walletresponse) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `POST` `/api/v1/payments/participant/wallets/{walletId}/submit-w9` {#op-submitw9form}

**Operation ID:** `SubmitW9Form` — Submit Form W-9 taxpayer identification certification.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_w9:submit`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `walletId` | `path` | `required` | `string` |  |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Payments_SubmitW9RequestDto`](#schema-payments-submitw9requestdto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_WalletResponse`](#schema-payments-walletresponse) |
| `400` | Bad Request | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `GET` `/api/v1/payments/participant/wallets/{walletId}/tax-1099-status` {#op-gettax1099status}

**Operation ID:** `GetTax1099Status` — Retrieve IRS 1099 tax status and threshold monitoring tracer.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_wallet:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `walletId` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_Tax1099StatusResponseDto`](#schema-payments-tax1099statusresponsedto) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `POST` `/api/v1/payments/participant/wallets/{walletId}/verify-w9` {#op-verifyw9form}

**Operation ID:** `VerifyW9Form` — Verify participant Form W-9 submission.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `payments_w9:verify`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Payments`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `walletId` | `path` | `required` | `string` |  |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Payments_VerifyW9RequestDto`](#schema-payments-verifyw9requestdto) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_WalletResponse`](#schema-payments-walletresponse) |
| `400` | Bad Request | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Payments_ProblemDetails`](#schema-payments-problemdetails) |

</details>

### `GET` `/api/v1/payments/status` {#op-paymentsstatus}

**Operation ID:** `PaymentsStatus` — Returns Payments operational status.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Payments`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_AnonymousTypeOfstringAndstring`](#schema-payments-anonymoustypeofstringandstring) |

</details>

### `GET` `/healthz` {#op-paymentshealthcheck}

**Operation ID:** `PaymentsHealthCheck` — Site and participant payments health probe.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Payments`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Payments_AnonymousTypeOfstringAndstring`](#schema-payments-anonymoustypeofstringandstring) |

</details>

## Schemas

Deduplicated component schemas exported in `components.schemas` (40 total). Operation payloads and responses link here so nested models are defined once per page.

### `Payments_AnonymousTypeOfstringAndstring` {#schema-payments-anonymoustypeofstringandstring}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `service` | `string` \| `null` | `required` |  |
| `status` | `string` \| `null` | `required` |  |

</details>

### `Payments_BatchBillingCompilationResponse` {#schema-payments-batchbillingcompilationresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (15)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `audit_logs` | array of [`Payments_PaymentAuditLogDto`](#schema-payments-paymentauditlogdto) | `required` |  |
| `batch_execution_id` | `string` | `required` |  |
| `billing_cycle` | `string` | `required` |  |
| `failed_sites_count` | `integer` (`int32`) | `required` |  |
| `period_end` | `string` | `required` |  |
| `period_start` | `string` | `required` |  |
| `site_results` | array of [`Payments_SiteBatchCompilationResult`](#schema-payments-sitebatchcompilationresult) | `required` |  |
| `study_id` | `string` | `required` |  |
| `successful_sites_count` | `integer` (`int32`) | `required` |  |
| `total_gross_amount` | `number` (`double`) | `required` |  |
| `total_holdback_amount` | `number` (`double`) | `required` |  |
| `total_invoices_generated` | `integer` (`int32`) | `required` |  |
| `total_net_amount` | `number` (`double`) | `required` |  |
| `total_payables_compiled` | `integer` (`int32`) | `required` |  |
| `total_sites_processed` | `integer` (`int32`) | `required` |  |

</details>

### `Payments_BatchBillingCycleCompilationRequest` {#schema-payments-batchbillingcyclecompilationrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `auto_approve` | `boolean` | `optional` |  |
| `billing_cycle` | `string` | `optional` |  |
| `period_end` | `string` \| `null` | `optional` |  |
| `period_start` | `string` \| `null` | `optional` |  |
| `site_ids` | array of `string` \| `null` | `optional` |  |
| `study_id` | `string` | `required` |  |

</details>

### `Payments_BillingCycleCompilationRequest` {#schema-payments-billingcyclecompilationrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `auto_approve` | `boolean` | `optional` |  |
| `billing_cycle` | `string` | `optional` |  |
| `period_end` | `string` \| `null` | `optional` |  |
| `period_start` | `string` \| `null` | `optional` |  |
| `site_id` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |

</details>

### `Payments_DisburseFundsRequestDto` {#schema-payments-disbursefundsrequestdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `amount` | `number` (`double`) | `required` |  |
| `disbursementType` | `string` | `required` |  |

</details>

### `Payments_DisburseStipendRequest` {#schema-payments-disbursestipendrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (9)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `amount` | `number` (`double`) \| `null` | `required` |  |
| `assignmentId` | `string` \| `null` | `required` |  |
| `diaryId` | `string` \| `null` | `required` |  |
| `disbursementType` | `string` \| `null` | `required` |  |
| `idempotencyKey` | `string` \| `null` | `required` |  |
| `sequenceNumber` | `integer` (`int32`) \| `null` | `required` |  |
| `siteId` | `string` \| `null` | `required` |  |
| `studyId` | `string` \| `null` | `required` |  |
| `subjectId` | `string` | `required` |  |

</details>

### `Payments_DisburseStipendResponse` {#schema-payments-disbursestipendresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (11)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `amount` | `number` (`double`) | `required` |  |
| `currency` | `string` | `required` |  |
| `dispatchedAt` | `string` (`date-time`) | `required` |  |
| `id` | `string` | `required` |  |
| `idempotencyKey` | `string` | `required` |  |
| `newBalance` | `number` (`double`) | `required` |  |
| `referenceNumber` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `subjectId` | `string` | `required` |  |
| `success` | `boolean` | `required` |  |
| `walletId` | `string` | `required` |  |

</details>

### `Payments_InvoiceLineItemDto` {#schema-payments-invoicelineitemdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (18)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `base_amount` | `number` (`double`) | `required` |  |
| `created_at` | `string` (`date-time`) | `required` |  |
| `currency` | `string` | `required` |  |
| `gross_amount` | `number` (`double`) | `required` |  |
| `id` | `string` | `required` |  |
| `invoice_id` | `string` | `required` |  |
| `net_amount` | `number` (`double`) | `required` |  |
| `overhead_amount` | `number` (`double`) | `required` |  |
| `patient_stipend` | `number` (`double`) | `required` |  |
| `payable_id` | `string` \| `null` | `required` |  |
| `procedure_code` | `string` \| `null` | `required` |  |
| `procedure_name` | `string` \| `null` | `required` |  |
| `subject_id` | `string` | `required` |  |
| `travel_reimbursement` | `number` (`double`) | `required` |  |
| `version_index` | `integer` (`int32`) | `required` |  |
| `visit_code` | `string` | `required` |  |
| `withholding_amount` | `number` (`double`) | `required` |  |
| `withholding_rate` | `number` (`double`) | `required` |  |

</details>

### `Payments_IssueCardRequestDto` {#schema-payments-issuecardrequestdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (1)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `cardType` | `string` \| `null` | `optional` |  |

</details>

### `Payments_OcrExtractRequestDto` {#schema-payments-ocrextractrequestdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `categoryHint` | `string` \| `null` | `optional` |  |
| `filename` | `string` \| `null` | `optional` |  |
| `imageBase64` | `string` \| `null` | `optional` |  |
| `rawText` | `string` \| `null` | `optional` |  |

</details>

### `Payments_ParticipantDisbursement` {#schema-payments-participantdisbursement}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (11)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `amount` | `number` (`double`) | `optional` |  |
| `createdAt` | `string` (`date-time`) | `required` |  |
| `createdBy` | `string` | `required` |  |
| `disbursementType` | `string` | `optional` |  |
| `id` | `string` | `optional` |  |
| `idempotencyKey` | `string` \| `null` | `optional` |  |
| `referenceNumber` | `string` | `optional` |  |
| `status` | `string` | `optional` |  |
| `subjectId` | `string` | `optional` |  |
| `versionIndex` | `integer` (`int32`) | `optional` |  |
| `walletId` | `string` | `optional` |  |

</details>

### `Payments_ParticipantExpenseClaim` {#schema-payments-participantexpenseclaim}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (25)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `category` | `string` | `optional` |  |
| `createdAt` | `string` (`date-time`) | `required` |  |
| `createdBy` | `string` | `required` |  |
| `disbursementReference` | `string` \| `null` | `optional` |  |
| `expenseDate` | `string` | `optional` |  |
| `grossAmount` | `number` (`double`) | `optional` |  |
| `id` | `string` | `optional` |  |
| `idempotencyKey` | `string` \| `null` | `optional` |  |
| `isWalletCredited` | `boolean` | `optional` |  |
| `merchantName` | `string` | `optional` |  |
| `netAmount` | `number` (`double`) | `optional` |  |
| `reasonForChange` | `string` \| `null` | `optional` |  |
| `receiptImageName` | `string` \| `null` | `optional` |  |
| `receiptOcrDataJson` | `string` \| `null` | `optional` |  |
| `reviewNotes` | `string` \| `null` | `optional` |  |
| `reviewedAt` | `string` (`date-time`) \| `null` | `optional` |  |
| `reviewedBy` | `string` \| `null` | `optional` |  |
| `siteId` | `string` | `optional` |  |
| `status` | `string` | `optional` |  |
| `studyId` | `string` | `optional` |  |
| `subjectId` | `string` | `optional` |  |
| `taxDeduction` | `number` (`double`) | `optional` |  |
| `updatedAt` | `string` (`date-time`) | `required` |  |
| `versionIndex` | `integer` (`int32`) | `optional` |  |
| `walletId` | `string` | `optional` |  |

</details>

### `Payments_ParticipantStipendRule` {#schema-payments-participantstipendrule}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (19)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `amount` | `number` (`double`) | `optional` |  |
| `antiCoercionCap` | `number` (`double`) \| `null` | `optional` |  |
| `autoDisbursementThreshold` | `number` (`double`) \| `null` | `optional` |  |
| `createdAt` | `string` (`date-time`) | `required` |  |
| `createdBy` | `string` | `required` |  |
| `currency` | `string` | `optional` |  |
| `id` | `string` | `optional` |  |
| `isActive` | `boolean` | `optional` |  |
| `isMilestone` | `boolean` | `optional` |  |
| `milestoneCode` | `string` \| `null` | `optional` |  |
| `procedureCode` | `string` \| `null` | `optional` |  |
| `reasonForChange` | `string` \| `null` | `optional` |  |
| `requiresVerification` | `boolean` | `optional` |  |
| `studyId` | `string` | `optional` |  |
| `taxable` | `boolean` | `optional` |  |
| `updatedAt` | `string` (`date-time`) | `required` |  |
| `versionIndex` | `integer` (`int32`) | `optional` |  |
| `visitCode` | `string` | `optional` |  |
| `visitName` | `string` | `optional` |  |

</details>

### `Payments_ParticipantStipendRuleCreateDto` {#schema-payments-participantstipendrulecreatedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (12)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `amount` | `number` (`double`) | `required` |  |
| `antiCoercionCap` | `number` (`double`) \| `null` | `optional` |  |
| `autoDisbursementThreshold` | `number` (`double`) \| `null` | `optional` |  |
| `currency` | `string` \| `null` | `optional` |  |
| `isMilestone` | `boolean` | `optional` |  |
| `milestoneCode` | `string` \| `null` | `optional` |  |
| `procedureCode` | `string` \| `null` | `required` |  |
| `requiresVerification` | `boolean` | `optional` |  |
| `studyId` | `string` | `required` |  |
| `taxable` | `boolean` | `optional` |  |
| `visitCode` | `string` | `required` |  |
| `visitName` | `string` | `required` |  |

</details>

### `Payments_ParticipantTransaction` {#schema-payments-participanttransaction}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (13)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `amount` | `number` (`double`) | `optional` |  |
| `balanceAfter` | `number` (`double`) | `optional` |  |
| `createdAt` | `string` (`date-time`) | `required` |  |
| `createdBy` | `string` | `required` |  |
| `description` | `string` | `optional` |  |
| `id` | `string` | `optional` |  |
| `idempotencyKey` | `string` \| `null` | `optional` |  |
| `referenceId` | `string` | `optional` |  |
| `studyId` | `string` | `optional` |  |
| `subjectId` | `string` | `optional` |  |
| `transactionType` | `string` | `optional` |  |
| `versionIndex` | `integer` (`int32`) | `optional` |  |
| `walletId` | `string` | `optional` |  |

</details>

### `Payments_PaymentAuditLog` {#schema-payments-paymentauditlog}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (13)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `action` | `string` | `optional` |  |
| `createdAt` | `string` (`date-time`) | `required` |  |
| `createdBy` | `string` | `required` |  |
| `details` | `string` \| `null` | `optional` |  |
| `entityId` | `string` | `optional` |  |
| `entityType` | `string` | `optional` |  |
| `id` | `string` | `optional` |  |
| `newStateJson` | `string` \| `null` | `optional` |  |
| `previousStateJson` | `string` \| `null` | `optional` |  |
| `reasonForChange` | `string` \| `null` | `optional` |  |
| `userId` | `string` | `optional` |  |
| `userRoles` | `string` | `optional` |  |
| `versionIndex` | `integer` (`int32`) | `optional` |  |

</details>

### `Payments_PaymentAuditLogDto` {#schema-payments-paymentauditlogdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `action` | `string` | `required` |  |
| `created_at` | `string` (`date-time`) | `required` |  |
| `created_by` | `string` | `required` |  |
| `details` | `string` \| `null` | `required` |  |
| `entity_id` | `string` | `required` |  |
| `entity_type` | `string` | `required` |  |
| `id` | `string` | `required` |  |
| `user_id` | `string` | `required` |  |

</details>

### `Payments_ProblemDetails` {#schema-payments-problemdetails}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `detail` | `string` \| `null` | `optional` |  |
| `instance` | `string` \| `null` | `optional` |  |
| `status` | `integer` (`int32`) \| `null` | `optional` |  |
| `title` | `string` \| `null` | `optional` |  |
| `type` | `string` \| `null` | `optional` |  |

</details>

### `Payments_ReceiptOcrExtractResponseDto` {#schema-payments-receiptocrextractresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (12)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `category` | `string` | `required` |  |
| `confidenceScore` | `number` (`double`) | `required` |  |
| `expenseDate` | `string` | `required` |  |
| `grossAmount` | `number` (`double`) | `required` |  |
| `merchantName` | `string` | `required` |  |
| `netAmount` | `number` (`double`) | `required` |  |
| `ocrReadable` | `boolean` | `required` |  |
| `ocrToken` | `string` \| `null` | `optional` |  |
| `rawText` | `string` | `required` |  |
| `receiptSha256` | `string` \| `null` | `optional` |  |
| `taxDeduction` | `number` (`double`) | `required` |  |
| `warning` | `string` \| `null` | `optional` |  |

</details>

### `Payments_ReconcileInvoiceRequest` {#schema-payments-reconcileinvoicerequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `amount_paid` | `number` (`double`) \| `null` | `optional` |  |
| `erp_system` | `string` \| `null` | `optional` |  |
| `notes` | `string` \| `null` | `optional` |  |
| `payment_date` | `string` \| `null` | `optional` |  |
| `payment_reference` | `string` | `required` |  |

</details>

### `Payments_RemittanceAdviceDto` {#schema-payments-remittanceadvicedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (19)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `created_at` | `string` (`date-time`) | `required` |  |
| `currency` | `string` | `required` |  |
| `dispatch_status` | `string` | `required` |  |
| `gross_amount` | `number` (`double`) | `required` |  |
| `holdback_amount` | `number` (`double`) | `required` |  |
| `id` | `string` | `required` |  |
| `invoice_id` | `string` | `required` |  |
| `is_sealed` | `boolean` | `required` |  |
| `is_signed` | `boolean` | `required` |  |
| `net_amount` | `number` (`double`) | `required` |  |
| `overhead_amount` | `number` (`double`) | `required` |  |
| `pdf_sha256_hash` | `string` | `required` |  |
| `remittance_number` | `string` | `required` |  |
| `signed_at` | `string` (`date-time`) \| `null` | `required` |  |
| `signed_by` | `string` \| `null` | `required` |  |
| `signer_role` | `string` \| `null` | `required` |  |
| `site_id` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `version_index` | `integer` (`int32`) | `required` |  |

</details>

### `Payments_ReviewExpenseClaimRequestDto` {#schema-payments-reviewexpenseclaimrequestdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `adjustedAmount` | `number` (`double`) \| `null` | `optional` |  |
| `reviewNotes` | `string` \| `null` | `optional` |  |
| `status` | `string` | `required` |  |

</details>

### `Payments_SelfBillingInvoiceResponse` {#schema-payments-selfbillinginvoiceresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (34)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `approved_at` | `string` (`date-time`) \| `null` | `required` |  |
| `approved_by` | `string` \| `null` | `required` |  |
| `billing_cycle` | `string` | `required` |  |
| `contract_id` | `string` | `required` |  |
| `created_at` | `string` (`date-time`) | `required` |  |
| `created_by` | `string` | `required` |  |
| `currency` | `string` | `required` |  |
| `erp_reference_id` | `string` \| `null` | `required` |  |
| `erp_sync_status` | `string` | `required` |  |
| `erp_synced_at` | `string` (`date-time`) \| `null` | `required` |  |
| `erp_system` | `string` \| `null` | `required` |  |
| `id` | `string` | `required` |  |
| `invoice_number` | `string` | `required` |  |
| `line_items` | array of [`Payments_InvoiceLineItemDto`](#schema-payments-invoicelineitemdto) | `required` |  |
| `payable_count` | `integer` (`int32`) | `required` |  |
| `period_end` | `string` | `required` |  |
| `period_start` | `string` | `required` |  |
| `reason_for_change` | `string` \| `null` | `required` |  |
| `reconciled_at` | `string` (`date-time`) \| `null` | `required` |  |
| `reconciled_by` | `string` \| `null` | `required` |  |
| `reconciliation_notes` | `string` \| `null` | `required` |  |
| `reconciliation_reference` | `string` \| `null` | `required` |  |
| `remittance_advice` | [`Payments_RemittanceAdviceDto`](#schema-payments-remittanceadvicedto) \| `null` | `required` |  |
| `site_id` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `total_base_amount` | `number` (`double`) | `required` |  |
| `total_gross_amount` | `number` (`double`) | `required` |  |
| `total_holdback_amount` | `number` (`double`) | `required` |  |
| `total_net_amount` | `number` (`double`) | `required` |  |
| `total_overhead_amount` | `number` (`double`) | `required` |  |
| `total_patient_stipend` | `number` (`double`) | `required` |  |
| `total_travel_reimbursement` | `number` (`double`) | `required` |  |
| `version_index` | `integer` (`int32`) | `required` |  |

</details>

### `Payments_SelfServiceDiaryStipendRequestDto` {#schema-payments-selfservicediarystipendrequestdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (9)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `assignmentId` | `string` \| `null` | `optional` |  |
| `diaryId` | `string` \| `null` | `optional` |  |
| `idempotencyKey` | `string` \| `null` | `optional` |  |
| `sequenceNumber` | `integer` (`int32`) \| `null` | `optional` |  |
| `siteId` | `string` \| `null` | `optional` |  |
| `studyId` | `string` \| `null` | `optional` |  |
| `subjectId` | `string` \| `null` | `optional` |  |
| `syncVerificationToken` | `string` \| `null` | `optional` |  |
| `walletId` | `string` \| `null` | `optional` |  |

</details>

### `Payments_SelfServiceDiaryStipendResponseDto` {#schema-payments-selfservicediarystipendresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (14)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `creditedAmount` | `number` (`double`) | `required` |  |
| `currency` | `string` | `required` |  |
| `currentBalance` | `number` (`double`) | `required` |  |
| `idempotencyKey` | `string` | `required` |  |
| `lifetimeStipends` | `number` (`double`) | `required` |  |
| `reason` | `string` \| `null` | `optional` |  |
| `referenceNumber` | `string` \| `null` | `required` |  |
| `requiresReview` | `boolean` | `optional` |  |
| `status` | `string` | `required` |  |
| `subjectId` | `string` | `required` |  |
| `success` | `boolean` | `required` |  |
| `transactionId` | `string` \| `null` | `required` |  |
| `w9Status` | `string` \| `null` | `optional` |  |
| `walletId` | `string` | `required` |  |

</details>

### `Payments_SelfServiceDisburseRequestDto` {#schema-payments-selfservicedisburserequestdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `amount` | `number` (`double`) \| `null` | `optional` |  |
| `disbursementType` | `string` \| `null` | `optional` |  |
| `idempotencyKey` | `string` \| `null` | `optional` |  |
| `subjectId` | `string` \| `null` | `optional` |  |
| `walletId` | `string` \| `null` | `optional` |  |

</details>

### `Payments_SelfServiceDisburseResponseDto` {#schema-payments-selfservicedisburseresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (12)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `amount` | `number` (`double`) | `required` |  |
| `createdAt` | `string` (`date-time`) | `required` |  |
| `currency` | `string` | `required` |  |
| `currentBalance` | `number` (`double`) | `required` |  |
| `destinationMask` | `string` | `required` |  |
| `disbursementType` | `string` | `required` |  |
| `id` | `string` | `required` |  |
| `lifetimeDisbursements` | `number` (`double`) | `required` |  |
| `referenceNumber` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `subjectId` | `string` | `required` |  |
| `walletId` | `string` | `required` |  |

</details>

### `Payments_SelfServiceExpenseClaimResponseDto` {#schema-payments-selfserviceexpenseclaimresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (22)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `approvalDecision` | `string` | `required` |  |
| `autoApproved` | `boolean` | `required` |  |
| `category` | `string` | `required` |  |
| `createdAt` | `string` (`date-time`) | `required` |  |
| `disbursementReference` | `string` \| `null` | `required` |  |
| `expenseDate` | `string` | `required` |  |
| `grossAmount` | `number` (`double`) | `required` |  |
| `id` | `string` | `required` |  |
| `lifetimeDisbursements` | `number` (`double`) | `required` |  |
| `lifetimeReimbursements` | `number` (`double`) | `required` |  |
| `lifetimeStipends` | `number` (`double`) | `required` |  |
| `merchantName` | `string` | `required` |  |
| `netAmount` | `number` (`double`) | `required` |  |
| `receiptImageName` | `string` \| `null` | `required` |  |
| `reviewNotes` | `string` \| `null` | `required` |  |
| `siteId` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `studyId` | `string` | `required` |  |
| `subjectId` | `string` | `required` |  |
| `taxDeduction` | `number` (`double`) | `required` |  |
| `walletCurrentBalance` | `number` (`double`) | `required` |  |
| `walletId` | `string` | `required` |  |

</details>

### `Payments_SelfServiceSubmitExpenseRequestDto` {#schema-payments-selfservicesubmitexpenserequestdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (16)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `category` | `string` | `required` |  |
| `expenseDate` | `string` \| `null` | `optional` |  |
| `grossAmount` | `number` (`double`) | `required` |  |
| `idempotencyKey` | `string` \| `null` | `optional` |  |
| `isMealAllowance` | `boolean` | `optional` |  |
| `merchantName` | `string` | `required` |  |
| `ocrConfidence` | `number` (`double`) \| `null` | `optional` |  |
| `ocrToken` | `string` \| `null` | `optional` |  |
| `perDiemCap` | `number` (`double`) \| `null` | `optional` |  |
| `receiptImageBase64` | `string` \| `null` | `optional` |  |
| `receiptImageName` | `string` \| `null` | `optional` |  |
| `receiptOcrData` | `any` | `optional` |  |
| `siteId` | `string` \| `null` | `optional` |  |
| `studyId` | `string` \| `null` | `optional` |  |
| `subjectId` | `string` \| `null` | `optional` |  |
| `walletId` | `string` \| `null` | `optional` |  |

</details>

### `Payments_SelfServiceWalletSummaryResponseDto` {#schema-payments-selfservicewalletsummaryresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `expenseClaims` | array of [`Payments_ParticipantExpenseClaim`](#schema-payments-participantexpenseclaim) | `required` |  |
| `recentTransactions` | array of [`Payments_ParticipantTransaction`](#schema-payments-participanttransaction) | `required` |  |
| `wallet` | [`Payments_WalletResponse`](#schema-payments-walletresponse) | `required` |  |

</details>

### `Payments_SignRemittanceRequest` {#schema-payments-signremittancerequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (1)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `signature_reason` | `string` \| `null` | `optional` |  |

</details>

### `Payments_SiteBatchCompilationResult` {#schema-payments-sitebatchcompilationresult}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (9)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `error_message` | `string` \| `null` | `required` |  |
| `invoice_id` | `string` \| `null` | `required` |  |
| `invoice_number` | `string` \| `null` | `required` |  |
| `payable_count` | `integer` (`int32`) | `required` |  |
| `site_id` | `string` | `required` |  |
| `success` | `boolean` | `required` |  |
| `total_gross_amount` | `number` (`double`) | `required` |  |
| `total_holdback_amount` | `number` (`double`) | `required` |  |
| `total_net_amount` | `number` (`double`) | `required` |  |

</details>

### `Payments_SubmitExpenseClaimRequestDto` {#schema-payments-submitexpenseclaimrequestdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (14)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `category` | `string` | `required` |  |
| `expenseDate` | `string` \| `null` | `optional` |  |
| `grossAmount` | `number` (`double`) | `required` |  |
| `isMealAllowance` | `boolean` | `optional` |  |
| `merchantName` | `string` | `required` |  |
| `ocrConfidence` | `number` (`double`) \| `null` | `optional` |  |
| `ocrToken` | `string` \| `null` | `optional` |  |
| `perDiemCap` | `number` (`double`) \| `null` | `optional` |  |
| `receiptImageBase64` | `string` \| `null` | `optional` |  |
| `receiptImageName` | `string` \| `null` | `optional` |  |
| `receiptOcrData` | `any` | `optional` |  |
| `siteId` | `string` | `required` |  |
| `studyId` | `string` | `required` |  |
| `subjectId` | `string` | `required` |  |

</details>

### `Payments_SubmitW9RequestDto` {#schema-payments-submitw9requestdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `address` | `string` \| `null` | `optional` |  |
| `certifyAccurate` | `boolean` | `optional` |  |
| `fullLegalName` | `string` | `required` |  |
| `taxIdLastFour` | `string` | `required` |  |

</details>

### `Payments_Tax1099StatusResponseDto` {#schema-payments-tax1099statusresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `is_1099_threshold_exceeded` | `boolean` | `required` |  |
| `subject_id` | `string` | `required` |  |
| `taxable_ytd` | `number` (`double`) | `required` |  |
| `threshold_amount` | `number` (`double`) | `required` |  |
| `threshold_percentage` | `number` (`double`) | `required` |  |
| `w9_status` | `string` | `required` |  |
| `wallet_id` | `string` | `required` |  |
| `warning_level` | `string` | `required` |  |

</details>

### `Payments_VerifyW9RequestDto` {#schema-payments-verifyw9requestdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (1)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `notes` | `string` \| `null` | `optional` |  |

</details>

### `Payments_VisitVerificationEventDto` {#schema-payments-visitverificationeventdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `eventId` | `string` | `required` |  |
| `siteId` | `string` | `required` |  |
| `studyId` | `string` | `required` |  |
| `subjectId` | `string` | `required` |  |
| `visitCode` | `string` | `required` |  |

</details>

### `Payments_VisitVerificationResponseDto` {#schema-payments-visitverificationresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (10)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `amount` | `number` (`double`) | `required` |  |
| `currentBalance` | `number` (`double`) | `required` |  |
| `eventId` | `string` | `required` |  |
| `reason` | `string` \| `null` | `optional` |  |
| `requiresReview` | `boolean` | `optional` |  |
| `status` | `string` | `required` |  |
| `subjectId` | `string` | `required` |  |
| `transactionId` | `string` \| `null` | `required` |  |
| `w9Status` | `string` \| `null` | `optional` |  |
| `walletId` | `string` | `required` |  |

</details>

### `Payments_WalletBalanceInquiryResponseDto` {#schema-payments-walletbalanceinquiryresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `available_balance` | `number` (`double`) | `required` |  |
| `lifetime_disbursements` | `number` (`double`) | `required` |  |
| `lifetime_reimbursements` | `number` (`double`) | `required` |  |
| `lifetime_stipends` | `number` (`double`) | `required` |  |
| `pending_claims_total` | `number` (`double`) | `required` |  |
| `subject_id` | `string` | `required` |  |
| `taxable_ytd` | `number` (`double`) | `required` |  |
| `wallet_id` | `string` | `required` |  |

</details>

### `Payments_WalletResponse` {#schema-payments-walletresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (24)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `achAccountHolder` | `string` \| `null` | `required` |  |
| `achAccountMask` | `string` \| `null` | `required` |  |
| `achRoutingMask` | `string` \| `null` | `optional` |  |
| `achRoutingNumber` | `string` \| `null` | `required` |  |
| `antiCoercionCap` | `number` (`double`) \| `null` | `required` |  |
| `autoDisbursementThreshold` | `number` (`double`) \| `null` | `required` |  |
| `cardStatus` | `string` | `optional` |  |
| `cardType` | `string` | `optional` |  |
| `currency` | `string` | `required` |  |
| `currentBalance` | `number` (`double`) | `required` |  |
| `id` | `string` | `required` |  |
| `is1099ThresholdExceeded` | `boolean` | `required` |  |
| `lifetimeDisbursements` | `number` (`double`) | `required` |  |
| `lifetimeReimbursements` | `number` (`double`) | `required` |  |
| `lifetimeStipends` | `number` (`double`) | `required` |  |
| `siteId` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `studyId` | `string` | `required` |  |
| `subjectId` | `string` | `required` |  |
| `taxableYtd` | `number` (`double`) | `required` |  |
| `updatedAt` | `string` (`date-time`) | `required` |  |
| `virtualCardExpiry` | `string` \| `null` | `required` |  |
| `virtualCardMask` | `string` \| `null` | `required` |  |
| `w9Status` | `string` | `required` |  |

</details>

