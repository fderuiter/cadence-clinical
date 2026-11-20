# ADR-2473: CDISC USDM 4 Import into the Protocol Design

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

ADR-2472 made a protocol version's content a typed `ProtocolDesign` and kept CDISC USDM at the boundary: "USDM
converts in and out through `packages/ddf`". Sponsors author protocols in USDM tools and exchange them as the USDM
API `Wrapper` JSON. Until the kernel can read that JSON, an author has to retype a USDM study into the design.

USDM 4 and the protocol design do not have the same shape:

1. USDM holds many versions and designs per study; a protocol version holds one design.
2. USDM names Biomedical Concepts by CDISC concept code; the design names a pinned concept version in the tenant's
   Concept Registry (K-SEM-1, ADR-2470).
3. USDM schedules instances by timings relative to each other (ISO 8601 durations); the design gives each planned
   activity a study day and a window.
4. USDM has facts the design has no place for yet (product codes, population hierarchies, decisions in a
   timeline), and the design has facts USDM does not state (why each item is collected, K-PROT-3).

Open questions (#4808):

1. What the import reads, and how it chooses among versions and designs.
2. How Biomedical Concepts become concept references.
3. How timings become study days.
4. What happens to what does not map.

## 2. Decision Drivers & Constraints

- **Driver 1 (one truth):** the imported result is an ordinary `ProtocolDesign`; nothing downstream knows it came
  from USDM (K-TRUTH-1, ADR-2472).
- **Driver 2 (no silent loss):** anything the import leaves out or approximates is reported, element by element.
- **Driver 3 (fail closed on meaning):** a Biomedical Concept that the tenant does not have is an error, never a
  placeholder, because collection items without a real concept break K-SEM-1 and every derivation after it.
- **Driver 4 (stable ids):** USDM ids become element ids unchanged, so a re-import of a revised USDM study diffs
  element by element against the earlier version (K-PROT-1).
- **Requirements:** PRD-DDF-001 (protocol digitization into arms, epochs, eligibility criteria and biomedical
  concepts), PRD-CRF-012 (pinned versions: the import resolves each concept to its current version and the design
  pins that version), PRD-CRF-013 (tenant isolation: concepts resolve in the importing tenant only).

## 3. Options Considered

### Option 1: A one-way, reporting importer over the USDM 4 JSON (Selected)

- **Overview:** `UsdmDesignImporter` in `packages/ddf` reads the `Wrapper` JSON tolerantly with `JsonDocument`,
  maps what the design has a place for, resolves Biomedical Concepts through `IConceptRegistry`, and returns the
  design with a list of notes, or the problems that stopped it.
- **Pros:**
  - ✅ No generated USDM class model to keep in step with each CDISC release; the importer reads only the properties
    it maps.
  - ✅ Every loss is visible to the author before a draft is created.
- **Cons:**
  - ❌ Not a round trip: export back to USDM is a separate piece of work.

### Option 2: Generate C# classes from the USDM API schema and map typed objects

- **Cons:**
  - ❌ A class per USDM API schema entity for a mapping that reads a fraction of them, regenerated on every USDM
    release.
  - ❌ Strict deserialization of a whole study fails on properties the import does not use.

### Option 3: Import concepts as placeholders and resolve them later

- **Cons:**
  - ❌ A design with placeholder concepts either fails ADR-2472's reading or passes with meaningless references;
    both push the problem to the submit gate (K-SEM-1) where it is harder to fix.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **Justification:** It gives authors a USDM on-ramp now, keeps USDM at the boundary, and never hides a loss.

### 4.1 What is read

- The `Wrapper` object: `usdmVersion` must be 4.x, and `study` must be present.
- One `StudyVersion` and one study design. When the study has several, the caller names them with
  `UsdmImportOptions.StudyVersionId` and `StudyDesignId`; otherwise the import is refused, listing the ids.
- The import fails with every problem at once, and the result is checked with `ProtocolDesignDocument.Read`, so an
  imported design always passes ADR-2472's checks.

### 4.2 How elements map

| USDM 4                                                                                             | Protocol design                                                                                                                                                              |
| -------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Official title, else the first title, else the study name                                          | `title`                                                                                                                                                                      |
| `Objective` and its `endpoints`, level from the level decode                                       | `objectives`, `endpoints`                                                                                                                                                    |
| `Estimand`: `variableOfInterestId`, `analysisPopulationId`, `interventionIds`, `populationSummary` | `estimands`: endpoint, population, `interventionIds`, the endpoint's text as the variable, the summary measure; arms are the arms whose study cells give those interventions |
| `IntercurrentEvent.strategy` (free text)                                                           | a known strategy when the text names one, else `Other` with the text in `strategyText`                                                                                       |
| `StudyDesignPopulation`, `AnalysisPopulation`                                                      | `populations`                                                                                                                                                                |
| `EligibilityCriterion` with its `EligibilityCriterionItem` text                                    | `eligibilityCriteria`                                                                                                                                                        |
| `StudyArm` (type from the decode), interventions from its cells' elements                          | `arms`                                                                                                                                                                       |
| `StudyEpoch`, ordered by `previousId` and `nextId`                                                 | `epochs`                                                                                                                                                                     |
| `StudyCohort`, arms from `StudyArm.populationIds`                                                  | `cohorts`                                                                                                                                                                    |
| `StudyIntervention` and its first administration: dose, route, frequency                           | `interventions` (dose unit in UCUM where the CDISC unit code maps with certainty)                                                                                            |
| `ScheduledActivityInstance` on the main timeline                                                   | `plannedActivities`: name from the encounter, locations from its settings and contact modes, timing per §4.3                                                                 |
| `Activity` and its Biomedical Concepts                                                             | `collectionSpecs`, one item per concept with id `<activity>.<concept>`                                                                                                       |

The design gained two optional properties for this (additive, so `schemaVersion` stays 1): an estimand's
`interventionIds`, checked as references to interventions, and an intercurrent event's `strategyText`, required when
the strategy is `Other`.

### 4.3 Timings become study days

- A `Fixed Reference` timing sets its instance's day to its value (`P1D` is day 1).
- A `Before` or `After` timing places its `relativeFrom` instance that many days before or after its `relativeTo`
  instance. Timings resolve in both directions until nothing changes.
- Study days skip day 0: the day before day 1 is day -1.
- `windowLower` and `windowUpper` become the window before and after.
- Durations are read in whole days. `P…Y` and `P…M` have no fixed length and are refused; a part shorter than a day
  (`PT15M`) is dropped and noted.

### 4.4 Biomedical Concepts

- Each concept's CDISC code (`BiomedicalConcept.code.standardCode.code`) is resolved with
  `IConceptRegistry.FindBiomedicalConceptAsync` in the importing tenant, once per code, and the item pins the
  concept version the registry returns.
- A code the registry does not have is a problem that names the USDM concept; the import produces no design. The
  author loads the concept (ADR-2470) and imports again.
- Callers without a registry pass a `BiomedicalConceptResolver` delegate instead.

### 4.5 What does not map is noted

`UsdmImportResult.Notes` lists, by USDM id: product codes, population `subsetOfIds`, cohort criteria, extra
administrations, doses without a UCUM unit, arm types read as `Other`, settings with no activity location,
decisions and other non-activity instances, timelines other than the main one, parts of durations shorter than a
day, text holding USDM tags (kept as written, not substituted from the study's dictionaries), indications,
biospecimen retentions and notes. Every import also notes that USDM does not say why data is collected, so the
design's items have no purpose until an author adds one (K-PROT-3 findings will say so).

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - A USDM 4 study becomes a draft protocol version in one step, with its losses listed.
  - USDM ids survive as element ids, so versions imported from USDM diff element by element.
- **Negative Impact / Technical Debt:**
  - Import only; export to USDM, and a USDM round trip, are later work.
  - Only the first administration of an intervention and only the main timeline are imported.
  - USDM tags in text are not substituted.
- **Mitigation Strategy:** the golden test pins the whole import of the CDISC example study, notes included, so any
  change in mapping shows up as a reviewed diff of the expected design.

## 6. Implementation & Verification

- **Affected Assemblies:**
  - `Cadence.Ddf`: `UsdmDesignImporter`, `UsdmImportOptions`, `UsdmImportResult`, `UsdmImportNote`,
    `BiomedicalConceptResolver` (public); `UsdmDesignReader`, `UsdmSchedule`, `UsdmDuration` (internal).
  - `Cadence.Kernel.Abstractions`: `Estimand.InterventionIds`, `IntercurrentEvent.StrategyText`,
    `IntercurrentEventStrategy.Other` and their checks in `ProtocolDesignDocument`.
- **Test fixture:** `tests/fixtures/usdm/cdisc_usdm4_full_1.json`, the CDISC example study from `cdisc-org/usdm`
  (MIT), unmodified; its README records the upstream commit and hash.
- **Tracked by:** #4808.
- **Verification Plan:**
  - `UsdmDesignImporterTests` (unit): the CDISC example against a checked-in expected design and notes; its study
    days and windows; each element kind on a small study; intercurrent-event strategies; notes for approximations;
    an unresolved concept failing the import with each code resolved once; the registry overload; refused documents;
    choosing among versions; every problem at once; duration parsing and day-0 skipping.
  - `ProtocolDesignTests` (unit): estimand interventions and `Other` strategies are checked.
  - `ProtocolDesignPostgresTests` (integration): the CDISC example fails to import before its concepts are defined
    in the tenant, then imports through the PostgreSQL Concept Registry and goes into review.
