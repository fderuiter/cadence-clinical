# Environment Variables

Every configuration key the .NET services, CLI and web apps read, with where it
is read. The template with local defaults is [`.env.example`](../.env.example).
`cadence check` warns when code reads an environment variable that
`.env.example` does not list, and when `.env.example` lists a key this page does
not.

ASP.NET Core services read these through `IConfiguration`, so environment
variables, `appsettings*.json` and command-line arguments all work; `__` in an
environment variable name maps to `:` (for example `ConnectionStrings__cadence`
is `ConnectionStrings:cadence`). CLI and library code that calls
`Environment.GetEnvironmentVariable` reads the process environment only.

"Required" means the service (or tool) fails with an error naming the key when
it is unset. There are no built-in fallback credentials in any environment,
Development included (ADR-2260).

## Hosting

- `ASPNETCORE_ENVIRONMENT` — Read by every service (ASP.NET Core host) and
  `packages/cli/src/Services/DatabaseResetService.cs`. Default: `Production`.
  Not required.
- `CADENCE_ENV` — Read by `packages/cli/src/Services/DatabaseResetService.cs`
  (refuses `db reset` in Production). Default: `Development`. Not required.
- `ENVIRONMENT` — Read by `packages/cli/src/Services/DatabaseResetService.cs`.
  Default: `Development`. Not required.
- `CADENCE_TRUSTED_PROXIES` — Read by every service
  (`packages/telemetry/src/CadenceHttpHardening.cs`, ADR-2491). Default: none
  (only loopback is trusted). Set behind a proxy as comma-separated IP addresses
  or CIDR ranges of the front proxy and gateway; `X-Forwarded-For` and
  `X-Forwarded-Proto` from any other sender are ignored, and an entry that is
  not an address or range fails startup.
- `AppHost__Profile` — Read by `apps/apphost/src/Program.cs` (Aspire AppHost
  only). Default: `full`. Not required; `full` runs every service, `core` runs
  the gateway, execution, kernel and web only
  ([AppHost README](../apps/apphost/README.md#profiles-and-existing-infrastructure)),
  and any other value stops the AppHost.
- `AppHost__OidcAuthority` — Read by `apps/apphost/src/Program.cs` (Aspire
  AppHost only). Default: none (the AppHost runs a Keycloak container). Not
  required; an existing issuer URL ending in `/realms/<realm>` replaces the
  Keycloak container.
- `ConnectionStrings__cadence` (AppHost) — Read by
  `apps/apphost/src/Program.cs`. Default: none (the AppHost runs a PostgreSQL
  container). Not required; an existing database replaces the PostgreSQL
  container and is passed to every service.

## Database

All services share the `cadence` database, each in its own schema; see
[OPERATIONAL_DATABASE.md](OPERATIONAL_DATABASE.md).

- `ConnectionStrings__cadence` — Read by
  `packages/database/src/CadenceDatabaseExtensions.cs`
  (`ResolvePostgresConnectionString`), used by every service with a `DbContext`,
  `packages/cli/src/Services/DatabaseResetService.cs` (`db reset`,
  `db migrate`), and `Cadence.DatabaseMigrator`. Default: none. Required (or
  `POSTGRES_CONNECTION_STRING`).
- `POSTGRES_CONNECTION_STRING` — Read by the same resolvers after
  `ConnectionStrings__cadence` and every design-time `*DbContextFactory.cs`
  (`dotnet ef`, via `ResolveDesignTimeConnectionString` or `-- --connection`).
  Default: none. Required (or `ConnectionStrings__cadence`); `dotnet ef`,
  `db reset`, and `db migrate` need it.
- `Database__MigrateOnStartup` — Read by
  `packages/database/src/CadenceMigrationHostedService.cs` (every service with a
  `DbContext`). Default: `false`. Not required; applies migrations before
  serving.
- `Database__MigrateOnly` — Read by
  `packages/database/src/CadenceMigrationHostedService.cs`. Default: `false`.
  Not required; applies migrations and exits 0 (deployment job).

## Execution service

- `DemographicsEncryption__KeyBase64` — Read by
  `apps/execution/src/Program.cs:105` (`DemographicsEncryptionOptions`).
  Default: none. Required: Base64 of a 16, 24 or 32 byte AES key
  (`openssl rand -base64 32`). The service refuses to start without a valid key.
  Under the Aspire AppHost, set it as the `demographics-encryption-key`
  parameter.
- `RANDOMIZATION_ALLOCATION_KEY` — Read by `apps/execution/src/Program.cs`
  (`AllocationProtectionOptions`). Default: none. Required: Base64 of a 32-byte
  key (`openssl rand -base64 32`), different from every other key. Encrypts
  randomisation list slots, allocations and kit arm labels (#5607, ADR-2640);
  the service refuses to start without a valid key. Under the Aspire AppHost,
  set it as the `randomization-allocation-key` parameter.

## Kernel API host

- `Kernel__DevelopmentTenant__Code` — Read by
  `apps/kernel-api/src/DevelopmentTenantBootstrap.cs` (ADR-2542). Default: none.
  Not required; Development only. Provisions the kernel tenant with this code on
  start, unless an active one exists. `docker compose` and the AppHost set
  `cadence-local`. The host refuses to start with it in any other environment.
- `Kernel__DevelopmentTenant__Name` — Read by
  `apps/kernel-api/src/DevelopmentTenantBootstrap.cs`. Default:
  `Local development (<code>)`. Not required.
- `Kernel__DevelopmentTenant__DemoData` — Read by
  `apps/kernel-api/src/DevelopmentTenantBootstrap.cs` (ADR-2543). Default:
  `false`. Not required; read only with `Kernel__DevelopmentTenant__Code`. Seeds
  the demo study `CADENCE-101` and grants the local realm's users.
  `docker compose` and the AppHost set `true`.
- `KeycloakAdmin__BaseUrl` — Read by
  `apps/kernel-api/src/Onboarding/KeycloakOnboardingClient.cs`. Default: none.
  Required for `POST /api/v1/kernel/onboarding/trial` (ADR-2547, ADR-2605);
  blank or incomplete settings fail closed with
  `502 K.Commercial.IdentityProviderSyncFailed`.
- `KeycloakAdmin__Realm` — Read by
  `apps/kernel-api/src/Onboarding/KeycloakOnboardingClient.cs`. Default:
  `cadence`. Required for trial onboarding; target Keycloak realm receiving
  `ADMIN_EDIT` `tenant_id` attribute and initial trial roles (`Sponsor Admin`,
  `Sponsor Designer`).
- `KeycloakAdmin__AdminUsername` — Read by
  `apps/kernel-api/src/Onboarding/KeycloakOnboardingClient.cs`. Default:
  `admin`. Required for trial onboarding; Keycloak admin username used to obtain
  short-lived Admin REST tokens.
- `KeycloakAdmin__AdminPassword` — Read by
  `apps/kernel-api/src/Onboarding/KeycloakOnboardingClient.cs`. Default: none.
  Required for trial onboarding; Keycloak admin password used to obtain
  short-lived Admin REST tokens.
- `CommercialQuote__SigningSecret` — Read by `apps/kernel-api/src/Program.cs`
  (ADR-2605). Default: falls back to `Security__GatewaySecret`. Not required;
  HMAC-SHA256 secret used to verify signed `SubscriptionQuoteToken`
  (`qtk1.<payload>.<sig>`) tokens during
  `POST /api/v1/kernel/entitlements/activate`.

## Intelligence module

The `Intelligence` configuration section (`IntelligenceOptions`) of the
Intelligence module, which the kernel API host composes (ADR-2630). Without a
provider, agent tasks have no model to call; the intent router
(`POST /api/v1/ai/intent/route`) is rule-based and needs none.

- `Intelligence__Anthropic__ApiKey` — Read by
  `apps/intelligence/src/IntelligenceModule.cs`. Default: none (falls back to
  `ANTHROPIC_API_KEY`). Not required; never defaulted. Without a key (here or in
  `ANTHROPIC_API_KEY`) the Anthropic provider is not configured.
- `ANTHROPIC_API_KEY` — Read by `apps/intelligence/src/IntelligenceModule.cs`.
  Default: none. Not required; read only when `Intelligence__Anthropic__ApiKey`
  is unset.
- `Intelligence__Anthropic__Tiers__<tier>` — Read by
  `apps/intelligence/src/IntelligenceModule.cs`. Defaults: `deep` =
  `claude-opus-5-5`, `balanced` = `claude-sonnet-5-5`, `fast` =
  `claude-haiku-5-5`. Not required; the model a task configuration's `tier:`
  alias resolves to.
- `Intelligence__FakeProvider` — Read by
  `apps/intelligence/src/IntelligenceModule.cs`. Default: `false`. Not required;
  registers the deterministic fake provider, so tests and local stacks never
  call a model. The host refuses to start with it outside the Development and
  `Testing` environments.
- `Intelligence__RequestTimeoutSeconds` — Read by
  `apps/intelligence/src/IntelligenceModule.cs`. Default: `120`. Not required;
  how long one model call may take.
- `Intelligence__Prices__<model>__InputPerMillion` — Read by
  `apps/intelligence/src/IntelligenceModule.cs`. Default: none. Not required; US
  dollars per million input tokens, for the agent-run ledger's cost. A model
  without a price records no cost.
- `Intelligence__Prices__<model>__OutputPerMillion` — Read by
  `apps/intelligence/src/IntelligenceModule.cs`. Default: none. Not required; US
  dollars per million output tokens.

## Intelligence inference

The model providers, routing and agent sandbox of the Intelligence module
(ADR-2636). Each provider is configured only when its section is present; a
provider with no section is not registered and no route can reach it.
Credentials for the cloud platforms come from each platform's standard chain
(AWS profile or role, Google application default credentials, Azure
`DefaultAzureCredential`), never from this page. `<P>` is one of `AnthropicAws`,
`Bedrock`, `Vertex`, `Foundry` or `OpenAiCompatible`. Every provider resolves
`tier:` aliases through `Intelligence__Anthropic__Tiers` and prices through
`Intelligence__Prices`, by first-party model id.

Outside Development, every model call goes to an external provider only while
that provider's supplier is `Approved`, the Claude API (`anthropic`) included. A
production deployment approves each one it uses under
`Intelligence__Inference__Suppliers__<id>`. The in-process fake provider needs
no approval.

- `Intelligence__Inference__Mode` — Read by
  `apps/intelligence/src/Inference/Providers/InferenceOptions.cs`. Default:
  `Standard`. Not required; `LocalOnly` refuses every external provider for
  every tenant, whatever the tenant policy.
- `Intelligence__Inference__Providers__<P>__Residency__0` (`__1`, …) — Read by
  `apps/intelligence/src/Inference/Providers/InferenceOptions.cs`. Default:
  none. Not required; the residency codes (2 to 8 upper-case letters, ADR-2458)
  the provider's data stays in. A tenant with a residency only reaches providers
  that declare it. Any other value fails startup.
- `Intelligence__Inference__Providers__<P>__Timeout` — Read by
  `apps/intelligence/src/Inference/Providers/InferenceOptions.cs`. Default:
  `00:10:00`. Not required.
- `Intelligence__Inference__Providers__AnthropicAws__Region` — Read by
  `apps/intelligence/src/Inference/Providers/ClaudePlatformProviders.cs`.
  Default: none. Required with that section (Claude Platform on AWS).
- `Intelligence__Inference__Providers__AnthropicAws__WorkspaceId` — Read by
  `apps/intelligence/src/Inference/Providers/ClaudePlatformProviders.cs`.
  Default: none. Required with that section.
- `Intelligence__Inference__Providers__Bedrock__Region` — Read by
  `apps/intelligence/src/Inference/Providers/ClaudePlatformProviders.cs`.
  Default: none. Required with that section (Amazon Bedrock).
- `Intelligence__Inference__Providers__Vertex__ProjectId` — Read by
  `apps/intelligence/src/Inference/Providers/ClaudePlatformProviders.cs`.
  Default: none. Required with that section (Google Vertex AI).
- `Intelligence__Inference__Providers__Vertex__Region` — Read by
  `apps/intelligence/src/Inference/Providers/ClaudePlatformProviders.cs`.
  Default: none (the global endpoint). Not required; `us` or `eu` use the
  multi-region endpoints, anything else a regional one. The global endpoint
  cannot carry a residency, so `Residency` without `Region` fails startup.
- `Intelligence__Inference__Providers__Foundry__Resource` — Read by
  `apps/intelligence/src/Inference/Providers/ClaudePlatformProviders.cs`.
  Default: none. Required with that section (Microsoft Foundry resource name).
- `Intelligence__Inference__Providers__Foundry__ApiKey` — Read by
  `apps/intelligence/src/Inference/Providers/ClaudePlatformProviders.cs`.
  Default: none (`DefaultAzureCredential`). Not required.
- `Intelligence__Inference__Providers__OpenAiCompatible__Endpoint` — Read by
  `apps/intelligence/src/Inference/Providers/OpenAiCompatibleProvider.cs`.
  Default: none. Required with that section; a model server inside the
  deployment. Loopback, private (RFC 1918, `fc00::/7`), single-label or `.svc`,
  `.cluster.local`, `.internal` and `.localhost` hosts only; anything else fails
  startup.
- `Intelligence__Inference__Providers__OpenAiCompatible__ApiKey` — Read by
  `apps/intelligence/src/Inference/Providers/OpenAiCompatibleProvider.cs`.
  Default: none. Not required; only if the server asks for one.
- `Intelligence__Inference__Providers__OpenAiCompatible__Models__<model>` — Read
  by `apps/intelligence/src/Inference/Providers/OpenAiCompatibleProvider.cs`.
  Default: the model id unchanged. Not required; maps a task configuration's
  model id (for example `claude-opus-5-5`) to the local server's model name.
- `Intelligence__Inference__Suppliers__<id>__Status` — Read by
  `apps/intelligence/src/Inference/Suppliers/SupplierRecord.cs`. Default: the
  shipped record's `Draft`. Not required; `Approved` (needs `AssessedBy`,
  `AssessedOn` and `NextReviewOn`, or startup fails) or `Suspended`. Outside
  Development an external provider is used only while its supplier is
  `Approved`.
- `Intelligence__Inference__Suppliers__<id>__AssessedBy`, `__AssessedOn`,
  `__NextReviewOn` — Read by
  `apps/intelligence/src/Inference/Suppliers/SupplierRecord.cs`. Default: none.
  Required with `Approved`.
- `Intelligence__Sandbox__AllowedClients__0` (`__1`, …) — Read by
  `apps/intelligence/src/Sandbox/SandboxEgressHandler.cs`. Default: none. Not
  required; named `IHttpClientFactory` clients an agent run may call besides the
  model providers. Every other client is refused inside a run.

## Notifications service

- `Notifications__PortalUrl` — Read by `apps/notifications/src/Program.cs:21`
  (`NotificationBrandingOptions`). Default: none. Not required; the participant
  portal's sign-in URL used by the "Log In" link. It must be an absolute `https`
  URL (`http` only on localhost), or the service refuses to start. Without it,
  notifications carry no portal link.
- `Notifications__BrandName` — Read by `apps/notifications/src/Program.cs:21`
  (`NotificationBrandingOptions`). Default: `Cadence Clinical Platform`. Not
  required; the platform name in notification footers. It must not be empty.

## Field-level protection

Keys for `packages/security/src/FieldProtection` (#4770). Both are base64
encodings of exactly 32 random bytes (`openssl rand -base64 32`), are validated
when the host starts, and have no default in any environment. Use two different
keys. See
[ADR-2494](adr/2026-09-30-field-level-encryption-and-keyed-lookup-hashes-for-secrets-at-rest.md).
Under the Aspire AppHost, set them as the `field-encryption-key` and
`field-lookup-hash-key` parameters. Imaging's two de-identification keys are the
`imaging-deid-signing-key` and `imaging-deid-pseudonymization-key` parameters.

- `FIELD_ENCRYPTION_KEY` — Read by
  `packages/security/src/FieldProtection/FieldProtectionServiceCollectionExtensions.cs`
  (`AddCadenceFieldEncryption`), called by `apps/payments/src/Program.cs` and
  `apps/econsent/src/Program.cs`. Default: none. Required (payments, econsent);
  the service refuses to start without a valid key. Rotating it makes existing
  ciphertext unreadable.
- `FIELD_LOOKUP_HASH_KEY` — Read by `AddCadenceLookupHashing` in the same file,
  called by `apps/interop/src/Program.cs`. Default: none. Required (interop);
  the service refuses to start without a valid key. Rotating it orphans stored
  device-token hashes.
- `CADENCE_SCE_SIGNING_KEY` — Read by
  `apps/interop/src/Infrastructure/Security/SceTokenSigner.cs`
  (`AddSceSigning`), called by `apps/interop/src/Program.cs`. Default: none.
  Required (interop); base64 of 32 bytes. The service refuses to start without a
  valid key. Rotating it invalidates outstanding SCE database-lock tokens
  (ADR-2515).
- `SmartSigningKeys__<ref>` — Read by
  `apps/interop/src/Infrastructure/Security/SmartSigningKeys.cs`
  (`ConfigurationSmartSigningKeys`), used by
  `POST /api/v1/interop/esource/smart/assertion` and `/smart/verify`. Default:
  none. Required only for an EHR endpoint registered with
  `SigningKeyRef: <ref>`; the PEM RSA private key (at least 2048 bits) that
  signs that endpoint's SMART client assertions, fed from a secret store.
  `<ref>` is letters, digits and underscores. A missing or invalid key answers
  `422` and signs nothing; requests never carry a key (#5436).
- `CADENCE_IMAGING_DEID_SIGNING_KEY` — Read by
  `apps/imaging/src/Infrastructure/Security/DeidentificationKeys.cs`
  (`AddImagingDeidentification`), called by `apps/imaging/src/Program.cs`.
  Default: none. Required (imaging); base64 of 32 bytes. Signs de-identification
  certificates and batch manifests. The service refuses to start without a valid
  key (#5399). Rotating it makes stored certificates report an invalid
  signature.
- `CADENCE_IMAGING_DEID_PSEUDONYMIZATION_KEY` — Read by
  `apps/imaging/src/Infrastructure/Security/DeidentificationKeys.cs`. Default:
  none. Required (imaging); base64 of 32 bytes, different from the signing key.
  Keys the anonymized DICOM UIDs and original-UID hashes (#5399). Rotating it
  gives the same source study new anonymized UIDs.

## Object storage

Read by `AddCadenceStorage` in
`packages/storage/src/StorageServiceCollectionExtensions.cs`, called by
`apps/etmf/src/Program.cs`. A service that calls it fails at startup when a
required key is missing.

- `Storage__Provider` — Read by `AddCadenceStorage`. Default: none. Required:
  `s3`, `minio`, `filesystem`, or `memory` (in-process, Development only).
- `Storage__BucketName` — Read by `AddCadenceStorage` (`S3StorageOptions`).
  Default: none. Required for `s3` and `minio`.
- `Storage__Region` — Read by `AddCadenceStorage` (`S3StorageOptions`). Default:
  `us-east-1`. Not required.
- `Storage__ServiceUrl` — Read by `AddCadenceStorage` (`S3StorageOptions`).
  Default: none (Amazon S3). Required for `minio`; an S3-compatible endpoint,
  which turns on path-style addressing.
- `Storage__ForcePathStyle` — Read by `AddCadenceStorage` (`S3StorageOptions`).
  Default: on when `Storage__ServiceUrl` is set. Not required.
- `Storage__ServerSideEncryption` — Read by `AddCadenceStorage`
  (`S3StorageOptions`). Default: `AES256`. Not required; `aws:kms` for KMS,
  empty to turn it off.
- `Storage__RootPath` — Read by `AddCadenceStorage`
  (`FileSystemStorageOptions`). Default: none. Required for `filesystem`; an
  absolute directory. The store keeps `objects/` and `meta/` under it.
- `Storage__AccessKeyId`, `Storage__SecretAccessKey` — Read by
  `AddCadenceStorage`. Default: none (the AWS default credential chain:
  environment, profile, instance or task role). Not required; set both or
  neither. There is no default key.

## Gateway routing

Read by `apps/gateway/src/Program.cs`, one per service in the topology manifest
(`packages/topology`). In Development each defaults to
`http://localhost:<port>`; in any other environment each is required and the
gateway refuses to start without it. A value that is not an absolute http or
https URL fails startup in every environment.

| Key                         | Default port |
| --------------------------- | ------------ |
| `DESIGNER_SERVICE_URL`      | 8001         |
| `EXECUTION_SERVICE_URL`     | 8002         |
| `ETMF_SERVICE_URL`          | 8003         |
| `INTEROP_SERVICE_URL`       | 8004         |
| `QUALITY_SERVICE_URL`       | 8005         |
| `NOTIFICATIONS_SERVICE_URL` | 8006         |
| `CTMS_SERVICE_URL`          | 8007         |
| `SAFETY_SERVICE_URL`        | 8008         |
| `TICKETS_SERVICE_URL`       | 8009         |
| `EISF_SERVICE_URL`          | 8010         |
| `ECONSENT_SERVICE_URL`      | 8011         |
| `ORG_SERVICE_URL`           | 8012         |
| `FILESHARE_SERVICE_URL`     | 8013         |
| `IMAGING_SERVICE_URL`       | 8014         |
| `KNOWLEDGE_SERVICE_URL`     | 8015         |
| `PAYMENTS_SERVICE_URL`      | 8016         |
| `KERNEL_SERVICE_URL`        | 8018         |

Timeouts and health checks for every route, read by
`apps/gateway/src/GatewayClusters.cs` (ADR-2451). Each must be a positive number
of seconds; the gateway refuses to start otherwise.

- `GATEWAY_PROXY_TIMEOUT_SECONDS` (default `30`) — A proxied request fails with
  504 once its service sends nothing for this long.
- `GATEWAY_HEALTH_CHECK_INTERVAL_SECONDS` (default `10`) — How often the gateway
  probes each service's `/healthz/ready`. Two failures in a row make the gateway
  answer 503 for that service until a probe succeeds.
- `GATEWAY_HEALTH_CHECK_TIMEOUT_SECONDS` (default `5`) — How long one readiness
  probe may take before it counts as failed.

Per-caller rate limit, read by `apps/gateway/src/GatewayRateLimiting.cs`
(ADR-2491). It must be a positive number; the gateway refuses to start
otherwise.

- `GATEWAY_RATE_LIMIT_PER_MINUTE` (default `600`) — Requests each caller may
  send per one-minute window, counted by token subject, or by client address for
  unauthenticated requests. Over the limit the gateway answers 429 with
  `Retry-After`. Health probes are not limited.

## Authentication and audit

- `GATEWAY_SECRET` — Read by
  `packages/security/src/DependencyInjection/CadenceSecurityServiceCollectionExtensions.cs`
  (every service), `apps/gateway/src/Program.cs:68`, and
  `packages/security/src/Authentication/SigTokenVerifier.cs:130`. Default: none.
  Required: 32+ characters; services refuse to start without it.
- `OIDC_AUTHORITY` — Read by `apps/gateway/src/Program.cs:30`. Default: none.
  Required (gateway).
- `OIDC_METADATA_ADDRESS` — Read by `apps/gateway/src/Program.cs:40`. Default:
  none. Not required.
- `OIDC_AUDIENCE` — Read by `apps/gateway/src/Program.cs:33`. Default: none
  (validated when set). Required outside Development (gateway): `cadence-api`,
  the audience the realm gives `cadence-web` tokens, so tokens from other realm
  clients are refused (#5037).
- `OIDC_REQUIRE_HTTPS_METADATA` — Read by `apps/gateway/src/Program.cs:51`.
  Default: `true`. Not required.
- `OIDC_SIGNATURE_CLIENT_ID` — Read by `apps/gateway/src/Program.cs:81`.
  Default: `cadence-esignature`. Not required; Keycloak client with only direct
  access grants, used for e-signature re-authentication.
- `OIDC_SIGNATURE_CLIENT_SECRET` — Read by `apps/gateway/src/Program.cs:84`.
  Default: none. Required only if that client is confidential.
- `OIDC_TOKEN_ENDPOINT` — Read by `apps/gateway/src/Program.cs:72`. Default:
  `<realm>/protocol/openid-connect/token` beside `OIDC_METADATA_ADDRESS` or
  `OIDC_AUTHORITY`. Not required.
- `AUDIT_LOG_SECRET_KEY` — Read by
  `packages/security/src/Audit/AuditSecretKey.cs` (the only resolver; used by
  `AuditDigestEngine`, both `AuditDigestCalculator`s and `AddCadenceSecurity`).
  Default: none. Required; signing or verifying the audit chain throws without
  it.
- `GXP_SIGNER_ID` — Read by `packages/compliance/src/GxPSigner.cs:67` (release
  signing with `SignGxpMarkdown` only; `gxp snapshot` stamps, it does not sign).
  Default: `cadence-validation-runner`. Not required.

## Observability

- `OTEL_EXPORTER_OTLP_ENDPOINT` — Read by
  `packages/telemetry/src/CadenceTelemetryExtensions.cs` (enables OTLP export of
  traces, metrics and logs); the OpenTelemetry SDK also honours the other
  standard `OTEL_EXPORTER_OTLP_*` keys. Default: unset (nothing exported). Not
  required.
- `OTEL_SERVICE_NAME` — Read by
  `packages/telemetry/src/CadenceTelemetryExtensions.cs`. Default: the service's
  assembly name. Not required.

## Tooling

- `BRAND_DOMAIN` — Read by
  `packages/tooling_core/src/OpenApiToolingService.cs:171`. Default:
  `ccrsoft.com`.
- `DOTNET_INSTALL_SH_SHA256` — Read by `scripts/setup_dotnet_env.sh:50`.
  Default: `082f7685e156738a1b2e2ed8381a621870d4ce8e8c59278034556f05c186eb2e`.
- `DOTNET_INSTALL_PS1_SHA256` — Read by `scripts/setup_dotnet_env.ps1:63`.
  Default: `e8b873e18a81e5c4cd8ab69d84dac8fead291d50b3c44633cd7fddad709a13d6`.
- `GH_TOKEN`, `GITHUB_TOKEN` — Read by
  `packages/tooling_core/src/Services/SyncGithubProjectService.cs:10`. Default:
  none.
- `RTM_DRAFT`, `GENERATE_RTM_DRAFT` — Read by
  `packages/compliance/src/RtmGeneratorService.cs:124-125` (honoured by
  `generate-rtm` and `check rtm`; `gxp sync` and `gxp snapshot` ignore them).
  Default: off.
- `CADENCE_UPDATE_BASELINE` — Read by
  `packages/tooling_core/src/Services/DeidComplianceService.cs` (`1` with
  `check deid` and no files rewrites `.deid-baseline.txt`),
  `tests/Cadence.ArchitectureTests/Architecture/KernelBoundaryBaseline.cs`
  (rewrites the kernel boundary baseline), and
  `tests/Cadence.ArchitectureTests/Architecture/KernelRuleCoverage.cs` (deletes
  covered rules from the kernel rule coverage baseline, never adds one).
  Default: off.
- `CADENCE_BASELINE_GROWTH` — Read by
  `packages/cli/src/Services/BaselineRatchetService.cs` (a reason; in the
  pre-commit hook only, admits staged growth of a ratchet baseline for the
  `baseline-ratchet` gate. The commit must also carry a
  `Baseline-Growth: <reason>` trailer, which `cadence check` and CI require; see
  [TESTING.md](TESTING.md)). Default: unset.
- `CI`, `GITHUB_ACTIONS` — Read by
  `packages/cli/src/Services/BaselineRatchetService.cs:248-249` (`true` or `1`
  makes the `baseline-ratchet` gate fail instead of skipping when `origin/main`
  is missing or shares no history with `HEAD`). Default: unset.
- `CADENCE_GXP_SNAPSHOT` — Read by
  `packages/compliance/src/GxpSnapshotPolicy.cs` (`1` or `true` turns on
  snapshot mode, ADR-2532: the `gxp-snapshot` gate admits changes to the RTM,
  the IQ/OQ/PQ report and `docs/SDLC/runs/`, and outside the pre-commit hook
  compares the regenerated documents with them; `check rtm`, `gxp validate` and
  the agent Stop hook also require the RTM's test mapping to be current; the
  `cadence ci` compliance job writes `docs/SDLC` instead of
  `TestResults/gxp/preview`. Set by the integration owner only;
  `gxp snapshot --commit` sets it for its commit). Default: off.
- `RTM_STRICT_ALL` — Read by
  `packages/compliance/src/RtmGeneratorService.cs:254`. Default: off.
- `CADENCE_TEST_POSTGRES` — Read by
  `tests/Cadence.IntegrationTests/Execution/ExecutionPostgresFixture.cs` (Npgsql
  connection string to an existing PostgreSQL server; without it or
  `CADENCE_TEST_DOCKER`, PostgreSQL tests are skipped). `cadence ci`
  (`packages/cli/src/Services/LocalCiRunner.cs`) opens the connection string
  once before the .NET tests and fails when neither is usable, unless
  `--allow-skip` is given. Default: none.
- `CADENCE_TEST_DOCKER` — Read by
  `tests/Cadence.IntegrationTests/Execution/ExecutionPostgresFixture.cs` and
  `tests/Cadence.TestSupport/Database/PostgresTestServer.cs` (the kernel
  fixtures) (`1` starts a Testcontainers container of `PostgresTestImage.Image`,
  PostgreSQL 18). Default: none.
- `CADENCE_TEST_EVIDENCE_DIR` — Read by
  `tests/Cadence.TestSupport/Database/PostgresTestEvidence.cs` (directory where
  database fixtures record the server version, extensions, latest migration and
  application-role attributes; `gxp sync`, `gxp snapshot` and `cadence ci` set
  it to `TestResults/gxp`). Default: none.
- `CADENCE_TEST_BROWSER` — Read by
  `tests/Cadence.IntegrationTests/Kernel/KernelObservationHostPostgresTests.cs`
  (`1` runs the browser-to-database check of the observation correction in
  Chromium; also needs `CADENCE_TEST_POSTGRES`, Node and pnpm). Default: none.
- `CADENCE_LINEAGE_BENCHMARK` — Read by
  `tests/Cadence.IntegrationTests/Kernel/LineageBenchmarkPostgresTests.cs` (`1`
  runs the 10M-edge lineage benchmark; also needs `CADENCE_TEST_POSTGRES` and
  about 4 GB of disk. `packages/compliance/src/DatabaseTestEvidence.cs` exempts
  its skip from the skipped-database-test rule). Default: off.
- `PLAYWRIGHT_CHROMIUM_PATH` — Read by
  `apps/web/playwright.correction-check.config.ts` (path to a preinstalled
  Chromium for the browser-to-database check; set to `/opt/pw-browsers/chromium`
  by the test when that file exists). Default: Playwright's bundled Chromium.
- `CADENCE_UPDATE_GOLDEN` — Read by
  `tests/Cadence.UnitTests/Packages/UsdmDesignImporterTests.cs` (`1` rewrites
  the expected design of the USDM import golden test; review the diff before
  committing). Default: off.

## Web apps

Vite inlines `VITE_*` values at build time. Put overrides in
`apps/<app>/.env.local`.

- `VITE_API_BASE_URL` — Read by `apps/web/src/api/authorization.js:18` (the only
  origin besides same-origin `/api/` that receives the bearer token),
  `apps/web/src/api/client.ts:7`, and `apps/subject-portal/src/api/client.js:7`.
  Default: `http://localhost:3000` in the web app's and `http://localhost:5174`
  in the subject portal's `vite` dev server (their own origins, so calls go
  through their `/api` proxies; the gateway sends no CORS headers, #5429,
  #5451); `http://localhost:8000` in builds and tests.
- `CADENCE_GATEWAY_URL` — Read by `apps/web/vite.config.js` and
  `apps/subject-portal/vite.config.js` (dev server `/api` proxy target; set by
  the AppHost, and by `docker compose` for the portal). Default:
  `http://localhost:8000`.
- `VITE_BASE_PATH` — Read by `apps/web/vite.config.js:16` and
  `apps/subject-portal/vite.config.js:16`. Default: `/cadence-clinical/` in
  `apps/web` (`/cadence-clinical/demo/` when staged into the Pages portal);
  `/subject-portal/` in `apps/subject-portal`
  (`/cadence-clinical/subject-portal/` when staged into the Pages portal).
- `VITE_APP_TITLE` — Read by `apps/web/vite.config.js:37` and
  `apps/subject-portal/vite.config.js:39`. Default: app name.
- `VITE_BRAND_NAME` — Read by `apps/web/src/App.vue:22`,
  `apps/web/src/components/AppShell.vue:841`, and
  `apps/subject-portal/src/components/PortalHeader.vue:58`. Default:
  `Cadence Clinical`.
- `VITE_KEYCLOAK_URL` — Read by `apps/web/src/auth/keycloak.js` and
  `apps/subject-portal/src/features/auth.js:79`. Default:
  `http://localhost:8080/` in dev and test builds; none in an `apps/web`
  production build, which shows the missing key on the sign-in page.
- `VITE_KEYCLOAK_REALM` — Read by `apps/web/src/auth/keycloak.js` and
  `apps/subject-portal/src/features/auth.js:80`. Default: `cadence` in dev and
  test builds; none in an `apps/web` production build, which shows the missing
  key on the sign-in page.
- `VITE_KEYCLOAK_CLIENT_ID` — Read by `apps/web/src/auth/keycloak.js` and
  `apps/subject-portal/src/features/auth.js:81`. Default: `cadence-web` in dev
  and test builds; none in an `apps/web` production build, which shows the
  missing key on the sign-in page.
- `VITE_DEMO_MODE` — Read by `apps/web/src/utils/demoMode.js` and
  `apps/subject-portal/src/utils/demoMode.js`. Default: off. `true` (or
  `vite --mode demo`, `pnpm build:demo`) makes a demo build: offline demo
  identity, hash routing, MSW mock API and sample data. Any other build shows
  API failures as errors and never substitutes sample data.
- `VITE_ENABLE_MSW` — Read by `apps/web/src/main.js` and
  `apps/subject-portal/src/bootstrap/initialize.js:40`. Default: off. Web:
  starts the MSW mock worker without the rest of demo mode (demo builds start it
  anyway).
