# Security Package

## Overview

`Cadence.Security`: caller authentication behind the gateway, role/scope authorization, blinding,
signatures and audit digests shared by every Cadence service.

---

## Architectural Boundaries

- Shared library; no database or cross-service runtime bindings of its own.
- Services wire it with `builder.Services.AddCadenceSecurity()` and `app.UseCadenceSecurity()`.
  `ArchitectureSentinelTests` fails the build when a service skips this.
- Authentication, identity headers and required settings are described in ADR-2250
  (`docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md`) and
  `apps/gateway/README.md`.

---

## Public APIs / Exports

- **Authentication:** `GatewayAuthenticationHandler` (the `CadenceGateway` scheme: verifies the
  gateway's HMAC-signed identity headers, fail-closed), `GatewayIdentity`, `GatewayHeaders`,
  `CadenceClaimTypes`, `HmacSigner`, `SigTokenVerifier` + `ReplayCache` (single-use e-signature tokens),
  `AuditorWriteGuardMiddleware`.
- **Authorization:** `PermissionMatrix`, `RoleNormalization`, `Principal`, site/study access evaluators
  and the permission, role, study-scope, change-justification and auditor-read-only handlers.
- **Blinding:** `BlindedFieldAttribute`, `FieldMasker`.
- **Signatures:** `CanonicalSigner` (canonical JSON + HMAC-SHA256) and `AsymmetricSigner` (RSA/X.509),
  used by `Cadence.Deid` for redaction manifests and de-identification certificates.
- **Audit:** `AuditDigestEngine`, `AuditDigestCalculator`, `GxPAuditInterceptor`, `IAuditLogWriter`.

### Canonical JSON (`CanonicalSigner`)

Object keys sorted by ordinal (UTF-16 code unit) order, no whitespace, and only the escapes JSON
requires (`"`, `\`, control characters). `<`, `>`, `&`, `'` and non-ASCII text are written literally.
This is byte-for-byte `canonicalSerialize` in `packages/ui/signing.js`, so signatures verify across
.NET and JavaScript; a known-answer vector in `tests/Packages/SignerUnitTests.cs` pins it.

---

## Dependencies

- ASP.NET Core shared framework
- `Microsoft.EntityFrameworkCore`
- `System.IdentityModel.Tokens.Jwt`, `Microsoft.IdentityModel.Tokens`

---

## Local Development & Testing

```bash
dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Security|FullyQualifiedName~Signer|FullyQualifiedName~Gateway"
```
