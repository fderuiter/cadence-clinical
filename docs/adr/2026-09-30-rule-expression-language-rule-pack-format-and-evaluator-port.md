# ADR-2461: Rule Expression Language, Rule-Pack Format and the IRuleEvaluator Port

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-004 (declarative rules and multi-layer edit checks), PRD-QRY-003 (cross-form edit check execution)

---

## 1. Context & Problem Statement

[ADR-2254](2026-09-29-rules-as-versioned-data-and-unified-rules-engine.md) (K008) decided that rules are versioned
data over one expression language, evaluated behind a narrow port so a Rust runtime can replace the .NET one later
(spec §10.1, K-RULE-1 to K-RULE-4). [ADR-2460](2026-09-30-standards-registry-and-mdr-as-governed-tenant-reference-data.md)
(K013) gave the kernel a governed library whose items include rule packs. Neither says what a rule looks like on
disk, what its expressions mean, or where the evaluator lives (issue #4811).

The only rule engine in code is `AstEditCheckEngine` in `apps/execution`. It accepts several JSON shapes for the same
node, silently treats unknown shapes as false, evaluates `FUNCTION` nodes through a branch that can never be reached,
and has no pack format, versioning or citation. Before findings (#4812), design-time conformance (#4813), the SDTM
projection (#4814) and the move of EDC edit checks onto the port (#4728) can be built, five questions need an answer:

1. What is the expression language, and what exactly does each operator mean for missing and mixed-type values?
2. What is the stored rule-pack format, and what does the platform refuse to store?
3. How is a rule pack governed, released and pinned by a study?
4. Where does the evaluator live, and what is the port a Rust runtime would implement?
5. What is the performance baseline a replacement engine must beat?

## 2. Decision Drivers & Constraints

- **K-RULE-1:** adding or changing a rule is a data change on a running platform, never a release.
- **K-RULE-2:** packs implement public standards only; the platform should refuse obvious proprietary content.
- **K-RULE-3:** one language for edit checks, conformance, sponsor and study rules; the existing EDC rules must parse.
- **K-RULE-4:** same rule versions and same inputs give the same violations, under any culture and in any process.
- **K-XS-2:** applications reference `Cadence.Kernel.Abstractions` only; the rules assembly must follow the same rule
  so an application can evaluate rules without kernel persistence.
- **Replaceability:** the port must be narrow enough that a Rust implementation needs only the stored JSON forms.

## 3. Options Considered

### Option 1: A small JSON expression language with fully specified semantics, packs as MDR library items, evaluator in `Cadence.Rules` (selected)

- **Overview:** one canonical grammar (`{"var": …}`, `{"op": …, "args": […]}` and JSON literals) with Kleene
  three-valued logic and fixed coercions; a strict pack schema; packs stored as `LibraryItem`s of kind `RulePack` so
  governance, attestation, hashing and study pinning come from ADR-2460; `IRuleEvaluator` in a new assembly that
  references Abstractions only.
- **Pros:** the grammar is the storage format, so a second engine reads the same bytes; every edge case is decided
  once and tested; no new tables or migration; applications can use it.
- **Cons:** less expressive than a general language; cross-record (dataset-level) rules need a later extension.

### Option 2: Embed an existing expression engine (CEL, JsonLogic, NCalc)

- **Pros:** more operators out of the box.
- **Cons:** semantics for null, mixed types and culture differ per library and are hard to pin for K-RULE-4; a Rust
  replacement would have to reproduce a third party's corner cases; JsonLogic truthiness does not match clinical
  missing-data handling.

### Option 3: Keep rules as C# checks and version the assembly

- **Cons:** violates K-RULE-1 (every rule change is a release) and gives no citation or applicability model.

## 4. Decision Outcome

**Chosen Option:** Option 1.

### 4.1 Expression language

- Values are null, boolean, number (`decimal`) and text. A variable a record lacks is null.
- `and`, `or`, `not` use Kleene logic: a non-boolean operand is unknown; `false and unknown` is false,
  `true or unknown` is true, otherwise unknown. A rule fires only when its condition is exactly `true`.
- Comparisons are never unknown. Null equals only null; ordering against null is false. Both sides compare as numbers
  when both coerce (text parses with the invariant culture); booleans compare for equality only; everything else
  compares as ordinal text.
- `+ - * /` give null for a non-number, overflow or division by zero. `is_empty` is true for null or blank text,
  `in`/`not_in` use `==`, `length` counts Unicode scalar values.
- The parser also reads the shapes existing EDC rules use (`operator`/`operands`, `node_type`, `&&`, `||`, `!`,
  `empty`) and writes only the canonical form. It rejects XPath, field references and indexed repeats, and limits
  depth to 64, size to 4,096 nodes and text to 4,000 characters.

### 4.2 Rule-pack format and K-RULE-2

A pack is `{authority, title, rules[]}`; each rule has `id`, `version` (≥ 1), `authority`, `citation` (`document`,
optional `section`, optional http(s) `uri`), `applicability` (`stage`, optional `standard`, `standardVersion`,
`agency`, `dataset`), `severity` (`Error`, `Warning`, `Notice`), `condition`, `explanation`, optional `remediation`
and optional `effective` (`from` inclusive, `until` exclusive). The reader is strict: unknown properties, duplicate
rule IDs, padded or over-long text, a standard version without a standard, and an empty effective period are
rejected with every problem listed. A citation naming a proprietary validator (Pinnacle 21, OpenCDISC) is rejected:
this is a guard, not a proof, and review of each pack's citations remains the control for K-RULE-2.

### 4.3 Governance

A rule pack is a `LibraryItem` of kind `RulePack` (ADR-2460). `DraftLibraryItem` validates its content through
`ILibraryContentValidator`, which moves to `Cadence.Kernel.Abstractions` so the rules assembly can provide one;
`ApproveLibraryItem` with the approver's `Approved` attestation over the version's content hash is the release. `RulePackLoader` loads Approved and Deprecated
versions and refuses drafts; `LoadForStudyAsync` loads the versions a study's standards profile pins, so deprecating
a pack never changes a study that already uses it.

### 4.4 Evaluator port

`IRuleEvaluator.Evaluate(RulePack, RuleEvaluationContext, IEnumerable<RuleRecord>)` returns `RuleViolation`s in
record order, then rule order. The context carries the stage, the as-of date and optional standard, standard version
and agency; a rule applies when every value it names matches exactly and the date is inside its effective period.
`DotNetRuleEvaluator` compiles each pack once and streams records. The port and the stored JSON forms are the whole
contract for a Rust runtime. Each violation carries the rule version, the explanation and the observed values, so
#4812 can turn it into a `ValidationFinding` whose `Explanation` is produced from the same evaluation
([ADR-2482](2026-09-30-explanation-model-produced-by-the-rule-owner.md), K-EXP-1, producer
`quality.finding/<rule-id>@<version>`).

### 4.5 Performance baseline

`packages/kernel/benchmarks/Cadence.Rules.Benchmarks` runs a 12-rule pack that uses every operator over a seeded,
streamed synthetic dataset. On the development container (Release, one thread) one million records took about 4.2 s
per run (about 239,000 records per second), allocated about 1.34 GiB and produced 1,560,796 violations with the same
result hash on every run. A replacement engine is justified only by beating this on the same harness with the same
hash.

## 5. Consequences & Trade-offs

- **Positive Impact:** rules ship as governed, attested, hash-identified data; the semantics a second engine must
  match are written down and tested, including a property test over 200 generated packs.
- **Negative Impact / Technical Debt:** EDC edit checks still run on `AstEditCheckEngine` until #4728; rules see one
  record at a time, so cross-record and cross-dataset rules need a later extension of the port.
- **Mitigation Strategy:** the parser already reads the EDC shapes, and tests show the two engines agree on them, so
  #4728 is a switch of evaluator, not a rewrite of rules. A dataset-level extension will be an additive port method.

## 6. Implementation & Verification

- **Affected Repositories / Services:** new `packages/kernel/src/Cadence.Rules` and
  `packages/kernel/benchmarks/Cadence.Rules.Benchmarks`; `ILibraryContentValidator` moves from
  `Cadence.Kernel.Application` to `Cadence.Kernel.Abstractions`.
- **Verification Plan:**
  - unit tests: round trips, EDC shapes and equivalence with `AstEditCheckEngine`, rejections and limits, Kleene
    truth tables, comparisons and coercions, the pack schema, applicability and ordering, and a K-RULE-4 property
    test (repeated, per record, after a stored round trip, under four cultures);
  - PostgreSQL integration tests: a pack drafted and released at runtime loads and evaluates, a changed rule arrives
    as a new version, invalid content is refused when drafted, and a study pin loads its version after deprecation;
  - architecture tests: `Cadence.Rules` references only `Cadence.Kernel.Abstractions`;
  - benchmark tests: the harness is reproducible and every benchmark rule that can fire does.
