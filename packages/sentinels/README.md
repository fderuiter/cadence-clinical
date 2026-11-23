# Cadence.Sentinels

Small engine for running registered quality-gate checks concurrently and
collecting their results.

## Project

- Project: `packages/sentinels/src/Cadence.Sentinels.csproj` (assembly
  `Cadence.Sentinels`, namespace `Cadence.Sentinels`).
- `SentinelBase.cs`: `ISentinelCheck` and the `SentinelResult` record.
- `SentinelEngine.cs`: `Register(ISentinelCheck)` and
  `ExecuteAllAsync(targetGate, ct)`.
- No Python reference sources remain; `SentinelEngineTests` covers the port of
  the deleted `engine.py` (#4789). Unlike the Python, the gate filter ignores
  case, and a failing check's exception surfaces after every check has finished.

## Key dependencies

None beyond the .NET base library.

## Referenced by

The three test projects only. The CLI's `check` command uses its own
`QualityGateOrchestrator` rather than this engine.

## Tests

Covered by `tests/Cadence.UnitTests/Packages/SentinelEngineTests.cs` and
`PortedPackagesUnitTests.cs`:

```bash
dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~SentinelEngineTests|FullyQualifiedName~PortedPackagesUnitTests"
```
