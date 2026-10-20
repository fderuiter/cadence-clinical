# ADR-2610: Scaffold Services Become Kernel Modules or Merge

- **Status:** Accepted
- **Date:** 2026-10-15 (amended 2026-10-15: tickets, quality, ai_gateway and org are kept as modules; file storage becomes a host capability)
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Issue:** #4787 (part of #4719); follow-ups #5371–#5378
- **Supersedes:** ADR-2310, ADR-2406; partly ADR-047, ADR-100, ADR-2191, ADR-2342, ADR-2374, ADR-2394, ADR-2396, ADR-2399

---

## 1. Context & Problem Statement

[ADR-2244](2026-09-29-clinical-kernel-modular-monolith-architecture.md) makes Cadence a modular monolith: one kernel
holds clinical truth and applications run in-process on it, referencing `Cadence.Kernel.Abstractions` only (K-XS-2).
What runs today is 19 separate ASP.NET hosts: 17 product services under `apps/`, the gateway and the kernel host. #5315
proposes the single composition host the products move into. Before anything moves, each of the 17 services needs a
disposition, because several of them define their own schemas for concepts the kernel owns (spec §2.3) and the gateway
still routes traffic to services that map no endpoints.

State on `main` at `7aa468b`:

| Service                                                             | Routes                                          | Persistence                  | What it holds                                                                                                              |
| ------------------------------------------------------------------- | ----------------------------------------------- | ---------------------------- | -------------------------------------------------------------------------------------------------------------------------- |
| execution                                                           | EDC core                                        | `ExecutionDbContext`         | Forms, observations, queries, subjects                                                                                     |
| ctms, etmf, payments, interop, imaging, safety, econsent, knowledge | real features (payments 30 routes, interop 24+) | own `DbContext` each         | Product workflows, mostly added since #4787 was filed                                                                      |
| designer                                                            | 1 (`ListStudies` returns an empty array)        | none                         | Protocol design entry point                                                                                                |
| eisf                                                                | 0                                               | `EisfDbContext`              | ISF documents and section taxonomy                                                                                         |
| fileshare                                                           | 0 (two background workers)                      | `FileshareDbContext` + MinIO | Files, share grants, guest links, conversion and transcoding                                                               |
| notifications                                                       | 1 (`POST /render`)                              | `NotificationsDbContext`     | Notification records, deliveries, six Liquid templates                                                                     |
| ai_gateway                                                          | 0                                               | none                         | Health only; its package references are unused (2026-10-20: became the Intelligence module, `apps/intelligence`, in #5374) |
| org                                                                 | 0                                               | `OrgDbContext`               | Delegation of authority, personnel assignments, training log                                                               |
| quality                                                             | 0                                               | `QualityDbContext`           | Deviations, root-cause analyses, CAPA records                                                                              |
| tickets                                                             | 0                                               | `TicketsDbContext`           | Support tickets, comments, attachments                                                                                     |

org, quality and tickets have identical 33-line `Program.cs` files apart from their descriptions. Every service with a
`DbContext` also defines a module-local audit table and uses `string` study, site or participant IDs.

## 2. Decision Drivers & Constraints

- **K-SCOPE-1:** a concept belongs in the kernel only if it explains provenance, authority, meaning or lifecycle of
  clinical evidence across more than one application. Everything else is an application module.
- **Spec §2.3 and K-XS-1:** applications never declare their own Study, Site, Participant, Delegation, Role or
  `*AuditLog` types.
- **Spec §12:** the application capability map fixes which kernel concepts each capability is built on.
- **One process, not seventeen.** A separate host per capability carries a process, a gateway route, a compose service
  and its own wiring; a module in the composition host carries none of them.
- **Keep the capabilities the product is built around.** Tickets, quality and the AI gateway are wanted as distinctive,
  self-contained features of the application (maintainer direction, 2026-10-15), even though they map no routes yet.
- **No new frameworks** (user direction, 2026-10-15): the existing stack hosts the modules.

## 3. Options Considered

### Option 1: Keep all 17 services and convert each to a module

- **Pros:**
  - ✅ No decision needed per service.
- **Cons:**
  - ❌ Keeps eisf and fileshare apart from the eTMF evidence they hold, and notifications as a module when it is plumbing.
  - ❌ Keeps org's delegation model alongside the kernel's Grants, contrary to spec §2.3.

### Option 2: Disposition each service against spec §12 (selected)

- **Pros:**
  - ✅ Thirteen capabilities stay as modules; storage and plumbing services fold into the module or host that owns the
    concern.
  - ✅ No capability is lost: every service either stays as a module or folds into the one that owns its concern.
- **Cons:**
  - ❌ Four of the kept modules (tickets, quality, ai_gateway, org) are scaffolds today and have to be built out.

### Option 3: Delete every scaffold and rebuild modules from the kernel up

- **Pros:**
  - ✅ No legacy shapes to migrate.
- **Cons:**
  - ❌ Throws away the features payments, interop, ctms, etmf and others have grown.

## 4. Decision Outcome

- **Chosen Option:** Option 2.

| Service                                                                                  | Disposition                                                                                                                                                                                                                                          | Follow-up |
| ---------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------- |
| execution, ctms, etmf, payments, interop, imaging, safety, econsent, designer, knowledge | **Module**: becomes `Cadence.Modules.X` over kernel IDs, hosted in the composition host (#5315)                                                                                                                                                      | #5378     |
| tickets                                                                                  | **Module**: a self-contained support and issue-tracking feature in the composition host                                                                                                                                                              | #5377     |
| quality                                                                                  | **Module**: deviations, root-cause analysis and CAPA in the composition host; deviations use the kernel's Findings and Deviations as they arrive (spec §12, §15 phase 3)                                                                             | #5376     |
| ai_gateway                                                                               | **Module**: the in-process AI capability (provider routing, cost and PHI controls) in the composition host; AI actors act through kernel Proposals limited to Read and Propose ([ADR-2489](2026-09-30-ai-proposals-and-automation-agents.md), #4783) | #5374     |
| eisf                                                                                     | **Merge into etmf**: ISF and TMF are one capability in spec §12 ("TMF / ISF")                                                                                                                                                                        | #5371     |
| fileshare                                                                                | **Split**: blob storage and document/media conversion (`FileRecord`, MinIO access, the conversion and transcoding workers) become a host capability, because imaging and payments store files too; share grants and guest links merge into etmf      | #5372     |
| notifications                                                                            | **Merge into the composition host**: rendering and delivery become a host capability modules call in-process                                                                                                                                         | #5373     |
| org                                                                                      | **Module**: organization administration in the composition host (delegation-of-authority log, personnel assignments, training records); authority itself stays in kernel Grants and Qualifications (below)                                           | #5375     |

- **Justification:** the ten feature modules map to rows of spec §12. Tickets, quality and the AI gateway are kept as
  distinctive features to build out inside the application rather than as separate services; they are applications
  under K-SCOPE-1, so they stay out of the kernel and build on its concepts (Findings, Deviations, Proposals). eisf and
  fileshare's sharing and guest links serve TMF documents, while its storage and conversion serve every module that keeps files. Notifications is shared plumbing with one route. Org is kept as the module people use to administer delegation,
  staffing and training, on top of kernel authority.

### Org and kernel authority

The kernel stays the single source of authority. Org does not keep its own permission or delegation model:

- A delegation of authority is a kernel `Grant` with a parent grant (spec §9.3); a personnel assignment is a role
  `Grant` in a study or site scope; a training record is evidence for a Qualification (ADR-2459, K031).
- Org owns the administration around them: the DOA log as people read and sign it, assignment and training workflows,
  curricula, and archive and export (ADR-2394). It writes through kernel commands and reads current authority from the
  kernel; it never decides on its own who may do what.
- The kernel concepts spec §2.3 reserves (`Organization`, `Person`, `Role`, `Delegation`) are not redeclared in org;
  org stores module state keyed by their kernel IDs.

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

- **Positive Impact:** one process instead of seventeen product hosts; four fewer schemas (eisf, fileshare and
  notifications merge, and org's delegation tables give way to kernel Grants); one delegation model.
- **Negative Impact / Technical Debt:** tickets, quality, the AI gateway and org have to be built out before they do
  anything. PRD-SYS-051 describes an AI gateway _microservice_ and must be reworded to an in-process module (#5374).
- **Mitigation Strategy:** every move has its own issue under #4787, each of which removes the old host's gateway
  cluster, AppHost entry, compose service and project references together, so what is wired always matches what runs.
  The K-XS-1 baseline in `tests/Cadence.ArchitectureTests/Architecture` shrinks as modules switch to kernel types.

### ADRs superseded

| ADR                                                                   | Why                                                         |
| --------------------------------------------------------------------- | ----------------------------------------------------------- |
| [ADR-2310](2026-08-02-notifications-service-foundation.md)            | Notifications is no longer a standalone service             |
| [ADR-2406](2026-08-26-eisf-gateway-compose-and-etmf-sync-topology.md) | eISF is no longer a separate service synchronised with eTMF |

Partly superseded, for hosting only: [ADR-2191](2026-08-21-ai-gateway-microservice-and-three-tier-clinical-intelligence-architecture.md)
(AI gateway), [ADR-047](2026-07-27-quality-capa-scaffold.md) and [ADR-2342](2026-08-05-quality-capa-management.md)
(quality), and [ADR-2374](2026-08-13-tickets-service-scaffold-and-gateway-integration.md),
[ADR-100](2026-08-23-tickets-notifications-integration.md), [ADR-2396](2026-08-23-ticket-read-audit-policy-and-pagination.md)
and [ADR-2399](2026-08-24-tickets-escalation-and-deduplication.md) (tickets). Their feature decisions stand; the
capability runs as a module in the composition host instead of a separate service.
[ADR-2394](2026-08-23-doa-archive-destination-and-taxonomy.md) is superseded only for its org parts: delegation of
authority lives in kernel Grants, and org administers it as a module.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/*`, `apps/gateway`, `apps/apphost`, `docker/docker-compose.yml`,
  `Cadence.sln`, the test projects and `packages/tooling_core` exporters. This ADR changes no code.
- **Order:** #5375 (org) also waits on the kernel authority model (#4781, #5319). The merges (#5371–#5373) and the
  module conversions (#5374–#5378) land in the composition host (#5315).
- **Verification Plan:** each follow-up PR passes the gateway route test ([ADR-2448](2026-09-29-gateway-routes-verified-against-service-endpoints.md)),
  `pnpm cadence check arch-drift` and the architecture tests; #4787 closes when its acceptance criteria are met.
