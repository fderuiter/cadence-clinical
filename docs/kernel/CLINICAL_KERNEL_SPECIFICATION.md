# Cadence Clinical Kernel Specification v0.1

| | |
| --- | --- |
| **Status** | Draft for review — not yet normative |
| **Scope** | The shared domain model that every Cadence product module (EDC, CTMS, eTMF, RTSM, Safety, eCOA, QMS, …) builds on |
| **Supersedes** | The per-service entity models inherited from the Python topology (see §12) |
| **Companion work** | ADR-K001…K007 (§13), migration inventory (after this spec is accepted) |

Keywords **MUST**, **MUST NOT**, **SHOULD**, **MAY** follow RFC 2119. Every rule tagged `K-…` is an
invariant intended to become an automated test (architecture test, property test, or DB constraint).

---

## 0. Thesis

The trial is the centre of the system. A protocol says what *should* happen; sites and participants make
things *actually* happen; systems *record* that they happened. EDC, CTMS, eTMF, RTSM, Safety and eCOA are
**projections and workflows over one body of clinical truth**, not separate owners of it.

The kernel owns that truth. Product modules own only their workflows, projections and UI concerns.

Non-goals of the kernel: form rendering, monitoring-visit reports, TMF filing structure, randomization
algorithms, SDTM datasets, payments. All of these *consume* the kernel.

---

## 1. Design Principles

1. **Temporal relational state + immutable events + transactional outbox.** Not event sourcing. Current
   and historical state are queryable directly from tables; events explain *why* state changed and drive
   integration. No clinical read path may require replaying a stream.
2. **Bitemporal where the truth is contestable.** Track *valid time* (when it was true in the trial) and
   *recorded time* (when Cadence learned it) for protocol applicability, enrollment, consent, authority,
   and observations. Everything else is append-only or current-state.
3. **Corrections supersede; nothing is overwritten.** A correction closes the prior row's recorded
   interval and inserts a new row. This generalises the existing Merkle-chained `AuditLogEntry`.
4. **Standards are projections.** SDTM, ODM, USDM, FHIR, Dataset-JSON are generated *from* the kernel
   through versioned mappings. No kernel type is shaped by a standard's table layout.
5. **Authority is contextual and explicit.** No `User.Role`. Every command is evaluated against
   actor × capability × scope × time × resource state.
6. **AI is an actor with a narrower verb set**, not a feature (§7.6).
7. **Modular monolith first.** The kernel is a library + single schema with in-process APIs. Product
   modules may be split into services later; the kernel is not (§9, ADR-K001).

---

## 2. Forbidden Redefinitions

Product modules **MUST NOT** define their own version of the following. They reference kernel IDs.

| Kernel concept | Consequence for modules |
| --- | --- |
| Tenant, Organization, Person | No `Organization`/`Personnel` tables in `org`, `ctms`, `econsent`, … |
| Study, Protocol, ProtocolVersion, Amendment, Effectivity | No `StudyId` strings. `StudyId` is the kernel `StudyId` type. |
| Site, StudySite | Same for `SiteId`; CTMS site milestones hang off `StudySiteId`. |
| Participant, Enrollment, Applicability | EDC "subject", eCOA "user", RTSM "randomized subject" are all views of `ParticipantId`. |
| Event (planned/occurred), Visit | CTMS "monitoring visit" is a *different* thing (an operational activity), and MUST NOT reuse the clinical Visit name (§5.2). |
| Observation, Assertion, Source, Artifact, Provenance | EDC form fields, lab results, ePRO answers and imaging reads all resolve to kernel Observations. |
| Actor, Role, Capability, Grant, Delegation, Attestation | No module-local roles or DOA tables. eSignature is kernel Attestation. |
| Audit entry, Domain event, Outbox | Modules emit through the kernel; no module-local audit log tables (`CtmsAuditLog`, `OrgAuditLog`, …). |

Enforcement:

- **K-XS-1** `Cadence.ArchitectureTests` fails if any assembly outside `Cadence.Kernel.*` declares a type
  named `Study|Site|Participant|Subject|Observation|Organization|Person|Role|Delegation`, or a public
  member of type `string` named `*StudyId|*SiteId|*ParticipantId`.
- **K-XS-2** Product modules reference `Cadence.Kernel.Abstractions` only, never `Cadence.Kernel.Persistence`.

---

## 3. Identity and Ownership

### 3.1 Hierarchy

```
Tenant
 └─ Organization (Sponsor | CRO | Site institution | Vendor | Regulator | IRB/EC | Lab)
      └─ owns → Study            (sponsor organization owns the study definition)
            └─ StudySite         (association of Study × Site, with its own lifecycle)
 Site  = a physical/operational location belonging to an Organization (institution)
 Person = human identity, independent of any tenant-scoped role
```

- A **Tenant** is the isolation and contractual boundary (a Cadence customer). A tenant may contain many
  organizations, including its CRO and site partners.
- A **Study is owned by exactly one Organization** (the sponsor) within one Tenant.
- **Site ≠ StudySite.** `Site` is a durable location record. `StudySite` is that site *in that study*
  (activation state, country approvals, PI, effective protocol version). Almost all operational and
  clinical references point at `StudySite`.

### 3.2 Identifiers

- Every entity ID is a distinct strongly-typed struct wrapping a **UUIDv7** (`StudyId`, `SiteId`,
  `StudySiteId`, `ParticipantId`, `ObservationId`, …). No shared `Guid`/`string` ID types cross a public API.
- Human-facing codes (`SubjectCode`, `StudyCode`, `SiteCode`) are **attributes**, unique within a stated
  scope, never keys. Existing `StudyCode`/`SiteCode`/`SubjectCode` value objects are kept as such.
- IDs are opaque to clients and are never reused.

### 3.3 Cross-organization relationships

Collaboration is modelled, not implied:

`OrganizationRelationship(from, to, kind, study_scope?, effective_period)` where `kind ∈ {SponsorOf,
ContractedCro, AffiliatedSite, VendorTo, …}`, itself bitemporal.

A module or actor may see another organization's data **only** if a relationship *and* a capability grant
(§7) both allow it. Tenant membership alone never grants cross-organization access.

### 3.4 Tenancy invariants

- **K-ID-1** Every kernel table has `tenant_id NOT NULL`, and every FK is composite `(tenant_id, id)`, so
  cross-tenant references are impossible at the schema level.
- **K-ID-2** PostgreSQL Row-Level Security is enabled on every kernel table; the application role cannot
  bypass it. The session variable is set from the authenticated request in one place.
- **K-ID-3** Cross-tenant data exchange occurs only through an explicit `DataSharingAgreement` grant
  (e.g. sponsor ↔ external vendor in a different tenant), which is itself audited.
- **K-ID-4** Person identity is tenant-independent; *roles and access* are tenant-scoped. A person MAY have
  memberships in several tenants; nothing joins them implicitly.

---

## 4. Clinical Temporal Model

### 4.1 Two clocks

| Clock | Meaning | Column pair |
| --- | --- | --- |
| **Valid time** | When it was true in the trial world | `valid_from`, `valid_to` (`tstzrange`) |
| **Recorded time** | When Cadence asserted / superseded it | `recorded_from`, `recorded_to` (`tstzrange`) |

Canonical query form: `as_of(valid_at, known_at)`. The default for operational reads is
`(now, now)`. Regulatory reconstruction is `(t_clinical, t_inspection_cutoff)`.

**Which entities are bitemporal** (rule of thumb: a *fact someone can later dispute or correct*):

| Bitemporal | Append-only (immutable) | Current-state + audit |
| --- | --- | --- |
| Effectivity, Enrollment, Applicability, ConsentStatus, Grant/Delegation, OrganizationRelationship, StudySite lifecycle | DomainEvent, AuditEntry, Attestation, ProvenanceActivity, Artifact version | Reference/config data with no dispute semantics |
| Observation (see §5.4), Assertion | | |

**K-TIME-1** Bitemporal rows are never `UPDATE`d except to close `recorded_to`. **K-TIME-2** An exclusion
constraint (`EXCLUDE USING gist`) prevents overlapping `(entity, valid, recorded)` rectangles where a single
truth is required. **K-TIME-3** All timestamps are `timestamptz`; the kernel takes time only from `IClock`.

### 4.2 Protocol lineage

```
Study 1─* Protocol 1─* ProtocolVersion 1─* Amendment (a version is the result of ≥0 amendments)
```

- **Protocol**: the logical document lineage of a study.
- **ProtocolVersion**: an immutable, content-addressed snapshot (USDM document + hash). Once `Approved`
  it MUST NOT change; changes create a new version. Fields: `number`, `content_hash`, `parent_version`,
  `amendment_id?`, `state`, `sponsor_approval`.
- **Amendment**: the intent and rationale for a change; classification (substantial/non-substantial;
  safety-driven urgent measure), summary of changes, impacted concepts.

**ProtocolVersion state machine** (`Draft → InReview → Approved → Superseded | Withdrawn`):

- `Approved` requires an Attestation (§7.5) with meaning "sponsor approval".
- `Superseded` is set only when a later version is *effective everywhere it will ever be* — never
  automatically on approval. Multiple approved versions coexist legitimately.

### 4.3 Effectivity (where and when a version applies)

Approval ≠ effectiveness. A version becomes usable at a site only after **jurisdictional** approvals.

```
Effectivity(
  protocol_version_id,
  scope : Study | Country | StudySite,
  status: Pending | RegulatoryApproved | EthicsApproved | Effective | Withdrawn,
  valid_from, valid_to,          -- when it took effect there
  recorded_from, recorded_to,
  evidence : ArtifactId[]        -- IRB/EC approval letters, CTA/regulatory acknowledgements
)
```

Scopes resolve most-specific-first: `StudySite` → `Country` → `Study`. A version is **effective at a
StudySite at time *t*** iff the most specific matching Effectivity with status `Effective` is valid at *t*
(as known at the query's recorded time).

**K-TIME-4** A `StudySite` can be `Activated` only when at least one ProtocolVersion is Effective there.
**K-TIME-5** Effectivity requires ≥1 evidence Artifact of the appropriate class before reaching `Effective`.

### 4.4 Participant applicability

`Applicability(participant_id, protocol_version_id, valid_from, valid_to, basis, transition_id?)`
answers *"which protocol version governs this participant, when?"*

**Algorithm — initial assignment** (at consent/enrollment, `t_enroll`):

1. `candidates` = ProtocolVersions Effective at the participant's StudySite at `t_enroll`.
2. Filter to versions the participant **has consented to** (ConsentStatus references the consent-form
   version and its ProtocolVersion mapping). No consent → no applicability.
3. Choose the highest-numbered candidate; ties are a configuration error and MUST fail closed.

**Algorithm — amendment reaching an existing participant**: when a new version becomes Effective at their
StudySite, a **TransitionPolicy** (declared in the Amendment) decides per participant:

| Policy | Behaviour |
| --- | --- |
| `NewParticipantsOnly` | No Applicability change. |
| `RequiresReconsent` | Create pending Transition; Applicability changes only on valid re-consent. |
| `Automatic` | Applicability moves at `valid_from = version effective date` (rare; non-substantive changes). |
| `ByCohort(predicate)` | Policy applies to participants matching a kernel predicate (e.g. `epoch = Treatment`). |

**K-TIME-6** A participant has **exactly one** governing ProtocolVersion at any valid instant, or the
system is in a flagged inconsistent state that blocks data capture.
**K-TIME-7** "Expected schedule" is a **pure function** of `(ProtocolVersion, participant anchor events,
as-of)`; it is never stored as truth, only cached as a projection.
**K-TIME-8** An observation's applicable version is *resolved at query time from Applicability at the
observation's valid time*, and stamped into the Observation's provenance for reconstruction. The current
`ProtocolVersionTag` / `ProtocolVersionIndex` strings on observations and `ActiveProtocolVersion` on
`ClinicalSubject` are replaced by this.

Worked example (from the motivating question): *"Expected visit schedule under protocol v3 when Site 17
enrolled the participant, before amendment 4 became effective at that site"* =
`schedule(ProtocolVersion v3, anchor events of P, as_of = (t_enroll, known_at = t_inspection))`.

---

## 5. Clinical Facts and Provenance

### 5.1 Participant

- `Participant` is a pseudonymous kernel identity: `ParticipantId`, tenant, and nothing else identifying.
- **Direct identifiers** (name, DOB, contact) live in a separate **IdentityVault** (encrypted, own access
  capability, envelope-encrypted keys). This subsumes today's `EncryptedDemographics` blob on
  `ClinicalSubject`.
- `Enrollment(participant, study_site, screening_id, status, valid, recorded)` — bitemporal. The existing
  `SubjectTransitionGraph` states (`Screening → Randomized → Active → Completed | Withdrawn | Unblinded`)
  are retained as the Enrollment state machine. **Unblinding is an Attestation-bearing event**, not a flag.
- A person may be a Participant in several studies (separate `ParticipantId` per study); linking them is an
  identity-vault operation with its own capability and audit.

### 5.2 Activity, Event, Visit

Separate *plan* from *occurrence*:

| Concept | Meaning | Owner |
| --- | --- | --- |
| `PlannedActivity` | From the ProtocolVersion (USDM Activity/Encounter/Timing). Immutable, per version. | Kernel (derived from protocol) |
| `ClinicalEvent` | Something that happened (or was attempted) to/with a participant: visit, procedure, dose, AE onset, discontinuation. Has `kind`, `valid_time`, `participant`, `study_site`, and optional `planned_activity_id`. | Kernel |
| `Visit` | A `ClinicalEvent` of kind `Visit`, mapped to a planned encounter. | Kernel |
| `OperationalActivity` | Monitoring visit, site initiation, audit. About the *site/study*, not a participant. | CTMS |

Deviations from plan (missed, out-of-window, unscheduled) are **derived assertions**, not fields on visits.

### 5.3 Observation

An `Observation` is a **recorded clinical fact**: *this concept had this value for this participant over
this valid period, as asserted by this source.*

```csharp
public sealed record Observation(
    ObservationId Id,
    ParticipantId Participant,
    ClinicalEventId? Event,
    ConceptRef Concept,          // biomedical concept (e.g. CDISC BC / LOINC), not an SDTM variable
    ObservationValue Value,      // discriminated: Quantity(value, ucum) | Coded | Text | Boolean | Instant | Missing(reason)
    ValidPeriod Valid,
    RecordedPeriod Recorded,
    SourceRef Source,            // §5.5
    ObservationStatus Status);   // Recorded | Verified | Queried | Corrected | Entered-In-Error
```

- **K-FACT-1** Observation values are **immutable**. A correction is a new Observation that supersedes the
  prior (recorded interval closed, `supersedes` link kept, reason for change required).
- **K-FACT-2** Units are UCUM. Normalisation is derived, stored as a *derived* Observation with provenance
  pointing at its input (§5.6) — not as columns on the source observation (replaces today's
  `NormalizedValue/NormalizedUnit/IsOutlier/ReferenceRange*` on `ClinicalObservation`).
- **K-FACT-3** `Missing` is a first-class value with a reason (not done / not applicable / unknown).
- **K-FACT-4** Observations have **no** EDC concepts: no `PageId`, `VisitId` string, `IsSdvVerified`. Those
  are EDC projections/assertions (below).

### 5.4 Assertion

An `Assertion` is a **statement about a fact or another assertion** by an actor with authority:
`Verified(SDV)`, `Queried`, `QueryAnswered`, `Reviewed`, `Adjudicated`, `CodedAs(MedDRA/WHODrug)`,
`ProtocolDeviation`, `CausalityAssessment`. Assertions are append-only and bitemporal in the sense that a
later assertion may retract an earlier one.

This is where today's `ClinicalQuery` lifecycle, SDV flags, and medical-coding review land. **The
query workflow (open → answered → closed) stays in EDC as a workflow; each transition emits kernel
Assertions.** CTMS's `CtmsClinicalQuery` (a duplicate model) is deleted; CTMS reads query assertions.

### 5.5 Source and Artifact

- `Source` says *where a fact came from*: `{ ManualEntry(actor, form) | Device | LabFeed(vendor, batch) |
  ePRO(participant) | SourceDocument(artifact) | Derived(inputs) | Import(job) }`.
- `Artifact` is any versioned, hash-addressed content: a signed consent form, IRB letter, lab report PDF,
  DICOM series, generated dataset. `ArtifactVersion(hash, size, media_type, storage_ref, created)`.
  Artifacts are immutable per version; classification (e.g. TMF artifact type) is an Assertion so eTMF
  filing can be re-projected without touching the artifact.

### 5.6 Provenance

W3C PROV-shaped, minimal: `ProvenanceActivity(id, kind, actor, started, ended, inputs[], outputs[],
protocol_version_context, tool_version)`.

- **K-PROV-1** Every kernel write is produced by exactly one ProvenanceActivity (human command, import,
  device ingest, derivation, AI proposal execution). Provenance is created in the same transaction.
- **K-PROV-2** Derived facts (unit normalisation, range flags, toxicity grades, computed schedules cached
  as artifacts) MUST name their inputs and the versioned algorithm.
- **K-PROV-3** For AI-assisted actions, provenance records model identifier, prompt/plan artifact, and the
  approving actor (§7.6).

### 5.7 Standards as projections

`Projection = f(kernel state as-of (valid, known), MappingVersion) → standard artifact`. SDTM/ADaM,
ODM, FHIR and Dataset-JSON are each a **MappingVersion** owned by a data-management module. A generated
dataset is an Artifact whose provenance names the as-of pair and mapping version, making any export
reproducible. **K-PROJ-1** No kernel type references SDTM variable names.

---

## 6. Actors, Identity and Authentication Boundary

- `Actor = Person | ServiceAccount | AutomationAgent` (AI agents are the third kind, see §7.6).
- Authentication (OIDC/Keycloak today) yields an `Actor`; the kernel never stores credentials.
- Step-up authentication for signatures is an authenticator-level fact recorded on the Attestation
  (retaining the existing 60 s step-up design).

---

## 7. Authority and Action

### 7.1 Model

```
Actor ─(Membership)→ Organization
Actor ─(Grant)→ Capability @ Scope, effective_period, conditions
Role  = named bundle of capabilities (administrative convenience only)
Delegation = Grant issued *by* an actor who holds the capability with grant-option, bounded by it
```

`Scope` is a path: `tenant / organization / study / country / study_site [/ participant-cohort]`.
A grant at a scope applies to everything beneath it.

### 7.2 Capabilities

Capabilities are `resource.action` tokens (e.g. `observation.record`, `observation.verify`,
`enrollment.unblind`, `protocol.approve`, `artifact.file`, `identity.reveal`), each declaring its **verb
class**:

| Verb class | Meaning |
| --- | --- |
| `Read` | Observe kernel state |
| `Propose` | Create a pending change that has no effect until executed |
| `Execute` | Cause the state change |
| `Approve` | Attest to a proposal or state (regulatory sign-off) |

### 7.3 Decision function

```
Authorize(actor, capability, resource, at, known_at) → Permit(obligations[]) | Deny(reason)
  1. Resolve actor's active Grants at (at, known_at)   -- delegations end when DOA ends
  2. Match capability and scope-covers(resource)
  3. Evaluate conditions: protocol state, study/site state, artifact state, blinding, training currency
  4. Check separation of duties (e.g. approver ≠ executor)
  5. Emit decision + inputs to audit
```

- **K-AUTH-1** Authorization is evaluated in the kernel command pipeline, not in endpoint filters.
  Endpoint filters only authenticate and translate.
- **K-AUTH-2** Denials and permits are both audited with the grants that justified them.
- **K-AUTH-3** Delegation-of-Authority is a Grant set. A study activity performed outside an active
  delegation for that actor/task/site/time is **denied**, and retroactive DOA cannot make it valid (it
  can only create a flagged deviation).
- **K-AUTH-4** Blinding is a **condition**, not a role: unblinded-data capabilities are separate
  (`randomization.read_assignment`) and never included in bundled roles by default.
- **K-AUTH-5** Prerequisites (e.g. GCP training current, protocol-specific training complete) are
  conditions evaluated from kernel Assertions, so eTMF/CTMS evidence directly gates EDC actions. This
  replaces the cached `SiteComplianceCache` in `apps/execution`.

### 7.4 Separation of the four verbs

`Read` and `Propose` are safe by default; `Execute` and `Approve` require explicit grants and (for
regulated actions) an Attestation.

### 7.5 Attestation (e-signature)

`Attestation(id, actor, meaning, target(kind,id,version,content_hash), authenticated_at, method,
recorded_at, reason?)` — append-only. `meaning` is one of a controlled set (`Authored`, `Reviewed`,
`Approved`, `Verified`, `Responsibility`, `Consented`). The signature binds to a **content hash of the
target version**, so a signed record cannot silently change (21 CFR 11.70). Replaces `SignatureManifest`
on `FormSubmission` and the per-module `signature_manifestation/signer/signing_timestamp` columns
(`CtmsMonitoringVisit`, `CtmsRegulatoryForm`, …).

### 7.6 AI actors

- An AI agent is an `AutomationAgent` actor with its own identity and **default grants limited to `Read`
  and `Propose`**, scoped to the requesting human's scope (never wider).
- A **Proposal** is a first-class kernel object: `{ intent, plan (artifact), affected concepts, simulated
  impact, requested capabilities, expiry }`. A proposal has no effect on kernel state.
- `Execute` happens by (a) a human with the capability approving, executing as themselves *with the
  agent recorded in provenance*, or (b) a pre-authorised, narrowly-scoped standing grant that is itself
  human-approved and time-boxed.
- **K-AI-1** No code path lets an `AutomationAgent` invoke an `Execute` or `Approve` capability without an
  approval reference to a human Attestation or standing grant.
- **K-AI-2** Every executed proposal links proposal → approval → activity → resulting events.
- **K-AI-3** Agents read through the same authorization pipeline as humans; there is no privileged read
  path (agents see only what the delegating human may see).

---

## 8. Commands, Events, Audit and Transactions

### 8.1 Command pipeline

```
Command → Authenticate → Authorize(§7.3) → Load aggregate(s) → Domain decision (Result<T>)
        → [state change + ProvenanceActivity + DomainEvent(s) + AuditEntry + Outbox row]  -- ONE tx
        → commit → Outbox relay → subscribers (projections, other modules, integrations)
```

The existing `Result<T>` / `DomainError` pattern, `ChangeReasonHeaderFilter` semantics (reason for
change is mandatory on mutating commands) and the same-transaction audit write in `EfAuditLogWriter`
carry over unchanged in spirit; they move into the kernel pipeline.

- **K-TX-1** One command changes **one aggregate**. Multi-aggregate business processes are sagas driven by
  events; there is no distributed transaction.
- **K-TX-2** Domain events, audit, provenance and outbox rows are written in the same DB transaction as the
  state change, or the change does not happen.
- **K-TX-3** Handlers are idempotent; commands carry an idempotency key (needed for offline eCOA sync and
  device/lab feeds).
- **K-TX-4** Advisory/pessimistic locking (already in `AdvisoryLockSql`) is used per aggregate, not per
  table.

### 8.2 Event taxonomy

Events are past-tense facts, versioned and immutable: `Envelope{ event_id, type, schema_version, tenant,
occurred_valid, recorded, actor, activity_id, correlation_id, causation_id, payload }`.

| Family | Examples |
| --- | --- |
| Identity | `OrganizationRegistered`, `PersonAffiliated`, `SiteLinkedToStudy` |
| Protocol | `ProtocolVersionDrafted`, `…Approved`, `AmendmentClassified`, `EffectivityGranted`, `EffectivityWithdrawn` |
| Participant | `ParticipantEnrolled`, `ConsentGiven`, `ConsentWithdrawn`, `ApplicabilityChanged`, `ParticipantRandomized`, `ParticipantUnblinded`, `ParticipantWithdrawn` |
| Clinical | `EventRecorded`, `ObservationRecorded`, `ObservationCorrected`, `AssertionMade`, `AssertionRetracted` |
| Artifact | `ArtifactVersioned`, `ArtifactClassified`, `ArtifactAttested` |
| Authority | `GrantIssued`, `GrantRevoked`, `DelegationSigned`, `DecisionDenied` |
| Proposal | `ProposalCreated`, `ProposalApproved`, `ProposalExecuted`, `ProposalRejected` |

**K-EVT-1** Event schemas are additive-only within a major version; consumers tolerate unknown fields.
**K-EVT-2** Events are the *integration* contract; they are not replayed to rebuild clinical state.

### 8.3 Audit guarantees

1. **Completeness** — every kernel state change has a ProvenanceActivity and at least one AuditEntry.
2. **Immutability** — audit/event/attestation tables are insert-only (no `UPDATE`/`DELETE` grants; DB
   trigger rejects them).
3. **Tamper evidence** — hash chain per **tenant** (extends the current per-entity Merkle chain) with
   periodic root sealing (retains the existing Merkle-root-sealing design). Chain verification is a
   scheduled job and an on-demand inspector tool (`AuditChainVerifier` carries over).
4. **Reason for change** — mandatory for every mutating command; stored on the audit entry and on any
   superseding bitemporal row.
5. **Reconstructability** — for any `(valid_at, known_at)` the kernel can re-derive state from tables;
   a test suite asserts this for each bitemporal aggregate ("time-travel tests").

---

## 9. Module Architecture

```
Cadence.Kernel.Abstractions   ← IDs, value objects, commands/queries/events, ports (no EF, no ASP.NET)
Cadence.Kernel.Domain         ← aggregates, state machines, temporal logic (pure)
Cadence.Kernel.Application    ← command pipeline, authorization, policy evaluation
Cadence.Kernel.Persistence    ← EF Core + Npgsql, RLS, bitemporal support, outbox (internal)
Cadence.Kernel.Api            ← Minimal API surface for external clients + module APIs
Cadence.Modules.Edc|Ctms|Etmf|Rtsm|Safety|Ecoa|Qms|…   ← workflows + projections + UI
```

**ADR-K001 (proposed): modular monolith with one kernel schema.** Identity, authority, temporal and
provenance guarantees rely on cross-aggregate transactional consistency and RLS. Splitting the kernel
into services would recreate the synchronisation problem this spec exists to remove. Modules may later
be extracted, but each still talks to the kernel via `Kernel.Abstractions` (in-process today, typed
client tomorrow), never to its tables.

Module ownership contract: a module **MAY** own (a) its own schema for projections/workflow state,
(b) its commands, expressed as kernel commands or module-local workflow steps that emit kernel commands,
(c) read models fed by kernel events. A module **MUST NOT** (a) write kernel tables, (b) read kernel
tables directly, (c) hold an authoritative copy of anything in §2.

- **K-MOD-1** Module schemas contain no columns that duplicate kernel truth; they store kernel IDs and
  module-specific state only.
- **K-MOD-2** A module's read model is rebuildable from kernel state + events (disposable projection).

---

## 10. Non-Functional Requirements That Shape the Model

- **Offline-first eCOA/eSource**: offline captures are `Source=ePRO(device)` commands with client
  `valid_time` and idempotency keys; server assigns `recorded_time` on sync; conflicts become Assertions.
- **Hot paths** (edit-check evaluation, unit normalisation, bulk lab ingest, dataset generation) sit
  behind narrow ports (`IEditCheckEngine`, `IUcumNormalizer`, …) so a Rust implementation can replace them
  later without touching the model. No premature FFI.
- **Data volume**: Observations are the growth table → partition by `(tenant_id, study_id)`, BRIN on
  recorded time, and keep indexes on the current-truth predicate (`upper(recorded) IS NULL`).
- **Privacy**: direct identifiers only in the IdentityVault; kernel APIs return `ParticipantId`. Regional
  residency is a tenant property.

---

## 11. Seed Mapping of Existing `apps/execution` (preliminary — refined in the inventory)

| Existing | Disposition |
| --- | --- |
| `ClinicalSubject` (identity, status, strat factors) | **Split**: `Participant` + `Enrollment` (kernel); randomization fields → RTSM module; `EncryptedDemographics` → IdentityVault |
| `SubjectTransitionGraph`, lock rules, `Unblind`/`Withdraw` | **Keep logic → kernel Enrollment state machine**; unblinding gains Attestation |
| `ProtocolVersionRef`, `ActiveProtocolVersion` | **Replace** with Applicability (§4.4) |
| `SubjectConsent`, `EConsentCompletedConsumer` | **Migrate** → kernel Consent (Assertion + Artifact + Attestation) |
| `ClinicalObservation` | **Split**: core → kernel Observation; SDV/flags → Assertions; normalisation/range/outlier → derived Observations; `PageId`/`VisitId` → EDC projection |
| `ClinicalQuery` (+ state graph) | **Remain EDC workflow**; emits Assertions |
| `FormSubmission`, `SignatureManifest` | **EDC-specific**; signature → kernel Attestation |
| `SiteComplianceCache` | **Replace** by authority conditions (K-AUTH-5) |
| `AuditLogEntry`, `EfAuditLogWriter`, `AuditChainVerifier` | **Migrate** to kernel audit; extend chain scope to tenant |
| MassTransit outbox | **Keep** as kernel outbox implementation |
| `AstEditCheckEngine`, UCUM/lab-range/outlier services | **Keep** behind ports; first Rust candidates |
| `apps/org` entities | **Delete** in favour of kernel Organization/Person/Site/Grant |
| `Ctms*` entities (queries, delegation, grants) | **Delete duplicates**; keep genuinely operational ones (`MonitoringVisit`, `SiteMilestone`, `CraAllocation`) as CTMS module state |

---

## 12. What This Supersedes

- ADRs describing per-service ownership of study/site/subject data or module-local audit logs (to be
  annotated per `AGENTS.md` §12 once K001–K007 are accepted).
- The "microservices catalog" framing in `ARCHITECTURE.md` §2 (to be rewritten as kernel + modules).
- Neo4j as the protocol store: protocol content lives as immutable ProtocolVersion snapshots; graph
  analytics MAY be an optional projection.

## 13. Open Decisions (need an owner and an ADR each)

| # | Decision | Recommended default |
| --- | --- | --- |
| K001 | Modular monolith kernel with single schema | Yes (§9) |
| K002 | Bitemporal implementation: hand-rolled ranges + exclusion constraints vs. an extension | Hand-rolled `tstzrange`, no extension dependency |
| K003 | Tenant isolation: RLS in shared schema vs. schema-per-tenant vs. DB-per-tenant | RLS default; DB-per-tenant as an upgrade path for regulated customers |
| K004 | Policy engine: hand-coded evaluator vs. Cedar/OPA/OpenFGA | Hand-coded evaluator behind `IAuthorizer` for v1 (small, auditable), revisit if grants scale |
| K005 | Concept vocabulary: CDISC Biomedical Concepts vs. LOINC/SNOMED-first | BC as primary key with mapped external codes |
| K006 | Identity vault: in-kernel vs. separate service | Separate schema + key hierarchy, same deployment |
| K007 | Retention of Neo4j for designer | Drop for v1; revisit on a demonstrated graph-query need |

## 14. Acceptance Tests for This Spec

1. Reconstruct a participant's governing protocol version and expected schedule as of `(t, known_at)` for a
   fixture with an amendment effective at Site A but not Site B.
2. Correct an observation; verify both the current and the pre-correction views reproduce exactly.
3. Attempt an action outside an active delegation; verify denial, audit of the denial, and that a
   retroactive DOA does not validate it.
4. Have an AI agent propose an amendment impact plan; verify it cannot execute without a human
   attestation, and the resulting provenance chain links proposal → approval → activity → events.
5. Attempt cross-tenant read and cross-tenant FK insert; verify both fail at the DB layer.
6. Generate an SDTM VS dataset twice for different `known_at`; verify reproducibility and provenance.
7. Architecture tests K-XS-1/2 and K-MOD-1 fail on a deliberately introduced duplicate `Site` entity.
