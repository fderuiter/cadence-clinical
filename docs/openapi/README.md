# OpenAPI specifications

The top-level `*_openapi.json` files are the OpenAPI documents the .NET services actually serve, one per service or consolidated kernel module, with each contract's schemas prefixed by its name. Don't edit them by hand.

- Regenerate them with `pnpm cadence sync-openapi-spec`. It runs `Cadence.OpenApiExporter`, which boots each service in memory and reads its OpenAPI document.
- Regenerate the matching `docs/api/*.md` pages with `pnpm cadence generate-api-docs`.
- The `openapi` quality gate (`pnpm cadence check openapi`) fails when either is out of date.

## `legacy/`

`legacy/` retains only `legacy/cdisc_openapi.json`, the aggregated 878-path Python-era OpenAPI snapshot (the 19 per-service Python `*_openapi.json` snapshots were retired per [ADR-2816](../adr/2026-11-19-retire-orphaned-legacy-web-type-schemas.md)). Across the 19 exported `.NET` service and module specs in `docs/openapi/*_openapi.json`, the platform currently serves **342 total operations** (**310 non-probe operations** alongside 32 `GET /healthz` and `GET /api/v1/<service>/status` probes).

- `apps/web/src/api/types.ts` was originally generated from `legacy/cdisc_openapi.json` with `openapi-typescript` and is pruned in place to the **69 reachable paths** and **175 transitive schemas** still referenced by unmigrated web clients, while migrated clients bind directly to `apps/web/src/api/generated/<contract>.ts`.

[`docs/web/API_MIGRATION.md`](../web/API_MIGRATION.md) tracks moving the remaining web clients onto the `.NET` specs above. Once the remaining web calls migrate, `legacy/cdisc_openapi.json` and `apps/web/src/api/types.ts` can be deleted. No gate checks `legacy/cdisc_openapi.json`.
