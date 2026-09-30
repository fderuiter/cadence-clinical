# Cadence.Standards

Loads public standards content into the kernel's Standards Registry (spec §6.3, [ADR-2460](../../../../docs/adr/2026-09-30-standards-registry-and-mdr-as-governed-tenant-reference-data.md)),
and provides the .NET mapping engine behind `IMappingEngine` ([ADR-2463](../../../../docs/adr/2026-09-30-mapping-specifications-as-governed-concept-to-variable-maps.md)).
It references `Cadence.Kernel.Abstractions` only, so any host or application can use it; registration itself goes
through the kernel command `RegisterStandardVersion`.

## Contents

- `CdiscLibrary/CdiscLibraryProductReader`: reads a CDISC Library product response (contract in
  [`docs/CDISC/cdisc-library-api.json`](../../../../docs/CDISC/cdisc-library-api.json)) into a `StandardPackage`.
  It supports implementation guides (`classes → datasets → datasetVariables`, or `domains`/`fields` for CDASHIG)
  and ADaM products (`dataStructures → analysisVariableSets → analysisVariables`). Codelist links such as
  `/mdr/ct/packages/sdtmct-2024-09-27/codelists/C66731` become the codelist code on the variable and the CT binding
  `(sdtmct, 2024-09-27)` on the package. Unknown data types or core values are rejected, never guessed.
- `ChecksumVerifier`: verifies content against a published SHA-256 before it is parsed. Use it for downloaded
  Library content as well as the shipped seeds.
- `StandardSeeds`: the seeds below, embedded in the assembly and verified against `Seed/manifest.json` on load.
- `Blocks/`: reusable collection specifications, or form blocks
  ([ADR-2503](../../../../docs/adr/2026-09-30-reusable-form-blocks-are-collection-specification-library-items.md)).
  - `CollectionSpecificationJson` reads and checks the content of a `CollectionSpecification` library item.
  - `CollectionSpecificationContentValidator` runs that check when `DraftLibraryItem` drafts one.
  - `FormBlocks` loads the shipped blocks, verified against `Blocks/manifest.json`.
  - `CollectionSpecificationInstantiator` resolves a block's Biomedical Concept codes and terminology in a tenant's
    Concept Registry and builds a protocol design `CollectionSpec`.
- `AddCadenceStandards()`: registers the collection specification validator. A host that drafts library items calls it
  next to `AddKernelStandards()` and `AddCadenceRules()`.
- `Mapping/MappingEngine`: applies a mapping specification to one collected value and returns the record fragment
  (constant variables, original result and unit, standardised result, CT decodes). It never guesses: a missing
  mapping, unit, conversion, unit term or decode, text that is neither a number nor a bound such as `<1.0`, or a
  conversion out of range is a problem. Register it with `services.AddCadenceMapping()`;
  register another `IMappingEngine` first to replace it. The format and the rules are in
  [Mapping specifications](../../../../docs/kernel/MAPPING_SPECIFICATIONS.md).

## Seeds

| File | Standard | Content | CT binding |
| --- | --- | --- | --- |
| `Seed/sdtmig-3-4.json` | SDTMIG v3.4 | DM, AE, VS, LB (192 variables) | `sdtmct 2024-09-27` |
| `Seed/adamig-1-3.json` | ADaMIG v1.3 | ADSL (141 variables) | `adamct 2024-09-27` |

Provenance: the variable metadata comes from the CDISC workbook exports in
[`docs/CDISC/Library/Data_Tabulation/SDTMIG_v3.4.json`](../../../../docs/CDISC/Library/Data_Tabulation/SDTMIG_v3.4.json)
and [`docs/CDISC/Library/DataAnalysis/ADaMIG_v1.3.json`](../../../../docs/CDISC/Library/DataAnalysis/ADaMIG_v1.3.json),
reshaped into the CDISC Library product format. The workbooks name codelists by C-code without a package version;
the seeds pin them to the 2024-09-27 CT packages in `docs/CDISC/Library/Terminology`, because the registry never
binds "the latest" terminology (spec §6.1). The seeds contain only public CDISC content (K-RULE-2).

## Form blocks

| File | Library item key | Items |
| --- | --- | --- |
| `Blocks/vital-signs.json` | `cadence.vital-signs` | Systolic (C25298) and diastolic (C25299) blood pressure in `mm[Hg]`, pulse rate (C49676) in `{beats}/min`, respiratory rate (C49678) in `{breaths}/min`, body temperature (C174446) in `Cel` |

The concept codes are the VSTESTCD terms (codelist C66741) of SDTM CT 2024-09-27 in
`docs/CDISC/Library/Terminology/SDTM_CT_2024-09-27.xlsx`. A block holds what to collect only. Its SDTM mapping belongs
to a mapping specification, its layout to form design, and its plausibility ranges to rule-pack edit checks.

To add a block, add the file under `Blocks/`, list it in `Blocks/manifest.json` with its `sha256`, and add it to the
table above. Block bytes are checked exactly, like the seeds.

## Changing a seed

Seed bytes are checked exactly (`.gitattributes` marks them `-text`, so line endings are never rewritten). After
editing a seed, update its `sha256` in `Seed/manifest.json` (`sha256sum Seed/<file>`). A changed seed has a new
content hash, so it must be registered as a new standard version, never over an existing one.
