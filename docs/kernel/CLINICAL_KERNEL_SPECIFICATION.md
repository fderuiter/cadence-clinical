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
- IDs are opaque to clients and never reused.

### 3.3 Tenancy invariants (ADR-2250)

- **K-ID-1** Every kernel table has `tenant_id NOT NULL`, every FK is composite `(tenant_id, id)`, so
  cross-tenant references are impossible at the schema level.
- **K-ID-2** PostgreSQL Row-Level Security is enabled **and forced** on every kernel table; the application
  role cannot bypass it. The tenant is set per transaction from the authenticated actor in exactly one place.
- **K-ID-3** Cross-tenant exchange occurs only through an explicit, audited `DataSharingAgreement`.
- **K-ID-4** Person identity is tenant-independent; roles and access are tenant-scoped.
- A regulated customer MAY be placed in a dedicated database with the identical schema (ADR-2250 upgrade path).

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
command; `KernelDbContext` refuses `SaveChanges` outside it. How to add a command:
[`packages/kernel/README.md`](../../packages/kernel/README.md#command-pipeline-adr-2449).

### 5.2 Events

`Envelope{ event_id, type, schema_version, tenant, occurred_valid, recorded, actor, activity_id,
correlation_id, causation_id, payload }`.

| Family | Examples |
| --- | --- |
| Knowledge | `ProtocolVersionDrafted`, `…Approved`, `AmendmentClassified`, `ConceptMapped`, `MappingSpecificationPublished`, `RulePackReleased` |
| Execution | `OrganizationRegistered`, `SiteLinkedToStudy`, `EffectivityGranted`, `StudySiteActivated`, `ParticipantEnrolled`, `ApplicabilityChanged`, `ParticipantWithdrawn` |
| Evidence | `EventRecorded`, `ObservationRecorded`, `ObservationCorrected`, `AssertionMade`, `AssertionRetracted`, `SpecimenCollected`, `SpecimenTransferred`, `ArtifactVersioned`, `IngestionBatchAccepted` |
| Trust | `ConsentGiven`, `ConsentScopeChanged`, `ConsentWithdrawn`, `GrantIssued`, `GrantRevoked`, `DelegationSigned`, `QualificationRecorded`, `DecisionDenied`, `Attested` |
| Quality | `ValidationRunCompleted`, `FindingOpened`, `FindingResolved`, `IndicatorBreached`, `SignalRaised`, `DeviationRecorded`, `LockApplied`, `LockReleased` |
| Intelligence | `ProposalCreated`, `ProposalApproved`, `ProposalExecuted`, `ProposalRejected` |

- **K-EVT-1** Event schemas are additive-only within a major version; consumers tolerate unknown fields.
- **K-EVT-2** Events are the integration contract; they are not replayed to rebuild clinical state.

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

- **ProtocolVersion** is an immutable, content-addressed snapshot (USDM document + `content_hash`) with
  `number`, `parent_version`, `amendment_id?`, `state`, `sponsor_approval`.
- State machine `Draft → InReview → Approved → Superseded | Withdrawn`.
  - `Approved` requires an Attestation with meaning `Approved` bound to `content_hash`.
  - Forking a new draft **does not** change the parent's state (readiness blocker, inventory §4).
  - `Superseded` is set only when a later version is effective everywhere it will ever be. Approved
    versions legitimately coexist.
  - `Withdrawn` is terminal and requires a reason and an Attestation.
- **Amendment**: intent, rationale, classification (substantial / non-substantial / urgent safety measure),
  summary of changes, **impacted elements** and a TransitionPolicy (§7.5).
- **K-PROT-1** Every ProtocolVersion element has a stable element ID across versions, so a structural
  diff between versions is computable.
- **K-PROT-2** **Amendment impact is computable**: given two ProtocolVersions, the kernel lists every
  downstream object that references a changed element (CollectionSpecs, mapping specifications, rules,
  RTSM configuration, monitoring plan, consent forms, TMF expected documents, open Proposals). An amendment
  cannot be approved while that impact report is not attested as reviewed.
- **K-PROT-3** The scientific chain `Objective → Endpoint → Estimand → required data → PlannedActivity →
  CollectionSpec` is explicit; a CollectionSpec item with no path to an Endpoint, Estimand, safety
  requirement or operational need is flagged at design time (a Quality finding, not a blocker).
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
  act on it.

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
- `Attestation(id, actor, meaning, target(kind, id, version, content_hash), authenticated_at, method,
  recorded_at, reason?)`, append-only. Meanings: `Authored`, `Reviewed`, `Approved`, `Verified`,
  `Responsibility`, `Consented`. Binding to a content hash means a signed record cannot silently change
  (21 CFR 11.70, PRD-SYS-053).

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
- **Privacy and residency**: direct identifiers only in the IdentityVault; residency is a tenant property.

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
| K005 | Concept vocabulary | Concept Registry keyed by CDISC Biomedical Concepts with mapped external codes | ADR-2252 |
| K006 | Identity vault | Separate schema and key hierarchy, same deployment; layered identity | ADR-2253 |
| K007 | Neo4j for designer | Dropped for v1 | ADR-2245 |
| K008 | Rules and standards | Rules as versioned data over one AST; public standards only; Rust runtime seam | ADR-2254 |
| K009 | Lineage | Lineage graph in the kernel schema, written transactionally, queried both ways | ADR-2255 |
| K010 | Command pipeline and audit | One transaction per command; per-tenant hash chain; insert-only history | ADR-2449 |

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
