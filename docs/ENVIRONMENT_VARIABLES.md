# Environment Variables

Every configuration key the .NET services, CLI and web apps read, with where it is read. The template with local
defaults is [`.env.example`](../.env.example). `cadence check` warns when code reads an environment variable that
`.env.example` does not list, and when `.env.example` lists a key this page does not.

ASP.NET Core services read these through `IConfiguration`, so environment variables, `appsettings*.json` and
command-line arguments all work; `__` in an environment variable name maps to `:` (for example
`ConnectionStrings__cadence` is `ConnectionStrings:cadence`). CLI and library code that calls
`Environment.GetEnvironmentVariable` reads the process environment only.

"Required" means the service fails at startup outside the Development environment when the key is unset.

## Hosting

| Key | Read by | Default | Required |
| --- | --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | every service (ASP.NET Core host); `packages/cli/src/Services/DatabaseResetService.cs:123` | `Production` | no |
| `CADENCE_ENV` | `packages/cli/src/Services/DatabaseResetService.cs:122` (refuses `db reset` in Production) | `Development` | no |
| `ENVIRONMENT` | `packages/cli/src/Services/DatabaseResetService.cs:124` | `Development` | no |

## Database

All services share the `cadence` database, each in its own schema; see [OPERATIONAL_DATABASE.md](OPERATIONAL_DATABASE.md).

| Key | Read by | Default | Required |
| --- | --- | --- | --- |
| `ConnectionStrings__cadence` | `packages/database/src/CadenceDatabaseExtensions.cs` (`ResolvePostgresConnectionString`), used by every service with a DbContext | local compose database in Development only | yes (or `POSTGRES_CONNECTION_STRING`) |
| `POSTGRES_CONNECTION_STRING` | same resolver, after `ConnectionStrings__cadence`; every `apps/*/src/Infrastructure/Persistence/*DbContextFactory.cs` (design-time `dotnet ef`); `packages/cli/src/Services/DatabaseResetService.cs:173` | local compose database | yes (or `ConnectionStrings__cadence`) |
| `Database__MigrateOnStartup` | `packages/database/src/CadenceMigrationHostedService.cs` (every service with a DbContext) | `false` | no; applies migrations before serving |
| `Database__MigrateOnly` | same | `false` | no; applies migrations and exits 0 (deployment job) |
| `DATABASE_URL` | `packages/cli/src/Services/DatabaseResetService.cs:174` (accepts `postgresql+asyncpg://` URLs) | none | no |

## Execution service

| Key | Read by | Default | Required |
| --- | --- | --- | --- |
| `DemographicsEncryption__KeyBase64` | `apps/execution/src/Program.cs:106` (`DemographicsEncryptionOptions`) | empty; encryption fails on first use | effectively yes |

## Gateway routing

Read by `apps/gateway/src/Program.cs:68-84`. Each defaults to `http://localhost:<port>`.

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

## Authentication and audit

| Key | Read by | Default | Required |
| --- | --- | --- | --- |
| `GATEWAY_SECRET` | `packages/security/src/DependencyInjection/CadenceSecurityServiceCollectionExtensions.cs` (every service), `apps/gateway/src/Program.cs:55`, `packages/security/src/Authentication/SigTokenVerifier.cs:38` | none | yes, 32+ characters; services refuse to start without it |
| `OIDC_AUTHORITY` | `apps/gateway/src/Program.cs:23` | none | yes (gateway) |
| `OIDC_METADATA_ADDRESS` | `apps/gateway/src/Program.cs:27` | none | no |
| `OIDC_AUDIENCE` | `apps/gateway/src/Program.cs:26` | none; validated when set | set it in production |
| `OIDC_REQUIRE_HTTPS_METADATA` | `apps/gateway/src/Program.cs:38` | `true` | no |
| `AUDIT_LOG_SECRET_KEY` | `packages/security/src/Audit/AuditDigestEngine.cs:27,45`, `packages/compliance/src/AuditDigestCalculator.cs:26` | hardcoded test values (they differ) | should be; see #4759 |
| `GXP_SIGNER_ID` | `packages/compliance/src/GxPSigner.cs:18` | `cadence-validation-runner` | no |

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
| `RTM_DRAFT`, `GENERATE_RTM_DRAFT` | `packages/compliance/src/RtmGeneratorService.cs:59-60` | off |
| `RTM_STRICT_ALL` | `packages/compliance/src/RtmGeneratorService.cs:152` | off |
| `NEO4J_URI`, `NEO4J_USER`, `NEO4J_PASSWORD` | `packages/cli/src/Services/DatabaseResetService.cs:44-46` (Neo4j is being retired, #4790) | `bolt://localhost:7687`, `neo4j`, `password123` |

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
| `VITE_ENABLE_MSW` | `apps/web/src/main.js:138`, `apps/subject-portal/src/index.js:2331` | off (MSW always runs in dev and `--mode demo`) |
