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
- Time (K-TIME-3, #4784): nothing here reads the wall clock, and the build fails on `DateTime.Now/UtcNow` (`CadenceBanWallClock`). The caller passes the instant: `RuleEvaluator(asOf)`, `EvaluateObservation(..., evaluatedAt)`, `EvaluateHysLaw(..., evaluatedAt)` and `EvaluateDtpDispatch(..., authorizedAt)`. `AggregateRootBase.CreatedAt` has no default, because the save path stamps it. `AuditLogEntry.Timestamp` and `RuleDefinition.CreatedAt` are `required`.
- Python reference sources remain for behaviour the C# port lacks or does not test yet: `derived/`, `operations/`, `rules/ast.py`, `rules/sentinels.py`, `sla/`, `supply/` (all but `forecasting.py`), `timeline/`, with their `__init__.py` files and `pyproject.toml`. `fsm/` is superseded by the kernel's aggregate state machines and waits only for a maintainer to approve its deletion, and so does `financial/`, ported to `apps/payments` (ADR-2521). They are not the implementation; [docs/PYTHON_REFERENCE_INVENTORY.md](../../docs/PYTHON_REFERENCE_INVENTORY.md) says what each needs before it goes.

## Key dependencies

Package: `FluentValidation`.

## Referenced by

Every .NET service, `packages/contracts`, `packages/database`, `packages/kernel` (`Cadence.Kernel.Domain`), `packages/testing`, `packages/cli`, and all three test projects.

## Tests

```bash
dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.DomainEngineUnitTests|FullyQualifiedName~BoinEngineTests|FullyQualifiedName~RuleEvaluatorTests|FullyQualifiedName~PredictiveDemandForecastingEngineTests|FullyQualifiedName~Cadence.UnitTests.Packages.RuleChecksumTests|FullyQualifiedName~Cadence.UnitTests.SecurityAndDomainTests"
```

`BoinEngineTests`, `RuleEvaluatorTests` and `PredictiveDemandForecastingEngineTests` pin the ports to the deleted
`randomization/boin.py`, `rules/evaluator.py` and `supply/forecasting.py` (#4789). Known differences they pin rather
than hide:

- `RuleEvaluator`: `==` treats a numeric string as a number and compares text ignoring case; `True == 1` is false;
  the string `"false"` is falsy; divisors below 1e-9 count as zero; integer results of `sum`, `floor`, `ceil`, `abs`,
  `min` and `max` come back as `double`. JSONLogic `min` and `max` are unsupported in both languages.
- `BoinEngine`: a configured boundary of `0.0` is kept (the Python replaced it with the calculated one).
- `PredictiveDemandForecastingEngine`: parameters are validated when the engine runs, with the Python model's
  limits; a null schedule or lot list falls back to the defaults.
