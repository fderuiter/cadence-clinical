# ADR-2504: The Tripartite Form Definition Is Not Carried Forward

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-DDF-001 (the form compiled from the protocol design); PRD-MDR-001 (Biomedical Concepts linked to eCRF fields)
- **Amends:** [ADR-2231](2026-08-28-ai-enhanced-cdisc-native-visual-metadata-ide-and-tripartite-ecrf-designer-standard.md) (its tripartite form model)
- **Issue:** #4789

---

## 1. Context & Problem Statement

[ADR-2231](2026-08-28-ai-enhanced-cdisc-native-visual-metadata-ide-and-tripartite-ecrf-designer-standard.md) made
the "tripartite" JSON document the form model: a data schema, a UI schema and a logic schema. The Python reference
`packages/ddf/tripartite.py` holds its Pydantic shapes, together with a patient migration matrix and a UAT evidence
report. The file has no behaviour, and no C# code reads these shapes. The web's generated Designer types still name
them, from the retired Python Designer API; #4829 hides screens whose module has no .NET backend.

Since then, the kernel specification v0.2 gave each of these concerns its own home, and most of them now exist.
Porting the tripartite model would add a second form model beside the kernel's, with SDTM names inside it (against
K-PROJ-1) and a rule format beside the one rule language (against K-RULE-3).

## 2. Decision Drivers & Constraints

- K-TRUTH-1 and the forbidden redefinitions (spec §2.3): no application copy of protocol design or collection
  meaning.
- K-SEM-1: collected items name concept versions. K-PROJ-1: no SDTM or CDASH names in kernel types.
- K-RULE-3: one rule language for edit checks, skip logic and conformance rules.
- A Python reference file leaves the inventory when a tested port exists or an ADR records that the behaviour is not
  carried forward (ADR-2492).

## 3. Options Considered

### Option 1: Port the tripartite model to C#

- **Cons:**
  - ❌ It is a second form model and a second rule format, and it carries SDTM names.

### Option 2: Record where each part now lives, and port nothing (Selected)

- **Pros:**
  - ✅ One model per concern, each already governed and tested in the kernel or an application.
- **Cons:**
  - ❌ The Designer UI must be rebuilt on the kernel's shapes rather than reusing its old JSON.

## 4. Decision Outcome

- **Chosen Option:** Option 2. `tripartite.py` is not ported. Each of its parts maps to an existing home:

| Tripartite part                                              | Where it lives now                                                                                                                                                                                                                           |
| ------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Data schema variables (`VariableDefinition`, `DataSchema`)   | Protocol design `CollectionSpec` items bound to concept versions (K-SEM-1). Reusable ones are `CollectionSpecification` library items ([ADR-2503](2026-09-30-reusable-form-blocks-are-collection-specification-library-items.md))            |
| `CdiscMetadata` (domain, OID, SDTM target, core designation) | Standards Registry data and mapping specifications (#4810), never the collected item (K-PROJ-1)                                                                                                                                              |
| `ConstraintDefinition`                                       | `required` is `CollectionItem.Required`, and allowed values are a `TerminologyBinding`. Ranges, lengths and patterns are rule-pack edit checks (K-RULE-3, ADR-2461)                                                                          |
| `phi_pii_masked`                                             | Direct identifiers live in the IdentityVault ([ADR-2253](2026-09-29-identity-vault-and-layered-participant-identity.md)) and are never form data                                                                                             |
| `sdv_tier`                                                   | The monitoring plan's SDV strategy in the Quality engine (K-QBD-2). A verification is a `Verified(SDV)` Assertion                                                                                                                            |
| UI schema (sections, widgets, viewports)                     | Form design in the DDF compiler: appearance and item text ([ADR-2502](2026-09-30-ddf-item-rules-use-the-one-rule-language.md)). It is presentation, so it stays out of the kernel (K-SCOPE-1)                                                |
| `AstEditCheck`                                               | Rule-pack rules in the one rule language (ADR-2461), with findings (ADR-2462)                                                                                                                                                                |
| `DerivedMathRule`                                            | Derived variables (#5043)                                                                                                                                                                                                                    |
| `CrossVisitDependency`                                       | The protocol design's schedule and `ExpectedSchedule` (ADR-2487), with conditional visits in #5045                                                                                                                                           |
| Version, `created_by` and `reason_for_change` fields         | The ProtocolVersion lifecycle and the kernel audit log                                                                                                                                                                                       |
| `PatientMigrationMatrix`                                     | Effectivity ([ADR-2456](2026-09-30-effectivity-and-study-site-activation.md)) and Applicability (spec §7.5): which protocol version governs a participant, and when. Missing data after a retrospective change is a finding, not a directive |
| `SyntheticPayload` and `UatEvidenceReport`                   | Evaluate-only simulation over candidate versions ([ADR-2480](2026-09-30-evaluate-only-simulation-over-candidate-versions.md)), with validation runs as the evidence                                                                          |

- ADR-2231's product direction still stands: a visual designer, forms as data rather than DDL, one rule language that
  runs the same everywhere, amendments without downtime, and automated qualification. Only its tripartite document is
  replaced by the kernel's shapes above.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - One model per concern.
  - `tripartite.py` can be deleted once a maintainer approves.
- **Negative Impact / Technical Debt:**
  - The web's generated Designer types that name tripartite shapes are dead until the Designer screens move onto the
    kernel API.
- **Mitigation Strategy:** the web epic (#4833) and #4829 already track moving screens onto .NET backends.

## 6. Implementation & Verification

- **Affected Repositories / Services:** documentation only. This ADR, the ADR-2231 note,
  `docs/PYTHON_REFERENCE_INVENTORY.md`, the ddf README and CONTEXT.md.
- **Verification Plan:** `pnpm cadence check adrs` and the documentation gates. Each home in the table is covered by
  the tests of its own ADR or issue.
