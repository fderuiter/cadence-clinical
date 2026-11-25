# quality API

Generated from `docs/openapi/quality_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

- **Service Maturity:** <span class="VPBadge warning">Scaffold</span> (Port `8005` · Gateway prefixes: `/api/v1/quality`)
- **OpenAPI 3.1 Contract:** [`quality_openapi.json`](../openapi/quality_openapi.json) (2 operation(s), 1 schema(s))

> **Scaffold Service (`ServiceTopology`):** This host provides production authentication, probes, and OpenAPI contract boundaries, while full domain persistence or downstream workflow coverage remains scheduled on the consolidation roadmap (`ARCHITECTURE.md §3`).

## Operations

| Method | Path | Operation | Summary | Contract |
| ------ | ---- | --------- | ------- | -------- |
| `GET` | `/api/v1/quality/status` | QualityStatus | Returns Quality management operational status. | [Details](#op-qualitystatus) |
| `GET` | `/healthz` | QualityHealthCheck | Quality management health probe. | [Details](#op-qualityhealthcheck) |

### `GET` `/api/v1/quality/status` {#op-qualitystatus}

**Operation ID:** `QualityStatus` — Returns Quality management operational status.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Quality`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Quality_AnonymousTypeOfstringAndstring`](#schema-quality-anonymoustypeofstringandstring) |

</details>

### `GET` `/healthz` {#op-qualityhealthcheck}

**Operation ID:** `QualityHealthCheck` — Quality management health probe.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Quality`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Quality_AnonymousTypeOfstringAndstring`](#schema-quality-anonymoustypeofstringandstring) |

</details>

## Schemas

Deduplicated component schemas exported in `components.schemas` (1 total). Operation payloads and responses link here so nested models are defined once per page.

### `Quality_AnonymousTypeOfstringAndstring` {#schema-quality-anonymoustypeofstringandstring}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `service` | `string` \| `null` | `required` |  |
| `status` | `string` \| `null` | `required` |  |

</details>

