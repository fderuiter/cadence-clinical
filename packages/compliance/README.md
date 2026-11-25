# Cadence.Compliance

Part 11 support library: audit digest calculation and hash-chain verification, e-signature and PKCS#7 signing and verification, and requirements traceability matrix generation.

## Project

- Project: `packages/compliance/src/Cadence.Compliance.csproj` (assembly `Cadence.Compliance`, namespace `Cadence.Compliance`).
- Main types: `AuditDigestCalculator`, `AuditChainVerifier`, `ComplianceAuditLogRecord`, `ESignatureVerifier`, `GxPSigner`, `Pkcs7Signer`, `GxpSnapshotPolicy`, `RequirementTagScanner`, `RtmGeneratorService`, and the exceptions `TamperDetectedException` and `UnapprovedAIRecordException`.
- No Python reference sources remain; `ESignatureVerifierTests` pins the verifier to the deleted `services/esignature_verifier.py` (#4789).

## Signature verification

`ESignatureVerifier.VerifySignature` returns `VALID` only when the RSA-PSS or ECDSA (SHA-256, DER-encoded) signature over the
document and the embedded certificate verifies, and the certificate is not on the revocation list. Otherwise the status names the failure: `TAMPERED_INVALID_HASH`, `DUPLICATE_BLOCKS_REJECTED`,
`REVOKED_CERTIFICATE`, `LEGACY_PADDING_REJECTED` (PKCS#1 v1.5), `UNSUPPORTED_KEY_TYPE`, or `MOCK_SIGNATURE_DETECTED`
when a failed document contains the word "mock". The word never rejects a valid signature, because base64
certificates and signatures contain it by chance. The revocation list takes PEM certificates, SHA-256 fingerprints (plain, or separated by colons, hyphens or
spaces), SHA-1 thumbprints and serial numbers (decimal, `0x` hex or plain hex, compared by value); revocation is
checked after the signature, so a tampered document reports tampering. Known differences from the deleted Python:
the status is `REVOKED_CERTIFICATE` (Python `CERTIFICATE_REVOKED`), there are no trust-store checks, and the AI-record
approval helpers are not ported (spec §11 Proposals supersedes them). `VerifyMarkdown` also checks the SHA-256 in the signature block
against the document body. Only tests call the verifier today; no CLI command verifies signatures.

## Generated GxP documents

`RtmGeneratorService` writes the RTM and the IQ/OQ/PQ report with `GxPSigner.StampGxpMarkdown`, a deterministic
SHA-256 integrity stamp marked as an unsigned working copy, and `GxPSigner.VerifyStamp` lets `check rtm` reject
hand edits. `GxPSigner.SignGxpMarkdown` (RSA-PSS with an embedded certificate) remains for signing a release copy
with a named person's certificate; without one it falls back to a throwaway self-signed key, which is only useful
in tests. [docs/TESTING.md](../../docs/TESTING.md) has the workflow.

`GxpSnapshotPolicy` (ADR-2532) names the snapshot paths (the two documents and `docs/SDLC/runs/`), the preview
directory `TestResults/gxp/preview` that `gxp sync` writes on a feature branch, `CADENCE_GXP_SNAPSHOT`, which turns
on snapshot mode, and `SnapshotTrailer` (`Gxp-Snapshot: refreshed`), which `gxp snapshot --commit` appends so CI only
exports `CADENCE_GXP_SNAPSHOT=1` on repository-owner PRs, `merge_group` runs, and post-merge pushes carrying that trailer.
`GenerateRtm(requireCurrentSnapshot: …)` checks that the committed RTM's test mapping matches the
tags only in snapshot mode, so a branch with new or moved tags passes `check rtm` without committing a new snapshot.

`RequirementTagScanner` scans C#, Vitest, and Playwright test files across `RtmGeneratorService.TestSearchDirs`
(`tests/`, `apps/web/tests/`, `apps/subject-portal/tests/`, and test directories under `packages/`), extracting per-test
requirement tags (`[GxPRequirement("PRD-…")]` and method/test-level `@req:PRD-…`) while rejecting file-level,
`describe`-level, class-level, and restricted requirement tags. `RtmGeneratorService.ParseTestResults` merges xUnit
`.trx` reports with Vitest and Playwright JUnit XML reports while preserving PostgreSQL evidence rules. On `refs/heads/main`
outside snapshot mode, `GenerateRtm` also enforces a bounded commit-lag check (`DefaultMaxMainSnapshotCommitLag`) against
the tested revision recorded in `docs/SDLC/Requirements_Traceability_Matrix.md`. The same scan reports `[KernelRule("K-…")]`
(`DiscoveredTest.KernelRuleIds`) for `KernelRuleCoverageTests` and canonical `PRD-*` coverage for `PrdRequirementCoverageTests`.

## Key dependencies

- Project references: `Cadence.Security`, `Cadence.Kernel.Abstractions`.
- Time (K-TIME-3): the project bans `DateTime.Now/UtcNow`. `GxPSigner.SignGxpMarkdown`, `Pkcs7Signer.GenerateSigningCredentials` and `RtmGeneratorService.GenerateRtm` take an `IClock` (default `SystemClock.Instance`), and `ComplianceAuditLogRecord.Timestamp` has no default (#4784).
- Packages: `System.IdentityModel.Tokens.Jwt`, `Microsoft.IdentityModel.Tokens`, `System.Security.Cryptography.Pkcs`, `Microsoft.AspNetCore.OpenApi`.

## Referenced by

`packages/tooling_core`, `packages/cli`, `tests/Cadence.UnitTests/Cadence.UnitTests.csproj`, `tests/Cadence.E2ETests/Cadence.E2ETests.csproj`. No application references it.

## Tests

`tests/Cadence.UnitTests/Packages/ComplianceUnitTests.cs` and parts of `tests/Cadence.UnitTests/Packages/PortedPackagesUnitTests.cs`:

```bash
dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.ComplianceUnitTests|FullyQualifiedName~Cadence.UnitTests.Packages.PortedPackagesUnitTests"
```
