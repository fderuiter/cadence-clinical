# ADR-2538: Unify Workspace Vue Typechecking and Root ESLint Gates

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001
- **Relates to:** [ADR-2508](2026-10-16-pin-pnpm-and-node-with-packagemanager-and-engines.md), [ADR-2525](2026-10-20-dependabot-bumps-merge-as-one-locally-validated-batch.md), [ADR-2527](2026-11-21-enforce-dependency-alignment-with-msbuild-and-pnpm-catalogs.md), [ADR-2534](2026-10-20-quality-gates-live-in-one-registry-and-each-must-be-able-to-fail.md), [ADR-2821](2026-11-22-migrate-web-overlays-to-clinical-primitives-and-enforce-ast-guardrails.md)

---

## 1. Context & Problem Statement

Frontend static verification across the pnpm workspace (`apps/web`, `apps/subject-portal`, `packages/ui`, `packages/usdm-schemas`, `packages/demo-fixtures`, and repository-root `.mjs` tooling) had three blind spots that allowed broken component contracts and undeclared runtime globals to bypass `pnpm cadence check`, `pnpm cadence ci`, and GitHub Actions while Actions runners remain paused until 2027 (ADR-2525):

1. **Zero Vue single-file component or shared-package TypeScript compilation:** `apps/web/package.json` and `ApiClientValidatorService.ValidateApiClients` (`pnpm cadence check api-clients`) invoked plain `tsc --noEmit` instead of `vue-tsc`, and `apps/web/tsconfig.json` excluded `tests/**` while `packages/ui`, `packages/usdm-schemas`, and `packages/demo-fixtures` had no `typecheck` scripts. Because Vite compiles `<script lang="ts">` blocks with `esbuild` by stripping types without typechecking, 152 TypeScript errors accumulated across `.vue` templates and scripts, `packages/ui/index.d.ts` slot/prop signatures (`ClinicalModal`, `ClinicalDrawer`, `ClinicalFieldLayout`, `EmptyState`, `ClinicalLookupInput`), and `apps/web/tests/**/*.ts` specs without failing any build or quality gate.
2. **Asymmetric ESLint file coverage between `--pre-commit` and unscoped/CI runs:** `FrontendQualityGateService.CheckEslint` streamed any staged `.js/.jsx/.ts/.tsx/.mjs/.cjs/.vue` path through root `eslint.config.mjs` during `--pre-commit`, whereas unscoped `pnpm cadence check eslint`, `CiPlan`, and `.github/workflows/ci.yml` (`lint-and-static`) fell back to `pnpm -r lint`. Because `packages/usdm-schemas`, `packages/demo-fixtures`, `.claude/skills/run-cadence/driver.mjs`, `eslint.config.mjs`, and root `tests/` were outside `pnpm -r lint`, lint errors in those files passed `pnpm cadence check` and CI undetected.
3. **Manual global allowlists and missing TypeScript ESLint presets:** `eslint.config.mjs` maintained hand-rolled browser/Node global lists and turned `no-undef` off broadly without `tseslint.configs.recommended` or structured `globals` cohorts, masking undeclared Node globals (`process` in `apps/subject-portal/src/stores/`) and broken references (`path.resolve` without importing `path` in `.claude/skills/run-cadence/driver.mjs`).

## 2. Decision Drivers & Constraints

- **Compile every `.vue` template, script, shared package, and TypeScript test:** `pnpm typecheck` and `pnpm cadence check api-clients` must fail closed on invalid Vue template props, slot bindings, `<script lang="ts">` errors, `packages/ui/index.d.ts` signature mismatches, and `apps/web/tests/**/*.ts` type errors without adding `@ts-ignore`, `@ts-expect-error`, or `any` casts.
- **Identical file scope across `--pre-commit`, `pnpm lint`, `cadence check eslint`, `cadence ci`, and `ci.yml`:** Every tracked `.js`, `.jsx`, `.ts`, `.tsx`, `.mjs`, `.cjs`, and `.vue` file outside explicit ignores in `eslint.config.mjs` must be linted by the same root configuration in every mode.
- **Single catalog source of truth & clean working tree (ADR-2527, ADR-2534):** Shared packages (`vue-tsc`, `@types/node`, `globals`) must live in `pnpm-workspace.yaml` `catalog:` and `.github/dependabot.yml` cohorts, incremental `.tsbuildinfo` caches must stay inside `node_modules/.cache/`, and `Cadence.Cli` / `Cadence.ToolingCore` must remain 100% Native AOT clean.

## 3. Options Considered

### Option 1: Catalog-managed `vue-tsc --build` solution configs + workspace `typecheck` scripts + unified root `CheckEslint` and `globals` cohorts (selected)

- **Overview:** Add `vue-tsc`, `@types/node`, and `globals` to `pnpm-workspace.yaml` `catalog:`; split `apps/web` into composite-style `tsconfig.json` referencing `tsconfig.app.json` and `tsconfig.test.json` (`vue-tsc --build`) and add `tsconfig.json` + `typecheck` scripts to `packages/ui`, `packages/usdm-schemas`, and `packages/demo-fixtures` with `tsBuildInfoFile` under `node_modules/.cache/`; fix all 152 existing Vue/UI/test type errors; switch `ApiClientValidatorService` to `pnpm run typecheck`; unify `FrontendQualityGateService.CheckEslint`, root `pnpm lint`, `CiPlan`, and `ci.yml` on root `eslint` over tracked files; and adopt `tseslint.configs.recommended` + `globals` cohorts in `eslint.config.mjs`.
- **Pros:**
  - ✅ Catches `.vue` template/script, shared `ui` declaration, `usdm-schemas`, `demo-fixtures`, and Vitest `.spec.ts` type errors deterministically in both `pnpm typecheck` and `pnpm cadence check api-clients`.
  - ✅ Eliminates the `--pre-commit` vs `pnpm -r lint` blind spot across root `.mjs`, `.claude/skills/`, and workspace packages.
  - ✅ Keeps `no-undef` active on untyped `.js`/`.mjs`/`.cjs` and `.vue` files with explicit browser, service-worker, Node CLI/config, and test-runner global boundaries.
- **Cons:**
  - ❌ `lint-and-static` in `.github/workflows/ci.yml` downloads the `cadence-cli` artifact two steps earlier so `node scripts/run-cli.js check eslint` runs in the `Run frontend style and lint` step.

### Option 2: Add per-package `lint` scripts to `packages/usdm-schemas` and `packages/demo-fixtures` while keeping `pnpm -r lint`

- **Overview:** Keep `pnpm -r lint` in `FrontendQualityGateService.CheckEslint` and add `lint` scripts to remaining workspace packages.
- **Pros:**
  - ✅ Covers `packages/usdm-schemas` and `packages/demo-fixtures`.
- **Cons:**
  - ❌ Still misses repository-root `.mjs`/`.cjs` files, `.claude/skills/**/*.mjs`, and root `tests/**`, leaving `--pre-commit` and `cadence check eslint` asymmetric.

### Option 3: Enable `allowJs: true` with `checkJs: true` across every legacy `.js` file in `apps/web`

- **Overview:** Force full TypeScript checking onto every untouched Python-era `.js` view and store in one pass.
- **Pros:**
  - ✅ Maximizes type coverage inside legacy JavaScript files.
- **Cons:**
  - ❌ Conflicts with the planned retirement/rewrite of unbacked Python-era screens (#4529, #4833), whereas `allowJs: true` (`checkJs: false` by default) resolves mixed `.js`/`.ts`/`.vue` imports cleanly while strictly typechecking all `.ts` and `.vue` bindings.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **Workspace TypeScript & Vue compilation (`pnpm typecheck`, `ApiClientValidatorService`):**
  - Pin `vue-tsc` (`^3.3.14`), `@types/node` (`^26.6.6`), and `globals` (`^17.4.0`) in `pnpm-workspace.yaml` `catalog:` and `.github/dependabot.yml` (`vue-vite` group).
  - Configure `apps/web/tsconfig.json` as a solution config referencing `apps/web/tsconfig.app.json` (`src/**/*.ts`, `src/**/*.d.ts`, `src/**/*.vue`) and `apps/web/tsconfig.test.json` (`src/**/*.d.ts`, `tests/**/*.ts`, `tests/**/*.d.ts`, `playwright*.ts`), compiled via `vue-tsc --build`, alongside `vue-tsc --noEmit` in `packages/ui` and `tsc --noEmit` in `packages/usdm-schemas` and `packages/demo-fixtures` (all writing `.tsbuildinfo` to `node_modules/.cache/`).
  - Model typed slots (`UiComponentWithSlots`) and emits on `packages/ui/index.d.ts` primitives (`ClinicalModal`, `ClinicalDrawer`, `ClinicalFieldLayout`, `EmptyState`) and resolve all existing Vue SFC, API client, and test TypeScript mismatches without `@ts-ignore`, `@ts-expect-error`, or `any` casts.
  - Update `ApiClientValidatorService.ValidateApiClients` (`pnpm cadence check api-clients`) to run `pnpm --filter web run generate:api -- --check` followed by workspace `pnpm run typecheck`.
- **Unified root ESLint gate (`eslint.config.mjs`, `FrontendQualityGateService`, `CiPlan`, `ci.yml`):**
  - Unify `FrontendQualityGateService.CheckEslint` so unscoped runs (`!preCommit && targetFiles is null`) enumerate tracked `.js/.jsx/.ts/.tsx/.mjs/.cjs/.vue` files (`git ls-files -z`), run `scripts/check_duplicate_identifiers.js` when `apps/web/src/stores/clinical.ts` is present, and execute root `pnpm exec eslint --no-warn-ignored -- <batch>`.
  - Update root `package.json` (`"lint": "node scripts/run-cli.js validate-path-patterns && node scripts/run-cli.js check eslint"`), `CiPlan.cs`, and `.github/workflows/ci.yml` (`lint-and-static`) to invoke `node scripts/run-cli.js check eslint` after downloading the `cadence-cli` artifact.
  - Adopt `tseslint.configs.recommended` for `.ts/.tsx/.mts/.cts` files and `globals` (`globals.browser`, `globals.serviceworker`, `globals.node`, `globals.commonjs`, `globals.vitest`) scoped by runtime cohort in `eslint.config.mjs`, keeping `no-undef` active on `.js/.mjs/.cjs` and `.vue` files.

## 5. Consequences & Trade-offs

- **Positive Impact:** Template prop/slot mismatches in `.vue` components, stale `packages/ui/index.d.ts` declarations, broken `.spec.ts` assertions, and undeclared globals in `.mjs`/`.js` scripts fail deterministically in `pnpm typecheck`, `pnpm lint`, `pnpm cadence check`, and `pnpm cadence ci`. Incremental `.tsbuildinfo` caches keep warm workspace typechecks fast without leaving untracked artifacts in the working tree.
- **Negative Impact / Technical Debt:** Legacy `any` usages in pre-existing `.ts` files and generated `packages/ui/src/types/cdash.ts` declarations require `@typescript-eslint/no-explicit-any` and `@typescript-eslint/no-empty-object-type` to remain disabled at the repo-wide rule level until Python-era API clients are retired under #4833.
- **Mitigation Strategy:** `vue-tsc` enforces strict structural typing on newly typed Vue components and `packages/ui` slot contracts, while `PreCommitGuardrailsTests`, `LocalCiPlanTests`, and `ContainerImagePinTests` lock CLI gate behavior, CI workflow parity, and catalog alignment.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `pnpm-workspace.yaml`, `package.json`, `pnpm-lock.yaml`, `.github/dependabot.yml`, `.github/workflows/ci.yml`, `eslint.config.mjs`, `.claude/skills/run-cadence/driver.mjs`, `apps/web`, `apps/subject-portal`, `packages/ui`, `packages/usdm-schemas`, `packages/demo-fixtures`, `packages/tooling_core`, `packages/cli`, `tests/Cadence.UnitTests`, `AGENTS.md`, `docs/CI.md`.
- **Verification Plan:**
  - `pnpm typecheck`, `pnpm lint`, `pnpm build`, and `pnpm -r test` pass with zero errors and keep `.tsbuildinfo` files exclusively under `node_modules/.cache/`.
  - `pnpm cadence check eslint` and `pnpm cadence check api-clients` pass on `HEAD` and fail closed on synthetic `.vue` template/script/test type errors and undeclared `.mjs`/`.js` globals.
  - `dotnet build Cadence.sln` and `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj` (`ContainerImagePinTests`, `LocalCiPlanTests`, `QualityGateRegistryTests`, `PreCommitGuardrailsTests`) pass with zero warnings or errors.
