# org API

Generated from `docs/openapi/org_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

- **Service Maturity:** <span class="VPBadge warning">Scaffold</span> (Port `8012` · Gateway prefixes: `/api/v1/org`)
- **OpenAPI 3.1 Contract:** [`org_openapi.json`](../openapi/org_openapi.json) (2 operation(s), 1 schema(s))

> **Scaffold Service (`ServiceTopology`):** This host provides production authentication, probes, and OpenAPI contract boundaries, while full domain persistence or downstream workflow coverage remains scheduled on the consolidation roadmap (`ARCHITECTURE.md §3`).

## Operations

| Method | Path | Operation | Summary | Contract |
| ------ | ---- | --------- | ------- | -------- |
| `GET` | `/api/v1/org/status` | OrgStatus | Returns Organization directory operational status. | [Details](#op-orgstatus) |
| `GET` | `/healthz` | OrgHealthCheck | Organization directory health probe. | [Details](#op-orghealthcheck) |

### `GET` `/api/v1/org/status` {#op-orgstatus}

**Operation ID:** `OrgStatus` — Returns Organization directory operational status.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Org`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Org_AnonymousTypeOfstringAndstring`](#schema-org-anonymoustypeofstringandstring) |

</details>

### `GET` `/healthz` {#op-orghealthcheck}

**Operation ID:** `OrgHealthCheck` — Organization directory health probe.

<details class="api-operation-details">
<summary>Authorization, Parameters, Payloads &amp; Responses</summary>

- **Authentication:** `AllowAnonymous` (no bearer token required)
- **Tags:** `Cadence.Apps.Org`

**Responses**

| Status | Description | Media Type | Schema |
| ------ | ----------- | ---------- | ------ |
| `200` | OK | `application/json` | [`Org_AnonymousTypeOfstringAndstring`](#schema-org-anonymoustypeofstringandstring) |

</details>

## Schemas

Deduplicated component schemas exported in `components.schemas` (1 total). Operation payloads and responses link here so nested models are defined once per page.

### `Org_AnonymousTypeOfstringAndstring` {#schema-org-anonymoustypeofstringandstring}

- **Type:** object

<details class="api-schema-details">
<summary>Properties (2)</summary>

| Property | Type | Required | Description |
| -------- | ---- | -------- | ----------- |
| `service` | `string` \| `null` | `required` |  |
| `status` | `string` \| `null` | `required` |  |

</details>

