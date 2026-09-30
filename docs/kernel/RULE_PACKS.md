# Rule Packs

How to write, release and run a rule pack. The model is in spec §10.1 and
[ADR-2461](../adr/2026-09-30-rule-expression-language-rule-pack-format-and-evaluator-port.md); the code is in
[`packages/kernel/src/Cadence.Rules`](../../packages/kernel/src/Cadence.Rules/README.md).

## What a rule pack may contain (K-RULE-2)

A rule pack implements a **public** standard or regulatory document: CDISC conformance rules, FDA or PMDA validator
rules and technical conformance guides, the Define-XML specification, controlled terminology, or a sponsor's or
study's own requirements. Every rule cites its source document, and the citation must let a reviewer find the
requirement. Never copy a vendor's rule implementations, rule IDs or messages. The reader refuses citations that
name Pinnacle 21 or OpenCDISC, but that is only a guard: the reviewer who approves the pack is the control.

## Format

A pack is a JSON object, stored as the content of a library item of kind `RulePack`:

```json
{
  "authority": "Sponsor",
  "title": "Vital signs plausibility",
  "rules": [
    {
      "id": "VS-001",
      "version": 1,
      "citation": { "document": "Sponsor data standards manual", "section": "4.2" },
      "applicability": { "stage": "Standardization", "standard": "SDTMIG", "standardVersion": "3.4", "dataset": "VS" },
      "severity": "Error",
      "condition": { "op": "<", "args": [{ "var": "SYSBP" }, { "var": "DIABP" }] },
      "explanation": "Systolic blood pressure is lower than diastolic.",
      "remediation": "Check the two results against the source.",
      "effective": { "from": "2026-01-01" }
    }
  ]
}
```

| Property | Rule |
| --- | --- |
| `authority` | `CDISC`, `FDA`, `PMDA`, `NMPA`, `EMA`, `Sponsor` or `Study`; a rule without one takes the pack's |
| `id` | Letters, digits, `.`, `_`, `-`; up to 64 characters; unique in the pack |
| `version` | 1 or more; raise it whenever the rule's meaning changes |
| `citation` | `document` required; `section` and an http(s) `uri` optional |
| `applicability.stage` | `Design`, `Collection`, `Standardization`, `Analysis` or `Submission` (spec §10.2) |
| `applicability` others | Optional; a rule applies only when each value it names equals the run's value exactly |
| `severity` | `Error`, `Warning` or `Notice` |
| `condition` | An expression that is `true` when the record **violates** the rule |
| `effective` | Optional; `from` is inclusive and `until` exclusive, compared with the run's as-of date |

Unknown properties, duplicate IDs, blank, padded or over-long text (2,000 characters) and more than 5,000 rules are
rejected, and the error lists the problems (up to 50).

## Expressions

An expression is a JSON literal (`null`, `true`, `false`, a number or a string), a variable `{"var": "NAME"}`, or an
operator `{"op": "…", "args": […]}`.

| Operators | Meaning |
| --- | --- |
| `and`, `or`, `not` | Three-valued (Kleene) logic; a non-boolean operand counts as unknown |
| `==`, `!=`, `<`, `<=`, `>`, `>=` | Numeric when both sides are numbers or numeric text, otherwise ordinal text; booleans support `==` and `!=` only |
| `+`, `-`, `*`, `/` | Decimal arithmetic; null on a non-number, overflow or division by zero |
| `is_empty`, `is_not_empty` | Null or blank text is empty |
| `in`, `not_in` | First argument compared with `==` to each of the others |
| `length` | Length of the text in Unicode characters |

Points that surprise people:

- A variable the record does not have is null. `null == null` is true; `null < 5` and `null > 5` are both false. Guard
  a required value with `is_empty` rather than relying on a comparison.
- A rule fires only when its condition is exactly `true`; unknown does not fire.
- Numbers are decimals and text numbers are parsed with the invariant culture, so results never depend on the server's
  locale.

Existing EDC edit checks (`operator`/`operands`, `node_type`, `&&`, `||`, `!`, `empty`) are read too and written back
in the canonical form.

## Release and use

1. Send `DraftLibraryItem` with kind `RulePack` and the pack as content. Invalid content is refused
   (`K.Standards.LibraryItemInvalid`) with the problems listed.
2. The approver signs an `Approved` attestation (`Attest`) over the version's content hash, then sends
   `ApproveLibraryItem` with it. That approval is the release; drafts never load (`K.Rules.PackNotReleased`).
3. A study pins the version in `DeclareStudyStandards`. `RulePackLoader.LoadForStudyAsync` loads the pinned versions,
   even after deprecation.
4. Evaluate with `IRuleEvaluator.Evaluate(pack, new RuleEvaluationContext(stage, asOf) { Standard = …, … }, records)`,
   or validate and record the result in one step with `ValidationRunner.RunAsync(new ValidationRequest(...))`: it
   evaluates every pack the study pins and sends one `RecordValidationRun`, whose findings `IValidationFindings` reads
   (ADR-2462). Stages and severities are the kernel's `ValidationStage` and `ValidationSeverity`.

To change a rule, raise its `version`, draft the pack again (the library item's next version) and release it. Studies
keep their pinned version until their standards profile is revised.

## Benchmark

```bash
dotnet run -c Release --project packages/kernel/benchmarks/Cadence.Rules.Benchmarks -- [records] [runs] [seed]
```

It runs a 12-rule pack over a seeded synthetic dataset (1,000,000 records by default) and prints time, throughput,
allocation and a hash of the violations; the run fails if two runs differ. The baseline is in ADR-2461 §4.5. A new
evaluator, such as a Rust runtime, is compared on this harness and must produce the same hash.
