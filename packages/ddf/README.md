# Cadence.Ddf

Digital Data Flow compiler. It turns a CDISC USDM study (JSON) into an intermediate representation and renders CDISC ODM XML and OpenRosa XForms from Liquid templates. A kernel `ProtocolVersion` holds a protocol design document, not USDM ([ADR-2472](../../docs/adr/2026-09-30-executable-protocol-design-document.md)), so the compiler takes USDM only.

It also imports a CDISC USDM 4 study into the kernel's protocol design ([ADR-2473](../../docs/adr/2026-09-30-usdm-4-import-into-the-protocol-design.md)):

```csharp
var result = await UsdmDesignImporter.ImportAsync(usdmJson, conceptRegistry, tenant, cancellationToken: ct);
if (result.IsSuccess)
{
    var content = ProtocolDesignDocument.Write(result.Design!);   // the content of a new protocol draft
}

// result.Problems: why there is no design (a Biomedical Concept the tenant lacks, an unsupported version, ...)
// result.Notes: what the design does not carry, by USDM id
```

USDM ids become element ids. Each Biomedical Concept resolves to the tenant's current concept version, and a missing one fails the import. Timings become study days (no day 0) with windows. A study with several versions or designs needs `UsdmImportOptions` to name one.

## Project

- Project: `packages/ddf/src/Cadence.Ddf.csproj` (assembly `Cadence.Ddf`, namespace `Cadence.Ddf`).
- `UsdmCompiler`: `CompileUsdm(usdmJson, studyId, versionTag, ...)`, `ParseUsdmToIr(...)`.
- `UsdmDesignImporter` (with `UsdmImportOptions`, `UsdmImportResult`, `UsdmImportNote`, `BiomedicalConceptResolver`): USDM 4 into a `ProtocolDesign`. `UsdmDesignReader`, `UsdmSchedule` and `UsdmDuration` (`UsdmJson.cs`) are internal.
- `Models.cs`: `DDFStudyIR`, `DDFItemIR`, `DDFCompilationResult`, `CompileOptions`, `CompilerWarning`, `AccessibilityReport`, `ItemLocalization`.
- `Templates/`: `odm_template.xml.liquid` and `openrosa_template.xml.liquid`, embedded into the assembly.
- Python reference sources remain for behaviour the C# port lacks or does not test yet: `compiler.py`, `dataset_json_etl.py`, `ports.py`, `smart_blocks.py`, `tripartite.py`, `rules/`, `stages/`, with their `__init__.py` files and `pyproject.toml`. They are not the implementation; [docs/PYTHON_REFERENCE_INVENTORY.md](../../docs/PYTHON_REFERENCE_INVENTORY.md) says what each needs before it goes.

## Key dependencies

- Project references: `Cadence.Kernel.Abstractions` (see [packages/kernel](../kernel/README.md)).
- Package: `Fluid.Core`.

## Referenced by

[apps/designer](../../apps/designer/README.md) (`POST /api/v1/protocols/compile`), `tests/Cadence.UnitTests/Cadence.UnitTests.csproj` (internals visible), `tests/Cadence.IntegrationTests/Cadence.IntegrationTests.csproj`, `tests/Cadence.E2ETests/Cadence.E2ETests.csproj`.

## Tests

```bash
dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.DdfCompilerTests|FullyQualifiedName~Cadence.UnitTests.Packages.UsdmDesignImporterTests"
```

The import's golden test compares the CDISC example study ([tests/fixtures/usdm](../../tests/fixtures/usdm/README.md)) with a checked-in expected design. After an intended mapping change, rerun it with `CADENCE_UPDATE_GOLDEN=1` and review the diff.
