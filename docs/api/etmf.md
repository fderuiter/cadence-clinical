# etmf API

Generated from `docs/openapi/etmf_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path                                           | Operation       | Summary                          |
| ------ | ---------------------------------------------- | --------------- | -------------------------------- |
| `GET`  | `/api/v1/etmf/documents`                       |                 |                                  |
| `GET`  | `/api/v1/etmf/documents/{document_id}`         |                 |                                  |
| `POST` | `/api/v1/etmf/ingest`                          |                 |                                  |
| `POST` | `/api/v1/etmf/intelligence/classify`           |                 |                                  |
| `GET`  | `/api/v1/etmf/intelligence/qc-queue`           |                 |                                  |
| `POST` | `/api/v1/etmf/intelligence/qc-review/{doc_id}` |                 |                                  |
| `POST` | `/api/v1/etmf/intelligence/stage-qc`           |                 |                                  |
| `GET`  | `/api/v1/etmf/status`                          | EtmfStatus      | Returns eTMF operational status. |
| `GET`  | `/api/v1/etmf/taxonomy`                        |                 |                                  |
| `GET`  | `/healthz`                                     | EtmfHealthCheck | eTMF operations health probe.    |
