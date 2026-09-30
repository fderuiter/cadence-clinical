# Cadence.Sentinels

Small engine for running registered quality-gate checks concurrently and collecting their results.

## Project

- Project: `packages/sentinels/src/Cadence.Sentinels.csproj` (assembly `Cadence.Sentinels`, namespace `Cadence.Sentinels`).
- `SentinelBase.cs`: `ISentinelCheck` and the `SentinelResult` record.
- `SentinelEngine.cs`: `Register(ISentinelCheck)` and `ExecuteAllAsync(targetGate, ct)`.
- Python reference sources remain in this folder until each C# port is verified (issue #4789): `base.py`, `engine.py`, `__init__.py`, `pyproject.toml`. They are not the implementation.

## Key dependencies

None beyond the .NET base library.

## Referenced by

The three test projects only. The CLI's `check` command uses its own `QualityGateOrchestrator` rather than this engine.

## Tests

Covered by `tests/Packages/PortedPackagesUnitTests.cs`:

```bash
dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.PortedPackagesUnitTests"
```
