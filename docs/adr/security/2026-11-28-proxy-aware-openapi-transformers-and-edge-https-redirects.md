# ADR-SECURITY-20261128-proxy-aware-openapi-transformers-and-edge-https-redirects: Proxy-Aware OpenAPI Transformers and Edge HTTPS Redirects

- **Status:** Accepted
- **Date:** 2026-11-28
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Cadence microservices and the YARP API gateway operate behind external reverse proxies, ingress controllers, or the local Nginx front proxy. Previously:

1. `CadenceHttpHardening.ConfigureForwardedHeaders` (`packages/telemetry/src/CadenceHttpHardening.cs`, ADR-2491) honored `X-Forwarded-For` and `X-Forwarded-Proto` from `CADENCE_TRUSTED_PROXIES`, but ignored `X-Forwarded-Host` and `X-Forwarded-Prefix` and lacked a forwarded-host allowlist.
2. Service (`CadenceTelemetryExtensions`) and kernel module (`CadenceModuleRegistration`) OpenAPI registrations omitted document server transformers, causing offline contract exports (`Cadence.OpenApiExporter`) to emit `http://localhost/` across all 19 `docs/openapi/*_openapi.json` contracts and live proxy deployments to omit external host/prefix origins.
3. Unified gateway aggregation (`OpenApiSyncService.AggregateOpenApiSpecs`) appended `/api/v1` to `cdisc_openapi.json` `servers` entries alongside `/api/v1/*` path keys, producing duplicated `/api/v1/api/v1/*` client URLs, and the gateway did not expose unified or per-contract OpenAPI/Scalar routes in `Development`.
4. Neither shared ASP.NET Core web defaults nor the Nginx front proxy provided opt-in HTTP-to-HTTPS redirection that exempted plain-HTTP container liveness and readiness probes (`PRD-SYS-189`, `PRD-SYS-020`, `PRD-SYS-046`).

## 2. Decision Drivers & Constraints

- **Proxy spoofing resistance (`PRD-SYS-189`):** Forwarded scheme, client IP, host, and path prefix headers must remain ignored from untrusted senders, and operators must be able to restrict accepted forwarded hostnames via an allowlist.
- **Accurate SDK and Scalar contracts (`PRD-SYS-020`):** Committed OpenAPI specs must resolve valid operation paths without `http://localhost/` placeholders or `/api/v1/api/v1` duplication, while live `Development` endpoints reflect external reverse-proxy origins.
- **Non-disruptive east-west HTTP and probes (`PRD-SYS-046`):** Internal container health checks (`/healthz`, `/healthz/live`, `/healthz/ready`, `/_proxy_health`), YARP `/api/v1/{prefix}/{**catch-all}` routes, and default local Vite HTTP proxying on port `8000` must continue working over plain HTTP.

## 3. Options Considered

### Option 1: Proxy-Aware OpenAPI Transformers, Root Gateway Origins, and Probe-Exempt Opt-In HTTPS Redirects (Selected)

- **Overview:**
  - Extend `CadenceHttpHardening.ConfigureForwardedHeaders` to honor `XForwardedFor | XForwardedProto | XForwardedHost | XForwardedPrefix` from `CADENCE_TRUSTED_PROXIES` and validate optional `CADENCE_ALLOWED_HOSTS` entries into `ForwardedHeadersOptions.AllowedHosts`.
  - Attach OpenAPI document transformers in `CadenceTelemetryExtensions` (`AddCadenceWebDefaults`, `AddCadenceOpenApiDocument`) and `CadenceModuleRegistration.AddCadenceModule` that rewrite `servers` from the active `HttpRequest` (`Scheme`, `Host`, `PathBase`) and fall back to `CADENCE_OPENAPI_PUBLIC_ORIGIN` or `/` during offline `TestServer` export.
  - Publish root gateway origins (`https://api.ccrsoft.com`, `http://localhost:8000`) without `/api/v1` in `cdisc_openapi.json`, and map anonymous `Development`-only `/openapi/cdisc.json`, `/openapi/{contract}.json`, and Scalar document routes on `apps/gateway` outside YARP's route table.
  - Add opt-in `CADENCE_HTTPS_REDIRECT_ENABLED` (`CADENCE_HTTPS_PORT`, default `443`) in `UseCadenceWebDefaults` outside `Development` exempting `/healthz`, `/healthz/live`, and `/healthz/ready`, paired with an opt-in Nginx TLS configuration (`docker/nginx/tls/default.conf`, `docker/docker-compose.tls.yml`) that redirects HTTP to HTTPS (`308`), preserves `/_proxy_health` on HTTP, and forwards `X-Forwarded-Proto: https` and `X-Forwarded-Host`.
- **Pros:**
  - ✅ Satisfies `PRD-SYS-189`, `PRD-SYS-020`, and `PRD-SYS-046` across standalone services, kernel modules, offline SDK exports, and the API gateway.
  - ✅ Preserves fail-closed startup validation on malformed proxy/host/origin/port configuration and keeps east-west plain-HTTP probes intact.
- **Cons:**
  - ❌ Operators terminating TLS upstream must configure `CADENCE_TRUSTED_PROXIES` (and optionally `CADENCE_ALLOWED_HOSTS`) so forwarded headers take effect.

### Option 2: Hardcoded Static Server URLs and Unconditional HTTPS Redirection

- **Overview:** Bake static environment URLs into OpenAPI specs at build time and enable unconditional `UseHttpsRedirection()` outside `Development`.
- **Pros:**
  - ✅ Minimal configuration surface.
- **Cons:**
  - ❌ Breaks container orchestrator HTTP probes on `/healthz/live` and `/healthz/ready` and fails under custom reverse-proxy hostnames or path prefixes.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Option 1 eliminates broken `http://localhost/` and `/api/v1/api/v1` OpenAPI client URLs, accurately reflects trusted reverse-proxy origins in live OpenAPI and Scalar endpoints, and enforces edge HTTPS redirects without breaking internal container health checks or local plain-HTTP development workflows.

## 5. Consequences & Trade-offs

- **Positive Impact:** External SDK generators and Scalar users receive valid operation URLs across direct, gateway, and reverse-proxy topologies; edge deployments can enforce HTTP-to-HTTPS redirects at Nginx and/or ASP.NET Core hosts while keeping probes healthy.
- **Negative Impact / Technical Debt:** Four new optional hosting configuration keys (`CADENCE_ALLOWED_HOSTS`, `CADENCE_OPENAPI_PUBLIC_ORIGIN`, `CADENCE_HTTPS_REDIRECT_ENABLED`, `CADENCE_HTTPS_PORT`) must be maintained in `.env.example` and `docs/ENVIRONMENT_VARIABLES.md`.
- **Mitigation Strategy:** Enforced continuously by `cadence check env-vars`, `cadence check openapi`, `cadence check api-clients`, and unit/gateway test suites.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/telemetry`, `packages/kernel/src/Cadence.Kernel.Modules`, `packages/tooling_core`, `apps/gateway`, `docker/nginx/tls/default.conf`, `docker/docker-compose.tls.yml`, `docs/openapi/*`.
- **Verification Plan:** Verified via `HttpHardeningTests`, `TelemetryUnitTests`, `OpenApiDriftTests`, `GatewayOpenApiTests`, `GatewayRouteCoverageTests`, and `cadence check` (`openapi`, `api-clients`, `env-vars`, `adrs`).
