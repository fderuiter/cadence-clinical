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

- **The authority evaluator** (#4781). `KernelAuthorization` needs an `IAuthorizationEvaluator`. When this ADR was
  written there was none; the kernel's `AuthorityEvaluator` (ADR-2455) now decides from grants, roles and
  delegations.
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
authorization, and maps the read endpoints from `Cadence.Kernel.Api`. What is not in place yet fails closed: before the
evaluator existed every decision was a denial that named it, and until visibility is decided every object is masked.

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
| `nodes/{nodeRef}/children` | The object's zoom children with their own child counts and grouping level. A node the caller may not see is refused with 403 (#5033). |
| `nodes/{nodeRef}/commands` | Every kernel command offered on the object's kind, each with the caller's dry-run decision and explanation. |
| `authorization/can-i?capability=&resource=` | The caller's dry-run decision on one capability and object, with its explanation. |
| `search?q=&kind=&limit=` | Search hits (limit 1 to 50, default 20). |
| `studies/{studyRef}/protocol-versions` | The study's protocol versions, newest first, without content: number, parent, status, content hash, amendment intent, approving attestation, withdrawal reason. |
| `protocol-versions/{versionRef}` | One protocol version, the same way. |
| `nodes/{nodeRef}/lineage?direction=&depth=&edge_kind=&valid_at=&known_at=` | Trace: the lineage walk from the object, backward (default) or forward, with every object reached and every edge followed (ADR-2471). |
| `nodes/{nodeRef}/dependents?depth=&edge_kind=&valid_at=&known_at=` | Impact: what depends on the object, grouped by kind with counts (Canvas D2). |
| `protocol-versions/{versionRef}/impact?depth=&valid_at=&known_at=` | Amendment impact (K-PROT-2): the dependents of the version the amendment changes, with the elements it declares it changes. A first version is a 400; it amends nothing. |

- NodeRefs travel as their canonical strings (`kind:id@version`); a malformed one, or one of the wrong kind for the
  endpoint, is a 400.
- Lineage reads take `depth` from 1 to 50 (default 10), `edge_kind` by name and repeatable (`DerivedFrom`, `GovernedBy`,
  …, any case), and `direction` as `Backward` or `Forward`; anything else is a 400. They read through `IKernelLineage`
  and `IAmendmentImpact` as the signed-in person, so a request with no accountable person is a 403.
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

The host registers the kernel's `KernelAuthorization` unchanged, over the kernel's `AuthorityEvaluator`
(`AddKernelAuthorityEvaluator`, ADR-2455): a decision is a permit when a grant or delegation the caller holds, in effect
now, carries the capability and covers the resource, and otherwise a denial whose unmet condition is `grant.active`.
The host only reads and never provisions a tenant, so it names no platform operators.

The actor is the person named by the verified subject. A subject the kernel does not accept as an accountable person
(a reserved id such as `system`, or a service or agent id) gives the request no actor, so every dry run denies it with
`actor.resolved`, the same as a request without a subject.

Until #4781 shipped the evaluator, the host registered a placeholder that denied every decision with the unmet
condition `authority.evaluator`.

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
- A lineage walk, a dependents read and an amendment impact keep a masked object in place as its kind, its mask and
  an opaque `masked:n` handle, and count it like any other (ADR-2471), so a count never depends on the reader. An
  edge touching a masked object withholds its activity.
- A request with no accountable actor sees every object masked, and lineage reads refuse it.

The host's `INodeVisibility` is the grant-backed visibility ([ADR-2457](2026-09-30-grant-backed-node-visibility.md)):
an object is visible when a grant the reader holds in effect covers it, and masked `NotPermitted` otherwise, so a reader
with no grant sees every object masked. Until then (#4781) the host masked every object. Blinding and the IdentityVault
(#4782) are not decided yet.

### 4.6 Out of scope

- Commands over HTTP: the endpoints only read. Command endpoints come with the slices that need them, through the
  kernel command pipeline (K-AUTH-1).

## 5. Consequences & Trade-offs

- **Positive:** The web app gets the kernel's own answers over the same gateway, with a generated client. The two
  missing pieces each change one registration in the host.
- **Positive:** Nothing can be shown wrongly in the meantime: every object is masked until visibility is decided, and
  every decision is the kernel's own, with its reason.
- **Negative:** Until #4781 decided visibility (ADR-2457), the reads returned masked objects only.
- **Negative:** One more deployable, on port 8018.

## 6. Implementation & Verification

- `apps/kernel-api/src`: `Program.cs`, `ClaimsCurrentActor`, `DirectoryTenantResolver`, `KernelDatabaseHealthCheck`,
  `KernelMigrationHostedService`.
- `packages/kernel/src/Cadence.Kernel.Api`: `KernelReadEndpoints`, `KernelReadResponses`, `KernelReadMasking`,
  `KernelTenantResolution`, `KernelLineageEndpoints`, `KernelLineageResponses`, `KernelProblems`.
- `tests/Cadence.UnitTests/Kernel/Api/KernelApiHostTests.cs` boots the real host: 403 for a tenant code the directory
  does not know or a missing one, 401 unsigned, reads in the resolved tenant, masking in place and in search, the host's
  grant-backed visibility with and without a grant, protocol
  versions listed, masked and refused for a hidden study, 400 and 404 problems, CanI denied without a grant and
  permitted with one, each explained, no actor for a subject that is no accountable person, the command catalogue,
  search, and the lineage reads: a Trace as the reader with masked objects in place, the default backward walk,
  dependents grouped by kind with masked ones counted, an amendment's impact, 400 for bad lineage parameters or a
  first version, 404 for an unknown one, and 403 with no accountable reader.
- `tests/Cadence.UnitTests/Gateway/GatewayRouteCoverageTests.cs` and
  `tests/Cadence.UnitTests/AppHost/AppHostModelTests.cs` cover the route and the port.
