# Cadence.Core.Domain

Shared domain library (folder `engine_core`): aggregate root base types, the `IGxPAuditable` contract and `AuditLogEntry`, a `Result`/`DomainError` type, and pure calculation engines for rules, randomisation, supply and toxicity grading.

## Project

- Project: `packages/engine_core/src/Cadence.Core.Domain.csproj` (assembly `Cadence.Core.Domain`).
- `Cadence.Core.Domain`: `AggregateRoot`, `AggregateRootBase`, `IGxPAuditable`, `Result`, `DomainError`.
- `Cadence.Core.Domain.Audit`: `AuditLogEntry` (the hash-chained audit row written by `Cadence.Database.GxPAuditInterceptor`).
- `Cadence.Core.Domain.Rules`: rule AST (`ExpressionNode`, `FieldReference`, `RuleDefinition`) and `RuleEvaluator`.
- `Cadence.Core.Domain.Randomization`: `BlockRandomizationEngine`, `MinimizationEngine`, `StochasticRandomizationEngine`, `BoinEngine` and their models.
- `Cadence.Core.Domain.Supply`: `DTPLogisticsEngine`, `PredictiveDemandForecastingEngine` and supply models.
- `Cadence.Core.Domain.Toxicity`: `LabToxicityGradingEngine` and CTCAE grading models.
- Python reference sources remain in this folder until each C# port is verified (issue #4789): `derived/`, `financial/`, `fsm/`, `operations/`, `randomization/`, `rules/`, `sla/`, `supply/`, `timeline/`, `toxicity/`, `tests/`, `pyproject.toml`. They are not the implementation, and several of those modules (for example `financial/`, `fsm/`, `sla/`, `timeline/`) have no C# port yet.

## Key dependencies

Package: `FluentValidation`.

## Referenced by

Every .NET service, `packages/contracts`, `packages/database`, `packages/kernel` (`Cadence.Kernel.Domain`), `packages/testing`, `packages/cli`, and all three test projects.

## Tests

```bash
dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.DomainEngineUnitTests|FullyQualifiedName~Cadence.UnitTests.Packages.RuleChecksumTests|FullyQualifiedName~Cadence.UnitTests.SecurityAndDomainTests"
```
