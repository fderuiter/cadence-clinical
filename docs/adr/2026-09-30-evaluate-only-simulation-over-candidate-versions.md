# ADR-2480: Evaluate-Only Simulation over Candidate Versions

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Simulate is one of the six Clinical Canvas verbs ([Clinical Canvas](../ux/CLINICAL_CANVAS.md) §2.6, PX-16,
decision D4, issue #4843). A user asks "what happens if I change this?" about a new ProtocolVersion, a visit
window, a mapping specification or a rule pack, and sees what would change before anything is changed. The user's
phrase for it is "Git for trial configuration".

The kernel has no simulation concept. There are two ways to build one, and they lead to different persistence
models:

- **Evaluate-only:** run the kernel's pure functions against the proposed change in memory, and write nothing.
- **Copy-on-write study branches:** fork the study's state, apply the change on the fork through ordinary
  commands, and compare the fork with the original.

The choice has to be made before anyone builds either, because a branch model would add a branch dimension to
every kernel table, every bitemporal guard and every read.

## 2. Decision Drivers & Constraints

- **K-TRUTH-1:** a simulated state must never be mistaken for, or leak into, clinical truth.
- **Existing guarantees.** The spec already makes the relevant computations pure: the expected schedule is
  `f(ProtocolVersion, anchor events, as_of)` (K-TIME-7), a projection is `f(state as_of, MappingSpecification
  version, CT versions)` (K-PROJ-2, K-LIN-3), rule evaluation is deterministic (K-RULE-4), and ProtocolVersion
  elements keep stable IDs so versions can be diffed (K-PROT-1).
- **Bitemporal reads.** Any read can be pinned to `(valid_at, known_at)` (ADR-2246), so two evaluations can see
  exactly the same state.
- **Authority and blinding.** A simulation reads clinical data, so it must not show a user anything they could
  not read directly (K-AUTH-4, K-IDN-1, K-AI-3).
- **Part 11.** Simulation results that inform a decision need to be reproducible and attributable. Exploring a
  change must not create audit noise in the tenant chain.
- **Requirements:** PRD-MDR-002 (an amendment is a new version that leaves the active one untouched), PRD-MDR-006
  (blinding constraints on UI data rendering). An earlier revision of this ADR cited PRD-MDR-004, which is crossover
  period sequencing, not amendment diffing.
- **Cost.** Every kernel table, guard and query should not grow a branch dimension for a feature the UI needs
  only for versioned specifications.

## 3. Options Considered

### Option 1: Evaluate-only simulation over candidate versions (selected)

- **Overview:** the proposed change is expressed as a *candidate version* of a versioned specification (a draft
  ProtocolVersion, MappingSpecification version or RulePack version). The simulation engine evaluates the pure
  functions twice at one pinned `(valid_at, known_at)`: once with the current versions (baseline) and once with the
  candidate substituted (candidate). It returns the difference. It writes nothing. A user who wants to keep the
  result saves it as an Artifact referenced by a Proposal (spec §11).
- **Pros:**
  - ✅ No change to the persistence model, the command pipeline or any bitemporal guard.
  - ✅ Reuses the functions the spec already requires to be pure, so a simulation and the real thing share code.
  - ✅ "Git for trial configuration" still holds: specifications are already versioned with `parent_version`,
    so a chain of drafts is a branch of the configuration. Clinical data is never branched, and it shouldn't be.
- **Cons:**
  - ❌ Only changes that can be written as a candidate version can be simulated. "What if site 017 had enrolled
    ten more participants" (a change to operational data) cannot be.
  - ❌ Large studies need the evaluation bounded or run as a background job.

### Option 2: Copy-on-write study branches

- **Overview:** a branch id on every kernel row (or a shadow schema per branch). Commands run on the branch, and
  a diff compares branch and trunk.
- **Pros:**
  - ✅ Any command can be simulated, including changes to operational data.
- **Cons:**
  - ❌ Every table, exclusion constraint, RLS policy, index and query gains a branch predicate. Getting one wrong
    lets simulated state show up as truth.
  - ❌ Branch commands would either write audit entries for things that never happened, or need a second,
    unaudited write path next to the pipeline. K-TX-2 allows neither.
  - ❌ Merging a branch back means replaying commands against a trunk that has moved on. That is the hard part of
    Git, and it would be applied to regulated data.

### Option 3: Simulate in the client

- **Overview:** the web app fetches data and computes the effect of the change itself.
- **Cons:**
  - ❌ The client would re-implement kernel rules, and its answers would drift from the kernel's (Canvas doc §8,
    rule 2). Rejected.

## 4. Decision Outcome

- **Chosen Option:** Option 1, evaluate-only simulation over candidate versions.
- **Justification:** it gives the Simulate verb its main uses (protocol amendments, visit windows, mappings and
  rule packs) without adding a branch dimension to clinical truth, and it reuses the pure functions the spec
  already guarantees. The operational what-ifs it cannot answer are forecasting questions, and spec §11 already
  treats forecasts as projections with provenance.

### 4.1 The simulation contract

```
Simulate(scope: NodeRef, candidate: CandidateVersion, as_of: (valid_at, known_at), actor) → SimulationResult

CandidateVersion = draft ProtocolVersion | draft MappingSpecification version | draft RulePack version,
                   identified by its content_hash (it may be unsaved)

SimulationResult {
  scope, as_of (pinned), candidate (kind, base version, content_hash),
  engine_versions (schedule, projection, rule evaluator), input_hash, result_hash,
  structural_diff[]     element-level Added | Removed | Changed, by stable element id (K-PROT-1)
  schedule_changes[]    per participant: planned activities added, removed or moved, window changes (K-TIME-7)
  projection_changes[]  per dataset: records added, removed or changed, with counts and bounded samples (K-PROJ-2)
  validation_delta      findings that would open, findings that would resolve, unchanged count (K-RULE-4, K-FIND-1)
  dependents[]          objects that reference changed elements, aggregated by kind (Dependents, D2 / #4841)
  masked[]              parts of the result withheld from this actor, with the reason (D1 / #4840)
}
```

Which sections a result has depends on the candidate kind. A ProtocolVersion candidate yields all of them. A
MappingSpecification candidate yields projection changes and a validation delta. A RulePack candidate yields
only a validation delta.

### 4.2 Invariants (spec §11.1)

- **K-SIM-1 Simulation writes no clinical state.** The simulation engine runs on a read-only snapshot. It has
  no access to `IKernelCommandSender` or a unit of work, and an architecture test enforces that.
- **K-SIM-2 Baseline and candidate share their inputs.** Both evaluations use the same pinned
  `(valid_at, known_at)` and the same engine versions, so every difference is caused by the candidate.
- **K-SIM-3 Results are reproducible.** The result records the hash of its inputs (as-of, the base versions, the
  candidate's content hash and the engine versions). The same inputs always give the same `result_hash` (K-LIN-3).
- **K-SIM-4 Simulation runs with the requester's authority.** It reads through the same authorization and
  masking as any other read (K-AI-3). A value derived from content the actor cannot read is masked in the result,
  never computed and shown, so a simulation cannot be used to infer blinded data (K-AUTH-4).
- **K-SIM-5 A result is advice, never a change.** Saving a result creates an Artifact and a Proposal through
  ordinary commands. Approving that Proposal does not apply the result. Act sends the real commands through the
  pipeline against current state. If the state has moved on since the pinned `known_at`, the UI offers to re-run
  the simulation first.

### 4.3 Authorization and audit

- Running a simulation is a read. It needs the `Read` capabilities for what it reads, plus
  `simulation.run` at the scope. Interactive runs are not written to the tenant audit chain, because they create,
  change and delete no record (21 CFR 11.10(e) covers actions that do). They are logged as operational telemetry.
- Saving a result as a Proposal is a command. It needs `Propose` and is audited like any other command.

### 4.4 When to reconsider copy-on-write branches

Branches would be reconsidered, in a new ADR, only if one of these holds:

1. Users need to simulate changes to operational or clinical data, not only to specifications, and a forecasting
   projection is not enough.
2. A combination of candidates cannot be expressed as a set of candidate versions evaluated together.
3. Measurement shows that re-evaluating from a pinned snapshot is too slow even as a background job with cached
   results.

Even then, the branch would hold candidate data in its own store and be read-only to the pipeline. The kernel
tables would not gain a branch dimension.

### 4.5 Multi-user review

Reviewing a simulation with other people (comments on a diff, requests for changes) is an **application concern
over Proposals**. The kernel provides the Proposal, the saved result Artifact and approval as an Attestation. The
review workflow and its comment threads are application state, as spec §12 says for authoring drafts. This
answers Canvas doc §9 open question 3.

### 4.6 First implementation slice (#4866)

The first slice implements the contract for protocol candidates with the structural diff only. It settles these
details:

- **Candidates.** A stored protocol version is compared with the version it was forked from; a study's first version
  has no base, so every element is added. Unsaved content is compared with a named base whose content is frozen (in
  review or later), so the comparison can be repeated. Unsaved content must read as a protocol design (ADR-2472),
  the rule a stored draft already meets, so a simulation never evaluates a protocol that could not be saved.
- **Scope.** The scope of a protocol candidate is its study. `simulation.run` is checked there through
  `IAuthorizationProbe` (ADR-2483), the same path as `CanI`.
- **Element identity (K-PROT-1).** An element is any JSON object with a non-empty string `id`, the CDISC USDM
  convention. Ids are unique within a version; a repeated id fails the run. A root without an id is the element
  `$root`. Each element is compared on its own values: a nested element is replaced in its parent by a reference to
  its id, so a change inside a child is reported once, on the child. The parent changes only when children are added,
  removed or reordered. Changed values are listed as JSON Pointers; arrays are compared whole. Executable protocol
  elements (#4808) will give elements types but keep `id` as the key.
- **Pinning (K-SIM-2).** Protocol versions are not bitemporal. Their content is pinned by content hash instead: a
  frozen version's hash is stored, and a draft's hash is computed when it is read. The as-of is still recorded and
  hashed, so later sections that read bitemporal state share it.
- **Hashes (K-SIM-3).** The input hash covers the as-of, the candidate and base references with their content hashes,
  the engine versions and the masked parts. The result hash covers every field of the result except itself. Both are
  canonical-JSON SHA-256 (`ContentHash`).
- **Masking (K-SIM-4).** The study and both versions are passed to `INodeVisibility` once. If any is hidden, the diff
  is withheld as a masked part with the reason, the references and content hashes are left out, and the hashes are
  computed from the mask instead of the content. A hidden change therefore cannot change what the reader sees.
  Element-level masking (arm-derived schedule and projection changes) comes with those sections.
- **Dependents.** When the diff is not empty and there is a base, the result carries `Dependents`: the
  `DependentsReport` of the base version at the pinned as-of, from the one dependents read (Canvas D2, #4841). A
  stored candidate is excluded from the walk, as amendment impact excludes the amended fork, so the two report the
  same objects. The walk follows only the protocol elements the diff changes or removes (ADR-2474), so the dependents
  engine is `lineage.dependents` version 2.
  Masked objects are counted and masked by the lineage read; a hidden version withholds the section entirely.
- **Schedule changes (ADR-2487).** A later slice adds `schedule_changes[]` from the expected schedule (K-TIME-7):
  the timed activities added, removed or moved on the study-day axis, with the planned visit before and after. It is
  recorded as the engine `protocol.expected_schedule`, withheld with the diff when masked, and part of the result
  hash; `engine_versions` is sorted by name. Per-participant impact follows with participant anchors.
- **Errors.** Unknown versions, a draft base, unsaved content that is not a protocol design (the error names its
  design problems) and repeated element ids are `K.Simulation.*` errors. A denial is `K.Authorization.Denied` with the evaluator's explanation.

## 5. Consequences & Trade-offs

- **Positive Impact:** the Simulate verb gets a kernel contract without any change to the write path. Simulation
  and execution share their functions, so a simulation can't drift from what execution will do. Reproducible
  result hashes make a saved simulation usable as evidence in an amendment review.
- **Negative Impact / Technical Debt:** operational what-ifs are out of scope. Simulation is only as broad as the
  pure functions that exist: schedules need executable protocol elements (#4808), projections need the SDTM
  projection (#4814), and the validation delta needs rules as data (#4811).
- **Mitigation Strategy:** simulation is built only when those functions exist (roadmap phase 2 to 3). Until
  then, the Simulate control is absent in the UI, not mocked (Canvas doc §6.1).

## 6. Implementation & Verification

- **Affected Repositories / Services:** kernel specification §11.1 (K-SIM-1..5) and §17 (K011); Canvas doc D4.
  The first slice (§4.6) is `Cadence.Kernel.Simulation` (contract), `Cadence.Kernel.Application.Simulation`
  (`SimulationEngine`, `ProtocolStructuralDiff`, the `ISimulationInputs` port) and
  `Cadence.Kernel.Persistence.Simulation` (`KernelSimulationInputs`). The simulation engine is tracked in #4866, which depends on #4783
  (Proposal), #4808 (protocol elements), #4811 (rules as data) and #4814 (SDTM projection).
- **Verification Plan:** when the engine is built, its tests must show:
  - an architecture test that the simulation assembly or namespace does not depend on `IKernelCommandSender`,
    `IKernelUnitOfWork` or kernel persistence write paths (K-SIM-1);
  - a candidate identical to the base version gives an empty result (K-SIM-2);
  - two runs with the same inputs give the same `result_hash` (K-SIM-3);
  - an actor without an unblinded capability sees arm-derived schedule and projection changes masked, and a
    blinded actor's result hash does not change when the hidden arm assignments change (K-SIM-4);
  - no rows are added to any kernel table by a simulation run (K-SIM-1).
