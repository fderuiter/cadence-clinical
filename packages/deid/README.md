# Cadence.Deid

De-identification library: PII/PHI detection with compliance profiles, value transforms, a reversible air-gap token vault, DICOM de-identification, and redaction manifests.

## Project

- Project: `packages/deid/src/Cadence.Deid.csproj` (assembly `Cadence.Deid`, namespace `Cadence.Deid`).
- `DeidDetector` (with `ComplianceProfile`, `ProfileCategories`, `DetectionResult`, `DetectorCategory`).
- `DeidTransforms`, `DeidAirGapVault`.
- `DicomDeidentifier` (with `DicomDeidResult`, `DicomSliceDeidResult`, `DicomDeidCertificate`, `PixelMaskBoundingBox`, and `PooledBlockStream`), built on fo-dicom. `DeidentifySlice` / `DeidentifyPreloadedSlice` stream slice de-identification over `ArrayPool<byte>.Shared` sub-LOH blocks (`65,536` bytes) and compute source/target SHA-256 digests and de-identified byte lengths without retaining output slice buffers.
- `RedactionManifestBuilder`, `RedactionManifest`, `RedactionRecordItem`.
- No Python reference sources remain (#4789). The repository scanner `cli.py` became the `check deid` gate (`packages/tooling_core/src/Services/DeidComplianceService.cs`, [docs/cli.md](../../docs/cli.md#cadence-check)).
- Keys (#5016, #5399): there are no default keys. `new DicomDeidentifier(signingKey, pseudonymizationKey)` needs two different keys of at least 32 bytes each: the signing key signs certificates (and, through `SignPayload`/`VerifyPayload`, a host's manifests), and the pseudonymization key keys the anonymized DICOM UIDs (`GenerateUid`) and the original-UID hashes (`HashOriginalUid`) with HMAC-SHA256, so nobody without it can link a de-identified study back to an original UID. The `pseudonymize` strategy of `DeidTransforms.ApplyDeidTransforms`, `PseudonymizeValue` and the subject date shift need an HMAC key of at least 32 bytes. Anything shorter or missing throws. The keys are secrets the host supplies from configuration, because anyone who knows them can forge certificates or recompute pseudonyms.
- Time (K-TIME-3): the project bans `DateTime.Now/UtcNow`. `DicomDeidentifier.Deidentify(..., deidentifiedAt)` and `RedactionManifestBuilder.BuildRedactionManifest(..., recordedAt)` take the instant from the caller.

## Key dependencies

- Project reference: `Cadence.Security`.
- Package: `fo-dicom`.

## Referenced by

`apps/imaging/src/Cadence.Apps.Imaging.csproj`, `packages/tooling_core` (the `deid` gate), `tests/Cadence.UnitTests/Cadence.UnitTests.csproj`, `tests/Cadence.E2ETests/Cadence.E2ETests.csproj`.

## Tests

```bash
dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.DeidEngineUnitTests|FullyQualifiedName~DeidDetectorTests"
```
