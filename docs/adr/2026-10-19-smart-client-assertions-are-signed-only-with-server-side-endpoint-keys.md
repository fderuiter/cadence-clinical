# ADR-2517: SMART Client Assertions Are Signed Only With Server-Side Endpoint Keys

- **Status:** Accepted
- **Date:** 2026-10-19
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-192
- **Related:** [ADR-2494](2026-09-30-field-level-encryption-and-keyed-lookup-hashes-for-secrets-at-rest.md),
  [ADR-2516](2026-10-16-routes-are-denied-unless-they-declare-who-may-call-them.md)

---

## 1. Context & Problem Statement

Interop authenticates to an EHR's FHIR server with SMART Backend Services: it signs a short-lived JWT client assertion
with the private key whose public half the EHR registered. Before this decision (#5436):

- `POST /esource/smart/assertion` took the client id, token URL, `kid` and the PEM private key in the request body, and
  `POST /esource/poll` accepted a PEM too. A private key that crosses an API boundary can be logged, cached or replayed
  by anyone who sees the request.
- When the PEM was missing or failed to parse, the route generated a fresh RSA key and signed with it, so a
  misconfiguration produced a well-formed assertion that no EHR would accept, with nothing reported.
- `POST /esource/smart/verify` read the token without checking its signature, so any string that parsed as a JWT with
  the right `sub`, `aud` and `exp` was reported valid.

## 2. Decision Drivers & Constraints

- **Secrets never cross the API (21 CFR 11.300):** a signing key is held by the server and named by reference.
- **Fail closed:** no key, a bad key or a key too weak to use is an error, never a substitute key.
- **Verification means signature verification:** a verify route that does not check the signature reports nothing.
- **Standards:** SMART App Launch Backend Services requires RS384 or ES384 and a lifetime of at most five minutes.
- **Pre-deployment:** no endpoint is registered anywhere yet, so the request shapes may change without a shim.

## 3. Options Considered

### Option 1: Keys by reference in configuration, resolved per endpoint (Selected)

- **Overview:** an EHR endpoint record stores `SigningKeyRef` (a configuration name) and `SigningKeyId` (the `kid`).
  `ISmartSigningKeys` resolves the PEM from `SmartSigningKeys:<ref>`, which deployments fill from a secret store through
  the environment variable `SmartSigningKeys__<ref>`. The assertion route takes only the endpoint id and a lifetime.
- **Pros:**
  - ✅ The private key never appears in a request, a response, the database or an audit entry.
  - ✅ Works with any secret store that projects into configuration, and the abstraction can later move to a KMS that
    signs without releasing the key.
- **Cons:**
  - ❌ Registering an endpoint becomes two steps: store the key, then register the reference.

### Option 2: Store the PEM in the database, encrypted at rest (ADR-2494)

- **Pros:**
  - ✅ One registration step.
- **Cons:**
  - ❌ The key still travels through the registration API, and anyone with database and field-key access can read it.

### Option 3: Keep accepting the PEM per request, remove the generated-key fallback

- **Cons:**
  - ❌ Fixes the silent fallback but leaves the key crossing the API on every call.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **Rules:**
  - Neither the assertion route nor polling accepts a key in the request body.
  - A reference matches `^[A-Za-z0-9_]{1,64}$`. A missing reference, an unconfigured key, a malformed PEM, a public key
    alone or an RSA key under 2048 bits returns `422` and signs nothing. There is no generated or default key.
  - Assertions are RS384, carry the endpoint's `kid`, use the endpoint's client id as issuer and subject and its token
    URL as audience, carry a fresh `jti`, and live 1 to 300 seconds on `IClock` (anything else is `400`).
  - Verification checks the signature against the endpoint's key, then issuer, audience and lifetime on `IClock`.
  - An unknown endpoint is `404`.

## 5. Consequences & Trade-offs

- **Positive Impact:** a misconfigured key is reported where it is configured, and a verified assertion is one this
  server actually signed.
- **Negative Impact / Technical Debt:** keys are read from configuration at signing time and are not rotated by the
  service. Rotation means adding a new reference and re-pointing the endpoint.
- **Mitigation Strategy:** the reference indirection makes rotation a configuration change, and a KMS-backed
  `ISmartSigningKeys` can replace the configuration one without touching the routes.

## 6. Implementation & Verification

- **Affected Services:** `apps/interop` (`Infrastructure/Security/SmartSigningKeys.cs`, `Endpoints/EsourceEndpoints.cs`,
  migration `AddEhrEndpointSigningKeyRef`), `docs/ENVIRONMENT_VARIABLES.md`.
- **Verification Plan:** `tests/Cadence.UnitTests/Interop/EsourceReconciliationTests.cs` signs with a configured key and
  verifies it, refuses an unconfigured or public-only key with `422` and no token, refuses a lifetime over 300 seconds,
  and reports a token signed by another key as invalid.
