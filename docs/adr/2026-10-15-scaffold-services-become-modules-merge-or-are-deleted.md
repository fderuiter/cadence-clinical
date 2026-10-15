# ADR-2610: Scaffold Services Become Kernel Modules, Merge or Are Deleted

- **Status:** Accepted
- **Date:** 2026-10-15
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Issue:** #4787 (part of #4719); follow-ups #5371–#5378
- **Supersedes:** ADR-047, ADR-100, ADR-2191, ADR-2310, ADR-2342, ADR-2374, ADR-2396, ADR-2399, ADR-2406

---

## 1. Context & Problem Statement

[ADR-2244](2026-09-29-clinical-kernel-modular-monolith-architecture.md) makes Cadence a modular monolith: one kernel
holds clinical truth and applications run in-process on it, referencing `Cadence.Kernel.Abstractions` only (K-XS-2).
What runs today is 19 separate ASP.NET hosts: 17 product services under `apps/`, the gateway and the kernel host. #5315
proposes the single composition host the products move into. Before anything moves, each of the 17 services needs a
disposition, because several of them define their own schemas for concepts the kernel owns (spec §2.3) and the gateway
still routes traffic to services that map no endpoints.

State on `main` at `7aa468b`:

| Service                                                             | Routes                                          | Persistence                  | What it holds                                                |
| ------------------------------------------------------------------- | ----------------------------------------------- | ---------------------------- | ------------------------------------------------------------ |
| execution                                                           | EDC core                                        | `ExecutionDbContext`         | Forms, observations, queries, subjects                       |
| ctms, etmf, payments, interop, imaging, safety, econsent, knowledge | real features (payments 30 routes, interop 24+) | own `DbContext` each         | Product workflows, mostly added since #4787 was filed        |
| designer                                                            | 1 (`ListStudies` returns an empty array)        | none                         | Protocol design entry point                                  |
| eisf                                                                | 0                                               | `EisfDbContext`              | ISF documents and section taxonomy                           |
| fileshare                                                           | 0 (two background workers)                      | `FileshareDbContext` + MinIO | Files, share grants, guest links, conversion and transcoding |
| notifications                                                       | 1 (`POST /render`)                              | `NotificationsDbContext`     | Notification records, deliveries, six Liquid templates       |
| ai_gateway                                                          | 0                                               | none                         | Health only; its package references are unused               |
| org                                                                 | 0                                               | `OrgDbContext`               | Delegation of authority, personnel assignments, training log |
| quality                                                             | 0                                               | `QualityDbContext`           | Deviations, root-cause analyses, CAPA records                |
| tickets                                                             | 0                                               | `TicketsDbContext`           | Support tickets, comments, attachments                       |

org, quality and tickets have identical 33-line `Program.cs` files apart from their descriptions. Every service with a
`DbContext` also defines a module-local audit table and uses `string` study, site or participant IDs.

## 2. Decision Drivers & Constraints

- **K-SCOPE-1:** a concept belongs in the kernel only if it explains provenance, authority, meaning or lifecycle of
  clinical evidence across more than one application. Everything else is an application module.
- **Spec §2.3 and K-XS-1:** applications never declare their own Study, Site, Participant, Delegation, Role or
  `*AuditLog` types.
- **Spec §12:** the application capability map fixes which kernel concepts each capability is built on.
- **Keep what has users or features; delete what has neither.** A host with no routes and no tests carries cost (a
  process, a route, a schema, a compose service) and no value.
- **No new frameworks** (user direction, 2026-10-15): the existing stack hosts the modules.

## 3. Options Considered

### Option 1: Keep all 17 services and convert each to a module

- **Pros:**
  - ✅ No decision needed per service.
- **Cons:**
  - ❌ Carries seven hosts with no routes into the composition host.
  - ❌ Keeps org's delegation model alongside the kernel's Grants, contrary to spec §2.3.

### Option 2: Disposition each service against spec §12 (selected)

- **Pros:**
  - ✅ Ten modules with real features stay; storage and plumbing services fold into the module or host that owns
    the concern.
  - ✅ Services that duplicate kernel truth or sit outside K-SCOPE-1 go.
- **Cons:**
  - ❌ Code for deviations, CAPA and tickets is discarded and must be rebuilt if those capabilities are wanted later.

### Option 3: Delete every scaffold and rebuild modules from the kernel up

- **Pros:**
  - ✅ No legacy shapes to migrate.
- **Cons:**
  - ❌ Throws away the features payments, interop, ctms, etmf and others have grown.

## 4. Decision Outcome

- **Chosen Option:** Option 2.

| Service                                                                                  | Disposition                                                                                                                 | Follow-up |
| ---------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------- | --------- |
| execution, ctms, etmf, payments, interop, imaging, safety, econsent, designer, knowledge | **Module**: becomes `Cadence.Modules.X` over kernel IDs, hosted in the composition host (#5315)                             | #5378     |
| eisf                                                                                     | **Merge into etmf**: ISF and TMF are one capability in spec §12 ("TMF / ISF")                                               | #5371     |
| fileshare                                                                                | **Merge into etmf**: file storage, sharing and conversion are document concerns of the TMF                                  | #5372     |
| notifications                                                                            | **Merge into the composition host**: rendering and delivery become a host capability modules call in-process                | #5373     |
| ai_gateway                                                                               | **Delete**: no routes; AI actors are a kernel concern (Proposals, #4783)                                                    | #5374     |
| org                                                                                      | **Delete**: delegation of authority and personnel assignments become kernel Grants, training becomes Qualification evidence | #5375     |
| quality                                                                                  | **Delete**: deviations arrive with the kernel's quality expansion (spec §15 phase 3); CAPA workflow is built on it then     | #5376     |
| tickets                                                                                  | **Delete**: a help desk is outside K-SCOPE-1                                                                                | #5377     |

- **Justification:** the ten modules have product features and map to rows of spec §12. eisf and fileshare hold the
  same evidence as etmf. Notifications is shared plumbing with one route. The four deleted services have no routes, and
  their data either duplicates kernel concepts (org) or is not clinical evidence (tickets), or will be rebuilt on kernel
  concepts that do not exist yet (quality, ai_gateway).

## 5. Consequences & Trade-offs

### Kernel replacements for duplicated entities

| Module-local entity                                                                  | Where                            | Kernel replacement                                                                                                                                                 |
| ------------------------------------------------------------------------------------ | -------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `*AuditLog`, `AuditLogEntry`, `TmfAuditLedgerSeal`                                   | every service with a `DbContext` | GxP audit interceptor and the per-tenant audit chain ([ADR-2449](2026-09-29-kernel-command-pipeline-transactional-write-set-and-per-tenant-audit-chain.md), #5312) |
| `DelegationOfAuthority`, `CtmsDelegation`, `DoaDelegationRecord`, `DelegationRecord` | org, ctms, knowledge             | `Grant` with a parent grant (spec §9.3; #4781, #5319)                                                                                                              |
| `PersonnelAssignment`, `CtmsInvestigatorGrant`                                       | org, ctms                        | Role `Grant` in a study or site scope (spec §9.3)                                                                                                                  |
| `TrainingLog`, training tables                                                       | org, knowledge                   | Qualification evidence ([ADR-2459](2026-09-30-qualifications-gate-capabilities.md), K031)                                                                          |
| `ClinicalQuery`, `CtmsClinicalQuery`                                                 | execution, ctms, interop         | Findings and `Queried` Assertions (spec §10.2)                                                                                                                     |
| `ClinicalSubject`, `SubjectAssignment`                                               | execution, interop               | `Participant` and enrollment ([ADR-2453](2026-09-30-participants-enrollment-lifecycle-and-gap-free-numbering.md))                                                  |
| `SubjectConsent`, `ConsentWithdrawal`                                                | execution, econsent              | Kernel `ConsentStatus` (spec §12, eConsent row)                                                                                                                    |
| `string` study, site and participant IDs                                             | all modules                      | `StudyId`, `SiteId`, `ParticipantId` in `Cadence.Kernel.Abstractions`                                                                                              |

- **Positive Impact:** seven fewer processes, gateway routes, compose services and schemas; no route points at a
  service that maps nothing; one delegation model.
- **Negative Impact / Technical Debt:** the quality, tickets and ai_gateway code is deleted, not ported.
  PRD-SYS-051 still describes an AI gateway microservice and must be reworded (#5374).
- **Mitigation Strategy:** every move and deletion has its own issue under #4787, each of which removes the gateway
  route, AppHost entry, compose service and project references together, so what is wired always matches what runs.
  The K-XS-1 baseline in `tests/Cadence.ArchitectureTests/Architecture` shrinks as modules switch to kernel types.

### ADRs superseded

| ADR                                                                                                                                                                                                                                                                       | Why                                                         |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------- |
| [ADR-2191](2026-08-21-ai-gateway-microservice-and-three-tier-clinical-intelligence-architecture.md)                                                                                                                                                                       | AI gateway microservice is deleted                          |
| [ADR-047](2026-07-27-quality-capa-scaffold.md), [ADR-2342](2026-08-05-quality-capa-management.md)                                                                                                                                                                         | Quality service is deleted                                  |
| [ADR-2310](2026-08-02-notifications-service-foundation.md)                                                                                                                                                                                                                | Notifications is no longer a standalone service             |
| [ADR-2374](2026-08-13-tickets-service-scaffold-and-gateway-integration.md), [ADR-100](2026-08-23-tickets-notifications-integration.md), [ADR-2396](2026-08-23-ticket-read-audit-policy-and-pagination.md), [ADR-2399](2026-08-24-tickets-escalation-and-deduplication.md) | Tickets service is deleted                                  |
| [ADR-2406](2026-08-26-eisf-gateway-compose-and-etmf-sync-topology.md)                                                                                                                                                                                                     | eISF is no longer a separate service synchronised with eTMF |

[ADR-2394](2026-08-23-doa-archive-destination-and-taxonomy.md) is superseded only for its org parts: delegation of
authority lives in kernel Grants.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/*`, `apps/gateway`, `apps/apphost`, `docker/docker-compose.yml`,
  `Cadence.sln`, the test projects and `packages/tooling_core` exporters. This ADR changes no code.
- **Order:** the deletions (#5374, #5376, #5377) have no dependencies. #5375 waits on the kernel authority model
  (#4781, #5319). The merges (#5371–#5373) and module conversion (#5378) land in the composition host (#5315).
- **Verification Plan:** each follow-up PR passes the gateway route test ([ADR-2448](2026-09-29-gateway-routes-verified-against-service-endpoints.md)),
  `pnpm cadence check arch-drift` and the architecture tests; #4787 closes when its acceptance criteria are met.
