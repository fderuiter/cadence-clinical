# execution_forms API

Generated from `docs/openapi/execution_forms_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

- **Service Maturity:** <span class="VPBadge tip">Active</span> (Consolidated module hosted in `kernel-api` · port `8018` · `/api/v1/execution/forms*`)
- **OpenAPI 3.1 Contract:** [`execution_forms_openapi.json`](../openapi/execution_forms_openapi.json) (15 operation(s), 39 schema(s))

## Operations

| Method | Path | Operation | Summary | Contract |
| ------ | ---- | --------- | ------- | -------- |
| `GET` | `/api/v1/execution/forms` | ExecutionListEnrollmentForms | Lists the forms opened for an enrollment, oldest first. | [Details](#op-executionlistenrollmentforms) |
| `POST` | `/api/v1/execution/forms` | ExecutionOpenForm | Opens a form for a participant's visit under the protocol version in force (collection.open). | [Details](#op-executionopenform) |
| `GET` | `/api/v1/execution/forms/queries` | ExecutionListEnrollmentQueries | Lists the queries on every form of an enrollment or across visible enrollments of a study, oldest first. | [Details](#op-executionlistenrollmentqueries) |
| `GET` | `/api/v1/execution/forms/queries/digests` | ExecutionListFormQueryAgingDigests | Lists daily de-identified query aging digests for visible scopes of a study. | [Details](#op-executionlistformqueryagingdigests) |
| `POST` | `/api/v1/execution/forms/queries/escalate` | ExecutionEscalateOverdueFormQueries | Escalates open queries past their 14-day SLA on IClock and emits daily aging digests (execution.query.manage). | [Details](#op-executionescalateoverdueformqueries) |
| `GET` | `/api/v1/execution/forms/{formId}` | ExecutionGetForm | Reads a form: its items in layout order with their values, completeness, locks, assertions and signatures. | [Details](#op-executiongetform) |
| `POST` | `/api/v1/execution/forms/{formId}/preview` | ExecutionPreviewFormValues | Evaluates pinned study collection rules in memory over draft form values without mutating state (K-RULE-3). | [Details](#op-executionpreviewformvalues) |
| `GET` | `/api/v1/execution/forms/{formId}/queries` | ExecutionListFormQueries | Lists a form's queries, oldest first, each with whether its value changed since it was raised. | [Details](#op-executionlistformqueries) |
| `POST` | `/api/v1/execution/forms/{formId}/queries` | ExecutionRaiseFormQuery | Raises a query on the current revision of one of the form's values or on a blank item (execution.query.raise). | [Details](#op-executionraiseformquery) |
| `POST` | `/api/v1/execution/forms/{formId}/queries/{queryId}/answer` | ExecutionAnswerFormQuery | Answers an open query (execution.query.answer). | [Details](#op-executionanswerformquery) |
| `POST` | `/api/v1/execution/forms/{formId}/queries/{queryId}/cancel` | ExecutionCancelFormQuery | Cancels an open or answered query raised in error (execution.query.manage). | [Details](#op-executioncancelformquery) |
| `POST` | `/api/v1/execution/forms/{formId}/queries/{queryId}/close` | ExecutionCloseFormQuery | Closes an answered manual query (separation of duties) or system query (execution.query.manage). | [Details](#op-executioncloseformquery) |
| `POST` | `/api/v1/execution/forms/{formId}/queries/{queryId}/reopen` | ExecutionReopenFormQuery | Reopens an answered or closed query with why it is not settled (execution.query.manage). | [Details](#op-executionreopenformquery) |
| `POST` | `/api/v1/execution/forms/{formId}/signatures` | ExecutionSignForm | Signs the form's current values with an X-Sig-Token step-up token (collection.sign); 409 when the form changed since it was read. | [Details](#op-executionsignform) |
| `PUT` | `/api/v1/execution/forms/{formId}/values` | ExecutionSaveFormValues | Saves a page of values: all of them or, on one stale revision or invalid value, none (collection.record). | [Details](#op-executionsaveformvalues) |

### `GET` `/api/v1/execution/forms` {#op-executionlistenrollmentforms}

**Operation ID:** `ExecutionListEnrollmentForms` — Lists the forms opened for an enrollment, oldest first.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Every read checks the caller may see the form's enrollment; every write is a kernel command authorized against the caller's grants.
- **Tags:** `Execution forms`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `enrollment_id` | `query` | `optional` | `string` (`uuid`) |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`ExecutionForms_FormSummaryResponse`](#schema-executionforms-formsummaryresponse) |
| `400` | Bad Request | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |

</details>

### `POST` `/api/v1/execution/forms` {#op-executionopenform}

**Operation ID:** `ExecutionOpenForm` — Opens a form for a participant's visit under the protocol version in force (collection.open).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Every read checks the caller may see the form's enrollment; every write is a kernel command authorized against the caller's grants.
- **Tags:** `Execution forms`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`ExecutionForms_OpenFormRequest`](#schema-executionforms-openformrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`ExecutionForms_FormStateResponse`](#schema-executionforms-formstateresponse) |
| `400` | Bad Request | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Collection.EnrollmentUnknown`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Collection.NoGoverningVersion`, `K.Command.IdempotencyConflict`, `K.Observation.ParticipantWithdrawn`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Collection.CollectedAtInvalid`, `K.Collection.SpecUnknown`, `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `423` | Locked. Kernel errors: `K.Observation.Locked`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Collection.EnrollmentUnknown` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Collection.NoGoverningVersion` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `K.Observation.ParticipantWithdrawn` |
| `422` | `K.Collection.CollectedAtInvalid` |
| `422` | `K.Collection.SpecUnknown` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `423` | `K.Observation.Locked` |

</details>

### `GET` `/api/v1/execution/forms/queries` {#op-executionlistenrollmentqueries}

**Operation ID:** `ExecutionListEnrollmentQueries` — Lists the queries on every form of an enrollment or across visible enrollments of a study, oldest first.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Every read checks the caller may see the enrollment; every write is a module command authorized against the caller's grants.
- **Tags:** `Execution queries`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `as_of` | `query` | `optional` | `string` (`date-time`) |  |
| `enrollment_id` | `query` | `optional` | `string` (`uuid`) |  |
| `escalated` | `query` | `optional` | `boolean` |  |
| `known_at` | `query` | `optional` | `string` (`date-time`) |  |
| `limit` | `query` | `optional` | `integer` (`int32`) |  |
| `offset` | `query` | `optional` | `integer` (`int32`) |  |
| `origin` | `query` | `optional` | `string` |  |
| `owner` | `query` | `optional` | `string` |  |
| `participant` | `query` | `optional` | `string` |  |
| `priority` | `query` | `optional` | `string` |  |
| `site` | `query` | `optional` | `string` |  |
| `status` | `query` | `optional` | `string` |  |
| `study_id` | `query` | `optional` | `string` (`uuid`) |  |
| `valid_at` | `query` | `optional` | `string` (`date-time`) |  |
| `value_changed` | `query` | `optional` | `boolean` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`ExecutionForms_FormQueryResponse`](#schema-executionforms-formqueryresponse) |
| `400` | Bad Request | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |

</details>

### `GET` `/api/v1/execution/forms/queries/digests` {#op-executionlistformqueryagingdigests}

**Operation ID:** `ExecutionListFormQueryAgingDigests` — Lists daily de-identified query aging digests for visible scopes of a study.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Every read checks the caller may see the enrollment; every write is a module command authorized against the caller's grants.
- **Tags:** `Execution queries`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `recipient_role` | `query` | `optional` | `string` |  |
| `site` | `query` | `optional` | `string` |  |
| `study_id` | `query` | `optional` | `string` (`uuid`) |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`ExecutionForms_FormQueryAgingDigestResponse`](#schema-executionforms-formqueryagingdigestresponse) |
| `400` | Bad Request | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |

</details>

### `POST` `/api/v1/execution/forms/queries/escalate` {#op-executionescalateoverdueformqueries}

**Operation ID:** `ExecutionEscalateOverdueFormQueries` — Escalates open queries past their 14-day SLA on IClock and emits daily aging digests (execution.query.manage).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Every read checks the caller may see the enrollment; every write is a module command authorized against the caller's grants.
- **Tags:** `Execution queries`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |
| `study_id` | `query` | `optional` | `string` (`uuid`) |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`ExecutionForms_FormQueryEscalationPassResponse`](#schema-executionforms-formqueryescalationpassresponse) |
| `400` | Bad Request | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Aggregate.NotFound`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `M.Execution.Query.InvalidTransition`. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `423` | Locked. Kernel errors: `K.Assertion.Locked`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |

</details>

### `GET` `/api/v1/execution/forms/{formId}` {#op-executiongetform}

**Operation ID:** `ExecutionGetForm` — Reads a form: its items in layout order with their values, completeness, locks, assertions and signatures.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Every read checks the caller may see the form's enrollment; every write is a kernel command authorized against the caller's grants.
- **Tags:** `Execution forms`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `formId` | `path` | `required` | `string` (`uuid`) |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`ExecutionForms_FormResponse`](#schema-executionforms-formresponse) |
| `403` | Forbidden | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |

</details>

### `POST` `/api/v1/execution/forms/{formId}/preview` {#op-executionpreviewformvalues}

**Operation ID:** `ExecutionPreviewFormValues` — Evaluates pinned study collection rules in memory over draft form values without mutating state (K-RULE-3).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Every read checks the caller may see the form's enrollment; every write is a kernel command authorized against the caller's grants.
- **Tags:** `Execution forms`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `formId` | `path` | `required` | `string` (`uuid`) |  |

**Request Body** (`optional`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`ExecutionForms_PreviewFormValuesRequest`](#schema-executionforms-previewformvaluesrequest) \| `null` |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`ExecutionForms_FormPreviewResponse`](#schema-executionforms-formpreviewresponse) |
| `400` | Bad Request | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Aggregate.NotFound`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `404` | `K.Aggregate.NotFound` |

</details>

### `GET` `/api/v1/execution/forms/{formId}/queries` {#op-executionlistformqueries}

**Operation ID:** `ExecutionListFormQueries` — Lists a form's queries, oldest first, each with whether its value changed since it was raised.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Every read checks the caller may see the enrollment; every write is a module command authorized against the caller's grants.
- **Tags:** `Execution queries`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `formId` | `path` | `required` | `string` (`uuid`) |  |
| `as_of` | `query` | `optional` | `string` (`date-time`) |  |
| `known_at` | `query` | `optional` | `string` (`date-time`) |  |
| `valid_at` | `query` | `optional` | `string` (`date-time`) |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of [`ExecutionForms_FormQueryResponse`](#schema-executionforms-formqueryresponse) |
| `400` | Bad Request | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `404` | Not Found | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |

</details>

### `POST` `/api/v1/execution/forms/{formId}/queries` {#op-executionraiseformquery}

**Operation ID:** `ExecutionRaiseFormQuery` — Raises a query on the current revision of one of the form's values or on a blank item (execution.query.raise).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Every read checks the caller may see the enrollment; every write is a module command authorized against the caller's grants.
- **Tags:** `Execution queries`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `formId` | `path` | `required` | `string` (`uuid`) |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`ExecutionForms_RaiseFormQueryRequest`](#schema-executionforms-raiseformqueryrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`ExecutionForms_FormQueryResponse`](#schema-executionforms-formqueryresponse) |
| `400` | Bad Request | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Aggregate.NotFound`. Kernel errors: `M.Execution.Form.CollectionInstanceUnknown`, `M.Execution.Query.ObservationUnknown`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `M.Execution.Query.RevisionStale`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Collection.ValuesInvalid`, `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `423` | Locked. Kernel errors: `K.Assertion.Locked`. Kernel errors: `K.Assertion.Locked`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `M.Execution.Form.CollectionInstanceUnknown` |
| `404` | `M.Execution.Query.ObservationUnknown` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `M.Execution.Query.RevisionStale` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `423` | `K.Assertion.Locked` |

</details>

### `POST` `/api/v1/execution/forms/{formId}/queries/{queryId}/answer` {#op-executionanswerformquery}

**Operation ID:** `ExecutionAnswerFormQuery` — Answers an open query (execution.query.answer).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Every read checks the caller may see the enrollment; every write is a module command authorized against the caller's grants.
- **Tags:** `Execution queries`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `formId` | `path` | `required` | `string` (`uuid`) |  |
| `queryId` | `path` | `required` | `string` (`uuid`) |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`ExecutionForms_FormQueryTextRequest`](#schema-executionforms-formquerytextrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`ExecutionForms_FormQueryResponse`](#schema-executionforms-formqueryresponse) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Aggregate.NotFound`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `M.Execution.Query.InvalidTransition`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `423` | Locked. Kernel errors: `K.Assertion.Locked`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Aggregate.NotFound` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `M.Execution.Query.InvalidTransition` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `423` | `K.Assertion.Locked` |

</details>

### `POST` `/api/v1/execution/forms/{formId}/queries/{queryId}/cancel` {#op-executioncancelformquery}

**Operation ID:** `ExecutionCancelFormQuery` — Cancels an open or answered query raised in error (execution.query.manage).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Every read checks the caller may see the enrollment; every write is a module command authorized against the caller's grants.
- **Tags:** `Execution queries`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `formId` | `path` | `required` | `string` (`uuid`) |  |
| `queryId` | `path` | `required` | `string` (`uuid`) |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`optional`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`ExecutionForms_FormQueryNoteRequest`](#schema-executionforms-formquerynoterequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`ExecutionForms_FormQueryResponse`](#schema-executionforms-formqueryresponse) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Aggregate.NotFound`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `M.Execution.Query.InvalidTransition`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `423` | Locked. Kernel errors: `K.Assertion.Locked`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Aggregate.NotFound` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `M.Execution.Query.InvalidTransition` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `423` | `K.Assertion.Locked` |

</details>

### `POST` `/api/v1/execution/forms/{formId}/queries/{queryId}/close` {#op-executioncloseformquery}

**Operation ID:** `ExecutionCloseFormQuery` — Closes an answered manual query (separation of duties) or system query (execution.query.manage).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Every read checks the caller may see the enrollment; every write is a module command authorized against the caller's grants.
- **Tags:** `Execution queries`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `formId` | `path` | `required` | `string` (`uuid`) |  |
| `queryId` | `path` | `required` | `string` (`uuid`) |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`optional`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`ExecutionForms_FormQueryNoteRequest`](#schema-executionforms-formquerynoterequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`ExecutionForms_FormQueryResponse`](#schema-executionforms-formqueryresponse) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Aggregate.NotFound`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `M.Execution.Query.InvalidTransition`, `M.Execution.Query.SeparationOfDuties`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `423` | Locked. Kernel errors: `K.Assertion.Locked`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Aggregate.NotFound` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `M.Execution.Query.InvalidTransition` |
| `409` | `M.Execution.Query.SeparationOfDuties` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `423` | `K.Assertion.Locked` |

</details>

### `POST` `/api/v1/execution/forms/{formId}/queries/{queryId}/reopen` {#op-executionreopenformquery}

**Operation ID:** `ExecutionReopenFormQuery` — Reopens an answered or closed query with why it is not settled (execution.query.manage).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Every read checks the caller may see the enrollment; every write is a module command authorized against the caller's grants.
- **Tags:** `Execution queries`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `formId` | `path` | `required` | `string` (`uuid`) |  |
| `queryId` | `path` | `required` | `string` (`uuid`) |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`ExecutionForms_FormQueryTextRequest`](#schema-executionforms-formquerytextrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`ExecutionForms_FormQueryResponse`](#schema-executionforms-formqueryresponse) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Aggregate.NotFound`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `M.Execution.Query.InvalidTransition`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `423` | Locked. Kernel errors: `K.Assertion.Locked`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Aggregate.NotFound` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `M.Execution.Query.InvalidTransition` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `423` | `K.Assertion.Locked` |

</details>

### `POST` `/api/v1/execution/forms/{formId}/signatures` {#op-executionsignform}

**Operation ID:** `ExecutionSignForm` — Signs the form's current values with an X-Sig-Token step-up token (collection.sign); 409 when the form changed since it was read.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Signing is a kernel command authorized against the caller's grants at the form's enrollment, after a step-up re-authentication.
- **Tags:** `Execution forms`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `formId` | `path` | `required` | `string` (`uuid`) |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |
| `X-Sig-Token` | `header` | `required` | `string` (minLen: 1) | Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`ExecutionForms_SignFormRequest`](#schema-executionforms-signformrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `201` | Created | `application/json` | [`ExecutionForms_FormSignatureResponse`](#schema-executionforms-formsignatureresponse) |
| `400` | Bad Request | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`, `K.Trust.AuthenticationStale`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`, `K.Trust.PersonRequired`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Aggregate.NotFound`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Collection.NotReadyToSign`, `K.Command.IdempotencyConflict`, `K.Trust.TargetContentChanged`, `K.Trust.TargetNotSignable`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Collection.SignatureMeaningInvalid`, `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Trust.MethodRequired`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `401` | `K.Trust.AuthenticationStale` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `403` | `K.Trust.PersonRequired` |
| `404` | `K.Aggregate.NotFound` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Collection.NotReadyToSign` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `K.Trust.TargetContentChanged` |
| `409` | `K.Trust.TargetNotSignable` |
| `422` | `K.Collection.SignatureMeaningInvalid` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `K.Trust.MethodRequired` |

</details>

### `PUT` `/api/v1/execution/forms/{formId}/values` {#op-executionsaveformvalues}

**Operation ID:** `ExecutionSaveFormValues` — Saves a page of values: all of them or, on one stale revision or invalid value, none (collection.record).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`SignedInCaller`)
- **Signed-In Caller Rule:** Every read checks the caller may see the form's enrollment; every write is a kernel command authorized against the caller's grants.
- **Tags:** `Execution forms`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `formId` | `path` | `required` | `string` (`uuid`) |  |
| `Idempotency-Key` | `header` | `required` | `string` (minLen: 1; maxLen: 255) | Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry. |
| `X-Change-Reason` | `header` | `required` | `string` (minLen: 1) | Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`ExecutionForms_SaveFormValuesRequest`](#schema-executionforms-saveformvaluesrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`ExecutionForms_FormSavedResponse`](#schema-executionforms-formsavedresponse) |
| `400` | Bad Request | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `401` | Unauthorized. Kernel errors: `K.Actor.Unresolved`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `403` | Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `404` | Not Found. Kernel errors: `K.Aggregate.NotFound`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `409` | Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Collection.RevisionStale`, `K.Command.IdempotencyConflict`, `K.Observation.ParticipantWithdrawn`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `422` | Unprocessable Entity. Kernel errors: `K.Collection.ValuesInvalid`, `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Observation.ObservedAtInvalid`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |
| `423` | Locked. Kernel errors: `K.Observation.Locked`. | `application/problem+json` | [`ExecutionForms_ProblemDetails`](#schema-executionforms-problemdetails) |

**Registered Kernel Error Codes (`x-kernel-errors`)**

| HTTP Status | Domain Error Code |
| ----------- | ----------------- |
| `401` | `K.Actor.Unresolved` |
| `403` | `K.Authorization.Denied` |
| `403` | `K.Subscription.Expired` |
| `404` | `K.Aggregate.NotFound` |
| `409` | `K.Aggregate.AlreadyExists` |
| `409` | `K.Aggregate.ConcurrencyConflict` |
| `409` | `K.Collection.RevisionStale` |
| `409` | `K.Command.IdempotencyConflict` |
| `409` | `K.Observation.ParticipantWithdrawn` |
| `422` | `K.Collection.ValuesInvalid` |
| `422` | `K.Command.IdempotencyKeyRequired` |
| `422` | `K.Command.Invalid` |
| `422` | `K.Command.ReasonForChangeRequired` |
| `422` | `K.Command.TenantRequired` |
| `422` | `K.Observation.ObservedAtInvalid` |
| `423` | `K.Observation.Locked` |

</details>

## Schemas

Deduplicated component schemas exported in `components.schemas` (39 total). Operation payloads and responses link here so nested models are defined once per page.

### `ExecutionForms_CodeSystemVersionId` {#schema-executionforms-codesystemversionid}

- **Type:** any
- **Description:** Strongly-typed UUIDv7 identifier for an immutable, checksum-verified release of a code system in the Concept Registry.

### `ExecutionForms_CodelistOptionView` {#schema-executionforms-codelistoptionview}

- **Type:** object
- **Description:** One option in a collection item's governed codelist.

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `code` | `string` | `required` | The code recorded in string CodedValue.Code. |
| `display` | `string` | `required` | Human-readable label for the code. |
| `submission_value` | `string` \| `null` | `optional` | Optional standards submission value (for example CDISC CT submission value). |

</details>

### `ExecutionForms_DateTimePrecision` {#schema-executionforms-datetimeprecision}

- **Type:** enum
- **Allowed Values:** `Year`, `Month`, `Day`, `Minute`, `Second`

### `ExecutionForms_FormAssertionResponse` {#schema-executionforms-formassertionresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (9)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `asserted_at` | `string` (`date-time`) | `required` |  |
| `asserted_by` | `string` | `required` |  |
| `assertion_id` | `string` (`uuid`) | `required` |  |
| `kind` | `string` | `required` |  |
| `reason` | `string` | `required` |  |
| `retracted_at` | `string` (`date-time`) \| `null` | `required` |  |
| `revision` | `integer` (`int32`) \| `null` | `required` |  |
| `target` | `string` | `required` |  |
| `text` | `string` \| `null` | `required` |  |

</details>

### `ExecutionForms_FormItemResponse` {#schema-executionforms-formitemresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (13)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `code_system_version_id` | `string` (`uuid`) \| `null` | `optional` |  |
| `codelist` | `string` \| `null` | `optional` |  |
| `codelist_options` | array of [`ExecutionForms_CodelistOptionView`](#schema-executionforms-codelistoptionview) \| `null` | `optional` |  |
| `concept` | `string` \| `null` | `required` |  |
| `item_id` | `string` | `required` |  |
| `observation_id` | `string` (`uuid`) \| `null` | `required` |  |
| `recorded_at` | `string` (`date-time`) \| `null` | `required` |  |
| `recorded_by` | `string` \| `null` | `required` |  |
| `required` | `boolean` | `required` |  |
| `revision` | `integer` (`int32`) | `required` |  |
| `unit` | `string` \| `null` | `required` |  |
| `value` | [`ExecutionForms_ObservationValue`](#schema-executionforms-observationvalue) \| `null` | `required` |  |
| `value_kind` | `string` \| `null` | `required` |  |

</details>

### `ExecutionForms_FormLockResponse` {#schema-executionforms-formlockresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `kind` | `string` | `required` |  |
| `lock_id` | `string` (`uuid`) | `required` |  |
| `locked_at` | `string` (`date-time`) | `required` |  |
| `locked_by` | `string` | `required` |  |
| `reason` | `string` | `required` |  |
| `scope` | `string` | `required` |  |

</details>

### `ExecutionForms_FormPreviewResponse` {#schema-executionforms-formpreviewresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `form_id` | `string` (`uuid`) | `required` |  |
| `warnings` | array of [`ExecutionForms_FormPreviewWarningResponse`](#schema-executionforms-formpreviewwarningresponse) | `required` |  |

</details>

### `ExecutionForms_FormPreviewWarningResponse` {#schema-executionforms-formpreviewwarningresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `dataset` | `string` | `required` |  |
| `item_id` | `string` | `required` |  |
| `message` | `string` | `required` |  |
| `pack_key` | `string` | `required` |  |
| `remediation_guidance` | `string` \| `null` | `optional` |  |
| `rule_id` | `string` | `required` |  |
| `rule_version` | `integer` (`int32`) | `required` |  |
| `severity` | `string` | `required` |  |

</details>

### `ExecutionForms_FormQueryAgingDigestEntry` {#schema-executionforms-formqueryagingdigestentry}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (10)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `age_days` | `integer` (`int32`) | `required` |  |
| `due_at` | `string` (`date-time`) | `required` |  |
| `form_id` | `string` (`uuid`) | `required` |  |
| `item_id` | `string` | `required` |  |
| `opened_at` | `string` (`date-time`) | `required` |  |
| `owner_role` | `string` | `required` |  |
| `priority` | `string` | `required` |  |
| `query_id` | `string` (`uuid`) | `required` |  |
| `site_code` | `string` | `required` |  |
| `subject_code` | `string` | `required` |  |

</details>

### `ExecutionForms_FormQueryAgingDigestResponse` {#schema-executionforms-formqueryagingdigestresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (11)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `digest_date` | `string` (`date`) | `required` |  |
| `digest_id` | `string` (`uuid`) | `required` |  |
| `entries` | array of [`ExecutionForms_FormQueryAgingDigestEntry`](#schema-executionforms-formqueryagingdigestentry) | `required` |  |
| `escalated_count` | `integer` (`int32`) | `required` |  |
| `generated_at` | `string` (`date-time`) | `required` |  |
| `recipient_role` | `string` | `required` |  |
| `scope_key` | `string` | `required` |  |
| `site_code` | `string` \| `null` | `required` |  |
| `study_code` | `string` | `required` |  |
| `study_id` | `string` (`uuid`) | `required` |  |
| `study_site_id` | `string` (`uuid`) \| `null` | `required` |  |

</details>

### `ExecutionForms_FormQueryEscalationPassResponse` {#schema-executionforms-formqueryescalationpassresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `digests` | array of [`ExecutionForms_FormQueryAgingDigestResponse`](#schema-executionforms-formqueryagingdigestresponse) | `required` |  |
| `escalated_queries` | `integer` (`int32`) | `required` |  |

</details>

### `ExecutionForms_FormQueryMessageResponse` {#schema-executionforms-formquerymessageresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `at` | `string` (`date-time`) | `required` |  |
| `by` | `string` | `required` |  |
| `kind` | `string` | `required` |  |
| `reason_for_change` | `string` | `optional` |  |
| `text` | `string` | `required` |  |

</details>

### `ExecutionForms_FormQueryNoteRequest` {#schema-executionforms-formquerynoterequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (1)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `text` | `string` \| `null` | `optional` |  |

</details>

### `ExecutionForms_FormQueryResponse` {#schema-executionforms-formqueryresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (29)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `assignee` | `string` \| `null` | `optional` |  |
| `collection_spec_id` | `string` \| `null` | `optional` |  |
| `current_revision` | `integer` (`int32`) \| `null` | `required` |  |
| `due_at` | `string` (`date-time`) \| `null` | `optional` |  |
| `enrollment_id` | `string` (`uuid`) | `required` |  |
| `escalated_at` | `string` (`date-time`) \| `null` | `optional` |  |
| `form_id` | `string` (`uuid`) | `required` |  |
| `item_id` | `string` | `required` |  |
| `last_answered_by` | `string` \| `null` | `optional` |  |
| `messages` | array of [`ExecutionForms_FormQueryMessageResponse`](#schema-executionforms-formquerymessageresponse) | `required` |  |
| `observation_id` | `string` (`uuid`) | `required` |  |
| `opened_at` | `string` (`date-time`) \| `null` | `optional` |  |
| `origin` | `string` | `optional` |  |
| `owner_role` | `string` | `optional` |  |
| `planned_activity_id` | `string` \| `null` | `optional` |  |
| `priority` | `string` | `optional` |  |
| `query_id` | `string` (`uuid`) | `required` |  |
| `raised_at` | `string` (`date-time`) | `required` |  |
| `raised_by` | `string` | `required` |  |
| `revision` | `integer` (`int32`) | `required` |  |
| `site_code` | `string` \| `null` | `optional` |  |
| `status` | `string` | `required` |  |
| `status_since` | `string` (`date-time`) \| `null` | `optional` |  |
| `study_id` | `string` (`uuid`) \| `null` | `optional` |  |
| `study_site_id` | `string` (`uuid`) \| `null` | `optional` |  |
| `subject_code` | `string` \| `null` | `optional` |  |
| `text` | `string` | `required` |  |
| `value_changed` | `boolean` | `required` |  |
| `version` | `integer` (`int64`) | `required` |  |

</details>

### `ExecutionForms_FormQueryTextRequest` {#schema-executionforms-formquerytextrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (1)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `text` | `string` | `required` |  |

</details>

### `ExecutionForms_FormResponse` {#schema-executionforms-formresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (25)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `answered_query_count` | `integer` (`int32`) | `optional` |  |
| `assertions` | array of [`ExecutionForms_FormAssertionResponse`](#schema-executionforms-formassertionresponse) | `required` |  |
| `can_sign` | `boolean` | `optional` |  |
| `collected_at` | `string` (`date-time`) | `required` |  |
| `collection_spec_id` | `string` | `required` |  |
| `collection_title` | `string` \| `null` | `optional` |  |
| `complete` | `boolean` | `required` |  |
| `content_hash` | `string` \| `null` | `required` |  |
| `enrollment_id` | `string` (`uuid`) | `required` |  |
| `form_id` | `string` (`uuid`) | `required` |  |
| `items` | array of [`ExecutionForms_FormItemResponse`](#schema-executionforms-formitemresponse) | `required` |  |
| `locks` | array of [`ExecutionForms_FormLockResponse`](#schema-executionforms-formlockresponse) | `required` |  |
| `missing_required` | array of `string` | `required` |  |
| `open_error_finding_count` | `integer` (`int32`) | `optional` |  |
| `open_query_count` | `integer` (`int32`) | `optional` |  |
| `opened_at` | `string` (`date-time`) | `required` |  |
| `opened_by` | `string` | `required` |  |
| `participant_id` | `string` (`uuid`) | `required` |  |
| `planned_activity_id` | `string` \| `null` | `required` |  |
| `protocol_version_id` | `string` (`uuid`) | `required` |  |
| `readiness_blockers` | array of `string` \| `null` | `optional` |  |
| `signatures` | array of [`ExecutionForms_FormSignatureResponse`](#schema-executionforms-formsignatureresponse) | `required` |  |
| `study_id` | `string` (`uuid`) | `required` |  |
| `study_site_id` | `string` (`uuid`) | `required` |  |
| `version` | `integer` (`int64`) | `required` |  |

</details>

### `ExecutionForms_FormSavedResponse` {#schema-executionforms-formsavedresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `form` | [`ExecutionForms_FormResponse`](#schema-executionforms-formresponse) \| `null` | `optional` |  |
| `form_id` | `string` (`uuid`) | `required` |  |
| `values` | array of [`ExecutionForms_FormSavedValueResponse`](#schema-executionforms-formsavedvalueresponse) | `required` |  |
| `version` | `integer` (`int64`) | `required` |  |

</details>

### `ExecutionForms_FormSavedValueResponse` {#schema-executionforms-formsavedvalueresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `changed` | `boolean` | `required` |  |
| `item_id` | `string` | `required` |  |
| `observation_id` | `string` (`uuid`) | `required` |  |
| `revision` | `integer` (`int32`) | `required` |  |
| `value` | [`ExecutionForms_ObservationValue`](#schema-executionforms-observationvalue) | `required` |  |

</details>

### `ExecutionForms_FormSignatureResponse` {#schema-executionforms-formsignatureresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `attestation_id` | `string` (`uuid`) | `required` |  |
| `content_hash` | `string` | `required` |  |
| `meaning` | `string` | `required` |  |
| `reason` | `string` | `required` |  |
| `signed_at` | `string` (`date-time`) | `required` |  |
| `signed_by` | `string` | `required` |  |
| `signed_values` | array of [`ExecutionForms_FormSignedValueResponse`](#schema-executionforms-formsignedvalueresponse) \| `null` | `required` |  |
| `superseded` | `boolean` | `required` |  |

</details>

### `ExecutionForms_FormSignedValueResponse` {#schema-executionforms-formsignedvalueresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `item_id` | `string` | `required` |  |
| `observation_id` | `string` (`uuid`) | `required` |  |
| `revision` | `integer` (`int32`) | `required` |  |
| `value` | [`ExecutionForms_ObservationValue`](#schema-executionforms-observationvalue) | `required` |  |

</details>

### `ExecutionForms_FormStateResponse` {#schema-executionforms-formstateresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `collected_at` | `string` (`date-time`) | `required` |  |
| `collection_spec_id` | `string` | `required` |  |
| `enrollment_id` | `string` (`uuid`) | `required` |  |
| `form_id` | `string` (`uuid`) | `required` |  |
| `planned_activity_id` | `string` \| `null` | `required` |  |
| `protocol_version_id` | `string` (`uuid`) | `required` |  |
| `version` | `integer` (`int64`) | `required` |  |

</details>

### `ExecutionForms_FormSummaryResponse` {#schema-executionforms-formsummaryresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (29)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `answered_query_count` | `integer` (`int32`) | `optional` |  |
| `collected_at` | `string` (`date-time`) | `required` |  |
| `collection_spec_id` | `string` | `required` |  |
| `collection_title` | `string` \| `null` | `optional` |  |
| `complete` | `boolean` | `required` |  |
| `enrollment_id` | `string` (`uuid`) | `required` |  |
| `form_id` | `string` (`uuid`) | `required` |  |
| `frozen` | `boolean` | `optional` |  |
| `item_count` | `integer` (`int32`) | `required` |  |
| `lock_count` | `integer` (`int32`) | `optional` |  |
| `locked` | `boolean` | `optional` |  |
| `missing_required_count` | `integer` (`int32`) | `optional` |  |
| `open_error_finding_count` | `integer` (`int32`) | `optional` |  |
| `open_query_count` | `integer` (`int32`) | `optional` |  |
| `opened_at` | `string` (`date-time`) | `required` |  |
| `opened_by` | `string` | `required` |  |
| `participant_id` | `string` (`uuid`) | `required` |  |
| `planned_activity_id` | `string` \| `null` | `required` |  |
| `protocol_version_id` | `string` (`uuid`) | `required` |  |
| `readiness_blockers` | array of `string` \| `null` | `optional` |  |
| `recorded_count` | `integer` (`int32`) | `required` |  |
| `signature_count` | `integer` (`int32`) | `optional` |  |
| `signature_superseded` | `boolean` | `optional` |  |
| `signed` | `boolean` | `optional` |  |
| `study_id` | `string` (`uuid`) | `required` |  |
| `study_site_id` | `string` (`uuid`) | `required` |  |
| `unverified_count` | `integer` (`int32`) | `optional` |  |
| `verified_count` | `integer` (`int32`) | `optional` |  |
| `version` | `integer` (`int64`) | `required` |  |

</details>

### `ExecutionForms_FormValueRequest` {#schema-executionforms-formvaluerequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `expected_revision` | `integer` (`int32`) | `required` |  |
| `item_id` | `string` | `required` |  |
| `observation_id` | `string` (`uuid`) \| `null` | `required` |  |
| `observed_at` | `string` (`date-time`) \| `null` | `required` |  |
| `value` | [`ExecutionForms_ObservationValue`](#schema-executionforms-observationvalue) \| `null` | `required` |  |

</details>

### `ExecutionForms_NullFlavor` {#schema-executionforms-nullflavor}

- **Type:** enum
- **Description:** Why a value is missing (HL7 null flavors, narrowed to the ones clinical data collection uses).
- **Allowed Values:** `NotDone`, `Unknown`, `AskedButUnknown`, `NotAsked`, `NotApplicable`, `Masked`

### `ExecutionForms_ObservationValue` {#schema-executionforms-observationvalue}

- **Type:** object
- **Description:** The value of a recorded clinical fact (spec §8.1, K-FACT-2, K-FACT-3, ADR-2623): exactly one of a quantity with its UCUM unit, a code from a pinned code-system version, text, a boolean, an ISO 8601 date or time at the precision it was known, or a reasoned absence. It is the value *as entered*: a normalised unit, a standardised result or a decoded label is a derivation of it, never a replacement (K-FACT-2).
- **Composition:** `anyOf`: [`ExecutionForms_ObservationValueQuantityValue`](#schema-executionforms-observationvaluequantityvalue), [`ExecutionForms_ObservationValueCodedValue`](#schema-executionforms-observationvaluecodedvalue), [`ExecutionForms_ObservationValueTextValue`](#schema-executionforms-observationvaluetextvalue), [`ExecutionForms_ObservationValueBooleanValue`](#schema-executionforms-observationvaluebooleanvalue), [`ExecutionForms_ObservationValueDateTimeValue`](#schema-executionforms-observationvaluedatetimevalue), [`ExecutionForms_ObservationValueMissingValue`](#schema-executionforms-observationvaluemissingvalue)

### `ExecutionForms_ObservationValueBooleanValue` {#schema-executionforms-observationvaluebooleanvalue}

- **Type:** object
- **Description:** A yes or no answer.

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `type` | `string` (enum: `boolean`) | `optional` |  |
| `value` | `boolean` | `required` | The answer. |

</details>

### `ExecutionForms_ObservationValueCodedValue` {#schema-executionforms-observationvaluecodedvalue}

- **Type:** object
- **Description:** A code from one pinned code-system version (spec §6.1), such as an NCI CT submission value in CDISC CT 2025-09-26 or a LOINC answer code. The version is part of the value, so the same code read years later still means what it meant.

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `code` | `string` | `required` | The code, exactly as the code system writes it. |
| `code_system_version_id` | [`ExecutionForms_CodeSystemVersionId`](#schema-executionforms-codesystemversionid) | `required` | The code-system version the code was chosen from. |
| `type` | `string` (enum: `coded`) | `optional` |  |

</details>

### `ExecutionForms_ObservationValueDateTimeValue` {#schema-executionforms-observationvaluedatetimevalue}

- **Type:** object
- **Description:** An ISO 8601 date or date and time at the precision it was known (ISO 8601-1 reduced precision): `2026`, `2026-03`, `2026-03-14`, `2026-03-14T09:30` or `2026-03-14T09:30:15`. A time may carry <!-- deid-ignore: ISO 8601 format examples, not PHI -->`Z` or an offset; without one it is the local time where it was observed. A partial date is never padded to a full one.

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `precision` | [`ExecutionForms_DateTimePrecision`](#schema-executionforms-datetimeprecision) | `optional` | How precisely the date or time is known, or null when string DateTimeValue.Value is not valid ISO 8601. |
| `type` | `string` (enum: `dateTime`) | `optional` |  |
| `value` | `string` | `required` | The ISO 8601 text. |

</details>

### `ExecutionForms_ObservationValueMissingValue` {#schema-executionforms-observationvaluemissingvalue}

- **Type:** object
- **Description:** A value that was not obtained, with why (K-FACT-3): the test was not done, the answer is unknown, the question does not apply, and so on. It is a recorded fact in its own right, corrected like any other value.

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `detail` | `string` \| `null` | `optional` | More about why, such as the reason a test was not done (SDTM `--REASND`); optional. |
| `reason` | [`ExecutionForms_NullFlavor`](#schema-executionforms-nullflavor) | `required` | Why there is no value. |
| `type` | `string` (enum: `missing`) | `optional` |  |

</details>

### `ExecutionForms_ObservationValueQuantityValue` {#schema-executionforms-observationvaluequantityvalue}

- **Type:** object
- **Description:** A measured amount in the UCUM unit it was entered in (K-FACT-2), such as 120 `mm[Hg]`, with an optional comparator for a bound such as &lt;0.5 `mg/dL`. The decimal keeps the precision it was entered with (1.50 stays 1.50).

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `comparator` | [`ExecutionForms_QuantityComparator`](#schema-executionforms-quantitycomparator) \| `null` | `optional` |  |
| `type` | `string` (enum: `quantity`) | `optional` |  |
| `unit` | `string` | `required` | The UCUM unit expression, case-sensitive; `1` for a dimensionless quantity. |
| `value` | `number` (`double`) | `required` | The amount as entered. |

</details>

### `ExecutionForms_ObservationValueTextValue` {#schema-executionforms-observationvaluetextvalue}

- **Type:** object
- **Description:** Free text as entered, such as a verbatim adverse event term.

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `type` | `string` (enum: `text`) | `optional` |  |
| `value` | `string` | `required` | The text. |

</details>

### `ExecutionForms_OpenFormRequest` {#schema-executionforms-openformrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `collected_at` | `string` (`date-time`) | `required` |  |
| `collection_spec_id` | `string` | `required` |  |
| `enrollment_id` | `string` (`uuid`) | `required` |  |
| `form_id` | `string` (`uuid`) \| `null` | `required` |  |
| `planned_activity_id` | `string` \| `null` | `required` |  |

</details>

### `ExecutionForms_PreviewFormItemValueRequest` {#schema-executionforms-previewformitemvaluerequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `item_id` | `string` | `required` |  |
| `value` | [`ExecutionForms_ObservationValue`](#schema-executionforms-observationvalue) \| `null` | `required` |  |

</details>

### `ExecutionForms_PreviewFormValuesRequest` {#schema-executionforms-previewformvaluesrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (1)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `values` | array of [`ExecutionForms_PreviewFormItemValueRequest`](#schema-executionforms-previewformitemvaluerequest) | `required` |  |

</details>

### `ExecutionForms_ProblemDetails` {#schema-executionforms-problemdetails}

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

### `ExecutionForms_QuantityComparator` {#schema-executionforms-quantitycomparator}

- **Type:** enum
- **Allowed Values:** `<`, `<=`, `>`, `>=`

### `ExecutionForms_RaiseFormQueryRequest` {#schema-executionforms-raiseformqueryrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `assignee` | `string` \| `null` | `optional` |  |
| `item_id` | `string` \| `null` | `optional` |  |
| `observation_id` | `string` (`uuid`) \| `null` | `required` |  |
| `owner_role` | `string` \| `null` | `optional` |  |
| `priority` | `string` \| `null` | `optional` |  |
| `query_id` | `string` (`uuid`) \| `null` | `optional` |  |
| `revision` | `integer` (`int32`) \| `null` | `required` |  |
| `text` | `string` | `required` |  |

</details>

### `ExecutionForms_SaveFormValuesRequest` {#schema-executionforms-saveformvaluesrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (1)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `values` | array of [`ExecutionForms_FormValueRequest`](#schema-executionforms-formvaluerequest) | `required` |  |

</details>

### `ExecutionForms_SignFormRequest` {#schema-executionforms-signformrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `content_hash` | `string` | `required` |  |
| `meaning` | `string` | `required` |  |
| `signature_id` | `string` (`uuid`) \| `null` | `required` |  |

</details>

