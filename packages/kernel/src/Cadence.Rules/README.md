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

## Registration

`services.AddCadenceRules()` registers `IRuleEvaluator`, `RulePackLoader`, the content validator and the scoped
`ValidationRunner`. `RulePackLoader` needs `IStandardsRegistry`, which `AddKernelStandardsPersistence()` provides;
`ValidationRunner` also needs the kernel's `IKernelCommandSender` and `IClock`, and the kernel host registers the
validation handlers with `AddKernelValidation()` and `AddKernelValidationPersistence()`.

## Benchmark

[`packages/kernel/benchmarks/Cadence.Rules.Benchmarks`](../../benchmarks/Cadence.Rules.Benchmarks/README.md) measures
an `IRuleEvaluator` over a seeded synthetic dataset; the baseline is in ADR-2461 §4.5.
