# ADR-2487: Expected Schedule as a Pure Function of the Protocol Design

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Spec K-TIME-7 says the expected schedule is a pure function of `(ProtocolVersion, anchor events, as_of)`, never
stored as truth. Three features need it: the participant timeline (Canvas PX-14), the Simulate verb's
`schedule_changes[]` section (ADR-2480, PX-16) and, later, visit scheduling and deviation assessment in the
Execution Engine. Issue #4866 tracks the simulation engine; this is the slice that gives it a schedule.

The protocol design (ADR-2472) now carries planned activities with an optional timing (`ActivityTiming`: a planned
day and a window before and after), so the function has typed input. The spec leaves open:

- how a planned day maps to a calendar date (is there a day 0?);
- what the "anchor events" are before participant state is modelled;
- how a schedule change is reported in a simulation that has no participants yet.

## 2. Decision Drivers & Constraints

- **K-TIME-7.** The schedule is computed, never stored. A cache is a projection with the inputs it came from.
- **K-SIM-2, K-SIM-3.** Simulation compares two evaluations of the same function, and its result hash must be
  reproducible, so the function takes its time as an argument and reads no clock (K-TIME-3).
- **Public standards.** SDTM derives study day (`--DY`) with no day 0: day 1 is the reference start date
  (`RFSTDTC`) and the day before is day -1. Using the same convention keeps the schedule, captured data and the
  SDTM projection (#4814) on one axis.
- **Requirements:** PRD-TRN-001 (a protocol amendment identifies its modified visit schedules) and PRD-MDR-004
  (timeline mapping of sequential periods, whose day arithmetic this function provides; period sequencing itself
  is not decided here).
- **K-SCOPE-1.** The schedule explains when evidence is expected for every application (EDC, CTMS, RTSM,
  participant portal), so it belongs in the kernel.

## 3. Options Considered

### Option 1: A pure function in `Cadence.Kernel.Abstractions` over the design (selected)

- **Pros:**
  - ✅ Simulation, the timeline and applications call the same code with no database.
  - ✅ No migration and nothing to keep in sync.
- **Cons:**
  - ❌ Every caller computes it; a per-participant cache comes later if reads need it.

### Option 2: Store planned visits per participant at enrollment

- **Cons:**
  - ❌ Contradicts K-TIME-7: stored visits drift when a protocol version or anchor changes.
  - ❌ An amendment would have to rewrite rows for every participant.

### Option 3: Day 0 as the reference start

- **Cons:**
  - ❌ Disagrees with SDTM `--DY`, so a visit planned on day 29 would be derived as day 30 in the tabulation.

## 4. Decision Outcome

**Chosen option: Option 1.**

### 4.1 The function

`Cadence.Kernel.Protocol.Design.ExpectedSchedule` has two entry points:

- `Plan(design)` places the design's timed activities on the study-day axis as `PlannedVisit`s: activity id, name,
  epoch and its order, study day, and window. It is ordered by study day, then epoch order, then activity id
  (ordinal), so the same design always gives the same list. Activities without a timing are event-driven (an
  adverse event form, an unscheduled visit) and are not on the schedule.
- `Compute(design, referenceStart, asOf)` places the plan on the calendar for one reference start: each
  `ExpectedVisit` has its target date, the first and last dates of its window, and whether the window is
  `NotYetOpen`, `Open` or `Closed` at `asOf`. The window's first and last dates count as open.

Both are dates in the site's calendar (`DateOnly`). The caller converts instants to the site's local date, because
a visit window is a matter of calendar days where the participant is, not UTC.

### 4.2 Study days

Study days follow the SDTM `--DY` convention: day 1 is the reference start, day -1 the day before, and there is no
day 0. A timing of day 0 is read as day 1, so a design written with a zero-based baseline still lands on the
reference start rather than being refused.

### 4.3 Anchors

The first slice has one anchor: the reference start (study day 1), passed in by the caller. Which event sets it
(randomization, first dose, consent) is a protocol choice that the design does not yet express. Per-epoch anchors
and anchor events read from participant state come with the participant timeline (PX-14) and enrollment events;
they add arguments to the same function rather than stored state.

### 4.4 Schedule changes in simulation

A simulation of a protocol candidate reports `schedule_changes[]` (spec §11.1) on the study-day axis, before any
participant is involved: the timed activities added to the schedule (new, or newly timed), removed from it (deleted,
or no longer timed), and changed (moved to another study day or epoch, or a window widened or narrowed). Each change
carries the planned visit before and after. A renamed activity or a changed collection is a structural change only,
already in `structural_diff[]`. Changes are sorted by activity id.

- The engine version is `protocol.expected_schedule` version 1, recorded in `engine_versions` and hashed.
  `engine_versions` is now sorted by name.
- The schedule changes are part of the result hash.
- A hidden study or version withholds the section as the masked part `schedule_changes`, like the structural diff.
  Planned activities are not per arm in the design, so there are no arm-derived schedule values to mask separately
  yet.
- A result is empty only when its structural diff, schedule changes and masked parts are all empty.

Per-participant schedule impact (which participants' pending visits move, given each participant's anchor and
governing version) is a later #4866 slice that calls `Compute` for both versions.

## 5. Consequences & Trade-offs

- **Positive:** one schedule function for simulation, the timeline and applications; a planned day means the same
  date in the schedule and in the SDTM projection.
- **Negative:** callers recompute the schedule; a design without timings has an empty schedule, which is correct
  but gives Simulate nothing to show until designs carry timings.
- **Mitigation Strategy:** the function is linear in the number of activities; a cached projection keyed by
  version and anchor can be added without changing callers.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/kernel`:
  - `Cadence.Kernel.Abstractions`: `Protocol/Design/ExpectedSchedule.cs` (`ExpectedSchedule`, `PlannedVisit`,
    `ExpectedVisit`, `VisitWindowStatus`); `Simulation/Simulation.cs` (`ScheduleChange`,
    `SimulationResult.ScheduleChanges`).
  - `Cadence.Kernel.Application/Simulation/SimulationEngine.cs`: `ScheduleEngine`, `ScheduleChangesPart`, the
    `schedule_changes` result section.
  - No migration.
- **Verification Plan:**
  - `ExpectedScheduleTests`: the study-day convention (days 1, 2, 0, -1, -14); plan order and untimed activities
    left out; target, window and status at each boundary; the same inputs give the same schedule.
  - `SimulationTests`: a candidate that moves a visit, adds a timed one and drops another reports each once with
    before and after; a rename is structural only; engine versions sorted; the masked result withholds
    `schedule_changes`.
