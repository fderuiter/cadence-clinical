# ADR-2260: No Default Credentials and Fail-Closed Secret Resolution

- **Status:** Accepted
- **Date:** 2026-09-29
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Issue #4765 found credentials committed to the repository and used as silent fallbacks when configuration was missing:

- Every EF Core design-time factory and `AddCadencePostgresDbContext` (in Development) fell back to a local connection string with a well-known password.
- The Aspire AppHost started MinIO and Keycloak with literal admin credentials and an unpinned `minio:latest` image.
- `AuditDigestEngine` and the compliance `AuditDigestCalculator` each fell back to a different hardcoded `AUDIT_LOG_SECRET_KEY`, so the two could sign and verify the same audit chain with different keys.
- `Cadence.Config` options carried secret defaults, the web client shipped the gateway HMAC secret in its bundle, and an RSA private key was committed under `tests/fixtures/keys/`.

A fallback credential turns a configuration mistake into a running system that is reachable with a publicly known secret, and for the audit key it silently weakens the 21 CFR Part 11 tamper evidence of the audit trail.

## 2. Decision Drivers & Constraints

- **Part 11 audit integrity (PRD-SYS-053):** the audit HMAC key must be a deployment secret, with one source for every producer and verifier.
- **Fail closed:** a missing secret stops the process with a message naming the variable (as ADR 2026-08-04 already requires for cryptographic secrets).
- **No secrets in source or bundles:** nothing committed, nothing shipped to browsers.
- **Existing names:** keep `POSTGRES_CONNECTION_STRING`, `ConnectionStrings__cadence` and `AUDIT_LOG_SECRET_KEY`.

## 3. Options Considered

### Option 1: Keep development-only fallbacks

- **Overview:** Fall back to local credentials only when the environment is Development.
- **Pros:**
  - ✅ Zero setup for a first local run.
- **Cons:**
  - ❌ The password stays committed and documented, and `WebApplicationFactory` and misconfigured hosts default to Development.
  - ❌ Does not fix the diverging audit keys.

### Option 2: Require configuration everywhere and resolve each secret in one place (Selected)

- **Overview:** Connection strings and the audit key are read from configuration or the environment and throw `InvalidOperationException` when absent. `Cadence.Security.Audit.AuditSecretKey` is the only resolver of the audit key. The AppHost uses Aspire parameters (`secret: true`). Tests generate key material at run time and pass explicit dummy settings to their hosts.
- **Pros:**
  - ✅ No credential remains in C# code, and misconfiguration is visible at startup.
  - ✅ Audit digests are computed with the same key by every component.
- **Cons:**
  - ❌ Developers must set a few variables (or user secrets) before the first run.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** It removes the committed secrets instead of narrowing where they apply, and it aligns the audit key handling with the fail-fast policy for cryptographic secrets.

## 5. Consequences & Trade-offs

- **Positive Impact:** A deployment cannot come up with a known password or a known audit key; secret scanning (detect-secrets with `.secrets.baseline`) now runs in pre-commit and CI.
- **Negative Impact / Technical Debt:** `POSTGRES_CONNECTION_STRING` (or `ConnectionStrings__cadence`) and `AUDIT_LOG_SECRET_KEY` are required in every environment, and `dotnet ef` needs `POSTGRES_CONNECTION_STRING` exported (or `-- --connection "..."`).
- **Mitigation Strategy:** Error messages name the missing variable; `.env.example`, the AppHost README and the root README describe how to set them.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/database`, `packages/security`, `packages/compliance`, `packages/config`, `packages/cli`, `packages/testing`, every `apps/*` design-time factory, `apps/apphost`, `apps/web`, `docker/nginx`.
- **Verification Plan:** Unit tests in `tests/Packages/DatabaseUnitTests.cs` and `tests/Packages/CadenceSecurityUnitTests.cs` cover the missing-configuration failures and the shared digest key; the Execution and E2E host tests supply their connection string through `UseTestPostgresConnectionString()`.
