# OpenAPI specifications

The top-level `*_openapi.json` files are the OpenAPI 3.1 contracts the .NET 10 services and consolidated kernel-host modules actually serve (`20` contracts: `13` **Active** and `7` **Scaffold** per `packages/topology/src/ServiceTopology.cs`), plus [`cdisc_openapi.json`](cdisc_openapi.json), which aggregates all `20` contracts (`300` unique gateway paths and `329` unique operations across `317` per-contract paths, `346` per-contract operations, and `550` component schemas) with each contract's schemas prefixed by `<service>_` and root server URLs (`https://api.ccrsoft.com`, `http://localhost:8000`). Don't edit them by hand.

- **Export contracts:** Regenerate `docs/openapi/*.json` with `pnpm cadence sync-openapi-spec` (or `pnpm openapi:export`). It runs `Cadence.OpenApiExporter`, which boots every standalone service (including `gateway` with YARP `{**catch-all}` proxy routes filtered out so gateway-owned endpoints such as `POST /api/v1/auth/signature-verification` are captured cleanly) and `Cadence.Kernel.Host` (`kernel`, `intelligence`, `execution_forms`, including the Streamable HTTP MCP transport `POST /api/v1/ai/mcp`) in memory.
- **Shared security & GxP metadata:** `CadenceOpenApiTransformers` enriches every exported contract with `components.securitySchemes.BearerAuth`, required `Permission:<resource:action>` or `SignedInCaller` policies, clinical scope isolation metadata, rate-limit policies, RFC 7807 `ProblemDetails` error schemas, `x-kernel-errors` codes, and regulated GxP headers (`X-Sig-Token`, `Idempotency-Key`, `X-Change-Reason`).
- **Schema-linked Markdown reference:** Regenerate [`docs/api/*.md`](../api/index.md) with `pnpm cadence generate-api-docs`. Every operation renders collapsible parameter, header, request/response schema, and error tables linked to a deduplicated per-page `components.schemas` catalog, `ServiceTopology` `Active`/`Scaffold` maturity badges, and explicit HTTP `501 Not Implemented` stub callouts.
- **Static portal downloads:** `pnpm docs:build` copies top-level `*_openapi.json` contracts and `cdisc_openapi.json` (excluding `legacy/`) to `docs/.vitepress/dist/openapi/` so portal links resolve to downloadable `/openapi/*.json` static assets.
- **Drift & completeness gate:** `pnpm cadence check openapi` (`OpenApiDriftValidatorService`) fails when any live service contract drifts from `docs/openapi/*.json`, when any operation is missing a non-empty `operationId` or `summary`, or when `docs/api/*.md` is out of date.

## `legacy/`

`legacy/` holds the frozen specs of the Python services this platform was ported from. The .NET services don't serve them: `legacy/cdisc_openapi.json` lists `878` Python-era paths, whereas the live .NET `cdisc_openapi.json` aggregate serves `300` unique gateway paths and `329` unique operations (`346` per-contract operations across `317` per-contract paths). They stay only where legacy web screens have not yet migrated onto `apps/web/src/api/generated/*.ts`:

- `apps/web/src/api/types.ts` was generated from `legacy/cdisc_openapi.json` with `openapi-typescript`.
- `apps/web/src/api/generated/*.ts` is generated from the live top-level `docs/openapi/*_openapi.json` contracts via `pnpm --filter web generate:api` and verified by `pnpm cadence check api-clients`.

[`docs/web/API_MIGRATION.md`](../web/API_MIGRATION.md) tracks moving the remaining web screens onto the live .NET contracts. Once the web clients finish migrating off `types.ts`, `legacy/` can be deleted. No gate checks `legacy/`.
