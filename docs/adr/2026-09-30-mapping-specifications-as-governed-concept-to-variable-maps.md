# ADR-2463: Mapping Specifications as Governed Concept-to-Variable Maps (K028)

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CDR-002 (automated SDTM and ADaM pipeline), PRD-LBM-001 (lab unit conversion between conventional and SI units)

---

## 1. Context & Problem Statement

In a conventional pipeline, SDTM mapping is written after data collection and fixed at database lock. Spec §6.3 moves
it to design time. A `MappingSpecification(version, standard_version, CT versions, rules[])` maps each Concept a
CollectionSpec item records to standard variables. It covers value-level mappings, unit standardisation
(`--ORRES/--ORRESU` → `--STRESN/--STRESU`) and CT decodes. **K-STD-1** requires the mapping to exist before first
data capture: a CollectionSpec cannot be published for a study whose declared standards have no mapping for it,
unless the gap is recorded as an accepted finding.

Issue #4810 asks five questions:

1. What is a mapping specification in the kernel?
2. What is it pinned to?
3. Where does the engine run?
4. Where is K-STD-1 enforced?
5. How does a mapping reach lineage?

It depends on the Standards Registry and MDR ([ADR-2460](2026-09-30-standards-registry-and-mdr-as-governed-tenant-reference-data.md)),
CT bindings resolved to the Concept Registry (#4888), and validation findings
([ADR-2462](2026-09-30-validation-findings-derived-from-immutable-runs-and-attested-decisions.md)).

## 2. Decision Drivers & Constraints

- **K-PROJ-1:** no kernel type names an SDTM, ADaM or SEND variable. Those names exist only in Standards Registry data
  and mapping specifications.
- **K-PROJ-2:** a projection is `f(kernel state, MappingSpecification version, CT versions)`, so a mapping version must
  be immutable and name exact versions, never "the latest".
- **K-SEM-1:** meaning is carried by Concepts, so mapping is authored against Concepts. One mapping then serves every
  CollectionSpec item that records the same Concept.
- **Spec §13:** the mapping engine sits behind a port so a native engine can replace it.
- **K-LIN-2:** lineage edges are written in the transaction of the object they describe.
- **K-RULE-2:** rules and mappings implement public standards. Unit conversion factors are the sponsor's stated data,
  not built-in rules.

## 3. Options Considered

### Option 1: A governed library item kind with a kernel-validated format (selected)

`LibraryItemKind.MappingSpecification` already exists in the MDR. Its content gets a strict JSON format, its drafting
resolves every reference, and a study pins it like any other library item.

- **Pros:** versioning, approval by attestation, deprecation, the content hash and the study pin all exist already.
  Nothing new is persisted, so no migration is needed.
- **Cons:** the content is JSON, not relational rows, so querying "which specifications map concept X" reads each
  pinned version. A study pins a handful, so this is cheap.

### Option 2: A new MappingSpecification aggregate with relational rows

- **Pros:** relational queries over mappings.
- **Cons:** it duplicates the governance envelope ADR-2460 built for library items, and it needs new tables, RLS and
  guards for a concept that is read as a whole.

### Option 3: Mappings as rules in rule packs

- **Pros:** one format for rules and mappings.
- **Cons:** a rule answers "is this valid"; a mapping answers "what does this become". Mixing them makes the
  evaluator produce records and the projection interpret findings.

## 4. Decision Outcome

**Chosen Option:** Option 1.

### 4.1 The format

`MappingSpecificationJson` (in `Cadence.Kernel.Abstractions`, namespace `Cadence.Kernel.Standards.Mapping`) reads and
writes the content. [MAPPING_SPECIFICATIONS.md](../kernel/MAPPING_SPECIFICATIONS.md) documents the format.

- A specification holds:
  - a title;
  - an optional `overrides` key;
  - the CT versions it uses;
  - unit terms (each UCUM code with its CT submission value, for example `mm[Hg]` → `mmHg`);
  - one mapping per Concept.
- Each mapping names:
  - the Concept at the version it was authored against;
  - the dataset;
  - constant variables (`assign`, for example `VSTESTCD = SYSBP`), which carry value-level metadata;
  - the result variables (original, original unit, standard character, standard numeric, standard unit);
  - the standard unit and linear conversions into it (`standard = value × factor + offset`);
  - optional rounding;
  - an optional codelist that decodes a coded result.
- Reading is strict and reports every problem. For example:
  - unknown properties are refused;
  - a variable may be written once;
  - a conversion must lead into the standard unit;
  - every unit the mapping writes needs a unit term.

### 4.2 Pinning and drafting

`DraftLibraryItem` with kind `MappingSpecification` must name exactly one standard version. Drafting also checks:

- every dataset and variable exists in that standard version's registered package (K-PROJ-1);
- every CT version is imported into the tenant's Concept Registry (resolved as in ADR-2460 §4.2);
- every decode codelist is in one of those CT versions;
- every Concept exists at the named version;
- an `overrides` key names an existing mapping specification.

Each resolved reference is a `DefinedBy` lineage input of the drafted version: the standard version, each
code-system version and each Concept version. The content is immutable, so these pins never change. Approval and
deprecation are the library item governance of ADR-2460. Spec §5.2's `MappingSpecificationPublished` is
`LibraryItemApproved` with kind `MappingSpecification`.

### 4.3 Study use and overrides

A study uses the mapping specifications it pins with `DeclareStudyStandards`. `StudyMappings.Resolve` applies them,
and a declaration is refused (`K.Standards.DeclarationInvalid`) when:

- a specification maps into a standard version the study does not declare;
- two versions of the same specification are pinned;
- an override's base is not pinned, is itself an override, or maps into another standard version;
- two specifications give the same Concept a mapping in the same standard version after overrides.

A study-level override is itself a versioned mapping specification. It names the base it overrides, and its mappings
replace that base's mappings for the same Concepts and add the rest. An override never replaces a mapping from another
specification: that is a clash. A mapping matches its Concept at any version:
the Concept's identity carries the meaning, and the version records what the mapping was authored against.

### 4.4 K-STD-1 at protocol submission

A CollectionSpec is published as part of a protocol version, so K-STD-1 is enforced when a draft protocol version is
submitted for review. That is also when its references must resolve (K-SEM-1).

- **Extension point.** The submit handler gains `IProtocolSubmissionCheck`, an extension point that modules register.
  The Standards module registers `MappingCoverageCheck`.
- **Coverage.** When the study has declared standards, every Concept its CollectionSpec items record must have a
  mapping in the resolved `StudyMappings`.
- **Accepting a gap.** Otherwise the gap must be an **Accepted** Design-stage finding on
  `MappingGap.Target(concept)`. That target is a `DesignElement` target whose key is the Concept's version-less
  NodeRef and whose dataset is `CollectionSpec`. The finding follows the Concept across its versions and protocol
  amendments. Accepting it needs a `Responsibility` attestation (K-FIND-2).
- **Refusal.** An uncovered Concept refuses the submission with `K.Standards.MappingMissing`, listing the items.
- **No declared standards.** A study that has declared no standards has nothing to map into and submits as before.
- **Lineage.** On success, each mapping specification version the design relies on becomes a `MappedBy` input of the
  protocol version, in the submit transaction (K-LIN-2). Continuous projection (#4814) adds `MappedBy` edges from the
  same versions to the records it writes.

### 4.5 The engine port

`IMappingEngine.Map(specification, input, decoder)` maps one collected value (Concept, value as invariant text, UCUM
unit) to a record fragment: the dataset and the variables with their values. `Cadence.Standards` provides the .NET
engine (`AddCadenceMapping`), and a host can register another implementation first.

The engine is pure and never guesses. Each of these is a problem, and a result with a problem writes nothing:

- a missing mapping;
- a missing unit;
- a missing conversion;
- a missing unit term;
- a missing decode;
- a quantity collected with a unit that is neither a number nor a bound such as `<1.0`;
- a conversion whose result is out of range.

A quantity is converted into the standard unit and rounded as the mapping says; without rounding it is written exactly,
without trailing zeros. A bound such as `<1.0` is kept as the character result with no numeric value. A coded result is written as its CT submission value.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - SDTM mapping is designed, reviewed and signed before first data capture.
  - An unmapped Concept is a visible, attested decision rather than a surprise at database lock.
  - Projection and conformance (#4813, #4814) reach exact, hash-identified mappings through the study's pins.
- **Negative Impact / Technical Debt:**
  - Only linear unit conversions are expressed; a non-linear conversion would need a new form.
  - The gap check reads accepted findings through `IValidationFindings`, so hosts that register `AddKernelStandards`
    also register `AddKernelValidationPersistence`.
  - The check runs at submission; a declaration revised between submission and approval is not re-checked.
- **Mitigation Strategy:**
  - Design-time conformance (#4813) produces the mapping-gap findings and runs the variable, CT and unit rules
    (K-STD-2).
  - Approval re-checking can be added through the same extension point if review shows declarations change in review.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `Cadence.Kernel.Abstractions/Standards/Mapping`: the model, JSON format, `StudyMappings`, `MappingGap` and the
    `IMappingEngine` port;
  - `Cadence.Kernel.Application/Standards`: the content validator, draft resolution, the declaration check and
    `MappingCoverageCheck`;
  - `Cadence.Kernel.Application/Protocol`: `IProtocolSubmissionCheck`;
  - `Cadence.Standards/Mapping`: `MappingEngine` and `AddCadenceMapping`.
- **Verification Plan:**
  - Unit tests:
    - VS systolic blood pressure (`SYSBP`, `mm[Hg]` → `VSORRES/VSORRESU/VSSTRESC/VSSTRESN/VSSTRESU`), including a kPa
      conversion;
    - LB glucose converted from mg/dL to mmol/L with rounding;
    - text and coded results, and missing values;
    - every "never guesses" problem;
    - the JSON round trip and every reader refusal;
    - override resolution and the refused pin combinations;
    - the gap target.
  - PostgreSQL integration tests:
    - drafting links the standard version, CT version and Concept versions as `DefinedBy`;
    - drafting refuses each kind of unresolved reference;
    - K-STD-1 refuses an unmapped Concept until the gap finding is accepted;
    - a mapped design links its mapping version as `MappedBy`;
    - overrides need their base, and mappings need a declared standard.
