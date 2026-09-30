# ADR-2453: Participants, Bitemporal Enrollment Lifecycle and Gap-Free Enrollment Numbering

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SUB-001 (subject state transition matrix and its enforcement), PRD-SYS-001 (audit entries on every enrollment change)

---

## 1. Context & Problem Statement

Spec §7.4 makes `Participant` a pseudonymous kernel identity and `Enrollment(participant, study_site, status,
enrollment_number, valid_period, recorded_period)` a bitemporal record with a fixed state machine. The kernel's
`Participant` did not implement that (#4776):

1. It carried the study, the study site, a string subject code and a free-text status, and `Enroll()` changed the
   status with no transition rules and no time. One participant could not be in two studies.
2. Nothing assigned enrollment numbers. The legacy EDC counted rows, which gives duplicates under concurrency and
   gaps after rollback (inventory G1).
3. Subject codes had no uniqueness scope (inventory G2) and there was no code → id resolution (K-ID-5).
4. Withdrawal was a status like any other (inventory §3 item 2), with no reason, kind or signature.
5. The status had no history, so "what was this participant's status on 3 March, as we knew it on 10 March?" could
   not be answered (K-TIME-9).
6. `IKernelUnitOfWork` had no way to take the advisory lock that K-TX-4 describes.

This ADR decides how the kernel represents participants and enrollments, how numbers and codes are allocated, how
withdrawal is proven, and how the database enforces the lifecycle.

## 2. Decision Drivers & Constraints

- **Driver 1 (one truth, many views):** EDC's subject, eCOA's user and RTSM's randomized subject are views of one
  `ParticipantId` (spec §2); study membership must not be baked into the identity.
- **Driver 2 (bitemporal truth, ADR-2246):** status changes are often recorded after they happened, and corrections
  must not erase what was believed before.
- **Driver 3 (gap-free numbers, spec §7.4):** enrollment numbers are allocated per study with no gaps and no
  duplicates, whatever the concurrency.
- **Driver 4 (21 CFR 11.50, 11.70):** withdrawal is a regulated act by a named person, bound to the exact record it
  withdraws.
- **Driver 5 (defence in depth, spec §5.3):** the lifecycle must hold for a direct SQL session too.
- **Constraint:** one aggregate per command (K-TX-1), the command pipeline (ADR-2449), tenant isolation (ADR-2250),
  PostgreSQL 16 and EF Core 10.

## 3. Options Considered

### Where study membership lives

- **Option A (selected): `Participant` is identity only; `Enrollment` is a separate aggregate** per participant and
  study site. A participant can be screened in several studies, and rescreened at the same site after a screen
  failure, each as its own enrollment.
- **Option B: status fields on `Participant`.** Simple for one study, but it forbids multi-study participants and
  mixes identity with membership, against Driver 1.

### How the status history is kept

- **Option A (selected): an enrollment header with bitemporal status rows.** `kernel.enrollments` holds the current
  status, number and code for queries and constraints; `kernel.enrollment_status_periods` holds one row per status
  period with `valid_period` and `recorded_period`, append-only apart from closing `recorded_period` (K-TIME-1).
  A change supersedes the current row and records two new ones: the old status up to the change, and the new status
  from it. As-of reads use the rows; everything else uses the header.
- **Option B: the enrollment row itself is bitemporal.** Every change would copy the whole enrollment, and unique
  indexes on code and number would have to be partial over current rows only.

### How numbers are allocated

- **Option A (selected): `max + 1` under the study's advisory lock.** `ChangeEnrollmentStatus` to `Enrolled` takes
  `IKernelUnitOfWork.LockAsync<Study>(studyId)`, reads the highest number in the study, and assigns the next one. The
  lock is held until the command's transaction has committed or rolled back, so a rollback leaves no gap and a
  concurrent enrollment waits.
- **Option B: a PostgreSQL sequence per study.** Sequences are not transactional: a rolled-back enrollment burns a
  number, which breaks Driver 3.
- **Option C: a counter row on the study.** Correct, but it would make every enrollment update the study aggregate
  as well (K-TX-1).

### How withdrawal is proven

- **Option A (selected): a dedicated `WithdrawParticipant` command naming an attestation** with meaning `Withdrawn`
  over the enrollment's content hash, signed by the acting person (the mechanism of ADR-2452).
- **Option B: a reason string on the generic status command.** Rejected by spec §7.4 and Driver 4.

## 4. Decision Outcome

1. **Participant** (`kernel.participants`): `id` and `tenant_id` only. `RegisterParticipant` creates it
   (`ParticipantRegistered`). Identifying details belong to the IdentityVault (ADR-2253, #4782).
2. **Enrollment** (`kernel.enrollments`, `EnrollmentId`, node kind `enrollment`): participant, study, study site,
   `subject_code`, `status`, `status_since` (valid time of the current status), `enrollment_number`,
   `withdrawal_kind` and `withdrawal_attestation_id`. The study is repeated from the study site, and a composite
   foreign key `(tenant_id, study_site_id, study_id)` keeps them consistent.
3. **State machine** (`EnrollmentTransitions`, spec §7.4):
   `Screening → ScreenFailed | Enrolled | Withdrawn`, `Enrolled → Randomized | Active | Withdrawn`,
   `Randomized → Active | Withdrawn`, `Active → Completed | Withdrawn`. `ScreenFailed`, `Completed` and `Withdrawn`
   are terminal. `Enrolled → Active` serves non-randomized studies. An illegal move is `K.Execution.InvalidTransition`
   (409). This refines the PRD-SUB-001 table: the spec allows withdrawal before randomization and `Enrolled → Active`,
   which the table forbids.
4. **Time rules:** a change is effective strictly after the current status began and not after it is recorded
   (`K.Execution.TransitionTimeInvalid`). Two changes recorded at the same instant are a concurrency conflict, and the
   second one retries.
5. **Status history** (`kernel.enrollment_status_periods`): `IBitemporalEntity` rows keyed by enrollment, with the
   K-TIME-1 and K-TIME-2 guards of ADR-2246. `IEnrollmentDirectory.StatusAsOfAsync(tenant, enrollment, validAt,
   knownAt)` answers the as-of question.
6. **Subject codes** (`SubjectCode` value object): 1 to 64 printable characters without surrounding whitespace,
   unique per study (unique index on `(tenant_id, study_id, subject_code)`), checked under the study lock
   (`K.Execution.SubjectCodeTaken`). `IEnrollmentDirectory.ResolveParticipantAsync(tenant, study, code)` is the
   K-ID-5 resolution query; commands and events carry ids.
7. **One active enrollment per participant and study site:** the handler refuses a screening that overlaps an active
   enrollment of the same participant at the same study site in valid time (`K.Execution.AlreadyActiveAtSite`), and
   the deferrable exclusion constraint `ex_enrollment_status_periods_one_active` repeats it in the database.
8. **Numbering:** gap-free per `(tenant, study)` as in Option A, assigned only on `Screening → Enrolled`, unique
   (partial unique index), and immutable afterwards. Per-StudySite numbering, which spec §7.4 allows when the study
   declares it, waits for a study-level setting.
9. **Advisory locks (K-TX-4):** `IKernelUnitOfWork.LockAsync<TAggregate>(id)` takes a session-level
   `pg_advisory_lock` on the command's own connection, keyed by the first 8 bytes of
   `SHA-256("{tenant}:{aggregate type}:{id}")`, and releases it when the command's unit of work is disposed, after
   commit or rollback. A command takes at most one lock, so lock ordering cannot deadlock. The lock is read-only with
   respect to the locked aggregate (K-TX-4). Protocol version numbering (ADR-2452) now takes the same study lock, so
   concurrent forks no longer race for a number.
10. **Withdrawal:** `WithdrawParticipant(kind, attestationId, effectiveAt)` with `WithdrawalKind` (withdrawal by
    subject, physician decision, adverse event, lost to follow-up, death, protocol deviation, study terminated by
    sponsor, other). The attestation must mean `Withdrawn`, target the enrollment's current content hash, and be
    signed by the acting person; otherwise `K.Execution.AttestationRejected`. `ChangeEnrollmentStatus` cannot reach
    `Withdrawn` (`K.Execution.WithdrawalRequiresCommand`). The enrollment's content hash covers its identity, code,
    status, status start and number, so a signature given before a later change no longer matches. `Attest` accepts
    `enrollment` targets.
11. **Events:** `ParticipantRegistered`, `ParticipantScreened`, `ParticipantEnrolled` (with the number),
    `EnrollmentStatusChanged` (from, to) and `ParticipantWithdrawn` (kind, attestation). Each carries the valid time
    of the change as its `occurred_valid`.
12. **Capabilities:** `participant.register`, `participant.screen` (on the study site), `enrollment.change_status`
    and `participant.withdraw` (on the enrollment), all `VerbClass.Execute` (ADR-2483).
13. **Database enforcement:** `kernel.enforce_enrollment_lifecycle()` (SQLSTATE `KE001`) runs before insert,
    update, delete and truncate on `enrollments`. It rejects:
    - any delete or truncate;
    - an insert that is not a fresh `Screening` row without number or withdrawal;
    - any change to a terminal row;
    - a change of tenant, participant, study, study site or subject code;
    - a status move outside the state machine, or a status start that does not move forward with the status;
    - a number set other than on `Screening → Enrolled`, or changed afterwards;
    - withdrawal fields present without `Withdrawn`, or `Withdrawn` without them.

    All three tables are tenant-isolated (ADR-2250).

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - One participant identity serves every study and application.
  - Status can be read as of any valid and known instant.
  - Enrollment numbers are gap-free under concurrency, which the tests show.
  - Withdrawal names the person, the kind and the exact state withdrawn from.
  - The same lock primitive serves every "respect another aggregate's state" rule in K-TX-4.
- **Negative Impact / Technical Debt:**
  - Enrolling in a study is serialized per study. At clinical enrollment rates this costs nothing; a bulk import
    would enroll one participant at a time.
  - Eligibility (spec §7.4, G3) is not checked yet: `Screening → Enrolled` does not require a complete set of
    `Eligibility` Assertions until Assertions exist (#4727).
  - Screening is not gated on an active StudySite until Effectivity and StudySite activation land (#4777).
  - The navigator (ADR-2481) does not show participants yet; whether a study site's zoom children are participants
    or their enrollments is decided with the navigator.
  - Unblinding and arm assignment are RTSM Assertions (spec §7.4) and are not modelled here.
- **Mitigation Strategy:**
  - The handlers are the only writers, and each deferred rule has an issue that adds it at the command.

## 6. Implementation & Verification

- **Affected Assemblies:** `Cadence.Kernel.Abstractions` (`EnrollmentId`, `SubjectCode`, `EnrollmentStatus`,
  `WithdrawalKind`, `EnrollmentTransitions`, commands, errors, events, node kind), `Cadence.Kernel.Domain`
  (`Participant`, `Enrollment`, `EnrollmentStatusPeriod`), `Cadence.Kernel.Application` (handlers,
  `IEnrollmentDirectory`, `IKernelUnitOfWork.LockAsync`), `Cadence.Kernel.Persistence` (mapping, directory, advisory
  lock, `EnrollmentSql`, migration `ParticipantsAndEnrollments`).
- **Tracked by:** #4776.
- **Verification Plan:**
  - `EnrollmentTests`: every status pair against the state machine, subject code rules, the bitemporal split on each
    change, the time rules, numbering arguments, withdrawal and the content hash.
  - `KernelEnrollmentPostgresTests`, through the pipeline against PostgreSQL:
    - the lifecycle end to end, with as-of reads and code resolution;
    - eight concurrent enrollments numbered exactly 1 to 8, and a second study numbered from 1;
    - code uniqueness per study, one active enrollment per study site, and rescreening after a screen failure;
    - refused moves and times;
    - withdrawal refusing wrong-meaning, other-person, stale and unknown attestations;
    - the trigger, the append-only status rows and the exclusion constraint refusing forbidden changes for the
      superuser.
