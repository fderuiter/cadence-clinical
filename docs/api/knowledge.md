# knowledge API

Generated from `docs/openapi/knowledge_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `GET` | `/api/v1/knowledge/status` | KnowledgeStatus | Returns Knowledge base operational status. |
| `GET` | `/api/v1/training/assignments` | ListAssignments |  |
| `GET` | `/api/v1/training/assignments/{assignment_id}` | GetAssignmentDetail |  |
| `GET` | `/api/v1/training/curricula` | ListCurricula |  |
| `POST` | `/api/v1/training/curricula/batch-assign` | BatchAssignCurriculum |  |
| `GET` | `/api/v1/training/curricula/{curriculum_id}` | GetCurriculum |  |
| `GET` | `/api/v1/training/delegations` | ListDelegations |  |
| `POST` | `/api/v1/training/delegations/bulk-signoff` | BulkSignOffDelegations |  |
| `GET` | `/healthz` | KnowledgeHealthCheck | Knowledge base and study training health probe. |
