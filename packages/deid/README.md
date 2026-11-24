# Cadence.Deid

De-identification library: PII/PHI detection with compliance profiles and streaming DICOM header/pixel de-identification with signed certificates.

## Project

- Project: `packages/deid/src/Cadence.Deid.csproj` (assembly `Cadence.Deid`, namespace `Cadence.Deid`).
- `DeidDetector` (with `ComplianceProfile`, `ProfileCategories`, `DetectionResult`, `DetectorCategory`).
- `DicomDeidentifier` (with `DicomDeidResult`, `DicomSliceDeidResult`, `DicomDeidCertificate`, `PixelMaskBoundingBox`, and `PooledBlockStream`), built on fo-dicom. `Deidentify`, `DeidentifySlice`, and `DeidentifyPreloadedSlice` reject null or whitespace `studyId`, `siteId`, or `subjectId` arguments and bind anonymized `StudyInstanceUID`, `SeriesInstanceUID`, `SOPInstanceUID`, and `FrameOfReferenceUID` values to length-prefixed `(studyId, siteId, subjectId)` salts under the pseudonymization key. `DeidentifySlice` / `DeidentifyPreloadedSlice` stream slice de-identification over `ArrayPool<byte>.Shared` sub-LOH blocks (`65,536` bytes) and compute source/target SHA-256 digests and de-identified byte lengths without retaining output slice buffers.
- No Python reference sources remain (#4789). The repository scanner `cli.py` became the `check deid` gate (`packages/tooling_core/src/Services/DeidComplianceService.cs`, [docs/cli.md](../../docs/cli.md#cadence-check)).
- Keys (#5016, #5399): there are no default keys. `new DicomDeidentifier(signingKey, pseudonymizationKey)` needs two different keys of at least 32 bytes each: the signing key signs certificates (and, through `SignPayload`/`VerifyPayload`, a host's manifests), and the pseudonymization key keys the anonymized DICOM UIDs (`GenerateUid`) and the original-UID hashes (`HashOriginalUid`) with HMAC-SHA256, so nobody without it can link a de-identified study back to an original UID. Anything shorter or missing throws. The keys are secrets the host supplies from configuration, because anyone who knows them can forge certificates or recompute pseudonyms.
- Time (K-TIME-3): the project bans `DateTime.Now/UtcNow`. `DicomDeidentifier.Deidentify(..., deidentifiedAt)` takes the instant from the caller.

## Key dependencies

- Project reference: `Cadence.Security`.
- Package: `fo-dicom`.

## Referenced by

`apps/imaging/src/Cadence.Apps.Imaging.csproj`, `packages/tooling_core` (the `deid` gate), `tests/Cadence.UnitTests/Cadence.UnitTests.csproj`, `tests/Cadence.E2ETests/Cadence.E2ETests.csproj`.

## Tests

```bash
dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.DeidEngineUnitTests|FullyQualifiedName~DeidDetectorTests"
```
