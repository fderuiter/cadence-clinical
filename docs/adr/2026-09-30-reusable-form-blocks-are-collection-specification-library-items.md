# ADR-2503: Reusable Form Blocks Are Collection Specification Library Items

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-MDR-001 (Biomedical Concepts linked to eCRF fields; its three range tiers are left to rule-pack edit checks, see §4)
- **Issue:** #4789

---

## 1. Context & Problem Statement

The Python reference `packages/ddf/smart_blocks.py` held a hard-coded catalogue of form blocks: a vital signs panel
and an adverse event log. It turned a block into a "tripartite" form definition (data, UI and logic schemas) with CDASH
item OIDs, SDTM domain codes, 12-column widget layouts and min/max ranges on each field. Nothing in C# replaced it.

The kernel specification already has a home for reusable collection design. The Metadata Registry is "a reusable,
governed library of CollectionSpecs, mappings and rules" (§6.3), and `LibraryItemKind.CollectionSpecification`
exists. However, nothing defined or checked the content of such an item: any JSON object could be drafted and approved.
A protocol design's `CollectionSpec` binds each item to a versioned concept (K-SEM-1), and no kernel type may name
SDTM variables (K-PROJ-1).

## 2. Decision Drivers & Constraints

- A block is governed, versioned content, not code: it must be drafted, approved with an attestation and pinned like
  any other library item.
- The same shipped block must work in any tenant, and each tenant's concepts have their own ids.
- K-SEM-1: every collected item names a concept at a version. K-PROJ-1: SDTM and CDASH names live only in standards
  data and mapping specifications.
- Content that cannot be used must be refused when it is drafted, not when a study tries to use it.

## 3. Options Considered

### Option 1: Port the Python catalogue and tripartite model as-is

- **Cons:**
  - ❌ A second form model beside the kernel's `CollectionSpec`, with SDTM domains and CDASH OIDs inside it
    (against K-PROJ-1).
  - ❌ Blocks in code cannot be governed or versioned per tenant.

### Option 2: Blocks as `CollectionSpecification` library items keyed by Biomedical Concept codes (Selected)

- **Overview:**
  - The content names each item's CDISC Biomedical Concept by its public C-code, with an optional UCUM unit and a
    pinned terminology reference.
  - Instantiating resolves those codes in the tenant's Concept Registry, the way `UsdmDesignImporter` resolves USDM
    Biomedical Concepts, and produces a protocol design `CollectionSpec`.
- **Pros:**
  - ✅ One model for collection design, and one governance envelope for reusable content.
  - ✅ Shipped content is public and portable across tenants.
- **Cons:**
  - ❌ Layout and plausibility ranges are not part of the block.

### Option 3: Library content holding tenant `ConceptRef`s

- **Cons:**
  - ❌ Shipped blocks could not be written once for every tenant.

## 4. Decision Outcome

- **Chosen Option:** Option 2.
- **Content format** (`CollectionSpecificationJson`, in `Cadence.Standards`):
  - `name` is required and `description` is optional.
  - `items` is a non-empty array of at most 500 items. Each item has:
    - `id`: starts with a letter, at most 63 characters, unique within the block;
    - `concept`: a C-code;
    - `unit`: optional, a UCUM code with no spaces;
    - `terminology`: optional, with `codeSystem` (an absolute URI), `version` and an optional codelist C-code;
    - `required`: optional, `true` or `false`.
  - Unknown or repeated properties are problems, and every problem is reported.
- **Drafting:** `CollectionSpecificationContentValidator` is the `ILibraryContentValidator` for the kind.
  `AddCadenceStandards()` registers it, so `DraftLibraryItem` refuses invalid content with `LibraryItemInvalid`.
- **Instantiation** (`CollectionSpecificationInstantiator`):
  - Each concept code resolves to the tenant's concept version.
  - Each terminology reference resolves to its code-system version, and a named codelist must have entries in it.
  - Anything missing is a problem. Nothing is guessed, and nothing is built unless everything resolves.
  - Item ids become `{specId}.{itemId}`, so one block can appear more than once in a design.
- **Shipped blocks** (`FormBlocks`):
  - The blocks are embedded in `Cadence.Standards` and checksum-verified against `Blocks/manifest.json`, like the
    standards seeds.
  - The first block is vital signs: systolic and diastolic blood pressure, pulse rate, respiratory rate and body
    temperature. Its concept codes are checked against SDTM CT 2024-09-27 (VSTESTCD, C66741).
- **Not carried forward from the Python:**
  - SDTM domain codes and CDASH OIDs belong to mapping specifications (#4810).
  - Widget layout belongs to form design (ADR-2502 appearance).
  - Min/max ranges are rules: write them as rule-pack edit checks in the one rule language (K-RULE-3).
  - The adverse event log is not shipped. An adverse event is one Observation of the AE concept with qualifiers, and
    a block with one concept per item cannot express that. It waits for the safety application's design.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Library items of kind `CollectionSpecification` now have a checked schema.
  - Reusable collection design is governed, attested and pinned like rule packs and indicator sets.
- **Negative Impact / Technical Debt:**
  - A host that drafts collection specifications must call `AddCadenceStandards()`, as it calls `AddCadenceRules()`
    for rule packs.
  - Only the vital signs block ships.
- **Mitigation Strategy:**
  - The integration fixture registers the validator, and the README says which hosts need it.
  - More blocks are data changes: a file and a manifest line.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/kernel/src/Cadence.Standards`: `Blocks/`,
  `StandardsServiceCollectionExtensions.cs`.
- **Verification Plan:**
  - `FormBlockTests` in `tests/Cadence.UnitTests` covers the content format, the shipped block, instantiation and
    registration.
  - `FormBlockPostgresTests` in `tests/Cadence.IntegrationTests` drafts, approves and instantiates the vital signs
    block into a protocol draft, and shows that invalid content is refused.
