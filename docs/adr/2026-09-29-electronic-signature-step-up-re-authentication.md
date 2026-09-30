# ADR-2259: Electronic Signature Step-Up Re-Authentication

- **Status:** Accepted
- **Date:** 2026-09-29
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

21 CFR 11.200(a)(1) requires a signer to enter their identification components when applying an electronic signature, not only to hold a session. `docs/SDLC/Signature_Token_Cryptographic_Contract.md` specifies how: the gateway re-checks the signer's credentials and issues a single-use, 60-second `X-Sig-Token` bound to one action, which the service verifies before writing.

After ADR-2256 the gateway forwarded `X-Sig-Token` inside the signed identity, but nothing issued the token and no endpoint required it (#4761). A PI could approve a form with nothing more than a session, and `SigTokenVerifier` only checked the signer and single use, not the action.

## 2. Decision Drivers & Constraints

- **Part 11 (PRD-SYS-053, PRD-SYS-002):** a signature needs a fresh credential check, bound to the record being signed.
- **Passwords never leave the gateway:** they go to Keycloak only and are never logged, stored or put in a token.
- **Fail closed:** a missing, expired, replayed or mismatched token means no write.
- **Compatibility:** the web client already posts `{ username, password, totp, action }` to `/api/v1/auth/signature-verification` and expects `sig_token`.

## 3. Options Considered

### Option 1: Keycloak direct-access grant from the gateway (Selected)

- **Overview:** The gateway posts the password to the realm's token endpoint as a resource-owner password grant on a client reserved for signing, then issues the contract's HS256 token.
- **Pros:**
  - ✅ Matches the existing contract and web client.
  - ✅ Keycloak applies its own password policy, OTP and brute-force lockout.
- **Cons:**
  - ❌ The password grant is omitted from OAuth 2.1. It works only for accounts with passwords in the realm (not federated SSO users).

### Option 2: OIDC re-authentication redirect (`prompt=login` / `max_age=0`)

- **Overview:** The browser re-runs the Keycloak login; the gateway checks `auth_time` in the new ID token before issuing the signature token.
- **Pros:**
  - ✅ The gateway never sees a password; works with federated identity providers.
- **Cons:**
  - ❌ Needs a redirect round-trip in the signing dialog and a web client rewrite; the contract and client assume Option 1.

## 4. Decision Outcome

- **Chosen Option:** Option 1, behind an `ICredentialVerifier` seam so Option 2 can replace it without touching services.
- **Gateway (`apps/gateway/src/Signatures/`):** `POST /api/v1/auth/signature-verification` requires the bearer token, takes `password`, optional `totp`, `action`, `semantic_action` and optional `batch_id`, and refuses a `username` other than the caller's. Auditors and inspectors get 403 before any credential check. The password is checked on `OIDC_SIGNATURE_CLIENT_ID` (default `cadence-esignature`, direct access grants only), and the session that grant opens is logged out. Five attempts per signer per minute, then 429. The response is `{ sig_token, expires_in: 60 }` with `Cache-Control: no-store`.
- **Token (`SigTokenIssuer`, `packages/security`):** HS256 with `GATEWAY_SECRET`; `sub`, `username`, `roles`, `action`, `semantic_action`, optional `batch_id`, `sig_ver: v3`, `iat`, `exp = iat + 60`, random `jti`.
- **Services:** `RequireElectronicSignature(semanticAction, resolveAction)` verifies the token before the handler: HS256 only, lifetime, signer, `sig_ver`, action, semantic action, batch when given, no auditor role, then single use. Failures return `401 REAUTHENTICATION_REQUIRED`. Form approval (`execution.form.approve`) binds to `/api/v1/execution/forms/{id}/approve` on both route aliases.
- **Keycloak realm:** adds the `cadence-esignature` client and enables brute-force protection.

## 5. Consequences & Trade-offs

- **Positive Impact:** PI approval cannot happen without a fresh password check, and a token for one form cannot approve another. Other signature-gated actions (eConsent, CAPA closure, unblinding) can opt in with one line.
- **Negative Impact / Technical Debt:**
  - Federated users without a realm password cannot sign until Option 2 exists.
  - The replay cache is per service instance; horizontally scaled services need a shared cache.
  - Older v1/v2 tokens are no longer accepted (none were issued by the .NET gateway).
- **Mitigation Strategy:** Keep the verifier seam; move the replay cache to a distributed store when services scale out.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/security`, `apps/gateway`, `apps/execution` form endpoints, `packages/testing`, `docker/cadence-realm.json`, `.env.example`, `docs/ENVIRONMENT_VARIABLES.md`, the signature-token contract.
- **Verification Plan:**
  - `tests/Cadence.UnitTests/Packages/SignatureTokenTests.cs`: contract claims and lifetime; auditors refused; single use; wrong signer, action, semantic action or batch rejected without consuming the token; expired, wrong-key, pre-v3, over-long and auditor tokens rejected.
  - `tests/Cadence.UnitTests/Gateway/SignatureTokenEndpointTests.cs`: sign-in required; right password issues a verifiable single-use token; wrong password, auditor, other username and unbound action are refused; rate limit; token endpoint derivation; the Keycloak grant, logout and error mapping.
  - `tests/Cadence.UnitTests/Execution/Endpoints/ExecutionAuthenticationTests.cs`: approval without a token is 401 and changes nothing; tokens for another form, action or signer are 401; replay is 401 across route aliases.
