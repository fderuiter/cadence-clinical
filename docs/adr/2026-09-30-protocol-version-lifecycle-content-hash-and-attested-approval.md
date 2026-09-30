# ADR-2452: Protocol Version Lifecycle, Content-Hash Freezing and Attested Approval

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-012 (version pinning and lock enforcement), PRD-SYS-001 (audit entries on every lifecycle change)

---

## 1. Context & Problem Statement

ADR-2245 moved protocol versions into PostgreSQL as immutable snapshots, and spec §6.2 defines their lifecycle:
`Draft → InReview → Approved → Superseded | Withdrawn`, approval bound to an Attestation on the content hash, and
forks that leave the parent approved. The kernel's `ProtocolVersion` did not implement that (#4775):

1. Forking an amendment marked the parent `Superseded` at once. Approved versions must coexist until Effectivity
   (#4777) retires one everywhere.
2. There was no `Withdrawn`, no `number` and no content hash, and the payload had a setter, so an approved version
   could change.
3. There was no amendment (intent, classification, impacted elements, transition policy).
4. `Attestation` recorded a signer and a meaning, but not what was signed: no target, no content hash, no
   authentication time or method (spec §9.5, 21 CFR 11.50, 11.70).

This ADR decides how the kernel represents the lifecycle, when content is frozen, how approval is proven, and how
the database enforces it. It refines ADR-2245 and does not replace it.

## 2. Decision Drivers & Constraints

- **Driver 1 (21 CFR 11.70):** a signature must be bound to the exact content it signs, so that changing the record
  breaks the binding.
- **Driver 2 (defence in depth):** immutability cannot rest on the absence of a setter; a mistaken migration or a
  direct SQL session must not be able to change approved content (spec §5.3).
- **Driver 3 (one aggregate per command, K-TX-1):** approval changes the protocol version; the signature is a
  separate record that approval reads.
- **Driver 4 (coexistence):** amendments run in parallel with the versions they amend while ethics approvals arrive
  country by country (spec §7.1).
- **Constraint:** PostgreSQL 16, EF Core 10, the command pipeline (ADR-2449), tenant isolation (ADR-2250).

## 3. Options Considered

### When the content hash is fixed

- **Option A (selected): on submission for review.** The version is frozen when it enters review, so reviewers and
  approvers sign the same hash, and approval only has to compare hashes. Returning to draft clears the hash.
- **Option B: on approval**, as #4775 first proposed. The approver's attestation would have to name a hash that does
  not exist yet, or approval would have to create the signature itself, which breaks K-TX-1 and makes review
  signatures impossible.

### How approval is proven

- **Option A (selected): a separate `Attest` command, then `ApproveProtocolVersion(attestationId)`.** The signature is
  its own append-only record with meaning, target `(kind, id, content_hash)`, authentication time and method. Any
  command that needs a signature names one, and `IAttestationVerifier` checks signer, meaning, record and hash
  within the tenant. Reviews, locks, consent and withdrawal reuse the same mechanism.
- **Option B: signing fields on the approve command.** Simpler for one workflow, but each regulated action would
  grow its own signature format, and the protocol row would hold signature data.

### Where immutability is enforced

- **Option A (selected): domain and database.** The domain has no setters and returns `K.Protocol.InvalidTransition`.
  A trigger on `protocol_versions` repeats the state machine and the content freeze for every database user.
- **Option B: domain only.** Relies on every writer using the domain; rejected by Driver 2.

## 4. Decision Outcome

1. **ProtocolVersion** (`Cadence.Kernel.Domain.Entities`) has `number` (1 for the first version, then one more than
   the highest in the study, unique per study), `parent_version_id`, `status`, `content` (a JSON object, stored as
   the exact submitted text), `content_hash`, `amendment`, `approval_attestation_id`, `withdrawal_attestation_id` and
   `withdrawal_reason`. It no longer implements `IBitemporalEntity`. When a version applies is Effectivity (per scope,
   #4777), and when it was recorded is the audit chain.
2. **Content hash** (`Cadence.Kernel.Trust.ContentHash`): `sha256:` followed by lower-case hex SHA-256 over the
   canonical JSON form (keys
   sorted by ordinal at every depth, arrays in order, no insignificant whitespace, relaxed escaping). This is the same
   form as `Cadence.Security.CanonicalSigner`. Two serialisations of one document hash the same; `3` and `3.0` do
   not, because the written number is the content. Duplicate keys are rejected. The algorithm prefix leaves room for a
   later algorithm. Standards metadata (ADR-2460, `CanonicalJson`) canonicalizes and hashes the same way, so one
   attestation scheme covers protocol versions, standard versions and library items.
3. **Lifecycle:**
   - `CreateProtocolDraft` creates a study's first version only.
   - `ReviseProtocolDraft` works in `Draft`.
   - `SubmitProtocolVersionForReview` freezes the content and sets the hash.
   - `ReturnProtocolVersionToDraft` clears the hash.
   - `ApproveProtocolVersion` and `WithdrawProtocolVersion` name an attestation.
   - Withdrawal is allowed from `InReview` or `Approved`, is terminal, and its reason is the command's reason for
     change.
   - `Superseded` is a domain transition only. Effectivity (#4777) calls it once a later version is effective
     everywhere this one will ever be.
4. **Forks:** `ForkProtocolVersion` starts a draft from an approved version. The draft copies the content, carries an
   amendment and leaves the parent `Approved`. Two concurrent forks can pick the same number; the unique
   `(tenant_id, study_id, number)` index rejects the loser (409), which retries with a new idempotency key.
5. **Amendment** (`Cadence.Kernel.Protocol.ProtocolAmendment`) has intent, rationale, classification (substantial /
   non-substantial / urgent safety measure), summary of changes, impacted element ids and a `TransitionPolicy`. It is
   a value on the version it introduces, stored as `jsonb` and frozen with that version. It is not a separate
   aggregate, because it has no lifecycle of its own and is always changed together with its version (K-TX-1). The
   first version has none. Its classification travels on the `ProtocolVersionDrafted` event, so there is no separate
   `AmendmentClassified` event.
6. **Attestation** (`kernel.attestations`) has actor, meaning, `target_kind`, `target_id`, `target_content_hash`,
   `authenticated_at`, `method`, `recorded_at` and `reason`. It is insert-only (SQLSTATE `KA001`).
   - Only a person can sign (`K.Trust.PersonRequired`).
   - The authentication must be no more than five minutes old at the kernel's recorded time
     (`K.Trust.AuthenticationStale`, 21 CFR 11.200(a)(1)).
   - The target's current hash must equal the signed hash (`K.Trust.TargetContentChanged`), and a draft cannot be
     signed.
   - `protocol_version` is the first target kind. Target kinds are the registered node kinds (`NodeKinds`, ADR-2481),
     so an attestation target and a `NodeRef` name a record the same way; others are added as their records gain
     content hashes.
   - The meanings are those in spec §9.5 plus `Withdrawn`.
7. **Verification:** `IAttestationVerifier.VerifyAsync(AttestationRequirement(tenant, attestationId, meaning,
   target, signer))` reads through a context bound to the tenant. An attestation from another tenant is therefore not
   found. The signer is the actor executing the governed command, and it must be the person who signed (21 CFR 11.50).
   Approval with a missing, mismatched or stale attestation returns `K.Protocol.AttestationRejected` with the reason.
   Kernel persistence registers the implementation (`AddKernelPersistence`), and standards and library governance
   (ADR-2460) use the same port and store. `Attest` accepts `protocol_version`, `standard_version` and `library_item`
   targets.
8. **Database enforcement:** `kernel.enforce_protocol_version_lifecycle()` (SQLSTATE `KA002`) runs before insert,
   update, delete and truncate. It rejects:
   - an insert that is not a fresh draft;
   - any delete or truncate;
   - a change of identity (tenant, study, number, parent);
   - a content or amendment change outside `Draft`;
   - a status move outside the state machine;
   - a hash that is missing outside `Draft`, present in `Draft`, or changed after review;
   - an approval or withdrawal without its attestation;
   - any change to a `Superseded` or `Withdrawn` row.

   Both tables are tenant-isolated (ADR-2250).
9. **Reading without claiming:** `IKernelUnitOfWork` gains `ReadAsync` and `ReadAllAsync`. They read other aggregates
   untracked and do not claim them, so a command can base its decision on a parent version or on attestations and
   still change exactly one aggregate (K-TX-1).

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Approved content cannot change, in the domain or in SQL.
  - Every approval names the exact signature and content it rests on.
  - Parallel approved versions are representable, which Effectivity (#4777) needs.
  - One signature mechanism serves every regulated action.
- **Negative Impact / Technical Debt:**
  - The database does not recompute the canonical hash; it only checks the hash's shape and that it never changes.
    Recomputing canonical JSON in PL/pgSQL would differ from `jsonb` normalisation.
  - Studies are still seeded directly in tests, because no `RegisterStudy` command exists yet.
  - The five-minute window is a constant, not a setting.
- **Mitigation Strategy:**
  - The application computes the hash in one place (`ContentHash`), and a test pins it to `CanonicalSigner`.
  - Step-up authentication (security epic #4759) will feed `authenticated_at` from the identity provider's
    `auth_time`.

## 6. Implementation & Verification

- **Affected Assemblies:** `Cadence.Kernel.Abstractions` (commands, errors, events, `ContentHash`, value types),
  `Cadence.Kernel.Domain`, `Cadence.Kernel.Application` (handlers, `IAttestationVerifier`), `Cadence.Kernel.Persistence`
  (mapping, verifier, migration `ProtocolVersionsAndAttestations`), `Cadence.Ddf` (compiles `Content`).
- **Tracked by:** #4775, and the Attestation part of #4729.
- **Verification Plan:**
  - `ProtocolVersionTests`: every status against every operation, content validation, forks and amendments.
  - `ContentHashTests`: stability across serialisations, sensitivity to change, agreement with `CanonicalSigner`.
  - `AttestationTests`: signer kind, authentication freshness, target shape, mismatch reasons.
  - `KernelProtocolVersionPostgresTests`, through the pipeline against PostgreSQL:
    - the attested approval path end to end;
    - rejection of stale, wrong-meaning, unknown and cross-tenant attestations;
    - forks that keep the parent approved;
    - withdrawal;
    - the trigger refusing every forbidden change for the superuser.
  - `InsertOnlyTables_RejectUpdateDeleteAndTruncate` now covers `attestations`.
