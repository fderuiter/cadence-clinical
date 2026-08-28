---
name: usdm-protocol-designer
description: "Author, modify, and validate CDISC USDM v3/v4 study protocol definitions, Schedule of Activities (SoA) matrices, and Biomedical Concepts in apps/designer. Use when designing clinical trial metadata, editing study arms/epochs/encounters, or validating DDF compiler graph mappings."
license: MIT
metadata:
  author: Cadence Team
  version: "1.0"
---

# CDISC USDM Protocol Designer

Author and validate CDISC Unified Study Data Model (USDM v3/v4) clinical study definitions, Schedule of Activities (SoA) matrices, and Biomedical Concept mappings.

## Domain Model Architecture

- **Designer Backend**: `apps/designer/` (Async Neo4j Python Driver, Pydantic v2 USDM schemas).
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

### 2. SoA Matrix Validation
- Verify encounter-to-activity intersections across arms and epochs.
- Run protocol quality sentinels: circular dependency detection, burden scoring, and synthetic cohort eligibility feasibility.

### 3. Schema & API Contract Synchronization
After adding or updating USDM routes or models in `apps/designer`:
```bash
uv run python scripts/validate_schemas.py --export-dir docs/openapi
uv run pytest tests/test_api_contract_validation.py
```

### 4. Verification
Run designer unit and integration test suites:
```bash
uv run pytest apps/designer/tests/ -v
```
