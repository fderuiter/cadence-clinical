# designer API

Generated from `docs/openapi/designer_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

- **Service Maturity:** <span class="VPBadge warning">Scaffold</span> (Port `8001` · Gateway prefixes: `/api/v1/studies`, `/api/v1/protocols`)
- **OpenAPI 3.1 Contract:** [`designer_openapi.json`](../openapi/designer_openapi.json) (6 operation(s), 27 schema(s))

> **Scaffold Service (`ServiceTopology`):** This host provides production authentication, probes, and OpenAPI contract boundaries, while full domain persistence or downstream workflow coverage remains scheduled on the consolidation roadmap (`ARCHITECTURE.md §3`).

## Operations

| Method | Path | Operation | Summary | Contract |
| ------ | ---- | --------- | ------- | -------- |
| `POST` | `/api/v1/protocols/compile` | CompileProtocol | Compiles CDISC USDM protocol JSON into DDF IR and EDC runtime schemas. | [Details](#op-compileprotocol) |
| `GET` | `/api/v1/studies` | ListStudies | Lists active CDISC USDM study protocol definitions. | [Details](#op-liststudies) |
| `POST` | `/api/v1/studies/{id}/rules/amendment-regression` | AmendmentRegression | Compares baseline and target rule pack versions during protocol amendments to highlight rule diffs and broken dependencies. | [Details](#op-amendmentregression) |
| `POST` | `/api/v1/studies/{id}/rules/batch-validate` | BatchValidateRules | Executes study-wide parallel batch compilation and validation across pinned edit check rule packs. | [Details](#op-batchvalidaterules) |
| `GET` | `/api/v1/studies/{id}/rules/batch-validate/{jobId}` | GetBatchValidationJob | Retrieves the aggregated report for an asynchronous batch validation job. | [Details](#op-getbatchvalidationjob) |
| `GET` | `/healthz` | DesignerHealthCheck | Protocol designer metadata health probe. | [Details](#op-designerhealthcheck) |

### `POST` `/api/v1/protocols/compile` {#op-compileprotocol}

**Operation ID:** `CompileProtocol` — Compiles CDISC USDM protocol JSON into DDF IR and EDC runtime schemas.

Stateless CDISC USDM compiler endpoint (Scaffold service maturity); compiles supplied USDM JSON in memory without persisting study definitions.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `study_design:create`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Cadence.Apps.Designer`

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Designer_CompileProtocolRequest`](#schema-designer-compileprotocolrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Designer_DDFCompilationResult`](#schema-designer-ddfcompilationresult) |
| `400` | Bad Request | `application/problem+json` | [`Designer_ProblemDetails`](#schema-designer-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Designer_ProblemDetails`](#schema-designer-problemdetails) |
| `422` | Unprocessable Entity | `application/problem+json` | [`Designer_ProblemDetails`](#schema-designer-problemdetails) |

</details>

### `GET` `/api/v1/studies` {#op-liststudies}

**Operation ID:** `ListStudies` — Lists active CDISC USDM study protocol definitions.

Scaffold placeholder returning an empty study list; persistent CDISC USDM study governance is served by the Clinical Kernel (/api/v1/kernel/studies).

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `study_design:read`
- **Clinical Scope Enforcement:** `CallerStudy`
- **Tags:** `Cadence.Apps.Designer`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | array of `string` |

</details>

### `POST` `/api/v1/studies/{id}/rules/amendment-regression` {#op-amendmentregression}

**Operation ID:** `AmendmentRegression` — Compares baseline and target rule pack versions during protocol amendments to highlight rule diffs and broken dependencies.

Compares inline or study-pinned baseline and target rule pack versions without persisting amendment history.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `study_design:create`
- **Clinical Scope Enforcement:** `RouteStudy:id`
- **Tags:** `Cadence.Apps.Designer`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `id` | `path` | `required` | `string` |  |

**Request Body** (`optional`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Designer_AmendmentRegressionApiRequest`](#schema-designer-amendmentregressionapirequest) \| `null` |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Designer_AmendmentRegressionReport`](#schema-designer-amendmentregressionreport) |
| `422` | Unprocessable Entity | `application/problem+json` | [`Designer_ProblemDetails`](#schema-designer-problemdetails) |

</details>

### `POST` `/api/v1/studies/{id}/rules/batch-validate` {#op-batchvalidaterules}

**Operation ID:** `BatchValidateRules` — Executes study-wide parallel batch compilation and validation across pinned edit check rule packs.

Validates inline or study-pinned rule packs and retains asynchronous job reports in the volatile in-memory BatchJobStore.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `study_design:create`
- **Clinical Scope Enforcement:** `RouteStudy:id`
- **Tags:** `Cadence.Apps.Designer`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `id` | `path` | `required` | `string` |  |

**Request Body** (`optional`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Designer_BatchValidateApiRequest`](#schema-designer-batchvalidateapirequest) \| `null` |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Designer_BatchValidationReport`](#schema-designer-batchvalidationreport) |
| `422` | Unprocessable Entity | `application/problem+json` | [`Designer_ProblemDetails`](#schema-designer-problemdetails) |

</details>

### `GET` `/api/v1/studies/{id}/rules/batch-validate/{jobId}` {#op-getbatchvalidationjob}

**Operation ID:** `GetBatchValidationJob` — Retrieves the aggregated report for an asynchronous batch validation job.

Scaffold service endpoint: reads batch validation reports from the volatile in-memory BatchJobStore.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Permission`)
- **Required Permission(s):** `study_design:create`
- **Clinical Scope Enforcement:** `RouteStudy:id`
- **Tags:** `Cadence.Apps.Designer`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `id` | `path` | `required` | `string` |  |
| `jobId` | `path` | `required` | `string` |  |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Designer_BatchValidationReport`](#schema-designer-batchvalidationreport) |
| `404` | Not Found | — | — |

</details>

### `GET` `/healthz` {#op-designerhealthcheck}

**Operation ID:** `DesignerHealthCheck` — Protocol designer metadata health probe.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Designer`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Designer_AnonymousTypeOfstringAndstringAndstringAndstring`](#schema-designer-anonymoustypeofstringandstringandstringandstring) |

</details>

## Schemas

Deduplicated component schemas exported in `components.schemas` (27 total). Operation payloads and responses link here so nested models are defined once per page.

### `Designer_AccessibilityReport` {#schema-designer-accessibilityreport}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `formHash` | `string` | `optional` |  |
| `inapplicable` | array of `object` \| `null` | `optional` |  |
| `incomplete` | array of `object` \| `null` | `optional` |  |
| `layoutErrors` | array of `string` \| `null` | `optional` |  |
| `passes` | array of `object` \| `null` | `optional` |  |
| `status` | `string` | `optional` |  |
| `violations` | array of `object` \| `null` | `optional` |  |

</details>

### `Designer_AmendmentRegressionApiRequest` {#schema-designer-amendmentregressionapirequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `baselinePack` | `any` | `optional` |  |
| `baselineVersion` | `string` \| `null` | `optional` |  |
| `records` | array of [`Designer_RuleRecord`](#schema-designer-rulerecord) \| `null` | `optional` |  |
| `targetPack` | `any` | `optional` |  |
| `targetVersion` | `string` \| `null` | `optional` |  |

</details>

### `Designer_AmendmentRegressionReport` {#schema-designer-amendmentregressionreport}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (7)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `baselineVersion` | `string` | `required` |  |
| `brokenAstReferences` | array of [`Designer_BrokenAstReference`](#schema-designer-brokenastreference) | `required` |  |
| `newViolations` | array of [`Designer_ValidationDetection`](#schema-designer-validationdetection) | `required` |  |
| `resolvedFindings` | array of [`Designer_ValidationDetection`](#schema-designer-validationdetection) | `required` |  |
| `ruleDiffs` | [`Designer_RuleDiffSummary`](#schema-designer-rulediffsummary) | `required` |  |
| `targetVersion` | `string` | `required` |  |
| `timing` | [`Designer_ValidationTimingMetrics`](#schema-designer-validationtimingmetrics) | `required` |  |

</details>

### `Designer_AnonymousTypeOfstringAndstringAndstringAndstring` {#schema-designer-anonymoustypeofstringandstringandstringandstring}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `compiler` | `string` \| `null` | `required` |  |
| `primaryDatastore` | `string` \| `null` | `required` |  |
| `service` | `string` \| `null` | `required` |  |
| `status` | `string` \| `null` | `required` |  |

</details>

### `Designer_BatchValidateApiRequest` {#schema-designer-batchvalidateapirequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `asyncThreshold` | `integer` (`int32`) \| `null` | `optional` |  |
| `isDryRun` | `boolean` \| `null` | `optional` |  |
| `records` | array of [`Designer_RuleRecord`](#schema-designer-rulerecord) \| `null` | `optional` |  |
| `rulePacks` | array of `any` \| `null` | `optional` |  |

</details>

### `Designer_BatchValidationReport` {#schema-designer-batchvalidationreport}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (9)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `circularDependencies` | array of `string` | `required` |  |
| `conflictingRules` | array of `string` | `required` |  |
| `detections` | array of [`Designer_ValidationDetection`](#schema-designer-validationdetection) | `required` |  |
| `isAsync` | `boolean` | `required` |  |
| `jobId` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `study` | [`Designer_StudyId`](#schema-designer-studyid) | `required` |  |
| `syntaxErrors` | array of `string` | `required` |  |
| `timing` | [`Designer_ValidationTimingMetrics`](#schema-designer-validationtimingmetrics) | `required` |  |

</details>

### `Designer_BrokenAstReference` {#schema-designer-brokenastreference}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (4)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `detail` | `string` | `required` |  |
| `referencedVariable` | `string` | `required` |  |
| `ruleId` | `string` | `required` |  |
| `ruleVersion` | `integer` (`int32`) | `required` |  |

</details>

### `Designer_CompileProtocolRequest` {#schema-designer-compileprotocolrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `studyId` | `string` \| `null` | `optional` |  |
| `usdmJson` | `string` | `required` |  |
| `versionTag` | `string` \| `null` | `optional` |  |

</details>

### `Designer_DDFCompilationResult` {#schema-designer-ddfcompilationresult}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `accessibilityReport` | [`Designer_AccessibilityReport`](#schema-designer-accessibilityreport) \| `null` | `optional` |  |
| `formHash` | `string` | `required` |  |
| `name` | `string` | `required` |  |
| `odmXml` | `string` | `required` |  |
| `openRosaXml` | `string` | `required` |  |
| `status` | `string` | `optional` |  |
| `studyId` | `string` | `required` |  |
| `warnings` | array of `object` \| `null` | `optional` |  |

</details>

### `Designer_EffectivePeriod` {#schema-designer-effectiveperiod}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `from` | `string` (`date`) \| `null` | `required` |  |
| `until` | `string` (`date`) \| `null` | `required` |  |

</details>

### `Designer_FindingTarget` {#schema-designer-findingtarget}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (5)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `dataset` | `string` | `required` |  |
| `key` | `string` | `required` |  |
| `kind` | [`Designer_FindingTargetKind`](#schema-designer-findingtargetkind) | `required` |  |
| `node` | [`Designer_NodeRef`](#schema-designer-noderef) | `optional` |  |
| `variable` | `string` \| `null` | `optional` |  |

</details>

### `Designer_FindingTargetKind` {#schema-designer-findingtargetkind}

- **Type:** integer

### `Designer_NodeRef` {#schema-designer-noderef}

- **Type:** any

### `Designer_ObservedVariable` {#schema-designer-observedvariable}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `value` | `string` \| `null` | `required` |  |
| `variable` | `string` | `required` |  |

</details>

### `Designer_ProblemDetails` {#schema-designer-problemdetails}

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

### `Designer_Rule` {#schema-designer-rule}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (10)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `applicability` | [`Designer_RuleApplicability`](#schema-designer-ruleapplicability) | `required` |  |
| `authority` | [`Designer_RuleAuthority`](#schema-designer-ruleauthority) | `required` |  |
| `citation` | [`Designer_SourceCitation`](#schema-designer-sourcecitation) | `required` |  |
| `condition` | [`Designer_RuleExpression`](#schema-designer-ruleexpression) | `required` |  |
| `effective` | [`Designer_EffectivePeriod`](#schema-designer-effectiveperiod) | `required` |  |
| `explanation` | `string` | `required` |  |
| `id` | `string` | `required` |  |
| `remediationGuidance` | `string` \| `null` | `required` |  |
| `severity` | [`Designer_ValidationSeverity`](#schema-designer-validationseverity) | `required` |  |
| `version` | `integer` (`int32`) | `required` |  |

</details>

### `Designer_RuleApplicability` {#schema-designer-ruleapplicability}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `agency` | `string` \| `null` | `required` |  |
| `dataset` | `string` \| `null` | `required` |  |
| `stage` | [`Designer_ValidationStage`](#schema-designer-validationstage) | `required` |  |
| `standard` | `string` \| `null` | `required` |  |
| `standardVersion` | `string` \| `null` | `required` |  |
| `variable` | `string` \| `null` | `optional` |  |

</details>

### `Designer_RuleAuthority` {#schema-designer-ruleauthority}

- **Type:** integer

### `Designer_RuleDiffSummary` {#schema-designer-rulediffsummary}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `addedRules` | array of [`Designer_Rule`](#schema-designer-rule) | `required` |  |
| `modifiedRules` | array of [`Designer_Rule`](#schema-designer-rule) | `required` |  |
| `removedRules` | array of [`Designer_Rule`](#schema-designer-rule) | `required` |  |

</details>

### `Designer_RuleExpression` {#schema-designer-ruleexpression}

- **Type:** any

### `Designer_RuleRecord` {#schema-designer-rulerecord}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `dataset` | `string` | `required` |  |
| `key` | `string` | `required` |  |
| `values` | `object` | `required` |  |

</details>

### `Designer_SourceCitation` {#schema-designer-sourcecitation}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `document` | `string` | `required` |  |
| `section` | `string` \| `null` | `required` |  |
| `uri` | `string` \| `null` | `required` |  |

</details>

### `Designer_StudyId` {#schema-designer-studyid}

- **Type:** any

### `Designer_ValidationDetection` {#schema-designer-validationdetection}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (8)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `expected` | `string` \| `null` | `required` |  |
| `message` | `string` | `required` |  |
| `observed` | array of [`Designer_ObservedVariable`](#schema-designer-observedvariable) | `required` |  |
| `packKey` | `string` | `required` |  |
| `ruleId` | `string` | `required` |  |
| `ruleVersion` | `integer` (`int32`) | `required` |  |
| `severity` | [`Designer_ValidationSeverity`](#schema-designer-validationseverity) | `required` |  |
| `target` | [`Designer_FindingTarget`](#schema-designer-findingtarget) | `required` |  |

</details>

### `Designer_ValidationSeverity` {#schema-designer-validationseverity}

- **Type:** integer

### `Designer_ValidationStage` {#schema-designer-validationstage}

- **Type:** integer

### `Designer_ValidationTimingMetrics` {#schema-designer-validationtimingmetrics}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `completedAt` | `string` (`date-time`) | `required` |  |
| `durationMs` | `number` (`double`) | `required` |  |
| `parallelWorkerCount` | `integer` (`int32`) | `required` |  |
| `recordsEvaluated` | `integer` (`int32`) | `required` |  |
| `rulesEvaluated` | `integer` (`int32`) | `required` |  |
| `startedAt` | `string` (`date-time`) | `required` |  |

</details>

