# Design-time conformance

A field that cannot produce a conformant standard record should be found when the protocol is designed, not at
database lock. Design-time conformance (K-STD-2, spec §6.3) checks a draft protocol design against the study's mapping
specifications, standard versions and controlled terminology, and blocks submitting the draft for review while an
`Error` finding is open. The decision is
[ADR-2464](../adr/2026-09-30-design-time-conformance-as-rule-packs-over-design-facts.md) (K030).

## Lifecycle

1. Set up the study as for [mapping specifications](MAPPING_SPECIFICATIONS.md#lifecycle): import the CT, register and
   approve the standard version, and approve the mapping specifications.
2. Release a design-stage rule pack. `Cadence.Rules` ships one: draft `DesignConformancePack.Content` with
   `DraftLibraryItem` (kind `RulePack`, key `DesignConformancePack.Key`, `cadence-design-conformance`), then approve it
   with an `Approved` attestation ([RULE_PACKS.md](RULE_PACKS.md#release-and-use)). A sponsor may release its own pack
   over the same facts instead, for example with different severities.
3. Pin the pack and the mapping specifications in `DeclareStudyStandards`.
4. Draft the protocol version (`CreateProtocolDraft`, `ReviseProtocolDraft`).
5. Run conformance on the draft:

   ```csharp
   var outcome = await designConformance.RunAsync(
       new DesignConformanceRequest(tenant, protocolVersion, idempotencyKey, "Design review"));
   ```

   The run evaluates every rule pack the study pins and records one validation run at stage `Design` (ADR-2462).
   Read its findings with `IValidationFindings.ListAsync(tenant, new FindingQuery(study) { Stage = ValidationStage.Design })`.
6. Fix the design and run again, or accept each finding you will not fix with `AcceptFinding` and a `Responsibility`
   attestation. A run resolves the findings it no longer detects.
7. Submit the protocol version with `SubmitProtocolVersionForReview`.

## What blocks submission

For a study that pins at least one rule pack, `SubmitProtocolVersionForReview` is refused:

- with `K.Standards.ConformanceNotRun` when no design-stage run names the draft as it was last saved, or the latest
  such run did not evaluate every pinned rule pack, cover both fact datasets, or use every mapping specification
  version the study pins now. Any save of the draft (a revision, or a return from review) and any change of the pins
  needs a new run;
- with `K.Standards.DesignNotConformant` when an `Error` finding that run detected is `Open`, `Investigating` or
  `Resolved`. Only the draft's own run counts: a finding resolved by a run of another protocol version still blocks
  this one. The message lists up to ten findings, with their pack, rule, target and message.

Conformance runs on drafts only. Warnings, accepted and explained findings never block. A study with no standards profile, or one that pins no rule
pack, submits as before. K-STD-1 ([mapping coverage](MAPPING_SPECIFICATIONS.md#k-std-1-mapping-before-first-data-capture))
is checked first.

## Design facts

`DesignConformance.BuildAsync` writes two record types. Each is keyed by a version-less NodeRef, so a finding follows
its element across protocol versions and an acceptance holds until the finding recurs after being resolved.

### `CollectionSpec`: one record per collected Concept

Keyed by the Concept (`MappingGap.Target(concept)`), so DC-001's finding is the mapping-gap finding K-STD-1 accepts.

| Variable | Value |
| --- | --- |
| `CONCEPT` | The Concept's NodeRef |
| `ITEMS` | The ids of the items that record it, comma-separated |
| `MAPPED` | `Y` when a pinned mapping specification maps it, otherwise `N` |

### `CollectionItem`: one record per CollectionSpec item

Keyed by the item's protocol element (`ProtocolElementId.For(study, itemId)`).

| Variable | Value |
| --- | --- |
| `ITEM`, `SPEC` | The item's id and its CollectionSpec's id |
| `CONCEPT` | The Concept reference, with its version |
| `UNIT`, `CODELIST` | The item's UCUM unit and codelist, when it has them |
| `MAPPED` | `Y` when a mapping covers the item's Concept |
| `PURPOSE` | `N` when the item has no path to an endpoint, estimand, safety rule or operational need (K-PROT-3) |
| `UNIT_PROBLEM` | The unit has no CT unit term, or no conversion into the standard unit; the mapping has no unit variable to write it to; the mapping standardises into a unit but the item records none; or a coded mapping covers an item with a unit |
| `TYPE_PROBLEM` | A result or assigned variable is missing from the dataset, or a result variable has the wrong type (`Num` for the standardised numeric result, `Char` for the others); a numeric variable is assigned text; or the dataset is not in the standard's package |
| `CT_PROBLEM` | The item records no codes for a mapping that decodes them; its codelist is not the one the mapping decodes, or its CT version is not one the specification uses; the mapping's codelist is in none of the specification's CT versions; or a CT version the specification uses is not imported |
| `VALUE_PROBLEM` | An assigned constant is not one of the values the standard lists for the variable (for example `DOMAIN`) |
| `LENGTH_PROBLEM` | An assigned constant is longer than 200 bytes of UTF-8 |
| `TERM_PROBLEM` | An assigned constant, a unit term or a decoded term is not a term of the variable's codelists in the specification's CT versions, or none of those versions has the codelists |

A problem variable is null when there is no problem. Otherwise it names the specification, its version and the
mapping, for example `sdtm-vitals v1 mapping vs-sysbp: the unit cm[H2O] has no controlled-terminology unit term in the
specification.` Several problems are joined with `; `. A fact that cannot be checked, such as a codelist that no
imported CT version holds, is a problem, never a pass.

## The shipped pack

`packages/kernel/src/Cadence.Rules/Design/design-conformance.json`, verified against `DesignConformancePack.Sha256`
when it is loaded.

| Rule | Condition | Severity | Source |
| --- | --- | --- | --- |
| DC-001 | `CollectionSpec.MAPPED = N` | Error | Clinical Kernel Specification §6.3 K-STD-1 |
| DC-002 | `UNIT_PROBLEM` is not empty | Error | CDISC SDTMIG v3.4 §4.5.1 |
| DC-003 | `TYPE_PROBLEM` is not empty | Error | CDISC SDTM v2.0 |
| DC-004 | `CT_PROBLEM` is not empty | Error | CDISC Controlled Terminology |
| DC-005 | `VALUE_PROBLEM` is not empty | Error | CDISC SDTMIG v3.4 |
| DC-006 | `LENGTH_PROBLEM` is not empty | Error | FDA Study Data Technical Conformance Guide |
| DC-007 | `PURPOSE = N` | Warning | Clinical Kernel Specification §6.2 K-PROT-3 |
| DC-008 | `TERM_PROBLEM` is not empty | Warning | CDISC Controlled Terminology (extensible codelists) |

DC-008 warns rather than blocks because many CDISC codelists are extensible, so a sponsor term can be conformant. To
change the embedded pack, edit the JSON, update `Sha256` and raise the changed rules' `version`.
