# ADR-2486: Work Queues as Capability-Keyed Definitions

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Role lenses (Canvas doc §2.7, PX-15, decision D10) give each person a work queue at the top of study home:
signatures due, queries to answer, visits to schedule, SAE follow-ups. Issue #4849 records the problem.

- If a queue is keyed on a role string ("CRA"), it contradicts the kernel rule that authority is computed from
  evidence (spec §9.3). A person would see work they cannot do, or miss work they were delegated.
- The kernel has no work-queue concept, so every application would invent its own.
- People who work across many studies (CRAs, medical monitors) need one queue over all of them (Canvas doc §9,
  open question 1).

## 2. Decision Drivers & Constraints

- **K-AUTH-7.** There is one decision procedure. A queue must hold exactly the items the corresponding command
  would permit now, so it cannot disagree with the command.
- **K-ID-2.** Every read runs under the tenant's Row-Level Security.
- **D1 (ADR-2471).** An object the reader may not see is masked with a reason, never silently dropped.
- **Versioned configuration.** Queue definitions change over time. A queue a person saw must be reproducible from
  the definition version it was computed with.
- **Requirements:** PRD-CRF-013 (site and tenant data isolation).

## 3. Options Considered

### Option 1: Queues per role, configured in each application

- **Cons:**
  - ❌ Keys work on role names, which the spec forbids as an authority source.
  - ❌ A revoked delegation leaves the work in the person's queue.

### Option 2: Materialized per-person queue rows, maintained by event handlers

- **Pros:**
  - ✅ Reads are one index lookup.
- **Cons:**
  - ❌ Every grant, delegation, revocation and expiry must rewrite rows for every affected item; a bitemporal
    grant that starts or ends in the future needs a scheduler.
  - ❌ A second copy of authorization that can drift from the evaluator.

### Option 3: Versioned definitions keyed by capability; items evaluated at read time (selected)

- **Pros:**
  - ✅ The evaluator that decides commands decides queue membership, so the two cannot disagree.
  - ✅ Revoking or expiring a grant removes items immediately, with nothing to rewrite.
- **Cons:**
  - ❌ One authorization decision per candidate item on every read (see §5).

## 4. Decision Outcome

**Chosen option: Option 3.**

### 4.1 Definitions

- `WorkQueueDefinition` (table `kernel.work_queue_definitions`) is one version of a tenant's queue: a `code`
  (lower snake_case, stable across versions), a `title`, a `capability` token with its verb class, and a `source`.
- `DefineWorkQueue` adds the next version of a code; the first is version 1. Versions are insert-only and the
  highest version is current. The command needs `work_queue.define` (Approve) at the tenant and raises
  `WorkQueueDefined` (Trust family).
- The capability is never a role name. When it is a kernel command's capability, its verb class must be the one
  the command declares, so an automation agent never sees Execute or Approve items it could not act on.
- The source must be registered in the host. The kernel registers `protocol.versions_in_review`; applications
  register sources for the objects they own.

### 4.2 Items

- An `IWorkItemSource` knows what is due, never who should do it. It returns candidates (a `NodeRef`, a title and
  when the item became due), oldest first.
- `IWorkQueues.ReadAsync` reads each current definition, takes up to 200 candidates from its source, and keeps a
  candidate only when `IAuthorizationProbe.CanIAsync(capability, subject)` permits the current actor now. The queue
  follows the person's grants and delegations in scope, and revoking a grant removes its items.
- Kept items the reader may not see are masked in place through `INodeVisibility`, so the count stays right.
- A queue reports `Truncated` when the reader has more items than the limit, or the source had more candidates
  than one read examines.
- Each queue carries the definition version it was computed with, as a pinned `NodeRef`.

### 4.3 Portfolio queues

A read with `Within = null` spans every study of the tenant, so the portfolio queue of a CRA or medical monitor is
the same read as a study queue without the scope filter. `Within` may name the tenant, a sponsor organization, a
study or one object. Queues never span tenants: a person with work in two tenants reads each tenant separately,
because tenant isolation (K-ID-2) is enforced by the database. This answers Canvas open question 1 for queues; the
portfolio home's layout remains an application decision.

### 4.4 Out of scope

- Sources for queries, visits, signatures due and SAE follow-ups. They arrive with the applications that own
  those objects.
- The kernel HTTP endpoint and the Canvas lens UI (web thread, ADR-2540).
- Default ordering of Needs Attention (D6, #4845).

## 5. Consequences & Trade-offs

- **Positive:**
  - A queue can never show work the person could not do, or hide work they were delegated.
  - Queue configuration is audited, versioned data.
- **Negative:**
  - A read makes one authorization decision per candidate examined, up to 200 per queue. Each decision reads the
    actor's grants.
- **Mitigation Strategy:**
  - The candidate cap keeps reads bounded; `Truncated` tells the reader there is more.
  - Batching decisions per resource path in the evaluator is a later optimisation behind the same contract.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/kernel`:
  - `Cadence.Kernel.Abstractions`: `WorkQueueDefinitionId`, `NodeKinds.WorkQueueDefinition`, `DefineWorkQueue`,
    `WorkQueueDefinitionState`, `WorkQueueErrors`, `KernelWorkItemSources`, the `WorkQueueDefined` event and
    `Cadence.Kernel.WorkQueues` (`IWorkQueues`, `WorkQueueQuery`, `WorkQueue`, `WorkItem`).
  - `Cadence.Kernel.Domain`: `WorkQueueDefinition`.
  - `Cadence.Kernel.Application/WorkQueues`: `WorkQueueService`, `DefineWorkQueueHandler`, the
    `IWorkQueueDefinitions` and `IWorkItemSource` ports, `AddKernelWorkQueues()`.
  - `Cadence.Kernel.Persistence/WorkQueues`: `KernelWorkQueueDefinitions` and `ProtocolVersionsInReviewSource`,
    registered by `AddKernelPersistence`.
  - Migration `WorkQueueDefinitions`.
  - `apps/web/src/canvas/nodeRef.ts` gains the `work_queue_definition` kind.
- **Verification Plan:**
  - `WorkQueueTests`: definition rules; only permitted items kept, asked with the definition's capability;
    masking in place; the limit; code filter and order; a missing source; no actor.
  - `KernelWorkQueuePostgresTests` with the production `AuthorityEvaluator`:
    - an approver sees the in-review version of the study their grant covers, due since it was submitted;
    - a person without the grant and an automation agent with it see nothing;
    - revoking the grant empties the queue;
    - defining a code again adds a version, and unknown sources and wrong verb classes are refused;
    - another tenant sees nothing (PRD-CRF-013).
  - `InsertOnlyTables_RejectUpdateDeleteAndTruncate` covers `work_queue_definitions`.
