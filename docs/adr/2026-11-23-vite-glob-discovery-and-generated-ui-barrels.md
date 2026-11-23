# ADR-2658: Vite Glob Discovery for Frontend Registries and Generated Shared UI Barrels

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (Clinical workspace integrity, traceable URL addressability, and zero-regression GxP UI governance)

---

## 1. Context & Problem Statement

Parallel pull requests adding or migrating Clinical Canvas sections, workspace panes, `NodeRef` inspector views, staff routes, participant portal screens, or shared `packages/ui` Vue components repeatedly collided on the same central append-only files:

1. `apps/web/src/views/StudyCanvasView.vue` combined the `/studies` directory and synchronous study creation form, the 15-field `StudyQuery` URL state and zoom-path lineage logic, and an eight-way template switch across Clinical Canvas workspaces, while `apps/web/src/canvas/lifecycle.ts` (`stages/*.ts`) listed stage sections centrally and `apps/web/src/components/canvas/CanvasInspector.vue` inlined every `NodeRef` kind pane.
2. `apps/web/src/router/index.js` and `apps/web/src/router/availability.js` maintained monolithic route and availability lists, while `stores/onboarding.js`, `views/OverviewView.vue`, `components/AppShell.vue`, `canvas/palette.ts`, and `stores/knowledge.js` duplicated per-route role checks, tour targets, shell navigation items, and SOP escalation prefixes.
3. `apps/subject-portal/src/router/routes.js` and `apps/subject-portal/src/router/availability.js` split route metadata and availability sets across central files.
4. `packages/ui/index.js`, `packages/ui/src/index.js`, and a checked-in `packages/ui/index.d.ts` required manual export additions in three files (plus hardcoded count lists in `packages/ui/tests/design_system_harmony.test.js`) whenever a shared Vue primitive changed.

## 2. Decision Drivers & Constraints

- **Eliminate Parallel PR Merge Contention:** Adding a canvas section, workspace, inspector pane, route, or shared `packages/ui` component must be self-contained in colocated files without editing central registries or brittle count assertions.
- **Preserve Clinical Canvas Contracts & Cold-Boot Budgets (`PRD-SYS-001`):** Every URL query key (`StudyQuery`), DOM landmark ID, keyboard focus handoff, mutable `AVAILABLE_SCREENS` test override, simultaneous `.portal-view` DOM mounting in `apps/subject-portal`, and the `apps/web` cold-boot initial JS ceiling (`<= 515 kB` raw / `<= 167 kB` gzip, [ADR-2537](2026-11-22-split-ui-vitest-matcher-and-enforce-demo-bundle-budgets.md)) must remain intact.
- **Deterministic Ordering & Tree-Shakeable Shared UI:** Glob-discovered registries must sort deterministically by explicit numeric `order` with tie-breaking by `id`/`name`, and `packages/ui` must emit named ESM component re-exports and generated TypeScript declarations without bundling unused components or legacy HTML string builders into cold-boot chunks.

## 3. Options Considered

### Option 1: File-Colocated `import.meta.glob` Descriptors, Cold-Boot Metadata Extraction, and Build-Time `packages/ui` Barrel & Declaration Generation (Selected)

- **Overview:**
  - Extract `OpenStudyWorkspace.vue` and `useCanvasRouteState.ts` from `StudyCanvasView.vue`, and discover Clinical Canvas stages (`src/canvas/stages/*.ts`), sections (`src/canvas/sections/*.section.ts`), workspaces (`src/canvas/workspaces/*.workspace.ts`), and `NodeRef` inspectors (`src/canvas/inspectors/*.inspector.ts`) via `import.meta.glob` sorted by `(order, id)`. Populate `src/canvas/sections/meta.ts` via `createCanvasSectionMetaPlugin` so cold-boot `STAGES` and `CanvasPalette.vue` read section metadata with zero `.vue` or route-only API imports while `StudyCanvasView.vue` mounts section components synchronously.
  - Replace monolithic web and participant portal route tables with colocated `src/router/routes/*.route.js` descriptors discovered via `import.meta.glob`, deriving `AVAILABLE_SCREENS`, `DEMO_AVAILABLE_SCREENS`, role checks, tour targets, shell navigation, palette destinations, SOP escalation prefixes, and portal tabs (`PORTAL_TABS`, `AUTHENTICATED_SCREENS`) from those descriptors.
  - Move legacy HTML builders and utilities out of `packages/ui/index.js` into modular `packages/ui/src/legacy/{debounce,htmlBuilders,hoverDetection}.js` files, expand named component exports on `packages/ui/src/index.js` via `createUiAutoBarrelPlugin` alongside `UI_COMPONENTS` (`import.meta.glob`), generate gitignored `packages/ui/dist/index.d.ts` in `packages/ui/scripts/generate-types.js` during `build` and `test`, and replace fixed count lists in `design_system_harmony.test.js` and `available_screen_tests.test.js` with dynamic filesystem/descriptor verification.
- **Pros:**
  - Eliminates merge hotspots across `apps/web`, `apps/subject-portal`, and `packages/ui` without adding external dependencies.
  - Preserves synchronous `StudyCanvasView` rendering in unit and workflow tests while keeping `apps/web` cold-boot JS under budget (`<= 515 kB` raw) by isolating section `.vue` components and `packages/ui/src/legacy/htmlBuilders.js` from initial boot chunks.
  - Keeps `packages/ui` TypeScript declarations synchronized automatically with Vue component `defineProps` signatures on every `build` and `test` run.
- **Cons:**
  - Descriptor files must declare an explicit `order` when deterministic visual or match precedence matters.

### Option 2: Pre-Commit Codegen Scripts Writing Tracked Registry and Barrel Files Into Git

- **Overview:** Generate tracked `registry.generated.ts` and `packages/ui/index.d.ts` files on disk and verify them in CI.
- **Pros:**
  - No Vite transform hooks required for `packages/ui/src/index.js` or `sections/meta.ts`.
- **Cons:**
  - Tracked generated barrels and `.d.ts` files recreate the exact git merge conflicts on concurrent branches that this change sets out to eliminate.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** File-colocated `import.meta.glob` descriptors paired with build-time `packages/ui` auto-barrel expansion and gitignored `dist/index.d.ts` emission remove central merge hotspots across both frontend apps and the shared UI library while preserving clinical URL addressability, cold-boot bundle budgets, and type safety.

## 5. Consequences & Trade-offs

- **Positive Impact:** Contributors add a Clinical Canvas section, workspace, `NodeRef` inspector, staff route, participant portal screen, or shared `packages/ui` component by adding a colocated file without editing shared registries or checked-in `.d.ts` barrels; `StudyCanvasView.vue` and `CanvasInspector.vue` shrink into focused shells; and `AVAILABLE_SCREENS` remains a mutable `Set` for existing test overrides.
- **Negative Impact / Technical Debt:** `packages/ui` consumers running `tsc --noEmit` rely on `packages/ui/dist/index.d.ts` emitted by `pnpm --filter ui build`, `pnpm --filter ui test`, or `pnpm --filter ui typecheck` (`node scripts/generate-types.js`).
- **Mitigation Strategy:** `packages/ui` `build`, `test`, and `typecheck` scripts run `node scripts/generate-types.js`, `apps/web/tests/canvas/glob_registries.spec.ts` verifies deterministic ordering and route/inspector/workspace discovery, `apps/web/tests/available_screen_tests.test.js` validates every available route descriptor dynamically, and `apps/web/tests/cold_boot_bundle.spec.ts` enforces the initial JS budget and forbidden cold-boot module list.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/web`, `apps/subject-portal`, `packages/ui`, `scripts/vite-strip-demo-fixtures.mjs`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`, `docs/web/API_MIGRATION.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build && pnpm --filter ui typecheck && pnpm --filter ui test`, `pnpm --filter web typecheck && pnpm --filter web test && pnpm --filter web build && pnpm --filter web build:demo`, `pnpm --filter subject-portal test && pnpm --filter subject-portal build && pnpm --filter subject-portal build:demo`, `pnpm exec eslint --max-warnings 0 .`, and `pnpm cadence check`.
