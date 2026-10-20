# ADR-2621: Observation Corrections Are Revisions; Locks Are Scoped Assertions

- **Status:** Accepted
- **Date:** 2026-10-19
- **Authors:** @fderuiter
- **Deciders:** @fderuiter (chose Option C, "Split", on 2026-10-19)
- **Issue:** #5404 (under #4719); blocks #4729
- **Requirements:** PRD-SYS-001 (audit fields and audit entries), 21 CFR 11.10(e) and 11.50
- **Amends:** kernel specification §5.2, §8.1 (K-FACT-1), §8.2, §10.5; ADR-2620 §4.1 Q3 and §4.2

---

## 1. Context & Problem Statement

ADR-2620 (#5395) built the first clinical write through the kernel: record, correct and lock an Observation. It made
two modelling choices that the kernel specification does not describe, and the spec was not amended (main @ `57f57ef`):

| Topic      | Kernel spec                                                                                                                                                               | Code since ADR-2620                                                                                                                                                                          |
| ---------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Correction | A **new Observation** with `Supersedes`, the old row's `recorded_period` closed (§8.1 K-FACT-1, K-TIME-1)                                                                 | The **same** Observation aggregate appends an insert-only `ObservationRevision`; `Value`/`Revision` on the head row repeat the latest one (`Observation.cs`, `kernel.observation_revisions`) |
| Lock       | `Locked` is an **Assertion at a scope** (form instance, participant, study site, study) with an Attestation; unlock is a retracting Assertion with a reason (§8.2, §10.5) | A separate per-observation `ObservationLock` aggregate (`kernel.observation_locks`); no Attestation, no scope above one observation, no unlock                                               |
| Event      | `LockApplied`, `LockReleased` (§5.2)                                                                                                                                      | `ObservationLocked`                                                                                                                                                                          |

K-rule tests will be written against one of the two. AGENTS.md says a structural change that contradicts the spec is
held and raised on #4719, so this ADR settles which side moves, topic by topic. Cadence is pre-deployment, so moving
either side costs only code and a migration, not data.

## 2. Decision Drivers & Constraints

- **K-FACT-1 intent:** a value is never overwritten, every change carries a reason, and the pre-correction view is
  reproducible. Both models meet this; the question is the shape.
- **Stable references:** Queries target an `ObservationId` (§8.2, inventory §3 item 10). SDV, coding and
  adjudication Assertions, eCRF projections, lineage edges and exports all point at an Observation.
- **Regulatory scope of a lock:** database lock is `Locked` at **Study** scope (§10.5). It must also stop new
  observations being recorded and must be reversible with a reason and a signature (21 CFR 11.10(e), 11.50).
- **K-TX-1/K-TX-4 and ADR-2453:** one aggregate per command; at most one advisory lock per command today.
- The ADR-2620 guarantees (one commit with audit and outbox, expected version, lock race refused) must keep holding and
  keep their PostgreSQL tests.

## 3. Options Considered

### Option A: Amend the spec to the code (revisions and per-observation lock aggregates)

| Dimension     | Assessment                                                               |
| ------------- | ------------------------------------------------------------------------ |
| Complexity    | Low: spec text, K-rule wording and §5.2 only                             |
| Cost          | Smallest now; deferred cost when study lock and unlock arrive            |
| Scalability   | A study lock needs one row per observation and does not stop new records |
| Fit with spec | Breaks §10.5's single lock model used by forms, participants and sites   |

- **Pros:** ✅ No code change. ✅ Stable `ObservationId` for every reference.
- **Cons:** ❌ Per-observation locks cannot express database lock, form freeze or participant lock. ❌ No unlock or
  Attestation, so a second lock model would follow. ❌ Lock and freeze diverge from every other status (Verified,
  Queried, Coded), which the spec deliberately made Assertions.

### Option B: Change the code to the spec (superseding Observations and scoped `Locked` Assertions)

| Dimension     | Assessment                                                                                          |
| ------------- | --------------------------------------------------------------------------------------------------- |
| Complexity    | High: new Observation per correction, head-of-chain uniqueness, Assertion aggregate, lock hierarchy |
| Cost          | Rewrites the ADR-2620 slice and its tests before the eCRF entry step (#5365)                        |
| Scalability   | Fine for locks; corrections fan identity out across rows                                            |
| Fit with spec | Exact                                                                                               |

- **Pros:** ✅ Spec unchanged. ✅ An Assertion about a superseded value naturally stops applying to the new value.
- **Cons:** ❌ Every correction changes the identity of "the current value": Queries, SDV, coding, eCRF projections and
  lineage must follow a `Supersedes` chain or re-target on every correction. ❌ Closing `recorded_period` is an
  `UPDATE` of history, the one exception K-TIME-1 has to carve out. ❌ "Correct the latest value" needs a uniqueness
  rule on `Supersedes` and a lock keyed on the chain, not the row, to refuse forks.

### Option C: Split the decision (selected)

Amend the spec for **corrections** (keep revisions) and change the code for **locks** (scoped `Locked` Assertions).

| Dimension     | Assessment                                                                                             |
| ------------- | ------------------------------------------------------------------------------------------------------ |
| Complexity    | Medium: spec text for corrections; the lock part lands with the Assertion aggregate #4729 already owes |
| Cost          | No rework of the proven correction path; one lock model instead of two                                 |
| Scalability   | Stable identities; one Assertion locks a whole study                                                   |
| Fit with spec | Spec changes where the code is better, code changes where the spec is better                           |

## 4. Decision Outcome

- **Chosen Option:** Option C.
- **Justification:** each side moves where the other is better: revisions keep one stable identity for every
  reference, and scoped Assertions give one lock model, with unlock and a signature, from a single observation up to
  database lock.

### 4.1 Corrections: the spec adopts revisions

K-FACT-1 is reworded to:

> **K-FACT-1** Values are immutable. An Observation has a stable `ObservationId` and an insert-only, numbered series
> of revisions; a correction appends revision _n+1_, which supersedes revision _n_, with a reason, the recording actor
> and the recorded time. Revision _n_ is recorded from its `RecordedAt` until revision _n+1_'s, so no history row is
> ever updated. The head row's current value and version are a cache of the latest revision, changed only in the
> transaction that appends it. Status such as Verified or Queried is an Assertion, not a mutable column.

and the §8.1 record shape replaces `ObservationId? Supersedes` with `IReadOnlyList<ObservationRevision> Revisions`.
The `Observation` row moves in the §4 table from Bitemporal to Current-state + insert-only history (like
`ArtifactVersion`), so K-TIME-1 needs no exception for it.

**One tightening the code must take:** an Assertion that judges a **value** (Verified, Reviewed, CodedAs,
Adjudicated) names the revision it judged: `target = (ObservationId, Revision)`. A Query and a lock name only the
`ObservationId`. A reader then never sees Verified on a value nobody verified, even before the after-commit "SDV
reset" of ADR-2620 §4.1 Q4 runs; that reset becomes a projection, not a correctness step.

### 4.2 Locks: the code adopts scoped `Locked` Assertions

- `Locked` and `Frozen` are Assertions (built under #4729) with a scope of `Observation`, `FormInstance`,
  `Participant` (enrollment), `StudySite` or `Study`, an Attestation (meaning `Approved` or `Responsibility`), and a
  reason. Unlock is a retracting Assertion with its own reason and Attestation. `ObservationLock`,
  `kernel.observation_locks` and `LockObservation` are replaced; there is no data to migrate.
- Events are `LockApplied` and `LockReleased` with the scope, as §5.2 already names them; `ObservationLocked` goes.
- **Concurrency (amends ADR-2453's one-lock rule):** a write that a lock can cover takes a **shared** advisory lock on
  each scope key that covers it (study, study site, enrollment, form instance, observation: at most five, always in
  that order), then reads the active `Locked` Assertions for those scopes. `ApplyLock`/`ReleaseLock` take the
  **exclusive** advisory lock on its own scope key. Either the write commits before the lock, or the lock commits first
  and the write is refused (423). Exclusive locks are still at most one per command.
- A lock denies `RecordObservation` as well as `CorrectObservation` within its scope, which is what database lock
  needs.

## 5. Consequences & Trade-offs

- **Easier:** one lock model for observation, form, participant, site and database lock, with unlock and a signature;
  stable references for queries and assertions; the ADR-2620 correction path and its PostgreSQL tests stay.
- **Harder:** every lockable command takes up to five shared advisory locks; ADR-2453's rule changes from "at most one
  advisory lock" to "at most one exclusive lock, shared scope locks in a fixed order".
- **Revisit:** whether `Frozen` (data entry blocked, review allowed) and `Locked` (everything blocked) differ in what
  they deny, when form approval (#5365) uses `Frozen`. The first implementation (#4729) draws the line that way: both
  deny record and correct, and only `Locked` denies new assertions (SDV, queries, answers, retractions).

## 6. Implementation & Verification

The spec and ADR amendments land with this ADR. The code change is tracked by #4729:

1. [x] Amend the kernel spec §4 table, §8.1 (K-FACT-1 and record shape), §8.2 (value Assertions cite a revision),
       §10.5 (scope keys and shared locks) and §5.1 (ADR-2453 lock rule); link this ADR from ADR-2620 and ADR-2453 (K034).
2. [x] Build the Assertion aggregate (#4729) with `Locked`/`Frozen` and retraction; replace `ObservationLock`,
       its table, `LockObservation` and `ObservationLocked`; regenerate the schema doc and the web client.
3. [x] Keep `KernelObservationPostgresTests` green and add: a study-scope lock refuses both record and correct;
       unlock with a reason re-allows them; the lock-versus-correction race at each scope.
4. [ ] Update #4729's remaining scope and the K-FACT-1 test wording; close #5404.
