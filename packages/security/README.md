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
- No Python reference sources remain (#4789, [ADR-2509](../../docs/adr/2026-11-24-retire-remaining-python-references-and-scanner-residue.md)).

---

## Public APIs / Exports

- **Authentication:** `GatewayAuthenticationHandler` (the `CadenceGateway` scheme: verifies the
  gateway's HMAC-signed identity headers, fail-closed), `GatewayIdentity`, `GatewayHeaders`,
  `CadenceClaimTypes`, `HmacSigner`, `SigTokenVerifier` (`VerifyAndConsumeAsync`, including atomic dual-signer
  verification before consuming either `jti` via `ISigTokenUseStore.TryRecordFirstUseBatchAsync`, acceptance window
  `iat - 5s <= now < exp + 5s`, [ADR-2666](../../docs/adr/2026-11-24-atomic-dual-signer-step-up-verification-and-startup-validated-cgt-barcode-key.md)) with `ISigTokenUseStore` for single use: `ReplayCache` is the process-local default,
  and every host with an e-signature route replaces it with an `IDurableSigTokenUseStore` (`AddCadenceDurableSigTokenStore`
  in [packages/database](../database/README.md)). Outside `Development`, `SigTokenUseStoreStartupCheck` refuses to start a
  host that maps a `RequireElectronicSignature` route without one (#5434,
  [ADR-2515](../../docs/adr/2026-10-15-step-up-tokens-are-single-use-across-instances-and-restarts.md)).
  `RequireElectronicSignature(...)` applies it to a route and `HttpContext.VerifiedSigToken()` reads the verified token;
  `AuditorWriteGuardMiddleware`, `ExpiredSubscriptionWriteGuardMiddleware` (enforces `403 K.Subscription.Expired` on mutating requests when signed `X-Tenant-Read-Only` is `true`, exempting only `GET`/`HEAD`, `etmf_document:export`, and closeout routes marked with `AllowExpiredTenantWrite()`, [ADR-2604](../../docs/adr/2026-11-21-signed-gateway-expiry-guard-and-scoped-trial-upgrades.md)), `ProductFamilyGuardMiddleware` (enforces `403 K.Commercial.ProductFamilyNotEntitled` across all 20 surfaces from signed `X-Tenant-Product-Families`), `SubscriptionScopingGuardMiddleware` (enforces `403 K.Commercial.ScopingCapExceeded` from signed `X-Tenant-Scoping`), and `ChangeReasonHeader`, the one codec for `X-Change-Reason`: plain
  printable ASCII, or RFC 8187 `UTF-8''` percent-encoded UTF-8 for any other text. Every reader decodes the
  header with it, so the audit trail stores what the user typed
  ([ADR-2495](../../docs/adr/2026-09-30-reason-for-change-header-encoding.md)). A signed request without `X-Tenant-Id` authenticates with no
  `tenant_id` claim; there is no default tenant, and `GatewayIdentity.TenantId`, `Principal.TenantId`, and `CallerScope.TenantId` are null when
  the sign-in names none ([ADR-2514](../../docs/adr/2026-09-30-the-tenant-comes-from-sign-in-with-no-fallback.md)). `GatewayHeaders.TenantReadOnly` (`X-Tenant-Read-Only`), `GatewayHeaders.TenantProductFamilies` (`X-Tenant-Product-Families`), and `GatewayHeaders.TenantScoping` (`X-Tenant-Scoping`) are bound into `HmacSigner` and exposed on `Principal`.
- **Authorization:** `PermissionMatrix`, `RoleNormalization`, `Principal`, `ClinicalScopeGuard`, site/study access evaluators
  and the permission, role, tenant-scope, study-scope, site-scope, change-justification and auditor-read-only handlers.
  `RequirePermission("resource:action")` on a route requires that permission in `PermissionMatrix`; the
  `PermissionPolicyProvider` registered by `AddCadenceSecurity()` builds the policy and a caller without it
  gets `403` ([ADR-2510](../../docs/adr/2026-09-30-endpoint-permissions-from-the-permission-matrix.md)).
  Routes are denied by default: the fallback policy refuses any route that declares neither a permission,
  `RequireSignedInCaller(reason)` (any signed caller, with the reason kept on the endpoint) nor `AllowAnonymous()`.
  Module permissions live in `PermissionMatrix.<Module>.cs`, and a permission no role holds is refused (`RoleNormalization.ROLE_TRIAL_COORDINATOR` provides self-service trial subject, eCRF, randomisation, site-compliance, CGT custody read, and support-ticket permissions outside `SiteAccessEvaluator.SITE_SCOPED_ROLES` while keeping `sponsor_dm` read-only separation of duties intact; `PermissionAuthorizationHandler` ignores `trial_coordinator` write grants unless `ClinicalScopeGuard.IsVerifiedTrialTenant(principal.TenantId)` matches `^trial-[0-9a-f]{12}\z`, and `ClinicalScopeGuard.IsSiteBound` / `SiteAccessEvaluator.CanAccessSite` treat non-trial `trial_coordinator` callers without an unscoped sponsor or oversight role as site-bound, [ADR-2662](../../docs/adr/2026-11-23-enforce-trial-tenant-writes-and-sponsor-comment-allowlist.md); `RoleNormalization.ROLE_UNBLINDED_STATISTICIAN` holds `subject_enrollment:read` and `randomization_scheme` capabilities without CGT chain-of-custody grants, [ADR-2833](../../docs/adr/2026-11-23-rtsm-supply-isolation-rbac-and-demo-seeding.md)).
  Standalone routes also declare explicit `EndpointScopeMetadata` via `RequireTenantScope()`, `RequireCallerStudyScope()`,
  `RequireCallerStudyAndSiteScope()`, `RequireStudyScope(routeParameter)` and `RequireSiteScope(routeParameter)`; study assignments
  arrive signed in `X-Study-Ids` from the sign-in token's `study_ids` claim. Handlers check scope and caller attribution that the route path
  cannot name (query/body parameters, stored records, filtered in-memory lists, or EF Core `IQueryable<T>` queries) through
  `ClinicalScopeGuard` (`IsVerifiedTrialTenant`, `RequireTenant`, `RequireVerifiedTenant`, `RequireStudy`, `RequireSite`, `RequireStudyAndSite`, `RequireStudyAndOptionalSite`,
  `ResolveStudyFilter`, `ResolveSiteFilter` and `ResolveSingleSiteFilter` with `StringComparer.OrdinalIgnoreCase`, `ApplyFilters`, `NarrowToStudyScope`,
  `NarrowToScope`, `IsUnrestricted`, `Forbidden`, `OutsideStudy`) and `CallerScope` (`Principal` with authenticated `ClaimsPrincipal`
  `sub` fallback, `UserId`, `VerifiedUserId`, `RequiredPrincipal`, `RequiredUserId`, `Roles`, `ResolveActor`), failing closed with
  `ProblemHttpResult` HTTP 403
  ([ADR-2516](../../docs/adr/2026-10-16-routes-are-denied-unless-they-declare-who-may-call-them.md),
  [ADR-2548](../../docs/adr/2026-11-20-standalone-postgres-rls-and-shared-scope-guards.md),
  [ADR-2552](../../docs/adr/2026-11-21-kernel-site-setup-and-execution-tenancy.md),
  [ADR-2661](../../docs/adr/2026-11-22-consolidate-standalone-caller-and-clinical-scope-guards.md),
  [ADR-2667](../../docs/adr/2026-11-23-fail-closed-site-scope-for-pharmacist-and-subject-roles.md); `SiteAccessEvaluator.SITE_SCOPED_ROLES` covers site investigators, CRCs, CRAs/monitors, ER physicians, pharmacists, and subjects).
- **Blinding:** `BlindedFieldAttribute`, `FieldMasker`.
- **Signatures:** `CanonicalSigner` (canonical JSON + HMAC-SHA256), `AsymmetricSigner` (RSA/X.509, used by `Cadence.Deid` for redaction manifests and de-identification certificates), and `BiometricLivenessTokenService` + `BiometricLivenessOptions` (`AddCadenceBiometricLiveness()`, requiring `CADENCE_BIOMETRIC_LIVENESS_KEY` as base64 of 32 bytes at startup, bounding biometric liveness tokens to a 15-minute lifetime with 5-second clock skew on `IClock`, binding `tenant_id`, `issuer_id`, and optional `study_id` / `site_id` scope claims—mandatory on `coordinator_fallback`—and failing closed in `VerifyToken` when `tenant_id` is blank or mismatched, [ADR-2660](../../docs/adr/2026-11-23-biometric-liveness-hmac-key-and-single-use-replay-protection.md)).
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

| Type                                                                    | Use                                                                                                                                                                                                                                                                                                                                                                                                                                |
| ----------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `IFieldEncryptor` / `AesGcmFieldEncryptor`                              | AES-256-GCM encryption (`Encrypt`, `EncryptBatch`, `Decrypt`) of a value that must be read back. Stored as `enc:v1:<keyId>:<base64(nonce, ciphertext, tag)>`; `EncryptBatch` reuses one cipher context per batch while drawing a fresh 96-bit random nonce per item, and the purpose string (`<service>.<table>.<column>`) and key id are authenticated, so values cannot be moved between columns. Randomised, so not searchable. |
| `ILookupHasher` / `HmacLookupHasher`                                    | HMAC-SHA256 "blind index" (64 lowercase hex characters) for a value that only needs to be matched, never read back.                                                                                                                                                                                                                                                                                                                |
| `EncryptedStringConverter`, `PropertyBuilder<string?>.IsEncrypted(...)` | EF Core mapping for an encrypted column; sizes the column with `AesGcmFieldEncryptor.GetMaxStoredLength`.                                                                                                                                                                                                                                                                                                                          |
| `IFieldEncryptingDbContext`, `FieldEncryptionModelCacheKeyFactory`      | A DbContext with encrypted columns implements the interface and replaces `IModelCacheKeyFactory` in `OnConfiguring`, so models built with different keys are never shared.                                                                                                                                                                                                                                                         |
| `DesignTimeFieldEncryptor`                                              | For `IDesignTimeDbContextFactory` only: builds the model, refuses to encrypt or decrypt.                                                                                                                                                                                                                                                                                                                                           |
| `FieldDecryptionException`                                              | Thrown on wrong key, wrong purpose, tampering or a non-encrypted value. Reads fail loudly.                                                                                                                                                                                                                                                                                                                                         |

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
