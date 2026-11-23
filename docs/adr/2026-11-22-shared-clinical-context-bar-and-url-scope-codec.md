# ADR-2651: Shared ClinicalContextBar and Canonical URL Scope Codec

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (21 CFR Part 11 attributable clinical context across workflow transitions), PRD-CRF-015 (WCAG 2.2 Level AA navigation landmarks and minimum 44px touch targets)

---

## 1. Context & Problem Statement

Clinicians, site coordinators, and monitors regularly move between the study-centric Clinical Canvas (`/studies/:studyCode`) and standalone operational workflows such as Data collection (`/ecrf`), Site readiness (`/ctms`, `/ssu`), Training (`/training`), Document vault (`/etmf`), Support (`/tickets`), Knowledge (`/knowledge`), and Observation inspection (`/observations/:observationRef`). Previously, zoom-path breadcrumbs (`#canvas-zoom-path`) existed only inside `StudyCanvasView.vue`, standalone workflow routes had no persistent study/site/participant context bar, stage and section transitions inside `StudyCanvasView.vue` dropped active `site` and `participant` (`node`) filters, `clinicalScope` initialized non-demo sessions with a synthetic `"SUBJ-001"` fallback, `readInstantiatedClinicalScope()` looked up a non-existent Pinia store key instead of `"clinicalScope"`, and standalone filter edits did not consistently reflect active scope back into URL queries via `router.replace`.

## 2. Decision Drivers & Constraints

- **URL-First Attributable Clinical Scope (PRD-SYS-001):** Every cross-route handoff and in-place scope change must preserve canonical study, site, and participant identifiers in the URL without fabricating participant IDs outside demo builds or leaking protocol codes, CDISC terms, or internal `NodeRef` strings into `apps/subject-portal`.
- **Single Accessible Landmark Pattern (PRD-CRF-015):** Breadcrumb navigation must expose a semantic `<nav>` landmark with explicit accessible naming, inline 16x16 Lucide SVG chevrons (`stroke-width="1.75"`), `JetBrains Mono` clinical identifier pills, and `>= 44px` touch hit areas (`min-height: 44px; min-width: 44px`) while keeping top chrome compact on tablet viewports (`768px` to `1280px`).
- **Draft-Guard Preservation:** Synchronizing standalone study, site, and participant query parameters via `router.replace` must not trigger false unsaved-draft leave prompts in `EcrfView.vue` or `StudyCanvasView.vue`.

## 3. Options Considered

### Option 1: Export Shared `ClinicalContextBar` from `ui` Paired with URL-First Scope Resolution and `useRouteScopeSync` (Selected)

- **Overview:** Export `ClinicalContextBar` from `packages/ui/src/index.js` (`ui`) rendering an accessible `<nav>` landmark over `Studies -> Study -> Site -> Participant` links; mount it inside `AppShell.vue` on authenticated standalone workflow routes while keeping `#canvas-zoom-path` authoritative on `StudyCanvasView.vue`; preserve `site` and `node` across `StudyCanvasView` stage, section, and Study home links while retaining `#zoom-studies`, `#zoom-study`, and `#zoom-site` zoom-up resets; pass `:site-id` from `StudyCanvasView` into `StudyTrainingView` and `ProtocolTrainingConsole`; remove the non-demo `"SUBJ-001"` fallback in `clinicalScope.ts`; fix `readInstantiatedClinicalScope()` and same-study fallback in `palette.ts`; serialize study, site, and participant parameters across `AppShell` and command-palette links; align `demoStore.getZoomPath` study labels with `KernelNavigator.cs`; include canonical `studyCode`, `site`, `participant`, and `observation` parameters on `/observations/:observationRef` redirects; and synchronize standalone scope edits via `router.replace` through `useRouteScopeSync.ts`.
- **Pros:**
  - Single reusable `ui` navigation primitive with SSR, `RouterLink`, WCAG 2.2 AA contrast, and 44px touch-target guarantees.
  - Preserves deep-linked clinical scope across all workflow handoffs without duplicating `#canvas-zoom-path` on Clinical Canvas routes or triggering false draft-leave dialogs.
- **Cons:**
  - Requires keeping Clinical Canvas query keys (`site`, `participant`, `node`) and standalone workflow query keys (`studyId`, `siteId`, `subjectId`) mapped cleanly in `palette.ts` and `useRouteScopeSync.ts`.

### Option 2: Mount `ClinicalContextBar` Unconditionally Above Every Route Including `StudyCanvasView`

- **Overview:** Render `ClinicalContextBar` inside `AppShell.vue` on all routes and retire `#canvas-zoom-path` inside `StudyCanvasView.vue`.
- **Pros:**
  - Uses one template mount point for every authenticated route.
- **Cons:**
  - Either duplicates breadcrumb landmarks on `/studies/:studyCode` or loses Clinical Canvas kernel-computed zoom lineage (`getZoomPath` masking and observation/collection lineage steps defined by [ADR-2481](2026-09-30-public-noderef-and-kernel-computed-zoom-path.md)).

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Mounting `ClinicalContextBar` on authenticated standalone workflow routes while keeping `#canvas-zoom-path` on `StudyCanvasView.vue` gives clinicians continuous study, site, and participant orientation and one-click zoom-up navigation across the entire application without duplicating landmarks or sacrificing kernel-computed lineage masking on the Clinical Canvas.

## 5. Consequences & Trade-offs

- **Positive Impact:** Clinicians retain study, site, and participant context when moving between `/studies/:studyCode`, `/ecrf`, `/ctms`, `/ssu`, `/training`, `/etmf`, `/tickets`, `/knowledge`, and `/observations/:observationRef`, and browser refresh or link sharing reproduces active standalone scope via `router.replace`.
- **Negative Impact / Technical Debt:** Standalone endpoints continue to accept `studyId`, `siteId`, and `subjectId` query keys while Clinical Canvas routes use `site`, `participant`, and `node`; `useRouteScopeSync.ts` and `palette.ts` normalize aliases so callers do not hand-roll query parsing.
- **Mitigation Strategy:** Unit and integration suites in `packages/ui/tests/ClinicalContextBar.spec.ts` and `apps/web/tests/clinical_context_bar_route_scope.spec.ts` enforce landmark semantics, 44px hit areas, non-demo empty participant initialization, zoom-up scope clearing, and draft-safe `router.replace` updates.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `docs/components/`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui test`, `pnpm --filter web test`, `pnpm --filter subject-portal test`, `pnpm --filter web typecheck`, `pnpm lint`, `pnpm build`, `pnpm build:demo`, and `dotnet run --project packages/cli/src/Cadence.Cli.csproj -- check`.
