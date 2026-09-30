# ADR-2472: Executable Protocol Design Document with Stable Element IDs

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Spec §6.2 says the protocol is a machine-readable specification of the experiment, and that CDISC USDM is its
interchange format, not its internal shape. ADR-2452 built the `ProtocolVersion` lifecycle, but its content was any
JSON object. The kernel could hash and freeze that content, but it could not read it. So it could not:

1. compare two versions element by element (K-PROT-1);
2. narrow amendment impact from the whole parent version to the elements that changed (K-PROT-2, ADR-2471 §4.5);
3. check that the scientific chain Objective → Endpoint → Estimand → data → PlannedActivity → CollectionSpec is
   explicit (K-PROT-3);
4. check that the concepts, pinned code-system versions and artifacts a protocol names exist (K-SEM-1).

Open questions (#4808):

1. What shape the content takes, and how strictly it is read.
2. How element ids are formed and kept across versions.
3. How versions are diffed.
4. Where the K-PROT-3 and K-SEM-1 checks run, and whether they block.

## 2. Decision Drivers & Constraints

- **Driver 1 (one truth):** no representation is clinical truth (K-TRUTH-1). USDM, the protocol PDF and the CRF set
  are renderings; the kernel holds one shape of its own.
- **Driver 2 (stable identity):** an element keeps its id across versions (K-PROT-1), so a reworded objective is one
  modified element, and lineage can point at an element.
- **Driver 3 (hash stability):** the content hash (ADR-2452) signs exactly what was submitted. Reading must never
  rewrite the stored text.
- **Driver 4 (fail early, never silently):** a malformed document is refused when it is written, with every problem
  listed. Design gaps inform the author but do not block work in progress.
- **Requirements:** PRD-DDF-001 (protocol digitization into arms, epochs, eligibility criteria and biomedical
  concepts): these are typed elements of the design. PRD-CRF-012 (version pinning): a design names concept versions
  and code-system versions, never "latest". PRD-CRF-013 (tenant isolation): reference checks read only the
  submitting tenant's rows.

## 3. Options Considered

### Option 1: A typed kernel design document, with USDM as an adapter (Selected)

- **Overview:** `ProtocolVersion.Content` holds a `ProtocolDesign` JSON document. The kernel reads it strictly with
  source-generated `System.Text.Json`, checks its structure, and derives a diff, findings and external references
  from it. USDM converts in and out through `packages/ddf`.
- **Pros:**
  - ✅ The kernel shape follows spec §6.2 and can grow with it, independent of USDM releases.
  - ✅ Every element kind is a C# record, so handlers and the Canvas share one model, and every element carries the
    `id` the kernel's structural diff matches on.
  - ✅ The stored text is still what was hashed and signed.
- **Cons:**
  - ❌ A USDM round trip needs an adapter with golden tests (a follow-up slice of #4808).

### Option 2: Store USDM v4 JSON as the content

- **Cons:**
  - ❌ Makes an interchange format the kernel's internal shape, which spec §6.2 rules out.
  - ❌ USDM has no place for some spec §6.2 facts (data purpose per item, activity locations beyond a site) and
    changes with each CDISC release.

### Option 3: Relational tables per element kind

- **Cons:**
  - ❌ A version is an immutable snapshot signed by its hash. Rows per element would need their own freeze and
    versioning, and the hash would have to be recomputed from them.
  - ❌ Much more schema for no read the kernel needs yet. Element-level lineage nodes (below) give queries their
    handle without it.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **Justification:** It makes the protocol executable while keeping ADR-2452's snapshot, hash and freeze, and it keeps
  USDM at the boundary where it belongs.

### 4.1 The document

- `ProtocolDesign` (`Cadence.Kernel.Protocol.Design`) has a `schemaVersion` (1), a `title`, and lists of objectives,
  endpoints, estimands, populations, eligibility criteria, arms, epochs, cohorts, interventions, planned activities,
  collection specifications, safety rules and discontinuation rules. The randomization scheme and the statistical
  design are references; the statistical design may name an `ArtifactId` (spec §6.2).
- The JSON is camel case with enums by name. `ProtocolDesignDocument.Write` produces it.
- Concept-based elements carry pinned `ConceptRef`s (`conceptId` and `version`). Collection items may bind a unit
  (UCUM) and a `TerminologyBinding` to a `CodeSystemVersionId` and an optional codelist.
- A `DataPurpose` on an activity, a specification or an item names the endpoints, estimands or safety rules it
  serves, or an operational need.
- Planned activities carry `locations` (site, home, telehealth, local facility, remote) and a timing with windows,
  so decentralised visits are part of the design.

### 4.2 Reading is strict (K-PROT-1)

`ProtocolDesignDocument.Read` refuses, listing every problem at once:

- JSON that is not one object, duplicate properties, unknown properties, missing constructor values, nulls where a
  value is required, enum names that do not exist, and concept references without a concept or a version;
- a missing or unsupported `schemaVersion`, and a blank title;
- element ids that do not match `^[A-Za-z][A-Za-z0-9_.:-]{0,127}$` (USDM ids such as `Objective_1` fit), or that are
  used twice anywhere in the design;
- references to an element that does not exist or is of the wrong kind (an estimand's `populationId` must name a
  population);
- a dose without a positive amount and a unit, negative timing windows, blank units, empty code-system versions and
  empty artifact ids.

`ProtocolVersion.CreateFirst` and `Revise` return `K.Protocol.ContentInvalid` with up to ten problems. The stored
text is the submitted text; the kernel never re-serializes it.

### 4.3 Structural diff

- The diff is the kernel's one structural diff, `ProtocolStructuralDiff` (ADR-2480 §4.6). It needs every element to
  be a JSON object with a unique string `id`; §4.2 guarantees that for every stored design, so it applies to designs
  as they are, and a second diff over the typed model would only disagree with it.
- Elements are matched by id: a reworded objective is one changed element, and an added arm is one added element.
- A collection specification's items are elements of their own, so changing an item changes the item and not its
  specification. The design root (`$root`) changes when an element list gains or loses a member.
- A fork copies its parent's content, so its elements keep their ids until an author changes them.

### 4.4 Design findings (K-PROT-3)

`ProtocolDesignReview.Findings(design)` reports, per element:

- `K-PROT-3.objective-without-endpoint`
- `K-PROT-3.primary-endpoint-without-estimand`
- `K-PROT-3.collection-spec-not-scheduled`: no planned activity collects the specification.
- `K-PROT-3.item-without-purpose`: the item has no purpose of its own, and neither its specification nor any activity
  that collects it has one.

Findings inform and never block. They become Quality `ValidationFinding`s once #4812 lands.

### 4.5 References must exist at submit (K-SEM-1)

- `design.ExternalReferences()` lists every concept version, code-system version and artifact the design names.
- A draft may name what the tenant does not have yet. `SubmitProtocolVersionForReview` checks, in the command
  transaction, that each concept exists at the referenced version or later, and that each code-system version and
  artifact exists in the tenant. The first unknown reference refuses the command with `K.Protocol.ReferenceUnknown`
  (422), and the version stays a draft.
- Reads go through the unit of work, so Row-Level Security limits them to the submitting tenant.

### 4.6 What is left for later slices of #4808

- The USDM v4 adapter in `packages/ddf`, with golden round-trip tests. Until it lands, the DDF compiler takes USDM JSON
  only; its `Compile(ProtocolVersion)` overload is removed because the content is no longer USDM.
- Protocol elements as lineage nodes (`protocol-element:<version>/<id>`), so amendment impact narrows to the objects
  that reference a changed element (ADR-2471 §4.5).
- Eligibility evaluation over Observations with the rule expression language (ADR-2461).
- The K-PROT-2 gate: an amendment cannot be approved until its impact report is attested as reviewed.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - The kernel can diff, review and check protocol versions, and later slices build on one typed model.
  - Simulation (ADR-2480) compares stored versions that are always designs.
  - Authors see every structural problem at once, when they save.
- **Negative Impact / Technical Debt:**
  - Existing free-form content no longer validates. No stored protocol versions depend on it yet; the only caller
    that assumed USDM content (`UsdmCompiler.Compile(ProtocolVersion)`) is removed.
  - The document's schema is versioned by `schemaVersion`. A change that is not additive needs a new version and a
    reader for the old one.
- **Mitigation Strategy:** unit tests pin the document's form and every refusal, and a Postgres test submits a design
  that names real concept, code-system and artifact rows.

## 6. Implementation & Verification

- **Affected Assemblies:**
  - `Cadence.Kernel.Abstractions`: `Protocol/Design/` (`ProtocolDesign` and its element records,
    `ProtocolDesignDocument`, `ProtocolDesignReview`), `ProtocolErrors.ReferenceUnknown`, and
    `[JsonConstructor]` on `ConceptRef` so JSON cannot produce an empty reference.
  - `Cadence.Kernel.Domain`: `ProtocolVersion.CheckContent` reads the design; `ProtocolVersion.Design()`.
  - `Cadence.Kernel.Application`: the reference check in `SubmitProtocolVersionForReviewHandler`.
  - `Cadence.Ddf`: `Compile(ProtocolVersion)` removed, with its reference to `Cadence.Kernel.Domain`.
- **Tracked by:** #4808.
- **Verification Plan:**
  - `ProtocolDesignTests` (unit): round trip of every element kind, strict reading, every structural problem, the id
    length limit, the structural diff over designs (same design, add, remove, change, item versus specification), each
    finding and purpose inheritance, and external references.
  - `ProtocolVersionTests` (unit): content must be a protocol design.
  - `ProtocolDesignPostgresTests` (integration): a design naming registered rows goes into review; unknown concepts,
    later concept versions, unknown code-system versions, unknown artifacts and another tenant's rows are refused, and
    the version stays a draft.
