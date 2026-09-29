# ADR-2252: Concept Registry with CDISC Biomedical Concepts as the Semantic Key (K005)

- **Status:** Accepted
- **Date:** 2026-09-29
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The kernel stores clinical facts in terms of meaning, not a standard's table layout (spec §0.1 K-TRUTH-1,
K-PROJ-1). "Serum potassium" must be one concept that an eCRF field, an SDTM LB record, a LOINC-coded lab feed,
a FHIR Observation and an ADaM parameter all point to. Without a shared concept identity, every product and
every standard grows its own code list, and mapping becomes a late, manual clean-up step at database lock
(PRD-MDR-001, PRD-DDF-001).

Candidate primary vocabularies are CDISC Biomedical Concepts (BCs), LOINC, SNOMED CT, or a Cadence-owned
ontology. Coding dictionaries (MedDRA, WHODrug) and terminologies (UCUM, RxNorm, ICD, NCIt/CDISC CT) must also
fit.

## 2. Decision Drivers & Constraints

- **Driver 1 (standards alignment):** CDISC standards (CDASH, SDTM, USDM) are the regulated submission path; concepts must map to them without loss.
- **Driver 2 (external interoperability):** labs, EHRs and devices speak LOINC, SNOMED CT, UCUM and RxNorm.
- **Driver 3 (versioning):** every fact, specification and rule must name the exact terminology version it used.
- **Driver 4 (licensing):** MedDRA, WHODrug and SNOMED CT content is licensed; the platform cannot redistribute it.

## 3. Options Considered

### Option 1: Concept Registry keyed by CDISC Biomedical Concepts, with mapped external codes (Selected)

- **Overview:** `Concept` is the kernel's semantic key. Where a CDISC BC exists it is the concept's identity;
  `ConceptMapping` links it to LOINC, SNOMED CT, UCUM, RxNorm, NCIt with a relation (exact/broader/narrower/
  related) and status. Sponsor concepts are allowed in the tenant namespace with a mapping status. Code-system
  versions are Artifacts. Coding a verbatim term with MedDRA/WHODrug is an Assertion (`CodedAs`).
- **Pros:**
  - ✅ BCs already carry the CDASH/SDTM linkage that makes design-time conformance possible (K-STD-1/2).
  - ✅ External codes are first-class mappings, so ingestion normalises to one concept.
  - ✅ Sponsor extensions are possible without forking a vocabulary.
- **Cons:**
  - ❌ BC coverage is incomplete; sponsor concepts will be common early on.

### Option 2: LOINC/SNOMED CT first

- **Overview:** Use LOINC for observations and SNOMED CT for everything else as primary keys.
- **Pros:**
  - ✅ Broad coverage and strong EHR interoperability.
- **Cons:**
  - ❌ No direct link to CDASH/SDTM; mapping to submission standards stays a separate, late activity.
  - ❌ SNOMED CT licensing varies by country, which complicates a primary key.

### Option 3: Cadence-owned ontology

- **Pros:**
  - ✅ Full control.
- **Cons:**
  - ❌ Recreates work CDISC and others already publish; every mapping becomes ours to maintain.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **Justification:** It puts the submission path and the interoperability path on one concept identity, keeps
  versions explicit, and supports the "mapping exists before first data capture" principle.

Rules: every Observation, CollectionSpec item, PlannedActivity assessment and variable mapping references a
`ConceptRef(concept, version)` (K-SEM-1). Licensed content is loaded per tenant under the tenant's licence
(K-SEM-2). Nothing references "latest" terminology.

## 5. Consequences & Trade-offs

- **Positive Impact:** AI and lineage queries can traverse by meaning; amendment impact and design-time validation
  work on concepts, not field names.
- **Negative Impact / Technical Debt:** Existing `ClinicalObservation` rows keyed by SDTM `DomainCode`/`TestCode`
  need a concept backfill during migration slice S7.
- **Mitigation Strategy:** Backfill maps `(domain, testcd)` to BCs through the registry's own mappings; unmapped
  rows get sponsor concepts flagged for review.

## 6. Implementation & Verification

- **Affected Assemblies:** `Cadence.Kernel.Abstractions` (`ConceptRef`), `Cadence.Kernel.Domain` (registry
  aggregates), `Cadence.Standards.*` (loaders for CDISC Library content and CT packages).
- **Tracked by:** roadmap phase 2 (spec §15); #4729 depends on `ConceptRef`.
- **Verification Plan:**
  - Architecture test: kernel types expose `ConceptRef`, never SDTM variable names (K-PROJ-1).
  - Unit tests: a LOINC-coded lab feed and an eCRF field for the same BC resolve to the same `ConceptRef`.
