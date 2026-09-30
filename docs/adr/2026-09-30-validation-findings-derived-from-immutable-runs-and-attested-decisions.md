# ADR-2462: Validation Findings Derived from Immutable Runs and Attested Decisions

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-004 (declarative rules and multi-layer edit checks), PRD-QRY-003 (cross-form edit check execution), PRD-CRF-013 (tenant data isolation)

---

## 1. Context & Problem Statement

[ADR-2461](2026-09-30-rule-expression-language-rule-pack-format-and-evaluator-port.md) (K018) gave the kernel rule
packs and an evaluator that returns violations. Spec §10.3 defines what those violations become: a `ValidationRun`
and `ValidationFinding`s with a lifecycle (Open → Investigating → Resolved, Accepted or Explained), a classification,
and three invariants:

- **K-FIND-1:** a finding is identified by rule and target across runs; a re-run updates it instead of duplicating
  it, and a finding the run no longer detects is resolved with the run as evidence.
- **K-FIND-2:** Accepted and Explained need an attestation by an actor with the capability.
- **K-FIND-3:** every finding links to lineage.

Issue #4812 asks how to store this. The questions are:

1. What identifies a finding?
2. Who writes status changes?
3. How does that fit one aggregate per command (K-TX-1)?
4. What does a person sign when accepting or explaining?
5. How does a finding reach lineage?

## 2. Decision Drivers & Constraints

- **K-TX-1:** one command changes one aggregate. A run can detect up to tens of thousands of problems, and it
  resolves findings it no longer detects. So writing finding rows per run would touch thousands of aggregates in
  one command.
- **K-MOD-2:** read models are disposable projections of kernel truth.
- **K-RULE-4:** evaluation is deterministic. The same run over the same data must name the same findings on every
  machine.
- **G4 (spec §10.2):** Collection-stage findings never block a kernel write. Error findings block the other stages
  until they are resolved, accepted or explained.
- **ADR-2483:** a command's capability names a non-null resource the authorizer can resolve.
- **ADR-2482 (K-EXP-1):** the rule owner produces the explanation from the same evaluation.

## 3. Options Considered

### Option 1: Immutable runs plus a decisions record; status derived on read (selected)

- **Overview:**
  - A run is one aggregate. It holds its packs, datasets and inputs, and one insert-only detection row per problem
    it found.
  - What people decide is a separate small aggregate per finding.
  - A finding's status is a pure function of the study's runs and that decision (`FindingLedger`).
- **Pros:**
  - A run is one command and one aggregate.
  - Nothing is ever rewritten.
  - A re-run cannot corrupt earlier evidence.
  - The read model has no state of its own.
- **Cons:** each read derives status, so large studies need the two-phase read described below. A materialised
  projection can replace it later without a schema change to the truth tables.

### Option 2: One finding aggregate per finding, updated by each run

- **Cons:**
  - Breaks K-TX-1: one run changes every finding it detects or resolves, so the pipeline would need a saga per run.
  - The audit chain would record system edits of thousands of rows per run.

### Option 3: Findings as rows of the run, copied forward each run

- **Cons:**
  - Duplicates data per run.
  - Decisions would have to be copied forward too, so K-FIND-1's "update instead of duplicate" becomes a copy.

## 4. Decision Outcome

**Chosen Option:** Option 1.

### 4.1 Identity (K-FIND-1)

`ValidationFindingId.For(tenant, identity)` is a UUIDv8 derived from:

- a SHA-256 hash over the length-prefixed components (tenant, study, stage, rule pack key, rule ID, target kind,
  dataset, record key and variable).

The rule version is deliberately left out: a rule revised from v1 to v2 that still fires on the same record is the
same finding.

A target names the object, not one version of it. An observation or design element target is a NodeRef without
`@version`, so correcting an observation keeps its finding.

### 4.2 Runs

- `RecordValidationRun` is study-scoped (capability `validation_run.record`, Execute, resource the study). It
  records:
  - the released rule pack versions (at most one per key);
  - the stage and the as-of date;
  - the optional standard, standard version and agency;
  - the versioned inputs;
  - the datasets covered;
  - start and completion times;
  - the detections.
- The handler refuses draft or non-rule-pack library items and an unknown study.
- A run is `[DerivedObject]`:
  - it links `DefinedBy` from each rule pack version and `ValidatedBy` from each input (K-FIND-3);
  - its tables are insert-only (SQLSTATE `KA001`).
- Its `ValidationRunCompleted` event carries the counts of detected, new and resolved findings. It replaces the
  per-finding `FindingOpened` and `FindingResolved` events in the spec's Quality family, which could put a hundred
  thousand events into one transaction. Consumers read the delta with `IValidationFindings`.
- Tenant-level validation of library content is left for a later, additive command.

### 4.3 Status (`FindingLedger`)

A run **covers** a finding when it has the finding's stage and evaluated the finding's rule pack over its dataset.
Runs are ordered by their recording time, then by ID.

- If the latest covering run did not detect the finding, the finding is **Resolved**. The first covering run after
  its last detection is the evidence.
- Otherwise the latest decision holds (Investigating, Accepted, Explained, or Open after a reopen). The exception is
  a finding that was resolved and has recurred since that decision: it is **Open** again.
- A finding nobody decided on is **Open**.
- `FindingBlocking.Blocks` is true for an Error at any stage except Collection while the finding is Open or
  Investigating.

### 4.4 Decisions (K-FIND-2)

`ValidationFinding` is the decisions record, created by the first decision. The commands are:

| Command | Capability | Verb | Allowed from | Attestation |
| --- | --- | --- | --- | --- |
| `InvestigateFinding` | `validation_finding.investigate` | Execute | Open | None |
| `ClassifyFinding` | `validation_finding.classify` | Execute | Any status except Resolved | None |
| `AcceptFinding` | `validation_finding.accept` | Approve | Open or Investigating | `Responsibility` |
| `ExplainFinding` | `validation_finding.explain` | Approve | Open or Investigating | `Approved` |
| `ReopenFinding` | `validation_finding.reopen` | Execute | Investigating, Accepted or Explained | None |

What is signed for an acceptance or explanation:

- The person signs before the decision exists. The attestation's content hash is
  `FindingDecisionContent.Hash(finding, last detecting run, decision, text)`.
- The handler recomputes that hash from the command and the derived state. A different text, decision, run or
  meaning is refused.
- The explanation text is kept with the decision, and listing Explained findings gives the reviewer guide.

Signing needs one extension point in the kernel core:

- `Attest` gains `IAttestationTargetCheck`, consulted only for kinds the core does not know.
- Validation registers the check for `validation_finding`. It admits a finding that exists and is Open or
  Investigating.

Each decision version links `DerivedFrom` the run it was made on (K-FIND-3).

### 4.5 Reading

`IValidationFindings` has three reads:

- `FindAsync` returns one finding.
- `ListAsync` filters by stage, severity, statuses, classification, pack, dataset and blocking, and orders by
  severity, stage, pack, dataset and ID.
- `FindRunAsync` returns one run.

A read derives status from the light columns of every detection in scope, then loads full detail only for the
findings it returns. Each view builds its `Explanation` with:

- producer `quality.finding/<rule-id>@<version>`;
- evidence: the rule pack, the target when it is a kernel object, the runs and the attestation.

### 4.6 Running validation

`ValidationRunner` in `Cadence.Rules` works in four steps:

1. It loads the rule packs the study pins.
2. It evaluates them with `IRuleEvaluator`.
3. It maps each violation to a detection. Observed values are shortened to 2,000 characters.
4. It sends one `RecordValidationRun`.

`Cadence.Rules` now uses the kernel's `ValidationStage` and `ValidationSeverity` instead of its own enums.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Findings satisfy K-FIND-1 by construction: re-running cannot duplicate a finding or lose a decision.
  - Accept and explain are bound to the exact text signed.
  - Lineage reaches from a finding to its rule pack version and inputs.
- **Negative Impact / Technical Debt:**
  - Reads derive status from every light detection row of a study and stage.
  - A run holds at most 100,000 detections. A larger evaluation is recorded as several runs over fewer datasets.
- **Mitigation Strategy:**
  - Detections are indexed by `(tenant, study, stage)` and `(tenant, finding)`.
  - A disposable materialised projection can be added behind `IValidationFindings` when volume demands it (K-MOD-2).

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `Cadence.Kernel.Abstractions/Validation` (IDs, model, commands, events, read port);
  - `Cadence.Kernel.Domain/Validation` (run, decisions, ledger);
  - `Cadence.Kernel.Application/Validation` (handlers, attestation check);
  - `Cadence.Kernel.Application/Trust` (`IAttestationTargetCheck`);
  - `Cadence.Kernel.Persistence/Validation` (mapping, store) and the `ValidationRunsAndFindings` migration;
  - `Cadence.Rules` (`ValidationRunner`, kernel enums).
- **Verification Plan:**
  - Unit tests:
    - identity is deterministic and length-prefixed;
    - the ledger resolves, keeps and voids decisions;
    - a property test over 200 generated run histories;
    - blocking per stage;
    - the decision hash;
    - run validation;
    - decision transitions;
    - the explanation;
    - violation mapping.
  - PostgreSQL integration tests:
    - spec §14 test 10: a re-run after a mapping fix resolves the finding instead of duplicating it;
    - lineage edges from rule pack, input and run;
    - accept and explain refuse a mismatched attestation;
    - decisions follow the derived status, and a recurrence reopens;
    - draft packs and unknown studies are refused;
    - tenant isolation;
    - the decisions record's identity is fixed in the database;
    - the insert-only guards on every run table.
