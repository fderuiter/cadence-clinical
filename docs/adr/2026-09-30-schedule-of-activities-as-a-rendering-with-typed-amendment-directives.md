# ADR-2475: Schedule of Activities as a Rendering, with Typed Amendment Directives

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The spec says the SoA grid is a rendering of the executable protocol, not a second copy of it (§6.2). ADR-2472 gave
the protocol version a typed design: planned activities (USDM encounters and scheduled activity instances) with an
epoch, a timing and locations, and collection specifications (USDM activities) with concept-bound items. ADR-2487
derives a participant's expected schedule from that design. Nothing yet draws the matrix protocol authors, sites and
the Canvas read, and nothing turns an amendment into the schedule changes a reviewer needs to choose its impacted
elements and transition policy (§7.5). Issue #4730 asks for both.

Open questions:

1. What the matrix is: rows, columns, cells and footnotes, and in what order.
2. Whether the matrix is stored or compiled.
3. What an amendment's schedule changes look like, and how they relate to the structural diff (ADR-2480 §4.6).
4. Who may read the matrix and the amendment report.
5. What is left for later: per-arm schedules and USDM export.

## 2. Decision Drivers & Constraints

- **Driver 1 (one truth, K-TRUTH-1):** the design is the truth. The matrix must follow from it and nothing else, so the
  same content hash always gives the same matrix.
- **Driver 2 (typed changes):** a reviewer asks "which visits moved, which assessments are collected less often, who
  can still enter", not "which JSON Pointers changed".
- **Driver 3 (stable design types):** the Canvas and the expected schedule read `ProtocolDesign`; its types stay as they
  are.
- **Driver 4 (performance):** compiling the matrix for a realistic protocol takes well under 50 ms.
- **Requirements:** PRD-DDF-001 (the SoA synthesised from the digitised USDM design): the matrix is compiled from the
  typed design the USDM import produces. PRD-SYS-179 (differential, element-level changes on a protocol amendment): the
  amendment impact report lists typed visit, activity and criterion changes with the elements and concepts they touch.
  PRD-CRF-013 (site and tenant data isolation) and PRD-MDR-006 (blinding constraints on data rendering): a version the
  reader may not see has its matrix and its impact report refused.
- **Constraint:** no new table and no migration.

## 3. Options Considered

### Option 1: A pure compiler and a pure diff in the kernel abstractions (Selected)

`ScheduleOfActivities.Compile(design)` and `ScheduleOfActivitiesDiff.Compare(before, after)`, beside
`ExpectedSchedule`, with the host reading the design and calling them.

- ✅ Deterministic by construction; the matrix is a function of the content hash.
- ✅ Nothing to keep in step: an approved version's content is frozen, so its matrix is too.
- ✅ Applications (Canvas, EDC, CTMS) call the same functions in process (ADR-2244).
- ❌ Every read compiles again. The cost is a few milliseconds.

### Option 2: Store the compiled matrix with the version

- ✅ No compile on read.
- ❌ A second copy of clinical truth that can drift from the content it came from, and a migration.

### Option 3: Build the matrix in the web client

- ✅ No kernel change.
- ❌ Every application would rebuild it differently, and the amendment report would not have typed changes.

## 4. Decision Outcome

**Chosen Option:** Option 1.

### 4.1 The matrix

`SoaMatrix(Title, Epochs, Columns, Rows, Footnotes)` in `Cadence.Kernel.Protocol.Design`:

- **Columns** are planned activities. They are ordered by their epoch's `Order` (then epoch id), then timed before
  event-driven, then by study day (there is no day 0, as in ADR-2487), then by id. Each carries its study day (null when
  event-driven), its window and its locations.
- **Epochs** head the columns: each epoch that has columns, with its first column and its span.
- **Rows** are collection specifications, in design order, with their items, the concept versions those items record,
  the indexes of the columns that collect them, and `Frequency` (how many columns do).
- **Cells** are implicit: a row's column indexes.
- **Footnotes** are generated, never authored, numbered from 1 in order of first use, and shared by identical text:
  - a visit window ("Window: 3 days either side.", or before and after when they differ);
  - where a visit may take place, when that is not only the site;
  - an event-driven visit;
  - the operational need a specification is collected for;
  - a specification no planned activity collects.
- `ScheduleOfActivities.Write` serialises with source-generated JSON: camel case, enum names, two-space indents and
  `\n` line ends on every platform. `ScheduleOfActivities.Hash` is the `ContentHash` of that text.

There is one matrix per version, for all arms: a planned activity has no arm today. Per-arm schedules need an arm
reference on the planned activity, which is an additive design change for a later slice.

### 4.2 Amendment directives

`ScheduleOfActivitiesDiff.Compare(before, after)` compares two designs through their stable element ids (K-PROT-1) and
returns `SoaAmendmentDiff`:

| Directive                                                | When                                                                                          |
| -------------------------------------------------------- | --------------------------------------------------------------------------------------------- |
| `VisitAdded`, `VisitRemoved`                             | A planned activity is new or gone.                                                            |
| `VisitMoved`                                             | Its epoch or study day changed, or it gained or lost a fixed day.                             |
| `VisitWindowChanged`                                     | Only its window changed.                                                                      |
| `VisitLocationsChanged`                                  | Where it may take place changed.                                                              |
| `ActivityAdded`, `ActivityRemoved`                       | A collection specification is new or gone.                                                    |
| `ActivityFrequencyChanged`                               | The planned activities that collect it changed; the directive lists those added and removed.  |
| `ActivityConceptsChanged`                                | Its items, or the concept versions they record, changed.                                      |
| `CriterionAdded`, `CriterionRemoved`, `CriterionChanged` | An eligibility criterion is new, gone, or its text, category, concepts or expression changed. |

- Each directive names the elements it touches: a visit directive names the activity and the specifications it
  collects (before and after); a criterion directive names the criterion and the populations that list it. It also
  names the concept versions it touches.
- `ChangesSchedule` is true when any visit or activity directive exists: current participants would do something
  different, or at a different time, unless the transition policy keeps them on the previous version.
  `ChangesEligibility` is true when any criterion directive exists, which concerns only new participants. The kernel
  states these facts; choosing the transition policy stays with the author.
- `Undeclared(declared)` lists the touched elements the amendment does not declare.
- A name change alone is not a directive; the structural diff (ADR-2480 §4.6) still reports it in `ChangedElements`.
- Directives are sorted visits, activities, criteria, then by element id and kind, so the diff is deterministic too.

### 4.3 Where they surface

- `IProtocolVersionReader.GetScheduleOfActivitiesAsync` compiles a stored version's design, under the tenant's
  Row-Level Security. It returns null when the tenant has no such version or the content is not a typed design.
- `AmendmentImpact.Schedule` carries the directives between the amended version and its parent; it is null when the
  amended content is not a typed design.
- The kernel API serves `GET /api/v1/kernel/protocol-versions/{versionRef}/schedule-of-activities` (with the matrix
  hash and the version's content hash) and adds `schedule` to the amendment impact response, with `undeclared`.

### 4.4 Visibility

The matrix and the impact report both describe a version's content. A version the reader may not see (grant-backed
visibility, ADR-2457) has both refused with 403, like a hidden study's version list; they are never returned masked.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - One SoA for every application, identical byte for byte for the same design.
  - Amendment review sees typed schedule and eligibility changes and the elements it forgot to declare.
  - The facts a transition policy depends on (does the schedule change, does eligibility change) are computed.
- **Negative Impact / Technical Debt:**
  - The matrix is compiled on every read.
  - One matrix for all arms until planned activities name their arms.
- **Future Considerations:**
  - USDM v3 and v4 export of the design, verified against `packages/usdm-schemas` (#4730, next slice).
  - Per-arm schedules.
  - The K-PROT-2 gate: approving an amendment needs its impact report, directives included, attested as reviewed.
  - A Quality finding when an amendment leaves touched elements undeclared.

## 6. Implementation & Verification

- **Code:** `ScheduleOfActivities`, `SoaMatrix`, `ScheduleOfActivitiesDiff` and `SoaAmendmentDiff`
  (`Cadence.Kernel.Abstractions/Protocol/Design`); `AmendmentImpact.Schedule` and
  `IProtocolVersionReader.GetScheduleOfActivitiesAsync`, implemented by `KernelAmendmentImpact` and
  `KernelProtocolVersionReader` (`Cadence.Kernel.Persistence`); `KernelReadEndpoints`, `KernelLineageEndpoints` and
  `KernelScheduleResponses` (`Cadence.Kernel.Api`).
- **Tests:** `ScheduleOfActivitiesTests` (unit) compare the matrices of the CDISC USDM 4 example study and a
  cardiology protocol, and one amendment's directives, with checked-in expected files in `tests/fixtures/soa`; check
  byte-identical output, epoch grouping, cells, footnotes, edge cases and the 50 ms budget. `KernelApiHostTests` cover
  the endpoint, the `schedule` section and the refusals. `ScheduleOfActivitiesPostgresTests` read a stored version's
  matrix and a real amendment's directives.
- **References:** spec §6.2 (K-PROT-1, K-PROT-2), §7.5, K-TIME-7; ADR-2472; ADR-2474; ADR-2480 §4.6; ADR-2487;
  #4730.
