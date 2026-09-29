# Cadence.Security

Caller authentication and authorization for the services, based on identity headers signed by the API gateway, plus signing utilities, RBAC, field blinding and audit helpers. The trust model is described in [ADR-2250](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).

## Project

- Project: `packages/security/src/Cadence.Security.csproj` (assembly `Cadence.Security`).
- `Cadence.Security.DependencyInjection`: `AddCadenceSecurity()` registers the `GatewayAuthenticationHandler` scheme, authorization handlers and a fallback policy that requires an authenticated caller on every endpoint not marked `AllowAnonymous()`. It validates at startup that `GATEWAY_SECRET` holds at least 32 characters. `UseCadenceSecurity()` adds authentication, authorization and `AuditorWriteGuardMiddleware`. Endpoint helpers: `RequirePermission`, `RequireRole`, `RequireStudyScope`, `RequireChangeJustification`.
- `Cadence.Security.Authentication`: `GatewayAuthenticationHandler` (verifies `X-Gateway-Signature` and `X-Timestamp` over the `X-User-*`, `X-Site-Id`, `X-Tenant-Id` and related headers), `GatewayIdentity`, `GatewayHeaders`, `CadenceClaimTypes`, `SigTokenVerifier`, `ReplayCache`, `AuditorWriteGuardMiddleware`.
- `Cadence.Security.Rbac`: `PermissionMatrix`, `Principal`, `RoleNormalization`, site and study access evaluators, authorization requirements and handlers.
- `Cadence.Security.Blinding`: `BlindedFieldAttribute`, `FieldMasker`, `MaskingJsonConverter`.
- `Cadence.Security.Audit`: `AuditDigestEngine`, `AuditLogRecord`, `AuditLogPayload`, `IAuditLogWriter` with `InMemoryAuditStore`, `GxPAuditInterceptor`.
- `Cadence.Security`: `HmacSigner`, `CanonicalSigner`, `AsymmetricSigner`.
- Python reference sources remain in this folder until each C# port is verified (issue #4789): the top-level `*.py` modules and `pyproject.toml`. They are not the implementation.

## Key dependencies

- Framework reference `Microsoft.AspNetCore.App`.
- Packages: `Microsoft.EntityFrameworkCore`, `System.IdentityModel.Tokens.Jwt`, `Microsoft.IdentityModel.Tokens`.

## Referenced by

Every .NET service (the gateway uses its header, claim and option types; the others call `AddCadenceSecurity()`/`UseCadenceSecurity()`), `packages/deid`, `packages/testing`, `packages/cli`, and all three test projects.

## Tests

```bash
dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.CadenceSecurityUnitTests|FullyQualifiedName~Cadence.UnitTests.Packages.CanonicalSignerTests|FullyQualifiedName~Cadence.UnitTests.Packages.AsymmetricSignerTests"
```

End-to-end gateway signing is covered by `--filter "FullyQualifiedName~Cadence.UnitTests.Gateway"`, and service-side authentication by `tests/Execution/Endpoints/ExecutionAuthenticationTests.cs`.
