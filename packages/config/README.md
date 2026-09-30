# Cadence.Config

Strongly typed options classes for the services and shared packages, bindable with `Microsoft.Extensions.Options`.

## Project

- Project: `packages/config/src/Cadence.Config.csproj` (assembly `Cadence.Config`, namespace `Cadence.Config`).
- `CommonOptions.cs`: `CommonOptions`.
- `ServiceOptions.cs`: one options class per service (`GatewayOptions`, `ExecutionOptions`, `DesignerOptions`, `CtmsOptions`, `EtmfOptions`, and so on) plus `SecurityOptions`, `TelemetryOptions`, `StorageOptions`, `DeidOptions` and `ComplianceOptions`.
- Python reference sources remain in this folder until each C# port is verified (issue #4789): `settings.py`, `__init__.py`, `pyproject.toml`. They are not the implementation.

## Key dependencies

`Microsoft.Extensions.Options`, `Microsoft.Extensions.Options.ConfigurationExtensions`, `Microsoft.Extensions.Configuration.Binder`.

## Referenced by

`tests/Cadence.UnitTests.csproj` and `tests/Cadence.ArchitectureTests.csproj` only. No application binds these options yet; services read configuration keys directly.

## Tests

```bash
dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.ConfigAndCoreModelsUnitTests"
```
