# OpenAPI specifications

The top-level `*_openapi.json` files are the OpenAPI documents the .NET services actually serve, one per service, plus `cdisc_openapi.json`, which aggregates them with each service's schemas prefixed by its name. Don't edit them by hand.

- Regenerate them with `pnpm cadence sync-openapi-spec`. It runs `Cadence.OpenApiExporter`, which boots each service in memory and reads its OpenAPI document.
- Regenerate the matching `docs/api/*.md` pages with `pnpm cadence generate-api-docs`.
- The `openapi` quality gate (`pnpm cadence check openapi`) fails when either is out of date.

## Remaining Python-era Web Client Types

All legacy Python OpenAPI snapshots formerly kept under `docs/openapi/` have been retired ([ADR-2816](../adr/2026-11-19-retire-orphaned-legacy-web-type-schemas.md), [ADR-2678](../adr/2026-11-24-retire-legacy-openapi-snapshot-and-isolate-legacy-web-client.md)). Live per-service and per-module `.NET` operation counts are generated directly in [`docs/api/index.md`](../api/index.md).

- Migrated web clients bind to `apps/web/src/api/generated/<contract>.ts` via `createOpenApiClient<Paths>()` in `apps/web/src/api/client.ts`.
- Remaining unmigrated Python-era calls import `openApiClient` from `apps/web/src/api/legacyClient.ts` and compile against `apps/web/src/api/types.ts` (pruned in place to **63 paths**, **69 called operations**, and **156 reachable schemas**, enforced by `apps/web/tests/api/interop_esource_and_legacy_types.spec.ts`).

[`docs/web/API_MIGRATION.md`](../web/API_MIGRATION.md) tracks moving the remaining web clients onto the `.NET` specs above; once the last legacy calls migrate, `apps/web/src/api/legacyClient.ts` and `apps/web/src/api/types.ts` can be deleted.
