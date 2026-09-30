# ADR-2540: Kernel API Host and Read Endpoints for the Clinical Canvas

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The kernel already answers the questions the Clinical Canvas verbs need, but only in process:

- navigation: a NodeRef's summary, its zoom path and its children (`IKernelNavigator`, ADR-2481);
- dry-run authorization and the command catalogue, each decision with its explanation (`IAuthorizationProbe`,
  ADR-2482, ADR-2483);
- search by code or title (`IKernelSearch`, ADR-2484).

No process serves them over HTTP. `Cadence.Kernel.Api` maps an empty `/api/v1/kernel` group, and no host calls
`AddKernelPersistence`, which only the kernel host may call (K-XS-2). So the web app cannot show a breadcrumb from the
kernel, grey a verb with the kernel's reason, or search, and the Canvas keeps those verbs disabled (#4833).

Two pieces the endpoints depend on are not in place yet:

- **The authority evaluator** (#4781). `KernelAuthorization` needs an `IAuthorizationEvaluator`; there is none.
- **Tenant resolution.** The gateway signs a tenant code (`tenant_id` claim), and the kernel keys everything by
  `TenantId` (a UUID). When this ADR was written there was no read port that found the kernel tenant for a code; the
  tenant directory (`ITenantDirectory`, #4780) now does.

## 2. Decision Drivers & Constraints

- **K-XS-2.** Kernel persistence is registered by one kernel host only; product services never reference it.
- **K-ID-2 and PRD-CRF-013.** A request reads only its own tenant's data, and the tenant comes from the verified caller
  identity, never from the route, the query or the body.
- **K-AUTH-1, K-AUTH-7.** Authorization is decided by the kernel's one decision procedure, not by an endpoint filter or
  by the web app.
- **Fail closed (ADR-2256, ADR-2260).** A missing evaluator, visibility decision or tenant mapping must refuse, not
  guess.
- **Masked, never dropped (ADR-2471).** Every kernel read that returns other objects masks the ones the reader may not
  see.
- **One HTTP entry point (PRD-SYS-191, ADR-2448).** The web app reaches the kernel through the gateway like every
  other service, and the route table is checked against the endpoints the host maps.
- **Requirements:** PRD-CRF-013 (tenant data isolation), PRD-SYS-191 (gateway routing).

## 3. Options Considered

### Option 1: Serve the kernel reads from the Execution service

- **Pros:** No new deployable.
- **Cons:** Execution is a product service; it would have to reference kernel persistence, which K-XS-2 forbids, and
  the kernel would take on Execution's release cycle.

### Option 2: Map a configured tenant code to a kernel tenant until the port exists

- **Pros:** The endpoints would serve data today.
- **Cons:** A hand-maintained mapping is a second source of tenant truth that outlives its purpose, and a wrong entry
  shows one sponsor another sponsor's objects. Rejected: the kernel slices thread ships the tenant directory port.

### Option 3: A kernel API host that serves the reads and fails closed on what is missing (selected)

A new host, `Cadence.Kernel.Host` in `apps/kernel-api`, registers kernel persistence, navigation, search and
authorization, and maps the read endpoints from `Cadence.Kernel.Api`. Until the evaluator exists every decision is a
denial that names the missing evaluator, and every object is masked.

## 4. Decision Outcome

### 4.1 The host

- `apps/kernel-api/src/Cadence.Kernel.Host.csproj`, assembly `Cadence.Kernel.Host`. As a `Cadence.Kernel.*` assembly it
  may reference kernel persistence (K-XS-2); no product service references it.
- It uses the same web defaults, gateway-signed caller authentication (`AddCadenceSecurity`), health probes and
  problem details as every other service. Its readiness probe opens a connection to the kernel database.
- The actor is the person named by the verified `sub` claim (`Actor.Person`); a request without one has no actor,
  and every dry run denies it with `actor.resolved` (K-TX-5).
- With `Database:MigrateOnStartup` or `Database:MigrateOnly` it applies the kernel migrations before serving, like the
  other services, for stacks that connect as the schema owner. A host that connects as the application role
  (ADR-2250) leaves both off.
- The gateway routes `/api/v1/kernel/**` to it (`KERNEL_SERVICE_URL`, default `http://localhost:8018`). The AppHost and
  docker compose run it on port 8018.

### 4.2 The endpoints

All are `GET`, all under `/api/v1/kernel`, all answer problem details on failure, and none sends a command.

| Endpoint | Answers |
| --- | --- |
| `nodes/{nodeRef}` | The object's summary: ref at its current version, kind, title, code, status. 404 outside the tenant. |
| `nodes/{nodeRef}/path` | The zoom path from the tenant down to the object, with grouping levels. Presentation only. |
| `nodes/{nodeRef}/children` | The object's zoom children with their own child counts and grouping level. |
| `nodes/{nodeRef}/commands` | Every kernel command offered on the object's kind, each with the caller's dry-run decision and explanation. |
| `authorization/can-i?capability=&resource=` | The caller's dry-run decision on one capability and object, with its explanation. |
| `search?q=&kind=&limit=` | Search hits (limit 1 to 50, default 20). |
| `studies/{studyRef}/protocol-versions` | The study's protocol versions, newest first, without content: number, parent, status, content hash, amendment intent, approving attestation, withdrawal reason. |
| `protocol-versions/{versionRef}` | One protocol version, the same way. |

- NodeRefs travel as their canonical strings (`kind:id@version`); a malformed one, or one of the wrong kind for the
  endpoint, is a 400.
- CanI takes the capability token only. Its verb class comes from the kernel command that declares it; a token no
  command declares is a 400. The caller cannot choose the verb class.
- The wire is snake_case, like Execution, and the host exports its OpenAPI document to
  `docs/openapi/kernel_openapi.json`, from which the web client is generated.

### 4.3 Tenant resolution

`Cadence.Kernel.Api` defines `IKernelTenantResolver`, which the host implements from the caller's claims, and
`KernelTenantFilter`, which runs it before every read endpoint. It answers:

- the kernel tenant, which the endpoint passes to the kernel port;
- 403 when the caller's tenant is not an active kernel tenant;
- 503 when the host cannot resolve tenants.

The host's resolver reads the signed `tenant_id` claim and asks the kernel's tenant directory
(`ITenantDirectory.FindActiveByCodeAsync`) for the active tenant with that exact code. A missing code, or one that names
no active tenant, is a 403; there is no fallback tenant and no configured mapping (option 2). Until the directory port
shipped, the resolver answered 503 for every read.

### 4.4 Authorization

The host registers the kernel's `KernelAuthorization` unchanged. Its `IAuthorizationEvaluator` is a fail-closed
placeholder until #4781: every decision is a denial whose one unmet condition is `authority.evaluator`, so the Canvas
shows every command disabled with a reason, never enabled by mistake. #4781 replaces the registration and nothing
else.

### 4.5 Masking

Every read runs under the tenant's Row-Level Security, and every object it returns goes through the host's
`INodeVisibility` for the signed-in person (ADR-2471):

- A masked object keeps its place and kind in a node read, a zoom path or a child list, with `mask` giving the reason
  (`NotPermitted`, `Blinded`, `IdentityVault`) and no ref, title, code, status or child count. A grouping level (a
  country) is masked with the object below it, and a masked child loses its group.
- Search leaves masked hits out: a hit matched the caller's text, so keeping it would confirm the hidden object exists.
- A protocol version list is refused with 403 when its study is masked, since listing the versions would describe the
  study; a masked version in a visible study keeps its place with only its mask. The protocol version reader does not
  authorize, so this masking is the read's authority check.
- A request with no accountable actor sees every object masked.

Until visibility is decided from the authority model (#4781) and the IdentityVault (#4782), the host's
`INodeVisibility` masks every object as `NotPermitted`. #4781 replaces that registration too.

### 4.6 Out of scope

- Commands over HTTP: the endpoints only read. Command endpoints come with the slices that need them, through the
  kernel command pipeline (K-AUTH-1).

## 5. Consequences & Trade-offs

- **Positive:** The web app gets the kernel's own answers over the same gateway, with a generated client. The two
  missing pieces each change one registration in the host.
- **Positive:** Nothing can be shown or enabled wrongly in the meantime: every object is masked and every decision is a
  denial with its reason.
- **Negative:** Until #4781 decides visibility, the reads return masked objects only, so the Canvas wiring can be
  exercised with real data only against the tests and a stubbed visibility.
- **Negative:** One more deployable, on port 8018.

## 6. Implementation & Verification

- `apps/kernel-api/src`: `Program.cs`, `ClaimsCurrentActor`, `DirectoryTenantResolver`,
  `UnavailableAuthorizationEvaluator`, `UnavailableNodeVisibility`, `KernelDatabaseHealthCheck`,
  `KernelMigrationHostedService`.
- `packages/kernel/src/Cadence.Kernel.Api`: `KernelReadEndpoints`, `KernelReadResponses`, `KernelReadMasking`,
  `KernelTenantResolution`.
- `tests/Cadence.UnitTests/Kernel/Api/KernelApiHostTests.cs` boots the real host: 403 for a tenant code the directory
  does not know or a missing one, 401 unsigned, reads in the resolved tenant, masking in place and in search, protocol
  versions listed, masked and refused for a hidden study, 400 and 404 problems, CanI and the command catalogue denied
  and explained, and search.
- `tests/Cadence.UnitTests/Gateway/GatewayRouteCoverageTests.cs` and
  `tests/Cadence.UnitTests/AppHost/AppHostModelTests.cs` cover the route and the port.
