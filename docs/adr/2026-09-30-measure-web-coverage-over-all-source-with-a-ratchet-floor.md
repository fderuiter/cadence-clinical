# ADR-2530: Measure Web Coverage over All Source with a Ratchet Floor

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-053 (development environment guardrails: the quality gates a contributor runs must
  report the truth about the change)
- **Amends:** [ADR-251](2026-08-02-centralized-accessibility-matcher-and-80-percent-coverage-gate.md)

---

## 1. Context & Problem Statement

ADR-251 put the web apps under an 80 percent Vitest coverage gate so that "all newly authored components and routes
are thoroughly validated". The configuration measured only the files named in `coverage.include`: six files in
`apps/web` and one in `apps/subject-portal`. Every other source file was invisible to the gate, so a new component
with no test at all passed it.

Measured over all hand-written source on 2026-09-30, `apps/web` covers 47.7 percent of lines (186 files, 41 of them
at 0 percent) and `apps/subject-portal` covers 77 percent. An 80 percent gate over everything would fail today, and
reaching 80 percent means writing tests for dozens of screens whose services are still being rebuilt on the kernel.

## 2. Decision Drivers & Constraints

- **An honest gate:** the number the gate prints must be the coverage of the app, not of a hand-picked subset.
- **No regressions:** a change must not lower coverage of the files ADR-251 already holds to 80 percent.
- **Progress without a cliff:** the gate must pass today and tighten as tests are added (PRD-SYS-053).
- **No hand-maintained lists:** a new file is measured without anyone adding it to a list.

## 3. Options Considered

### Option 1: All source, a ratchet floor, and the ADR-251 files kept at 80 percent (Selected)

- **Overview:** `coverage.include` covers `src/**/*.{ts,js,vue}` minus generated code (the generated API client,
  generated types), the mock service worker and the `main.js` bootstrap. The global thresholds are set just under the measured coverage, and
  the files ADR-251 named keep their own 80 percent thresholds through glob keys.
- **Pros:**
  - ✅ Every new file counts from the day it is written.
  - ✅ A change that removes tests or adds untested code below the floor fails.
  - ✅ The 80 percent files lose nothing.
- **Cons:**
  - ❌ The floor is far below 80 percent until more of the app is tested.

### Option 2: All source at 80 percent

- **Overview:** Widen `coverage.include` and keep the 80 percent thresholds.
- **Pros:**
  - ✅ Matches ADR-251's wording.
- **Cons:**
  - ❌ Fails on every run until dozens of screens have tests, so contributors would stop running it.

### Option 3: Keep the hand-picked list (status quo)

- **Pros:**
  - ✅ No change.
- **Cons:**
  - ❌ The gate reports 80 percent for an app that is under half covered.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **Justification:** It is the only option that measures the whole app, passes today and cannot move backwards. The
  floor is a ratchet: whoever raises coverage raises the floor in the same change, and nobody lowers it.

## 5. Consequences & Trade-offs

- **Positive Impact:** Coverage reports cover the whole app; untested new code shows up as a drop.
- **Negative Impact / Technical Debt:** The floor (47 percent of lines in `apps/web`) is a baseline, not a target.
- **Mitigation Strategy:** Raise the floor with each change that adds tests. The screens at 0 percent are listed by
  running `pnpm --filter web test`, which prints per-file coverage.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/web/vitest.config.js`, `apps/subject-portal/vite.config.js`,
  [docs/TESTING.md](../TESTING.md#web-app-tests-appsweb).
- **Floors set on 2026-09-30:** `apps/web` lines 47, statements 46, functions 36, branches 38;
  `apps/subject-portal` lines 76, statements 76, functions 75, branches 59.
- **Verification Plan:**
  - `pnpm --filter web test` and `pnpm --filter subject-portal test` pass with the new thresholds.
  - Raising the `apps/web` line floor to 48 fails the run with "does not meet global threshold", and raising the
    `src/stores/{signatures,sync}.ts` threshold to 100 fails it on that glob, so both kinds of threshold are enforced.

## 7. Amendment (2026-10-19): `packages/ui`, and the plan to raise the floor (#5335)

**`packages/ui` joins the gate.** `pnpm --filter ui test` now runs with coverage over every source file, components
included, with a ratchet floor in `packages/ui/vite.config.js` (lines 37, statements 36, functions 29, branches 33 on
2026-10-19). The Part 11 helpers `signing.js` and `econsent.js` carry an 80 percent file gate; on 2026-10-19 they
measure 100 percent of lines, and 95.6 and 91.3 percent of branches.

**The floors had fallen behind.** Tests added since 2026-09-30 lifted `apps/web` to 61.7 percent of lines without
anyone raising the floor, so a change could have dropped 14 points unnoticed. The floors are reset to just under the
2026-10-19 figures: `apps/web` lines 61, statements 60, functions 45, branches 50; `apps/subject-portal` lines 77,
statements 76, functions 75, branches 61.

**Plan for `apps/web`.** The floor only moves up, and these milestones say how far it must move:

| Milestone                                                        | Lines | Branches | What gets the tests                                                                                          |
| ---------------------------------------------------------------- | ----- | -------- | ------------------------------------------------------------------------------------------------------------ |
| eCRF reference slice done (gate 3 of #5310: #5365, #5368, #5369) | 70    | 58       | eCRF entry, save states, corrections and the reason-for-change flow, end to end against the generated client |
| Modules folded into the host (gate 5 of #5310)                   | 75    | 65       | Each screen a module conversion touches leaves with tests for its API calls and error states                 |
| Production readiness review (2027)                               | 80    | 70       | The remaining screens at 0 percent, listed by `pnpm --filter web test`                                       |

The same steps apply to `packages/ui`, whose clinical components (`AuditLogViewer`, `ClinicalDataTable`,
`ClinicalModal`, `ClinicalFieldLayout`, `PersonaSwitcher` and the rest) have no tests yet: a change that touches one
adds its tests and raises the package floor. Whoever closes a milestone's last issue checks the measured figure against
the table and raises the floor in that change; a milestone the figure misses stays open on its epic.
