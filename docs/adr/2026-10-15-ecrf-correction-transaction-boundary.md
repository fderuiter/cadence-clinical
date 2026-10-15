# ADR-2620: The eCRF Correction Transaction Boundary

- **Status:** Accepted
- **Date:** 2026-10-15
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Issue:** #5366 (part of #5310, gate 2), the correction step of #5365
- **Requirements:** PRD-SYS-001 (audit fields and audit entries, 21 CFR Part 11 § 11.10(e))

---

## 1. Context & Problem Statement

Correcting a value on an eCRF is the first clinical write the reference slice (#5365) runs through the kernel command
pipeline ([ADR-2449](2026-09-29-kernel-command-pipeline-transactional-write-set-and-per-tenant-audit-chain.md)). Today
the Vue eCRF writes a correction only to a ledger in the browser, and the Execution service's `PUT /forms/{id}` saves
the form payload, every derived `clinical_observations` row and the auto-queries in one EF `SaveChanges`, with its own
audit table and no outbox. The coordination review asked whether that form, its observations and its queries should
become one aggregate by amending K-TX-1 ("one command changes one aggregate").

Cadence is pre-deployment, so the boundary can be set from domain ownership rather than from what today's code
happens to save together. This ADR answers the four questions #5366 asks before the correction is implemented (§4.1).

## 2. Decision Drivers & Constraints

- **K-TRUTH-1 and spec §2.3, §8.1:** a clinical fact is a kernel Observation; an eCRF field is a representation of it.
  Corrections supersede and never overwrite (K-FACT-1), and status such as Locked is not a column on the value.
- **Fold-in ownership** (`fold-in/README.md`, revision 2): Observation and its correction history are owned by kernel
  Evidence; Locks by kernel Quality; form instances and the query workflow by the Execution module.
- **K-TX-1, K-TX-2, K-TX-4:** one aggregate per command; state, audit, events and outbox in one transaction; a command
  that must respect another aggregate's state takes that aggregate's lock and reads it, without writing it.
- The project owner's rules on #5366: the authoritative correction, its reason, its audit evidence and its outgoing
  work commit atomically; no correction is acknowledged while its authoritative value waits on an event consumer; one
  authoritative audit path, with no dual-audit transition.

## 3. Options Considered

### Option 1: One aggregate for form, observations and queries (amend K-TX-1)

- **Pros:** ✅ Matches today's single `SaveChanges`.
- **Cons:** ❌ Makes the module workflow record own clinical values, contradicting K-TRUTH-1 and the fold-in ownership.
  ❌ Puts query workflow (a module process with its own lifecycle and actors) inside every correction's lock.
  ❌ Needs two `DbContext`s sharing one connection across the module/kernel boundary (K-XS-2).

### Option 2: The Observation is the aggregate; everything else reacts after commit (selected)

- **Pros:** ✅ K-TX-1 and K-TX-2 hold as written. ✅ One authoritative audit path (the kernel chain). ✅ The lock rule
  is checked at commit time through K-TX-4 rather than by convention.
- **Cons:** ❌ Projections are eventually consistent. ❌ Form entry must record kernel Observations before the form
  screen can correct them (the next step).

## 4. Decision Outcome

- **Chosen Option:** Option 2.

### 4.1 The four questions of #5366

#### Q1. Which record owns the authoritative clinical value?

The kernel `Observation` (table `kernel.observations`, with its insert-only `kernel.observation_revisions`). Each
correction appends a revision holding the value, the reason, who recorded it and when; revision _n_'s recorded period
ends where revision _n+1_ begins, so the pre-correction view is reproducible without updating any history row.

The Execution form instance owns workflow (completion, review, signature) and is not an owner of values. Its payload
and its `clinical_observations` rows are today a competing copy; they become a projection of kernel Observations when
form entry records observations (the "enter" step of #5365), and are not written by a correction.

#### Q2. What does "correction saved" promise?

When the kernel answers a correction with success, all of this is durable in PostgreSQL, in one transaction:

- the observation's new current value and its new revision, superseding the previous one, with the reason;
- the observation's version, incremented exactly once, and the version the caller expected was the version it changed
  (a stale expected version is refused, and a concurrent change between read and commit is refused);
- the observation was not locked when the transaction committed;
- the authoritative audit entry (one link of the tenant's hash chain, which covers the reason), the provenance activity,
  the `ObservationCorrected` domain event and its outbox row, and the idempotency record for the request.

It does **not** promise that projections, edit-check re-runs, query reactions, exports or notifications have happened.

#### Q3. Which changes must commit together?

Exactly the set above, through `KernelCommandPipeline` and `KernelCommandTransaction`. It changes one aggregate (the
Observation), so K-TX-1 holds unchanged and **no amendment to K-TX-1 is needed**. The lock is a separate aggregate
(`ObservationLock`, kernel Quality); `CorrectObservation` and `LockObservation` both take the lock aggregate's advisory
lock first (K-TX-4), the correction then reads the lock and refuses a locked value. The two commands therefore
serialise: either the correction commits before the lock, or the lock commits first and the correction is refused.

#### Q4. Which downstream effects may happen after commit?

Everything that reacts to `ObservationCorrected` through the outbox: the Execution form projection, re-running edit
checks and the resulting query workflow (raising, relinking or auto-closing queries), SDV reset as an Assertion,
the activity stream, standards exports and notifications. Each is idempotent on the event id. None of them holds the
authoritative value, so a correction is complete when the kernel commit returns.

### 4.2 Commands, HTTP and audit

- **Commands** (`Cadence.Kernel.Abstractions/Commands/ObservationCommands.cs`), each authorised against the
  enrollment the observation belongs to:
  - `RecordObservation` (`observation.record`): the first value, from the enrollment's participant and study.
  - `CorrectObservation` (`observation.correct`): a new value with a reason and the expected version.
  - `LockObservation` (`observation.lock`): locks the value; there is no unlock in this slice.
- **HTTP:** the kernel host maps them under `/api/v1/kernel/observations` (ADR-2540's host), takes the reason from the
  gateway-signed identity (ADR-2495), the idempotency key from `Idempotency-Key`, and the expected version from the
  request's `expected_version`. It answers 409 for a stale version and 423 for a locked observation.
- **Audit:** the kernel audit chain is the only audit of an observation. Execution's `GxPAuditInterceptor` keeps
  auditing module records (#5312) and is not involved in a correction.

## 5. Consequences & Trade-offs

- **Positive Impact:** one write path for a clinical correction, with its guarantees proven against PostgreSQL.
- **Negative Impact / Technical Debt:** until form entry records kernel Observations, the eCRF grid (`EcrfView.vue`)
  still keeps its demo values in the browser; corrections run from the observation record screen. Execution's
  `PUT /forms/{id}` still rewrites its own payload copy; it is retired when the form becomes a projection.
- **Mitigation Strategy:** the next step of #5365 binds form entry to `RecordObservation` and the grid's correction to
  the same client call; the Execution payload route is removed in that change.

## 6. Implementation & Verification

- **Affected:** `packages/kernel` (domain, commands, persistence, migration, `Cadence.Kernel.Api`), `apps/kernel-api`
  (hosts the command pipeline), `apps/web` (observation record screen).
- **Verification:** `tests/Cadence.IntegrationTests/Kernel/KernelObservationPostgresTests.cs` (persistence, rollback at
  every commit stage, stale version, lock against correction race) and
  `KernelObservationHostPostgresTests.cs` (the HTTP path through the real host, and the browser-to-database check).
