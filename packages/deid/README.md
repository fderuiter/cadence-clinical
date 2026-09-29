# Cadence.Deid

De-identification library: PII/PHI detection with compliance profiles, value transforms, a reversible air-gap token vault, DICOM de-identification, and redaction manifests.

## Project

- Project: `packages/deid/src/Cadence.Deid.csproj` (assembly `Cadence.Deid`, namespace `Cadence.Deid`).
- `DeidDetector` (with `ComplianceProfile`, `ProfileCategories`, `DetectionResult`, `DetectorCategory`).
- `DeidTransforms`, `DeidAirGapVault`.
- `DicomDeidentifier` (with `DicomDeidResult`, `DicomDeidCertificate`, `PixelMaskBoundingBox`), built on fo-dicom.
- `RedactionManifestBuilder`, `RedactionManifest`, `RedactionRecordItem`.
- Python reference sources remain in this folder until each C# port is verified (issue #4789): `detector.py`, `transforms.py`, `air_gap.py`, `dicom.py`, `manifest.py`, `ner_scrubber.py`, `cli.py`, `__main__.py`, `models.py`, `pyproject.toml`. They are not the implementation.

## Key dependencies

- Project reference: `Cadence.Security`.
- Package: `fo-dicom`.

## Referenced by

`packages/compliance`, `tests/Cadence.UnitTests.csproj`, `tests/Cadence.E2ETests.csproj`. No application references it directly.

## Tests

```bash
dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.DeidEngineUnitTests"
```
