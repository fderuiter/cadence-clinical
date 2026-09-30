# Gateway

The single public entry point. A YARP reverse proxy that authenticates every caller and forwards the
caller's identity to backend services as HMAC-signed headers.

## Authentication

1. The gateway validates the `Authorization: Bearer` token against the OIDC provider (Keycloak) named by
   `OIDC_AUTHORITY` (and audience `OIDC_AUDIENCE` when set). Anonymous or invalid tokens get `401`.
2. It strips every identity header the client sent (`X-User-Id`, `X-User-Roles`, `X-User-Role`,
   `X-Gateway-Signature`, ...) and the bearer token, then adds its own set built from the token's claims
   (`sub`, Keycloak realm roles, site/sponsor/tenant scope) plus the client's `X-Change-Reason` and
   `X-Sig-Token`. The set is signed with `GATEWAY_SECRET` and timestamped
   (`apps/gateway/src/GatewayIdentityForwarding.cs`). The change reason is decoded from its RFC 8187 form,
   signed as typed and forwarded re-encoded; a malformed encoding gets `400`
   ([ADR-2495](../../docs/adr/2026-09-30-reason-for-change-header-encoding.md)).
3. Each backend service (`AddCadenceSecurity`/`UseCadenceSecurity` in `packages/security`) accepts a
   request only with a valid signature inside a two-minute window, and requires an authenticated caller on
   every endpoint except health, status and development API docs. `ArchitectureSentinelTests` fails the
   build when a service's `Program.cs` skips this wiring.

See ADR-2256 (`docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md`).

Only `/healthz` and `/api/v1/status` are anonymous on the gateway itself.

## Electronic signatures (step-up)

21 CFR 11.200(a)(1) requires a signer to re-enter credentials when signing. Signature-gated endpoints (today:
PI approval of a form, `POST /api/v1/execution/forms/{id}/approve`) need an `X-Sig-Token` next to the
bearer token:

1. The signed-in client sends `POST /api/v1/auth/signature-verification` with
   `{ "password", "totp"?, "action", "semantic_action", "batch_id"? }`, where `action` is the path being signed
   and `semantic_action` its meaning (`execution.form.approve`). `username`, if sent, must be the caller's own.
2. The gateway checks the password (and one-time code) with a Keycloak direct-access grant on the
   `OIDC_SIGNATURE_CLIENT_ID` client, ends the session that grant opened, and discards the password.
   Auditors and inspectors get `403`; a wrong password `401`; more than five attempts a minute per signer
   `429`, on top of the realm's brute-force lockout.
3. It answers `{ "sig_token", "expires_in": 60 }` (`Cache-Control: no-store`): an HS256 JWT signed with
   `GATEWAY_SECRET`, carrying the signer, their roles, the action, `sig_ver: v3` and a unique `jti`.
4. The client repeats the request with `X-Sig-Token`. The gateway forwards it inside the signed identity, and
   the service verifies it with `RequireElectronicSignature(...)` (`packages/security`): signature, 60-second
   lifetime, signer, action and semantic action, then consumes it. Anything else is
   `401 REAUTHENTICATION_REQUIRED` and nothing is written.

The claim set and checks are specified in `docs/SDLC/Signature_Token_Cryptographic_Contract.md`.

## Configuration

| Setting | Required | Purpose |
| --- | --- | --- |
| `GATEWAY_SECRET` | yes (gateway and every service) | Shared HMAC key, at least 32 characters. No default; startup fails without it. |
| `OIDC_AUTHORITY` | yes | Token issuer, e.g. `https://idp.example.com/realms/cadence`. |
| `OIDC_AUDIENCE` | no | Expected `aud` claim; validated when set. |
| `OIDC_METADATA_ADDRESS` | no | Internal discovery URL when it differs from the public issuer. |
| `OIDC_REQUIRE_HTTPS_METADATA` | no (default `true`) | Set `false` only for local Keycloak over HTTP. |
| `OIDC_SIGNATURE_CLIENT_ID` | no (default `cadence-esignature`) | Keycloak client with only direct access grants enabled, used to re-check signer passwords. |
| `OIDC_SIGNATURE_CLIENT_SECRET` | no | That client's secret, when it is confidential. |
| `OIDC_TOKEN_ENDPOINT` | no | Token endpoint override; defaults to `<realm>/protocol/openid-connect/token` beside `OIDC_METADATA_ADDRESS` or `OIDC_AUTHORITY`. |
| `<SERVICE>_SERVICE_URL` | outside Development | Backend base URLs, e.g. `EXECUTION_SERVICE_URL`; absolute http(s) URLs. |
| `GATEWAY_PROXY_TIMEOUT_SECONDS` | no (default `30`) | Idle time after which a proxied request fails with 504. |
| `GATEWAY_HEALTH_CHECK_INTERVAL_SECONDS` | no (default `10`) | How often each service's `/healthz/ready` is probed. |
| `GATEWAY_HEALTH_CHECK_TIMEOUT_SECONDS` | no (default `5`) | Time limit for one probe. |
| `GATEWAY_RATE_LIMIT_PER_MINUTE` | no (default `600`) | Requests per caller per minute before 429. |
| `CADENCE_TRUSTED_PROXIES` | behind a proxy | Proxies whose `X-Forwarded-For`/`-Proto` are honoured (IPs or CIDR ranges). |

## Routes

Each route forwards a whole path prefix to one service. Service addresses come from `<SERVICE>_SERVICE_URL`. In
Development a missing address defaults to `http://localhost:<port>` (the ports in
[docs/LOCAL_DEV_ENVIRONMENT.md](../../docs/LOCAL_DEV_ENVIRONMENT.md)); in any other environment the gateway refuses to
start without it. An address that is not an absolute http or https URL fails startup everywhere
(`tests/Gateway/GatewayServiceUrlValidationTests.cs`).

| Path prefix | Service | Address key |
| --- | --- | --- |
| `/api/v1/studies/**` | designer | `DESIGNER_SERVICE_URL` |
| `/api/v1/protocols/**` | designer | `DESIGNER_SERVICE_URL` |
| `/api/v1/observations/**` | execution | `EXECUTION_SERVICE_URL` |
| `/api/v1/forms/**` | execution | `EXECUTION_SERVICE_URL` |
| `/api/v1/queries/**` | execution | `EXECUTION_SERVICE_URL` |
| `/api/v1/execution/**` | execution | `EXECUTION_SERVICE_URL` |
| `/api/v1/etmf/**` | etmf | `ETMF_SERVICE_URL` |
| `/api/v1/interop/**` | interop | `INTEROP_SERVICE_URL` |
| `/api/v1/quality/**` | quality | `QUALITY_SERVICE_URL` |
| `/api/v1/notifications/**` | notifications | `NOTIFICATIONS_SERVICE_URL` |
| `/api/v1/ctms/**` | ctms | `CTMS_SERVICE_URL` |
| `/api/v1/safety/**` | safety | `SAFETY_SERVICE_URL` |
| `/api/v1/tickets/**` | tickets | `TICKETS_SERVICE_URL` |
| `/api/v1/eisf/**` | eisf | `EISF_SERVICE_URL` |
| `/api/v1/econsent/**` | econsent | `ECONSENT_SERVICE_URL` |
| `/api/v1/org/**` | org | `ORG_SERVICE_URL` |
| `/api/v1/fileshare/**` | fileshare | `FILESHARE_SERVICE_URL` |
| `/api/v1/imaging/**` | imaging | `IMAGING_SERVICE_URL` |
| `/api/v1/knowledge/**` | knowledge | `KNOWLEDGE_SERVICE_URL` |
| `/api/v1/payments/**` | payments | `PAYMENTS_SERVICE_URL` |
| `/api/v1/ai/**` | ai-gateway | `AI_GATEWAY_SERVICE_URL` |

`tests/Gateway/GatewayRouteCoverageTests.cs` boots the gateway and every service and fails when a route leads to a
prefix its service does not map, or when a service maps an `/api/v1` endpoint no route reaches. Add or remove a
route in `src/Program.cs` together with the endpoints it serves
([ADR-2448](../../docs/adr/2026-09-29-gateway-routes-verified-against-service-endpoints.md)).

## Timeouts and health checks

Every cluster gets the same policy from `src/GatewayClusters.cs`
([ADR-2451](../../docs/adr/2026-09-29-gateway-timeouts-and-health-checked-clusters.md)):

- A request fails with **504** once its service has sent nothing for `GATEWAY_PROXY_TIMEOUT_SECONDS`.
- The gateway probes each service's `/healthz/ready`. After two failed probes in a row it answers **503** for that
  service without forwarding, until a probe succeeds again. Repeated connection failures also take a service out for
  30 seconds.
- The gateway never retries a request, so a write is never sent twice.

## Rate limiting and forwarded headers

`src/GatewayRateLimiting.cs` gives every caller `GATEWAY_RATE_LIMIT_PER_MINUTE` requests (default 600) per one-minute
window. An authenticated caller is counted by the token's subject, anyone else by client address. Over the limit the
gateway answers **429** with `Retry-After` and forwards nothing. Health probes are not limited
([ADR-2491](../../docs/adr/2026-09-30-http-edge-hardening-forwarded-headers-hsts-rate-limits.md)).

The client address comes from `X-Forwarded-For`, which the gateway honours only from the proxies in
`CADENCE_TRUSTED_PROXIES` (see [packages/telemetry](../../packages/telemetry/README.md)). Behind a front proxy, set it
to the proxy's address; otherwise every caller behind the proxy shares one limit.

## Tests

```bash
dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Gateway"
```
