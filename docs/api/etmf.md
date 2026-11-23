# etmf API

Generated from `docs/openapi/etmf_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

The service exposes 17 operations.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `GET` | `/api/v1/etmf/documents` |  |  |
| `POST` | `/api/v1/etmf/documents/export` |  |  |
| `GET` | `/api/v1/etmf/documents/{document_id}` |  |  |
| `GET` | `/api/v1/etmf/documents/{document_id}/content` |  |  |
| `POST` | `/api/v1/etmf/documents/{document_id}/submit-qc` |  |  |
| `PATCH` | `/api/v1/etmf/documents/{document_id}/taxonomy` |  |  |
| `GET` | `/api/v1/etmf/export-jobs` |  |  |
| `GET` | `/api/v1/etmf/export-jobs/{job_id}` |  |  |
| `GET` | `/api/v1/etmf/export-jobs/{job_id}/download` |  |  |
| `POST` | `/api/v1/etmf/ingest` |  |  |
| `POST` | `/api/v1/etmf/intelligence/classify` |  |  |
| `GET` | `/api/v1/etmf/intelligence/qc-queue` |  |  |
| `POST` | `/api/v1/etmf/intelligence/qc-review/{doc_id}` |  |  |
| `POST` | `/api/v1/etmf/intelligence/stage-qc` |  |  |
| `GET` | `/api/v1/etmf/status` | EtmfStatus | Returns eTMF operational status. |
| `GET` | `/api/v1/etmf/taxonomy` |  |  |
| `GET` | `/healthz` | EtmfHealthCheck | eTMF operations health probe. |
