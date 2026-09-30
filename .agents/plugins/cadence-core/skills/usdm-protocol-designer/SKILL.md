---
name: usdm-protocol-designer
description: "Author, modify, and validate CDISC USDM v3/v4 study protocol definitions, Schedule of Activities (SoA) matrices, and Biomedical Concepts in apps/designer. Use when designing clinical trial metadata, editing study arms/epochs/encounters, or validating DDF compiler graph mappings."
license: MIT
metadata:
  author: Cadence Team
  version: "1.0"
---

# CDISC USDM Protocol Designer

Author and validate CDISC Unified Study Data Model (USDM v3/v4) clinical study definitions, Schedule of Activities (SoA) matrices, and Biomedical Concept mappings using Antigravity design and validation tools.

## Antigravity Tool Execution Matrix

| Phase | Antigravity Tool | Invocation Pattern | Objective |
| :--- | :--- | :--- | :--- |
| **Schema Discovery** | `grep_search` / `view_file` | Query USDM models in `apps/designer/domain/` | Inspect entity structures, relations, and enums |
| **Visual Wireframing** | `generate_image` | `Prompt="High-density Schedule of Activities (SoA) matrix table..."`, `AspectRatio="16:9"` | Generate visual matrix layouts for clinical review |
| **Interactive Design** | `ask_question` | `questions=[{"question": "Select Study Design Phase", "options": [...]}]` | Prompt user for clinical trial parameters and arm branches |
| **Model Authoring** | `replace_file_content` | Target: `apps/designer/domain/...`, surgical model updates | Add or modify USDM entities with strict Pydantic v2 types |
| **Artifact Emission** | `write_to_file` | Target: `<appDataDir>/brain/<conversation-id>/study_protocol_spec.md` | Emit structured USDM JSON and protocol definition artifact |
| **Schema Export & Parity**| `run_command` | `CommandLine="dotnet cadence check"` | Re-verify architecture contracts and sentinel parity |

## Domain Model Architecture

- **Designer Backend**: `apps/designer/` (Async Neo4j .NET 10 Driver, CDISC USDM schemas).
- **Core Entities**:
  - `Study`: The root clinical trial definition.
  - `StudyVersion` / `StudyProtocol`: Versioned protocol representation with amendment indexing.
  - `StudyArm`: Distinct treatment groups or cohorts.
  - `StudyEpoch`: Protocol phases (Screening, Treatment, Follow-up).
  - `Encounter`: Scheduled or unscheduled clinical visits.
  - `Activity`: Clinical procedures, assessments, or interventions.
  - `BiomedicalConcept`: Standardized clinical data element definitions mapping to CDASH/SDTM.

## Authoring Workflows

### 1. Authoring Protocol Entities
When creating or modifying study entities in `apps/designer`:
- Ensure all models strictly use Pydantic v2 with explicit type annotations (no `Any`).
- Adhere to immutable graph branching patterns: historical published versions remain immutable; amendments branch via predecessor relationships.
- Preserve 21 CFR Part 11 audit properties: `created_at`, `created_by`, `reason_for_change`, `version_index`.

### 2. SoA Matrix Validation & Visual Generation
- Verify encounter-to-activity intersections across arms and epochs.
- Use `generate_image` to create visual SoA diagrams and visit schedule timelines for clinical operations reviews.
- Run protocol quality sentinels: circular dependency detection, burden scoring, and synthetic cohort eligibility feasibility.

### MANDATORY TOOL INVOCATION (`ask_question`)

When confirming protocol design choices or arm branching, you **MUST** call `ask_question`:
```json
{
  "questions": [
    {
      "question": "Select Study Arm & Epoch Configuration",
      "options": [
        "(Recommended) 2-Arm Parallel Active-Controlled with 4-Week Screening Epoch",
        "Single-Arm Open-Label Dose Escalation with Sentinel Dosing Cohort",
        "Crossover 2x2 Factorial Design with 2-Week Washout Window"
      ],
      "is_multi_select": false
    }
  ]
}
```


### 3. Schema & API Contract Synchronization
After adding or updating USDM routes or models in `apps/designer`:
```bash
dotnet cadence check
dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj
```

### 4. Verification
Run designer unit and integration test suites:
```bash
dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Designer"
```

