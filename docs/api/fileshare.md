# fileshare API

Generated from `docs/openapi/fileshare_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

- **Service Maturity:** <span class="VPBadge warning">Scaffold</span> (Port `8013` · Gateway prefixes: `/api/v1/fileshare`)
- **OpenAPI 3.1 Contract:** [`fileshare_openapi.json`](../openapi/fileshare_openapi.json) (2 operation(s), 1 schema(s))

> **Scaffold Service (`ServiceTopology`):** This host provides production authentication, probes, and OpenAPI contract boundaries, while full domain persistence or downstream workflow coverage remains scheduled on the consolidation roadmap (`ARCHITECTURE.md §3`).

## Operations

| Method | Path | Operation | Summary | Contract |
| ------ | ---- | --------- | ------- | -------- |
| `GET` | `/api/v1/fileshare/status` | FileshareStatus | Returns FileShare operational status. | [Details](#op-filesharestatus) |
| `GET` | `/healthz` | FileshareHealthCheck | FileShare storage health probe. | [Details](#op-filesharehealthcheck) |

### `GET` `/api/v1/fileshare/status` {#op-filesharestatus}

**Operation ID:** `FileshareStatus` — Returns FileShare operational status.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Fileshare`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Fileshare_AnonymousTypeOfstringAndstring`](#schema-fileshare-anonymoustypeofstringandstring) |

</details>

### `GET` `/healthz` {#op-filesharehealthcheck}

**Operation ID:** `FileshareHealthCheck` — FileShare storage health probe.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Fileshare`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Fileshare_AnonymousTypeOfstringAndstring`](#schema-fileshare-anonymoustypeofstringandstring) |

</details>

## Schemas

Deduplicated component schemas exported in `components.schemas` (1 total). Operation payloads and responses link here so nested models are defined once per page.

### `Fileshare_AnonymousTypeOfstringAndstring` {#schema-fileshare-anonymoustypeofstringandstring}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `service` | `string` \| `null` | `required` |  |
| `status` | `string` \| `null` | `required` |  |

</details>

