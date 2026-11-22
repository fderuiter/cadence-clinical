# knowledge API

Generated from `docs/openapi/knowledge_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `GET` | `/api/v1/knowledge/articles` | ListKnowledgeArticles |  |
| `GET` | `/api/v1/knowledge/articles/{article_id}` | GetKnowledgeArticle |  |
| `GET` | `/api/v1/knowledge/categories` | ListKnowledgeCategories |  |
| `GET` | `/api/v1/knowledge/contextual-help` | ResolveContextualHelp |  |
| `GET` | `/api/v1/knowledge/status` | KnowledgeStatus | Returns Knowledge base operational status. |
| `GET` | `/api/v1/training/assignments` | ListAssignments |  |
| `GET` | `/api/v1/training/assignments/{assignment_id}` | GetAssignmentDetail |  |
| `POST` | `/api/v1/training/assignments/{assignment_id}/complete` | CompleteTrainingAssignment |  |
| `GET` | `/api/v1/training/curricula` | ListCurricula |  |
| `POST` | `/api/v1/training/curricula` | CreateCurriculum |  |
| `POST` | `/api/v1/training/curricula/batch-assign` | BatchAssignCurriculum |  |
| `GET` | `/api/v1/training/curricula/{curriculum_id}` | GetCurriculum |  |
| `GET` | `/api/v1/training/delegations` | ListDelegations |  |
| `POST` | `/api/v1/training/delegations/bulk-signoff` | BulkSignOffDelegations |  |
| `GET` | `/healthz` | KnowledgeHealthCheck | Knowledge base and study training health probe. |
