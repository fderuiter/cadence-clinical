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
   (`apps/gateway/src/GatewayIdentityForwarding.cs`).
3. Each backend service (`AddCadenceSecurity`/`UseCadenceSecurity` in `packages/security`) accepts a
   request only with a valid signature inside a two-minute window, and requires an authenticated caller on
   every endpoint except health, status and development API docs. `ArchitectureSentinelTests` fails the
   build when a service's `Program.cs` skips this wiring.

See ADR-2250 (`docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md`).

Only `/healthz` and `/api/v1/status` are anonymous on the gateway itself.

## Configuration

| Setting | Required | Purpose |
| --- | --- | --- |
| `GATEWAY_SECRET` | yes (gateway and every service) | Shared HMAC key, at least 32 characters. No default; startup fails without it. |
| `OIDC_AUTHORITY` | yes | Token issuer, e.g. `https://idp.example.com/realms/cadence`. |
| `OIDC_AUDIENCE` | no | Expected `aud` claim; validated when set. |
| `OIDC_METADATA_ADDRESS` | no | Internal discovery URL when it differs from the public issuer. |
| `OIDC_REQUIRE_HTTPS_METADATA` | no (default `true`) | Set `false` only for local Keycloak over HTTP. |
| `<SERVICE>_SERVICE_URL` | no | Backend base URLs, e.g. `EXECUTION_SERVICE_URL`. |

## Routes

Each route forwards a whole path prefix to one service. Service addresses come from `<SERVICE>_SERVICE_URL` and
default to `http://localhost:<port>` (the ports in [docs/LOCAL_DEV_ENVIRONMENT.md](../../docs/LOCAL_DEV_ENVIRONMENT.md)).

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

## Tests

```bash
dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Gateway"
```
