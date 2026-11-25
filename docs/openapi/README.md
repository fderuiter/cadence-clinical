# OpenAPI specifications

The top-level `*_openapi.json` files are the OpenAPI documents the .NET services actually serve, one per service, plus `cdisc_openapi.json`, which aggregates them with each service's schemas prefixed by its name. Don't edit them by hand.

- Regenerate them with `pnpm cadence sync-openapi-spec`. It runs `Cadence.OpenApiExporter`, which boots each service in memory and reads its OpenAPI document.
- Regenerate the matching [`docs/api/*.md`](../api/index.md) pages with `pnpm cadence generate-api-docs`; [`docs/api/index.md`](../api/index.md) is the single source of truth for per-service operation counts.
- The `openapi` quality gate (`pnpm cadence check openapi`) fails when either is out of date.

## Web client contracts

- Migrated web clients in `apps/web/src/api/` use `createOpenApiClient<paths>()` from `apps/web/src/api/client.ts` against `apps/web/src/api/generated/<contract>.ts` (`pnpm --filter web generate:api`, verified by `pnpm cadence check api-clients`).
- Remaining unmigrated Python-era web calls isolate onto `apps/web/src/api/legacyClient.ts` over `apps/web/src/api/types.ts`, which is pruned in place to the **63 paths**, **69 called operations**, and **156 reachable schemas** still invoked by legacy callers ([ADR-2677](../adr/2026-11-24-retire-legacy-openapi-snapshot-and-isolate-legacy-web-client.md)). [`docs/web/API_MIGRATION.md`](../web/API_MIGRATION.md) tracks moving those remaining callers onto `.NET` service contracts so `legacyClient.ts` and `types.ts` can be deleted.
