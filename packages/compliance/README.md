# Cadence.Compliance

Part 11 support library: audit digest calculation and hash-chain verification, e-signature and PKCS#7 signing and verification, PHI redaction, protocol RAG helpers, and requirements traceability matrix generation.

## Project

- Project: `packages/compliance/src/Cadence.Compliance.csproj` (assembly `Cadence.Compliance`, namespace `Cadence.Compliance`).
- Main types: `AuditDigestCalculator`, `AuditChainVerifier`, `ComplianceAuditLogRecord`, `ESignatureVerifier`, `GxPSigner`, `Pkcs7Signer`, `PhiRedactorService`, `ProtocolRagService`, `RtmGeneratorService`, and the exceptions `TamperDetectedException` and `UnapprovedAIRecordException`.
- Python reference sources remain in this folder until each C# port is verified (issue #4789): `audit.py`, `services/`, `pyproject.toml`. They are not the implementation.

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
