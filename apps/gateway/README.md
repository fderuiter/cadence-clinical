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

## Tests

```bash
dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Gateway"
```
