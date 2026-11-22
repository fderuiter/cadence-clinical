# ADR-2536: Enforce Strict Workspace TypeScript and Vue SFC Typechecking

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001, PRD-EDC-005

---

## 1. Context & Problem Statement

Plain `tsc --noEmit` in `apps/web` only compiled non-Vue `.ts` modules under `apps/web/src`, skipping every `<script lang="ts">` Vue Single-File Component (`56` SFCs in `apps/web` and `2` in `packages/ui`), all Vitest and Playwright `.ts` suites, `packages/ui/index.d.ts`, and `packages/usdm-schemas` (`136/193` TypeScript units skipped — `70.47%`). Furthermore, neither `.github/workflows/ci.yml` nor `packages/cli/src/Services/CiPlan.cs` invoked `pnpm -r typecheck`, and `packages/cli/src/Services/QualityGates.cs` lacked a workspace `typecheck` gate. Because Vue templates and `<script setup lang="ts">` blocks were never typechecked against generated OpenAPI schemas, latent runtime field mismatches reached clinical views (`StudyCanvasView.vue`, `ObservationView.vue`, `LocksSection.vue`, `ScopeLockControls.vue`, `CtmsGreenlightSection.vue`, `CtmsDelegationSection.vue`, `CtmsMonitoringSection.vue`, `AggregateSafetyConsole.vue`, `EhrReconciliationDrawer.vue`, `SensorTelemetryConsole.vue`, and `StatisticalComputingConsole.vue`).

## 2. Decision Drivers & Constraints

- **100% TypeScript & Vue SFC Coverage (`strict: true`):** Every `.ts` module, `.d.ts` declaration, Vitest/Playwright `.ts` file, and `<script lang="ts">` `.vue` component across `apps/web`, `packages/ui`, and `packages/usdm-schemas` (`193/193` units) must compile cleanly under `strict: true` with zero `@ts-ignore` or `@ts-nocheck` suppressions.
- **OpenAPI Contract Fidelity (PRD-SYS-001, PRD-EDC-005):** Clinical UI components and Part 11 electronic signature surfaces must bind directly to verified OpenAPI response and request fields rather than untyped or stale legacy properties.
- **Fail-Closed Pre-Commit, CLI & CI Parity ([ADR-2534](2026-10-20-quality-gates-live-in-one-registry-and-each-must-be-able-to-fail.md)):** `pnpm -r typecheck` must run in `lint-and-static` (`.github/workflows/ci.yml` and `CiPlan.cs`), `ApiClientValidatorService`, `Makefile`, and as a fail-closed `typecheck` gate in `QualityGates.cs` (`FrontendQualityGateService.CheckTypecheck`).
- **Centralized Dependency Pinning ([ADR-2527](2026-11-21-enforce-dependency-alignment-with-msbuild-and-pnpm-catalogs.md)):** Pin `vue-tsc` once in `pnpm-workspace.yaml` `catalog:` and reference `"catalog:"` from workspace `package.json` manifests.

## 3. Options Considered

### Option 1: Workspace-Wide `vue-tsc --noEmit` + `tsc --noEmit` Gate with Strict `tsconfig.json` Alignment (Selected)

- **Overview:** Pin `vue-tsc` in the `pnpm-workspace.yaml` catalog, add strict `tsconfig.json` configurations and `typecheck` scripts across `apps/web` (`vue-tsc --noEmit` covering `src`, `tests`, and `playwright*.config.ts`), `packages/ui` (`vue-tsc --noEmit` validating `index.d.ts`, SFCs, and tests), and `packages/usdm-schemas` (`tsc --noEmit`), resolve all latent component and test type defects, register `typecheck` in `QualityGates.cs`, and wire `pnpm -r typecheck` into `ci.yml`, `CiPlan.cs`, `ApiClientValidatorService.cs`, and `Makefile`.
- **Pros:**
  - ✅ Compiles `193/193` (`100%`) `.ts` and `<script lang="ts">` `.vue` units under `strict: true` with zero `@ts-ignore` / `@ts-nocheck` directives.
  - ✅ Catches template/script OpenAPI drift (such as protocol version labels, observation lineage `ref` steps, Part 11 signer full names, and CTMS greenlight/delegation/monitoring contracts) at compile time.
  - ✅ Preserves exact parity between `.github/workflows/ci.yml`, `cadence ci`, `cadence check`, and `.githooks/pre-commit`.
- **Cons:**
  - ❌ Adds ~10–15 seconds to `lint-and-static` and staged frontend pre-commit checks when JS/TS/Vue/JSON/YAML files change.

### Option 2: Keep Plain `tsc --noEmit` in `apps/web` Only

- **Overview:** Add `pnpm --filter web typecheck` to CI without `vue-tsc` or workspace `tsconfig.json` files in `packages/ui` and `packages/usdm-schemas`.
- **Pros:**
  - ✅ Smaller dependency footprint.
- **Cons:**
  - ❌ Leaves all `58` `<script lang="ts">` `.vue` components, `packages/ui`, and `packages/usdm-schemas` untypechecked and misses runtime clinical UI bugs.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Enforcing strict `vue-tsc` and `tsc` across every TypeScript-enabled workspace package closes the 70.47% coverage gap, eliminates real runtime defects in clinical staff workflows, and guarantees fail-closed parity across pre-commit hooks, `pnpm cadence check`, `pnpm cadence ci`, and GitHub Actions.

## 5. Consequences & Trade-offs

- **Positive Impact:** Any future mismatch between backend OpenAPI contracts, shared `ui` components/declarations, `usdm-schemas` validators, Vitest suites, and Vue SFC templates fails immediately during local pre-commit and CI checks.
- **Negative Impact / Technical Debt:** Legacy JavaScript-only packages (`apps/subject-portal`) remain governed by ESLint and Vitest until migrated to TypeScript in a separate change.
- **Mitigation Strategy:** `FrontendQualityGateService.CheckTypecheck` skips `pnpm -r typecheck` during pre-commit when no frontend/config files are staged, keeping C#-only and documentation-only commits fast while failing closed whenever `pnpm` or `node_modules` is missing.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `pnpm-workspace.yaml`, `apps/web`, `packages/ui`, `packages/usdm-schemas`, `packages/tooling_core`, `packages/cli`, `.github/workflows/ci.yml`, `Makefile`, `tests/Cadence.UnitTests`, `AGENTS.md`, `docs/TESTING.md`, `docs/CI.md`, `docs/cli.md`.
- **Verification Plan:** Verified via `pnpm -r typecheck`, `pnpm -r lint`, `pnpm -r test`, `pnpm build`, `pnpm cadence check typecheck`, `pnpm cadence check cli-docs`, `pnpm cadence check adrs`, `pnpm cadence check api-clients`, and `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj`.
