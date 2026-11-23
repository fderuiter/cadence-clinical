# Kernel API host

The Clinical Kernel's HTTP host
([ADR-2540](../../docs/adr/2026-09-30-kernel-api-host-and-read-endpoints-for-the-canvas.md)).
It serves the kernel's read endpoints for the Clinical Canvas: navigation,
dry-run authorization with explanations, the command catalogue, search, protocol
versions, lineage and the activity stream. It is the only process that registers
kernel persistence (K-XS-2); product services never reference it. Since ADR-2620
it also hosts the command pipeline for observation writes.

**Status:** the endpoints are mapped, documented and tested, and each read runs
in the kernel tenant found from the tenant code the gateway signs. Authorization
decisions come from the caller's grants, roles and delegations, and so does what
the caller may see (ADR-2457): an object is visible when a grant in effect
covers it, and masked in place otherwise, so a caller with no grant sees every
object masked. Blinding and the IdentityVault (#4782) are not decided yet.

## Project

- Project: `apps/kernel-api/src/Cadence.Kernel.Host.csproj` (assembly
  `Cadence.Kernel.Host`).
- `Program.cs`: composition root. Registers kernel persistence, the scope graph
  and navigation, search, authorization, synchronous protocol design import
  (`ProtocolDesignImporterService`) and the activity stream, and maps
  `MapKernelApi().MapKernelReads().MapKernelStudyImports()` from
  `Cadence.Kernel.Api`.
- `ClaimsCurrentActor`: the acting person, from the verified `sub` claim. A
  subject the kernel does not accept as an accountable person (`system`,
  `admin`, a `svc:` or `agent:` id) gives no actor, so dry runs deny with
  `actor.resolved`.
- Authorization: the kernel's `AuthorityEvaluator`
  (`AddKernelAuthorityEvaluator`, ADR-2455), with no platform operators, except
  the development tenant bootstrap's service account when
  `Kernel:DevelopmentTenant:Code` is set in Development (ADR-2542). Without a
  grant in effect covering the resource, a decision is a denial whose unmet
  condition is `grant.active`.
- `DevelopmentTenantBootstrap` (ADR-2542): in Development, with
  `Kernel:DevelopmentTenant:Code` set, provisions that tenant on start through
  the command pipeline as `svc:kernel-development-bootstrap`, the host's only
  platform operator, unless an active tenant already has the code. docker
  compose and the AppHost set `cadence-local`, the code the local realm's users
  sign in with (ADR-2514). The host refuses to start with the setting in any
  other environment.
- `DevelopmentDemoData` (ADR-2543): with `Kernel:DevelopmentTenant:DemoData`
  also true, the bootstrap then writes the demo study `CADENCE-101` into that
  tenant through the pipeline (a sponsor, `SITE-101` and `SITE-102` with their
  institutions, an approved and effective protocol v1 with scheduled and
  unscheduled `CollectionSpec`s and activated study sites, and three screened
  participants) and grants each user of `docker/cadence-realm.json` a role by
  their fixed subject id: `crc.site101`, `pi.site101` and `cra.monitor` at the
  `SITE-101` study site, everyone else on the whole study. It first grants
  itself a tenant-wide `DEMO_SEEDER` role for those writes. Ids, timestamps and
  idempotency keys are fixed, so a restart replays every command and writes
  nothing new. docker compose and the AppHost turn it on.
- `DirectoryTenantResolver`: finds the caller's kernel tenant from the signed
  `tenant_id` claim through `ITenantDirectory.FindActiveByCodeAsync`. A missing
  code, or one that names no active kernel tenant, is a 403; there is no
  fallback tenant and no configured mapping.
- `KernelDatabaseHealthCheck`: the readiness probe opens a connection to the
  kernel database.
- `KernelMigrationHostedService`: applies the kernel migrations on start when
  `Database:MigrateOnStartup` or `Database:MigrateOnly` is set, then each
  module's (`AddModuleMigrations`, in its own schema).
- The Intelligence module ([`apps/intelligence`](../intelligence/README.md),
  ADR-2630): `AddCadenceIntelligenceModule` and `MapIntelligenceModule()` add
  the AI agent runtime, its ledger and controls, and the command-palette intent
  router under `/api/v1/ai`.
- The Execution module
  ([`apps/execution/module`](../execution/module/README.md), ADR-2622):
  `AddCadenceExecutionModule` and `MapExecutionModule()` add eCRF forms over
  kernel form instances under `/api/v1/execution/forms`. `KernelReadAccess`
  gives module reads the kernel's masking (`IKernelReadAccess`).
- The activity projector (`AddKernelActivityProjection()`, ADR-2485): copies
  committed kernel events into the activity stream every five seconds. This is
  the one host with kernel persistence, so it runs the projector (ADR-2540
  §4.7).

The endpoints, response records and tenant filter live in
[`packages/kernel/src/Cadence.Kernel.Api`](../../packages/kernel/README.md).

## HTTP surface

All reads are `GET`, need the gateway-signed caller identity, run in the
caller's kernel tenant, and answer RFC 9457 problem details on failure. The wire
is snake_case. Objects the caller may not see are masked (ADR-2471): node reads,
paths and child lists keep them in place with only `kind` and `mask`; search
leaves them out; lineage reads keep them as a `masked:n` handle with their kind
and count them like any other.

Routes under `/api/v1/kernel`:

- `GET nodes/{nodeRef}`: summary of one object (`ref`, `kind`, `title`, `code`,
  `status`).
- `GET nodes/{nodeRef}/path`: zoom path from the tenant down to the object
  (ADR-2481).
- `GET nodes/{nodeRef}/children`: zoom children with their own child counts. A
  hidden node is refused with 403, since even masked its list would say how many
  children it has (#5033); a hidden child is masked in place.
- `GET nodes/{nodeRef}/commands`: kernel commands offered on the object's kind,
  each with the caller's dry-run decision and explanation (ADR-2483).
- `GET authorization/can-i?capability=&resource=`: the caller's dry-run decision
  on one capability and object, with its explanation (ADR-2482).
- `GET search?q=&kind=&limit=`: search hits by code or title (ADR-2484); `kind`
  repeats, `limit` is 1 to 50.
- `GET studies`: visible studies in the caller's tenant (`IStudyDirectory`),
  filtered by `INodeVisibility`.
- `GET organizations?kind=`: visible organizations in the caller's tenant,
  optionally filtered by `OrganizationKind`.
- `GET protocol-templates`: bundled standard CDISC Library eCRF templates
  available for protocol draft import.
- `GET studies/{studyRef}/protocol-versions`: the study's protocol versions,
  newest first, without content. A hidden study is refused with 403; a hidden
  version is masked in place.
- `GET protocol-versions/{versionRef}`: one protocol version, without content.
- `GET protocol-versions/{versionRef}/design-summary`: viewable summary of a
  protocol version's forms (`CollectionSpecs`), ordered items, mandatory flags,
  and `import_notes`.
- `GET protocol-versions/{versionRef}/schedule-of-activities`: the version's
  Schedule of Activities compiled from its design, with the matrix hash
  (ADR-2475). A hidden version is refused with 403; content that is not a typed
  design is a 400.
- `GET nodes/{nodeRef}/lineage?direction=&depth=&edge_kind=&valid_at=&known_at=`:
  trace lineage walk, `Backward` (default) or `Forward`, depth 1 to 50 (default
  10), `edge_kind` repeats (ADR-2471).
- `GET nodes/{nodeRef}/dependents?depth=&edge_kind=&valid_at=&known_at=`: impact
  summary of what depends on the object, grouped by kind with counts.
- `GET protocol-versions/{versionRef}/impact?depth=&valid_at=&known_at=`:
  amendment impact (K-PROT-2): dependents of the version it amends, the elements
  it changes, and its Schedule of Activities directives (`schedule`, ADR-2475).
  A hidden amendment is refused with 403; a first version is a 400.
- `GET activity?scope=&family=&actor=&from=&before=&limit=`: the activity stream
  (ADR-2485) for what happened in the scope (the tenant when absent), newest
  first; `family` repeats, `limit` is 1 to 200 (default 50), and `before` takes
  the previous page's `next` cursor. A hidden scope is refused with 403; an
  entry about a hidden object is masked in place.
- `GET entitlements`: active commercial subscription tier (`Individual`,
  `Select`, `Complete`), status (`Trialing`, `Active`, `Expired`), trial end
  timestamp, read-only state, scoping metadata and enabled product families for
  the caller's kernel tenant
  ([ADR-2547](../../docs/adr/2026-11-20-keycloak-self-registration-and-clinical-kernel-trial-bootstrap.md),
  [ADR-2604](../../docs/adr/2026-11-21-signed-gateway-expiry-guard-and-scoped-trial-upgrades.md)).
- `POST entitlements/tier`: switches the commercial tier (`Individual`,
  `Select`, `Complete`) of an active `Trialing` subscription in place while
  preserving `TrialEndsAt` (`SwitchTrialTier`); refused with
  `403 K.Subscription.Expired` once expired.
- `POST entitlements/activate`: transitions a `Trialing` or `Expired` workspace
  to `Active` after validating structured study scoping (`study_phase`,
  `study_duration_months`, `countries`, `site_count`, `participant_count`,
  `active_study_count`, `billing_confirmation`), returning
  `422 K.Commercial.ScopingInvalid` with field errors when incomplete
  (`ActivateSubscription`, marked `AllowExpiredTenantWrite()`).

### Self-service trial onboarding (ADR-2547)

`POST /api/v1/kernel/onboarding/trial` runs outside `KernelTenantFilter`
(protected by `SignedInCallerPolicy`) so newly self-registered Keycloak users
without a `tenant_id` claim can bootstrap an isolated trial workspace
(`organization_name`, `tier`, `residency`). `TrialOnboardingService` executes
idempotently per OIDC `sub` under platform operator
`svc:kernel-trial-onboarding`
(`KernelAuthorityOptions.TrialOnboardingOperator`), dispatching kernel commands
to provision the `Tenant`, sponsor `Organization`, pseudonymous `Person`,
`TenantMembership`, initial `sponsor_admin` and `sponsor_designer` `Role` and
`Grant` records, `Subscription` (`Trialing`), and `ProductEntitlement` rows
(`Blueprint`, `Capture`, plus tier-included families), then writes `tenant_id`
and realm roles back to Keycloak via `KeycloakOnboardingClient`.

### Study creation & synchronous protocol draft import

- `POST studies`: registers a study (`study.register`) and, when needed, a
  sponsor organization (`organization.register`) with `Idempotency-Key` and
  signed `X-Change-Reason`.
- `POST studies/{studyRef}/protocol-imports`: synchronously imports an ODM
  v1.3.2 XML file, a USDM v4 JSON file, or bundled CDISC Library eCRF templates
  into a `Draft` protocol version (`CreateProtocolDraft`, `ReviseProtocolDraft`,
  or `ForkProtocolVersion` + `ReviseProtocolDraft`). Enforces a 10 MB limit and
  blocks DTDs.

### Observation writes (ADR-2620)

The host also runs the kernel command pipeline (`AddKernelCommandPipeline`), so
a write commits its state, audit chain link, event and outbox row in one
PostgreSQL transaction. After commit, its background jobs relay the outbox to
the in-process event consumers and seal the audit chains, each under a
PostgreSQL leader lock so that one replica runs a job at a time (ADR-2601). Each
write needs an `Idempotency-Key` header and takes its reason for change only
from the gateway-signed identity (`X-Change-Reason`, ADR-2495;
`SignedChangeReason`). Each is authorized against the observation's enrollment.

- `POST observations`: records an observation (`observation.record`):
  enrollment, concept, typed value (ADR-2623, for example
  `{"type":"quantity","value":182,"unit":"mm[Hg]"}`), observed-at. 201; 422
  `K.Observation.ValueInvalid` for a value that fails its kind's rules; 409
  `K.Observation.ParticipantWithdrawn` once the participant is withdrawn
  (audited).
- `POST observations/{ref}/corrections`: corrects the value
  (`observation.correct`) with the expected version. 409
  `K.Observation.VersionStale`, 423 `K.Observation.Locked`, 422
  `K.Command.ReasonForChangeRequired` without a reason (the pipeline's check, as
  for every kernel mutation).
- `POST observations/{ref}/verifications`: verifies an observation's current
  revision against source (`observation.verify`, `VerifyObservation`). 201; 409
  `K.Assertion.RevisionStale`, 423 `K.Assertion.Locked` (Frozen still allows
  SDV), 422 without a reason.
- `POST assertions/{ref}/retraction`: withdraws an active assertion
  (`assertion.retract`, `RetractAssertion`) by recording a retracting assertion.
  200; 409 `K.Assertion.AlreadyRetracted`, 423 `K.Assertion.Locked`, 422 without
  a reason.
- `GET observations/{ref}`: returns the value, version, locks covering it
  (ADR-2621), every revision, assertions about it, and kernel audit entries. 403
  when the caller may not see its enrollment.

### Signed locks and streaming clinical exports (spec §10.1, §10.5)

Applying or releasing a clinical lock requires an `Idempotency-Key`, a signed
`X-Change-Reason`, and a fresh single-use 60-second `X-Sig-Token` (21 CFR Part
11). Each lock write records an `Attest` over the canonical `LockStatement`
before dispatching `ApplyLock` or `ReleaseLock`. Study exports and participant
casebooks project canonical bitemporal kernel state at
`as_of(valid_at, known_at)` (`K-TRUTH-1`), exclude `IdentityVault` direct
identifiers, and emit explicit `masked` placeholders for unauthorized sites or
blinded treatment values (`K-AUTH-4`).

- `POST locks`: applies a `Frozen` or `Locked` assertion (`lock.apply`) across
  `study`, `study_site`, `enrollment`, `collection_instance` or `observation`
  after verifying `X-Sig-Token` and recording `Attest`.
- `POST locks/{lockRef}/release`: releases an active lock (`lock.release`) with
  a step-up signed retracting assertion and `Attest`.
- `GET studies/{studyRef}/locks?as_of=&active_only=`: lists locks recorded for a
  study at an optional bitemporal `as_of` coordinate, masking unauthorized or
  blinded scopes in place.
- `GET studies/{studyRef}/exports?format=&lock_filter=&as_of=`: streams a
  bitemporal study snapshot as `csv`, `odm_xml` (ODM v1.3.2 `MetaDataVersion` +
  `ClinicalData`), or `dataset_json` (Dataset-JSON v1.1.0), filtered by lock
  state and masked per caller visibility.
- `GET enrollments/{enrollmentRef}/casebook?format=&as_of=`: streams a
  participant casebook (`json`, `odm_xml`, or printable `html`) with revisions,
  queries, verifications, `Attestation` signatures, and `X-Content-Sha256`
  digests.

### Execution forms (ADR-2622)

`POST /api/v1/execution/forms`, `GET /api/v1/execution/forms/{formId}` and
`PUT /api/v1/execution/forms/{formId}/values` open, read and save eCRF forms;
the [Execution module README](../execution/module/README.md) describes them.

### Proposals (ADR-2635)

Proposals an agent or a person made (ADR-2489), for the Canvas's Needs Attention
and review screen. A proposal, its plan and each affected object the caller may
not see stay in place with their mask (ADR-2471).

- `GET studies/{studyRef}/proposals?status=&limit=`: lists the study's
  proposals, newest first. `status` is `Open`, `Approved`, `Rejected`,
  `Withdrawn` or `Expired` (an open proposal past its expiry); `limit` is 1 to
  200 (default 50). A hidden study is refused with 403.
- `GET proposals/{ref}`: returns one proposal with intent, plan, affected
  objects, requested capabilities, proposer and kind, model, confidence, content
  hash, status, and decision. 403 when the caller may not see it.
- `POST proposals/{ref}/rejection`: rejects an open proposal (`proposal.reject`)
  with the signed reason for change and an `Idempotency-Key`. 409
  `K.Proposal.NotOpen` or `K.Proposal.Expired`; 422 without a reason. Approval
  waits on #5552.

Also `GET /healthz`, `/healthz/live`, `/healthz/ready` and
`GET /api/v1/kernel/status` (anonymous). OpenAPI and the Scalar reference are
mapped in Development only; the exported document is
[docs/openapi/kernel_openapi.json](../../docs/openapi/kernel_openapi.json).

The gateway forwards `/api/v1/kernel/**`, `/api/v1/ai/**` and
`/api/v1/execution/forms/**` here. Compose publishes host port 8018 (container
port 8080); the gateway's default upstream is `http://localhost:8018`
(`KERNEL_SERVICE_URL`).

## Security

`AddCadenceSecurity()` and `UseCadenceSecurity()` from
[packages/security](../../packages/security/README.md): every route not marked
anonymous requires the gateway-signed caller identity, and `GATEWAY_SECRET` is
required at startup
([ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md)).
The tenant and the actor come only from the verified claims. Authorization is
the kernel's own `KernelAuthorization` (K-AUTH-7); dry runs go to the
authorization access log, not the audit chain (ADR-2483).

## Persistence

`AddKernelPersistence` with the connection string from
`ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`; startup fails
without one (ADR-2260). Every read runs through a context bound to the caller's
tenant by Row-Level Security
([ADR-2250](../../docs/adr/2026-09-29-kernel-tenant-isolation-via-row-level-security.md)).
In production the host connects as the application role and the migrations and
role grant are applied separately with `KernelDatabase`; docker compose and the
AppHost connect as the schema owner and set `Database__MigrateOnStartup=true`.

## Key dependencies

- Project references: `Cadence.Kernel.Abstractions`,
  `Cadence.Kernel.Application`, `Cadence.Kernel.Persistence`,
  `Cadence.Kernel.Api`, `Cadence.Ddf`, `Cadence.Modules.Intelligence`,
  `Cadence.Modules.Execution`, `Cadence.Security`, `Cadence.Database`,
  `Cadence.Telemetry`.
- Packages: `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`,
  `Npgsql.EntityFrameworkCore.PostgreSQL` (versions in
  `Directory.Packages.props`).

## Tests

- `tests/Cadence.UnitTests/Kernel/Api/KernelApiHostTests.cs` boots the real host
  with fake navigation, search, tenant directory, protocol version reader and
  authority store: 403 for an unknown or missing tenant code, 401 unsigned,
  reads in the resolved tenant, masking in place and in search (and the host's
  grant-backed visibility: everything masked without a grant, visible under a
  tenant-wide one), protocol versions listed, masked and refused for a hidden
  study, 400 and 404 problems, CanI denied without a grant and permitted with
  one, each explained, no actor for a subject that is no accountable person, the
  command catalogue, search, and the activity read (filters, masking, cursor
  paging, a hidden scope refused).
- `tests/Cadence.UnitTests/Gateway/GatewayRouteCoverageTests.cs` checks the
  gateway route against the endpoints this host maps;
  `tests/Cadence.UnitTests/AppHost/AppHostModelTests.cs` checks its port.
- The kernel ports themselves are tested against PostgreSQL in
  `tests/Cadence.IntegrationTests/Kernel`; the Execution module's forms through
  this host in
  `tests/Cadence.IntegrationTests/Execution/ExecutionFormHostPostgresTests.cs`.
- `tests/Cadence.IntegrationTests/Kernel/KernelDevelopmentTenantPostgresTests.cs`
  starts the real host on PostgreSQL as the application role with a development
  tenant code: the tenant is provisioned once, with an audit entry naming the
  bootstrap's service account, a caller with that code is served, and other
  codes are still refused. With demo data on, the coordinator finds
  `CADENCE-101` and lists their own site's participants while the other study
  site is masked, a person with no grant finds nothing, the grantees are exactly
  the realm's subject ids, and a second start adds no audit entry.
  `KernelApiHostTests` checks that the setting stops the host outside
  Development.
- `tests/Cadence.E2ETests/Journeys/CrossModuleJourneyTests.cs` runs the
  cross-module clinical journeys through this host on PostgreSQL as
  gateway-signed callers (#5471), with the host's modules migrated as it
  registers them: enrollment, forms opened, saved and read over the Execution
  module's routes, a query raised, answered and closed, SDV and the database
  lock; an SAE captured on the adverse event form, published under the same
  participant and corrected; and a withdrawal after which earlier data stays
  readable. Consent, form signatures, the SDTM export and Safety's intake are
  dated TODOs naming their issues. Kernel commands this host has no route for
  yet reach it through a test-only signed relay (`KernelHostCommandRelay`).
- `tests/Cadence.IntegrationTests/Kernel/KernelApiHostPostgresTests.cs` boots
  the real host on PostgreSQL with nothing faked: a coordinator granted at one
  study site gets their enrollment, the design elements and the version from
  Trace, dependents and amendment impact, the other site's enrollment masked in
  place, and the changed elements with the schedule directives; a reader with no
  grant gets the same dependents counts with everything masked, and is refused
  the amendment's impact report, which describes the version's content
  (ADR-2475). The host's own projector fills the activity stream, where the
  coordinator sees their site's screening and the other site's masked, and a
  reader with no grant is refused the study's activity.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=<password>"
export Database__MigrateOnStartup=true
dotnet run --project apps/kernel-api/src/Cadence.Kernel.Host.csproj --urls http://localhost:8018
```

The Aspire AppHost ([apps/apphost](../apphost/README.md)) and docker compose
also run it on port 8018.
