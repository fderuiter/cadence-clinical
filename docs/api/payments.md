# payments API

Generated from `docs/openapi/payments_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `GET` | `/api/v1/payments/audit-logs` | GetPaymentAuditLogs | Retrieve payment audit ledger records. |
| `POST` | `/api/v1/payments/disburse` | DisburseStipendDirect | Direct disburse ePRO stipend to participant wallet with strict idempotency and audit trail. |
| `GET` | `/api/v1/payments/invoices` | ListInvoices | Lists compiled self-billing invoices matching filter criteria. |
| `POST` | `/api/v1/payments/invoices/batch-compile` | BatchCompileBillingCycles | Executes study-wide batch milestone invoice compilation across all active trial sites. |
| `POST` | `/api/v1/payments/invoices/compile` | CompileBillingCycle | Automates single-site billing cycle compilation into a self-billing invoice. |
| `GET` | `/api/v1/payments/invoices/{id}` | GetInvoiceById | Retrieve single self-billing invoice details with line items. |
| `POST` | `/api/v1/payments/invoices/{id}/approve` | ApproveInvoice | Approve draft self-billing invoice for ERP export and settlement. |
| `POST` | `/api/v1/payments/invoices/{id}/erp-export` | ExportInvoiceToErp | Exports self-billing invoice to ERP gateway (e.g. SAP). |
| `POST` | `/api/v1/payments/invoices/{id}/reconcile` | ReconcileInvoice | Reconciles invoice settlement against bank wire or ERP clearing statement. |
| `GET` | `/api/v1/payments/invoices/{id}/remittance-advice/pdf` | DownloadRemittancePdf | Downloads cryptographic PDF remittance statement. |
| `POST` | `/api/v1/payments/invoices/{id}/remittance-advice/sign` | SignRemittanceAdvice | Applies 21 CFR Part 11 electronic signature to cryptographic remittance advice. |
| `GET` | `/api/v1/payments/participant/disbursements` | ListDisbursements | List digital disbursement dispatches. |
| `POST` | `/api/v1/payments/participant/disbursements/{id}/settle` | SettleDisbursement | Marks a dispatched digital disbursement as settled upon confirmation. |
| `POST` | `/api/v1/payments/participant/events/visit-verified` | IngestVisitVerifiedEvent | Receives verified eCRF visit completion event and evaluates automated stipend disbursement rules. |
| `GET` | `/api/v1/payments/participant/expenses` | ListExpenseClaims | List travel and meal expense reimbursement claims. |
| `POST` | `/api/v1/payments/participant/expenses` | SubmitExpenseClaim | Submit travel/meal expense claim for coordinator review. |
| `POST` | `/api/v1/payments/participant/expenses/ocr-extract` | ExtractReceiptOcr | Extract metadata from receipt image/text using rule-based OCR engine. |
| `POST` | `/api/v1/payments/participant/expenses/{claimId}/review` | ReviewExpenseClaim | Coordinator 1-click review and approval/rejection of expense claim. |
| `GET` | `/api/v1/payments/participant/stipend-rules` | ListStipendRules | List SoA protocol visit stipend compensation rules. |
| `POST` | `/api/v1/payments/participant/stipend-rules` | CreateStipendRule | Configure participant visit compensation rules linked to Schedule of Activities. |
| `GET` | `/api/v1/payments/participant/transactions` | ListTransactions | List participant wallet transactions ledger. |
| `GET` | `/api/v1/payments/participant/wallets` | ListWallets | List all participant wallets. |
| `POST` | `/api/v1/payments/participant/wallets/disburse` | DisburseStipendWallet | Disburse stipend to participant wallet. |
| `GET` | `/api/v1/payments/participant/wallets/subject/{subjectId}` | GetSubjectWallet | Retrieve participant wallet balance and details. |
| `GET` | `/api/v1/payments/participant/wallets/{walletId}` | GetWalletById | Retrieve participant wallet details by wallet ID. |
| `GET` | `/api/v1/payments/participant/wallets/{walletId}/balance` | GetBalanceInquiry | Real-time balance inquiry for participant wallet. |
| `POST` | `/api/v1/payments/participant/wallets/{walletId}/disburse` | DisburseParticipantFunds | Disburse digital funds from participant wallet to debit card or bank account. |
| `POST` | `/api/v1/payments/participant/wallets/{walletId}/issue-card` | IssueReloadableCard | Issue or re-issue reloadable debit card for participant wallet. |
| `POST` | `/api/v1/payments/participant/wallets/{walletId}/submit-w9` | SubmitW9Form | Submit Form W-9 taxpayer identification certification. |
| `GET` | `/api/v1/payments/participant/wallets/{walletId}/tax-1099-status` | GetTax1099Status | Retrieve IRS 1099 tax status and threshold monitoring tracer. |
| `POST` | `/api/v1/payments/participant/wallets/{walletId}/verify-w9` | VerifyW9Form | Verify participant Form W-9 submission. |
| `GET` | `/api/v1/payments/status` | PaymentsStatus | Returns Payments operational status. |
| `GET` | `/healthz` | PaymentsHealthCheck | Site and participant payments health probe. |
