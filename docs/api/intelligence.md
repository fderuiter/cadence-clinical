# intelligence API

Generated from `docs/openapi/intelligence_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

- **Service Maturity:** <span class="VPBadge tip">Active</span> (Consolidated module hosted in `kernel-api` · port `8018` · `/api/v1/ai/*`)
- **OpenAPI 3.1 Contract:** [`intelligence_openapi.json`](../openapi/intelligence_openapi.json) (12 operation(s), 20 schema(s))

## Operations

| Method | Path | Operation | Summary | Contract |
| ------ | ---- | --------- | ------- | -------- |
| `POST` | `/api/v1/ai/biometrics/verify-liveness` | AiVerifyLiveness | Tests video frame payloads for anti-spoofing in temporary memory and returns signed liveness tokens. | [Details](#op-aiverifyliveness) |
| `GET` | `/api/v1/ai/controls` | AiListControls | The tenant's AI kill switches and monthly token budgets. | [Details](#op-ailistcontrols) |
| `PUT` | `/api/v1/ai/controls/{scope}` | AiSetControl | Switches agents on or off for every task ('*') or one task, and sets the monthly token budget (agent_control.set). | [Details](#op-aisetcontrol) |
| `POST` | `/api/v1/ai/evaluation-runs` | AiRecordEvaluationRun | Stores an evaluation run record after checking it against its task configuration version, set, criteria and case results (evaluation_run.record, ADR-2634). | [Details](#op-airecordevaluationrun) |
| `GET` | `/api/v1/ai/evaluation-runs/{runId}` | AiGetEvaluationRun | One stored evaluation run with its record, exactly as hashed. | [Details](#op-aigetevaluationrun) |
| `POST` | `/api/v1/ai/intent/route` | RouteAiIntent | Match a command-palette query against fixed patterns and return the navigation targets the caller's roles allow. | [Details](#op-routeaiintent) |
| `GET` | `/api/v1/ai/runs` | AiListMyRuns | The caller's latest agent runs from the interaction ledger, newest first. | [Details](#op-ailistmyruns) |
| `GET` | `/api/v1/ai/runs/{runId}` | AiGetMyRun | One of the caller's agent runs with its request, answer, usage and integrity hash. | [Details](#op-aigetmyrun) |
| `GET` | `/api/v1/ai/tasks` | AiListTasks | The agent task configuration versions in this deployment, with their content hashes and the tenant's qualification of each. | [Details](#op-ailisttasks) |
| `GET` | `/api/v1/ai/tasks/{key}/evaluation-runs` | AiListEvaluationRuns | The stored evaluation runs of a task, newest first, optionally of one version. | [Details](#op-ailistevaluationruns) |
| `POST` | `/api/v1/ai/tasks/{key}/versions/{version}/qualification` | AiQualifyTask | Qualifies a task configuration version on a stored, accepted evaluation run and the caller's signed approval (task_configuration.qualify, ADR-2631). | [Details](#op-aiqualifytask) |
| `POST` | `/api/v1/ai/tasks/{key}/versions/{version}/suspension` | AiSuspendTask | Takes a qualified task configuration version out of use until it is qualified again (task_configuration.suspend). | [Details](#op-aisuspendtask) |

### `POST` `/api/v1/ai/biometrics/verify-liveness` {#op-aiverifyliveness}

**Operation ID:** `AiVerifyLiveness` — Tests video frame payloads for anti-spoofing in temporary memory and returns signed liveness tokens.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Anti-spoof facial liveness verification generates short-lived HMAC signed tokens for consent capture.
- **Tags:** `Biometrics AI Gateway`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Intelligence_VerifyLivenessRequest`](#schema-intelligence-verifylivenessrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Intelligence_VerifyLivenessResponse`](#schema-intelligence-verifylivenessresponse) |
| `400` | Bad Request | `application/problem+json` | [`Intelligence_ProblemDetails`](#schema-intelligence-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Intelligence_ProblemDetails`](#schema-intelligence-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |

</details>

### `GET` `/api/v1/ai/controls` {#op-ailistcontrols}

**Operation ID:** `AiListControls` — The tenant's AI kill switches and monthly token budgets.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Reads are limited to the caller's own runs and the tenant's settings; every write is a kernel command authorized against the caller's grants.
- **Tags:** `AI agents`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Intelligence_AgentControlResponse`](#schema-intelligence-agentcontrolresponse) |

</details>

### `PUT` `/api/v1/ai/controls/{scope}` {#op-aisetcontrol}

**Operation ID:** `AiSetControl` — Switches agents on or off for every task ('*') or one task, and sets the monthly token budget (agent_control.set).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Reads are limited to the caller's own runs and the tenant's settings; every write is a kernel command authorized against the caller's grants.
- **Tags:** `AI agents`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `scope` | `path` | `required` | `string` |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Intelligence_AgentControlRequest`](#schema-intelligence-agentcontrolrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Intelligence_AgentControlResponse`](#schema-intelligence-agentcontrolresponse) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Intelligence_ProblemDetails`](#schema-intelligence-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Intelligence_ProblemDetails`](#schema-intelligence-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`. | `application/problem+json` | [`Intelligence_ProblemDetails`](#schema-intelligence-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. | `application/problem+json` | [`Intelligence_ProblemDetails`](#schema-intelligence-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |

</details>

### `POST` `/api/v1/ai/evaluation-runs` {#op-airecordevaluationrun}

**Operation ID:** `AiRecordEvaluationRun` — Stores an evaluation run record after checking it against its task configuration version, set, criteria and case results (evaluation_run.record, ADR-2634).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Reads are limited to the caller's own runs and the tenant's settings; every write is a kernel command authorized against the caller's grants.
- **Tags:** `AI agents`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Intelligence_JsonElement`](#schema-intelligence-jsonelement) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Intelligence_EvaluationRunStateResponse`](#schema-intelligence-evaluationrunstateresponse) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Intelligence_ProblemDetails`](#schema-intelligence-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Intelligence_ProblemDetails`](#schema-intelligence-problemdetails) |
| `404` | Not Found. Kernel errors: `M.Intelligence.Task.Unknown`. | `application/problem+json` | [`Intelligence_ProblemDetails`](#schema-intelligence-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `M.Intelligence.Configuration.Changed`. | `application/problem+json` | [`Intelligence_ProblemDetails`](#schema-intelligence-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `M.Intelligence.Configuration.NotQualifiable`, `M.Intelligence.Evaluation.RunInvalid`. | `application/problem+json` | [`Intelligence_ProblemDetails`](#schema-intelligence-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `M.Intelligence.Task.Unknown` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `M.Intelligence.Configuration.Changed` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `M.Intelligence.Configuration.NotQualifiable` |
| `422` | `M.Intelligence.Evaluation.RunInvalid` |

</details>

### `GET` `/api/v1/ai/evaluation-runs/{runId}` {#op-aigetevaluationrun}

**Operation ID:** `AiGetEvaluationRun` — One stored evaluation run with its record, exactly as hashed.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Reads are limited to the caller's own runs and the tenant's settings; every write is a kernel command authorized against the caller's grants.
- **Tags:** `AI agents`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `runId` | `path` | `required` | `string` (`uuid`) |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Intelligence_EvaluationRunResponse`](#schema-intelligence-evaluationrunresponse) |
| `404` | Not Found | `application/problem+json` | [`Intelligence_ProblemDetails`](#schema-intelligence-problemdetails) |

</details>

### `POST` `/api/v1/ai/intent/route` {#op-routeaiintent}

**Operation ID:** `RouteAiIntent` — Match a command-palette query against fixed patterns and return the navigation targets the caller's roles allow.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Matches fixed patterns, reads and writes no record, and filters recommendations by the caller's signed roles.
- **Tags:** `IntentEndpoints`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Intelligence_IntentRouteRequest`](#schema-intelligence-intentrouterequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Intelligence_IntentRouteResponse`](#schema-intelligence-intentrouteresponse) |
| `400` | Bad Request | `application/problem+json` | [`Intelligence_ProblemDetails`](#schema-intelligence-problemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Intelligence_ProblemDetails`](#schema-intelligence-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Intelligence_ProblemDetails`](#schema-intelligence-problemdetails) |

</details>

### `GET` `/api/v1/ai/runs` {#op-ailistmyruns}

**Operation ID:** `AiListMyRuns` — The caller's latest agent runs from the interaction ledger, newest first.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Reads are limited to the caller's own runs and the tenant's settings; every write is a kernel command authorized against the caller's grants.
- **Tags:** `AI agents`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `limit` | `query` | `optional` | `integer` (`int32`) |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Intelligence_AgentRunSummaryResponse`](#schema-intelligence-agentrunsummaryresponse) |

</details>

### `GET` `/api/v1/ai/runs/{runId}` {#op-aigetmyrun}

**Operation ID:** `AiGetMyRun` — One of the caller's agent runs with its request, answer, usage and integrity hash.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Reads are limited to the caller's own runs and the tenant's settings; every write is a kernel command authorized against the caller's grants.
- **Tags:** `AI agents`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `runId` | `path` | `required` | `string` (`uuid`) |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Intelligence_AgentRunResponse`](#schema-intelligence-agentrunresponse) |
| `404` | Not Found | `application/problem+json` | [`Intelligence_ProblemDetails`](#schema-intelligence-problemdetails) |

</details>

### `GET` `/api/v1/ai/tasks` {#op-ailisttasks}

**Operation ID:** `AiListTasks` — The agent task configuration versions in this deployment, with their content hashes and the tenant's qualification of each.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Reads are limited to the caller's own runs and the tenant's settings; every write is a kernel command authorized against the caller's grants.
- **Tags:** `AI agents`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Intelligence_AgentTaskResponse`](#schema-intelligence-agenttaskresponse) |

</details>

### `GET` `/api/v1/ai/tasks/{key}/evaluation-runs` {#op-ailistevaluationruns}

**Operation ID:** `AiListEvaluationRuns` — The stored evaluation runs of a task, newest first, optionally of one version.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Reads are limited to the caller's own runs and the tenant's settings; every write is a kernel command authorized against the caller's grants.
- **Tags:** `AI agents`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `key` | `path` | `required` | `string` |  |
| `limit` | `query` | `optional` | `integer` (`int32`) |  |
| `version` | `query` | `optional` | `integer` (`int32`) |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`Intelligence_EvaluationRunSummaryResponse`](#schema-intelligence-evaluationrunsummaryresponse) |

</details>

### `POST` `/api/v1/ai/tasks/{key}/versions/{version}/qualification` {#op-aiqualifytask}

**Operation ID:** `AiQualifyTask` — Qualifies a task configuration version on a stored, accepted evaluation run and the caller's signed approval (task_configuration.qualify, ADR-2631).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Reads are limited to the caller's own runs and the tenant's settings; every write is a kernel command authorized against the caller's grants.
- **Tags:** `AI agents`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `key` | `path` | `required` | `string` |  |
| `version` | `path` | `required` | `integer` (`int32`) |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Intelligence_QualificationRequest`](#schema-intelligence-qualificationrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Intelligence_QualificationResponse`](#schema-intelligence-qualificationresponse) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Intelligence_ProblemDetails`](#schema-intelligence-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Intelligence_ProblemDetails`](#schema-intelligence-problemdetails) |
| `404` | Not Found. Kernel errors: `M.Intelligence.Evaluation.RunNotFound`, `M.Intelligence.Task.Unknown`. | `application/problem+json` | [`Intelligence_ProblemDetails`](#schema-intelligence-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `M.Intelligence.Configuration.Changed`, `M.Intelligence.Qualification.AlreadyQualified`. | `application/problem+json` | [`Intelligence_ProblemDetails`](#schema-intelligence-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `M.Intelligence.Configuration.NotQualifiable`, `M.Intelligence.Qualification.AttestationRejected`, `M.Intelligence.Qualification.EvaluationNotAccepted`. | `application/problem+json` | [`Intelligence_ProblemDetails`](#schema-intelligence-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `M.Intelligence.Evaluation.RunNotFound` |
| `404` | `M.Intelligence.Task.Unknown` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `M.Intelligence.Configuration.Changed` |
| `409` | `M.Intelligence.Qualification.AlreadyQualified` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `M.Intelligence.Configuration.NotQualifiable` |
| `422` | `M.Intelligence.Qualification.AttestationRejected` |
| `422` | `M.Intelligence.Qualification.EvaluationNotAccepted` |

</details>

### `POST` `/api/v1/ai/tasks/{key}/versions/{version}/suspension` {#op-aisuspendtask}

**Operation ID:** `AiSuspendTask` — Takes a qualified task configuration version out of use until it is qualified again (task_configuration.suspend).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Reads are limited to the caller's own runs and the tenant's settings; every write is a kernel command authorized against the caller's grants.
- **Tags:** `AI agents`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `key` | `path` | `required` | `string` |  |
| `version` | `path` | `required` | `integer` (`int32`) |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Intelligence_QualificationResponse`](#schema-intelligence-qualificationresponse) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`Intelligence_ProblemDetails`](#schema-intelligence-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`Intelligence_ProblemDetails`](#schema-intelligence-problemdetails) |
| `404` | Not Found. Kernel errors: `M.Intelligence.Qualification.NotQualified`. | `application/problem+json` | [`Intelligence_ProblemDetails`](#schema-intelligence-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`. | `application/problem+json` | [`Intelligence_ProblemDetails`](#schema-intelligence-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. | `application/problem+json` | [`Intelligence_ProblemDetails`](#schema-intelligence-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `M.Intelligence.Qualification.NotQualified` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |

</details>

## Schemas

Deduplicated component schemas exported in `components.schemas` (20 total). Operation payloads and responses link here so nested models are defined once per page.

### `Intelligence_AcceptanceCriteria` {#schema-intelligence-acceptancecriteria}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `maximum_mean_cost` | `number` (`double`) \| `null` | `optional` |  |
| `maximum_mean_latency` | `string` \| `null` | `optional` |  |
| `minimum_pass_rate` | `number` (`double`) | `required` |  |
| `minimum_reviewed_pass_rate` | `number` (`double`) | `required` |  |

</details>

### `Intelligence_AgentControlRequest` {#schema-intelligence-agentcontrolrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `enabled` | `boolean` | `required` |  |
| `monthly_token_limit` | `integer` (`int64`) \| `null` | `required` |  |

</details>

### `Intelligence_AgentControlResponse` {#schema-intelligence-agentcontrolresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `enabled` | `boolean` | `required` |  |
| `monthly_token_limit` | `integer` (`int64`) \| `null` | `required` |  |
| `scope` | `string` | `required` |  |
| `version_index` | `integer` (`int64`) | `required` |  |

</details>

### `Intelligence_AgentRunResponse` {#schema-intelligence-agentrunresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `configuration_hash` | `string` | `required` |  |
| `content_hash` | `string` \| `null` | `required` |  |
| `content_hash_verified` | `boolean` | `required` |  |
| `failure_reason` | `string` \| `null` | `required` |  |
| `provider` | `string` | `required` |  |
| `request_json` | `string` | `required` |  |
| `response_text` | `string` \| `null` | `required` |  |
| `summary` | [`Intelligence_AgentRunSummaryResponse`](#schema-intelligence-agentrunsummaryresponse) | `required` |  |

</details>

### `Intelligence_AgentRunSummaryResponse` {#schema-intelligence-agentrunsummaryresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (10)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `completed_at` | `string` (`date-time`) \| `null` | `required` |  |
| `cost_usd` | `number` (`double`) \| `null` | `required` |  |
| `input_tokens` | `integer` (`int64`) | `required` |  |
| `model` | `string` | `required` |  |
| `output_tokens` | `integer` (`int64`) | `required` |  |
| `run_id` | `string` (`uuid`) | `required` |  |
| `started_at` | `string` (`date-time`) | `required` |  |
| `status` | `string` | `required` |  |
| `task_key` | `string` | `required` |  |
| `task_version` | `integer` (`int32`) | `required` |  |

</details>

### `Intelligence_AgentTaskResponse` {#schema-intelligence-agenttaskresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (13)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `acceptance_criteria` | [`Intelligence_AcceptanceCriteria`](#schema-intelligence-acceptancecriteria) \| `null` | `required` |  |
| `autonomy_level` | `string` | `required` |  |
| `content_hash` | `string` | `required` |  |
| `evaluation_set` | [`Intelligence_EvaluationSetRef`](#schema-intelligence-evaluationsetref) \| `null` | `required` |  |
| `key` | `string` | `required` |  |
| `model` | `string` | `required` |  |
| `provider` | `string` | `required` |  |
| `qualification` | [`Intelligence_TaskQualificationResponse`](#schema-intelligence-taskqualificationresponse) \| `null` | `required` |  |
| `qualification_gaps` | array of `string` | `required` |  |
| `retrieval_sources` | array of `string` | `required` |  |
| `risk_class` | `string` \| `null` | `required` |  |
| `tools` | array of `string` | `required` |  |
| `version` | `integer` (`int32`) | `required` |  |

</details>

### `Intelligence_EvaluationRunResponse` {#schema-intelligence-evaluationrunresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `record_json` | `string` | `required` |  |
| `run` | [`Intelligence_EvaluationRunSummaryResponse`](#schema-intelligence-evaluationrunsummaryresponse) | `required` |  |

</details>

### `Intelligence_EvaluationRunStateResponse` {#schema-intelligence-evaluationrunstateresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `accepted` | `boolean` | `required` |  |
| `configuration_hash` | `string` | `required` |  |
| `evaluation_set_hash` | `string` | `required` |  |
| `reasons` | array of `string` | `required` |  |
| `record_hash` | `string` | `required` |  |
| `run_id` | `string` (`uuid`) | `required` |  |
| `task_key` | `string` | `required` |  |
| `task_version` | `integer` (`int32`) | `required` |  |

</details>

### `Intelligence_EvaluationRunSummaryResponse` {#schema-intelligence-evaluationrunsummaryresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (13)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `accepted` | `boolean` | `required` |  |
| `completed_at` | `string` (`date-time`) | `required` |  |
| `configuration_hash` | `string` | `required` |  |
| `evaluation_set_hash` | `string` | `required` |  |
| `evaluation_set_id` | `string` | `required` |  |
| `evaluation_set_version` | `integer` (`int32`) | `required` |  |
| `model` | `string` | `required` |  |
| `provider` | `string` | `required` |  |
| `record_hash` | `string` | `required` |  |
| `run_id` | `string` (`uuid`) | `required` |  |
| `started_at` | `string` (`date-time`) | `required` |  |
| `task_key` | `string` | `required` |  |
| `task_version` | `integer` (`int32`) | `required` |  |

</details>

### `Intelligence_EvaluationSetRef` {#schema-intelligence-evaluationsetref}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `content_hash` | `string` | `required` |  |
| `set_id` | `string` | `required` |  |
| `version` | `integer` (`int32`) | `required` |  |

</details>

### `Intelligence_IntentRecommendation` {#schema-intelligence-intentrecommendation}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (9)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `action` | `string` \| `null` | `required` |  |
| `category` | `string` | `required` |  |
| `confidence` | `number` (`double`) | `required` |  |
| `description` | `string` | `required` |  |
| `icon` | `string` | `required` |  |
| `id` | `string` | `required` |  |
| `name` | `string` | `required` |  |
| `roles` | array of `string` | `required` |  |
| `target_route` | `string` | `required` |  |

</details>

### `Intelligence_IntentRouteRequest` {#schema-intelligence-intentrouterequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `context` | `object` \| `null` | `optional` |  |
| `query` | `string` | `required` |  |

</details>

### `Intelligence_IntentRouteResponse` {#schema-intelligence-intentrouteresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `query` | `string` | `required` |  |
| `recommendations` | array of [`Intelligence_IntentRecommendation`](#schema-intelligence-intentrecommendation) | `required` |  |

</details>

### `Intelligence_JsonElement` {#schema-intelligence-jsonelement}

- **Type:** any

### `Intelligence_ProblemDetails` {#schema-intelligence-problemdetails}

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

### `Intelligence_QualificationRequest` {#schema-intelligence-qualificationrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `attestation_id` | `string` (`uuid`) | `required` |  |
| `configuration_hash` | `string` | `required` |  |
| `evaluation_run_id` | `string` (`uuid`) | `required` |  |

</details>

### `Intelligence_QualificationResponse` {#schema-intelligence-qualificationresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `configuration_hash` | `string` | `required` |  |
| `risk_class` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `task_key` | `string` | `required` |  |
| `task_version` | `integer` (`int32`) | `required` |  |
| `version_index` | `integer` (`int64`) | `required` |  |

</details>

### `Intelligence_TaskQualificationResponse` {#schema-intelligence-taskqualificationresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (9)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `configuration_hash` | `string` | `required` |  |
| `evaluation_run_hash` | `string` | `required` |  |
| `evaluation_run_reference` | `string` | `required` |  |
| `qualified_at` | `string` (`date-time`) | `required` |  |
| `risk_class` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `suspended_at` | `string` (`date-time`) \| `null` | `required` |  |
| `suspension_reason` | `string` \| `null` | `required` |  |
| `usable` | `boolean` | `required` |  |

</details>

### `Intelligence_VerifyLivenessRequest` {#schema-intelligence-verifylivenessrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `challenge_id` | `string` \| `null` | `required` |  |
| `fallback_mode` | `boolean` | `optional` |  |
| `subject_pseudonym` | `string` \| `null` | `required` |  |
| `video_frames` | array of `string` \| `null` | `required` |  |

</details>

### `Intelligence_VerifyLivenessResponse` {#schema-intelligence-verifylivenessresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `anti_spoof_score` | `number` (`double`) | `required` |  |
| `expires_at` | `string` (`date-time`) | `required` |  |
| `is_live` | `boolean` | `required` |  |
| `liveness_token` | `string` | `required` |  |
| `status` | `string` | `required` |  |

</details>

