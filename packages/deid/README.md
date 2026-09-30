# Cadence.Deid

De-identification library: PII/PHI detection with compliance profiles, value transforms, a reversible air-gap token vault, DICOM de-identification, and redaction manifests.

## Project

- Project: `packages/deid/src/Cadence.Deid.csproj` (assembly `Cadence.Deid`, namespace `Cadence.Deid`).
- `DeidDetector` (with `ComplianceProfile`, `ProfileCategories`, `DetectionResult`, `DetectorCategory`).
- `DeidTransforms`, `DeidAirGapVault`.
- `DicomDeidentifier` (with `DicomDeidResult`, `DicomDeidCertificate`, `PixelMaskBoundingBox`), built on fo-dicom.
- `RedactionManifestBuilder`, `RedactionManifest`, `RedactionRecordItem`.
- Python reference sources remain for behaviour the C# port lacks or does not test yet: `cli.py`, `__main__.py`, `detector.py`, `models.py`, with their `__init__.py` files and `pyproject.toml`. They are not the implementation; [docs/PYTHON_REFERENCE_INVENTORY.md](../../docs/PYTHON_REFERENCE_INVENTORY.md) says what each needs before it goes.

## Key dependencies

- Project reference: `Cadence.Security`.
- Package: `fo-dicom`.

## Referenced by

`packages/compliance`, `tests/Cadence.UnitTests.csproj`, `tests/Cadence.E2ETests.csproj`. No application references it directly.

## Tests

```bash
dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.DeidEngineUnitTests"
```
