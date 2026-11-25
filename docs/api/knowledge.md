# knowledge API

Generated from `docs/openapi/knowledge_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

- **Service Maturity:** <span class="VPBadge tip">Active</span> (Port `8015` · Gateway prefixes: `/api/v1/knowledge`, `/api/v1/training`)
- **OpenAPI 3.1 Contract:** [`knowledge_openapi.json`](../openapi/knowledge_openapi.json) (15 operation(s), 18 schema(s))

## Operations

| Method | Path | Operation | Summary | Contract |
| ------ | ---- | --------- | ------- | -------- |
| `GET` | `/api/v1/knowledge/articles` | ListKnowledgeArticles | Searches and lists published SOP knowledge base articles. | [Details](#op-listknowledgearticles) |
| `GET` | `/api/v1/knowledge/articles/{article_id}` | GetKnowledgeArticle | Retrieves a published SOP knowledge base article by article ID. | [Details](#op-getknowledgearticle) |
| `GET` | `/api/v1/knowledge/articles/{article_id}/audit-log` | GetKnowledgeArticleAuditLog | Retrieves the 21 CFR Part 11 hash-chained audit log for a knowledge base article. | [Details](#op-getknowledgearticleauditlog) |
| `GET` | `/api/v1/knowledge/articles/{article_id}/versions` | ListKnowledgeArticleVersions | Lists immutable version snapshots for a knowledge base article. | [Details](#op-listknowledgearticleversions) |
| `GET` | `/api/v1/knowledge/categories` | ListKnowledgeCategories | Lists knowledge base SOP categories visible to the caller's clinical persona. | [Details](#op-listknowledgecategories) |
| `GET` | `/api/v1/knowledge/contextual-help` | ResolveContextualHelp | Resolves persona-filtered contextual SOP guidance for the active workspace route. | [Details](#op-resolvecontextualhelp) |
| `GET` | `/api/v1/knowledge/status` | KnowledgeStatus | Returns Knowledge base operational status. | [Details](#op-knowledgestatus) |
| `GET` | `/api/v1/training/assignments` | ListAssignments | Lists study training curriculum assignments filtered by study, site, or assignee. | [Details](#op-listassignments) |
| `GET` | `/api/v1/training/assignments/{assignment_id}` | GetAssignmentDetail | Retrieves a training assignment and completion status by assignment ID. | [Details](#op-getassignmentdetail) |
| `GET` | `/api/v1/training/curricula` | ListCurricula | Lists active study training curricula visible within the caller's study scope. | [Details](#op-listcurricula) |
| `POST` | `/api/v1/training/curricula/batch-assign` | BatchAssignCurriculum | Assigns a training curriculum to a batch of study staff members. | [Details](#op-batchassigncurriculum) |
| `GET` | `/api/v1/training/curricula/{curriculum_id}` | GetCurriculum | Retrieves a study training curriculum by curriculum ID. | [Details](#op-getcurriculum) |
| `GET` | `/api/v1/training/delegations` | ListDelegations | Lists site delegation-of-authority records and PI sign-off status. | [Details](#op-listdelegations) |
| `POST` | `/api/v1/training/delegations/bulk-signoff` | BulkSignOffDelegations | Applies a batch-bound 21 CFR Part 11 Principal Investigator electronic signature across site delegations. | [Details](#op-bulksignoffdelegations) |
| `GET` | `/healthz` | KnowledgeHealthCheck | Knowledge base and study training health probe. | [Details](#op-knowledgehealthcheck) |

### `GET` `/api/v1/knowledge/articles` {#op-listknowledgearticles}

**Operation ID:** `ListKnowledgeArticles` — Searches and lists published SOP knowledge base articles.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `knowledge:read`
- **Clinical Scope Enforcement:** `Tenant`
- **Tags:** `Knowledge`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `category_id` | `query` | `optional` | `string` |  |
| `persona` | `query` | `optional` | `string` |  |
| `q` | `query` | `optional` | `string` |  |
| `status_filter` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Knowledge_KnowledgeArticleDto`](#schema-knowledge-knowledgearticledto) |

</details>

### `GET` `/api/v1/knowledge/articles/{article_id}` {#op-getknowledgearticle}

**Operation ID:** `GetKnowledgeArticle` — Retrieves a published SOP knowledge base article by article ID.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `knowledge:read`
- **Clinical Scope Enforcement:** `Tenant`
- **Tags:** `Knowledge`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `article_id` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Knowledge_KnowledgeArticleDto`](#schema-knowledge-knowledgearticledto) |
| `404` | Not Found | `application/problem+json` | [`Knowledge_ProblemDetails`](#schema-knowledge-problemdetails) |

</details>

### `GET` `/api/v1/knowledge/articles/{article_id}/audit-log` {#op-getknowledgearticleauditlog}

**Operation ID:** `GetKnowledgeArticleAuditLog` — Retrieves the 21 CFR Part 11 hash-chained audit log for a knowledge base article.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `knowledge:read`
- **Clinical Scope Enforcement:** `Tenant`
- **Tags:** `Knowledge`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `article_id` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Knowledge_KnowledgeArticleAuditEntryDto`](#schema-knowledge-knowledgearticleauditentrydto) |
| `404` | Not Found | `application/problem+json` | [`Knowledge_ProblemDetails`](#schema-knowledge-problemdetails) |

</details>

### `GET` `/api/v1/knowledge/articles/{article_id}/versions` {#op-listknowledgearticleversions}

**Operation ID:** `ListKnowledgeArticleVersions` — Lists immutable version snapshots for a knowledge base article.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `knowledge:read`
- **Clinical Scope Enforcement:** `Tenant`
- **Tags:** `Knowledge`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `article_id` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Knowledge_KnowledgeArticleVersionDto`](#schema-knowledge-knowledgearticleversiondto) |
| `404` | Not Found | `application/problem+json` | [`Knowledge_ProblemDetails`](#schema-knowledge-problemdetails) |

</details>

### `GET` `/api/v1/knowledge/categories` {#op-listknowledgecategories}

**Operation ID:** `ListKnowledgeCategories` — Lists knowledge base SOP categories visible to the caller's clinical persona.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `knowledge:read`
- **Clinical Scope Enforcement:** `Tenant`
- **Tags:** `Knowledge`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `persona` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Knowledge_KnowledgeCategoryDto`](#schema-knowledge-knowledgecategorydto) |

</details>

### `GET` `/api/v1/knowledge/contextual-help` {#op-resolvecontextualhelp}

**Operation ID:** `ResolveContextualHelp` — Resolves persona-filtered contextual SOP guidance for the active workspace route.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `knowledge:read`
- **Clinical Scope Enforcement:** `Tenant`
- **Tags:** `Knowledge`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `persona` | `query` | `optional` | `string` |  |
| `route` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Knowledge_ContextualHelpResponseDto`](#schema-knowledge-contextualhelpresponsedto) |

</details>

### `GET` `/api/v1/knowledge/status` {#op-knowledgestatus}

**Operation ID:** `KnowledgeStatus` — Returns Knowledge base operational status.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Knowledge`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Knowledge_AnonymousTypeOfstringAndstring`](#schema-knowledge-anonymoustypeofstringandstring) |

</details>

### `GET` `/api/v1/training/assignments` {#op-listassignments}

**Operation ID:** `ListAssignments` — Lists study training curriculum assignments filtered by study, site, or assignee.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `training_log:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `Study Training & Gating`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `site_id` | `query` | `optional` | `string` |  |
| `status` | `query` | `optional` | `string` |  |
| `study_id` | `query` | `optional` | `string` |  |
| `user_id` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Knowledge_TrainingAssignmentDto`](#schema-knowledge-trainingassignmentdto) |
| `403` | Forbidden | `application/problem+json` | [`Knowledge_ProblemDetails`](#schema-knowledge-problemdetails) |

</details>

### `GET` `/api/v1/training/assignments/{assignment_id}` {#op-getassignmentdetail}

**Operation ID:** `GetAssignmentDetail` — Retrieves a training assignment and completion status by assignment ID.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `training_log:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `Study Training & Gating`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `assignment_id` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Knowledge_TrainingAssignmentDto`](#schema-knowledge-trainingassignmentdto) |
| `403` | Forbidden | `application/problem+json` | [`Knowledge_ProblemDetails`](#schema-knowledge-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Knowledge_ProblemDetails`](#schema-knowledge-problemdetails) |

</details>

### `GET` `/api/v1/training/curricula` {#op-listcurricula}

**Operation ID:** `ListCurricula` — Lists active study training curricula visible within the caller's study scope.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `training_log:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Study Training & Gating`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `protocol_version` | `query` | `optional` | `string` |  |
| `study_id` | `query` | `optional` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Knowledge_TrainingCurriculumDto`](#schema-knowledge-trainingcurriculumdto) |
| `403` | Forbidden | `application/problem+json` | [`Knowledge_ProblemDetails`](#schema-knowledge-problemdetails) |

</details>

### `POST` `/api/v1/training/curricula/batch-assign` {#op-batchassigncurriculum}

**Operation ID:** `BatchAssignCurriculum` — Assigns a training curriculum to a batch of study staff members.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `training_curriculum:assign`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Study Training & Gating`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Knowledge_BatchAssignCurriculumRequest`](#schema-knowledge-batchassigncurriculumrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`Knowledge_BatchAssignmentResponse`](#schema-knowledge-batchassignmentresponse) |
| `400` | Bad Request | `application/problem+json` | [`Knowledge_ProblemDetails`](#schema-knowledge-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Knowledge_ProblemDetails`](#schema-knowledge-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Knowledge_ProblemDetails`](#schema-knowledge-problemdetails) |
| `409` | Conflict | `application/problem+json` | [`Knowledge_ProblemDetails`](#schema-knowledge-problemdetails) |
| `422` | Unprocessable Entity | `application/problem+json` | [`Knowledge_ProblemDetails`](#schema-knowledge-problemdetails) |

</details>

### `GET` `/api/v1/training/curricula/{curriculum_id}` {#op-getcurriculum}

**Operation ID:** `GetCurriculum` — Retrieves a study training curriculum by curriculum ID.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `training_log:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Study Training & Gating`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `curriculum_id` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Knowledge_TrainingCurriculumDto`](#schema-knowledge-trainingcurriculumdto) |
| `403` | Forbidden | `application/problem+json` | [`Knowledge_ProblemDetails`](#schema-knowledge-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Knowledge_ProblemDetails`](#schema-knowledge-problemdetails) |

</details>

### `GET` `/api/v1/training/delegations` {#op-listdelegations}

**Operation ID:** `ListDelegations` — Lists site delegation-of-authority records and PI sign-off status.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `delegation_log:read`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `Study Training & Gating`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `known_at` | `query` | `optional` | `string` (`date-time`) |  |
| `site_id` | `query` | `optional` | `string` |  |
| `status` | `query` | `optional` | `string` |  |
| `study_id` | `query` | `optional` | `string` |  |
| `valid_at` | `query` | `optional` | `string` (`date-time`) |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Knowledge_DelegationSummaryDto`](#schema-knowledge-delegationsummarydto) |
| `403` | Forbidden | `application/problem+json` | [`Knowledge_ProblemDetails`](#schema-knowledge-problemdetails) |

</details>

### `POST` `/api/v1/training/delegations/bulk-signoff` {#op-bulksignoffdelegations}

**Operation ID:** `BulkSignOffDelegations` — Applies a batch-bound 21 CFR Part 11 Principal Investigator electronic signature across site delegations.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `delegation_log:sign`
- **Clinical Scope Enforcement:** `CallerStudyAndSite`
- **Tags:** `Study Training & Gating`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Sig-Token` | `header` | `required` | `string` (minLen: 1) | Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Knowledge_BulkDelegationSignOffRequest`](#schema-knowledge-bulkdelegationsignoffrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Knowledge_BulkDelegationSignOffResponse`](#schema-knowledge-bulkdelegationsignoffresponse) |
| `400` | Bad Request | `application/problem+json` | [`Knowledge_ProblemDetails`](#schema-knowledge-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Knowledge_ProblemDetails`](#schema-knowledge-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Knowledge_ProblemDetails`](#schema-knowledge-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`Knowledge_ProblemDetails`](#schema-knowledge-problemdetails) |

</details>

### `GET` `/healthz` {#op-knowledgehealthcheck}

**Operation ID:** `KnowledgeHealthCheck` — Knowledge base and study training health probe.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Knowledge`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Knowledge_AnonymousTypeOfstringAndstring`](#schema-knowledge-anonymoustypeofstringandstring) |

</details>

## Schemas

Deduplicated component schemas exported in `components.schemas` (18 total). Operation payloads and responses link here so nested models are defined once per page.

### `Knowledge_AnonymousTypeOfstringAndstring` {#schema-knowledge-anonymoustypeofstringandstring}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `service` | `string` \| `null` | `required` |  |
| `status` | `string` \| `null` | `required` |  |

</details>

### `Knowledge_BatchAssignCurriculumRequest` {#schema-knowledge-batchassigncurriculumrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `curriculum_id` | `string` | `required` |  |
| `reason_for_change` | `string` \| `null` | `required` |  |
| `roles` | array of `string` \| `null` | `required` |  |
| `site_ids` | array of `string` \| `null` | `required` |  |
| `study_id` | `string` | `required` |  |
| `target_personnel_ids` | array of `string` \| `null` | `required` |  |

</details>

### `Knowledge_BatchAssignmentResponse` {#schema-knowledge-batchassignmentresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (9)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `assignments` | array of [`Knowledge_TrainingAssignmentDto`](#schema-knowledge-trainingassignmentdto) | `required` |  |
| `batch_transaction_id` | `string` | `required` |  |
| `curriculum_id` | `string` | `required` |  |
| `executed_at` | `string` (`date-time`) | `required` |  |
| `skipped_items` | array of [`Knowledge_SkippedAssignmentItemDto`](#schema-knowledge-skippedassignmentitemdto) | `required` |  |
| `study_id` | `string` | `required` |  |
| `total_assigned` | `integer` (`int32`) | `required` |  |
| `total_eligible` | `integer` (`int32`) | `required` |  |
| `total_skipped` | `integer` (`int32`) | `required` |  |

</details>

### `Knowledge_BulkDelegationSignOffRequest` {#schema-knowledge-bulkdelegationsignoffrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `delegation_ids` | array of `string` | `required` |  |
| `signature_payload` | [`Knowledge_SignaturePayloadDto`](#schema-knowledge-signaturepayloaddto) | `required` |  |
| `site_id` | `string` \| `null` | `required` |  |
| `study_id` | `string` \| `null` | `required` |  |

</details>

### `Knowledge_BulkDelegationSignOffResponse` {#schema-knowledge-bulkdelegationsignoffresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (9)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `batch_transaction_id` | `string` | `required` |  |
| `etmf_artifact_id` | `string` | `required` |  |
| `executed_at` | `string` (`date-time`) | `required` |  |
| `failed_items` | array of [`Knowledge_FailedDelegationItemDto`](#schema-knowledge-faileddelegationitemdto) | `required` |  |
| `sha256_seal` | `string` | `required` |  |
| `signed_delegations` | array of [`Knowledge_DelegationSummaryDto`](#schema-knowledge-delegationsummarydto) | `required` |  |
| `total_failed` | `integer` (`int32`) | `required` |  |
| `total_requested` | `integer` (`int32`) | `required` |  |
| `total_signed` | `integer` (`int32`) | `required` |  |

</details>

### `Knowledge_ContextualHelpMappingDto` {#schema-knowledge-contextualhelpmappingdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `article_id` | `string` | `required` |  |
| `id` | `string` | `required` |  |
| `is_active` | `boolean` | `required` |  |
| `persona` | `string` \| `null` | `required` |  |
| `priority` | `integer` (`int32`) | `required` |  |
| `route_pattern` | `string` | `required` |  |
| `section_anchor` | `string` \| `null` | `required` |  |

</details>

### `Knowledge_ContextualHelpResponseDto` {#schema-knowledge-contextualhelpresponsedto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `matched_mapping` | [`Knowledge_ContextualHelpMappingDto`](#schema-knowledge-contextualhelpmappingdto) \| `null` | `required` |  |
| `primary_article` | [`Knowledge_KnowledgeArticleDto`](#schema-knowledge-knowledgearticledto) \| `null` | `required` |  |
| `primary_version` | [`Knowledge_KnowledgeArticleVersionDto`](#schema-knowledge-knowledgearticleversiondto) \| `null` | `required` |  |
| `related_articles` | array of [`Knowledge_KnowledgeArticleDto`](#schema-knowledge-knowledgearticledto) | `required` |  |
| `section_anchor` | `string` \| `null` | `required` |  |

</details>

### `Knowledge_DelegationSummaryDto` {#schema-knowledge-delegationsummarydto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (13)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `batch_transaction_id` | `string` \| `null` | `required` |  |
| `duties` | `string` | `required` |  |
| `id` | `string` | `required` |  |
| `pi_approved_at` | `string` (`date-time`) \| `null` | `required` |  |
| `pi_signature_hash` | `string` \| `null` | `required` |  |
| `role` | `string` | `required` |  |
| `signed_off` | `boolean` | `required` |  |
| `site_id` | `string` | `required` |  |
| `staff_name` | `string` | `required` |  |
| `staff_user_id` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `task_code` | `string` | `required` |  |

</details>

### `Knowledge_FailedDelegationItemDto` {#schema-knowledge-faileddelegationitemdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `delegation_id` | `string` | `required` |  |
| `reason` | `string` | `required` |  |

</details>

### `Knowledge_KnowledgeArticleAuditEntryDto` {#schema-knowledge-knowledgearticleauditentrydto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (11)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `actor_user_id` | `string` | `required` |  |
| `article_id` | `string` | `required` |  |
| `created_at` | `string` (`date-time`) | `required` |  |
| `event_type` | `string` | `required` |  |
| `id` | `string` | `required` |  |
| `predecessor_hash` | `string` | `required` |  |
| `reason_for_change` | `string` | `required` |  |
| `sha256_hash` | `string` | `required` |  |
| `target_status` | `string` \| `null` | `required` |  |
| `version_index` | `integer` (`int64`) | `required` |  |
| `version_label` | `string` \| `null` | `required` |  |

</details>

### `Knowledge_KnowledgeArticleDto` {#schema-knowledge-knowledgearticledto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (17)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `approved_by` | `string` \| `null` | `required` |  |
| `author_user_id` | `string` | `required` |  |
| `body_html` | `string` \| `null` | `required` |  |
| `body_markdown` | `string` \| `null` | `required` |  |
| `category_id` | `string` | `required` |  |
| `category_name` | `string` \| `null` | `required` |  |
| `created_at` | `string` (`date-time`) | `required` |  |
| `current_published_version_id` | `string` \| `null` | `required` |  |
| `id` | `string` | `required` |  |
| `last_edited_by` | `string` \| `null` | `required` |  |
| `slug` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `tags` | array of `string` | `required` |  |
| `title` | `string` | `required` |  |
| `updated_at` | `string` (`date-time`) | `required` |  |
| `version_index` | `integer` (`int32`) | `required` |  |
| `version_label` | `string` | `required` |  |

</details>

### `Knowledge_KnowledgeArticleVersionDto` {#schema-knowledge-knowledgearticleversiondto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (11)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `article_id` | `string` | `required` |  |
| `body_html` | `string` \| `null` | `required` |  |
| `body_markdown` | `string` | `required` |  |
| `created_at` | `string` (`date-time`) | `required` |  |
| `created_by` | `string` | `required` |  |
| `id` | `string` | `required` |  |
| `is_locked` | `boolean` | `required` |  |
| `reason_for_change` | `string` | `required` |  |
| `status_at_snapshot` | `string` | `required` |  |
| `version_index` | `integer` (`int32`) | `required` |  |
| `version_label` | `string` | `required` |  |

</details>

### `Knowledge_KnowledgeCategoryDto` {#schema-knowledge-knowledgecategorydto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (9)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `created_at` | `string` (`date-time`) | `required` |  |
| `created_by` | `string` | `required` |  |
| `description` | `string` \| `null` | `required` |  |
| `id` | `string` | `required` |  |
| `is_deleted` | `boolean` | `required` |  |
| `name` | `string` | `required` |  |
| `parent_id` | `string` \| `null` | `required` |  |
| `persona_visibility` | array of `string` | `required` |  |
| `slug` | `string` | `required` |  |

</details>

### `Knowledge_ProblemDetails` {#schema-knowledge-problemdetails}

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

### `Knowledge_SignaturePayloadDto` {#schema-knowledge-signaturepayloaddto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `meaning` | `string` | `required` |  |
| `reason_for_change` | `string` \| `null` | `required` |  |

</details>

### `Knowledge_SkippedAssignmentItemDto` {#schema-knowledge-skippedassignmentitemdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `reason` | `string` | `required` |  |
| `site_id` | `string` | `required` |  |
| `user_id` | `string` | `required` |  |
| `user_role` | `string` | `required` |  |

</details>

### `Knowledge_TrainingAssignmentDto` {#schema-knowledge-trainingassignmentdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (13)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `batch_transaction_id` | `string` \| `null` | `required` |  |
| `completed_at` | `string` (`date-time`) \| `null` | `required` |  |
| `created_at` | `string` (`date-time`) | `required` |  |
| `curriculum_id` | `string` | `required` |  |
| `id` | `string` | `required` |  |
| `quiz_score` | `number` (`double`) \| `null` | `required` |  |
| `site_id` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `time_spent_seconds` | `integer` (`int32`) | `required` |  |
| `user_id` | `string` | `required` |  |
| `user_name` | `string` | `required` |  |
| `user_role` | `string` | `required` |  |

</details>

### `Knowledge_TrainingCurriculumDto` {#schema-knowledge-trainingcurriculumdto}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `created_at` | `string` (`date-time`) | `required` |  |
| `description` | `string` \| `null` | `required` |  |
| `id` | `string` | `required` |  |
| `is_active` | `boolean` | `required` |  |
| `protocol_version` | `string` | `required` |  |
| `study_id` | `string` | `required` |  |
| `title` | `string` | `required` |  |

</details>

