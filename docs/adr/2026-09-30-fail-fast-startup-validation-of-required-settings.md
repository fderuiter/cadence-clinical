# ADR-2490: Fail-Fast Startup Validation of Required Settings

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-190

---

## 1. Context & Problem Statement

Most required settings already stop a service at boot:

- `GATEWAY_SECRET` (ADR-2256)
- `OIDC_AUTHORITY` on the gateway
- the PostgreSQL connection string outside Development

Two settings still failed late or silently:

- **Demographics encryption key (execution):** `DemographicsEncryption__KeyBase64` defaulted to an empty string, and the service started without it. The first subject registration then failed deep in AES-GCM.
- **Service addresses (gateway):** a missing `<SERVICE>_SERVICE_URL` fell back to `http://localhost:<port>` in every environment. A deployment that forgot one kept running and forwarded that service's traffic to localhost.

## 2. Decision Drivers & Constraints

- A misconfigured deployment should fail when it starts, with a message that names the setting.
- No secret or key may appear as a literal default in source (`01_spec.md` §3).
- Local development stays zero-config for the settings that are only addresses.

## 3. Options Considered

### Option 1: Keep the defaults and log a warning

- **Cons:**
  - ❌ A warning in a startup log is easy to miss, and the service still takes traffic it cannot serve.

### Option 2: Validate required settings at startup (Selected)

- **Overview:**
  - **Execution:** `DemographicsEncryptionOptions` is bound with `ValidateOnStart()`. The key must be Base64 that decodes to 16, 24 or 32 bytes in every environment, because there is no safe default key.
  - **Gateway:** a missing `<SERVICE>_SERVICE_URL` falls back to localhost only in Development. Anywhere else startup fails. A value that is not an absolute http or https URL fails startup in every environment.
- **Pros:**
  - ✅ Follows the pattern `GATEWAY_SECRET` already uses.
  - ✅ The error names the setting and how to generate a key.
- **Cons:**
  - ❌ Every host that runs the execution service needs a key, test hosts included.

## 4. Decision Outcome

- **Chosen Option:** Option 2.
- **Justification:** It turns two late or silent failures into a clear error at boot, with no new dependency.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - A missing demographics key or service address is found at deploy time, not by a site user.
- **Negative Impact / Technical Debt:**
  - Test hosts call `UseTestDemographicsKey()` from `Cadence.Testing`, and the OpenAPI exporter supplies a throwaway key.
  - The Aspire AppHost does not yet pass a demographics key to the execution service, so running through it needs `DemographicsEncryption__KeyBase64` set in the environment.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `apps/execution/src/Program.cs`, `apps/execution/src/Infrastructure/Security/DemographicsEncryptionOptions.cs`
  - `apps/gateway/src/Program.cs`
  - `packages/testing/src/Security/TestSecurityContext.cs`, `packages/tooling_core/src/Cadence.OpenApiExporter/Program.cs`
  - `docs/ENVIRONMENT_VARIABLES.md`, `apps/execution/README.md`, `apps/gateway/README.md`, `.env.example`
- **Verification Plan:**
  - `tests/Execution/Security/DemographicsKeyStartupValidationTests.cs` checks that an empty, non-Base64 or wrong-length key stops the host at startup.
  - `tests/Gateway/GatewayServiceUrlValidationTests.cs` checks that the gateway will not start outside Development without service addresses, or with a relative address.
