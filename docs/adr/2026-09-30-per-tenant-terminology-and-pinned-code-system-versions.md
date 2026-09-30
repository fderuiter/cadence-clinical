# ADR-2470: Per-Tenant Terminology, Pinned Code-System Versions and Versioned Concepts

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

ADR-2252 made the Concept Registry the kernel's semantic key: facts and specifications reference a
`ConceptRef(concept, version)`, CDISC Biomedical Concepts are the primary key where one exists, and terminology
packages are versioned. It left three implementation questions open (#4807):

1. **Where public terminology lives.** CDISC CT, UCUM and LOINC are the same content for every customer, but tenant
   isolation (ADR-2250) puts `tenant_id NOT NULL` and forced Row-Level Security on every kernel table (K-ID-1, K-ID-2),
   and licensed content (MedDRA, WHODrug, SNOMED CT) must be loaded per tenant under that tenant's licence (K-SEM-2).
2. **What a concept version is.** A `ConceptRef` must keep meaning the same thing after the concept is edited.
3. **How a package is trusted.** "Nothing references the latest package" needs a concrete, checkable identity for
   each release.

## 2. Decision Drivers & Constraints

- **Driver 1 (isolation):** no exception to K-ID-1/K-ID-2; the RLS policy stays one predicate per table.
- **Driver 2 (governance):** sponsors adopt CT versions on their own schedule and per study; a platform-wide
  "current CT" would change meaning under running studies.
- **Driver 3 (licensing):** licensed content is never shipped and never visible across tenants.
- **Driver 4 (audit):** each loaded release is attributable, reasoned and bound to the publisher's exact bytes.

## 3. Options Considered

### Option 1: Every tenant loads its own releases (Selected)

- **Overview:** `code_systems`, `code_system_versions`, `code_system_entries`, `concepts`, `concept_revisions` and
  `concept_mappings` are ordinary tenant-scoped kernel tables. A tenant adopts a release by loading it through the
  command pipeline (`ImportCodeSystemVersion`), which audits it like any other kernel change.
- **Pros:**
  - ✅ No change to the tenant isolation model; every table gets the standard policy.
  - ✅ Adoption of a release is an explicit, audited decision per tenant, which is how sponsors govern CT.
  - ✅ Open, accepted-terms and licensed systems take the same path; the licence class only gates registration.
- **Cons:**
  - ❌ Public content is stored once per tenant (LOINC is roughly 100k rows per release).

### Option 2: A shared platform tenant readable by all tenants

- **Overview:** public releases live under a reserved tenant; reference tables get a second RLS predicate allowing
  reads of that tenant's rows.
- **Pros:**
  - ✅ Public content stored once.
- **Cons:**
  - ❌ Every reference from tenant data into platform rows breaks the composite `(tenant_id, id)` foreign keys.
  - ❌ Two policies on a family of tables, and a platform writer outside any customer's audit chain.

### Option 3: A separate reference schema outside RLS

- **Cons:**
  - ❌ Licensed systems cannot use it, so there would be two storage paths for one concept.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **Justification:** It keeps isolation and audit uniform and matches how sponsors adopt terminology. Storage is the
  only cost, and it is bounded by the releases a tenant chooses to load. Option 2 stays open as an optimisation if
  measured storage requires it; it would not change the commands or the read port.

Rules:

1. **Pinned versions.** A `CodeSystemVersion` is written once, with all of its entries, and never changes
   (`code_system_versions`, `code_system_entries` are insert-only, SQLSTATE `KA001`). It is unique per tenant by
   `(code system, version label)`. Every read of terminology names the version; the read port has no "latest"
   lookup, and a blank version throws.
2. **Checksum-verified packages.** A release is loaded from the publisher's file by `TerminologyPackageLoader`, which
   refuses a file whose SHA-256 differs from the expected one, parses it (NCI EVS CDISC CT text, `ucum-essence.xml`,
   `Loinc.csv`), and stores the digest, file name, source and format with the version. Loading the same file again
   replays the original import. The digest becomes an Artifact reference once Artifacts exist (spec §8.3).
3. **Licence classes.** `Open` (CDISC CT, UCUM), `AcceptedTerms` (LOINC, RxNorm) and `Licensed` (MedDRA, WHODrug,
   SNOMED CT). Registering a known URI with a more permissive class is refused. The repository ships parsers and
   structure, never content; a unit test fails on licensed release files in the tree (K-SEM-2).
4. **Concept versions.** A concept's label or definition change is a new version in the insert-only
   `concept_revisions`; `concepts` holds the current version and lifecycle status. Deprecation does not create a
   version and never removes one. A CDISC Biomedical Concept is identified by its CDISC concept code, unique per
   tenant; a sponsor concept has none.
5. **Mappings pin both ends.** `ConceptMapping` links one concept version to one code in one code-system version,
   with a relation (exact, broader, narrower, related), a review status (proposed, confirmed, rejected) and evidence.
   Only confirmed mappings resolve an incoming code to a concept. A sponsor concept's mapping state (unmapped,
   proposed, mapped) is derived from its mappings on read, never stored.
6. **Coding verbatim terms** (MedDRA, WHODrug) is a `CodedAs` Assertion carrying a `Coding(code-system version, code)`
   about an Observation (#4729); the registry provides the `Coding` type and the lookups.

## 5. Consequences & Trade-offs

- **Positive Impact:** a `ConceptRef` and a `Coding` mean the same thing forever; ingestion (LOINC-coded labs) and
  design (eCRF fields bound to a Biomedical Concept) resolve to the same `ConceptRef`.
- **Negative Impact / Technical Debt:** per-tenant storage of public content; UCUM unit *expressions* (`mg/dL`) are
  not enumerable, so their validation is a rule (spec §10), not a registry lookup.
- **Mitigation Strategy:** releases load in one command per version; storage is watched once real tenants exist.

## 6. Implementation & Verification

- **Affected Assemblies:** `Cadence.Kernel.Abstractions` (`ConceptRef`, `Coding`, IDs, commands, events,
  `IConceptRegistry`), `Cadence.Kernel.Domain` (`Knowledge/`), `Cadence.Kernel.Application` (`Knowledge/`, including
  the terminology parsers and loader), `Cadence.Kernel.Persistence` (`KnowledgeConfigurations`,
  `KernelConceptRegistry`, migration `ConceptRegistry`).
- **Tracked by:** #4807.
- **Verification Plan:**
  - `ConceptRegistryPostgresTests`: a CDISC CT package loads as one pinned, audited version; a duplicate version is
    refused; a LOINC-coded lab result and an eCRF field for the same Biomedical Concept resolve to the same
    `ConceptRef`; a revised concept's earlier version resolves unchanged; terminology is invisible to other tenants.
  - `ConceptRegistryDomainTests` and `TerminologyParserTests`: version pinning, licence classes, mapping review, the
    three parsers, the checksum check, and the licensed-content repository check.
  - `KernelTenantIsolationPostgresTests` and `InsertOnlyTables_RejectUpdateDeleteAndTruncate` cover the new tables.
