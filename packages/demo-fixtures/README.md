# Demo Fixtures (`demo-fixtures`)

Canonical synthetic trial catalog and shared MSW route factories across the public static demos (`apps/web` and `apps/subject-portal`).

## Overview

`demo-fixtures` exports the single reference study (`CADENCE-101`), sponsor (`CADENCE-PHARMA`), sites (`SITE-101` and `SITE-102`), site compliance stages, screened participants (`SUBJ-101-001`, `SUBJ-101-002`, `SUBJ-102-001`), deterministic RFC 9562 UUIDv8 identifiers (`deriveDemoUuidV8`), fixed epoch (`2026-01-05T09:00:00Z`), and eleven role-named Keycloak realm personas (`crc.site101`, `pi.site101`, `cra.monitor`, `dm.lead`, `designer_demo`, `designer.lead`, `safety.officer`, `auditor.lead`, `stat.author`, `stat.approver`, `pharmacist.site101`).

## Declarative Seed Contract and Drift Verification

`demo-seed.json` (validated against `demo-seed.schema.json`) is the single declarative demo seed contract consumed across backend and frontend runtimes:

- `apps/kernel-api/src/DevelopmentDemoData.cs` and `apps/execution/src/Infrastructure/Persistence/ExecutionDbSeeder.cs` embed `packages/demo-fixtures/demo-seed.json` and deserialize it via source-generated `System.Text.Json`.
- `src/index.js` and `src/index.d.ts` synthesize `DEMO_MANIFEST` and deterministic RFC 9562 UUIDv8 identifiers directly from `demo-seed.json` at import time (with zero committed `manifest.js` or `manifest.d.ts` artifacts).
- `docker/cadence-realm.json` binds the eleven staff personas and isolates `subject.site101` (`Subject` role, zero staff kernel grants per `K027`).

Validate schema conformance, Keycloak realm parity, `K027` Subject-role isolation, absence of stale generated manifest files, and declaration types (also run by `pnpm cadence check`, `pnpm cadence fix`, and `pnpm typecheck`):

```bash
pnpm cadence check demo-manifest
pnpm cadence generate-demo-manifest
pnpm --filter demo-fixtures typecheck
```

## Production Bundle Isolation

- `demo-fixtures` (root entry `src/index.js`): synthesizes the canonical `CADENCE-101` study, sites, participants, epoch constants (`DEMO_EPOCH_ISO`, `DEMO_EPOCH_MS`), `deriveDemoUuidV8`, and synthetic personas from `demo-seed.json`. Production Vite builds (`pnpm --filter web build` and `pnpm --filter subject-portal build`) resolve bare `"demo-fixtures"` imports to an inert virtual stub via `scripts/vite-strip-demo-fixtures.mjs` (`createDemoFixturesPlugin`), keeping synthetic study payloads out of production JavaScript bundles while preserving full static demo functionality in `build:demo`, `dev`, and Vitest suites.
- `demo-fixtures/routes` (`src/routes.js` and `src/routes.d.ts`): exports shared MSW route factories (`createDemoHealthRoute`, `createDemoSignatureVerificationRoute`, `createDemoAmendmentReconsentRoute`, `createDemoParticipantExpenseOcrRoute`, and `createDemoParticipantExpenseSubmitRoute`) shared between `apps/web` and `apps/subject-portal` demo slices, and resolves to an inert virtual stub in non-demo production builds.
- `demo-fixtures/runtime` (`src/runtime.js` and `src/runtime.d.ts`): exports `isDemoBuild`, `createDemoRuntime`, and `createUnbackedActionGuard` without importing `./index.js`. App wrappers (`apps/web/src/utils/demoMode.js`, `apps/subject-portal/src/utils/demoMode.js`, and `unbackedActions.js`) pass `DEMO_EPOCH_ISO` and `DEMO_EPOCH_MS` via bare `"demo-fixtures"` imports so production bundles retain safe clock/Web Crypto fallbacks and Part 11 action guards with zero `CADENCE-101` manifest strings.
