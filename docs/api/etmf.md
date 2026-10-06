# etmf API

Generated from `docs/openapi/etmf_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `GET` | `/api/v1/etmf/audit-logs` |  |  |
| `POST` | `/api/v1/etmf/audit-logs/verify-chain` |  |  |
| `GET` | `/api/v1/etmf/completeness` |  |  |
| `GET` | `/api/v1/etmf/documents` |  |  |
| `GET` | `/api/v1/etmf/documents/{document_id}` |  |  |
| `POST` | `/api/v1/etmf/documents/{document_id}/sign-off` |  |  |
| `POST` | `/api/v1/etmf/documents/{document_id}/verify-signature` |  |  |
| `GET` | `/api/v1/etmf/documents/{document_id}/watermark` |  |  |
| `POST` | `/api/v1/etmf/ingest` |  |  |
| `POST` | `/api/v1/etmf/intelligence/classify` |  |  |
| `GET` | `/api/v1/etmf/intelligence/qc-queue` |  |  |
| `POST` | `/api/v1/etmf/intelligence/qc-review/{doc_id}` |  |  |
| `POST` | `/api/v1/etmf/intelligence/stage-qc` |  |  |
| `GET` | `/api/v1/etmf/status` | EtmfStatus | Returns eTMF operational status. |
| `GET` | `/api/v1/etmf/studies/{study_id}/binder` |  |  |
| `GET` | `/api/v1/etmf/studies/{study_id}/binder/structure` |  |  |
| `GET` | `/api/v1/etmf/studies/{study_id}/ems-export` |  |  |
| `GET` | `/api/v1/etmf/studies/{study_id}/inspection-readiness` |  |  |
| `GET` | `/api/v1/etmf/taxonomy` |  |  |
| `GET` | `/healthz` | EtmfHealthCheck | eTMF operations health probe. |
