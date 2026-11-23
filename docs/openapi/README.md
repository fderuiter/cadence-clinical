# OpenAPI specifications

The top-level `*_openapi.json` files are the OpenAPI documents the .NET services actually serve, one per service or consolidated kernel module, with each contract's schemas prefixed by its name. Don't edit them by hand.

- Regenerate them with `pnpm cadence sync-openapi-spec`. It runs `Cadence.OpenApiExporter`, which boots each service in memory and reads its OpenAPI document.
- Regenerate the matching `docs/api/*.md` pages with `pnpm cadence generate-api-docs`.
- The `openapi` quality gate (`pnpm cadence check openapi`) fails when either is out of date.

## `legacy/`

`legacy/` holds the specs of the Python services this platform was ported from. The .NET services don't serve them: the legacy aggregate lists 878 paths, and the .NET services serve 53. They stay because the web app is still typed against them:

- `apps/web/src/api/types.ts` was generated from `legacy/cdisc_openapi.json` with `openapi-typescript`.

Most web API calls therefore target endpoints that no .NET service implements yet; [`docs/web/API_MIGRATION.md`](../web/API_MIGRATION.md) tracks moving the web onto these specs. Once the web clients move to the specs above, `legacy/` can be deleted. No gate checks the legacy specs.
