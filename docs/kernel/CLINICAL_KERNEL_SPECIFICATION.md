# Cadence Clinical Research OS: Ontology and Kernel Specification v0.2

| | |
| --- | --- |
| **Status** | Proposed. Normative once accepted (then v1.0). |
| **Scope** | The concepts, invariants and boundaries that every Cadence capability builds on, across the whole lifecycle of developing evidence about an intervention |
| **Supersedes** | Kernel specification v0.1 (same file, 2026-09-29) and the per-service entity models inherited from the Python topology (§16) |
| **Companion work** | ADR-2244…ADR-2255 (§17), [execution migration inventory](EXECUTION_MIGRATION_INVENTORY.md) (its §3/§4 are resolved in Appendix A/B), epic #4719 |

Keywords **MUST**, **MUST NOT**, **SHOULD**, **MAY** follow RFC 2119. Every rule tagged `K-…` is an invariant
that becomes an automated test (architecture test, property test, database constraint or acceptance test).
Rule IDs from v0.1 keep their meaning; new areas add new prefixes.

This version fixes **concepts and invariants**. It deliberately does not specify every capability in detail.
Each capability gets its own module spec when its roadmap phase starts (§15).

---

## 0. North Star

Cadence is an **operating system for clinical research**: one platform that understands the entire
lifecycle of developing evidence about an intervention, not a bundle of EDC, CTMS, eTMF and a validator.

```
Development strategy → Study conception → Protocol design → Feasibility / start-up → Site activation
  → Recruitment / consent → Randomization → Conduct (clinical data · safety · operations)
  → Data management → Standardization → Analysis → Submission → Regulatory review → Post-market / RWE
```

The platform keeps **one graph** of:

- *why* the trial was designed (objectives, endpoints, estimands),
- *what was supposed to happen* (the executable protocol),
- *what actually happened* (events, observations, specimens, artifacts),
- *who was allowed to make it happen* (authority computed from evidence),
- *where each piece of information came from* (provenance),
- *how that evidence was transformed* into standard, analytical and submission representations (lineage),
- and *how it supported a regulatory conclusion*.

ICH E6(R3) (quality by design, risk proportionality, fit-for-purpose data sources) and its Annex 2
(decentralised, pragmatic and real-world-data-enabled trials) are design inputs, not later compliance work.

### 0.1 The founding invariant

> **K-TRUTH-1** No product owns clinical truth, and no representation is clinical truth.
> EDC, CTMS, eTMF, Safety, RTSM, eCOA do not own it. SDTM, ADaM, SEND, FHIR, ODM, Define-XML, Dataset-JSON and
> the protocol PDF are not it. They are **workflows over** or **representations of** the canonical model.

Consequences:

- A clinical fact is stored once, in the kernel, in terms of *meaning* (a Concept), not in terms of a
  standard's table layout (K-PROJ-1).
- Every representation (an eCRF field, an SDTM record, an ADaM value, a CSR table cell) is **derived** and
  carries lineage back to the facts and the versioned specification that produced it (§11.4).
- Re-generating any representation for the same inputs and the same `(valid_at, known_at)` produces the same
  bytes (K-LIN-3).

### 0.2 Non-goals of the kernel

The kernel does not render forms or documents, run randomization algorithms, compute statistics, file TMF
documents, pay sites or send notifications. Applications do these things *on top of* kernel concepts and
record their results back as kernel facts where the result is evidence.

---

## 1. Design Principles

1. **Temporal relational state + immutable events + transactional outbox.** Not event sourcing. Current and
   historical state are queryable from tables; events explain *why* state changed and drive integration.
   No clinical read path requires replaying a stream.
2. **Bitemporal where truth is contestable.** Valid time (when it was true in the trial) and recorded time
   (when Cadence learned it) for anything that can later be disputed or corrected.
3. **Corrections supersede; nothing is overwritten.** A correction closes the prior row's recorded interval
   and inserts a new row with a reason for change.
4. **Meaning first, representation second.** Facts are keyed by Concepts (§6.1). Standards are projections
   through versioned mapping specifications (§6.3).
5. **The protocol is executable.** The protocol is a machine-readable specification of the experiment; the
   PDF is one rendering of it (§6.2).
6. **Activity is not tied to a site.** An activity may happen at a site, at home, by telehealth, at a local
   healthcare provider, on a device or by the participant alone (§7.3).
7. **Authority is computed from evidence.** Not `User.Role`. Every command is evaluated against actor ×
   capability × scope × time × resource state, and capabilities follow from delegation, training,
   credentials and activation evidence (§9.3).
8. **Rules are versioned data.** Validation, edit-check, derivation and conformance rules are data with a
   version, an authority and a citation, not compiled application behaviour (§10.1).
9. **Lineage is a first-class output.** Every write names its inputs; every derived value can be traced
   forward and backward (§9.6).
10. **Quality is native.** Critical-to-quality factors, risks, controls, indicators and findings are part
    of how the trial executes, not a separate QMS (§10.4).
11. **AI proposes; accountable humans decide.** AI is an actor with a narrower verb set (§11).
12. **Modular monolith first.** One kernel, one schema, in-process APIs; applications may be extracted
    later, the kernel is not (ADR-2244).

---

## 2. Engines, Kernel and Applications

### 2.1 Six engines

| Engine | Answers | Kernel concepts (owned here) | Application capabilities built on it |
| --- | --- | --- | --- |
| **Knowledge** | What does it mean, and what is supposed to happen? | Concept Registry, terminology versions, Study, Protocol, ProtocolVersion, Amendment, Objective, Endpoint, Estimand, Population, Arm, Epoch, Cohort, Eligibility, Intervention, PlannedActivity, CollectionSpec, Standards Registry, MappingSpecification | Protocol authoring, SoA builder, MDR/standards governance, CRF design, statistical design, transparency registration |
| **Execution** | What is happening, where, and to whom? | Tenant, Organization, Site, StudySite, Vendor responsibility, Effectivity, Participant, Enrollment, Applicability, ClinicalEvent, ActivityLocation | EDC workflow, CTMS, feasibility, start-up, recruitment, scheduling, RTSM, supply, payments |
| **Evidence** | What was observed, collected or produced? | Observation, Assertion, Source, Artifact, Specimen, IngestionBatch | eCOA/ePRO, lab, imaging, devices/DHT, biospecimen management, eTMF/ISF, safety case intake |
| **Trust** | Who was allowed, who agreed, where did it come from? | Actor, Person, identity layers, Grant, Capability, Delegation, Qualification, ConsentStatus, Attestation, ProvenanceActivity, AuditEntry, lineage graph | Signatures, DOA logs, training management, eConsent, inspection readiness, privacy |
| **Quality** | Is it right, and is it under control? | Rule, RulePack, ValidationRun, ValidationFinding, CriticalToQualityFactor, Risk, Control, Indicator, Signal, Deviation, Issue/CAPA, Lock | Edit checks, P21-class conformance, RBQM/central monitoring, monitoring plans, SDV/SDR strategy, QMS, audits |
| **Intelligence** | What does it imply, and what should happen next? | AutomationAgent, Proposal | AI assistants, analytics, forecasting, statistical analysis, TLF generation, submission assembly |

Engines are an organising frame for concepts and teams. They are **not** deployment units; they share one
kernel schema and one transaction boundary.

### 2.2 Kernel-vs-application rule

> **K-SCOPE-1** A concept belongs in the kernel if it is needed to explain the **provenance, authority,
> meaning or lifecycle of clinical evidence across more than one application**. Otherwise it belongs to an
> application.

Worked examples:

| Concept | Placement | Why |
| --- | --- | --- |
| Monitoring visit | CTMS application (`OperationalActivity`) | Operational, not evidence about a participant; its *findings* become kernel Assertions/Findings. |
| Query (data clarification) workflow | EDC application | Workflow; each transition emits kernel Assertions targeting an Observation. |
| Validation finding | Kernel (Quality) | Referenced by DM, standards, submission QC and inspection; carries authority and resolution evidence. |
| Specimen | Kernel (Evidence) | Links participant, collection event, custody, consent scope and derived results across lab, biobank, safety and analysis. |
| Randomization algorithm | RTSM application | The *assignment* is kernel evidence (an Assertion with blinding class); the algorithm is not. |
| Site budget, invoice | Payments application | Needed by no evidence chain. Payment *triggers* read kernel events. |
| Estimand | Kernel (Knowledge) | Needed to explain why data was collected and how a result was derived (§6.2, §9.6). |

### 2.3 Forbidden redefinitions

Applications **MUST NOT** define their own version of a kernel concept. They reference kernel IDs.

| Kernel concept | Consequence for applications |
| --- | --- |
| Tenant, Organization, Person, Vendor | No `Organization`/`Personnel` tables in `org`, `ctms`, `econsent`, … |
| Study, Protocol, ProtocolVersion, Amendment, Effectivity, Estimand, Endpoint | No `StudyId` strings; `StudyId` is the kernel type. No application-local protocol copies. |
| Site, StudySite | Same for `SiteId`; CTMS site milestones hang off `StudySiteId`. |
| Participant, Enrollment, Applicability | EDC "subject", eCOA "user", RTSM "randomized subject" are views of `ParticipantId`. |
| ClinicalEvent, Visit | CTMS monitoring visits are `OperationalActivity`, never `Visit` (§7.4). |
| Observation, Assertion, Source, Artifact, Specimen | EDC fields, lab results, ePRO answers, imaging reads and assay results resolve to kernel Observations. |
| Concept, terminology version | No application-local code lists for clinical meaning; applications reference `ConceptRef`. |
| Actor, Role, Capability, Grant, Delegation, Qualification, Attestation | No application-local roles, DOA or training tables. eSignature is kernel Attestation. |
| Rule, ValidationFinding | No application-local rule engines with their own finding formats. |
| AuditEntry, DomainEvent, Outbox | No application-local audit tables (`CtmsAuditLog`, `OrgAuditLog`, …). |

- **K-XS-1** `Cadence.ArchitectureTests` fails if any assembly outside `Cadence.Kernel.*` declares a type named
  `Study|Site|Participant|Subject|Observation|Organization|Person|Role|Delegation|Specimen|Estimand` or
  `*AuditLog`, or a public `string` member named `*StudyId|*SiteId|*ParticipantId`. Existing violations live in
  a checked-in baseline that may only shrink (#4785).
- **K-XS-2** Applications reference `Cadence.Kernel.Abstractions` only, never `Cadence.Kernel.Persistence`.
- **K-MOD-1** Application schemas contain no columns that duplicate kernel truth; they store kernel IDs and
  application-specific state only.
- **K-MOD-2** An application's read model is rebuildable from kernel state + events (disposable projection).

### 2.4 Assemblies

```
Cadence.Kernel.Abstractions   ← IDs, value objects, commands/queries/events, ports, IClock (no EF, no ASP.NET)
Cadence.Kernel.Domain         ← aggregates, state machines, temporal logic (pure)
Cadence.Kernel.Application    ← command pipeline, authorization, policy and consent evaluation
Cadence.Kernel.Persistence    ← EF Core + Npgsql, RLS, bitemporal support, outbox, lineage store (internal)
Cadence.Kernel.Api            ← Minimal API surface for external clients + application APIs
Cadence.Rules.*               ← rule model, rule-pack loader, evaluation port (engine replaceable, §10.1)
Cadence.Standards.*           ← standards registry, mapping specifications, projection generators (§6.3)
Cadence.Modules.<App>         ← EDC, CTMS, eTMF, RTSM, Safety, eCOA, QMS, … workflows + projections + UI
```

Rendering (templating, PDF, notifications) lives outside the kernel in a shared, non-kernel package.

---

## 3. Identity, Tenancy and Identifiers

### 3.1 Hierarchy

```
Tenant
 └─ Organization (Sponsor | CRO | Site institution | Vendor | Regulator | IRB/EC | Lab | Depot | Courier | Payer)
      └─ owns → Study            (the sponsor organization owns the study definition)
            └─ StudySite         (Study × Site, with its own lifecycle)
 Site   = a location belonging to an Organization (institution)
 Person = human identity, independent of any tenant-scoped role (§9.1)
```

- A **Tenant** is the isolation and contractual boundary (a Cadence customer).
- A **Study is owned by exactly one Organization** (the sponsor) within one Tenant.
- **Site ≠ StudySite.** `Site` is a durable location record. `StudySite` is that site *in that study*
  (activation, country approvals, PI, effective protocol version).
- `OrganizationRelationship(from, to, kind, study_scope?, effective_period)` models collaboration
  (`SponsorOf`, `ContractedCro`, `AffiliatedSite`, `VendorTo`, …) and is bitemporal. Visibility across
  organizations needs a relationship **and** a grant (§9.3).

### 3.2 Identifiers

- Every entity ID is a distinct strongly-typed struct wrapping a **UUIDv7**. No shared `Guid`/`string` ID
  types cross a public API.
- Human-facing codes (`StudyCode`, `SiteCode`, `SubjectCode`) are **value objects** in
  `Cadence.Kernel.Abstractions`, unique within a stated scope, never keys:
  `StudyCode` per tenant, `SiteCode` per organization, `SubjectCode` **per study** (resolves inventory G2).
- **K-ID-5** Code → ID resolution is one kernel query per code type (`ResolveParticipant(studyId, code)`).
  Commands and events carry IDs only; APIs accept a code only on resolution endpoints.
  `SubjectCode` and its resolution query (`IEnrollmentDirectory.ResolveParticipantAsync`) are implemented (ADR-2453).
- IDs are opaque to clients and never reused.

### 3.4 Object references and zoom path (ADR-2481)

Every kernel object has a public reference, **NodeRef** `kind:id[@version]`
(`study_site:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e@3`), used in URLs, the command palette, trace links, event
envelopes (`subject`) and lineage edges. `kind` is a registered lower snake_case name bound to one identifier type
(`Cadence.Kernel.References.NodeKinds`); `id` is the lower-case UUID; `version` is the object's version index.
Without a version the reference means "as of the read's `(valid_at, known_at)`".

- **K-ID-6** Each object version has exactly one NodeRef string; parsing accepts only the canonical form, and every
  kernel identifier type has a registered kind.
- **K-ID-7** The presentation zoom path is computed by the kernel from a NodeRef:
  `tenant → study → [country] → study_site → enrollment → clinical event → observation`, and
  `tenant → organization → site`. A participant appears in a study as its enrollment (ADR-2453), titled by its
  subject code; an enrollment without a responsible study site sits directly under its study.
  Protocol versions and study standards profiles sit under their study, and an attestation under the object it
  signs (ADR-2485). The zoom path is presentation only and never implies authority (§9.3).
- **K-ID-8** Search (ADR-2484) finds kernel objects by code and title within one tenant and returns NodeRefs. It
  queries the source tables themselves with PostgreSQL full-text search, never a copy, so Row-Level Security,
  deletion and crypto-shredding apply to it exactly as to the source. Searchable tables are an explicit
  allow-list; identity content is never a source unless the actor holds its `identity.reveal.<layer>` capability
  (K-IDN-1).

### 3.3 Tenancy invariants (ADR-2250)

- **K-ID-1** Every kernel table has `tenant_id NOT NULL`, every FK is composite `(tenant_id, id)`, so
  cross-tenant references are impossible at the schema level.
- **K-ID-2** PostgreSQL Row-Level Security is enabled **and forced** on every kernel table; the application
  role cannot bypass it. The tenant is set per transaction from the authenticated actor in exactly one place.
- **K-ID-3** Cross-tenant exchange occurs only through an explicit, audited `DataSharingAgreement`.
- **K-ID-4** Person identity is tenant-independent; roles and access are tenant-scoped.
- A regulated customer MAY be placed in a dedicated database with the identical schema (ADR-2250 upgrade path).
- *Implemented (#4780):* composite keys and forced Row-Level Security on every kernel table; the tenant is set on
  each kernel connection by `KernelTenantConnectionInterceptor`; persons are visible through `tenant_memberships`;
  system jobs list tenants through `kernel.tenant_ids()`. See ADR-2250 §6 and `packages/kernel/README.md`.

---

## 4. Time

### 4.1 Two clocks

| Clock | Meaning | Column |
| --- | --- | --- |
| **Valid time** | When it was true in the trial world | `valid_period tstzrange` |
| **Recorded time** | When Cadence asserted / superseded it | `recorded_period tstzrange` |

Canonical query: `as_of(valid_at, known_at)`. Operational reads default to `(now, now)`; regulatory
reconstruction uses `(t_clinical, t_inspection_cutoff)`.

| Bitemporal | Append-only | Current-state + audit |
| --- | --- | --- |
| Effectivity, Enrollment, Applicability, ConsentStatus, Grant, Delegation, Qualification, OrganizationRelationship, StudySite lifecycle, Observation, Assertion, Specimen custody state | DomainEvent, AuditEntry, Attestation, ProvenanceActivity, LineageEdge, ArtifactVersion, ValidationRun | Reference/config data without dispute semantics |

- **K-TIME-1** Bitemporal rows are never `UPDATE`d except to close `recorded_period`. They are never deleted or truncated.
  Enforced by a trigger (SQLSTATE `KT001`).
- **K-TIME-2** An exclusion constraint (`EXCLUDE USING gist`) prevents overlapping `(tenant, entity, valid,
  recorded)` rectangles where a single truth is required (ADR-2246). The constraint is deferred to commit, so a correction
  can close the old row and insert its replacement in one transaction. How to apply both guards to a table:
  [`packages/kernel/README.md`](../../packages/kernel/README.md#bitemporal-tables-adr-2246).
- **K-TIME-3** All timestamps are `timestamptz`; the kernel takes time only from `IClock`; `DateTime.Now`
  and `DateTime.UtcNow` are banned in kernel and domain code (#4784).
- **K-TIME-9** Client-supplied valid time (offline capture, device time) is preserved as captured; the server
  assigns recorded time. The two are never collapsed.

---

## 5. Commands, Events, Audit and Transactions

### 5.1 Command pipeline

```
Command → Authenticate → Resolve Actor → Authorize (§9.3) → Check consent scope (§9.4, if data use)
        → Load aggregate → Domain decision (Result<T>)
        → [state + ProvenanceActivity + LineageEdges + DomainEvent(s) + AuditEntry + Outbox row]  -- ONE tx
        → commit → Outbox relay → subscribers (projections, applications, integrations, validation)
```

- **K-TX-1** One command changes **one aggregate**. Multi-aggregate processes are sagas driven by events.
- **K-TX-2** Events, audit, provenance, lineage and outbox rows are written in the same transaction as the
  state change, or the change does not happen.
- **K-TX-3** Commands carry an idempotency key and a reason for change (mutations). Replaying a key with the
  same payload returns the original result; the same key with a different payload is a conflict. For
  consumed integration events the key is `hash(source_system, message_type, natural_key)` (resolves G15).
- **K-TX-4** Advisory locks are per aggregate, not per table. A command that must respect another
  aggregate's state (for example demographics locking after randomization) takes that aggregate's lock and
  reads its state at command time; it does not update it (resolves G12).
- **K-TX-5** No kernel write without a resolved Actor. System work runs as a named `ServiceAccount`;
  anonymous or `"system"` fallbacks are rejected (resolves G14).

Implemented by `KernelCommandPipeline` and `KernelCommandTransaction` (ADR-2449): one PostgreSQL transaction per
command; `KernelDbContext` refuses `SaveChanges` outside it. K-TX-4 locks are `IKernelUnitOfWork.LockAsync<TAggregate>(id)`:
a PostgreSQL advisory lock on the command's connection, held until the transaction ends, at most one per command
(ADR-2453). How to add a command:
[`packages/kernel/README.md`](../../packages/kernel/README.md#command-pipeline-adr-2449).

### 5.2 Events

`Envelope{ event_id, type, schema_version, tenant, occurred_valid, recorded, actor, activity_id,
correlation_id, causation_id, payload, subject }`, where `subject` is the NodeRef of the aggregate the command
changed, pinned to its new version (§3.4; null for events about no aggregate, such as `DecisionDenied`).

| Family | Examples |
| --- | --- |
| Knowledge | `ProtocolVersionDrafted` (carries the amendment's classification), `…Approved`, `…Withdrawn`, `ConceptMapped`, `MappingSpecificationPublished` (`LibraryItemApproved` with kind `MappingSpecification`, ADR-2463), `RulePackReleased`, `StandardVersionRegistered`, `StandardVersionApproved`, `StandardVersionDeprecated`, `LibraryItemDrafted`, `LibraryItemApproved`, `LibraryItemDeprecated`, `StudyStandardsDeclared` |
| Execution | `OrganizationRegistered`, `SiteLinkedToStudy`, `EffectivityDeclared`, `EffectivityStatusChanged`, `StudySiteActivated`, `ParticipantRegistered`, `ParticipantScreened`, `ParticipantEnrolled`, `EnrollmentStatusChanged`, `ApplicabilityChanged`, `ParticipantWithdrawn` |
| Evidence | `EventRecorded`, `ObservationRecorded`, `ObservationCorrected`, `AssertionMade`, `AssertionRetracted`, `SpecimenCollected`, `SpecimenTransferred`, `ArtifactRegistered`, `ArtifactVersioned`, `IngestionBatchAccepted` |
| Trust | `ConsentGiven`, `ConsentScopeChanged`, `ConsentWithdrawn`, `RoleDefined`, `GrantIssued`, `GrantRevoked`, `DelegationSigned`, `QualificationRecorded`, `DecisionDenied`, `Attested` |
| Quality | `ValidationRunCompleted` (with counts of detected, new and resolved findings), `FindingInvestigationStarted`, `FindingClassified`, `FindingAccepted`, `FindingExplained`, `FindingReopened`, `IndicatorBreached`, `SignalRaised`, `DeviationRecorded`, `LockApplied`, `LockReleased` |
| Intelligence | `ProposalCreated`, `ProposalApproved`, `ProposalExecuted`, `ProposalRejected` |

- **K-EVT-1** Event schemas are additive-only within a major version; consumers tolerate unknown fields.
- **K-EVT-2** Events are the integration contract; they are not replayed to rebuild clinical state.
- **K-EVT-3** People read events through the activity stream (ADR-2485), a projection separate from the
  integration contract. Each entry carries its family, actor, time and subject NodeRef, and its scope: the
  subject's zoom path when the event happened. It carries no payload. Every read is masked per reader through the
  D1 visibility contract, so an entry about an object the reader may not see (blinded, not permitted, identity) is
  returned in place with the reason and without the reference, never dropped. Projecting never changes the outbox
  or integration delivery.

### 5.3 Audit guarantees

1. **Completeness**: every kernel state change has a ProvenanceActivity and at least one AuditEntry.
2. **Immutability**: audit, event, attestation, provenance and lineage tables are insert-only (no
   `UPDATE`/`DELETE` grants; a trigger rejects them).
3. **Tamper evidence**: **one hash chain per tenant**, replacing today's two chains per entity type (inventory
   §3 item 7), with periodic root sealing. `AuditChainVerifier` is adapted as a scheduled job and inspector tool.
4. **Reason for change** on every mutating command, stored on the audit entry and any superseding row.
5. **Reconstructability**: for any `(valid_at, known_at)` the kernel re-derives state from tables;
   time-travel tests assert this per bitemporal aggregate.

Items 1-3 are implemented for the kernel by ADR-2449: the `enforce_insert_only` trigger (SQLSTATE `KA001`) guards
`audit_entries`, `domain_events`, `provenance_activities` and `idempotency_records`; `audit_chain_heads` serialises
each tenant's chain; `KernelAuditChainVerifier` re-derives it, and an hourly job seals each intact chain in the
insert-only `audit_chain_seals` table. `KernelOutboxRelay` delivers events at least once to the host's `IKernelEventPublisher`.

### 5.4 Error-code registry (resolves G13)

- **K-ERR-1** Every domain error has a stable code `K.<area>.<name>` (kernel) or `M.<app>.<name>`
  (application), registered once in a checked-in registry with its HTTP status. Codes are never reused
  or renamed. Defaults: authorization denial 403, not found 404, idempotency/concurrency conflict 409,
  rule violation 422, lock 423.

### 5.5 Explanations (ADR-2482)

Why an action is permitted or denied, why an object is in its state, why a finding was raised and why a metric
has its value are answered by one kernel type, `Explanation{ subject, claim, producer, steps[claim, evidence[]],
conditions[code, description, met, evidence[]], next_actions[] }` (`Cadence.Kernel.Explanations`). Unmet
conditions are the conditions whose `met` is false.

- **K-EXP-1** The component that owns a rule (authorization §9.3, Applicability §7.5, Findings K-FIND-3,
  Indicators §10.4) produces its explanation from the same evaluation that made the decision. Applications and
  the UI render explanations and never compose them.
- **K-EXP-2** Evidence is a NodeRef (§3.4), pinned to the version the producer read where it has one. Evidence
  the reader may not see is masked, never dropped.
- **K-EXP-3** An explanation is a pure function of the evaluated evidence and the producer's rule version; the
  producer names that version.

Authorization implements this today: a denied command returns `K.auth.denied` with the explanation, and the
`DecisionDenied` event and audit entry record every evaluated condition with its evidence. Findings produce theirs
as `quality.finding/<rule-id>@<version>` (ADR-2462). Indicators produce theirs as `quality.indicator/<id>@<version>` (ADR-2488). Applicability's producer follows as that
slice lands (#4896).

---

## 6. Knowledge Engine

### 6.1 Concept Registry (ADR-2252)

The semantic layer every other model points at.

```
Concept(id, tenant?, kind, preferred_label, definition, status, version)
ConceptRef(concept_id, version)                         -- what facts and specs point at
CodeSystem(id, uri, publisher, license_class)           -- LOINC, SNOMED CT, UCUM, RxNorm, ICD-10/11,
                                                          -- NCIt / CDISC CT, MedDRA, WHODrug, sponsor dictionaries
CodeSystemVersion(code_system, version, released, package_artifact)
ConceptMapping(concept, code_system_version, code, relation: exact|broader|narrower|related, status, evidence)
```

- The primary key of clinical meaning is a **CDISC Biomedical Concept** where one exists, with mapped
  external codes (LOINC for labs, SNOMED CT for findings/procedures, UCUM for units, RxNorm/WHODrug for
  medications). Where none exists, a sponsor concept in the tenant namespace MAY be created, and it carries
  a mapping status.
- **Coding dictionaries used for classification** (MedDRA, WHODrug) are code systems too, but coding a verbatim
  term is an **Assertion** (`CodedAs`) about an Observation, not a change to the Observation.
- Terminology packages are versioned Artifacts. Nothing references "the latest" package: every
  specification, rule and projection names the package version it used.
- **K-SEM-1** Every Observation, CollectionSpec item, PlannedActivity assessment and standards variable
  mapping references a `ConceptRef` (concept + version).
- **K-SEM-2** Licensed code-system content (MedDRA, WHODrug, SNOMED CT) is loaded per tenant under the
  tenant's licence; the platform ships the structure, not the licensed content.

Implemented by ADR-2470 (#4807): every tenant loads the terminology releases it adopts, public ones included, through
`ImportCodeSystemVersion` (or `TerminologyPackageLoader`, which checks the publisher's SHA-256 first). A
code-system version and its entries, and each concept version, are insert-only; reads go through `IConceptRegistry`,
which has no "latest" lookup. How to load a release: [`packages/kernel/README.md`](../../packages/kernel/README.md#concept-registry-adr-2470).

### 6.2 Executable protocol

The protocol is a **machine-readable specification of the experiment**. The PDF, the CRF set, the SoA grid,
the RTSM configuration and the registry entry are renderings or derivations of it. CDISC USDM is the
interchange format for it, not its internal shape.

```
Study 1─* Protocol 1─* ProtocolVersion 1─* Amendment
ProtocolVersion contains:
  Objective ─< Endpoint ─< Estimand(population, variable, intercurrent events + strategies, summary measure)
  Population, EligibilityCriterion(concept-based, evaluable)
  Arm, Epoch, Cohort, Intervention(product, dose, route, regimen)
  PlannedActivity(encounter/timing, location modes, assessments) ─< CollectionSpec(item → ConceptRef, unit, CT)
  SafetyRule, DiscontinuationRule, RandomizationScheme (reference), StatisticalDesign (reference)
```

- **ProtocolVersion** is an immutable, content-addressed snapshot (protocol design document + `content_hash`) with
  `number`, `parent_version`, `amendment?`, `state` and the approving Attestation (ADR-2452). The design document
  (ADR-2472) is the typed shape above, read strictly; USDM converts in and out of it (import: ADR-2473).
- State machine `Draft → InReview → Approved → Superseded | Withdrawn`.
  - Submitting for review freezes the content and sets `content_hash`, the canonical-JSON SHA-256
    (`ContentHash`). Returning to draft clears it. Reviewers and approvers therefore sign the same hash.
  - `Approved` requires an Attestation with meaning `Approved` bound to `content_hash`.
  - Forking a new draft **does not** change the parent's state (readiness blocker, inventory §4). The
    draft copies the parent's content and takes the next number in the study.
  - `Superseded` is set only when a later version is effective everywhere it will ever be. Approved
    versions legitimately coexist.
  - `Withdrawn` (from `InReview` or `Approved`) is terminal and requires a reason and an Attestation with
    meaning `Withdrawn`.
  - A database trigger repeats the state machine and the content freeze for every database user (`KA002`).
- **Amendment**: intent, rationale, classification (substantial / non-substantial / urgent safety measure),
  summary of changes, **impacted elements** and a TransitionPolicy (§7.5). It is a value carried by the
  forked version it introduces and freezes with that version (ADR-2452); the first version has none.
- **K-PROT-1** Every ProtocolVersion element has a stable element ID across versions, so a structural
  diff between versions is computable. An element is a JSON object with a non-empty string `id` (the USDM
  convention), and ids are unique within a version (ADR-2480 §4.6).
  *Implemented (#4808, ADR-2472):* content is a protocol design document whose element ids are unique and whose
  references between elements are checked by kind when it is written, so `ProtocolStructuralDiff` applies to it as is.
- **K-PROT-2** **Amendment impact is computable**: given two ProtocolVersions, the kernel lists every
  downstream object that references a changed element (CollectionSpecs, mapping specifications, rules,
  RTSM configuration, monitoring plan, consent forms, TMF expected documents, open Proposals). An amendment
  cannot be approved while that impact report is not attested as reviewed.
  *Implemented in part (#4841, #4808, ADR-2471 §4.5, ADR-2474):* approving a version writes its design elements as
  lineage nodes. `IAmendmentImpact` lists the dependents of the amended parent version (§9.6 `Dependents`), excluding
  the fork itself, and follows only the elements the amendment changes or removes. The report carries the elements
  actually changed next to the amendment's declared impacted elements. The attestation gate on approval is still to
  come.
- **K-PROT-3** The scientific chain `Objective → Endpoint → Estimand → required data → PlannedActivity →
  CollectionSpec` is explicit; a CollectionSpec item with no path to an Endpoint, Estimand, safety
  requirement or operational need is flagged at design time (a Quality finding, not a blocker).
  *Implemented (#4808, ADR-2472 §4.4):* `ProtocolDesignReview` reports objectives without endpoints, primary
  endpoints without estimands, unscheduled CollectionSpecs and items without a purpose. They become Quality findings
  with #4812. Submitting a version for review also checks that every ConceptRef, pinned code-system version and
  Artifact the design names exists in the tenant (K-SEM-1).
- Statistical design (sample size, multiplicity, interim analyses, stopping boundaries, missing-data
  strategy, SAP) is **referenced** from ProtocolVersion as versioned Artifacts in v0.2; its detailed model
  belongs to the Statistics roadmap phase.

### 6.3 Standards subsystem (ADR-2254, ADR-2255)

Standards (SDTM, SEND, ADaM, Define-XML, CDASH, Dataset-JSON, ODM, FHIR) are **representations**.
The subsystem makes them native, continuous outputs of the platform rather than an export-map-validate loop at
database lock.

| Component | Concept(s) | Placement |
| --- | --- | --- |
| Standards Registry | `Standard`, `StandardVersion`, `DatasetDefinition`, `VariableDefinition`, `ValueLevelDefinition` | Kernel (Knowledge), reference data |
| Controlled Terminology | CDISC CT packages as `CodeSystemVersion` (§6.1) | Kernel |
| Metadata Registry (MDR) | Reusable, governed library of CollectionSpecs, mappings and rules with versions and status | Kernel concepts, Standards application for authoring/governance |
| Mapping Engine | `MappingSpecification(version, standard_version, CT versions, rules[])`: Concept/CollectionSpec → variable | Kernel concept; engine behind a port |
| Transformation Engine | `Transformation` nodes forming a DAG from Observations to datasets | Engine behind a port (§10.1 Rust seam) |
| Derivation Engine | `Derivation(algorithm, version, inputs → outputs)` for ADaM and derived observations | Engine behind a port |
| Validation Engine | Rule evaluation over designs, observations and datasets (§10) | Quality engine |
| Submission Generator | `SubmissionPackage` assembling datasets, Define-XML, reviewer guides (SDRG/ADRG), XPT/Dataset-JSON | Application |
| Standards Governance | Versioning, approval (Attestation), deprecation of standards content | Application over kernel workflow |

- **K-STD-1** The mapping from a CollectionSpec item to its standard variables exists **before first data
  capture**: a CollectionSpec cannot be published for a study whose declared standards have no mapping for it,
  unless the gap is recorded as an accepted Finding.
- **K-STD-2** **Design-time conformance**: publishing a CollectionSpec runs the applicable standards rules
  (variable, CT, unit/UCUM, value-level) and blocks on `Error` findings: "this field cannot produce a conformant
  downstream representation."
- **K-PROJ-1** No kernel type references SDTM, ADaM or SEND variable names. Those names exist only in
  Standards Registry data and mapping specifications.
- **K-PROJ-2** A projection is `f(kernel state as_of(valid, known), MappingSpecification version,
  CT versions) → dataset Artifact`. The dataset Artifact's provenance names all of those inputs.
- **K-PROJ-3** Standards datasets are generated **continuously** (on events, per study) and validated in
  stream; database lock freezes inputs, it is not when SDTM is first produced.

**Implemented (#4809, ADR-2460):** the Standards Registry and MDR. `StandardVersion` owns its datasets, variables,
value-level definitions and terminology bindings, identified by `(tenant, standard code, version)` and a SHA-256
content hash over the canonical package. `LibraryItem` versions collection specifications, mapping
specifications, rule packs and codelist subsets under one governance envelope (Draft → Approved → Deprecated;
approval needs an `Approved` Attestation, deprecation a `Responsibility` one, both checked through
`IAttestationVerifier`). `StudyStandardsProfile` pins a study's approved standard versions, CT versions and library
items; deprecation never changes a pin. Registered definitions are insert-only (`KA001`) and the identity and content
columns of versions and items are immutable (`KS001`). `Cadence.Standards` loads CDISC Library product content with
checksum verification and ships SDTMIG v3.4 (DM, AE, VS, LB) and ADaMIG v1.3 (ADSL) seeds. CT bindings keep the
natural key `(code system, version)`, for example `sdtmct 2024-09-27`, and resolve to the tenant's Concept Registry
`CodeSystemVersion` (#4888, ADR-2460 §4.2): each CDISC CT package type is its own code system
(`KnownCodeSystems.CdiscCt`, ADR-2470) and any other code system is named by its URI. A standard version stores the
resolved version when the package is already imported; a study declaration must resolve every CT version it declares. An architecture test
enforces K-PROJ-1 against the shipped seeds.

**Implemented (#4789, ADR-2503):** reusable form blocks. `CollectionSpecification` library item content is checked
when it is drafted. Its items name CDISC Biomedical Concepts by C-code, and instantiating it in a tenant resolves them
to a protocol design `CollectionSpec`. `Cadence.Standards` ships a checksum-verified vital signs block.

**Implemented (#4810, [ADR-2463](../adr/2026-09-30-mapping-specifications-as-governed-concept-to-variable-maps.md), K028):** mapping specifications and K-STD-1. A
`MappingSpecification` is the content of a `MappingSpecification` library item, pinned to exactly one standard
version, its CT versions and the Concept versions it maps; drafting checks every dataset and variable against the
standard's registered package and links each reference as a `DefinedBy` input. Each mapping maps one Concept to a
dataset: constant variables (value-level metadata such as `VSTESTCD`), the original result and unit, the result
converted into a standard unit (`--STRESC/--STRESN/--STRESU`), and CT decodes. A study-level override is a versioned
specification naming the base it overrides. `IMappingEngine` applies a specification to a collected value; the .NET
engine is in `Cadence.Standards`. K-STD-1 is enforced when a protocol version is submitted for review: every Concept
its CollectionSpec items record must be mapped in the study's pinned specifications, or the gap must be an accepted
Design finding (`MappingGap`); the protocol version records the mapping versions it relies on as `MappedBy` inputs.
The format is described in [MAPPING_SPECIFICATIONS.md](MAPPING_SPECIFICATIONS.md).

---

## 7. Execution Engine

### 7.1 StudySite lifecycle and Effectivity

Approval ≠ effectiveness. A ProtocolVersion becomes usable at a site only after jurisdictional approvals.

```
Effectivity(protocol_version_id, scope: Study | Country | StudySite,
            status: Pending | RegulatoryApproved | EthicsApproved | Effective | Withdrawn,
            valid_period, recorded_period, evidence: ArtifactId[])
```

Scopes resolve most-specific-first: `StudySite → Country → Study`.

- **K-TIME-4** A `StudySite` can be `Activated` only when at least one ProtocolVersion is Effective there
  (this replaces the site-compliance cache gate in `apps/execution`; inventory §3 item 6).
- **K-TIME-5** Effectivity reaches `Effective` only with ≥1 evidence Artifact of the required class
  (IRB/EC approval, CTA acknowledgement), so eTMF evidence directly gates activation.
- Start-up dependencies (contracts executed, essential documents present, site initiation completed) are
  expressed as the same kind of evidence conditions on activation; their workflows live in CTMS/start-up.
- **Implemented** (ADR-2456, #4777): `Effectivity` is an aggregate per protocol version and scope, with
  bitemporal `EffectivityStatusPeriod` rows that each name the `ArtifactVersionId`s evidencing them (a version, not
  an artifact, so the evidence cannot change after the fact). `DeclareEffectivity` starts it `Pending`;
  `ChangeEffectivityStatus` moves it, accepting an ethics approval for `EthicsApproved`, a regulatory approval or CTA
  acknowledgement for `RegulatoryApproved`, and any of them for `Effective` (K-TIME-5), which also needs an approved
  version. `ActivateStudySite` refuses a site where no version is in force (K-TIME-4). `IEffectivityReader` resolves
  the versions in force at a study site as of any valid and known time. The other start-up conditions are not yet
  checked.

### 7.2 Vendors and responsibilities

`VendorResponsibility(study, organization, responsibility kind, scope, contract_artifact, effective_period)`
(central lab, imaging core, eCOA vendor, IRT, courier, depot, specialty lab, CRO functions). Data
transfers from a vendor are `IngestionBatch`es (§8.5) attributed to the responsibility. SLAs, deliverables,
issues and performance are application state (vendor oversight) keyed by the responsibility ID.

### 7.3 Where activity happens

- **K-LOC-1** No kernel concept assumes an activity occurs at an investigative site. `ActivityLocation` is
  one of `StudySite | ParticipantHome | Telehealth | LocalHealthcareProvider(organization) | Device |
  ParticipantSelf | Laboratory(organization)`. PlannedActivities declare the modes they permit; ClinicalEvents
  record the mode used.
- Every activity is still **attributed** to a responsible StudySite and investigator for oversight, even when
  performed elsewhere (ICH E6(R3) Annex 2).

### 7.4 Participant, Enrollment, Events

- `Participant` is a pseudonymous kernel identity (§9.2).
- `Enrollment(participant, study_site, status, enrollment_number, valid_period, recorded_period)` is
  bitemporal. The state machine (resolves inventory §3 item 1):

  ```
  Screening → ScreenFailed
  Screening → Enrolled → Randomized → Active → Completed
  (Screening | Enrolled | Randomized | Active) → Withdrawn
  ```
  `Enrolled → Active` is allowed for non-randomized studies. `ReconsentRequired` is not a state: it is a
  pending Applicability transition (§7.5). `Unblinded` is not a state: unblinding is an
  Attestation-bearing event plus an RTSM Assertion, and blinding is an authorization condition (K-AUTH-4).
- Withdrawal is a dedicated command with reason, withdrawal type and an Attestation; a generic state PATCH
  cannot reach it (inventory §3 item 2).
- **Enrollment numbering** (resolves G1): numbers are allocated gap-free per `(tenant, study)` by default, or
  per StudySite when the Study declares it, inside the enrolling command under the Study's advisory lock.
- **Eligibility** (resolves G3): EligibilityCriteria are evaluated over Observations; the result is an
  `Eligibility` Assertion per criterion, and `Screening → Enrolled | ScreenFailed` requires a complete set.
- **Arm assignment** (resolves G7): every arm assignment, randomized or not, is an RTSM Assertion with a
  blinding class (`Open | Blinded`); the kernel Participant holds no arm.
- **Implemented** (ADR-2453, #4776): `Participant` is identity only; `Enrollment` is an aggregate per participant
  and study site whose status history is bitemporal rows in `enrollment_status_periods`. Subject codes are unique
  per study, numbers are gap-free per study under the Study lock, withdrawal names a `Withdrawn` Attestation over
  the enrollment's content hash, and a database trigger and exclusion constraint repeat the lifecycle. Eligibility
  gating (#4727), StudySite activation gating (#4777) and per-StudySite numbering are not yet enforced.

| Concept | Meaning | Owner |
| --- | --- | --- |
| `PlannedActivity` | From the ProtocolVersion. Immutable per version. | Kernel |
| `ClinicalEvent` | Something that happened (or was attempted) with a participant: visit, procedure, dose, AE onset, discontinuation, specimen collection. `kind`, `valid_time`, `location`, `participant`, `study_site`, optional `planned_activity_id`. | Kernel |
| `Visit` | A `ClinicalEvent` of kind `Visit`, mapped to a planned encounter. | Kernel |
| `OperationalActivity` | Monitoring visit, site initiation, audit. About the site/study, not a participant. | CTMS |

Deviations from plan (missed, out-of-window, unscheduled) are **derived Assertions**, not fields on visits.

### 7.5 Applicability

`Applicability(participant, protocol_version, valid_period, recorded_period, basis, transition_id?)`
answers "which protocol version governs this participant, when?"

Initial assignment at `t_enroll`:

1. `candidates` = ProtocolVersions Effective at the participant's StudySite at `t_enroll`.
2. Keep versions the participant has a valid ConsentStatus for (§9.4). No consent → no applicability.
3. Choose the highest-numbered candidate; ties fail closed.

When a new version becomes Effective at the StudySite, the Amendment's TransitionPolicy decides:

| Policy | Behaviour |
| --- | --- |
| `NewParticipantsOnly` | No change. |
| `RequiresReconsent` | Pending Transition; Applicability moves only on valid re-consent. |
| `Automatic` | Moves at the version's effective date (non-substantive changes). |
| `ByCohort(predicate)` | Policy applies to participants matching a kernel predicate. |

- A newer consent resolves **every** pending Transition whose target version it covers (resolves G6).
- **K-TIME-6** Exactly one governing ProtocolVersion per participant at any valid instant, or a flagged
  inconsistency that blocks data capture.
- **K-TIME-7** The expected schedule is a pure function of `(ProtocolVersion, anchor events, as_of)`; it is
  never stored as truth, only cached as a projection.
  - Implemented as `ExpectedSchedule` over the protocol design (ADR-2487). Timed planned activities are placed on
    the study-day axis with the SDTM `--DY` convention: day 1 is the reference start, day -1 the day before, and
    there is no day 0 (a day-0 timing is day 1). Activities without a timing are event-driven and not scheduled.
  - The first anchor is the reference start, passed by the caller as a site-calendar date; each visit gets a
    target date, a window and its status (`NotYetOpen`, `Open`, `Closed`) at the as-of date.
  - `Assess` measures an actual visit date against its expected visit: days from target, `Early`, `InWindow` or
    `Late`, and days outside the window (ADR-2507, K032). It carries no severity; a derived out-of-window deviation
    takes its importance from the study's Quality rules. Visits anchored on another visit and repeating cycles
    follow under the same ADR.
- **K-TIME-8** An observation's governing version is resolved from Applicability at its valid time and
  stamped into its provenance. `ActiveProtocolVersion`, `ProtocolVersionTag/Index` and
  `FormSubmission.ProtocolVersion` are replaced by this (inventory §3 item 3).

---

## 8. Evidence Engine

### 8.1 Observation

A **recorded clinical fact**: *this concept had this value for this participant over this valid period, as
asserted by this source.*

```csharp
public sealed record Observation(
    ObservationId Id,
    ParticipantId Participant,
    ClinicalEventId? Event,
    SpecimenId? Specimen,
    ConceptRef Concept,          // §6.1, never an SDTM variable
    ObservationValue Value,      // Quantity(value, ucum) | Coded(ConceptRef) | Text | Boolean | Instant | Missing(reason)
    ValidPeriod Valid,
    RecordedPeriod Recorded,
    SourceRef Source,
    ObservationId? Supersedes);
```

- **K-FACT-1** Values are immutable. A correction is a new Observation that supersedes the prior one
  (recorded period closed, `Supersedes` link, reason required). Status such as Verified or Queried is an
  Assertion, not a mutable column.
- **K-FACT-2** Units are UCUM. Normalisation, reference-range flags and toxicity grades are **derived
  Observations** with provenance naming inputs and the versioned algorithm and range set.
- **K-FACT-3** `Missing` is a first-class value with a reason.
- **K-FACT-4** No EDC concepts on Observations: no `PageId`, `VisitId` string, `IsSdvVerified`.
- **K-FACT-5** No free-form extension attributes on Observations (resolves G8). Qualifiers that carry
  meaning are Concepts (e.g. position, laterality, fasting status as qualifier ConceptRefs); workflow or
  display metadata belongs to the application.

### 8.2 Assertion

A statement about a fact or another assertion by an actor with authority: `Verified(SDV)`, `Reviewed(SDR)`,
`Queried`, `QueryAnswered`, `Adjudicated`, `CodedAs(MedDRA/WHODrug)`, `Eligibility`, `ProtocolDeviation`,
`CausalityAssessment`, `Expectedness`, `ArmAssigned`, `Frozen`, `Locked`. Assertions are append-only and
bitemporal; a later assertion may retract an earlier one. Queries target an `ObservationId`, never
coordinates (inventory §3 item 10).

### 8.3 Source and Artifact

- `Source`: `ManualEntry(actor, collection_spec) | Device(device_id, firmware) | LabFeed(lab_org, batch,
  range_set_version) | ePRO(participant, device) | EHR(system, record_ref) | SourceDocument(artifact) |
  Derived(inputs, algorithm) | Import(batch)`. Local lab ranges are versioned range-set Artifacts owned by
  the lab organization (resolves G9).
- `Artifact` is versioned, hash-addressed content (signed ICF, IRB letter, lab report, DICOM series,
  generated dataset, protocol rendering). Classification (e.g. TMF artifact type) is an Assertion, so eTMF
  filing can be re-projected without touching the artifact.
- **Implemented** (ADR-2454, #4729): `Artifact(kind, study?, title)` with insert-only, numbered `ArtifactVersion`s
  that record the content hash, size, media type and storage reference of bytes kept in object storage. Attestations
  can target an `artifact_version`. `Source`, range sets and the TMF classification Assertion are not yet
  implemented.

### 8.4 Specimen

Specimens are evidence with a lifecycle, not lab rows.

```
Specimen(id, participant, kind, parent_specimen?, collection_event, container, volume/quantity,
         consent_scope_snapshot, state: Collected | Processed | Aliquoted | InTransit | Stored |
         Consumed | Destroyed | Lost)
CustodyEvent(specimen, from_custodian, to_custodian, location, condition (e.g. temperature excursion), at)
Assay(specimen, method, lab_org, version) → Observation(s) with Source=LabFeed and Specimen link
```

- **K-SPEC-1** Aliquots and derivatives reference their parent; the derivation tree is complete.
- **K-SPEC-2** Chain of custody has no gaps: every state change is a CustodyEvent by an identified custodian.
- **K-SPEC-3** Using a specimen for an analysis, purpose, organization or jurisdiction is a consent-scoped
  authorization (§9.4).

### 8.5 Ingestion layer

External data (EHR, central/local labs, imaging, wearables and devices, eCOA, IRT, genomics, PK labs,
safety vendors, claims, registries) enters through one pipeline:

```
receive (IngestionBatch, raw Artifact) → identity resolution (§9.2) → structural validation
  → semantic normalisation (Concepts, UCUM) → provenance → kernel commands (idempotent, §5.1)
```

- **K-ING-1** Ingestion issues ordinary kernel commands; there is no bulk back door to kernel tables.
- **K-ING-2** The raw payload is kept as an Artifact and every resulting Observation's Source names the batch.
- FHIR is an **interchange** representation (in and out), mapped through versioned specifications like SDTM;
  it is not the internal ontology.

---

## 9. Trust Engine

### 9.1 Actors

- `Actor = Person | ServiceAccount | AutomationAgent`.
- Authentication (OIDC) yields an Actor; the kernel never stores credentials. Step-up authentication for
  signatures is recorded on the Attestation.

### 9.2 Layered identity (ADR-2253)

A participant is not a code. The model separates identity layers, each with its own resolution capability:

| Layer | Example | Where it lives |
| --- | --- | --- |
| Person (healthcare identity) | Name, DOB, contact, MRN, national IDs | IdentityVault |
| Recruitment identity | Prescreening candidate, referral | IdentityVault + recruitment application |
| Study participant | `ParticipantId` (pseudonymous) | Kernel |
| Site-local identifier | Site's screening log number | IdentityVault |
| Sponsor pseudonym | `SubjectCode` | Kernel (value object) |
| Submission identifier | `USUBJID` | Standards projection |

- The **IdentityVault** is a separate schema with its own database role and envelope encryption (per-tenant
  key-encryption key in a KMS, per-record data keys, AES-256-GCM, blind indexes for lookup). It subsumes
  today's `EncryptedDemographics`.
- **K-IDN-1** Kernel APIs return `ParticipantId`; resolving any other layer requires a capability
  (`identity.reveal.<layer>`) and every resolution is audited in the tenant chain.
- **K-IDN-2** Linking the same person across studies, or matching an EHR record to a participant, is a vault
  operation with its own capability, evidence and audit.
- Erasure where law requires it is crypto-shredding of the data key; the pseudonymous kernel record remains.
- **Implementation (ADR-2458, K029):** the `identity_vault` schema holds sealed identity records for the person and
  site-local layers and the cross-study participant links. The application role reaches them only through
  `SECURITY DEFINER` functions under row-level security. Recording, revealing, erasing and linking are kernel
  commands (`identity.record.<layer>`, `identity.reveal.<layer>`, `identity.erase.<layer>`,
  `identity.link.cross_study`, `identity.reveal.links`), so each is authorized and audited, and a reveal commits its
  audit entry before anything is decrypted. `kernel.people` holds no direct identifier.

### 9.3 Authority computed from evidence (ADR-2251)

```
Actor ─(Membership)→ Organization
Actor ─(Grant)→ Capability @ Scope, effective_period, conditions
Role        = named bundle of capabilities (administrative convenience only)
Delegation  = Grant issued by an actor holding the capability with grant option, bounded by it (DOA)
Qualification = evidence about an actor: training completion (incl. protocol-specific), credential/licence,
                GCP certificate, with validity period and evidence Artifact
```

Scope path: `tenant / organization / study / country / study_site [/ cohort]`.

Capabilities are `resource.action` tokens (`observation.record`, `observation.verify`, `eligibility.sign`,
`enrollment.unblind`, `protocol.approve`, `artifact.file`, `identity.reveal.person`, `specimen.use`),
each with a **verb class** `Read | Propose | Execute | Approve`.

```
Authorize(actor, capability, resource, at, known_at) → Permit(obligations[]) | Deny(reason, missing_evidence[])
  1. Active Grants and Delegations at (at, known_at)
  2. Capability match and scope covers resource
  3. Conditions: required Qualifications current; StudySite/Effectivity state; protocol state;
     resource state (e.g. Lock); blinding; consent scope for data use
  4. Separation of duties (approver ≠ executor)
  5. Decision + inputs recorded
```

So `CanSignEligibility(Fred, Participant42)` is computed from role + delegation + required training +
protocol training + credential + effective dates + site activation state, never from a role string.

- **K-AUTH-1** Authorization runs in the kernel command pipeline, not in endpoint filters.
- **K-AUTH-2** Denials and permits are audited with the grants and evidence that justified them.
- **K-AUTH-3** An activity performed outside an active delegation is denied; a retroactive DOA cannot make it
  valid, it can only create a flagged deviation.
- **K-AUTH-4** Blinding is a condition, not a role; unblinded capabilities are separate and never bundled by
  default.
- **K-AUTH-5** Actor prerequisites (training, credentials) are conditions evaluated from Qualifications, so
  training evidence directly gates actions. Site-level readiness is StudySite/Effectivity (§7.1), not an actor
  condition (inventory §3 item 6).
- **K-AUTH-6** A denial names the missing evidence (e.g. "protocol v3 training not current"), so the user can
  act on it. The decision carries every condition evaluated, met and unmet, each with its evidence NodeRef, and
  explains itself as an `Explanation` (§5.5); the `DecisionDenied` event and audit entry record the conditions.
- **K-AUTH-7** Authorization is one decision procedure over `AuthorizationRequest(actor, tenant, capability,
  resource, at, known_at)`. Every kernel command declares its capability and the object it is authorized against;
  a command without a declaration is denied. A dry run, `CanI(capability, resource)`, asks the same procedure for
  the calling actor only. It is not written to the audit chain; it is recorded in the authorization access log
  (ADR-2483), and the command catalogue for an object lists each command offered on it with its dry-run decision.
- **K-AUTH-8** A work queue is keyed by a capability, never a role name. Its definition is versioned data (a code,
  a capability with its verb class, and a registered source of due items). A person's queue holds the source's
  items on which the evaluator permits them that capability now (K-AUTH-7), so it follows their Grants and
  Delegations in scope and a revoked grant removes its items. Items the reader may not see are masked in place
  (ADR-2471). A queue read without a scope spans every study of the tenant, which is the portfolio queue; queues
  never span tenants (ADR-2486).

*Implemented in part (#4781, ADR-2455):* `Role`, `Grant` (with bitemporal `GrantPeriod` validity) and delegation as
a grant bounded by its parent, changed by `DefineRole`, `IssueGrant`, `DelegateGrant` and `RevokeGrant`.
`AuthorityEvaluator` is the `IAuthorizationEvaluator`: it checks steps 1 and 2, requires every grant above a
delegation to be in effect, denies automation agents Execute and Approve, and records each decision's conditions
in `provenance_activities.authorization`. Separation of duties (step 4) and the StudySite, protocol and blinding
conditions of step 3 are still to come. Work-queue definitions and the queue read
(K-AUTH-8) are implemented (#4849), with protocol versions in review as the first item source.

*Qualifications (#4781, ADR-2459, K031):* `Qualification` (bitemporal `QualificationPeriod` validity, a
`TrainingRecord` or `Credential` artifact version as evidence, a protocol version for protocol training) and
`QualificationRequirement` (a capability, a qualification code, optionally a protocol version, a scope and bitemporal
validity), changed by `RecordQualification`, `RevokeQualification`, `DefineQualificationRequirement` and
`RetireQualificationRequirement`. The evaluator applies step 3's first condition: every requirement in effect over the
resource must be met by a qualification the actor holds at the same `(at, known_at)`, and a denial names what to
obtain, or what to renew and when it expired. Scenario 12 of §14 passes. A requirement names its protocol version
explicitly; deriving it from the resource's governing version is still to come.

*Visibility (#4781, ADR-2457):* `GrantNodeVisibility` is the `INodeVisibility` computed from the same grants. A reader
sees what a grant it holds in effect covers, the organization, study and country above that scope, participants and
sites through the study sites they reach, and tenant reference data. Everything else is masked `NotPermitted`.

### 9.4 Computable consent

Consent is not only a signed PDF. `ConsentStatus` is **bitemporal state** (resolves inventory §3 item 4):

```
ConsentStatus(participant, consent_form_version → protocol_version, scopes[], valid_period, recorded_period,
              basis: Attestation(Consented) + ICF Artifact | SourceDocument (paper) + site Attestation)
ConsentScope(purpose: MainStudy | FutureResearch | GeneticTesting | CommercialResearch | SpecimenStorage(duration)
             | InternationalTransfer(jurisdictions) | Contact | …, decision: Granted | Declined, conditions)
```

- Paper consent is an ordinary basis with a `SourceDocument` artifact and an Attestation by responsible site
  staff; there is no override flag (resolves G6).
- **K-CONS-1** `ConsentAllows(participant, purpose, data_or_specimen, organization, jurisdiction, at)` is a
  kernel query, and every data-use command (analysis, specimen use, transfer, sharing) evaluates it in the
  pipeline next to authorization.
- **K-CONS-2** Withdrawal closes the affected scopes' valid periods; data already collected stays (per
  regulation) but new uses outside remaining scopes are denied.
- `ProtocolVersionRef` on today's consent maps to `ConsentStatus.protocol_version` (inventory §3 item 3).

### 9.5 Provenance and Attestation

- `ProvenanceActivity(id, kind, actor, started, ended, inputs[], outputs[], protocol_version_context,
  tool_version, rule/algorithm versions)`, W3C PROV-shaped.
- **K-PROV-1** Every kernel write is produced by exactly one ProvenanceActivity, in the same transaction.
- **K-PROV-2** Derived facts name their inputs and the versioned algorithm.
- **K-PROV-3** AI-assisted actions record model identifier, prompt/plan artifact and the approving actor.
- `Attestation(id, actor, meaning, target(kind, id, content_hash), authenticated_at, method, recorded_at,
  reason)`, append-only. Meanings: `Authored`, `Reviewed`, `Approved`, `Verified`, `Responsibility`,
  `Consented`, `Withdrawn`. Binding to a content hash means a signed record cannot silently change
  (21 CFR 11.70). Only a person signs. The authentication must be at most five minutes old, and the target's
  current content must have the signed hash. Commands that need a signature name an Attestation, which
  `IAttestationVerifier` checks within the tenant: the acting person must be the signer (ADR-2452). Content hashes
  are `sha256:` and lower-case hex over canonical JSON, for protocol versions and standards metadata alike.

### 9.6 Lineage graph (ADR-2255)

Lineage is the union of provenance edges and specification edges, queryable in both directions.

```
LineageNode = any versioned kernel object: Objective, Endpoint, Estimand, ProtocolVersion element,
              PlannedActivity, CollectionSpec item, Observation, Assertion, Specimen, Artifact,
              MappingSpecification, Transformation, Derivation, dataset record/variable, analysis result,
              ValidationFinding, Proposal
LineageEdge(from, to, kind, activity_id): requiredBy | definedBy | collectedBy | derivedFrom |
              mappedBy | transformedBy | validatedBy | attestedBy | governedBy(protocol_version) | supersedes
```

Backward trace from a submission value:
`ADSL.AGE → DM.AGE → canonical AGE Observation → Demographics CollectionSpec → Study metadata version
→ ProtocolVersion → Applicability at site and date → source, actor, timestamp, audit history`.

Forward (scientific traceability):
`Objective → Endpoint → Estimand → PlannedActivity → CollectionSpec → Observation → SDTM → ADaM →
analysis → TLF → submission artifact`.

- **K-LIN-1** Every derived object has at least one inbound lineage edge; orphans are findings.
- **K-LIN-2** Lineage edges are written in the same transaction as the object they describe (K-TX-2).
- **K-LIN-3** Given the same inputs and the same `(valid_at, known_at)`, any projection is byte-reproducible.
- **Inspection readiness** is a lineage query: "everything relating to participant 104-017's week 12 primary
  endpoint assessment" returns consent version, protocol version, delegation, visit, source, observation,
  corrections, queries, audit trail, SDV/SDR, monitoring findings, SDTM mapping, ADaM derivation and analysis
  lineage, as one evidence dossier.

Implemented by ADR-2471 (#4815, #4840):

- **Nodes and edges.** Nodes are pinned NodeRefs (§3.4). `kernel.lineage_nodes` and `kernel.lineage_edges` are
  insert-only and tenant-scoped.
- **Writing.** A handler declares inputs with `IKernelUnitOfWork.LinkInput`, and the pipeline writes them in the
  command transaction. It also adds `supersedes` from the previous version on every update. It refuses to commit a
  `[DerivedObject]` aggregate without an input (K-LIN-1, K-LIN-2), and an hourly check reports stored orphans.
- **Reading.** `IKernelLineage` implements the read contract below and the evidence dossier. Objects the reader may
  not see come back masked, never omitted, so a trace keeps its length:

```
Lineage(node, direction, depth, edge_kinds, valid_at, known_at) → { start, nodes[], edges[], truncated }
node = { handle, kind, ref | null, mask: null | NotPermitted | Blinded | IdentityVault, distance }
```

- **Masking elsewhere.** `MaskReason` and `INodeVisibility` are the masking contract for every kernel read that can
  reach blinded or identity-layer content.
- **Dependents** (#4841, Canvas D2). `IKernelLineage.DependentsAsync` implements the read below:

```
Dependents(node, depth, edge_kinds, valid_at, known_at) → { start, groups[{ kind, count, objects[] }], total, truncated }
```

  Counts are of objects, not versions. Masked objects are counted, so a count never depends on the reader. The start
  object is never its own dependent. Amendment impact (K-PROT-2) is one caller of this read (§6.2).
- **Protocol elements** (#4808, ADR-2474). An approved version's design elements are `protocol_element` nodes, one per
  study and element id, versioned by the protocol version that last changed them. Approval writes them with
  `governedBy` from the version, `supersedes` from a changed element's previous version, the scientific chain between
  elements, and `definedBy` from the concept versions that define them. Amendment impact and simulation follow only
  the elements a change touches.

---

## 10. Quality Engine

### 10.1 Rules are versioned data (ADR-2254)

```
Rule(id, version, authority: CDISC | FDA | PMDA | NMPA | EMA | Sponsor | Study,
     source_citation, applicability(standard, standard_version, agency, dataset/domain, stage),
     expression (AST), severity, explanation, remediation_guidance, effective_period)
RulePack(id, version, authority, rules[], release Attestation)
```

- **K-RULE-1** Rules are data. The platform loads rule packs; adding or changing a rule never needs a
  platform release.
- **K-RULE-2** Rule packs implement **public** standards and regulatory requirements (CDISC conformance
  rules, FDA/PMDA validator specifications and technical conformance guides, Define-XML specification,
  controlled terminology). They MUST NOT reproduce any vendor's proprietary rule implementations or messages.
- **K-RULE-3** One expression language (AST) serves EDC edit checks, standards conformance, ADaM checks,
  sponsor and study rules, and quality indicators. The evaluator is behind a narrow port
  (`IRuleEvaluator`) so a **Rust runtime** can replace the .NET one for large, deterministic, streaming
  workloads without changing the model. The existing `AstEditCheckEngine` is the seed.
- **K-RULE-4** Evaluation is deterministic: same rule version + same inputs → same findings.

Implemented by #4811 ([ADR-2461](../adr/2026-09-30-rule-expression-language-rule-pack-format-and-evaluator-port.md)):
`Cadence.Rules` holds the expression language, the rule-pack reader and `IRuleEvaluator` with its .NET
implementation; it references `Cadence.Kernel.Abstractions` only. A `RulePack` is a library item of kind `RulePack`
(§6.3): its release `Attestation` is the library item's approval, and a study uses the versions its standards profile
pins. Rule packs are described in [RULE_PACKS.md](RULE_PACKS.md).

### 10.2 Validation across the lifecycle

| Stage | Examples | Blocking effect |
| --- | --- | --- |
| Design | Protocol completeness, CollectionSpec ↔ Concept ↔ standard mapping, CT and UCUM checks (K-STD-2) | `Error` blocks publishing the design object |
| Collection | Edit checks, semantic and unit validation, cross-domain checks | Never blocks a kernel write of a clinical fact (below) |
| Standardization | SDTM/SEND conformance, CT, sponsor and agency rules | Blocks release of a dataset Artifact at `Error` unless explained |
| Analysis | ADaM conformance, derivation provenance, traceability | Same |
| Submission | Define-XML, reviewer guides, XPT/Dataset-JSON, package completeness | Blocks package attestation |

**Edit checks and kernel writes** (resolves G4): a clinical fact that is well-typed (Concept, UCUM unit,
value type, permissible CT values declared in the CollectionSpec) is **always accepted**; the kernel
validates only those structural constraints pre-commit. Edit checks run after commit and produce Findings
and `Queried` Assertions; they never reject what the source said. EDC may block **form completion**
(application workflow state) on unresolved fatal findings. Rule sets are versioned RulePacks bound to the
ProtocolVersion and CollectionSpec versions they target.

### 10.3 ValidationFinding

```
ValidationRun(id, stage, rule_packs[], inputs(as_of, spec versions), started, completed)
ValidationFinding(id, rule(id, version), standard, standard_version, agency, agency_rule_version,
                  study, target(dataset/record/variable | observation | design element), severity,
                  expected, observed, source lineage, first_detected_run, last_detected_run,
                  status, classification, resolution, resolution_authority, resolution_evidence)
status: Open → Investigating → Resolved | Accepted | Explained
classification: DataIssue | MappingIssue | MetadataIssue | RuleIssue | NotAnIssue
```

- **K-FIND-1** Findings are identified by `(rule, target)` across runs, so re-running validation updates
  `last_detected_run` instead of duplicating findings; a finding not re-detected is auto-resolved with the
  run as evidence.
- **K-FIND-2** `Accepted` and `Explained` require an Attestation by an actor with the capability; the
  explanation flows into the reviewer guide.
- **K-FIND-3** Every finding links to lineage, so its root cause (data, mapping, metadata, CRF version,
  CT package) is traversable.

Implemented by #4812 ([ADR-2462](../adr/2026-09-30-validation-findings-derived-from-immutable-runs-and-attested-decisions.md)):
a run is an immutable, study-scoped aggregate holding its released rule pack versions, datasets, versioned inputs and
one detection row per problem. A finding's identifier is derived from tenant, study, stage, pack, rule and target
(not the rule version), and its status is derived on read from the study's runs and a per-finding decisions record,
so re-running never duplicates a finding or rewrites a run. `Accepted` needs a `Responsibility` and `Explained` an
`Approved` attestation over the decision's own text. A run links `DefinedBy` from its rule packs and `ValidatedBy`
from its inputs; each decision links `DerivedFrom` its run. Per-finding opened and resolved events are replaced by
the counts on `ValidationRunCompleted`; `agency_rule_version` is not modelled yet.

### 10.4 Quality by design and RBQM

```
CriticalToQualityFactor(study, description, linked endpoints/processes)
  → Risk(likelihood, impact, detectability, score) → Control(kind: design | process | monitoring)
  → Indicator(kind: KRI | QTL, rule, threshold, scope) → Signal(breach, evidence)
  → Investigation → Issue → CAPA(actions, effectiveness check)
Deviation(participant/site/study, category, importance, source finding/assertion)
```

- **K-QBD-1** Every QTL and KRI is a Rule evaluated by the same engine over kernel state; breaches raise
  Signals with lineage to the contributing observations.
- **K-QBD-2** Monitoring plans (SDV/SDR strategy) reference Risks and Controls; monitoring findings are
  Findings/Assertions, not free text.
- Audits, inspections and CAPA workflows are QMS application state over these kernel concepts.

Indicators are implemented by #4845 ([ADR-2488](../adr/2026-09-30-study-pulse-metrics-as-versioned-indicators.md)): an `IndicatorSet` library item
holds versioned indicators (Study Pulse metrics, attention rules, KRIs and QTLs), released by attestation and pinned in
the study's standards profile like rule packs. Each indicator counts the records of a population for which its
expression is true, through `IRuleEvaluator`, and reports a count or a percentage against an optional threshold. A
result names the indicator and set versions, carries input and result hashes and up to 100 contributing objects, and
has an explanation produced as `quality.indicator/<id>@<version>`. Ranking in Needs Attention is application logic,
and a study configures its Pulse only by pinning released sets (ADR-2488 §4.5). A study's Pulse reads each population
from a registered population source (the kernel provides `enrollments` and `validation_findings`) and counts only the
records the reader may see: hidden records are left out of the value and its contributors, and the value is marked
partial with the mask reasons but no hidden count, so a number never reveals another site's or blinded data
(ADR-2488 §4.6, §4.7). Signals follow.

### 10.5 Locks and freezes (resolves G5)

`Frozen` and `Locked` are Assertions at a scope (form instance, participant, study site, study), each with an
Attestation. They act as authorization conditions that deny `Execute` on covered facts. Unlocking is a
retracting Assertion with a reason and an Attestation. Database lock is `Locked` at Study scope; EDC form
approval and read-only display are application workflow over it.

---

## 11. Intelligence Engine

- An AI agent is an `AutomationAgent` actor whose default grants are `Read` and `Propose`, scoped to the
  requesting human's scope, never wider.
- A **Proposal** is a first-class kernel object `{intent, plan (artifact), affected lineage nodes, simulated
  impact, requested capabilities, expiry}` with no effect on kernel state. AI-drafted queries are Proposals,
  not a query status (inventory §3 item 10, G11).
- `Execute` happens by (a) a human with the capability approving and executing as themselves with the agent
  recorded in provenance, or (b) a pre-authorised, narrowly scoped, time-boxed standing grant that a human
  approved.
- **K-AI-1** No code path lets an `AutomationAgent` invoke `Execute` or `Approve` without a human Attestation
  or standing grant reference.
- **K-AI-2** Every executed proposal links proposal → approval → activity → resulting events.
- **K-AI-3** Agents read through the same authorization and consent pipeline as humans.
- **Lineage-grounded reasoning**: agents answer questions such as "why does rule X fail for these 37
  participants?" by traversing Finding → dataset records → mapping specification → observations → source
  CollectionSpec versions → protocol elements, and respond with a Proposal for remediation, never a direct
  change to regulated data.
- Analytics, forecasting and statistical outputs are **projections** with provenance; they never write
  clinical truth.

### 11.1 Simulation (ADR-2480)

Simulation answers "what happens if I change this?" (Clinical Canvas Simulate verb) by **evaluating, never
writing**. The change is a *candidate version* of a versioned specification: a draft ProtocolVersion,
MappingSpecification version or RulePack version, identified by its content hash. The engine evaluates the pure
functions twice at one pinned `(valid_at, known_at)`, with the current versions (baseline) and with the candidate
(candidate), and returns the difference:

```
Simulate(scope, candidate, as_of, actor) → SimulationResult{ as_of, candidate, engine_versions, input_hash,
  result_hash, structural_diff[] (K-PROT-1), schedule_changes[] (K-TIME-7), projection_changes[] (K-PROJ-2),
  validation_delta (K-RULE-4, K-FIND-1), dependents[] (Dependents), masked[] }
```

- **K-SIM-1** Simulation writes no clinical state; the engine cannot reach the command sender or a unit of work.
- **K-SIM-2** Baseline and candidate use the same pinned `(valid_at, known_at)` and engine versions.
- **K-SIM-3** Same inputs (as-of, base versions, candidate hash, engine versions) → same `result_hash`.
- **K-SIM-4** Simulation reads with the requester's authority; values derived from content the actor cannot read
  are masked, so a simulation cannot reveal blinded data.
- **K-SIM-5** A saved result is an Artifact referenced by a Proposal. Approving the Proposal does not apply it:
  the real commands run through the pipeline against current state.

A result has only the sections whose pure functions exist; its `engine_versions` name them. The structural diff
of a protocol candidate reports each element once, by id, with the JSON Pointers of its changed values. Its
schedule changes are the timed activities added to, removed from or moved on the expected schedule (K-TIME-7), on
the study-day axis with their planned visit before and after (ADR-2487). Its
dependents come from the one dependents read, as amendment impact's do (K-PROT-2). When the
actor may not see the study or a version, the diff and schedule changes are withheld as masked parts and the hashes are computed from the
mask, not the content (ADR-2480 §4.6).

Running a simulation is a read (`simulation.run` plus the `Read` capabilities for what it reads) and is not
written to the tenant audit chain; saving it as a Proposal is an audited command. Study data is never branched.
Copy-on-write branches, review workflows over simulations, and operational what-ifs are out of scope; ADR-2480
§4.4 lists when branches would be reconsidered, and review is application state over Proposals.

---

## 12. Application Capability Map

Applications are compositions of engine concepts. This table fixes ownership, not detailed design.

| Capability | Built on (kernel) | Application-owned state |
| --- | --- | --- |
| Study design / protocol authoring | ProtocolVersion model, Concepts, Estimands | Authoring drafts, comments, renderings |
| Feasibility | Organization, Site, (RWD) Observations | Surveys, forecasts, site scoring |
| Regulatory start-up | Effectivity, Artifacts, Attestations | Submission packets, tracking |
| Contracts & finance | Organization, StudySite, events | Budgets, CTAs, invoices, payments |
| Recruitment | Identity layers, Eligibility | Funnels, referrals, prescreening workflow |
| Participant experience / eConsent / eCOA | ConsentStatus, Observations (ePRO), ActivityLocation | Portal content, reminders, travel |
| EDC | CollectionSpec, Observations, Assertions, Findings, Locks | Form instances, query workflow, UI state |
| RTSM / supply | Enrollment, arm Assertions, blinding conditions | Algorithms, inventory, shipments |
| Laboratory / biospecimens | Specimen, custody, Observations, range sets | Kits, accessioning, storage maps |
| Imaging / devices / DHT | Artifacts, Sources, derived Observations | Transfers, reads, device fleet |
| Safety / PV | Observations (AE), Assertions (causality, expectedness, coding) | Cases, narratives, expedited reports |
| Medical coding | Concept Registry, `CodedAs` Assertions | Synonym lists, auto-coder config |
| Data management | Findings, Assertions, Locks, ingestion | Review workflows, reconciliation views |
| Risk / quality / monitoring | QbD chain, Findings, Deviations | Plans, visit reports, CAPA workflow |
| TMF / ISF | Artifacts, classification Assertions, expected-document rules | Filing views, QC workflow |
| Standards / MDR / conformance | Standards Registry, mappings, rules, Findings | Authoring and governance UI |
| Statistics / analysis | Estimands, Derivations, lineage | SAP authoring, analysis code, TLFs |
| Submission / regulatory / transparency | Dataset Artifacts, lineage, Attestations | Packages, correspondence, registry records |
| RWE / post-market | Ingestion, identity layers, consent scopes | Registries, study-specific cohorts |

---

## 13. Non-Functional Requirements That Shape the Model

- **Offline-first eCOA/eSource**: offline captures are commands with client valid time and idempotency keys;
  recorded time is set on sync; conflicts become Assertions (K-TIME-9).
- **Hot paths** (rule evaluation, transformation, derivation, unit normalisation, bulk ingest, dataset
  generation) sit behind narrow ports. The unified rules/transformation engine is the designated **Rust
  seam** (ADR-2254): millions of records, deterministic, parallel, streaming, parse-heavy, usable as CLI,
  server or WASM. It is introduced when a measured workload needs it, not before.
- **Data volume**: Observations are the growth table → partition by `(tenant_id, study_id)`, BRIN on recorded
  time, indexes on the current-truth predicate (`upper_inf(recorded_period)`).
- **Privacy and residency**: direct identifiers only in the IdentityVault; residency is a tenant property, set when
  the tenant is provisioned, and a vault serves only the tenants of its own region (ADR-2458).

---

## 14. Acceptance Tests

1. Reconstruct a participant's governing protocol version and expected schedule as of `(t, known_at)` for a
   fixture with an amendment effective at Site A but not Site B.
2. Correct an observation; the current and the pre-correction views both reproduce exactly.
3. Act outside an active delegation: denial, audited denial naming missing evidence, and a retroactive DOA
   does not validate it.
4. An AI agent proposes an amendment impact plan; it cannot execute without a human attestation, and
   provenance links proposal → approval → activity → events.
5. Cross-tenant read and cross-tenant FK insert both fail at the database layer.
6. Generate an SDTM VS dataset twice for different `known_at`: reproducible, with provenance naming the
   mapping specification and CT versions.
7. Architecture tests K-XS-1/2 and K-MOD-1 fail on a deliberately introduced duplicate `Site` entity.
8. Publish a CollectionSpec item whose unit has no conformant SDTM representation: publishing is blocked with
   a design-stage finding (K-STD-2).
9. Trace an ADaM value backward to its CollectionSpec, ProtocolVersion, site Applicability and source audit
   history, and an Objective forward to the datasets that serve it (K-LIN).
10. Re-run validation after a mapping fix: the finding is resolved by the run, not duplicated (K-FIND-1).
11. Use a specimen for genetic testing when the participant declined that scope: denied (K-CONS-1).
12. An eligibility signature by an actor whose protocol training expired is denied, and the denial names the
    expired qualification (K-AUTH-5, K-AUTH-6).

---

## 15. Roadmap

Each phase ships with tests for the invariants it introduces. Issue numbers refer to epic #4719.

| Phase | Outcome | Scope | Issues |
| --- | --- | --- | --- |
| **0. Foundations** | A kernel that can hold truth safely | Kernel projects and pure Domain; `IClock`; tstzrange + exclusion; RLS + composite FKs; command pipeline with provenance, audit chain, outbox, idempotency, error registry; architecture baseline | #4772, #4784, #4773, #4780, #4779, #4785 |
| **1. EDC on the kernel** | `apps/execution` runs on kernel concepts | ProtocolVersion lineage; Participant/Enrollment; Effectivity and Applicability; ConsentStatus; Observation, Assertion, Source, Artifact, Attestation; authority with Qualifications; IdentityVault; EDC projection | #4775, #4776, #4777, #4729, #4781, #4782, #4727, #4728 |
| **2. Knowledge and standards** | Meaning-first design and continuous standards | Concept Registry and CT versions; executable protocol elements (objectives → estimands); Standards Registry; MappingSpecification; rule model and packs; ValidationFinding; design-time conformance; continuous SDTM projection; lineage store | new issues (spec §6, §9.6, §10) |
| **3. Quality and trust expansion** | Native RBQM and computable consent | QbD chain, KRIs/QTLs, deviations, locks; consent scopes; Specimen and custody; ingestion layer; AI Proposals | #4783 + new issues |
| **4. Analysis and submission** | Scientific traceability to the CSR | Statistical design artifacts, Derivations, ADaM, TLF lineage, Define-XML, reviewer guides, submission package, Rust rules/transformation runtime if measured need | new issues |
| **5. Lifecycle breadth** | Whole evidence lifecycle | Feasibility, start-up, vendors, finance, recruitment, DCT, regulatory, transparency, RWE/post-market | new issues; scaffold services per #4787 |
| **Verification** | Spec acceptance | §14 suite against PostgreSQL | #4731 |

The 17 scaffold services are dispositioned against §12 (#4787): each becomes an application module over the
kernel, is merged into another, or is retired.

---

## 16. What This Supersedes

- ADRs describing per-service ownership of study/site/subject data or module-local audit logs (annotated per
  `AGENTS.md` §12 as their replacements land).
- ADR-2237 (schema-per-tenant), superseded by ADR-2250.
- The "microservices catalog" framing in `ARCHITECTURE.md` §2 (to be rewritten as kernel + engines +
  applications).
- Neo4j as the protocol store (ADR-2245); graph analytics MAY be a projection of the lineage store.

---

## 17. Decisions

| # | Decision | Outcome | ADR |
| --- | --- | --- | --- |
| K001 | Modular monolith kernel with single schema | Accepted | ADR-2244, ADR-2247 |
| K002 | Bitemporal implementation | Hand-rolled `tstzrange` + exclusion constraints | ADR-2246 |
| K003 | Tenant isolation | RLS in a shared schema; DB-per-tenant upgrade path | ADR-2250 |
| K004 | Policy engine | Hand-coded evaluator behind `IAuthorizer`, authority computed from evidence | ADR-2251 |
| K005 | Concept vocabulary | Concept Registry keyed by CDISC Biomedical Concepts with mapped external codes; per-tenant, pinned terminology | ADR-2252, ADR-2470 |
| K006 | Identity vault | Separate schema and key hierarchy, same deployment; layered identity | ADR-2253 |
| K007 | Neo4j for designer | Dropped for v1; protocol lifecycle in PostgreSQL | ADR-2245, ADR-2452 |
| K008 | Rules and standards | Rules as versioned data over one AST; public standards only; Rust runtime seam | ADR-2254 |
| K009 | Lineage | Lineage graph in the kernel schema, written transactionally, queried both ways; masked, never dropped, reads | ADR-2255, ADR-2471 |
| K010 | Command pipeline and audit | One transaction per command; per-tenant hash chain; insert-only history | ADR-2449 |
| K011 | Simulation | Evaluate-only over candidate versions; no study branches | ADR-2480 |
| K012 | Object references and zoom path | `kind:id@version` NodeRef; kernel-computed, presentation-only zoom path | ADR-2481 |
| K013 | Standards Registry and MDR | Tenant-scoped reference data; one governance envelope for library items; attested approval; immutable once registered | ADR-2460 |
| K014 | Explanations | One `Explanation` type produced by the rule owner; authorization returns met and unmet conditions with evidence | ADR-2482 |
| K015 | Dry-run authorization | One evaluator for commands and `CanI`; declared command capabilities; dry runs in the access log, not the audit chain | ADR-2483 |
| K016 | Search | PostgreSQL full-text search over the source tables under RLS; NodeRef results; identity content excluded | ADR-2484 |
| K017 | Participants and enrollment | Identity-only Participant; bitemporal Enrollment status rows; gap-free numbers under the Study advisory lock; attested withdrawal | ADR-2453 |
| K018 | Rule language and evaluator | Canonical JSON AST with Kleene logic and fixed coercions; packs are `RulePack` library items; `IRuleEvaluator` in `Cadence.Rules` | ADR-2461 |
| K019 | Activity stream | Asynchronous projection of outbox envelopes scoped by zoom path; payload-free; masked per reader at read time | ADR-2485 |
| K020 | Evidence artifacts | Immutable, hash-addressed versions; bytes in object storage; declared kind, filing as an Assertion; attestations sign a version | ADR-2454 |
| K021 | Grants, roles and delegation | Bitemporal grants of a capability or role at a scope; delegation as a bounded child grant; platform operators bootstrap tenants | ADR-2455 |
| K022 | Validation findings | Immutable study-scoped runs; finding identity derived from rule and target; status derived from runs and attested decisions | ADR-2462 |
| K023 | Work queues | Versioned definitions keyed by a capability; items from a registered source kept per reader by the one evaluator; portfolio queues span a tenant's studies | ADR-2486 |
| K024 | Effectivity and study-site activation | Bitemporal effectivity per version and scope on artifact-version evidence; most specific scope decides; activation needs a version in force | ADR-2456 |
| K025 | Expected schedule | Pure function of the design, a reference start and an as-of date; SDTM study days with no day 0; simulation reports schedule changes on the study-day axis | ADR-2487 |
| K026 | Indicators | Pulse metrics, attention rules, KRIs and QTLs as versioned indicators in released `IndicatorSet` library items; counted through `IRuleEvaluator`; results carry the definition version, hashes, contributors and an explanation | ADR-2488 |
| K027 | Grant-backed visibility | A reader sees what its grants in effect cover and the scope path above them; participants and sites through their study sites; reference data to any grant holder | ADR-2457 |
| K028 | Mapping specifications | Governed library items pinned to one standard version, CT versions and Concept versions; K-STD-1 checked at protocol submission, gaps accepted as findings; engine behind `IMappingEngine` | ADR-2463 |
| K029 | IdentityVault | Direct identifiers sealed per record (AES-256-GCM data keys wrapped by a per-tenant key) in the `identity_vault` schema, reached only through its functions; record, reveal, erase and cross-study link are audited kernel commands; residency is a tenant property | ADR-2458 |
| K031 | Qualifications | Actor prerequisites are bitemporal Qualifications with artifact evidence; QualificationRequirements name the qualification a capability needs within a scope; the evaluator denies naming what to obtain or renew (K-AUTH-5, K-AUTH-6) | ADR-2459 |
| K032 | Visit timing, anchors and cycles | `ExpectedSchedule.Assess` measures an actual date against the window with no built-in severity; visits anchored on another visit's actual date; repeating cycles as numbered occurrences | ADR-2507 |

---

## Appendix A. Resolution of the inventory's disagreements (inventory §3)

| # | Topic | Resolution | Where |
| --- | --- | --- | --- |
| 1 | Enrollment states | Inventory recommendation accepted: 7 states; `ReconsentRequired` → pending transition; `Unblinded` → Attestation-bearing event + RTSM Assertion | §7.4 |
| 2 | Unblind/Withdraw logic | Only guards migrate; withdrawal is a dedicated command | §7.4 |
| 3 | `ProtocolVersionRef` | Maps to `ConsentStatus.protocol_version`; Applicability replaces `ActiveProtocolVersion`, observation tags and `FormSubmission.ProtocolVersion` | §7.5, §9.4 |
| 4 | Consent model | Bitemporal ConsentStatus with scopes + `Attestation(Consented)` + ICF Artifact | §9.4 |
| 5 | `EConsentCompletedConsumer` | Replaced: eConsent issues kernel commands as a saga | §5.1 K-TX-1 |
| 6 | Site compliance cache | StudySite activation via Effectivity (K-TIME-4/5); K-AUTH-5 only for actor prerequisites | §7.1, §9.3 |
| 7 | Audit chain location | Kernel AuditEntry, one chain per tenant, unifying the two per-type chains | §5.3 |
| 8 | Outbox | Plumbing reused; domain events are net-new | §5.2 |
| 9 | Edit checks / UCUM / outlier | UCUM and ranges are kernel derivations; edit checks are EDC rules on the shared rule engine; outlier service deleted (central monitoring may reintroduce it as an indicator) | §8.1, §10 |
| 10 | Query targeting | Queries target `ObservationId`; `DraftAi` becomes a Proposal | §8.2, §11 |
| 11 | Code value objects and IDs | `StudyCode/SiteCode/SubjectCode` move to Abstractions; execution `SubjectId`/`ObservationId` replaced by kernel IDs | §3.2 |

## Appendix B. Disposition of the inventory's gaps (inventory §4)

| Gap | Disposition | Where |
| --- | --- | --- |
| G1 Enrollment numbering | Specified: gap-free per study (or per StudySite), under the Study lock | §7.4 |
| G2 `SubjectCode` scope | Specified: unique per Study; resolution query | §3.2 |
| G3 Eligibility | Specified: Eligibility Assertions over Observations | §7.4 |
| G4 Edit checks vs kernel writes | Specified: structural checks pre-commit; edit checks post-commit, never reject a fact | §10.2 |
| G5 Lock/freeze | Specified: `Frozen`/`Locked` Assertions as conditions | §10.5 |
| G6 Re-consent and paper consent | Specified | §7.5, §9.4 |
| G7 Non-randomized arm | Specified: RTSM Assertion with blinding class | §7.4 |
| G8 Extension properties | Specified: not allowed; use Concept qualifiers | §8.1 K-FACT-5 |
| G9 Local lab ranges | Specified: range-set Artifacts; `LabFeed` source | §8.3 |
| G10 Outlier detection | Deferred to the central-monitoring (RBQM) application as an Indicator | §10.4 |
| G11 Query metadata, AI drafts | Metadata stays EDC; AI drafts are Proposals | §11 |
| G12 Cross-aggregate locking | Specified: lock and read, no cross-aggregate write | §5.1 K-TX-4 |
| G13 Error-code registry | Specified | §5.4 |
| G14 Anonymous/system actors | Specified: named ServiceAccounts only | §5.1 K-TX-5 |
| G15 Natural-key idempotency | Specified | §5.1 K-TX-3 |
| G16 Legacy routes | Deferred to the API gateway's versioning and deprecation policy; aliases removed in migration slice S9 | inventory §5 |
