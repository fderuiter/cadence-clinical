# ADR-2594: Unify Cross-Study Home, Scoped Work Queues, and Shared EntityLink Navigation

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (21 CFR Part 11 attributable clinical context and plain-English capability explanations), PRD-CRF-015 (WCAG 2.2 Level AA contextual disclosure, focus restoration, and 44px touch targets)

---

## 1. Context & Problem Statement

Clinicians, study coordinators, monitors, and sponsor reviewers previously encountered fragmented entry points and disconnected operational silos across the web workspace: `/` landed on `/overview` with commercial product-family launcher cards (`Blueprint`, `Capture`, `Orbit`), `/studies` without a `:studyCode` parameter rendered only a bare code-input box instead of actionable cross-study work items, standalone module URLs (`/ctms`, `/ssu`, `/etmf`, `/training`) took non-demo users away from the lifecycle-oriented Clinical Canvas (`StudyCanvasView.vue`), and clicking `NodeRef` chips jumped straight across views or collapsed inspector panes without preserving focus or offering a consistent contextual preview first.

## 2. Decision Drivers & Constraints

- **Unified Clinical Workspace Entry (`/studies`):** Clinicians signing into live workspaces need immediate visibility into cross-study items waiting on their authority (`My Action Items`, `within` omitted on `GET /api/v1/kernel/work-queues`) alongside visible study portfolio cards (`kernelApi.listStudies()`) and study-scoped work queues above `StudyPulse` on `/studies/:studyCode`.
- **Contextual Entity Inspection Before Route Transitions (PRD-CRF-015):** Clicking any `NodeRef` entity link must resolve a human-readable clinical label (or masked visibility badge), open a contextual `ClinicalDrawer` or `CanvasInspector` preview first while preserving focus restoration, and expose a canonical `Open full view` link with unsaved-draft protection (`hasRegisteredUnsavedDrafts`).
- **Scope-Preserving Silo Consolidation & Demo Compatibility (PRD-SYS-001):** Non-demo navigations to `/`, `/overview`, `/ctms`, `/ssu`, `/etmf`, and `/training` must redirect into `/studies` and its `Start` stage sections (`startup-greenlight`, `site-activation`, `documents`, `training`) while preserving `studyCode`, `site`, and `participant` query context ([ADR-2651](2026-11-22-shared-clinical-context-bar-and-url-scope-codec.md)) and keeping static demo builds (`isDemoBuild()`) compatible with MSW handlers.

## 3. Options Considered

### Option 1: Shared `EntityLink` UI Primitive + Unified `/studies` Cross-Study Home + Scope-Preserving Canvas Stage Redirects (Selected)

- **Overview:** Export `<EntityLink>` from `packages/ui` (`ui`) to resolve `NodeRef` summaries, mask hidden items in place, open a contextual `ClinicalDrawer` or `CanvasInspector` preview first, and guard `Open full view` navigation against unsaved drafts via an accessible `ClinicalModal` or injected confirmation handler; upgrade `/studies` in `StudyCanvasView.vue` into the Cross-Study Home combining `WorkQueuesPanel` (`within` omitted) and `kernelApi.listStudies()` portfolio cards; mount study-scoped `WorkQueuesPanel` (`within: studyRef`) above `StudyPulse` on `/studies/:studyCode` with plain-English capability summaries and `KernelExplanation` disclosures; redirect `/` and non-demo `/overview`, `/ctms`, `/ssu`, `/etmf`, and `/training` to `/studies` and `Start` stage sections while retiring commercial launcher cards from `OverviewView.vue` and simplifying `AppShell.vue` and `palette.ts`.
- **Pros:**
  - Eliminates commercial product-family jargon and extra clicks between sign-in, cross-study triage, and study-scoped conduct or startup work.
  - Standardizes contextual `NodeRef` inspection and focus return across work queues, explanations, activity trails, and inspector drawers without exposing raw UUIDs or internal capability dot-codes.
  - Preserves active `studyCode`, `site`, and `participant` scope across legacy bookmarks and keeps static demo mode (`VITE_DEMO_MODE=true`) functional.
- **Cons:**
  - Requires conditional route guards in `apps/web/src/router/index.js` and `apps/web/src/canvas/palette.ts` so non-demo builds consolidate onto Clinical Canvas stages while demo builds retain standalone MSW showcase routes.

### Option 2: Keep `/overview` as Primary Hub and Duplicate Work Queue Panels Across Standalone Silo Views

- **Overview:** Retain `/overview` as the default landing page, add cross-study work queues to `OverviewView.vue`, and keep `/ctms`, `/ssu`, `/etmf`, and `/training` as separate primary navigation silos outside demo mode.
- **Pros:**
  - Avoids updating existing non-demo route redirect expectations.
- **Cons:**
  - Perpetuates split navigation between `/overview`, `/studies`, and standalone startup modules, duplicates chrome and context switching, and leaves `NodeRef` links inconsistent across views.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centering cross-study triage and study execution on `/studies` (`StudyCanvasView.vue`), consolidating startup silos into `Start` stage sections outside demo mode, and routing every `NodeRef` interaction through `<EntityLink>` delivers a cohesive, plain-English clinical operating workspace with attributable scope preservation and WCAG 2.2 AA contextual disclosure.

## 5. Consequences & Trade-offs

- **Positive Impact:** Clinicians land directly on `/studies` with `My Action Items` and tenant study cards, inspect `NodeRef` entities in context before switching views, see plain-English capability explanations with linked blocking entities above `StudyPulse`, and retain unsaved-draft protection across `StudyCanvasView` transitions.
- **Negative Impact / Technical Debt:** `OverviewView.vue` remains mounted only in static demo builds (`isDemoBuild()`) for orientation tours and plan summaries while live workspaces use `OnboardingChecklistDrawer.vue` from the persistent header.
- **Mitigation Strategy:** Vitest suites cover both live non-demo redirects and `VITE_DEMO_MODE=true` behavior across `router_rbac.test.js`, `screen_availability.test.js`, `CrossStudyHomeAndEntityLink.spec.ts`, and `packages/ui/tests/EntityLink.spec.ts`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `tests/Cadence.UnitTests`, `docs/components/`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm --filter ui test`, `pnpm --filter web typecheck`, `pnpm --filter web test`, `pnpm --filter subject-portal test`, `pnpm lint`, `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~WorkQueue"`, and `dotnet run --project packages/cli/src/Cadence.Cli.csproj -- check`.
