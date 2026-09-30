# Cadence.Standards

Loads public standards content into the kernel's Standards Registry (spec §6.3, [ADR-2460](../../../../docs/adr/2026-09-30-standards-registry-and-mdr-as-governed-tenant-reference-data.md)).
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

## Changing a seed

Seed bytes are checked exactly (`.gitattributes` marks them `-text`, so line endings are never rewritten). After
editing a seed, update its `sha256` in `Seed/manifest.json` (`sha256sum Seed/<file>`). A changed seed has a new
content hash, so it must be registered as a new standard version, never over an existing one.
