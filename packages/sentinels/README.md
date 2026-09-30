# Cadence.Sentinels

Small engine for running registered quality-gate checks concurrently and collecting their results.

## Project

- Project: `packages/sentinels/src/Cadence.Sentinels.csproj` (assembly `Cadence.Sentinels`, namespace `Cadence.Sentinels`).
- `SentinelBase.cs`: `ISentinelCheck` and the `SentinelResult` record.
- `SentinelEngine.cs`: `Register(ISentinelCheck)` and `ExecuteAllAsync(targetGate, ct)`.
- Python reference sources remain for behaviour the C# port lacks or does not test yet: `engine.py`, with their `__init__.py` files and `pyproject.toml`. They are not the implementation; [docs/PYTHON_REFERENCE_INVENTORY.md](../../docs/PYTHON_REFERENCE_INVENTORY.md) says what each needs before it goes.

## Key dependencies

None beyond the .NET base library.

## Referenced by

The three test projects only. The CLI's `check` command uses its own `QualityGateOrchestrator` rather than this engine.

## Tests

Covered by `tests/Cadence.UnitTests/Packages/PortedPackagesUnitTests.cs`:

```bash
dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.PortedPackagesUnitTests"
```
