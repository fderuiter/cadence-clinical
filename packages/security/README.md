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
  `CadenceClaimTypes`, `HmacSigner`, `SigTokenVerifier` (`VerifyAndConsumeAsync`, acceptance window
  `iat - 5s <= now < exp + 5s`) with `ISigTokenUseStore` for single use: `ReplayCache` is the process-local default,
  and a host with a database replaces it with a durable store, as Execution does
  ([ADR-2515](../../docs/adr/2026-10-15-step-up-tokens-are-single-use-across-instances-and-restarts.md)).
  `RequireElectronicSignature(...)` applies it to a route and `HttpContext.VerifiedSigToken()` reads the verified token;
  `AuditorWriteGuardMiddleware`, and `ChangeReasonHeader`, the one codec for `X-Change-Reason`: plain
  printable ASCII, or RFC 8187 `UTF-8''` percent-encoded UTF-8 for any other text. Every reader decodes the
  header with it, so the audit trail stores what the user typed
  ([ADR-2495](../../docs/adr/2026-09-30-reason-for-change-header-encoding.md)). A signed request without `X-Tenant-Id` authenticates with no
  `tenant_id` claim; there is no default tenant, and `GatewayIdentity.TenantId` is null when the sign-in names none
  ([ADR-2514](../../docs/adr/2026-09-30-the-tenant-comes-from-sign-in-with-no-fallback.md)).
- **Authorization:** `PermissionMatrix`, `RoleNormalization`, `Principal`, site/study access evaluators
  and the permission, role, study-scope, change-justification and auditor-read-only handlers.
  `RequirePermission("resource:action")` on a route requires that permission in `PermissionMatrix`; the
  `PermissionPolicyProvider` registered by `AddCadenceSecurity()` builds the policy and a caller without it
  gets `403` ([ADR-2510](../../docs/adr/2026-09-30-endpoint-permissions-from-the-permission-matrix.md)).
- **Blinding:** `BlindedFieldAttribute`, `FieldMasker`.
- **Signatures:** `CanonicalSigner` (canonical JSON + HMAC-SHA256) and `AsymmetricSigner` (RSA/X.509),
  used by `Cadence.Deid` for redaction manifests and de-identification certificates.
- **Audit:** `AuditDigestEngine`, `AuditDigestCalculator`, `IAuditLogWriter`.
- **Time (K-TIME-3):** the project bans `DateTime.Now/UtcNow`. `ReplayCache` and `AuditDigestEngine` take an
  `IClock` (default `SystemClock.Instance`; `AddCadenceSecurity` passes the registered one), and
  `AuditLogRecord.Timestamp` is `required` (#4784).

### Canonical JSON (`CanonicalSigner`)

Object keys sorted by ordinal (UTF-16 code unit) order, no whitespace, and only the escapes JSON
requires (`"`, `\`, control characters). `<`, `>`, `&`, `'` and non-ASCII text are written literally.
This is byte-for-byte `canonicalSerialize` in `packages/ui/signing.js`, so signatures verify across
.NET and JavaScript; a known-answer vector in `tests/Cadence.UnitTests/Packages/SignerUnitTests.cs` pins it.

---

## Dependencies

- ASP.NET Core shared framework
- `Cadence.Kernel.Abstractions` (`IClock`)
- `Microsoft.EntityFrameworkCore`
- `System.IdentityModel.Tokens.Jwt`, `Microsoft.IdentityModel.Tokens`

---

## Field-Level Protection (`Cadence.Security.FieldProtection`, .NET)

Protects individual secret columns at rest (#4770, see
[ADR-2494](../../docs/adr/2026-09-30-field-level-encryption-and-keyed-lookup-hashes-for-secrets-at-rest.md)).

| Type                                                                    | Use                                                                                                                                                                                                                                                                              |
| ----------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `IFieldEncryptor` / `AesGcmFieldEncryptor`                              | AES-256-GCM encryption of a value that must be read back. Stored as `enc:v1:<keyId>:<base64(nonce, ciphertext, tag)>`; the purpose string (`<service>.<table>.<column>`) and key id are authenticated, so values cannot be moved between columns. Randomised, so not searchable. |
| `ILookupHasher` / `HmacLookupHasher`                                    | HMAC-SHA256 "blind index" (64 lowercase hex characters) for a value that only needs to be matched, never read back.                                                                                                                                                              |
| `EncryptedStringConverter`, `PropertyBuilder<string?>.IsEncrypted(...)` | EF Core mapping for an encrypted column; sizes the column with `AesGcmFieldEncryptor.GetMaxStoredLength`.                                                                                                                                                                        |
| `IFieldEncryptingDbContext`, `FieldEncryptionModelCacheKeyFactory`      | A DbContext with encrypted columns implements the interface and replaces `IModelCacheKeyFactory` in `OnConfiguring`, so models built with different keys are never shared.                                                                                                       |
| `DesignTimeFieldEncryptor`                                              | For `IDesignTimeDbContextFactory` only: builds the model, refuses to encrypt or decrypt.                                                                                                                                                                                         |
| `FieldDecryptionException`                                              | Thrown on wrong key, wrong purpose, tampering or a non-encrypted value. Reads fail loudly.                                                                                                                                                                                       |

Registration (fail closed: a missing or invalid key stops the host at startup; there are no default keys):

```csharp
builder.Services.AddCadenceFieldEncryption(); // FIELD_ENCRYPTION_KEY, base64 of 32 bytes
builder.Services.AddCadenceLookupHashing();   // FIELD_LOOKUP_HASH_KEY, base64 of 32 bytes, a different key
```

Tests take keys from `Cadence.Testing.Security.TestFieldProtection`, never from production code.

---

## Local Development & Testing

```bash
dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Security|FullyQualifiedName~Signer|FullyQualifiedName~Gateway"
```
