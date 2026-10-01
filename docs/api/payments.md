# payments API

Generated from `docs/openapi/payments_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `GET` | `/api/v1/payments/audit-logs` | GetPaymentAuditLogs | Retrieve payment audit ledger records. |
| `POST` | `/api/v1/payments/disburse` | DisburseStipendDirect | Direct disburse ePRO stipend to participant wallet with strict idempotency and audit trail. |
| `POST` | `/api/v1/payments/participant/wallets/disburse` | DisburseStipendWallet | Disburse stipend to participant wallet. |
| `GET` | `/api/v1/payments/participant/wallets/subject/{subjectId}` | GetSubjectWallet | Retrieve participant wallet balance and details. |
| `GET` | `/api/v1/payments/status` | PaymentsStatus | Returns Payments operational status. |
| `GET` | `/healthz` | PaymentsHealthCheck | Site and participant payments health probe. |
