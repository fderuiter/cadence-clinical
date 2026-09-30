# Indicators

How to write, release and evaluate the indicators behind the Study Pulse, Needs Attention, key risk indicators and
quality tolerance limits. The model is in spec §10.4 and
[ADR-2488](../adr/2026-09-30-study-pulse-metrics-as-versioned-indicators.md); the code is in
[`packages/kernel/src/Cadence.Rules/Indicators`](../../packages/kernel/src/Cadence.Rules/README.md). Indicators use the
expression language of [rule packs](RULE_PACKS.md#expressions).

## Format

An indicator set is a JSON object, stored as the content of a library item of kind `IndicatorSet`:

```json
{
  "authority": "Sponsor",
  "title": "Study Pulse",
  "indicators": [
    {
      "id": "pulse.screen_failure_rate",
      "version": 1,
      "title": "Screen failure rate",
      "area": "Enrollment",
      "kind": "Pulse",
      "population": "enrollments",
      "counts": { "op": "==", "args": [{ "var": "STATUS" }, "ScreenFailed"] },
      "measure": "Percent",
      "threshold": { "direction": "Above", "limit": 30, "severity": "Warning" },
      "explanation": "Participants who failed screening, as a share of everyone screened."
    }
  ]
}
```

| Property | Rule |
| --- | --- |
| `authority` | `Sponsor` or `Study` |
| `id` | Letters, digits, `.`, `_`, `-`; up to 64 characters; unique in the set |
| `version` | 1 or more; raise it whenever the indicator's meaning changes |
| `area` | `Enrollment`, `Data`, `Safety`, `Sites`, `Quality`, `Supply`, `Tmf` or `Submission` |
| `kind` | `Pulse`, `Kri` or `Qtl`; a `Kri` or `Qtl` needs a threshold |
| `population` | The record type counted, as its source names it; same characters as `id` |
| `counts` | An expression that is `true` for each record counted; unknown does not count |
| `measure` | `Count`, or `Percent` of the population (two decimals, no value for an empty population) |
| `threshold` | Optional: `direction` `Above` or `Below`, a numeric `limit`, and a `severity` (`Error`, `Warning`, `Notice`); breached when the value is strictly beyond the limit |
| `effective` | Optional; `from` inclusive and `until` exclusive, compared with the as-of date |

Unknown properties, duplicate IDs, blank or padded text and more than 200 indicators are rejected with the problems
listed.

## Release and pin

1. Send `DraftLibraryItem` with kind `IndicatorSet` and the set as content. Invalid content is refused
   (`K.Standards.LibraryItemInvalid`).
2. Sign an `Approved` attestation over the version's content hash and send `ApproveLibraryItem`. Drafts never load
   (`K.Indicators.SetNotReleased`).
3. Pin the version in the study's `DeclareStudyStandards`. `IndicatorSetLoader.LoadForStudyAsync` returns the pinned
   sets; rule packs pinned in the same profile are left to `RulePackLoader`.

A study changes its Pulse only by pinning another released version, or a Study-authority set of its own; it never
edits an indicator in place (ADR-2488 §4.5).

## Evaluate

```csharp
var results = indicatorEvaluator.Evaluate(
    set,
    new IndicatorContext(NodeRef.For(studyId), asOf),
    new Dictionary<string, IReadOnlyList<IndicatorRecord>> { ["enrollments"] = enrollmentRecords });
```

Give the records of every population the effective indicators name, each once, read within the scope at the as-of.
Each `IndicatorResult` has the value, whether its threshold is breached, up to 100 contributing objects, input and
result hashes, and an `Explanation` from `quality.indicator/<id>@<version>`. Render the explanation as it is; never
compose one in the application (K-EXP-1).

Ordering breached indicators in Needs Attention is the application's choice (ADR-2488 §4.4).
