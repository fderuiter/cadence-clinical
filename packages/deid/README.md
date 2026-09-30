# Cadence.Deid

De-identification library: PII/PHI detection with compliance profiles, value transforms, a reversible air-gap token vault, DICOM de-identification, and redaction manifests.

## Project

- Project: `packages/deid/src/Cadence.Deid.csproj` (assembly `Cadence.Deid`, namespace `Cadence.Deid`).
- `DeidDetector` (with `ComplianceProfile`, `ProfileCategories`, `DetectionResult`, `DetectorCategory`).
- `DeidTransforms`, `DeidAirGapVault`.
- `DicomDeidentifier` (with `DicomDeidResult`, `DicomDeidCertificate`, `PixelMaskBoundingBox`), built on fo-dicom.
- `RedactionManifestBuilder`, `RedactionManifest`, `RedactionRecordItem`.
- No Python reference sources remain (#4789). The repository scanner `cli.py` became the `check deid` gate (`packages/tooling_core/src/Services/DeidComplianceService.cs`, [docs/cli.md](../../docs/cli.md#cadence-check)).

## Key dependencies

- Project reference: `Cadence.Security`.
- Package: `fo-dicom`.

## Referenced by

`packages/compliance`, `packages/tooling_core` (the `deid` gate), `tests/Cadence.UnitTests/Cadence.UnitTests.csproj`, `tests/Cadence.E2ETests/Cadence.E2ETests.csproj`. No application references it directly.

## Tests

```bash
dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.DeidEngineUnitTests|FullyQualifiedName~DeidDetectorTests"
```
