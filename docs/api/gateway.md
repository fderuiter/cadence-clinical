# gateway API

Generated from `docs/openapi/gateway_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

- **Service Maturity:** <span class="VPBadge tip">Active</span> (Cadence API Gateway · port `8000` · edge OIDC, rate-limiting & step-up `X-Sig-Token` verification)
- **OpenAPI 3.1 Contract:** [`gateway_openapi.json`](../openapi/gateway_openapi.json) (3 operation(s), 6 schema(s))

## Operations

| Method | Path | Operation | Summary | Contract |
| ------ | ---- | --------- | ------- | -------- |
| `POST` | `/api/v1/auth/signature-verification` | IssueSignatureToken | Re-authenticates the signer and issues a single-use e-signature token for one action. | [Details](#op-issuesignaturetoken) |
| `GET` | `/api/v1/status` | GatewayStatus | Returns gateway dispatch status. | [Details](#op-gatewaystatus) |
| `GET` | `/healthz` | HealthCheck | Gateway liveness and readiness probe. | [Details](#op-healthcheck) |

### `POST` `/api/v1/auth/signature-verification` {#op-issuesignaturetoken}

**Operation ID:** `IssueSignatureToken` — Re-authenticates the signer and issues a single-use e-signature token for one action.

21 CFR 11.200(a)(1) step-up credential check: verifies the signed-in caller's password (and optional TOTP) against Keycloak and returns a single-use 60-second X-Sig-Token bound to one canonical API path and semantic action.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `BearerAuth` (`Authenticated`)
- **Rate Limit:** 5 requests/minute per authenticated signer (FixedWindow; answers 429 Too Many Requests with Retry-After).
- **Tags:** `Electronic Signatures`

**Parameters & Headers**

| Name | In | Required | Schema / Constraints | Description |
| ---- | -- | -------- | -------------------- | ----------- |
| `X-Change-Reason` | `header` | `optional` | `string` | Optional reason-for-change header validated by GatewayIdentityForwarding (printable ASCII or RFC 8187 UTF-8''); malformed values return 400 Bad Request. |

**Request Body** (`required`)

| Media Type | Schema |
| ---------- | ------ |
| `application/json` | [`Gateway_SignatureVerificationRequest`](#schema-gateway-signatureverificationrequest) |

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Gateway_SignatureTokenResponse`](#schema-gateway-signaturetokenresponse) |
| `400` | Bad Request | `application/problem+json` | [`Gateway_HttpValidationProblemDetails`](#schema-gateway-httpvalidationproblemdetails) |
| `401` | Unauthorized | `application/problem+json` | [`Gateway_ProblemDetails`](#schema-gateway-problemdetails) |
| `403` | Forbidden | `application/problem+json` | [`Gateway_ProblemDetails`](#schema-gateway-problemdetails) |
| `429` | Too Many Requests | `application/problem+json` | [`Gateway_ProblemDetails`](#schema-gateway-problemdetails) |
| `503` | Service Unavailable | `application/problem+json` | [`Gateway_ProblemDetails`](#schema-gateway-problemdetails) |

</details>

### `GET` `/api/v1/status` {#op-gatewaystatus}

**Operation ID:** `GatewayStatus` — Returns gateway dispatch status.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Gateway`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Gateway_GatewayStatusResponse`](#schema-gateway-gatewaystatusresponse) |

</details>

### `GET` `/healthz` {#op-healthcheck}

**Operation ID:** `HealthCheck` — Gateway liveness and readiness probe.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Gateway`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Gateway_GatewayHealthResponse`](#schema-gateway-gatewayhealthresponse) |

</details>

## Schemas

Deduplicated component schemas exported in `components.schemas` (6 total). Operation payloads and responses link here so nested models are defined once per page.

### `Gateway_GatewayHealthResponse` {#schema-gateway-gatewayhealthresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (3)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `service` | `string` | `required` |  |
| `status` | `string` | `required` |  |
| `version` | `string` | `required` |  |

</details>

### `Gateway_GatewayStatusResponse` {#schema-gateway-gatewaystatusresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `gateway` | `string` | `required` |  |
| `mode` | `string` | `required` |  |

</details>

### `Gateway_HttpValidationProblemDetails` {#schema-gateway-httpvalidationproblemdetails}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `detail` | `string` \| `null` | `optional` |  |
| `errors` | `object` | `optional` |  |
| `instance` | `string` \| `null` | `optional` |  |
| `status` | `integer` (`int32`) \| `null` | `optional` |  |
| `title` | `string` \| `null` | `optional` |  |
| `type` | `string` \| `null` | `optional` |  |

</details>

### `Gateway_ProblemDetails` {#schema-gateway-problemdetails}

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

### `Gateway_SignatureTokenResponse` {#schema-gateway-signaturetokenresponse}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `expires_in` | `integer` (`int32`) | `required` |  |
| `sig_token` | `string` | `required` |  |

</details>

### `Gateway_SignatureVerificationRequest` {#schema-gateway-signatureverificationrequest}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (6)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `action` | `string` \| `null` | `required` |  |
| `batch_id` | `string` \| `null` | `required` |  |
| `password` | `string` \| `null` | `required` |  |
| `semantic_action` | `string` \| `null` | `required` |  |
| `totp` | `string` \| `null` | `required` |  |
| `username` | `string` \| `null` | `required` |  |

</details>

