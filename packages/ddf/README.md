# Cadence.Ddf

Digital Data Flow compiler. It turns a CDISC USDM study (JSON) or a kernel `ProtocolVersion` into an intermediate representation and renders CDISC ODM XML and OpenRosa XForms from Liquid templates.

## Project

- Project: `packages/ddf/src/Cadence.Ddf.csproj` (assembly `Cadence.Ddf`, namespace `Cadence.Ddf`).
- `UsdmCompiler`: `Compile(ProtocolVersion, ...)`, `CompileUsdm(usdmJson, studyId, versionTag, ...)`, `ParseUsdmToIr(...)`.
- `Models.cs`: `DDFStudyIR`, `DDFItemIR`, `DDFCompilationResult`, `CompileOptions`, `CompilerWarning`, `AccessibilityReport`, `ItemLocalization`.
- `Templates/`: `odm_template.xml.liquid` and `openrosa_template.xml.liquid`, embedded into the assembly.
- Python reference sources remain in this folder until each C# port is verified (issue #4789): `compiler.py`, `models.py`, `stages/`, `rules/`, `dataset_json_etl.py`, `smart_blocks.py`, `tripartite.py`, `ports.py`, `pyproject.toml`. They are not the implementation.

## Key dependencies

- Project references: `Cadence.Kernel.Abstractions`, `Cadence.Kernel.Domain` (see [packages/kernel](../kernel/README.md)).
- Package: `Fluid.Core`.

## Referenced by

[apps/designer](../../apps/designer/README.md) (`POST /api/v1/protocols/compile`), `tests/Cadence.UnitTests.csproj`, `tests/Cadence.E2ETests.csproj`.

## Tests

```bash
dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.DdfCompilerTests"
```
