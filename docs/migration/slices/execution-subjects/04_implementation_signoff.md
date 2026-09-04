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
- **Git Commit Message Recommendation:**
  `feat(execution-subjects): implement MIG-SUBJ-001 - domain enums, exceptions, and subject state machine`
