# ADR-2553: Unify Root ESLint Gates, Explicit Runtime Globals, and Demo-Fixtures Typechecking

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001
- **Relates to:** [ADR-2508](2026-10-16-pin-pnpm-and-node-with-packagemanager-and-engines.md), [ADR-2527](2026-11-21-enforce-dependency-alignment-with-msbuild-and-pnpm-catalogs.md), [ADR-2534](2026-10-20-quality-gates-live-in-one-registry-and-each-must-be-able-to-fail.md), [ADR-2539](2026-11-22-enforce-workspace-vue-tsc-typecheck.md), [ADR-2821](2026-11-22-migrate-web-overlays-to-clinical-primitives-and-enforce-ast-guardrails.md)

---

## 1. Context & Problem Statement

Following [ADR-2539](2026-11-22-enforce-workspace-vue-tsc-typecheck.md), which enforced `vue-tsc` and `tsc` across `apps/web`, `packages/ui`, and `packages/usdm-schemas`, three static-verification gaps remained across the pnpm workspace:

1. **Duplicate and asymmetric ESLint entry points (`pnpm -r lint` vs `--pre-commit`):** `FrontendQualityGateService.CheckEslint` streamed staged JS/TS/Vue blobs through root `eslint.config.mjs` during `--pre-commit`, whereas unscoped `pnpm cadence check eslint`, `CiPlan.cs`, and `.github/workflows/ci.yml` ran `pnpm -r lint && pnpm exec eslint --max-warnings 0 .` (linting package directories twice while root `pnpm lint` skipped `.claude/skills/**/*.mjs`, `packages/usdm-schemas/`, `packages/demo-fixtures/`, `eslint.config.mjs`, and root `tests/`).
2. **Manual global allowlists and disabled `no-undef` on untyped JS/Vue scripts:** `eslint.config.mjs` maintained hand-rolled browser/Node global maps and turned `no-undef` off broadly without `tseslint.configs.recommended` or structured `globals` cohorts, masking undeclared Node globals (`process` in browser-only `apps/subject-portal/src/stores/`, bare `Buffer` in `packages/ui/callerScope.js`, and missing `node:path`/`node:process` imports in `.claude/skills/run-cadence/driver.mjs`).
3. **Missing `packages/demo-fixtures` `checkJs` typecheck and incremental `.tsbuildinfo` caches:** `packages/demo-fixtures` (`src/**/*.js` and `src/**/*.d.ts`) lacked a `tsconfig.json` and `typecheck` script in `pnpm -r typecheck`, and workspace `tsconfig.json` files did not persist incremental `.tsbuildinfo` caches under `node_modules/.cache/`.

## 2. Decision Drivers & Constraints

- **One tracked-file ESLint gate across `--pre-commit`, `pnpm lint`, `cadence check eslint`, `cadence ci`, and `ci.yml`:** Every tracked `.js`, `.jsx`, `.ts`, `.tsx`, `.mjs`, `.cjs`, `.vue`, and `.css` file outside explicit ignores in `eslint.config.mjs` must run once through the same root configuration.
- **Explicit runtime global boundaries & TypeScript ESLint rules:** Keep `no-undef` active on untyped `.js`/`.mjs`/`.cjs` and `.vue` scripts via catalog-managed `globals` cohorts (`globals.browser`, `globals.serviceworker`, `globals.node`, `globals.commonjs`, `globals.vitest`) while applying `tseslint.configs.recommended` to TypeScript files.
- **Complete workspace `typecheck` & fast incremental warm runs ([ADR-2527](2026-11-21-enforce-dependency-alignment-with-msbuild-and-pnpm-catalogs.md), [ADR-2539](2026-11-22-enforce-workspace-vue-tsc-typecheck.md)):** Include `packages/demo-fixtures` (`checkJs: true`) in `pnpm -r typecheck` (`pnpm typecheck`) and store all `.tsbuildinfo` caches under `node_modules/.cache/` so working trees stay clean.

## 3. Options Considered

### Option 1: Unify `CheckEslint` on Tracked Files via Root `eslint.config.mjs` + `globals` Cohorts + `demo-fixtures` `tsc --noEmit` (Selected)

- **Overview:** Pin `globals` (`^17.4.0`) in `pnpm-workspace.yaml` `catalog:`; adopt `tseslint.configs.recommended` and explicit `globals` runtime cohorts in `eslint.config.mjs`; update `FrontendQualityGateService.CheckEslint`, root `package.json` (`pnpm lint`), `CiPlan.cs` (`NodeCli("check", "eslint")` with `NativeAotCacheHit`), and `.github/workflows/ci.yml` (`lint-and-static`) to execute one tracked-file root ESLint pass; add `tsconfig.json` (`checkJs: true`) and `"typecheck": "tsc --noEmit"` to `packages/demo-fixtures` alongside root `"typecheck": "pnpm -r typecheck"`; and configure `incremental: true` (`./node_modules/.cache/tsconfig.tsbuildinfo`) across all four workspace `tsconfig.json` files.
- **Pros:**
  - ✅ Eliminates duplicate `pnpm -r lint` passes in CI while guaranteeing identical tracked-file coverage across `pnpm lint`, `pnpm cadence check eslint`, `--pre-commit`, `cadence ci`, and GitHub Actions.
  - ✅ Catches undeclared browser/Node globals in untyped JavaScript and Vue SFCs (`no-undef`) with zero warnings.
  - ✅ Extends `pnpm -r typecheck` (`ADR-2539`) to `packages/demo-fixtures` and cuts warm workspace typecheck runs to under 10 seconds.
- **Cons:**
  - ❌ Moves `Setup .NET SDK` and `Download Cadence CLI Artifact` ahead of `Run frontend style and lint` in `lint-and-static` so `node scripts/run-cli.js check eslint` reuses the Native AOT CLI binary.

### Option 2: Keep `pnpm -r lint` Alongside Separate Root `eslint .` Invocations

- **Overview:** Leave `pnpm -r lint` in `package.json`, `FrontendQualityGateService`, and `ci.yml` while adding per-package `lint` scripts.
- **Pros:**
  - ✅ Requires no workflow step reordering in `lint-and-static`.
- **Cons:**
  - ❌ Lints `apps/web`, `apps/subject-portal`, and `packages/ui` twice in CI and still leaves `pnpm lint` out of sync with `--pre-commit` on root `.mjs` and `.claude/skills/` scripts.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Delegating `pnpm lint`, `CiPlan.cs`, and `.github/workflows/ci.yml` to `FrontendQualityGateService.CheckEslint` over `git ls-files` removes redundant ESLint invocations, enforces explicit browser/Node/worker/Vitest global boundaries across every tracked script, and completes workspace typechecking (`packages/demo-fixtures` + incremental `.tsbuildinfo` caching) on top of [ADR-2539](2026-11-22-enforce-workspace-vue-tsc-typecheck.md).

## 5. Consequences & Trade-offs

- **Positive Impact:** Undeclared runtime globals in `.js`/`.mjs`/`.cjs`/`.vue` files, broken JSDoc/declaration contracts in `packages/demo-fixtures`, and lint issues in root tooling scripts fail deterministically in `pnpm lint`, `pnpm typecheck`, `pnpm cadence check`, and `pnpm cadence ci`.
- **Negative Impact / Technical Debt:** Legacy `any` annotations in pre-existing `.ts` clients require `@typescript-eslint/no-explicit-any` to stay off at the repository level until Python-era API clients are retired (#4833).
- **Mitigation Strategy:** `vue-tsc` (`strict: true`, ADR-2539) enforces structural types across TypeScript and Vue SFCs, while `PreCommitGuardrailsTests`, `LocalCiPlanTests`, and `ContainerImagePinTests` lock CLI/CI parity and catalog pins.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `pnpm-workspace.yaml`, `package.json`, `pnpm-lock.yaml`, `.github/dependabot.yml`, `.github/workflows/ci.yml`, `eslint.config.mjs`, `.claude/skills/run-cadence/driver.mjs`, `apps/web`, `apps/subject-portal`, `packages/ui`, `packages/usdm-schemas`, `packages/demo-fixtures`, `packages/tooling_core`, `packages/cli`, `tests/Cadence.UnitTests`, `AGENTS.md`, `docs/CI.md`, `docs/cli.md`.
- **Verification Plan:** Verified via `pnpm typecheck`, `pnpm lint`, `pnpm cadence check eslint`, `pnpm cadence check typecheck`, `pnpm cadence check api-clients`, `pnpm -r test`, `dotnet build Cadence.sln`, and `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj`.
