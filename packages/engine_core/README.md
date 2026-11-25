# Cadence.Core.Domain

Shared domain library (folder `engine_core`): aggregate root and state machine base types, the `IGxPAuditable` contract, `AuditLogEntry` and `AuditLogChainVerifier`, a `Result`/`DomainError` type, and pure domain engines for randomisation, CGT supply verification and toxicity grading.

## Project

- Project: `packages/engine_core/src/Cadence.Core.Domain.csproj` (assembly `Cadence.Core.Domain`).
- `Cadence.Core.Domain`: `AggregateRoot`, `AggregateRootBase`, `IGxPAuditable`, `Result`, `DomainError`.
- `Cadence.Core.Domain.Audit`: `AuditLogEntry` (the hash-chained audit row written by `Cadence.Database.GxPAuditInterceptor`) and `AuditLogChainVerifier`.
- `Cadence.Core.Domain.StateMachine`: `StatefulAggregateRoot`, `StateMachineDefinition`, transition records and transactional outbox message types.
- `Cadence.Core.Domain.Randomization`: `BlockRandomizationEngine`, `MinimizationEngine`, `ResponseAdaptiveEngine`, `StochasticRandomizationEngine`, `BoinEngine`, internal `RandomizationMath` helpers and their models.
- `Cadence.Core.Domain.Supply`: `CgtBarcodeEngine.VerifyCryptoBarcode`, `Isbt128Engine` and `TemperatureZoneClassifier.ClassifyZone` (`TemperatureZone`).
- `Cadence.Core.Domain.Toxicity`: `LabToxicityGradingEngine` and CTCAE grading models.
- Time (K-TIME-3, #4784): nothing here reads the wall clock, and the build fails on `DateTime.Now/UtcNow` (`CadenceBanWallClock`). The caller passes the instant: `EvaluateObservation(..., evaluatedAt)` and `EvaluateHysLaw(..., evaluatedAt)`. `AggregateRootBase.CreatedAt` has no default, because the save path stamps it. `AuditLogEntry.Timestamp` is `required`.
- No Python reference sources remain (#4789, [ADR-2509](../../docs/adr/2026-11-24-retire-remaining-python-references-and-scanner-residue.md)); visit-window schedules and timing assessments live on the kernel's `ExpectedSchedule` ([ADR-2487](../../docs/adr/2026-09-30-expected-schedule-as-a-pure-function.md), [ADR-2507](../../docs/adr/2026-09-30-visit-timing-anchored-visits-and-cycles-on-the-expected-schedule.md)).

## Key dependencies

Package: `FluentValidation`.

## Referenced by

Every .NET service, `packages/database`, `packages/kernel` (`Cadence.Kernel.Domain`), `packages/testing`, `packages/cli`, and all three test projects.

## Tests

```bash
dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.DomainEngineUnitTests|FullyQualifiedName~BoinEngineTests|FullyQualifiedName~ResponseAdaptiveEngineTests|FullyQualifiedName~CgtEngineTests|FullyQualifiedName~StateMachineTests|FullyQualifiedName~Cadence.UnitTests.SecurityAndDomainTests"
```

`BoinEngineTests` pins the port to the deleted `randomization/boin.py` (#4789), including a known difference it pins rather than hides:

- `BoinEngine`: a configured boundary of `0.0` is kept (the Python replaced it with the calculated one).
