# eisf API

Generated from `docs/openapi/eisf_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

- **Service Maturity:** <span class="VPBadge warning">Scaffold</span> (Port `8010` · Gateway prefixes: `/api/v1/eisf`)
- **OpenAPI 3.1 Contract:** [`eisf_openapi.json`](../openapi/eisf_openapi.json) (2 operation(s), 1 schema(s))

> **Scaffold Service (`ServiceTopology`):** This host provides production authentication, probes, and OpenAPI contract boundaries, while full domain persistence or downstream workflow coverage remains scheduled on the consolidation roadmap (`ARCHITECTURE.md §3`).

## Operations

| Method | Path | Operation | Summary | Contract |
| ------ | ---- | --------- | ------- | -------- |
| `GET` | `/api/v1/eisf/status` | EisfStatus | Returns eISF operational status. | [Details](#op-eisfstatus) |
| `GET` | `/healthz` | EisfHealthCheck | eISF operations health probe. | [Details](#op-eisfhealthcheck) |

### `GET` `/api/v1/eisf/status` {#op-eisfstatus}

**Operation ID:** `EisfStatus` — Returns eISF operational status.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Eisf`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Eisf_AnonymousTypeOfstringAndstring`](#schema-eisf-anonymoustypeofstringandstring) |

</details>

### `GET` `/healthz` {#op-eisfhealthcheck}

**Operation ID:** `EisfHealthCheck` — eISF operations health probe.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Eisf`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Eisf_AnonymousTypeOfstringAndstring`](#schema-eisf-anonymoustypeofstringandstring) |

</details>

## Schemas

Deduplicated component schemas exported in `components.schemas` (1 total). Operation payloads and responses link here so nested models are defined once per page.

### `Eisf_AnonymousTypeOfstringAndstring` {#schema-eisf-anonymoustypeofstringandstring}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `service` | `string` \| `null` | `required` |  |
| `status` | `string` \| `null` | `required` |  |

</details>

