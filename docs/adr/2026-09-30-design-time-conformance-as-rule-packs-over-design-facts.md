# ADR-2464: Design-Time Conformance as Rule Packs over Design Facts (K030)

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CDR-002 (automated SDTM and ADaM pipeline: a collected field must be able to produce a conformant SDTM record)

---

## 1. Context & Problem Statement

**K-STD-2** (spec §6.3) requires design-time conformance. Publishing a CollectionSpec runs the applicable standards
rules (variable, CT, unit and UCUM, value-level) and blocks on `Error` findings: "this field cannot produce a
conformant downstream representation." Spec §14 test 8 is the acceptance case: publishing a CollectionSpec item whose
unit has no conformant SDTM representation is blocked with a design-stage finding.

The pieces exist:

- mapping specifications and K-STD-1 at protocol submission
  ([ADR-2463](2026-09-30-mapping-specifications-as-governed-concept-to-variable-maps.md));
- rule packs and the `IRuleEvaluator` port
  ([ADR-2461](2026-09-30-rule-expression-language-rule-pack-format-and-evaluator-port.md));
- validation findings derived from immutable runs and attested decisions
  ([ADR-2462](2026-09-30-validation-findings-derived-from-immutable-runs-and-attested-decisions.md)).

Issue #4813 asks three questions:

1. What do design-stage rules evaluate?
2. Who runs them, and how does a run relate to the draft it checked?
3. Where does the block happen?

## 2. Decision Drivers & Constraints

- **K-TX-1:** a command changes one aggregate. Submitting a protocol version cannot also record a validation run.
- **K-RULE-1/K-RULE-2:** rules are versioned data over one AST and implement public standards only. Severity is rule
  content that a sponsor may choose, not code.
- **K-PROJ-1:** no kernel type names an SDTM variable, so a check such as "the standardised numeric result goes to a
  numeric variable" reads the variable's type from the registered package, never from a list of names.
- **K-FIND-1/K-FIND-2:** findings are derived from runs; accepting one needs a `Responsibility` attestation. A
  finding must follow its design element across protocol versions, or every amendment would reopen it.
- **K-PROT-3:** an item with no path to an endpoint, estimand, safety rule or operational need is a design finding.
- **Nothing guesses:** a fact the check cannot show to conform is a problem, not a pass.

## 3. Options Considered

### Option 1: Design facts evaluated by a released rule pack (selected)

The kernel derives facts from the design, the study's mappings, the standard packages and the CT, as records. A
design-stage rule pack judges them, and the run is recorded like any other validation run. The submit check reads the
run and its findings.

- **Pros:** the facts are computed once, in one place, without judgement. Severity and wording are pack content, so a
  sponsor can release its own pack over the same facts. Findings, acceptance and resolution reuse ADR-2462 unchanged.
- **Cons:** a fact such as "this unit has no conformant representation" is computed in code, and the rule only tests it.
  The judgement of what conforms sits in the facts.

### Option 2: Rules that read the design, the mapping and the package directly

- **Pros:** every check is visible as a rule.
- **Cons:** the expression language would need joins over the design, the mapping specifications, the package and the
  CT. That makes the AST a query language and every evaluator implement it.

### Option 3: Hard-coded checks in the submit handler

- **Pros:** simple, one transaction.
- **Cons:** no findings, so nothing to accept, explain or resolve; the block could not be overridden with an attested
  decision, and severities would be code.

## 4. Decision Outcome

**Chosen Option:** Option 1.

### 4.1 Design facts

`DesignConformance.BuildAsync` (in `Cadence.Kernel.Abstractions`, namespace `Cadence.Kernel.Standards.Conformance`)
turns a design, the study's resolved `StudyMappings`, the registered package of each declared standard version and
the CT into records. [DESIGN_CONFORMANCE.md](../kernel/DESIGN_CONFORMANCE.md) lists every variable.

- **`CollectionSpec` records**, one per Concept the design's items record, keyed by the Concept's version-less NodeRef.
  This is `MappingGap.Target`, so a finding here is the mapping-gap finding K-STD-1 accepts. `MAPPED` is `N` when no
  pinned specification maps the Concept.
- **`CollectionItem` records**, one per item, keyed by the item's protocol element (`ProtocolElementId.For(study,
  item)`), which keeps its id across versions (K-PROT-1). For each mapping that covers the item:
  - `UNIT_PROBLEM`: the item's unit has no CT unit term in the specification, or no conversion into the standard unit;
    the mapping has no unit variable for an item with a unit; the mapping standardises into a unit but the item records
    none; or a coded mapping covers an item with a unit. These match what the mapping engine refuses (ADR-2463 §4.5).
  - `TYPE_PROBLEM`: a result or assigned variable is not in the dataset; a result variable has the wrong type (`Num`
    for the standardised numeric result, `Char` for the others); a numeric variable is assigned text; or the dataset
    is not in the package.
  - `CT_PROBLEM`: the item records no codes for a mapping that decodes them; the item's codelist is not the one the
    mapping decodes, or its CT version is not one the specification uses; the mapping's codelist is in none of the
    specification's CT versions; or a CT version the specification uses is not imported.
  - `VALUE_PROBLEM`: an assigned constant is not one of the values the standard lists for its variable.
  - `LENGTH_PROBLEM`: an assigned constant is longer than 200 bytes of UTF-8.
  - `TERM_PROBLEM`: an assigned constant, a unit term or a decoded term is not a term of the codelists the variable
    names, in the specification's CT versions; or none of those versions has the codelists.
  - `PURPOSE` is `N` when K-PROT-3 flags the item.
- Each problem names the specification, its version and the mapping, so the finding explains itself.

### 4.2 The shipped pack

`Cadence.Rules` embeds `design-conformance.json` (`DesignConformancePack`, key `cadence-design-conformance`), verified
against its SHA-256 before it is read. A tenant drafts it as a `RulePack` library item, releases it, and pins it in the
study's standards profile. Every rule applies at stage `Design` and cites its source.

| Rule | Tests | Severity |
| --- | --- | --- |
| DC-001 | `MAPPED = N` on a Concept (K-STD-1) | Error |
| DC-002 | `UNIT_PROBLEM` (SDTMIG v3.4 §4.5.1) | Error |
| DC-003 | `TYPE_PROBLEM` (SDTM v2.0) | Error |
| DC-004 | `CT_PROBLEM` (CDISC CT) | Error |
| DC-005 | `VALUE_PROBLEM` (SDTMIG v3.4) | Error |
| DC-006 | `LENGTH_PROBLEM` (FDA Study Data Technical Conformance Guide) | Error |
| DC-007 | `PURPOSE = N` (K-PROT-3) | Warning |
| DC-008 | `TERM_PROBLEM` (CDISC CT, extensible codelists) | Warning |

DC-008 is a warning because many CDISC codelists are extensible: a sponsor term outside the published codelist can be
conformant, and a person confirms it. A sponsor who wants it to block releases its own pack with an `Error` severity.

### 4.3 The run

`DesignConformanceRunner` (in `Cadence.Rules`) reads the draft through `IProtocolDesignReader`, builds the facts and
evaluates every rule pack the study pins through `ValidationRunner`, recording one `RecordValidationRun` at stage
`Design` over the `CollectionSpec` and `CollectionItem` datasets. The run's inputs are:

- the protocol version's lineage node as it was last saved (`NodeRef.For(version, VersionIndex)`);
- each mapping specification version the study pins.

It runs on drafts only (`K.Protocol.InvalidTransition` otherwise). Findings follow elements across versions, so a run
of an approved or withdrawn version could otherwise resolve a finding a draft still has.

Each is a `ValidatedBy` input of the run, and each pack a `DefinedBy` input (ADR-2462). A run over the same datasets
resolves the findings it no longer detects (K-FIND-1), so fixing the design and running again resolves them
(spec §14 test 10).

### 4.4 The block at submission

`DesignConformanceCheck` is an `IProtocolSubmissionCheck` (ADR-2463 §4.4), registered after `MappingCoverageCheck`.
When the study pins at least one rule pack:

- the latest design-stage run of the study that names the draft's node **as it was last saved** must exist. It must
  have evaluated every pinned rule pack, covered both fact datasets, and named every mapping specification version
  the study pins now. Otherwise the submission is refused with `K.Standards.ConformanceNotRun`. Any save of the draft
  gives its node a new version (a revision, and also a return from review), so a run of an earlier state never counts;
  a change of the study's pins needs a new run too.
- none of the `Error` findings **that run detected** may still be undecided: `Open`, `Investigating`, or `Resolved`.
  Otherwise the submission is refused with `K.Standards.DesignNotConformant`, listing up to ten findings and how many
  more there are. The run read this draft, so a finding it detected can only have been resolved by a run of another
  protocol version, which says nothing about this one. Two amendments forked from one approved version share their
  items, and so their findings; reading only this draft's run keeps one from clearing or raising the other's block.

A study that pins no rule pack, or has no standards profile, submits as before. Accepting or explaining a finding with
an attestation unblocks it; warnings never block.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - A field that cannot produce a conformant SDTM record is found at design time, with the mapping and the reason.
  - Every block is a finding a person can fix, accept with a signature or explain, and a later run resolves it.
  - Sponsors choose severities by releasing their own pack over the same facts.
- **Negative Impact / Technical Debt:**
  - Running conformance is a separate step before submission. A client that submits without it is refused with a
    clear error, not run for.
  - A finding's status is study-wide, so drafts that share an item share its finding: accepting it for one draft
    accepts it for the other, and runs of the two drafts alternately resolve and reopen it.
  - Looking a run up by input reads `validation_run_inputs` by its `input` column, which no index leads with. A study
    has few design-stage runs, so this is cheap now; an index belongs with the next validation migration.
  - The facts cover the checks listed here. Value-level conditions (`ValueLevelDefinition`) and UCUM dimension checks
    are not facts yet.
- **Mitigation Strategy:**
  - A client that submits a design runs conformance first, as two commands; the refusal says which step is missing.
  - New facts are added as new variables; existing packs ignore variables they do not name.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `Cadence.Kernel.Abstractions/Standards/Conformance`: `DesignConformance`, `IDesignTerminology`;
  - `Cadence.Kernel.Abstractions/Protocol`: `IProtocolDesignReader`, implemented by the protocol version reader;
  - `Cadence.Kernel.Abstractions/Validation`: `IValidationFindings.FindLatestRunAsync` and `FindingQuery.DetectedBy`;
  - `Cadence.Kernel.Application/Standards`: `DesignConformanceCheck`; the submission checks now receive a
    `ProtocolSubmission` with the draft's node;
  - `Cadence.Rules/Design`: the pack, `DesignConformancePack` and `DesignConformanceRunner`.
- **Verification Plan:**
  - Unit tests (`DesignConformanceTests`) against the SDTMIG v3.4 seed: a conformant design raises nothing; each of
    DC-001 to DC-008 is raised by the fact it tests; missing CT and a missing package are problems, not passes.
  - PostgreSQL integration tests (`DesignConformancePostgresTests`):
    - spec §14 test 8: a unit with no conformant representation raises DC-002 on the item and blocks submission until
      the finding is accepted;
    - a draft with no run, or revised after its run, is refused; running again resolves the fixed finding, and the run
      links its inputs;
    - a K-PROT-3 warning does not block, and a conformant design raises nothing;
    - of two amendments forked from one approved version, a run of the conformant one resolving the shared finding
      does not let the other through;
    - a run that did not cover both fact datasets, or predates a new mapping specification pin, is refused as not run;
    - unknown versions, versions that are not drafts and studies without standards are refused by the runner.
