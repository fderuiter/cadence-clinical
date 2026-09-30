# Cadence.Compliance

Part 11 support library: audit digest calculation and hash-chain verification, e-signature and PKCS#7 signing and verification, PHI redaction, protocol RAG helpers, and requirements traceability matrix generation.

## Project

- Project: `packages/compliance/src/Cadence.Compliance.csproj` (assembly `Cadence.Compliance`, namespace `Cadence.Compliance`).
- Main types: `AuditDigestCalculator`, `AuditChainVerifier`, `ComplianceAuditLogRecord`, `ESignatureVerifier`, `GxPSigner`, `Pkcs7Signer`, `PhiRedactorService`, `ProtocolRagService`, `RtmGeneratorService`, and the exceptions `TamperDetectedException` and `UnapprovedAIRecordException`.
- Python reference sources remain for behaviour the C# port lacks or does not test yet: `services/esignature_verifier.py`, with their `__init__.py` files and `pyproject.toml`. They are not the implementation; [docs/PYTHON_REFERENCE_INVENTORY.md](../../docs/PYTHON_REFERENCE_INVENTORY.md) says what each needs before it goes.

## Signature verification

`ESignatureVerifier.VerifySignature` returns `VALID` only when the PSS signature over the document and the embedded
certificate verifies. Otherwise the status names the failure: `TAMPERED_INVALID_HASH`, `DUPLICATE_BLOCKS_REJECTED`,
`REVOKED_CERTIFICATE`, `LEGACY_PADDING_REJECTED` (PKCS#1 v1.5), `UNSUPPORTED_KEY_TYPE`, or `MOCK_SIGNATURE_DETECTED`
when a failed document contains the word "mock". The word never rejects a valid signature, because base64
certificates and signatures contain it by chance. `VerifyMarkdown` also checks the SHA-256 in the signature block
against the document body. Only tests call the verifier today; `dotnet cadence verify-gxp-signatures` is not
implemented yet ([docs/cli.md](../../docs/cli.md)).

## Generated GxP documents

`RtmGeneratorService` writes the RTM and the IQ/OQ/PQ report with `GxPSigner.StampGxpMarkdown`, a deterministic
SHA-256 integrity stamp marked as an unsigned working copy, and `GxPSigner.VerifyStamp` lets `check rtm` reject
hand edits. `GxPSigner.SignGxpMarkdown` (RSA-PSS with an embedded certificate) remains for signing a release copy
with a named person's certificate; without one it falls back to a throwaway self-signed key, which is only useful
in tests. [docs/TESTING.md](../../docs/TESTING.md) has the workflow.

## Key dependencies

- Project references: `Cadence.CoreModels`, `Cadence.Deid`.
- Packages: `System.IdentityModel.Tokens.Jwt`, `Microsoft.IdentityModel.Tokens`, `System.Security.Cryptography.Pkcs`, `Microsoft.AspNetCore.OpenApi`.

## Referenced by

`packages/tooling_core`, `packages/cli`, `tests/Cadence.UnitTests.csproj`, `tests/Cadence.E2ETests.csproj`. No application references it.

## Tests

`tests/Packages/ComplianceUnitTests.cs` and parts of `tests/Packages/PortedPackagesUnitTests.cs`:

```bash
dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.ComplianceUnitTests|FullyQualifiedName~Cadence.UnitTests.Packages.PortedPackagesUnitTests"
```
