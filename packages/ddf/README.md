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

USDM ids become element ids. Each Biomedical Concept resolves to the tenant's current concept version, and a missing one fails the import. Timings become SDTM study days (no day 0; a `P0D` `Fixed Reference` baseline normalizes to day 1) with windows. A study with several versions or designs needs `UsdmImportOptions` to name one.

## Project

- Project: `packages/ddf/src/Cadence.Ddf.csproj` (assembly `Cadence.Ddf`, namespace `Cadence.Ddf`).
- `UsdmCompiler`: `CompileUsdm(usdmJson, studyId, versionTag, options, templateRenderer, accessibilityValidator)`, `ParseUsdmToIr(...)`. Unless `CompileOptions.ValidateAccessibility` is off, every rendered OpenRosa form is checked and a form that fails throws `AccessibilityValidationException` instead of being returned ([ADR-2501](../../docs/adr/2026-09-30-the-ddf-compiler-refuses-forms-that-fail-the-accessibility-check.md)). Text that is not JSON, or a value with the wrong JSON type (an array for the study, a number for a name), throws `UsdmFormatException`, which a service maps to `400` (#5040).
- Item rules (`DdfRules.cs`, `RuleXPathCompiler.cs`, [ADR-2502](../../docs/adr/2026-09-30-ddf-item-rules-use-the-one-rule-language.md)): an item's `rules` array holds `skip_logic` rules (`action` `show` or `hide`) and `constraint` rules (with a `query_message`), each with a `Cadence.Rules` condition whose variables are item ids. `RuleXPathCompiler` compiles them to the form's `relevant` and `constraint`; `DdfDependencyGraph` finds skip-logic cycles; `DdfIdentifier` makes item names into XML element names the same way every time. A rule that does not parse, names an unknown item or makes a cycle, a duplicate item, or the old raw `constraint` string fails the compile with `DdfCompilationException`, which lists every problem. A column width of 1 to 4 (`cols`, `column_span` or `span`) becomes the Enketo appearance `w1` to `w4`.
- Item text (`ItemText` in `DdfRules.cs`): an item's `label` (default: its own name), `description` and `hint` are in `CompileOptions.DefaultLanguage`; `localizations` adds languages as `{"fr": {"label": "…", "hint": "…", "description": "…"}}`. The form gets one label and hint per language; the ODM `ItemDef` gets a `Question` and, when there are descriptions, a `Description`. A missing study id or name, or an item without an id, is filled in and reported in `Warnings`.
- `IAccessibilityValidator`, `AccessibilityStatus`, `AccessibilityValidationException` (`Accessibility.cs`) and `XFormsAccessibilityValidator`: the accessibility check. The built-in validator is a structural WCAG 2.1 review of the XForm (title, labels, label languages, choice labels, and every control bound once); pass another `IAccessibilityValidator` to replace it.
- `UsdmDesignImporter` (with `UsdmImportOptions`, `UsdmImportResult`, `UsdmImportNote`, `BiomedicalConceptResolver`): USDM 4 into a `ProtocolDesign`. `UsdmDesignReader`, `UsdmSchedule` and `UsdmDuration` (`UsdmJson.cs`) are internal.
- `OdmDesignImporter`, `CdiscLibraryTemplateCatalog`, and `ProtocolDesignImporterService`: synchronous ODM v1.3.2 XML and USDM v4 JSON import into `ProtocolDesign` (mapping item datatypes, UCUM units, requiredness, and terminology bindings via `ProtocolTerminologyResolver`), bundled CDISC Library eCRF template catalog (`docs/CDISC/Library/Data_Collection/eCRFs`), 10 MB payload enforcement, DTD/XXE blocking, and readable `ProtocolImportNote` generation.
- `ProtocolFormCompiler`: implements `IProtocolFormCompiler` to compile `ProtocolDesign.CollectionSpecs` into OpenRosa XForm XML and CDISC ODM v1.3.2 XML snapshots when a `ProtocolVersion` is approved.
- `Models.cs`: `DDFStudyIR`, `DDFItemIR`, `DDFCompilationResult`, `CompileOptions`, `CompilerWarning`, `AccessibilityReport`, `ItemLocalization`.
- `Templates/`: `odm_template.xml.liquid` and `openrosa_template.xml.liquid`, embedded into the assembly.
- No Python reference sources remain (#4789).

## Key dependencies

- Project references: `Cadence.Kernel.Abstractions` and `Cadence.Rules` (see [packages/kernel](../kernel/README.md)).
- Package: `Fluid.Core`.

## Referenced by

[apps/designer](../../apps/designer/README.md) (`POST /api/v1/protocols/compile`), [apps/kernel-api](../../apps/kernel-api/README.md) (`POST /api/v1/kernel/studies/{studyRef}/protocol-imports`), `tests/Cadence.UnitTests/Cadence.UnitTests.csproj` (internals visible), `tests/Cadence.IntegrationTests/Cadence.IntegrationTests.csproj`, `tests/Cadence.E2ETests/Cadence.E2ETests.csproj`.

## Tests

```bash
dotnet test --project tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.DdfCompilerTests|FullyQualifiedName~Cadence.UnitTests.Packages.XFormsAccessibilityValidatorTests|FullyQualifiedName~Cadence.UnitTests.Packages.DdfRulesTests|FullyQualifiedName~Cadence.UnitTests.Packages.UsdmDesignImporterTests"
```

The import's golden test compares the CDISC example study ([tests/fixtures/usdm](../../tests/fixtures/usdm/README.md)) with a checked-in expected design. After an intended mapping change, rerun it with `CADENCE_UPDATE_GOLDEN=1` and review the diff.
