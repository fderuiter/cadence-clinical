# Cadence.Rules

Rules as versioned data (spec §10.1, [ADR-2461](../../../../docs/adr/2026-09-30-rule-expression-language-rule-pack-format-and-evaluator-port.md)).
It references `Cadence.Kernel.Abstractions` only, so the kernel and any application can evaluate rules. How to write
and release a pack: [docs/kernel/RULE_PACKS.md](../../../../docs/kernel/RULE_PACKS.md).

## Contents

- `Expressions/`: the expression language. `RuleExpressionJson` parses and writes the canonical JSON form (and reads
  the shapes existing EDC edit checks use), `RuleExpressionCompiler` compiles an expression to a delegate, and
  `RuleValue` is the null, boolean, number or text value it works on.
- `Packs/`: `Rule` and `RulePack`, the strict `RulePackJson` reader and writer, `RulePackContentValidator` (validates
  `RulePack` library items when they are drafted) and `RulePackLoader`, which loads released packs from the Standards
  Registry by library item or by a study's pins.
- `Evaluation/`: the `IRuleEvaluator` port and `DotNetRuleEvaluator`, which compiles each pack once and streams
  records. A Rust runtime can replace it behind the same port (K-RULE-3). `ValidationRunner` evaluates a study's
  pinned packs and records the result as a kernel validation run (`RecordValidationRun`, ADR-2462).
- `Validation/`: pre-validation static analyzers (`CircularDependencyAnalyzer` over target variables and
  `ConflictingConditionAnalyzer` over overlapping applicability and interval domains) and `ParallelBatchRuleValidator`,
  which evaluates packs in parallel while preserving canonical `LoadedRulePack.Key` finding identity (K-FIND-1) and
  deterministic `(pack, record, rule)` detection ordering (K-RULE-4).
- `Amendment/`: `AmendmentRegressionEngine`, which diffs rule versions across all rule metadata, validates AST variable
  references against authoritative `StandardPackage` dataset schemas (even on empty record sets), and highlights newly
  triggered or resolved findings.
- `Indicators/`: Study Pulse metrics, attention rules, KRIs and QTLs as versioned indicators
  ([ADR-2488](../../../../docs/adr/2026-09-30-study-pulse-metrics-as-versioned-indicators.md),
  [docs/kernel/INDICATORS.md](../../../../docs/kernel/INDICATORS.md)). `IndicatorSetJson` reads and writes
  `IndicatorSet` library item content, `IndicatorSetContentValidator` validates it when drafted, `IndicatorSetLoader`
  loads released sets by id or by a study's pins, and `IndicatorEvaluator` counts each population through
  `IRuleEvaluator` and returns values with hashes, contributing objects and an explanation. `StudyIndicators` evaluates
  a study's pinned sets over the populations its registered `IIndicatorPopulationSource`s read, leaving out the
  records the reader may not see (`INodeVisibility`) and marking such values partial.

## Registration

`services.AddCadenceRules()` registers `IRuleEvaluator`, `RulePackLoader`, `IndicatorSetLoader`, `IndicatorEvaluator`,
the two content validators and the scoped `ValidationRunner` and `StudyIndicators`. `StudyIndicators` also needs
`INodeVisibility` and the population sources, which `AddKernelPersistence()` and `AddKernelValidationPersistence()`
register. `RulePackLoader` needs `IStandardsRegistry`, which `AddKernelStandardsPersistence()` provides;
`ValidationRunner` also needs the kernel's `IKernelCommandSender` and `IClock`, and the kernel host registers the
validation handlers with `AddKernelValidation()` and `AddKernelValidationPersistence()`.

## Benchmark

[`packages/kernel/benchmarks/Cadence.Rules.Benchmarks`](../../benchmarks/Cadence.Rules.Benchmarks/README.md) measures
an `IRuleEvaluator` over a seeded synthetic dataset; the baseline is in ADR-2461 §4.5.
