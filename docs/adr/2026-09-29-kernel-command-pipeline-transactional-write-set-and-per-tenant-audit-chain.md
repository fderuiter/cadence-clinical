# ADR-2449: Kernel Command Pipeline, Transactional Write Set and Per-Tenant Audit Chain

- **Status:** Accepted
- **Date:** 2026-09-29
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Issue:** #4779 (epic #4719)
- **Related:** ADR-2244 (kernel modular monolith), ADR-2246 (bitemporal persistence), ADR-2247 (kernel schema), ADR-2251 (policy evaluator behind `IAuthorizer`), ADR-2257 (attributable actors and named service accounts), ADR-2446 (single audit writer: the interim audit path for `apps/execution` and `packages/`)

---

## 1. Context & Problem Statement

The kernel specification (§5.1-5.4) requires that every change to kernel truth is one command that changes one
aggregate and, in the same database transaction, writes its provenance activity, domain events, an audit entry and
outbox rows (K-TX-1, K-TX-2, K-PROV-1). Commands carry an idempotency key and a reason for change (K-TX-3), always
have an accountable actor (K-TX-5), are authorized in the pipeline rather than in endpoint filters (K-AUTH-1), and
denials are audited (K-AUTH-2). Audit, event and provenance tables are insert-only, and tamper evidence is one hash
chain per tenant (§5.3). Errors carry registered codes (K-ERR-1).

Before this decision the kernel had none of that. `KernelDbContext` accepted any `SaveChanges`, stamped audit fields
through the shared `GxPAuditInterceptor`, and wrote `audit_log_entries` rows keyed per entity, with no provenance,
events, outbox, idempotency or chain.

## 2. Decision Drivers & Constraints

- **21 CFR Part 11 / ALCOA+ (PRD-SYS-053):** every change attributable, reasoned, time-stamped from the kernel clock
  and tamper-evident.
- **All-or-nothing:** a state change without its audit link, or an event without its state change, must be impossible,
  not merely unlikely.
- **Enforced by the database** where possible, so a bug or a direct SQL session cannot bypass it.
- **Retry-safe:** clients and message consumers retry; a retry must never apply a change twice.
- **Scope:** kernel only. ADR-2446 governs today's module audit path (`Cadence.Database.GxPAuditInterceptor`,
  execution's `EfAuditLogWriter`/`AuditActions`, `AuditLogEntry`, `AuditLogChainVerifier`) until those modules move
  onto kernel commands; this ADR is the target model they migrate to. `packages/security`'s `GxPAuditInterceptor` is
  unchanged by either. Two rules carry over from ADR-2446 unchanged: there is exactly one audit writer (here, the
  command transaction), and timestamps are hashed as UTC truncated to whole microseconds, because PostgreSQL
  `timestamptz` holds no more. The kernel bakes the truncation into `KernelAuditHash` and the pipeline's clock read,
  so a caller cannot forget it.

## 3. Options Considered

1. **Keep the EF `SaveChangesInterceptor` and add events and outbox rows from it.** Rejected: an interceptor sees
   entities, not commands, so it cannot know the actor's intent, the reason, the idempotency key or which aggregate
   the command meant to change, and it cannot refuse a second aggregate.
2. **Event sourcing** (events as the source of truth, state rebuilt by replay). Rejected by K-EVT-2 and ADR-2246:
   kernel truth is bitemporal tables; events are the integration contract.
3. **An explicit command pipeline over a per-command unit of work that owns the transaction.** Chosen.

## 4. Decision Outcome

1. **Contracts in `Cadence.Kernel.Abstractions/Commands`:** `ICommand<TResult>` (tenant, idempotency key, reason for
   change), `IKernelCommandSender`, `CommandResult<T>` (value, registered `KernelError`, or replay), `Actor`
   (`Person`, `ServiceAccount` with `svc:` ids, `AutomationAgent` with `agent:` ids; `system`, `anonymous` and similar
   ids are refused at construction, following ADR-2257), the `EventEnvelope` of spec §5.2 and `[KernelEvent(type,
   schemaVersion)]`.
2. **Error registry (K-ERR-1):** every `KernelError` is a public static field with a code `K.<Area>.<Name>` and an
   HTTP status; `KernelErrors.All` collects them, and a unit test fails on a duplicate or malformed code.
3. **Pipeline in `Cadence.Kernel.Application` (`KernelCommandPipeline`):** validate the command → resolve the actor
   from `ICurrentActor` (K-TX-5) → find the handler → authorize through `IAuthorizer` (K-AUTH-1; a denial is written
   as its own provenance activity, `DecisionDenied` event and `Denied` audit link, K-AUTH-2) → replay or reject a used
   idempotency key (K-TX-3) → run the handler → commit. Recorded time comes from `IClock`, truncated to microseconds so
   the hash computed before the write matches the stored row.
4. **Unit of work in `Cadence.Kernel.Persistence` (`KernelCommandTransaction`):** one `KernelDbContext` and one
   PostgreSQL transaction per command. The handler may load or add exactly one aggregate; touching a second throws
   (K-TX-1). On commit it stamps the aggregate's `CreatedAt`/`CreatedBy`/`ReasonForChange`/`VersionIndex`, then adds
   the provenance activity, events and their outbox rows, the audit link and the idempotency record, and saves once.
5. **Write guard (K-TX-2):** `KernelDbContext.SaveChanges` throws unless the pipeline's transaction opened a write
   window, so no code path can change kernel state outside a command. The shared `GxPAuditInterceptor` is no longer
   registered for the kernel, and `Cadence.Kernel.Persistence` no longer references `Cadence.Database`.
6. **One audit chain per tenant (§5.3):** `audit_chain_heads` holds each tenant's last sequence and hash. A commit
   inserts the head if missing, locks it `FOR UPDATE`, and appends a link whose SHA-256 hash covers a canonical JSON
   array of every field plus the predecessor's hash. Concurrent commands for one tenant serialise on that row, so the
   chain has no gaps and never forks. `KernelAuditChainVerifier` re-derives a tenant's chain (sequence, predecessor,
   recomputed hash, head) for inspection.
7. **Insert-only tables (§5.3 item 2):** `kernel.enforce_insert_only()` triggers reject `UPDATE`, `DELETE` and
   `TRUNCATE` on `audit_entries`, `domain_events`, `provenance_activities` and `idempotency_records` with SQLSTATE
   `KA001`, whoever the database user is. `audit_chain_heads` and `outbox_messages` are the only mutable bookkeeping.
8. **Idempotency (K-TX-3):** `idempotency_records` is keyed by `(tenant_id, idempotency_key)` and stores a SHA-256
   request hash and the serialised result. The same key and payload returns the stored result with
   `Replayed = true`; the same key with a different payload returns `K.Command.IdempotencyConflict` (409). When two
   sends with one key race, the winner's stored outcome is the loser's answer. The loser checks for it again whenever
   it would otherwise fail: when its handler refuses (the winner's aggregate is already visible, so it sees
   `AlreadyExists`), and when its commit hits a unique violation (on the key or on the aggregate) or a concurrency
   conflict. Only when no outcome is stored under the key does the loser's own error stand.
9. **First commands:** `ProvisionTenant` and `RegisterOrganization`, registered by `AddKernelCommandPipeline()`.
10. **Migration `CommandPipeline`** creates the six tables and the triggers and drops `audit_log_entries`, which no
    deployment has written to (the kernel is not yet hosted).
11. **Outbox relay (`KernelOutboxRelay`):** delivers committed events to the host's `IKernelEventPublisher`
    at least once. Each pass claims due rows with `FOR UPDATE SKIP LOCKED`, so concurrent relays never deliver the
    same row at once, and marks each row dispatched or schedules a retry after 2^attempts seconds (capped at one
    hour) with the error recorded. A tenant's rows are claimed oldest first, and a failed row holds back that
    tenant's later rows until it is delivered, so one relay per deployment keeps each tenant's order. Subscribers
    deduplicate on `EventId`. The relay touches only delivery bookkeeping, never kernel truth.
12. **Root sealing (`KernelAuditChainSealer`):** verifies a tenant's chain end to end and, when it is intact and has
    grown, inserts a row in the insert-only `audit_chain_seals` table recording the head's sequence and hash, as
    the service account `svc:audit-sealer`. A broken chain is never sealed and is logged as critical. The verifier
    also checks every seal still matches its link, so a privileged user who deletes the newest links and rewinds the
    head is caught even though the remaining chain is internally consistent.
13. **Scheduled jobs:** `AddKernelBackgroundJobs()` runs the relay continuously (back to back while there is a
    backlog, then every 2 s) and the verify-and-seal pass over every tenant hourly. Exactly one host per deployment
    calls it.
14. **Verification under load:** the verifier reads the chain head first and checks links only up to it. A link and
    its head update commit together, so commands appending during verification never make an intact chain look
    broken.

## 5. Consequences & Trade-offs

- Positive: a kernel change without its audit, provenance, event and outbox rows cannot be committed; the database
  itself refuses edits to history; retries are safe; errors are stable API.
- Positive: the per-tenant chain replaces the two per-entity-type chains of the old design (inventory §3 item 7).
- Negative: commands for one tenant serialise on the chain head for the short tail of each commit. Acceptable at
  expected write rates; a later sealing job can shard by time if measurement says otherwise.
- Negative: tests and tools that need to insert rows directly must open a write window explicitly
  (`KernelPostgresFixture.SeedAsync`).
- The triggers do not replace database grants. Production roles should also be denied `UPDATE`, `DELETE` and
  `TRUNCATE` on these tables; the triggers are the guarantee that holds even for the owner.
- No EF Core execution retry strategy is configured: a command's transaction is not safely re-runnable by EF, so
  callers retry the whole command with the same idempotency key.

### Follow-up

- Seals live in the same database as the chain, so they detect tampering by anyone who cannot also rewrite
  `audit_chain_seals`. Exporting each seal to storage outside the database (a WORM bucket, or a signed timestamp)
  would extend that to the database owner.
- Recording the grants and evidence behind each permit and denial (K-AUTH-2) arrives with the policy evaluator
  (ADR-2251, #4781); until then `IAuthorizer` is a port the host must provide.
- Kernel API endpoints that call `IKernelCommandSender`, and the host wiring of `ICurrentActor` from the gateway
  identity.

## 6. Implementation & Verification

- Code: `packages/kernel/src/Cadence.Kernel.Abstractions/Commands`, `Cadence.Kernel.Application/Commands`,
  `Cadence.Kernel.Persistence/Commands`, migration `20260929223332_CommandPipeline`.
- `tests/Kernel/Integration/KernelCommandPipelinePostgresTests.cs` (real PostgreSQL): the full write set as one
  activity; replay and conflicting reuse of a key; concurrent sends with one key write once; no actor and invalid
  commands write nothing; audited denials; a failure injected at every commit stage persists nothing; duplicate codes
  map to `K.Aggregate.AlreadyExists`; concurrent commands keep one tenant's chain linear; the verifier detects an
  edited link; every insert-only table rejects `UPDATE`, `DELETE` and `TRUNCATE`; K-TX-1; optimistic concurrency.
- `tests/Kernel/KernelCommandPipelineTests.cs`: actor rules, error registry, request hash, time truncation, and the
  order of refusals before any unit of work opens.
- `tests/Kernel/Integration/KernelSchemaPostgresTests.cs`: the write guard refuses `SaveChanges` outside the pipeline.
- `tests/Kernel/Integration/KernelOutboxAndSealingPostgresTests.cs` (real PostgreSQL): events delivered once and
  marked dispatched; a failed delivery retries after its backoff while only its own tenant waits; concurrent relays
  deliver each event once; an intact chain is sealed only when it grows; a broken chain is not sealed; a chain cut
  back below its seal is reported broken; verification stays intact while commands append.
- `tests/Kernel/KernelBackgroundJobsTests.cs`: the relay and sealing services are registered and resolvable.
- Code for items 11-14: `Cadence.Kernel.Persistence/Outbox`, `/Audit`, `/Background`, migration
  `20260929234442_OutboxRelayAndChainSeals`.
