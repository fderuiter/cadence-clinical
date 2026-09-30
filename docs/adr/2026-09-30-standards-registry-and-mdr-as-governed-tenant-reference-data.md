# ADR-2460: Standards Registry and Metadata Registry as Governed, Tenant-Scoped Reference Data

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-MDR-001

---

## 1. Context & Problem Statement

The kernel treats SDTM, ADaM, SEND, CDASH and Define-XML as representations, not clinical truth (K-TRUTH-1,
spec §6.3). To generate those representations continuously and to validate study designs against them, the
platform needs each standard's definitions as data: which datasets and variables a standard version has, their
types, roles and core status, the codelists they bind and the value-level metadata that narrows them. Mapping
specifications, rule packs and collection specifications then need a governed library: versioned, approved by
someone accountable, deprecated without disturbing the studies that already use them (issue #4809).

None of this exists in code. The Python-era services kept CDISC content as JSON files read at runtime, with no
versioning, no approval and no link from a study to the standard versions it used. Four questions have to be
settled before the mapping engine (#4810), rules (#4811), findings (#4812), design-time conformance (#4813) and the
SDTM projection (#4814) are built on top:

1. Is standards content global or tenant-scoped?
2. Is the Metadata Registry (MDR) one model or a model per kind of library content?
3. How does a standard version bind controlled terminology before the Concept Registry (#4807) lands?
4. How is governance enforced, and how is governed content kept from changing after approval?

## 2. Decision Drivers & Constraints

- **K-PROJ-1:** no kernel type names an SDTM, ADaM or SEND variable. Those names exist only as registry data.
- **K-ID-1 and ADR-2250:** every kernel row carries a tenant and is guarded by row-level security.
- **K-RULE-2:** content implements public standards only. Nothing in the loader adds rules or messages of its own.
- **§6.1:** nothing references "the latest" terminology package; every specification names the version it used.
- **Part 11:** approvals and deprecations are attributable and audited; approved content cannot be altered.
- **Reproducibility (K-PROJ-2):** a projection or validation run must be able to name, and re-read, exactly the
  definitions it used.

## 3. Options Considered

### Option 1: Tenant-scoped registry with one governance envelope for library items (selected)

Each tenant registers the standard versions it uses. A standard version is one aggregate that owns its datasets,
variables, value-level definitions and terminology bindings. Library content (collection specifications, mapping
specifications, rule packs, codelist subsets) shares one `LibraryItem` aggregate whose JSON content is validated
per kind by a pluggable validator.

- **Pros:** RLS, audit chain and tenant deletion work unchanged. A tenant can register a sponsor extension or a
  pre-release version without affecting others. New library kinds need a validator, not a new table.
- **Cons:** the same public content is stored once per tenant.

### Option 2: Global registry shared by all tenants

- **Pros:** one copy of each public standard.
- **Cons:** a second, non-tenant table family outside RLS and outside the per-tenant audit chain; a tenant's
  approval would govern every tenant; sponsor extensions would need a separate model anyway.

### Option 3: A separate aggregate and table per library kind

- **Pros:** typed columns for each kind.
- **Cons:** governance (versioning, approval, deprecation, pinning) is written four times, and each new kind
  needs a migration before it can be governed.

## 4. Decision Outcome

**Chosen option: Option 1.** Storage cost is small (SDTMIG v3.4 DM, AE, VS and LB are 192 variables) and the
tenant boundary stays uniform across the kernel.

### 4.1 Model

| Aggregate | Owns | Governance |
| --- | --- | --- |
| `StandardVersion` | datasets, variables, value-level definitions, terminology bindings | Draft → Approved → Deprecated |
| `LibraryItem` | one version of a keyed item (`Kind`, `Key`, `Version`) with canonical JSON content | Draft → Approved → Deprecated |
| `StudyStandardsProfile` | a study's declared standard versions, CT versions and library pins | revised in place, one per study |

- A standard version is identified by `(tenant, standard code, version)`; registering the same pair twice fails.
- Its **content hash** is the SHA-256 of the canonical JSON of the normalized package (datasets and variables by
  ordinal, keys sorted). `ExpectedContentHash` lets a loader pin the exact content it expects.
- A library item's next version is the key's highest version plus one; drafting never edits an earlier version.
- A study profile accepts only **approved** standard versions and library items, one version per standard code,
  one version per terminology code system and one pin per `(kind, key)`. Deprecation stops new pins but never
  changes a study that already pinned the item.

### 4.2 Terminology binding before the Concept Registry

A binding is the natural key `CodeSystemVersionRef(CodeSystem, Version)`, for example `("sdtmct", "2024-09-27")`,
read from CDISC Library codelist links (`/mdr/ct/packages/sdtmct-2024-09-27/codelists/C66741`). When the Concept
Registry lands (#4807), bindings resolve to `CodeSystemVersionId` through
`IConceptRegistry.FindCodeSystemVersionAsync(uri, version)`. The natural key is kept because it is what a standard
publishes and what an auditor reads.

### 4.3 Governance and immutability

- Approve requires an Attestation with meaning `Approved`; deprecate requires one with meaning `Responsibility`.
  Both are checked through the `IAttestationVerifier` port with the target's kind, id and content hash, so a
  signature over different content is rejected. The Trust engine implements the port with #4729 and #4781; hosts
  register it.
- Every governance change is an ordinary kernel command: audited, idempotent and attributed (ADR-2449).
- In the database, dataset, variable, value-level and terminology rows are insert-only (`enforce_insert_only`,
  SQLSTATE `KA001`, no `UPDATE` grant). The identity and content columns of `standard_versions` and
  `library_items` are guarded by `enforce_immutable_columns` (SQLSTATE `KS001`); only status, attestation and
  version columns may change.
- Library content is stored as canonical JSON text, not `jsonb`, so the stored bytes re-hash to the recorded hash.

### 4.4 Loading public content

`Cadence.Standards` reads CDISC Library product responses (the contract in `docs/CDISC/cdisc-library-api.json`)
into a `StandardPackage`. It references `Cadence.Kernel.Abstractions` only. Shipped seeds (SDTMIG v3.4 DM, AE, VS,
LB; ADaMIG v1.3 ADSL) are embedded resources listed in a manifest with their SHA-256, verified before parsing.
Downloaded content goes through the same `ChecksumVerifier`.

## 5. Consequences & Trade-offs

- **Positive Impact:** mapping specifications, rule packs and findings can name exact, approved, hash-identified
  definitions. A study's standards are a governed, audited declaration instead of a configuration file.
- **Negative Impact / Technical Debt:** terminology bindings are strings until the Concept Registry lands; public
  content is duplicated per tenant.
- **Mitigation Strategy:** a follow-up issue resolves bindings to `CodeSystemVersionId` once #4807 merges. If
  per-tenant duplication becomes material, a shared read-only catalogue can seed tenants without changing this
  model.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/kernel` (Abstractions `Cadence.Kernel.Standards`; Domain,
  Application and Persistence `Standards/`; migration `StandardsRegistry`), new `packages/kernel/src/Cadence.Standards`.
- **Verification Plan:**
  - unit tests: seed checksums, CDISC Library parsing, content hashing, governance transitions, study pinning rules;
  - PostgreSQL integration tests: register and approve are audited and read back exactly; approval without an
    accepted attestation fails; registration is once per tenant and tenant-scoped; pins survive deprecation; the
    database rejects changes to registered definitions (`KA001`, `KS001`);
  - architecture tests: `Cadence.Standards` references only `Cadence.Kernel.Abstractions`; no identifier or string
    literal in kernel or standards source names a shipped dataset or variable (K-PROJ-1).
