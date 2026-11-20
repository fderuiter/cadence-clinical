# ADR-2819: Export Canonical Demo Manifest from Kernel Seed

- **Status:** Accepted
- **Date:** 2026-11-20
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Relates to:** [ADR-2329](2026-08-04-compile-time-vite-build-mode-for-interactive-public-demo.md), [ADR-2534](2026-10-20-quality-gates-live-in-one-registry-and-each-must-be-able-to-fail.md), [ADR-2543](2026-10-19-the-kernel-host-seeds-the-local-demo-study.md), [ADR-2546](2026-11-19-isolate-static-demo-builds-with-stateful-msw-handlers.md)

---

## 1. Context & Problem Statement

ADR-2543 established `CADENCE-101` (`Cadence demo study: cardiovascular outcomes`, sponsor `CADENCE-PHARMA`, sites `SITE-101` and `SITE-102`, screened participants `SUBJ-101-001`, `SUBJ-101-002`, and `SUBJ-102-001`, fixed valid-time epoch `2026-01-05T09:00:00Z`, RFC 9562 UUIDv8 identifiers, and eight fixed-`sub` accounts in `docker/cadence-realm.json`) as the local stack's shared demo study across the kernel host and Execution seeder.

However, the static public demo builds (`apps/web` and `apps/subject-portal`, ADR-2329, ADR-2546) still maintained hand-written mock identifiers (`STUDY-USDM-001`, `CAD-101`, `study_01`, `SITE-001`, `SUBJ-001`, `subject_001`), exposed maintainer personal identifiers in offline demo authentication (`apps/web/src/stores/auth.js`), hashed `new Date().toISOString()` into every Genesis SHA-256 audit ledger block, computed relative task due dates from `Date.now()`, and called unseeded `Math.random()` across simulated telemetry and workflow actions. As a result, staff and participant demo portals showed mismatched trials, leaked maintainer PII into compiled static chunks, and produced non-repeatable cryptographic ledger digests on reload (PRD-SYS-001).

## 2. Decision Drivers & Constraints

- **Single synthetic trial across local stack and static demos:** `apps/web`, `apps/subject-portal`, `DevelopmentDemoData.cs`, `ExecutionDbSeeder.cs`, and `docker/cadence-realm.json` must share one canonical study, sponsor, site list, participant cohort, and persona set without manual duplication.
- **Kernel time discipline (`K-TIME-3`):** C# tooling must never read the system wall clock; all exported timestamps derive from `DevelopmentDemoData.Epoch` (`2026-01-05T09:00:00Z`).
- **Native AOT & layer boundaries (`K-XS-1`, `K-XS-2`):** The CLI exporter in `packages/tooling_core` and `packages/cli` must compile cleanly under Native AOT without reflection serialization or references to kernel persistence/application projects.
- **Strict production isolation:** Non-demo production bundles must exclude `packages/demo-fixtures`, and authenticated sessions must keep live clocks, Web Crypto randomness (`crypto.getRandomValues`), and identity provider claims without synthetic fallbacks.
- **Automated drift prevention (ADR-2534):** `pnpm cadence check` must fail whenever backend demo seeds and committed frontend fixtures diverge.

## 3. Options Considered

### Option 1: Export a shared `packages/demo-fixtures` catalog via `cadence generate-demo-manifest` with a drift gate and Vite production stub (selected)

- **Overview:** Parse and cross-validate `DevelopmentDemoData.cs`, `ExecutionDbSeeder.cs`, and `docker/cadence-realm.json` in `DemoManifestGeneratorService`, emit deterministic `manifest.js` and `manifest.d.ts` into `packages/demo-fixtures`, enforce parity in `QualityGates` (`demo-manifest`), strip the package in non-demo Vite builds via `scripts/vite-strip-demo-fixtures.mjs`, and gate deterministic clock and Mulberry32 PRNG helpers on `isDemoBuild` / `isDemoModeActive`.
- **Pros:**
  - ✅ Guarantees byte-identical static demo fixtures, UUIDv8 derivations, and role-named realm personas from one CLI command.
  - ✅ Eliminates maintainer PII from source and compiled bundles without adding scanner path exclusions.
  - ✅ Keeps production bundles free of synthetic trial payloads while preserving offline static demo capability.
- **Cons:**
  - ❌ Updating `DevelopmentDemoData.cs`, `ExecutionDbSeeder.cs`, or `docker/cadence-realm.json` requires running `pnpm cadence generate-demo-manifest` before commit.

### Option 2: Hand-maintain synchronized constants in `apps/web` and `apps/subject-portal`

- **Overview:** Manually copy `CADENCE-101` identifiers and realm users into both frontend apps.
- **Pros:**
  - ✅ No new CLI generator or workspace package.
- **Cons:**
  - ❌ Fails to prevent future drift across the kernel seed, Execution seeder, realm JSON, and two frontend stores.

### Option 3: Fetch demo seed metadata from a backend HTTP endpoint at runtime

- **Overview:** Serve the demo manifest from `apps/kernel-api` at runtime.
- **Pros:**
  - ✅ No generated frontend file.
- **Cons:**
  - ❌ Breaks zero-backend static public demo deployments (GitHub Pages / static previews, ADR-2329) and exposes development seed metadata on runtime endpoints.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **CLI generator & quality gate:** `pnpm cadence generate-demo-manifest` (`DemoManifestGeneratorService`) cross-checks `apps/kernel-api/src/DevelopmentDemoData.cs`, `apps/execution/src/Infrastructure/Persistence/ExecutionDbSeeder.cs`, and `docker/cadence-realm.json`, derives RFC 9562 version 8 UUIDs over SHA-256 (`cadence-development-demo:{tenant}:{kind}:{name}`) and fixed `2026-01-05T09:00:00Z` timestamps, and writes byte-identical `packages/demo-fixtures/src/manifest.js` and `manifest.d.ts`. The `demo-manifest` quality gate (`--check`) blocks drift in `pnpm cadence check`.
- **Production bundle exclusion:** `scripts/vite-strip-demo-fixtures.mjs` (`createDemoFixturesPlugin`) resolves `demo-fixtures` to an inert virtual module during non-demo production Rollup builds (`pnpm --filter web build`, `pnpm --filter subject-portal build`) while resolving to `packages/demo-fixtures/src/index.js` in `build:demo`, `dev`, and Vitest runs.
- **Synthetic role-based personas:** Demo authentication in `apps/web/src/stores/auth.js` maps coordinator, investigator, monitor, data manager, designer, designer lead, safety, and auditor workspaces to the eight synthetic realm accounts (`crc.site101`, `pi.site101`, `cra.monitor`, `dm.lead`, `designer_demo`, `designer.lead`, `safety.officer`, `auditor.lead`), removing all personal maintainer attributes and the `deid-ignore` override on `auth.js`.
- **Deterministic demo clock and PRNG:** `demoNowMs`, `demoNowIso`, `createDemoPrng`, `demoRandom`, `demoRandomInt`, and `demoRandomToken` in `demoMode.js` anchor demo-only Genesis SHA-256 ledger blocks, relative task schedules, commercial trial entitlements, and simulated telemetry/IDs to `2026-01-05T09:00:00Z` and keyed Mulberry32 streams, while delegating to `Date.now()` and `crypto.getRandomValues` outside demo mode.

## 5. Consequences & Trade-offs

- **Positive Impact:** Both static demo portals and the local Aspire/Compose stack present the same `CADENCE-101` cardiovascular trial, synthetic hospital sites, screened participants, and role-named personas, with repeatable Genesis SHA-256 digests and zero maintainer PII in compiled bundles.
- **Negative Impact / Technical Debt:** Changes to `DevelopmentDemoData.cs`, `ExecutionDbSeeder.cs`, or `docker/cadence-realm.json` fail `pnpm cadence check demo-manifest` until `pnpm cadence generate-demo-manifest` is run.
- **Mitigation Strategy:** The error message from `demo-manifest` names the exact regeneration command (`pnpm cadence generate-demo-manifest`).

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/tooling_core`, `packages/cli`, `packages/demo-fixtures`, `scripts/vite-strip-demo-fixtures.mjs`, `apps/web`, `apps/subject-portal`, `docs/cli.md`, `docs/CI.md`.
- **Verification Plan:**
  - `DemoManifestGeneratorTests` and `QualityGateRegistryTests` verify RFC 9562 UUIDv8 derivation, byte-identical output, eight-persona parity, and drift failure in `Cadence.UnitTests`.
  - `apps/web/tests/demo_manifest_determinism.spec.ts` and `apps/subject-portal/tests/demo_manifest_determinism.test.js` verify identical Genesis SHA-256 hashes, fixed task schedules, repeatable PRNG sequences, synthetic persona switching, and live clock / Web Crypto preservation outside demo mode.
