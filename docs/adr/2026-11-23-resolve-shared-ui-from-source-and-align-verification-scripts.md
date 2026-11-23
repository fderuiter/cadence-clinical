# ADR-2656: Resolve Shared UI from Workspace Source and Align Verification Scripts

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Supersedes:** [ADR-2323](2026-08-03-migrate-clinical-forms-to-pre-compiled-shared-ui-library.md)
- **Amends:** [ADR-2545](2026-11-19-centralize-focus-containment-primitives.md)
- **Requirements:** PRD-CRF-015 (WCAG 2.2 AA shared clinical form, modal, drawer, and accessibility primitive verification), PRD-SYS-001 (deterministic local verification and GxP quality gate parity)

---

## 1. Context & Problem Statement

Under [ADR-2323](2026-08-03-migrate-clinical-forms-to-pre-compiled-shared-ui-library.md) and [ADR-2545](2026-11-19-centralize-focus-containment-primitives.md), `apps/web/vite.config.js` aliased `"ui"` to the gitignored library output `packages/ui/dist/index.js`, and `apps/web/src/style.css` imported `packages/ui/dist/index.css`. Meanwhile, `apps/subject-portal/vite.config.js` already resolved `"ui"` directly from workspace source (`packages/ui/index.js`) and imported only `packages/ui/tokens.css` and `packages/ui/responsive.css`.

Because `scripts/setup_wizard.sh`, `pnpm install --frozen-lockfile`, and the .NET Aspire AppHost (`apps/apphost`) do not pre-build `packages/ui/dist/`, a clean checkout failed all Vitest test files in `apps/web`, failed `pnpm --filter web dev`, `pnpm --filter web build`, and `pnpm --filter web build:demo`, and required `pnpm --filter ui build` workarounds in root `package.json` and `scripts/local_stack_nodocker.sh`. In addition, local `Makefile` entrypoints had drifted: `make setup` skipped `.cadence/venv` (`detect-secrets`) and `.cadence/bin` (`actionlint`, `hadolint`, `shellcheck`) provisioning performed by `scripts/setup_wizard.sh --dev --minimal`, and `make verify` omitted `pnpm test` (`pnpm -r test && pnpm exec vitest run --config vitest.config.mjs`).

## 2. Decision Drivers & Constraints

- **Zero-Build Fresh-Clone Readiness (`PRD-CRF-015`, `PRD-SYS-001`):** Contributors and automated agents cloning the repository and running `make setup` or `scripts/setup_wizard.sh --dev --minimal` must be able to run `pnpm test`, `pnpm -r test`, `pnpm --filter web dev`, `pnpm --filter web build`, `pnpm --filter web build:demo`, and `pnpm --filter web typecheck` immediately without `packages/ui/dist/`.
- **Live HMR & Scoped CSS Parity Across Portals (`PRD-CRF-015`):** Editing shared Vue 3 components or accessibility composables in `packages/ui` must hot-reload in `apps/web` and compile all `<style scoped>` rules directly through `@vitejs/plugin-vue`, matching `apps/subject-portal` without duplicating or dropping WCAG 2.2 AA focus, modal, drawer, toast, or badge styles.
- **Preservation of CI Library Build & Bundle Budgets:** `packages/ui/package.json` must retain `"build": "node scripts/generate-types.js && vite build"` (`pnpm --filter ui build`) so `packages/cli/src/Services/CiPlan.cs` and `.github/workflows/ci.yml` continue generating `index.d.ts` and validating the standalone library build, while `apps/web` cold-boot and staged demo bundle budgets ([ADR-2537](2026-11-22-split-ui-vitest-matcher-and-enforce-demo-bundle-budgets.md)) and Vitest coverage ratchet thresholds ([ADR-2530](2026-09-30-measure-web-coverage-over-all-source-with-a-ratchet-floor.md)) remain strictly enforced.
- **No External Task Runners or Dependency Drift:** Align verification and setup using existing `pnpm`, `Makefile`, and `setup_wizard.sh` scripts without introducing Turborepo or Nx or mutating root `package.json` dependency sections.

## 3. Options Considered

### Option 1: Resolve `packages/ui` from Source in `apps/web` and Align `Makefile` & Workspace Verification Scripts (Selected)

- **Overview:**
  - Update `apps/web/vite.config.js` to call `createUiWorkspaceAliases(__dirname)` (defaulting `ui` to `packages/ui/index.js`, alongside `ui/testing` and `ui/vitest`, identical to `apps/subject-portal/vite.config.js`), add `"../../packages/ui/index.js"` to `optimizeDeps.entries`, add `"ui > axe-core"` to `optimizeDeps.include`, and consolidate boot-resident `packages/ui` shell/overlay primitives (`session.js`, `useFocusTrap`, `useEscapeClose`, `useSignOutGuard`, `sharedProps`, `ClinicalModal`, `ClinicalDrawer`, `ClinicalContextBar`, `ClinicalIconButton`, `ClinicalToastHost`, `SignOutConfirmModal`) into a single `ui-shell` chunk while leaving route-only `packages/ui` components code-split.
  - Remove `@import "../../../packages/ui/dist/index.css";` from `apps/web/src/style.css` so Vite compiles component `<style scoped>` blocks directly from `packages/ui/src/components/**/*.vue` while retaining `tokens.css` and `responsive.css`.
  - Update root `package.json` `"test"` (`pnpm -r test && pnpm exec vitest run --config vitest.config.mjs`) and `"verify"` (`node scripts/run-cli.js check --exclude public-bundle && pnpm test`), and remove the `packages/ui/dist/index.js` pre-build workaround from `scripts/local_stack_nodocker.sh`.
  - Delegate `make setup` in `Makefile` to `bash scripts/setup_wizard.sh --dev --minimal` so `.cadence/venv`, `.cadence/bin`, NuGet, `pnpm`, `.env`, and `.githooks` are provisioned together, and add `pnpm test` to `make verify` alongside `pnpm cadence check` and `pnpm cadence test`.
  - Preserve `pnpm --filter ui build` in `packages/ui/package.json` for `index.d.ts` declaration generation and CI library build validation.
- _**Pros:**_
  - Eliminates missing `packages/ui/dist/` failures across Vitest, Vite dev, Aspire AppHost, and production/demo builds on a clean checkout.
  - Enables instant HMR for shared `packages/ui` Vue components and composables in both `apps/web` and `apps/subject-portal`, and lets Rollup tree-shake and code-split `packages/ui` modules per route rather than importing one monolithic pre-bundled `dist/index.js`.
  - Aligns `make setup`, `make verify`, `pnpm verify`, and `pnpm test` without adding external build orchestrators.
- _**Cons:**_
  - Both consumer apps compile `packages/ui` Vue SFCs via `@vitejs/plugin-vue` during their own Vite builds, mitigated by Vite's shared configuration helpers (`scripts/vite-strip-demo-fixtures.mjs`) and fast Rolldown/Vite compilation.

### Option 2: Run `pnpm --filter ui build` Inside `postinstall`, `setup_wizard.sh`, and Every App `pretest`/`predev` Script

- **Overview:** Keep `apps/web` aliased to `packages/ui/dist/index.js` and `packages/ui/dist/index.css`, and add `pnpm --filter ui build` hooks to `setup_wizard.sh`, `apps/apphost`, and `apps/web/package.json`.
- _**Pros:**_
  - Preserves ADR-2323's pre-compiled consumption path in `apps/web`.
- _**Cons:**_
  - Leaves `apps/web` inconsistent with `apps/subject-portal`, breaks live HMR when editing `packages/ui` during `pnpm --filter web dev` unless a separate watcher runs, slows down every single-file Vitest run, and still risks stale `packages/ui/dist/` artifacts after `git pull` or branch switches.

### Option 3: Adopt an External Monorepo Task Graph Runner (Turborepo or Nx)

- **Overview:** Add Turborepo or Nx to orchestrate `ui#build` before `web#dev`, `web#test`, and `web#build`.
- _**Pros:**_
  - Caches task outputs across packages.
- _**Cons:**_
  - Adds unnecessary third-party tooling and root dependency churn to a workspace with only two frontend apps, still fails to give live source HMR without watch daemons, and does not simplify Aspire AppHost startup.

## 4. Decision Outcome

- **Chosen Option:** Option 1 (Resolve `packages/ui` from Source in `apps/web` and Align `Makefile` & Workspace Verification Scripts).
- **Justification:** Resolving `packages/ui` from workspace source in `apps/web` matches the proven `apps/subject-portal` setup, guarantees that WCAG 2.2 AA primitives (`PRD-CRF-015`) work immediately on fresh clones and hot-reload during development, and pairs with `make setup` / `make verify` / `pnpm test` alignment (`PRD-SYS-001`) so contributors have complete local verification.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Fresh clones pass `pnpm --filter web test`, `pnpm --filter web build`, `pnpm --filter web build:demo`, `pnpm --filter web typecheck`, `pnpm test`, and `pnpm -r test` without `packages/ui/dist/`.
  - `make setup` provisions `.cadence/venv`, `.cadence/bin`, NuGet packages, `pnpm` modules, and `.githooks` non-interactively via `scripts/setup_wizard.sh --dev --minimal`.
  - `make verify` executes quality gates (`pnpm cadence check`), all workspace and VitePress Vitest suites (`pnpm test`), and .NET test suites (`pnpm cadence test`).
- **Negative Impact & Mitigations:**
  - Changes to exported signatures in `packages/ui` still require running `pnpm --filter ui build` (or `node packages/ui/scripts/generate-types.js`) when updating the tracked `packages/ui/index.d.ts` declarations; CI (`CiPlan.cs` and `.github/workflows/ci.yml`) continues running `pnpm --filter ui build` to enforce declaration and library build health.

## 6. Implementation & Verification

- **Affected Files:**
  - `apps/web/vite.config.js`, `apps/web/src/style.css`, `apps/web/README.md`
  - `apps/subject-portal/README.md`, `packages/ui/README.md`, `README.md`
  - `Makefile`, `package.json`, `scripts/local_stack_nodocker.sh`, `.claude/skills/run-cadence/SKILL.md`
  - `CONTRIBUTING.md`, `docs/LOCAL_DEV_ENVIRONMENT.md`, `docs/TESTING.md`
  - `docs/adr/2026-08-03-migrate-clinical-forms-to-pre-compiled-shared-ui-library.md`, `docs/adr/2026-11-19-centralize-focus-containment-primitives.md`, `docs/adr/index.md`, `docs/DOCUMENTATION_INDEX.md`
- **Verification:**
  - Remove `packages/ui/dist/` and verify `pnpm --filter web test`, `pnpm --filter web build`, `pnpm --filter web build:demo`, `pnpm --filter web typecheck`, and root `pnpm test` all exit 0 with scoped `packages/ui` CSS rules compiled into `apps/web/dist/assets/*.css` and cold-boot / demo bundle budgets preserved.
  - Verify `make setup` provisions `.cadence/venv`, `.cadence/bin`, and `.githooks`, and verify `pnpm cadence check adrs`, `pnpm cadence check doc-index`, `pnpm cadence check markdown`, and quality/test suites pass cleanly.
