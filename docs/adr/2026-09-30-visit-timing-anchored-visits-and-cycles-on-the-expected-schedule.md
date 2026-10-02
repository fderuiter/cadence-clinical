# ADR-2507: Visit Timing, Anchored Visits and Cycles on the Expected Schedule

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-QLT-001 (out-of-window visits feed `VISIT_WINDOW` deviations); PRD-MDR-004 (a period that
  starts from the final visit of the one before)
- **Extends:** [ADR-2487](2026-09-30-expected-schedule-as-a-pure-function.md) (the expected schedule, K025)
- **Kernel decision:** K032
- **Issue:** #5045 (part of #4789)

---

## 1. Context & Problem Statement

`ExpectedSchedule` (ADR-2487) places a design's timed activities on the study-day axis and on the calendar for
one reference start. The Python reference `packages/engine_core/timeline/` did three more things that real
protocols need:

- It classified an actual visit as within its window, early, late or a major deviation.
- It anchored a visit on the actual date of the visit before it, so a delay does not push every later window out of
  step.
- It expanded repeating treatment cycles (for example six 21-day cycles with visits on days 1, 8 and 15) from one
  template.

It also fell back silently from one anchor to another (first dose, then randomization, then screening), and it
graded a deviation as major after a fixed 7 days outside the window.

## 2. Decision Drivers & Constraints

- **K-TIME-7:** the schedule stays a pure function of the design, the anchors and the as-of date. Nothing is stored.
- **Spec §7.4:** a deviation from plan (missed, out of window, unscheduled) is a derived Assertion, not a field on
  a visit. Its importance belongs to `Deviation(category, importance)` in the Quality engine (§10.5).
- **K-RULE-3:** a grading such as "major after 7 days" is a rule, written in the one rule language, not a constant.
- **K-SCOPE-1:** EDC, CTMS, the participant portal, RTSM and Quality all need the same dates, so this stays in the
  kernel with the function it extends.

## 3. Options Considered

### Option 1: Port the Python timeline engine as a separate engine

- **Cons:**
  - ❌ A second schedule beside `ExpectedSchedule`, with its own anchors and its own day arithmetic.
  - ❌ It hard-codes deviation severity and falls back between anchors without saying so.

### Option 2: Extend `ExpectedSchedule` (Selected)

- **Pros:**
  - ✅ One function for the timeline, simulation and every application.
- **Cons:**
  - ❌ The design's `ActivityTiming` grows, so the design format, its validator and the USDM import change with it.

## 4. Decision Outcome

- **Chosen Option:** Option 2, in three slices.

### 4.1 Visit timing assessment (slice A)

`ExpectedSchedule.Assess(expectedVisit, actualDate)` measures a visit that happened against its expected visit. It
returns a `VisitTimingAssessment`: the calendar days from the target (negative when earlier), `Early`, `InWindow`
or `Late`, and the calendar days before the window opened or after it closed (0 in the window). Both ends of the
window are in it.

- The assessment has no severity. Whether an out-of-window visit is a minor or major deviation is a Quality rule
  over `DaysOutsideWindow`, and the derived deviation Assertion records it. The Python's fixed 7-day threshold is
  not carried forward.
- A missed visit is an expected visit whose window is `Closed` at the as-of date with no actual date. It needs no
  new function.

### 4.2 Anchored visits (slice B)

`ActivityTiming` gains an optional anchor: another planned activity. The visit's day then counts from that
activity's actual date when there is one, otherwise from its expected target, so a late visit moves the visits
anchored on it. `Compute` takes the actual dates it knows. The design validator refuses an anchor that names no
timed activity, and a chain of anchors that loops.

- Named anchor events (consent, randomization, first dose) are not invented here. As ADR-2487 §4.3 says, they
  arrive with participant state, as further arguments to the same function. There is no silent fallback between
  anchors: a visit whose anchor has no date is not placed.
- The USDM import keeps resolving relative timings to fixed days until it can carry them as anchors.

### 4.3 Cycles (slice C)

`ActivityTiming` gains an optional repeat: a count and an interval in days. `Plan` expands it into occurrences, and
`PlannedVisit` gains an occurrence number (1 when the activity does not repeat). Occurrence *n* is due
`(n - 1) * interval` days after the first. The simulation's schedule changes are keyed by activity and occurrence.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Deviation detection, anchored visits and cycles all come from the one schedule function.
  - Deviation severity is configurable per study, through the rule language.
- **Negative Impact / Technical Debt:**
  - Until slice B and C land, visits anchored on another visit and cycles must be written out as fixed days.
- **Mitigation Strategy:** slices B and C follow in their own pull requests; the concepts thread, which owns the
  design types, reviews the `ActivityTiming` changes.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/kernel` (`Cadence.Kernel.Abstractions`,
  `Protocol/Design/ExpectedSchedule.cs`; slice C also changes `ScheduleEngine` in `Cadence.Kernel.Application`).
  No migration.
- **Slice A:** `ExpectedSchedule.Assess`, `VisitTimingAssessment` and `VisitTiming`. `ExpectedScheduleTests` covers
  each side of the window, both window edges, a visit without a window, and that the assessment does not depend on
  the as-of date.
- **Slices B and C:** `ExpectedScheduleTests` will cover anchors on actual and expected dates, missing anchors,
  refused loops, cycle expansion and occurrence numbering; `SimulationTests` will cover the occurrence key.
