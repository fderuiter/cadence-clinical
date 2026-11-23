# Demo Fixtures (`demo-fixtures`)

Canonical synthetic trial catalog shared across the public static demos
(`apps/web` and `apps/subject-portal`).

## Overview

`demo-fixtures` exports the single reference study (`CADENCE-101`), sponsor
(`CADENCE-PHARMA`), sites (`SITE-101` and `SITE-102`), screened participants
(`SUBJ-101-001`, `SUBJ-101-002`, `SUBJ-102-001`), deterministic RFC 9562 UUIDv8
identifiers, fixed epoch (`2026-01-05T09:00:00Z`), and eight role-named Keycloak
realm personas (`crc.site101`, `pi.site101`, `cra.monitor`, `dm.lead`,
`designer_demo`, `designer.lead`, `safety.officer`, `auditor.lead`).

## Generation and Drift Verification

`src/manifest.js` and `src/manifest.d.ts` are generated from the authoritative
demo seed sources:

- `packages/demo-fixtures/demo-seed.json` (canonical `CADENCE-101` study,
  sponsor, sites, participants, roles, and user grants loaded via
  source-generated `System.Text.Json` by
  `apps/kernel-api/src/DevelopmentDemoData.cs`; ADR-2543, ADR-2549)
- `apps/execution/src/Infrastructure/Persistence/ExecutionDbSeeder.cs`
- `docker/cadence-realm.json`

Regenerate after modifying any of those seed files (also run automatically by
`pnpm cadence fix`):

```bash
pnpm cadence generate-demo-manifest
```

Verify parity without modifying files (also run by `pnpm cadence check`):

```bash
pnpm cadence check demo-manifest
```

## Production Bundle Isolation

Production Vite builds (`pnpm --filter web build` and
`pnpm --filter subject-portal build`) resolve `demo-fixtures` to an inert
virtual stub via `scripts/vite-strip-demo-fixtures.mjs`
(`createDemoFixturesPlugin`) and guard demo consumers behind `isDemoBuild()`,
keeping synthetic study fixtures out of production JavaScript bundles while
preserving full static demo functionality in `build:demo`, `dev`, and Vitest
suites.
