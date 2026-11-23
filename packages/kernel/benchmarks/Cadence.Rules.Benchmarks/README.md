# Cadence.Rules.Benchmarks

Measures an `IRuleEvaluator` (spec §10.1, K-RULE-3) over a seeded, streamed
synthetic dataset with a 12-rule pack that uses every operator and function of
the rule language.

```bash
dotnet run -c Release --project packages/kernel/benchmarks/Cadence.Rules.Benchmarks -- [records] [runs] [seed]
```

Defaults: 1,000,000 records, 3 measured runs after a warm-up, seed 20260930.
Each run prints elapsed time, records per second, allocated memory, the number
of violations and a SHA-256 over the violations. The program exits with 1 if two
runs give different hashes (K-RULE-4).

The baseline for `DotNetRuleEvaluator` is in
[ADR-2461 §4.5](../../../../docs/adr/2026-09-30-rule-expression-language-rule-pack-format-and-evaluator-port.md).
A replacement evaluator must give the same hash for the same records and seed.
`tests/Cadence.UnitTests/Kernel/Rules/RuleBenchmarkTests.cs` keeps the harness
reproducible.
