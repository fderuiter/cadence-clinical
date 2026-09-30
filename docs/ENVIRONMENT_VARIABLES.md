# Environment Variables

Every configuration key the .NET services, CLI and web apps read, with where it is read. The template with local
defaults is [`.env.example`](../.env.example). `cadence check` warns when code reads an environment variable that
`.env.example` does not list, and when `.env.example` lists a key this page does not.

ASP.NET Core services read these through `IConfiguration`, so environment variables, `appsettings*.json` and
command-line arguments all work; `__` in an environment variable name maps to `:` (for example
`ConnectionStrings__cadence` is `ConnectionStrings:cadence`). CLI and library code that calls
`Environment.GetEnvironmentVariable` reads the process environment only.

"Required" means the service (or tool) fails with an error naming the key when it is unset. There are no built-in
fallback credentials in any environment, Development included (ADR-2260).

## Hosting

| Key | Read by | Default | Required |
| --- | --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | every service (ASP.NET Core host); `packages/cli/src/Services/DatabaseResetService.cs:123` | `Production` | no |
| `CADENCE_ENV` | `packages/cli/src/Services/DatabaseResetService.cs:122` (refuses `db reset` in Production) | `Development` | no |
| `ENVIRONMENT` | `packages/cli/src/Services/DatabaseResetService.cs:124` | `Development` | no |
| `CADENCE_TRUSTED_PROXIES` | every service (`packages/telemetry/src/CadenceHttpHardening.cs`, ADR-2491) | none; only loopback is trusted | set it behind a proxy. Comma-separated IP addresses or CIDR ranges of the front proxy and gateway. `X-Forwarded-For` and `X-Forwarded-Proto` from any other sender are ignored, and an entry that is not an address or range fails startup |

## Database

All services share the `cadence` database, each in its own schema; see [OPERATIONAL_DATABASE.md](OPERATIONAL_DATABASE.md).

| Key | Read by | Default | Required |
| --- | --- | --- | --- |
| `ConnectionStrings__cadence` | `packages/database/src/CadenceDatabaseExtensions.cs` (`ResolvePostgresConnectionString`), used by every service with a DbContext | none | yes (or `POSTGRES_CONNECTION_STRING`) |
| `POSTGRES_CONNECTION_STRING` | same resolver, after `ConnectionStrings__cadence`; every design-time `*DbContextFactory.cs` (`dotnet ef`, via `ResolveDesignTimeConnectionString` or `-- --connection`); `packages/cli/src/Services/DatabaseResetService.cs` | none | yes (or `ConnectionStrings__cadence`); `dotnet ef` and `db reset` need it |
| `Database__MigrateOnStartup` | `packages/database/src/CadenceMigrationHostedService.cs` (every service with a DbContext) | `false` | no; applies migrations before serving |
| `Database__MigrateOnly` | same | `false` | no; applies migrations and exits 0 (deployment job) |
| `DATABASE_URL` | `packages/cli/src/Services/DatabaseResetService.cs:174` (accepts `postgresql+asyncpg://` URLs) | none | no |

## Execution service

| Key | Read by | Default | Required |
| --- | --- | --- | --- |
| `DemographicsEncryption__KeyBase64` | `apps/execution/src/Program.cs:105` (`DemographicsEncryptionOptions`) | none | yes; Base64 of a 16, 24 or 32 byte AES key (`openssl rand -base64 32`). The service refuses to start without a valid key |

## Gateway routing

Read by `apps/gateway/src/Program.cs:105-121`. In Development each defaults to `http://localhost:<port>`; in any other environment each is required and the gateway refuses to start without it. A value that is not an absolute http or https URL fails startup in every environment.

| Key | Default port |
| --- | --- |
| `DESIGNER_SERVICE_URL` | 8001 |
| `EXECUTION_SERVICE_URL` | 8002 |
| `ETMF_SERVICE_URL` | 8003 |
| `INTEROP_SERVICE_URL` | 8004 |
| `QUALITY_SERVICE_URL` | 8005 |
| `NOTIFICATIONS_SERVICE_URL` | 8006 |
| `CTMS_SERVICE_URL` | 8007 |
| `SAFETY_SERVICE_URL` | 8008 |
| `TICKETS_SERVICE_URL` | 8009 |
| `EISF_SERVICE_URL` | 8010 |
| `ECONSENT_SERVICE_URL` | 8011 |
| `ORG_SERVICE_URL` | 8012 |
| `FILESHARE_SERVICE_URL` | 8013 |
| `IMAGING_SERVICE_URL` | 8014 |
| `KNOWLEDGE_SERVICE_URL` | 8015 |
| `PAYMENTS_SERVICE_URL` | 8016 |
| `AI_GATEWAY_SERVICE_URL` | 8017 |

Timeouts and health checks for every route, read by `apps/gateway/src/GatewayClusters.cs` (ADR-2451). Each must be a positive number of seconds; the gateway refuses to start otherwise.

| Key | Default | Purpose |
| --- | --- | --- |
| `GATEWAY_PROXY_TIMEOUT_SECONDS` | 30 | A proxied request fails with 504 once its service sends nothing for this long. |
| `GATEWAY_HEALTH_CHECK_INTERVAL_SECONDS` | 10 | How often the gateway probes each service's `/healthz/ready`. Two failures in a row make the gateway answer 503 for that service until a probe succeeds. |
| `GATEWAY_HEALTH_CHECK_TIMEOUT_SECONDS` | 5 | How long one readiness probe may take before it counts as failed. |

Per-caller rate limit, read by `apps/gateway/src/GatewayRateLimiting.cs` (ADR-2491). It must be a positive number; the gateway refuses to start otherwise.

| Key | Default | Purpose |
| --- | --- | --- |
| `GATEWAY_RATE_LIMIT_PER_MINUTE` | 600 | Requests each caller may send per one-minute window, counted by token subject, or by client address for unauthenticated requests. Over the limit the gateway answers 429 with `Retry-After`. Health probes are not limited. |

## Authentication and audit

| Key | Read by | Default | Required |
| --- | --- | --- | --- |
| `GATEWAY_SECRET` | `packages/security/src/DependencyInjection/CadenceSecurityServiceCollectionExtensions.cs` (every service), `apps/gateway/src/Program.cs:60`, `packages/security/src/Authentication/SigTokenVerifier.cs:130` | none | yes, 32+ characters; services refuse to start without it |
| `OIDC_AUTHORITY` | `apps/gateway/src/Program.cs:28` | none | yes (gateway) |
| `OIDC_METADATA_ADDRESS` | `apps/gateway/src/Program.cs:32` | none | no |
| `OIDC_AUDIENCE` | `apps/gateway/src/Program.cs:31` | none; validated when set | set it in production |
| `OIDC_REQUIRE_HTTPS_METADATA` | `apps/gateway/src/Program.cs:43` | `true` | no |
| `OIDC_SIGNATURE_CLIENT_ID` | `apps/gateway/src/Program.cs:73` | `cadence-esignature` | no; Keycloak client with only direct access grants, used for e-signature re-authentication |
| `OIDC_SIGNATURE_CLIENT_SECRET` | `apps/gateway/src/Program.cs:76` | none | only if that client is confidential |
| `OIDC_TOKEN_ENDPOINT` | `apps/gateway/src/Program.cs:72` | `<realm>/protocol/openid-connect/token` beside `OIDC_METADATA_ADDRESS` or `OIDC_AUTHORITY` | no |
| `AUDIT_LOG_SECRET_KEY` | `packages/security/src/Audit/AuditSecretKey.cs` (the only resolver; used by `AuditDigestEngine`, both `AuditDigestCalculator`s and `AddCadenceSecurity`) | none | yes; signing or verifying the audit chain throws without it |
| `GXP_SIGNER_ID` | `packages/compliance/src/GxPSigner.cs:67` (release signing with `SignGxpMarkdown` only; `gxp sync` stamps, it does not sign) | `cadence-validation-runner` | no |

## Observability

| Key | Read by | Default | Required |
| --- | --- | --- | --- |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `packages/telemetry/src/CadenceTelemetryExtensions.cs` (enables OTLP export of traces, metrics and logs); the OpenTelemetry SDK also honours the other standard `OTEL_EXPORTER_OTLP_*` keys | unset: nothing exported | no |
| `OTEL_SERVICE_NAME` | `packages/telemetry/src/CadenceTelemetryExtensions.cs` | the service's assembly name | no |

## Tooling

| Key | Read by | Default |
| --- | --- | --- |
| `BRAND_DOMAIN` | `packages/tooling_core/src/OpenApiToolingService.cs:171` | `ccrsoft.com` |
| `GH_TOKEN`, `GITHUB_TOKEN` | `packages/tooling_core/src/Services/SyncGithubProjectService.cs:10` | none |
| `RTM_DRAFT`, `GENERATE_RTM_DRAFT` | `packages/compliance/src/RtmGeneratorService.cs:101-102` (honoured by `generate-rtm` and `check rtm`; `gxp sync` ignores them) | off |
| `RTM_STRICT_ALL` | `packages/compliance/src/RtmGeneratorService.cs:214` | off |
| `CADENCE_TEST_POSTGRES`, `CADENCE_TEST_DOCKER` | `tests/Execution/Integration/ExecutionPostgresFixture.cs` (connection string, or `1` for a Testcontainers `postgres:16-alpine`; without either, PostgreSQL tests are skipped) | none |

## Web apps

Vite inlines `VITE_*` values at build time. Put overrides in `apps/<app>/.env.local`.

| Key | Read by | Default |
| --- | --- | --- |
| `VITE_API_BASE_URL` | `apps/web/src/api/apiClient.js:5`, `apps/web/src/api/client.ts:7`, `apps/web/src/stores/auth.js:292` | `http://localhost:8000` |
| `VITE_BASE_PATH` | `apps/web/vite.config.js:11` | `/cadence-clinical/` |
| `VITE_APP_TITLE` | `apps/web/vite.config.js:17`, `apps/subject-portal/vite.config.js:17` | app name |
| `VITE_BRAND_NAME` | `apps/web/src/App.vue:22`, `apps/web/src/components/AppShell.vue:841`, `apps/subject-portal/src/App.vue:1491` | `Cadence Clinical` |
| `VITE_KEYCLOAK_URL` | `apps/web/src/main.js:25`, `apps/subject-portal/src/index.js:2413` | `http://localhost:8080/` |
| `VITE_KEYCLOAK_REALM` | `apps/web/src/main.js:26`, `apps/subject-portal/src/index.js:2414` | `cadence` |
| `VITE_KEYCLOAK_CLIENT_ID` | `apps/web/src/main.js:28`, `apps/subject-portal/src/index.js:2415` | `cadence-web` |
| `VITE_DEMO_MODE` | `apps/web/src/utils/demoMode.js` | off. `true` (or `vite --mode demo`, `pnpm build:demo`) makes a demo build: offline demo identity, MSW mock API and sample data. Any other build shows API failures as errors and never substitutes sample data |
| `VITE_ENABLE_MSW` | `apps/web/src/main.js`, `apps/subject-portal/src/index.js:2331` | off. Web: starts the MSW mock worker without the rest of demo mode (demo builds start it anyway) |
