# ADR-2485: Reader-Facing Activity Stream Projection

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The Clinical Canvas activity stream (Canvas doc PX-11, decision D5) shows what happened in a scope: a study, a
site, a protocol version or the whole tenant. People filter it by event family, actor and time. Issue #4844
records the gaps:

- Domain events are the integration contract (K-EVT-2). They are not authorised per reader and not masked for
  blinding, so they cannot be handed to readers as they are.
- An event names only its own subject (§5.2). "Everything under this study" needs the subject's place in the zoom
  hierarchy (ADR-2481), which the event does not carry.
- Entries must link to objects by NodeRef (D7), so the Canvas opens them like any other object.

## 2. Decision Drivers & Constraints

- **K-EVT-2.** Integration events stay exactly as they are. The stream is a reader's view built from them, never
  a second contract, and never replayed to rebuild clinical state.
- **K-ID-2.** Every read runs under the tenant's Row-Level Security.
- **D1 (ADR-2471).** An object a reader may not see is masked with a reason, never silently dropped, through the
  one `INodeVisibility` contract that lineage, search and navigation use. Blinding is a condition, not a role
  (K-AUTH-4).
- **Scale.** A study accumulates events for years. "Latest 50 under this study" must be one index lookup, not a
  walk of the hierarchy per event at read time.
- **Requirements:** PRD-CRF-013 (site and tenant data isolation): a reader never sees another tenant's activity.

## 3. Options Considered

### Option 1: Read the outbox or domain events directly at query time

- **Cons:**
  - ❌ A scope filter would resolve every event's zoom path on every read.
  - ❌ Couples the Canvas to the integration contract's storage.

### Option 2: Write activity entries inside the command transaction

- **Cons:**
  - ❌ Adds a navigator read and a write to every command's transaction, the kernel's hottest path, owned by the
    command pipeline (ADR-2449).
  - ❌ A projection bug would fail clinical commands.

### Option 3: An asynchronous projection from the outbox (selected)

- **Overview:** a background projector copies each outbox envelope into `kernel.activity_entries`, adding its
  family and its scope (the subject's zoom path at the time of the event). Reads query that table and mask the
  page per reader.
- **Pros:**
  - ✅ The command path is untouched. The outbox and integration events are only read.
  - ✅ Scope filtering is a GIN index lookup on the stored path.
- **Cons:**
  - ❌ The stream lags commits by up to one projection interval (5 s by default).
  - ❌ The scope is the zoom path when the event happened. An object that later moves (a participant changing
    responsible site) keeps its old entries under the old parent. That is the historical truth, and it is what
    an activity log should show.

## 4. Decision Outcome

- **Chosen Option:** Option 3.
- **Justification:** it keeps the stream a derived, rebuildable read model outside the command transaction. It also
  keeps per-reader masking in the one D1 contract, applied at read time, so a change in someone's authority takes
  effect on the next read.

### 4.1 Contract (`Cadence.Kernel.Activity`)

- `IActivityStream.ReadAsync(tenant, ActivityQuery)` returns an `ActivityPage` for the current actor. It fails
  without an accountable actor (K-TX-5).
- `ActivityQuery(Scope, Families, ActorId, From, Before, Limit)`:
  - `Scope` is any NodeRef. It matches whatever version it names.
  - `Limit` is from 1 to 200.
  - `Before` is the previous page's cursor.
- `ActivityEntry` fields:
  - `EventId`, `Type` and `Family`;
  - `Recorded` and `OccurredValid`;
  - `ActorId`;
  - `SubjectKind` and `Subject` (the NodeRef pinned to the version the event produced);
  - `Masked`;
  - `CorrelationId`, which groups the entries of one request.
- Order is newest first by `(Recorded, EventId)`. `ActivityCursor(Recorded, EventId)` pages without gaps or
  repeats.
- `KernelEventFamilies` maps every kernel event type to its §5.2 family:
  - Tenant and organization events are Execution.
  - `AttestationRecorded` and `DecisionDenied` are Trust.
  - Unknown types fall into `Other`, and a test keeps `Other` unused.

### 4.2 Projection

- The table is `kernel.activity_entries`, keyed by `(tenant_id, event_id)`:
  - It is tenant-isolated (ADR-2250) and insert-only (§5.3 item 2), because a projected event never changes.
  - An index on `(tenant_id, recorded DESC, event_id DESC)` serves time order.
  - A GIN index on `scope text[]` serves scope filters.
- The scope is the tenant, every object on the subject's zoom path, and the subject itself, all unversioned.
- The zoom hierarchy (ADR-2481 §4.3) gains three kinds so that a study's activity includes its own records:
  - protocol versions and study standards profiles sit under their study;
  - an attestation sits under the object it signs.
- `KernelActivityProjector` runs per tenant, in batches of `ActivityBatchSize` (200), every `ActivityInterval`
  (5 s):
  - It reads outbox envelopes enqueued since the newest projected entry minus `ActivityLookBack` (5 min), and
    skips events already projected.
  - Because of that look-back, an event whose transaction committed after a later one is still projected.
  - It is idempotent and never changes the outbox, the domain events or integration delivery.
- The projector writes through the pipeline's write window (K-TX-2) as a derived read model, like the relay's
  delivery bookkeeping. It records no audit entry, because it creates no clinical state.
- **Outbox retention** must not purge a row before it is projected. The table is derived: a migration that
  re-creates it (for example to add a column) lets the projector rebuild it from the outbox.

### 4.3 Per-reader masking

- Each page is read unmasked from the store. The distinct subjects are then passed once to `INodeVisibility`
  (D1, ADR-2471).
- A masked entry keeps its place, time, type, family and subject kind. It loses its `Subject` and carries the
  `MaskReason` (`NotPermitted`, `Blinded`, `IdentityVault`), so the stream never silently shortens.
- Payloads are not exposed. The stream says what happened to which object and never copies clinical values, so
  masking the subject is enough to hide a blinded event.
- Read authorization of the stream as a whole (who may read a study's activity) comes with the authority model
  (#4781). It will be a capability checked on the scope through the same evaluator as `CanI` (ADR-2483).

### 4.4 Out of scope

- Enrollment events (ADR-2453) are scoped under their study site and study. `ParticipantRegistered` is scoped to
  the tenant only, because a Participant is identity only and belongs to no single study.
- A kernel HTTP endpoint and live push (server-sent events) wait for the kernel API host's tenant resolution and
  read authorization (#4780, #4781). Until then applications call `IActivityStream` in-process.
- A host registers `AddKernelActivityStream()`, `AddKernelActivityProjection()` and an `INodeVisibility`. Exactly
  one host per deployment runs the projector, as with the other background jobs.

## 5. Consequences & Trade-offs

- **Positive Impact:** a scope-filtered, per-reader activity stream with NodeRef links, and no change to commands
  or integration events.
- **Negative Impact / Technical Debt:**
  - A second copy of event metadata, without payloads.
  - A lag of a few seconds.
  - A commit delayed beyond the look-back window would be missed.
- **Mitigation Strategy:**
  - Command transactions are short and serialise per tenant on the audit chain head. Five minutes also absorbs
    clock skew between hosts.
  - A migration can re-create the table and let the projector rebuild it from the outbox.
  - Tests cover late commits, idempotence, and an unchanged outbox.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/kernel`:
  - `Cadence.Kernel.Abstractions/Activity`: `IActivityStream`, `ActivityQuery`, `ActivityEntry`, `ActivityPage`,
    `ActivityCursor`, `EventFamily`, `KernelEventFamilies`.
  - `Cadence.Kernel.Application/Activity`: `ActivityStream`, the `IActivityStore` port, `AddKernelActivityStream()`.
  - `Cadence.Kernel.Persistence/Activity`: `KernelActivityProjector`, `KernelActivityStore`, the hosted
    `KernelActivityProjectionService`, `AddKernelActivityProjection()`.
  - The `KernelScopeGraph` kinds described in §4.2.
  - `KernelBackgroundOptions.Activity*`.
  - Migration `KernelActivityStream`.
- **Verification Plan:**
  - `ActivityStreamTests`:
    - every kernel event type has a family;
    - the cursor is set only when more entries exist;
    - hidden objects are masked in place;
    - limits and the actor are checked.
  - `KernelActivityPostgresTests`:
    - study scope by zoom path, and idempotent projection with the outbox unchanged;
    - family, actor and time filters;
    - cursor paging returns every entry once;
    - late commits are projected;
    - a blinded protocol version is masked for one reader and visible to another;
    - no activity crosses tenants (PRD-CRF-013).
  - The existing schema, RLS and insert-only tests now include `activity_entries`.
