# ADR-2491: HTTP Edge Hardening: Trusted Forwarded Headers, HSTS and Per-Caller Rate Limits

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-189

---

## 1. Context & Problem Statement

Requests reach a service through two proxies: the nginx front proxy, then the gateway. The services had no
HTTP hardening:

- **Forwarded headers ignored:** every service saw the proxy as the client and plain HTTP as the scheme.
  Logs recorded the wrong address, and nothing could tell that the caller used HTTPS.
- **No HSTS:** browsers were never told to stay on HTTPS.
- **No rate limit:** one caller, or one script retrying with bad tokens, could use all of the gateway's capacity.
- **`Server` header:** every response advertised Kestrel.

## 2. Decision Drivers & Constraints

- Forwarded headers are client-controlled. They may be trusted only when a known proxy sent them.
- One policy for every service, in the shared web defaults (`packages/telemetry`).
- Limits go at the gateway, the single entry point, and are counted per caller rather than per proxy.
- No new package. ASP.NET Core ships forwarded headers, HSTS and rate limiting.

## 3. Options Considered

### Option 1: Trust forwarded headers from anyone (`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`)

- **Cons:**
  - ❌ Any client could spoof its address and scheme, and so escape a per-address limit.

### Option 2: Trusted proxy list, HSTS outside Development, fixed-window limit per caller at the gateway (Selected)

- **Overview:**
  - **Forwarded headers:** `CADENCE_TRUSTED_PROXIES` lists proxy addresses or CIDR ranges. `X-Forwarded-For` and
    `X-Forwarded-Proto` are applied back through every trusted hop to the first untrusted address, which is the
    client. A malformed entry fails startup.
  - **HSTS:** `Strict-Transport-Security: max-age=31536000` on HTTPS responses outside Development. It needs no
    per-service setting, because it is only sent when the request (or a trusted proxy) says HTTPS.
  - **Rate limit:** `GATEWAY_RATE_LIMIT_PER_MINUTE` (default 600) per one-minute window. The key is the token's
    subject, or the client address for unauthenticated requests. Over the limit the gateway answers 429 with
    `Retry-After`. Health probes are exempt.
  - **`Server` header:** removed.
- **Pros:**
  - ✅ Spoofed headers from untrusted senders are ignored.
  - ✅ A caller over its limit is refused at the gateway, before any service does work.
- **Cons:**
  - ❌ A fixed window allows up to twice the limit across a window boundary. A sliding window would not, but it
    cannot report `Retry-After`.

## 4. Decision Outcome

- **Chosen Option:** Option 2.
- **Justification:** It closes the gaps with built-in middleware and never trusts headers a client could forge.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Logs and limits see the real client address behind the proxies.
  - Browsers stay on HTTPS once they have used it.
- **Negative Impact / Technical Debt:**
  - Deployments must set `CADENCE_TRUSTED_PROXIES`. Without it, every caller behind the front proxy shares one
    per-address limit before signing in. Compose trusts the private ranges for local use.
  - The limit is per gateway instance. Several instances would need a shared store.
  - CORS stays closed (no policy means browsers refuse cross-origin calls). The web apps reach the gateway through
    the same-origin front proxy.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `packages/telemetry/src/CadenceHttpHardening.cs`, `packages/telemetry/src/CadenceTelemetryExtensions.cs` (every service)
  - `apps/gateway/src/GatewayRateLimiting.cs`, `apps/gateway/src/Program.cs`
  - `docker/docker-compose.yml`, `docs/ENVIRONMENT_VARIABLES.md`, `packages/telemetry/README.md`, `apps/gateway/README.md`
- **Verification Plan:**
  - `tests/Cadence.UnitTests/Packages/HttpHardeningTests.cs` checks proxy list parsing, HSTS on HTTPS outside Development only, and that
    `X-Forwarded-Proto` is honoured from a trusted proxy and ignored from anyone else.
  - `tests/Cadence.UnitTests/Gateway/GatewayRateLimitingTests.cs` checks that a caller over the limit gets 429 with `Retry-After`,
    another caller is still served, and health probes are never limited.
