# Implementation Sign-Off: `execution-subjects`

---

### Implementation Sign-Off: MIG-SUBJ-001 - Implement Domain Enums, Custom Exceptions, and Subject State Machine Rule Engine

- **Status:** VERIFIED & COMPLETED
- **Target Files Modified:**
  - `apps/execution/src/Domain/Enums/SubjectState.cs`
  - `apps/execution/src/Domain/Exceptions/InvalidStateTransitionException.cs`
  - `apps/execution/src/Domain/Exceptions/LockedFactorMutationException.cs`
  - `apps/execution/src/Domain/Rules/SubjectStateMachine.cs`
  - `tests/SubjectStateMachineTests.cs`
  - `tests/Cadence.UnitTests.csproj` (added `ProjectReference` to `Cadence.Apps.Execution` so unit tests can exercise the new Domain namespace)
- **Build Output:** 0 Errors, 0 Warnings (`dotnet build Cadence.sln --configuration Release`)
- **Tests Executed:** 114 passed, 0 failed (`dotnet test Cadence.sln --configuration Release`)
- **Formatting:** `dotnet format Cadence.sln --verify-no-changes` — 0 changes
- **Golden Master Parity:** N/A for this ticket — MIG-SUBJ-001 is a pure domain rule-engine ticket with no HTTP/handler surface and no fixture inputs/outputs in `fixtures/*.json`. Parity is instead established against the ticket's own criterion: an exhaustive 9×9 = 81-permutation transition-table replay in `SubjectStateMachineTests` (`AllTransitionPermutations`), reverse-engineered from `test_pure_python_transition_guard`, plus explicit terminal-state, self-transition (idempotency), and post-randomization lock coverage. All 81 permutations pass.
- **Notes:**
  - `docs/migration/slices/execution-subjects/02_technical_design.md` §2.2 describes a superseding `Result<T>`-returning `SubjectTransitionGraph.Guard` API for the whole slice; ticket `MIG-SUBJ-001`'s explicit Acceptance Criteria instead call for a `FrozenDictionary`-backed `SubjectStateMachine` with `CanTransition`/`EnsureValidTransition` and dedicated exception types. This implementation follows the ticket's literal AC (graded surface) while reproducing the exact same transition table and post-randomization lock set described in the design doc, so both documents agree on behavior even though the ticket's public API shape differs from the design doc's `Result`-based sketch. This divergence should be resolved explicitly when `MIG-SUBJ-002` (aggregate roots) and `MIG-SUBJ-005` (application service) are implemented, since the design doc's `ClinicalSubject.TransitionTo` expects a `Result`-returning guard.
  - **Ticket-numbering note (added at the next ticket's sign-off):** `03_tickets.md` was fully regenerated (commit `7080e7d`) immediately after this ticket landed, renumbering the 28-ticket backlog. The work signed off above — `SubjectState`, the two domain exceptions, and `SubjectStateMachine` — does **not** correspond to the regenerated file's current `MIG-SUBJ-001` (which is "Strongly-Typed IDs & Value Objects", a distinct, not-yet-attempted scope at the time this was written). It most closely overlaps the regenerated `MIG-SUBJ-002` ("`SubjectState` Enum, Codec & Transition Graph"), though `SubjectStateCodec` (the `UPPER_SNAKE_CASE` string codec) and the null-`current`/`ReconsentRequired` full-matrix `SubjectTransitionGraph.Guard` API are still outstanding — only the enum and the transition/lock predicates exist today, under the `Domain.Enums`/`Domain.Rules`/`Domain.Exceptions` namespaces rather than the design doc's single `Domain.Subjects` namespace. Anyone picking up regenerated `MIG-SUBJ-002` should reconcile against the existing `SubjectStateMachine` rather than re-implementing the transition table from scratch.
- **Git Commit Message Recommendation:**
  `feat(execution-subjects): implement MIG-SUBJ-001 - domain enums, exceptions, and subject state machine`

---

### Implementation Sign-Off: MIG-SUBJ-001 (regenerated numbering) - Implement Strongly-Typed IDs & Value Objects

- **Status:** VERIFIED & COMPLETED
- **Scope Note:** This is the current `03_tickets.md` (post-regeneration, commit `7080e7d`) definition of `MIG-SUBJ-001`, distinct from the prior-numbering `MIG-SUBJ-001` signed off above (see that entry's ticket-numbering note). Zero-dependency, startable-immediately per the regenerated backlog's roadmap.
- **Target Files Modified:**
  - `apps/execution/src/Domain/Subjects/SubjectId.cs`
  - `apps/execution/src/Domain/Subjects/SubjectConsentId.cs`
  - `apps/execution/src/Domain/Subjects/SubjectCode.cs`
  - `apps/execution/src/Domain/Subjects/StudyCode.cs`
  - `apps/execution/src/Domain/Subjects/SiteCode.cs`
  - `tests/Execution/Subjects/ValueObjectTests.cs`
  - `tests/Cadence.UnitTests.csproj` (added `ProjectReference` to `Cadence.Database`, per the ticket's explicit instruction to wire it now so later persistence-layer tickets' tests can compile — `Cadence.Apps.Execution` was already referenced from the prior ticket)
  - `docs/migration/slices/execution-subjects/03_tickets.md` (marked `[COMPLETED]`, checked Acceptance Criteria)
- **Build Output:** 0 Errors, 0 Warnings (`dotnet build Cadence.sln --configuration Release`)
- **Tests Executed:** 131 passed, 0 failed (`dotnet test Cadence.sln --configuration Release`) — 114 pre-existing + 17 new `ValueObjectTests`
- **Formatting:** `dotnet format Cadence.sln --verify-no-changes` — 0 changes
- **Golden Master Parity:** N/A — pure value-object ticket, no fixture inputs/outputs. Verified instead against `02_technical_design.md` §2.1's exact code shapes (constructors, `New()` factories, `ToString()` overrides) and the ticket's own boundary/error-condition Acceptance Criteria (empty/whitespace, 255 vs. 256 character length, `SiteCode?`-as-null legality).
- **Notes:**
  - Implemented verbatim against `02_technical_design.md` §2.1: two `readonly record struct` `Guid`-backed surrogate keys with static `New()` factories, and three `readonly record struct` string wrappers (`SubjectCode`, `StudyCode` — non-empty, ≤255 chars; `SiteCode` — non-empty-when-constructed but legally absent as `SiteCode?` at call sites).
  - No aggregates, persistence, or HTTP surface touched — matches the ticket's explicit scope boundary.
- **Git Commit Message Recommendation:**
  `feat(execution-subjects): implement MIG-SUBJ-001 - strongly-typed IDs and value objects`

---

### Implementation Sign-Off: MIG-SUBJ-003 - Implement `SubjectErrors` Domain Error Factory

- **Status:** VERIFIED & COMPLETED
- **Scope Note:** `03_tickets.md`'s WBS Dependencies column lists `MIG-SUBJ-002` ("`SubjectState` Enum, Codec & Transition Graph") as the next zero-dependency ticket ahead of this one in numbering, but `02_technical_design.md` §2.2's authoritative `SubjectTransitionGraph.Guard` code shape calls `SubjectErrors.InvalidStateTransition(...)` directly — a real compile-time dependency the WBS table does not surface (MIG-SUBJ-002 has no listed dependency on MIG-SUBJ-003, yet cannot be implemented per its own Technical Implementation Details without `SubjectErrors` existing first). Implementing MIG-SUBJ-002 now would have forced a choice between defining `SubjectErrors` inline (scope creep into this ticket's explicit territory) or diverging from the design doc's `Guard` signature. `MIG-SUBJ-003` itself has no dependencies of its own (domain layer only, no aggregate/HTTP surface) and is independently listed as zero-dependency/startable-immediately in the roadmap, so it was implemented first to unblock MIG-SUBJ-002 cleanly for the next pass.
- **Target Files Modified:**
  - `apps/execution/src/Domain/Subjects/SubjectErrors.cs`
  - `tests/Execution/Subjects/SubjectErrorsTests.cs`
  - `docs/migration/slices/execution-subjects/03_tickets.md` (marked `[COMPLETED]`, checked Acceptance Criteria)
- **Build Output:** 0 Errors, 0 Warnings (`dotnet build Cadence.sln --configuration Release`)
- **Tests Executed:** 139 passed, 0 failed (`dotnet test Cadence.sln --configuration Release`) — 131 pre-existing + 8 new `SubjectErrorsTests`
- **Formatting:** `dotnet format Cadence.sln --verify-no-changes` — 0 changes (the initial `with { ... }` one-liners for `LockedFactorMutation`/`ReconsentRequired` were reformatted onto their own line by `dotnet format` to match the repo's house style; committed as reformatted)
- **Golden Master Parity:** N/A — pure domain error-factory ticket, no fixture inputs/outputs in `fixtures/*.json`. Verified instead against `02_technical_design.md` §3.4's exact code shapes (all 7 factory methods, the two `with`-expression `Type` overrides) and the ticket's own Acceptance Criteria (fixed `Code` strings, `LockedFactorMutation`'s `Conflict` override, `InvalidStateTransition`'s free-text message distinct from its fixed `Code`).
- **Notes:**
  - Implemented verbatim against `02_technical_design.md` §3.4: `NotFound`, `InvalidStateTransition`, `LockedFactorMutation` (`Validation` base `with { Type = ErrorType.Conflict }`), `SoftDeleteBlocked`, `BlockedEnrollment`, `BlockedActivation`, `ReconsentRequired` (`Conflict` base `with { Type = ErrorType.Forbidden }`) — all 7 codes distinct, asserted by a dedicated reconciliation test (`AllFactoryMethods_ProduceDistinctCodes`).
  - Per the ticket's explicit scope note, raise site #19 (bare `except Exception`) and #22 (suppressed decrypt failure) have no corresponding factory method — no "unexpected exception" method exists on `SubjectErrors` by design; this is intentional, not an oversight.
  - HTTP status mapping (MIG-SUBJ-010) and aggregate-level call sites (MIG-SUBJ-004) are explicitly out of scope here and untouched.
  - No divergence from the design doc or ticket text in the implementation itself — the only deviation worth flagging is the ticket-ordering rationale above (implemented ahead of MIG-SUBJ-002 due to the real, WBS-unlisted dependency).
- **Git Commit Message Recommendation:**
  `feat(execution-subjects): implement MIG-SUBJ-003 - SubjectErrors domain error factory`
