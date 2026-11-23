# Demo Fixtures (`demo-fixtures`)

Canonical synthetic trial catalog shared across the public static demos (`apps/web` and `apps/subject-portal`).

## Overview

`demo-fixtures` exports the single reference study (`CADENCE-101`), sponsor (`CADENCE-PHARMA`), sites (`SITE-101` and `SITE-102`), screened participants (`SUBJ-101-001`, `SUBJ-101-002`, `SUBJ-102-001`), deterministic RFC 9562 UUIDv8 identifiers, fixed epoch (`2026-01-05T09:00:00Z`), and eleven role-named Keycloak realm personas (`crc.site101`, `pi.site101`, `cra.monitor`, `dm.lead`, `designer_demo`, `designer.lead`, `safety.officer`, `auditor.lead`, `stat.author`, `stat.approver`, `pharmacist.site101`).

## Generation and Drift Verification

`src/manifest.js` and `src/manifest.d.ts` are generated from the authoritative demo seed sources:

- `packages/demo-fixtures/demo-seed.json` (canonical `CADENCE-101` study, sponsor, sites, participants, roles, and user grants loaded via source-generated `System.Text.Json` by `apps/kernel-api/src/DevelopmentDemoData.cs`; ADR-2543, ADR-2549)
- `apps/execution/src/Infrastructure/Persistence/ExecutionDbSeeder.cs`
- `docker/cadence-realm.json`

Regenerate after modifying any of those seed files (also run automatically by `pnpm cadence fix`):

```bash
pnpm cadence generate-demo-manifest
```

Verify parity and declaration types without modifying files (also run by `pnpm cadence check` and `pnpm typecheck`):

```bash
pnpm cadence check demo-manifest
pnpm --filter demo-fixtures typecheck
```

## Production Bundle Isolation

- `demo-fixtures` (root entry `src/index.js`): exports the canonical `CADENCE-101` study, sites, participants, epoch constants (`DEMO_EPOCH_ISO`, `DEMO_EPOCH_MS`), and synthetic personas. Production Vite builds (`pnpm --filter web build` and `pnpm --filter subject-portal build`) resolve bare `"demo-fixtures"` imports to an inert virtual stub via `scripts/vite-strip-demo-fixtures.mjs` (`createDemoFixturesPlugin`), keeping synthetic study payloads out of production JavaScript bundles while preserving full static demo functionality in `build:demo`, `dev`, and Vitest suites.
- `demo-fixtures/runtime` (`src/runtime.js` and `src/runtime.d.ts`): exports `isDemoBuild`, `createDemoRuntime`, and `createUnbackedActionGuard` without importing `./index.js` or `./manifest.js`. App wrappers (`apps/web/src/utils/demoMode.js`, `apps/subject-portal/src/utils/demoMode.js`, and `unbackedActions.js`) pass `DEMO_EPOCH_ISO` and `DEMO_EPOCH_MS` via bare `"demo-fixtures"` imports so production bundles retain safe clock/Web Crypto fallbacks and Part 11 action guards with zero `CADENCE-101` manifest strings.
