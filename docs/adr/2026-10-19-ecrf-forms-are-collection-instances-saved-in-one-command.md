# ADR-2622: eCRF Forms Are Kernel Collection Instances Saved in One Command

- **Status:** Accepted
- **Date:** 2026-10-19
- **Authors:** @fderuiter
- **Deciders:** @fderuiter (accepted all recommended options on 2026-10-19)
- **Issue:** #5458 (under #5365, gate 3 of #5310); raised on #4719
- **Requirements:** PRD-SYS-001 (audit fields and audit entries), 21 CFR 11.10(e), 11.50 and 11.70
- **Amends:** kernel specification §5.1 (K-TX-1), §8.1 (new K-FACT-6), §9.5, §10.2, §10.5; extends ADR-2620 and ADR-2621
- **Design:** [eCRF reference slice: system design](https://claude.ai/code/artifact/ae008942-f0b5-4742-8fb0-71067b7b9b54)

---

## 1. Context & Problem Statement

The eCRF reference slice (#5365) runs one form end to end on the kernel: enter, correct with a reason, query, verify,
sign and lock. On `main` @ `7350b7b` the form keeps its own copy of clinical truth, and the kernel has no way to say
which values form one page:

| Topic       | Today                                                                                                                                                                                                                                           | Problem                                                                                                                    |
| ----------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------- |
| Values      | `FormSubmission.Payload` holds the form as JSON, and each save copies values into Execution's `clinical_observations` (`apps/execution/src/Application/Forms/FormSubmissionApplicationService.cs:831`), with `protocolVersionIndex: 1` (`:801`) | Two copies of clinical truth (K-TRUTH-1); the kernel `Observation` path of ADR-2620 is unused by the form                  |
| Form scope  | Spec §10.5 and ADR-2621 lock at "form instance" scope, but K-FACT-4 bans EDC concepts such as page IDs on Observations                                                                                                                          | Nothing in the kernel says which Observations belong to one form, so a form lock or a form signature has nothing to key on |
| Page save   | ADR-2620 makes each Observation its own aggregate, and K-TX-1 allows one aggregate per command                                                                                                                                                  | A 15-value page is 15 commands; a crash leaves half a page saved and cross-field edit checks run on half-states            |
| Concurrency | The form PUT takes no expected version (`apps/execution/src/Endpoints/FormEndpoints.cs:245`)                                                                                                                                                    | A stale browser overwrites another user's edit without a 409                                                               |
| Signature   | Approval signs a hash of the JSON payload; unlock nulls the signature (`apps/execution/src/Domain/Forms/FormSubmission.cs:181-183`)                                                                                                             | The signature binds to no record version, and the signed state is lost rather than kept beside the correction              |
| Edit checks | `AstEditCheckEngine` hard-codes blood-pressure checks and fatal results do not block completion (`FormSubmissionApplicationService.cs:349`); #5368 asks for checks "inside the command path"                                                    | Spec §10.2 says edit checks never reject a fact, so which checks may refuse a save is unsettled                            |

## 2. Decision Drivers & Constraints

- **K-TRUTH-1 and spec §12:** the kernel owns Observations, Assertions, Findings and Locks; EDC owns form instances,
  query workflow and UI state.
- **K-FACT-4:** no EDC concepts on Observations.
- **Atomic pages:** a user who saves a page expects all of it or none of it to be entered data.
- **Part 11:** a signature binds to the exact record it signed (11.70) and a correction never silently changes signed
  content (11.10(e)).
- **Gate 2 (ADR-2600, ADR-2601):** one host, one command path, module writes enlisted in the kernel transaction.
- Pre-deployment: no data to migrate and no back-compat layer.

## 3. Options Considered

### How a page save commits

| Option                                                                            | Page atomic                    | Cross-field checks see a whole page | Spec change                |
| --------------------------------------------------------------------------------- | ------------------------------ | ----------------------------------- | -------------------------- |
| A. One kernel command records all values of one CollectionInstance under its lock | Yes                            | Yes                                 | K-TX-1 reworded            |
| B. A saga sends one `RecordObservation` per field                                 | No: a crash leaves half a page | No                                  | None                       |
| C. Execution keeps values in its own tables and mirrors them to the kernel        | Yes                            | Yes                                 | None, but breaks K-TRUTH-1 |
| D. The whole form becomes one kernel aggregate holding its values                 | Yes                            | Yes                                 | K-FACT-4 and ADR-2620      |

- **A** keeps every Observation its own aggregate with a stable id (ADR-2621) and adds only a membership scope.
- **B** needs no spec change but shows users partial saves and runs soft checks on half-pages.
- **C** is the problem this slice removes.
- **D** makes every query, SDV Assertion and lineage edge point inside a form.

### Smaller choices

| Decision             | Chosen                                                                                  | Rejected alternative                                       |
| -------------------- | --------------------------------------------------------------------------------------- | ---------------------------------------------------------- |
| Drafts               | Save is commit; unsaved edits stay in the browser                                       | Server-side drafts promoted on submit                      |
| Conflict granularity | Expected revision per value                                                             | One version for the whole form                             |
| Where checks run     | Structural checks in the command; edit checks after commit; the browser asks the server | Every check in the command; a JavaScript port of the rules |
| Signature binding    | Kernel Attestation over the hash of the instance's revisions                            | Execution's HMAC-sealed payload manifest                   |
| Query ownership      | Execution aggregate, mirrored as kernel `Queried`/`QueryAnswered`                       | Queries as kernel Assertions only                          |

## 4. Decision Outcome

- **Chosen Option:** A, with the smaller choices above.
- **Justification:** one copy of every value, atomic pages, and a form scope that locks and signatures can key on,
  for one new kernel concept and one reworded rule.

### 4.1 `CollectionInstance` (kernel)

`CollectionInstance(id, participant, event: ClinicalEventId, collection_spec_version, members: ObservationId[])` is
the kernel side of one form instance: which Observations were collected together, for which participant and visit,
under which CollectionSpec version (resolved from Applicability at the event's valid time, K-TIME-8). It is a
current-state head with insert-only membership: an Observation joins one instance and never leaves it. It carries no
layout, item order, workflow status or UI state; those stay on Execution's `FormInstance`, which references it 1:1.
It is the "form instance" scope key of §10.5 and ADR-2621, and the target of form signatures (§4.5).

### 4.2 K-TX-1 reworded

> **K-TX-1** One command changes **one aggregate**, or the member Observations of one CollectionInstance under that
> instance's exclusive lock and the shared scope locks that cover it (§10.5). Other multi-aggregate processes are
> sagas driven by events.

`RecordCollectedValues(CollectionInstanceId, entries[{ObservationId?, Concept, Value, ExpectedRevision}],
ReasonForChange)` records new values and appends correction revisions in one transaction. Any stale
`ExpectedRevision` refuses the whole save with 409 and returns the current revisions; two people editing different
fields of one form both succeed one after the other. `AssertVerified` over one instance's members follows the same
rule. A command still takes at most one exclusive lock (ADR-2453, ADR-2621).

### 4.3 No server-side drafts

A saved page is entered data, with its revisions and audit entry. Unsaved edits live only in the browser, keyed by
form instance and the revisions they started from, and are never shown as saved. Form completeness ("every required
item has a value or a `Missing` reason, no unresolved blocking finding") is derived, not stored.

### 4.4 Structural checks before commit, edit checks after

This confirms spec §10.2 rather than changing it:

- **Structural checks** (Concept, value variant, UCUM unit, permissible CT values declared in the CollectionSpec) run
  inside `RecordCollectedValues` and refuse the save with 422 and field errors.
- **Edit checks** (ranges, cross-field plausibility, required items) run after commit in an outbox consumer through
  `IRuleEvaluator`, against the CollectionSpec version the instance records. They write Findings and may raise
  automatic queries as a service account; they never reject a fact. A blocking Finding keeps the form incomplete.
- The browser gets early feedback from a check endpoint that evaluates the same compiled rules and commits nothing,
  so there is one rule engine (K-RULE-3) and no JavaScript port. `AstEditCheckEngine` and engine_core's
  `RuleEvaluator` retire (#5368).

### 4.5 Signatures and re-signing

A form signature is an `Attest` with target kind `CollectionInstance` and a content hash over the sorted
`(ObservationId, Revision, value)` of its members, so it names the exact revisions signed (§9.5). A correction never
alters an Attestation: the signature stays bound to the revisions it covered and is shown as superseded, and the form
needs a new signature before it can be frozen again. Unlocking is a retracting lock Assertion (ADR-2621), never an
erased signature.

## 5. Consequences & Trade-offs

- **Easier:** one copy of every value; atomic page saves with honest 409s; one scope for form locks and signatures;
  queries, SDV and lineage keep pointing at stable `ObservationId`s.
- **Harder:** K-TX-1 has one named exception, and a page save holds one exclusive and up to four shared advisory
  locks for its transaction; edit-check results arrive after the save, so the screen must show them as pending.
- **Revisit:** a CollectionInstance per row group if log forms (adverse events) grow to hundreds of rows; moving cheap
  cross-field checks into the command if their latency bothers users.

## 6. Implementation & Verification

The spec amendment lands with this ADR. The code is tracked under #5365, one pull request per issue:

1. [x] Amend the kernel spec §5.1 (K-TX-1), §8.1 (new K-FACT-6, `CollectionInstance`), §9.5 (form signatures and
       re-signing), §10.2 (where checks run) and §10.5 (form scope key); record K035 in §17, ARCHITECTURE.md and the
       ADR index; add Collection Instance to CONTEXT.md.
2. [x] #5363 typed values; #4729 Assertions and scoped locks.
3. [x] #5459 `CollectionInstance` and `RecordCollectedValues`, with PostgreSQL tests for atomicity, per-value 409,
       lock races at each scope and replay.
4. [ ] #5460 Execution as a module over the kernel; #5368 edit checks; #5461 queries; #5465 SDV; #5463 signatures;
       #5466 locks; #5369 and #5469 the web screen.
5. [ ] #5365's definition-of-done suite on PostgreSQL as the application role, with RTM evidence (#5324).
