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
- Python reference sources remain for behaviour the C# port lacks or does not test yet: `derived/`, `financial/`, `fsm/`, `operations/`, `randomization/boin.py`, `rules/`, `sla/`, `supply/`, `timeline/`, with their `__init__.py` files and `pyproject.toml`. They are not the implementation; [docs/PYTHON_REFERENCE_INVENTORY.md](../../docs/PYTHON_REFERENCE_INVENTORY.md) says what each needs before it goes.

## Key dependencies

Package: `FluentValidation`.

## Referenced by

Every .NET service, `packages/contracts`, `packages/database`, `packages/kernel` (`Cadence.Kernel.Domain`), `packages/testing`, `packages/cli`, and all three test projects.

## Tests

```bash
dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.DomainEngineUnitTests|FullyQualifiedName~Cadence.UnitTests.Packages.RuleChecksumTests|FullyQualifiedName~Cadence.UnitTests.SecurityAndDomainTests"
```
