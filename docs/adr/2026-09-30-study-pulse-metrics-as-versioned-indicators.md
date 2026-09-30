# ADR-2488: Study Pulse Metrics and Attention Rules as Versioned Indicators

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Study Pulse (Canvas PX-9) and Needs Attention (PX-10) show numbers that people act on: enrollment, clean data,
open SAEs, active sites, quality signals, TMF and submission readiness. Issue #4845 (Canvas decision D6) records the
problem. If each number is a hard-coded query, nobody can reproduce or explain it later, and UX-7 says a number that
cannot be explained down to objects does not belong on the Pulse.

The kernel already has the parts:

- rules as versioned data, with one expression language and one evaluator port (K-RULE-1..4, ADR-2461);
- governed library items released by attestation and pinned per study (ADR-2460);
- one `Explanation` type (ADR-2482).

Spec §10.4 names `Indicator(kind: KRI | QTL, rule, threshold, scope)` but no format, evaluation or result. Canvas
open question 2 asks how much of the Pulse a study may configure.

## 2. Decision Drivers & Constraints

- **K-QBD-1.** Every QTL and KRI is a Rule evaluated by the same engine over kernel state.
- **K-RULE-3, K-RULE-4.** One evaluator port; the same rule version and inputs give the same result.
- **K-EXP-1, K-EXP-3.** The rule owner produces the explanation from the same evaluation and names its version.
- **UX-7.** Every value drills down to the objects that produce it.
- **Part 11.** A number that informed a decision must be reproducible from the definition version and inputs.
- **Requirements:** PRD-CTMS-007 (compute key risk indicators and flag breached quality tolerance limits) and
  PRD-QLT-004 (configurable KRIs with thresholds).

## 3. Options Considered

### Option 1: Named SQL queries per metric in the web read model

- **Cons:**
  - ❌ Not versioned or attested; a changed query silently changes history.
  - ❌ A second rule engine beside the one the spec requires.

### Option 2: Indicators inside rule packs

- **Cons:**
  - ❌ A rule pack is a validation artifact whose rules produce findings. Mixing metrics into it would make every
    Pulse change a validation release, and `ValidationRunner` would have to skip them.

### Option 3: Indicator sets as their own library item kind, evaluated through the rule evaluator (selected)

- **Pros:**
  - ✅ Versioned, attested and pinned exactly like rule packs, with no new governance.
  - ✅ The counting rule of each indicator runs through `IRuleEvaluator`, so metrics and edit checks share semantics
    and a replacement engine changes both.
- **Cons:**
  - ❌ A second content format beside rule packs; it reuses the expression language, so the difference is small.

## 4. Decision Outcome

**Chosen option: Option 3.** This is kernel decision K026.

### 4.1 Indicator sets

- `LibraryItemKind.IndicatorSet` is a Metadata Registry library item whose content is an indicator set
  ([INDICATORS.md](../kernel/INDICATORS.md)). Drafting runs `IndicatorSetContentValidator`, approval with an
  attestation is the release, and a study pins versions in its standards profile (`DeclareStudyStandards`). A draft
  never loads, and a deprecated set still loads for studies that pinned it. No migration is needed: the kind is
  stored as text.
- An indicator has an id and a version, a title, a Pulse area (`Enrollment`, `Data`, `Safety`, `Sites`, `Quality`,
  `Supply`, `Tmf`, `Submission`), a kind (`Pulse`, `Kri`, `Qtl`), a population, a `counts` expression, a measure
  (`Count` or `Percent`), an optional threshold (direction, limit, severity), an explanation and an effective period.
  KRIs and QTLs must have a threshold.
- A set's authority is `Sponsor` or `Study`. Public authorities publish rules, not Pulse metrics.

### 4.2 Evaluation

- `IndicatorEvaluator.Evaluate(set, context(scope, asOf), populations)` is pure. The caller reads the records of each
  population the effective indicators name, within the scope at the as-of. Each record is an object's `NodeRef` and
  its variables.
- Each indicator becomes one rule of an in-memory counting pack, evaluated through `IRuleEvaluator` once per
  population. A record is counted when the rule's condition is true, with the same three-valued logic as any rule.
  The stage filter of the port plays no part: the counting rules and the context use one fixed stage.
- `Count` is the number counted. `Percent` is the share of the population, rounded to two decimals (half to even),
  with no value when the population is empty. A threshold is breached when the value is strictly above or below its
  limit.
- Records are sorted by `NodeRef` before evaluation. The result is the same in any arrival order, and the same object
  may not appear twice in a population.

### 4.3 Results

An `IndicatorResult` carries:

- the indicator version and the released set it came from (id, key, version, content hash);
- the scope and as-of;
- the population size, the count, the measure, the value and whether the threshold is breached;
- up to 100 contributing objects, sorted: the drill-down of UX-7;
- an input hash over the indicator version, the set's content hash, the scope, the as-of and the records;
- a result hash over the values;
- an `Explanation` produced by `quality.indicator/<id>@<version>`. Its subject is the set's library item version, its
  steps name the records read and the objects counted, and a threshold is an evaluated condition that is unmet
  when breached (K-EXP-1, K-EXP-3).

The kernel never stores a result as truth. A cache or a breach `Signal` (spec §10.4) is a projection recorded with its
input hash.

### 4.4 Needs Attention ranking is application logic

A breached indicator is an attention item with the severity its threshold declares. How Needs Attention orders
those items together with findings, effectivity gaps and proposals (by severity, age, the reader's work queue or
lens) is **application logic over indicator results**, not part of the kernel. A lens may reorder items; it never
hides one the reader may see (UX-6).

### 4.5 What a study may configure (Canvas open question 2)

- **Fixed by the platform:** the eight Pulse areas, the evaluation semantics and the result shape.
- **The sponsor's:** the indicators themselves. A tenant releases a Sponsor-authority set, and its studies pin it.
- **A study's:** which released sets and versions it pins. A study that needs its own thresholds or extra metrics
  releases a Study-authority set and pins it beside, or instead of, the sponsor's. A study never edits an indicator
  in place: a change is a new version, released with an attestation and pinned through an audited profile revision.
  Every Pulse number therefore names the definition that produced it.

### 4.6 Not in this slice

- Population sources (enrollments, findings, observations, sites) and the service that loads a study's pinned sets,
  reads the sources with the reader's authority and masks contributors through `INodeVisibility`. They come with
  the kernel read endpoint for the Pulse.
- Breach `Signal`s and `IndicatorBreached` events (spec §10.4) and time series.

## 5. Consequences & Trade-offs

- **Positive:** a Pulse number is reproducible from its definition version and inputs, explainable down to objects,
  and governed like any other rule. KRIs and QTLs for RBQM use the same machinery.
- **Negative:** the caller must provide complete populations; a missing population is an error, not a zero.
- **Mitigation Strategy:** the population sources are kernel components registered by name, so the service in the
  next slice can check that every population a pinned set names has a source.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/kernel`:
  - `Cadence.Kernel.Abstractions`: `LibraryItemKind.IndicatorSet`.
  - `Cadence.Rules/Indicators`: `Indicator`, `IndicatorSet`, `IndicatorSetJson`, `IndicatorSetLoader`,
    `IndicatorSetContentValidator`, `IndicatorEvaluator` and the result types, registered by `AddCadenceRules()`.
  - `Cadence.Rules/Packs/RulePackJson`: its property readers become internal so both formats read text, enums and
    effective periods the same way.
  - No migration.
- **Verification Plan:**
  - `IndicatorTests`:
    - the format round-trips and refuses bad content;
    - values, percentages and thresholds are computed as specified;
    - contributors and the explanation are correct;
    - hashes are reproducible in any record order and change with a record or the set version;
    - evaluation goes through the `IRuleEvaluator` port;
    - an empty population, effective periods and the contributor cap behave as specified;
    - a missing population or a duplicated object is refused.
  - `IndicatorSetPostgresTests`:
    - a released, pinned set computes KRIs and flags a breached QTL (PRD-CTMS-007);
    - a draft does not load;
    - rule packs and indicator sets load separately from the same pins;
    - invalid content is refused when drafted.
