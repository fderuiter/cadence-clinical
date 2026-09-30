# Cadence.Core.Domain

Shared domain library (folder `engine_core`): aggregate root base types, the `IGxPAuditable` contract and `AuditLogEntry`, a `Result`/`DomainError` type, and pure calculation engines for rules, randomisation, supply and toxicity grading.

## Project

- Project: `packages/engine_core/src/Cadence.Core.Domain.csproj` (assembly `Cadence.Core.Domain`).
- `Cadence.Core.Domain`: `AggregateRoot`, `AggregateRootBase`, `IGxPAuditable`, `Result`, `DomainError`.
- `Cadence.Core.Domain.Audit`: `AuditLogEntry` (the hash-chained audit row written by `Cadence.Database.GxPAuditInterceptor`).
- `Cadence.Core.Domain.Rules`: rule AST (`ExpressionNode`, `FieldReference`, `RuleDefinition`) and `RuleEvaluator`.
- `Cadence.Core.Domain.Randomization`: `BlockRandomizationEngine`, `MinimizationEngine`, `StochasticRandomizationEngine`, `BoinEngine` and their models.
- `Cadence.Core.Domain.Supply`: `DTPLogisticsEngine`, `MultiEchelonHierarchyEngine`, `PredictiveDemandForecastingEngine` and supply models. The emergency unblinding shapes are `[Obsolete]`: Execution's `UNBLINDED` state change is the only unblinding path (ADR-2520).
- `Cadence.Core.Domain.Sla`: `SlaEngine` (due dates around the clock or in business hours, countdown clocks, escalation tiers) and `SlaPolicy` with its checksum.
- `Cadence.Core.Domain.Toxicity`: `LabToxicityGradingEngine` and CTCAE grading models.
- Time (K-TIME-3, #4784): nothing here reads the wall clock, and the build fails on `DateTime.Now/UtcNow` (`CadenceBanWallClock`). The caller passes the instant: `RuleEvaluator(asOf)`, `EvaluateObservation(..., evaluatedAt)`, `EvaluateHysLaw(..., evaluatedAt)`, `EvaluateDtpDispatch(..., authorizedAt)` and `SlaEngine.ComputeCountdownClock(..., asOf)`. `AggregateRootBase.CreatedAt` has no default, because the save path stamps it. `AuditLogEntry.Timestamp` and `RuleDefinition.CreatedAt` are `required`.
- Python reference sources remain for behaviour the C# port lacks or does not test yet: `derived/`, `operations/`, `rules/ast.py`, `rules/sentinels.py`, `timeline/`, with their `__init__.py` files and `pyproject.toml`. `fsm/` is superseded by the kernel's aggregate state machines and waits only for a maintainer to approve its deletion, and so do `financial/`, ported to `apps/payments` (ADR-2521), `sla/`, ported to `Cadence.Core.Domain.Sla`, and `supply/`, ported or not carried forward (ADR-2520). They are not the implementation; [docs/PYTHON_REFERENCE_INVENTORY.md](../../docs/PYTHON_REFERENCE_INVENTORY.md) says what each needs before it goes.

## Key dependencies

Package: `FluentValidation`.

## Referenced by

Every .NET service, `packages/contracts`, `packages/database`, `packages/kernel` (`Cadence.Kernel.Domain`), `packages/testing`, `packages/cli`, and all three test projects.

## Tests

```bash
dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.DomainEngineUnitTests|FullyQualifiedName~BoinEngineTests|FullyQualifiedName~RuleEvaluatorTests|FullyQualifiedName~PredictiveDemandForecastingEngineTests|FullyQualifiedName~SlaEngineTests|FullyQualifiedName~MultiEchelonHierarchyEngineTests|FullyQualifiedName~Cadence.UnitTests.Packages.RuleChecksumTests|FullyQualifiedName~Cadence.UnitTests.SecurityAndDomainTests"
```

`BoinEngineTests`, `RuleEvaluatorTests` and `PredictiveDemandForecastingEngineTests` pin the ports to the deleted
`randomization/boin.py`, `rules/evaluator.py` and `supply/forecasting.py` (#4789), and `SlaEngineTests` pins the
port of `sla/` (#5048). Known differences they pin rather than hide:

- `RuleEvaluator`: `==` treats a numeric string as a number and compares text ignoring case; `True == 1` is false;
  the string `"false"` is falsy; divisors below 1e-9 count as zero; integer results of `sum`, `floor`, `ceil`, `abs`,
  `min` and `max` come back as `double`. JSONLogic `min` and `max` are unsupported in both languages.
- `BoinEngine`: a configured boundary of `0.0` is kept (the Python replaced it with the calculated one).
- `PredictiveDemandForecastingEngine`: parameters are validated when the engine runs, with the Python model's
  limits; a null schedule or lot list falls back to the defaults.
- `SlaEngine`: the caller passes the instant a clock is read at, dates are `DateTimeOffset` rather than parsed
  strings, and a business-hours schedule with no work day or with hours that end before they start is refused
  (the Python loop never ended on one).
- `MultiEchelonHierarchyEngine` (`MultiEchelonHierarchyEngineTests`, pinned to `supply/hierarchy.py` with the same
  error messages): node capacity and quantities are validated when the engine runs, with the Python model's limits.
