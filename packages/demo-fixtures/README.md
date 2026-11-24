# Demo Fixtures (`demo-fixtures`)

Canonical synthetic trial catalog and MSW-free payload builders shared across the public static demos (`apps/web` and `apps/subject-portal`).

## Overview

`demo-fixtures` exports the single reference study (`CADENCE-101`), sponsor (`CADENCE-PHARMA`), sites (`SITE-101` and `SITE-102` with `complianceStage`), screened participants (`SUBJ-101-001`, `SUBJ-101-002`, `SUBJ-102-001`), deterministic RFC 9562 UUIDv8 identifiers, fixed epoch (`2026-01-05T09:00:00Z`), and eleven role-named Keycloak realm personas (`crc.site101`, `pi.site101`, `cra.monitor`, `dm.lead`, `designer_demo`, `designer.lead`, `safety.officer`, `auditor.lead`, `stat.author`, `stat.approver`, `pharmacist.site101`).

## Build-Time Synthesis and Drift Verification

`DEMO_MANIFEST` is synthesized virtually (`virtual:demo-fixtures-manifest` in `scripts/vite-strip-demo-fixtures.mjs`) during Vite demo builds, dev servers, and Vitest runs directly from the authoritative JSON seeds—with zero committed `manifest.js` or `manifest.d.ts` files on disk:

- `packages/demo-fixtures/demo-seed.json` (canonical `CADENCE-101` study, sponsor, sites with `complianceStage`, participants, roles, and user grants embedded and loaded via source-generated `System.Text.Json` in `apps/kernel-api/src/DevelopmentDemoData.cs` and `apps/execution/src/Infrastructure/Persistence/ExecutionDbSeeder.cs`; ADR-2543, ADR-2549)
- `docker/cadence-realm.json`

Verify in-memory parity between `demo-seed.json` and `docker/cadence-realm.json` along with declaration types without modifying files (also run by `pnpm cadence check` and `pnpm typecheck`):

```bash
pnpm cadence check demo-manifest
pnpm --filter demo-fixtures typecheck
```

## Subpath Exports and Production Bundle Isolation

- `demo-fixtures` (`src/index.js` and `src/index.d.ts`): exports `DEMO_MANIFEST`, the canonical `CADENCE-101` study, sites, participants, epoch constants (`DEMO_EPOCH_ISO`, `DEMO_EPOCH_MS`), and synthetic personas. Production Vite builds (`pnpm --filter web build` and `pnpm --filter subject-portal build`) resolve bare `"demo-fixtures"` imports to an inert virtual stub via `scripts/vite-strip-demo-fixtures.mjs` (`createDemoFixturesPlugin`), keeping synthetic study payloads out of production JavaScript bundles while preserving full static demo functionality in `build:demo`, `dev`, and Vitest suites.
- `demo-fixtures/runtime` (`src/runtime.js` and `src/runtime.d.ts`): exports `isDemoBuild`, `createDemoRuntime`, and `createUnbackedActionGuard` without importing `./index.js`. App wrappers (`apps/web/src/utils/demoMode.js`, `apps/subject-portal/src/utils/demoMode.js`, and `unbackedActions.js`) pass `DEMO_EPOCH_ISO` and `DEMO_EPOCH_MS` via bare `"demo-fixtures"` imports so production bundles retain safe clock/Web Crypto fallbacks and Part 11 action guards with zero `CADENCE-101` manifest strings.
- `demo-fixtures/mocks` (`src/mocks.js` and `src/mocks.d.ts`): exports MSW-free payload builders (`buildDemoHealthPayload`, `buildSignatureVerificationOutcome`, `buildAmendmentReconsentPayload`, `extractDemoReceiptOcr`, and `evaluateDemoExpenseClaim`) shared across `apps/web` and `apps/subject-portal` demo handlers, and resolves to an inert virtual stub in non-demo production builds.
