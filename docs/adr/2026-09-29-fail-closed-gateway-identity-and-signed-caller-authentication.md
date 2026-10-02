# ADR-2256: Fail-Closed Gateway Identity and Signed Caller Authentication

Also known as: ADR-2250 (former number)

- **Status:** Accepted
- **Date:** 2026-09-29
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The 2026-07-22 gateway authentication ADR decided that the gateway validates OIDC tokens and forwards the caller's identity as HMAC-signed headers. The .NET 10 port never enforced it (#4761):

- The gateway proxied every route with no authentication at all.
- No service called `AddCadenceSecurity`/`UseCadenceSecurity`, so no service authenticated anything.
- `GatewayAuthMiddleware` trusted unsigned `X-User-*` headers unless an opt-in flag was set, accepted several fallback payload shapes, and never checked the timestamp.
- Query lifecycle permissions read a caller-supplied `X-User-Role` header, and PI e-signatures trusted the signer id and role in the request body.
- `SigTokenVerifier` fell back to a hard-coded secret.

Under 21 CFR Part 11 an electronic record's author and signer must be the authenticated user, so identity has to come from something the caller cannot forge.

## 2. Decision Drivers & Constraints

- **Part 11 attribution (PRD-SYS-053, PRD-SYS-002, PRD-SYS-007):** audit entries, query transitions and PI signatures must name the authenticated user.
- **Fail closed:** a missing secret, signature or identity must deny access, never fall back to trusting headers.
- **One verification path:** every service authenticates the same way, through `Cadence.Security`.
- **Operability:** misconfiguration must stop the host at startup, not surface as silent 200s.

## 3. Options Considered

### Option 1: Validate the OIDC token in every service

- **Overview:** Each service runs JwtBearer against Keycloak.
- **Pros:**
  - ✅ No shared HMAC secret.
- **Cons:**
  - ❌ Every service needs IdP reachability, JWKS caching and Keycloak role mapping.
  - ❌ Change reason and e-signature step-up headers would still need their own integrity protection.

### Option 2: Gateway validates OIDC and signs identity; services verify the signature (Selected)

- **Overview:** Enforce the 2026-07-22 design, fail closed.
- **Pros:**
  - ✅ One JWT validation point; services only verify an HMAC.
  - ✅ The signature also covers the change reason and the e-signature token the client sends.
- **Cons:**
  - ❌ `GATEWAY_SECRET` must be distributed to the gateway and every service.

## 4. Decision Outcome

- **Chosen Option:** Option 2.
- **Gateway:** JwtBearer against `OIDC_AUTHORITY` (required; `OIDC_METADATA_ADDRESS` and `OIDC_REQUIRE_HTTPS_METADATA` optional). _Amended 2026-09-30 (#5037):_ `OIDC_AUDIENCE` is required outside Development. The realm gives only `cadence-web` tokens the `cadence-api` audience, so tokens issued to other realm clients, such as the password-grant e-signature client, are refused. An authenticated caller is required on every proxied route. Keycloak `realm_access.roles` are flattened to role claims. A YARP transform strips all client-supplied identity headers and the bearer token, then adds signed `X-User-Id`, `X-User-Roles`, `X-Site-Id`, `X-Sponsor-Id`, `X-Unblinded-Access`, `X-Tenant-Id`, `X-Timestamp` and `X-Gateway-Signature`, with the client's `X-Change-Reason` and `X-Sig-Token` covered by the signature.
- **Services:** `AddCadenceSecurity()` registers the `CadenceGateway` authentication scheme and a fallback policy requiring an authenticated caller. `UseCadenceSecurity()` adds authentication, authorization and the auditor write guard. Only health, status and development API-doc endpoints are `AllowAnonymous()`.
- **Verification:** exactly one canonical payload, a timestamp within two minutes, and a secret of at least 32 characters. There is no default secret and no fallback payload shapes. A signed request yields `sub`, `NameIdentifier`, normalized role, site, sponsor, tenant and unblinded claims.
- **Authorization inputs:** application code takes identity and roles from those claims only. A PI signature records the authenticated caller as signer; a manifest naming someone else is rejected with 403.

## 5. Consequences & Trade-offs

- **Positive Impact:** Anonymous, forged, tampered and stale requests get 401 in every service. Role spoofing through headers or request bodies no longer works. Audit `UserId` is the real subject instead of `system`.
- **Negative Impact / Technical Debt:**
  - A signed request can be replayed within the two-minute window, so the internal network must stay private.
  - The web client's demo mode (no token) can no longer call the API; it must sign in through Keycloak.
  - E-signature step-up (`X-Sig-Token` issuance and server-side verification at approval) was not wired here; ADR-2259 adds it.
- **Mitigation Strategy:** Rotate `GATEWAY_SECRET` through deployment secrets, never source. Track e-signature step-up and demo-mode retirement under #4761.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/security`, `apps/gateway`, every service `Program.cs`, `apps/execution` query and form endpoints, `packages/testing` (`Cadence.Testing.Security`), `docker/docker-compose.yml`, `.env.example`, `docs/ENVIRONMENT_VARIABLES.md`.
- **Verification Plan:**
  - `tests/Cadence.UnitTests/Execution/Endpoints/ExecutionAuthenticationTests.cs` covers:
    - anonymous, wrong-secret, tampered-role and stale requests return 401;
    - startup fails fast without `GATEWAY_SECRET`.
  - `tests/Cadence.IntegrationTests/Execution/ExecutionWriteAuthorizationTests.cs` (PostgreSQL) covers:
    - wrong role and body-only PI role return 403;
    - a mismatched signer returns 403;
    - the caller is recorded as signer.
  - `tests/Cadence.UnitTests/Gateway/GatewayAuthenticationTests.cs` covers:
    - anonymous and untrusted-token requests return 401;
    - signed forwarding replaces spoofed client headers;
    - startup fails fast without `OIDC_AUTHORITY`.

This record supersedes [2026-07-22: Centralized API Gateway Authentication and Header Propagation](2026-07-22-gateway-authentication-propagation.md) for .NET 10.
