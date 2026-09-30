# Cadence.Ddf

Digital Data Flow compiler. It turns a CDISC USDM study (JSON) or a kernel `ProtocolVersion` into an intermediate representation and renders CDISC ODM XML and OpenRosa XForms from Liquid templates.

## Project

- Project: `packages/ddf/src/Cadence.Ddf.csproj` (assembly `Cadence.Ddf`, namespace `Cadence.Ddf`).
- `UsdmCompiler`: `Compile(ProtocolVersion, ...)` (compiles `Content`, version tag `v{Number}`), `CompileUsdm(usdmJson, studyId, versionTag, ...)`, `ParseUsdmToIr(...)`.
- `Models.cs`: `DDFStudyIR`, `DDFItemIR`, `DDFCompilationResult`, `CompileOptions`, `CompilerWarning`, `AccessibilityReport`, `ItemLocalization`.
- `Templates/`: `odm_template.xml.liquid` and `openrosa_template.xml.liquid`, embedded into the assembly.
- Python reference sources remain for behaviour the C# port lacks or does not test yet: `compiler.py`, `dataset_json_etl.py`, `ports.py`, `smart_blocks.py`, `tripartite.py`, `rules/`, `stages/`, with their `__init__.py` files and `pyproject.toml`. They are not the implementation; [docs/PYTHON_REFERENCE_INVENTORY.md](../../docs/PYTHON_REFERENCE_INVENTORY.md) says what each needs before it goes.

## Key dependencies

- Project references: `Cadence.Kernel.Abstractions`, `Cadence.Kernel.Domain` (see [packages/kernel](../kernel/README.md)).
- Package: `Fluid.Core`.

## Referenced by

[apps/designer](../../apps/designer/README.md) (`POST /api/v1/protocols/compile`), `tests/Cadence.UnitTests.csproj`, `tests/Cadence.E2ETests.csproj`.

## Tests

```bash
dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.DdfCompilerTests"
```
