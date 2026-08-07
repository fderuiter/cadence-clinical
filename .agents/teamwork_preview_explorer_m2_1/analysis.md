# Designer Domain Models Investigation & Mapping Analysis

## Overview
This document presents the detailed investigation and structural mapping of all **Designer Domain Models** currently located in `packages/core-models/`. It catalogs exact source files, defined classes/functions, repository-wide import sites, target destination paths under `apps/designer/src/domain/`, and potential import conflicts or circular dependency risks.

---

## 1. Inventory of Designer Domain Model Source Files & Symbols

| Domain Category | Source File Path in `packages/core-models/` | Classes, Functions, Enums, and Constants Defined |
|---|---|---|
| **USDM Domain** | `cdisc/usdm_models.py` | Classes: `Code`, `SyntaxTemplate`, `EligibilityCriterion`, `Activity`, `Encounter`, `StudyArm`, `StudyEpoch`, `StudyDesign`, `USDMStudy` |
| **USDM Transport** | `cdisc/usdm_transport_models.py` | Classes: `UsdmImportRequest`, `UsdmImportResponse`, `UsdmExportResponse` |
| **USDM Importer** | `cdisc/usdm_importer.py` | Classes: `USDMImportResult`, `USDMImporter` |
| **Branching & Diff** | `cdisc/branch_models.py` | Classes: `ProtocolBranch`, `BlockDiff`, `AmendmentComparisonResponse` |
| **Cascade Engine** | `cdisc/cascade_models.py` | Classes: `CascadedFormTemplate`, `CascadeSummaryReport` |
| **CDISC Library Client** | `cdisc/cdisc_library_client.py` | Classes: `CdiscLibraryConfig`, `CdiscProductSummary`, `CdashDomainDefinition`, `SdtmDomainDefinition`, `CodelistTerm`, `CodelistDefinition`, `CdiscLibraryClient`<br>Constants: `REPO_ROOT`, `LOCAL_CDISC_DIR` |
| **Quality Sentinel** | `cdisc/sentinel_models.py` | Classes: `QualityRuleFinding`, `ReadabilityReport`, `BurdenTraceItem`, `BurdenTraceReport`, `AmendmentImpactReport`, `AttritionStep`, `FeasibilityReport`, `ProtocolQualityScore` |
| **Terminology Cache** | `cdisc/terminology_cache.py` | Class: `CdiscTerminologyCache`<br>Constant: `DEFAULT_CACHE_DB_PATH` |
| **CDISC Package Init** | `cdisc/__init__.py` | Re-exports all USDM, library client, and terminology cache symbols |
| **Synopsis Transport** | `designer/synopsis_transport_models.py` | Classes: `SynopsisExportRequest`, `SynopsisExportResponse` |
| **USDM Ingestion** | `usdm_ingestion.py` | Classes: `ValidationIssue`, `USDMValidationReport`, `FieldReference`, `ExpressionNode`<br>Functions: `extract_field_references`, `detect_circular_dependencies`, `safe_parse_payload`, `resolve_usdm_version`, `normalize_usdm_payload`, `traverse_rules_in_payload`, `detect_stochastic_operators`, `validate_usdm_payload` |
| **Protocol Authoring Models** | `protocol_authoring/models.py` | Enums: `BlockType`, `SectionReviewStatus`, `SuggestionStatus`<br>Classes: `ProtocolBlock`, `NarrativeBlock`, `ObjectiveBlock`, `EligibilityBlock`, `SoADerivedBlock`, `ICHSection`, `Comment`, `CommentThread`, `Suggestion`, `SectionReviewTransition`<br>Type Alias: `ProtocolBlockUnion`<br>Functions: `build_canonical_ich_skeleton`<br>Constants: `CANONICAL_ICH_SKELETON` |
| **Protocol Authoring SoA** | `protocol_authoring/soa.py` | Classes: `StudyArm`, `Epoch`, `Visit`, `Procedure`, `TimingWindow`, `StudyArmProperties`, `EpochProperties`, `VisitProperties`, `ProcedureProperties`, `TimingWindowProperties`, `CreateStudyArmRequest`, `UpdateStudyArmRequest`, `CreateEpochRequest`, `UpdateEpochRequest`, `CreateVisitRequest`, `UpdateVisitRequest`, `CreateProcedureRequest`, `UpdateProcedureRequest`, `CreateTimingWindowRequest`, `UpdateTimingWindowRequest`, `LinkEpochVisitRequest`, `LinkVisitProcedureRequest`, `LinkTimingRequest`, `LinkArmApplicabilityRequest`, `SoALinkResponse`, `SoAEntityCreatedResponse`, `SoAEntityDetail`, `AuditMetadata`, `ProjectionCell`, `SoAMatrixProjectionResponse`, `VisitReorderItem`, `VisitReorderRequest`, `ActivityAssignmentRequest`, `ArmReorderItem`, `ArmReorderRequest`, `EpochReorderItem`, `EpochReorderRequest`, `ProcedureReorderItem`, `ProcedureReorderRequest`, `VisitToArmAssignmentRequest`, `VisitToEpochAssignmentRequest` |
| **Protocol Authoring Init** | `protocol_authoring/__init__.py` | Re-exports symbols from `models.py` and `soa.py` |
| **Protocol Render Models** | `protocol_render/models.py` | Classes: `ExportMetadata`, `NarrativeItemView`, `NarrativeSectionView`, `SynopsisView`, `SoAHeaderArm`, `SoAHeaderEpoch`, `SoAHeaderEncounter`, `SoACellView`, `SoARowView`, `SoAMatrixView`, `RenderedProtocolDocument` |
| **Protocol Render Init** | `protocol_render/__init__.py` | Re-exports symbols from `models.py` |
| **Protocol Version Ref Models** | `protocol_version_ref/models.py` | Enum: `ProtocolVersionStatus`<br>Class: `ProtocolVersionRef` |
| **Protocol Version Ref Init** | `protocol_version_ref/__init__.py` | Re-exports `ProtocolVersionRef`, `ProtocolVersionStatus` |
| **Eligibility Models** | `eligibility/models.py` | Enums: `ComparisonOperator`, `LogicalOperator`<br>Classes: `FieldReference`, `ExpressionNode`, `EligibilityCriterion`, `NodeEvaluation`, `CriterionEvaluation`, `AggregateEligibilityResult`<br>Regex: `FIELD_REF_RE` |
| **Eligibility Evaluator** | `eligibility/evaluator.py` | Functions: `evaluate_node`, `evaluate_eligibility`, `evaluate_structured_expression`, `evaluate_criteria_group` |
| **Eligibility Parser** | `eligibility/parser.py` | Classes: `Token`, `DSLParser`<br>Functions: `tokenize`, `parse_dsl`<br>Constant: `TOKEN_SPEC` |
| **Eligibility Init** | `eligibility/__init__.py` | Re-exports symbols from `models.py`, `evaluator.py`, `parser.py` |
| **Document Renderer** | `document_renderer.py` | Class: `ProtocolDocumentRenderer` (methods: `render_pdf`, `render_docx`) |

---

## 2. Target Destinations under `apps/designer/src/domain/`

To adhere to the `apps/designer/src/domain/` layout convention specified in `PROJECT.md` Feature #2, each source file is mapped to its target location as follows:

| Current File Path (`packages/core-models/`) | Target Destination Path (`apps/designer/src/domain/`) | Sub-domain Module |
|---|---|---|
| `cdisc/usdm_models.py` | `apps/designer/src/domain/usdm/usdm_models.py` | USDM core graph models |
| `cdisc/usdm_transport_models.py` | `apps/designer/src/domain/usdm/usdm_transport_models.py` | USDM API transport models |
| `cdisc/usdm_importer.py` | `apps/designer/src/domain/usdm/usdm_importer.py` | USDM graph ingestion service |
| `cdisc/branch_models.py` | `apps/designer/src/domain/amendments/branch_models.py` | Amendment branching & diff models |
| `cdisc/cascade_models.py` | `apps/designer/src/domain/cascade/cascade_models.py` | eCRF / SoA cascade propagation models |
| `cdisc/cdisc_library_client.py` | `apps/designer/src/domain/cdisc/cdisc_library_client.py` | CDISC library API client |
| `cdisc/sentinel_models.py` | `apps/designer/src/domain/quality/sentinel_models.py` | Protocol quality sentinel & feasibility models |
| `cdisc/terminology_cache.py` | `apps/designer/src/domain/cdisc/terminology_cache.py` | CDISC terminology SQLite cache |
| `cdisc/__init__.py` | `apps/designer/src/domain/cdisc/__init__.py` | CDISC package re-exports |
| `designer/synopsis_transport_models.py` | `apps/designer/src/domain/synopsis/synopsis_transport_models.py` | Synopsis export transport DTOs |
| `usdm_ingestion.py` | `apps/designer/src/domain/usdm/usdm_ingestion.py` | USDM ingestion, validation & normalization |
| `protocol_authoring/models.py` | `apps/designer/src/domain/protocol_authoring/models.py` | Protocol authoring blocks & ICH skeleton |
| `protocol_authoring/soa.py` | `apps/designer/src/domain/protocol_authoring/soa.py` | SoA matrix, arms, epochs, visits, procedures |
| `protocol_authoring/__init__.py` | `apps/designer/src/domain/protocol_authoring/__init__.py` | Protocol authoring re-exports |
| `protocol_render/models.py` | `apps/designer/src/domain/protocol_render/models.py` | Presentation view models for rendering |
| `protocol_render/__init__.py` | `apps/designer/src/domain/protocol_render/__init__.py` | Protocol render re-exports |
| `protocol_version_ref/models.py` | `apps/designer/src/domain/protocol_version_ref/models.py` | Protocol version reference DTOs |
| `protocol_version_ref/__init__.py` | `apps/designer/src/domain/protocol_version_ref/__init__.py` | Protocol version ref re-exports |
| `eligibility/models.py` | `apps/designer/src/domain/eligibility/models.py` | Eligibility AST, criteria, evaluation DTOs |
| `eligibility/evaluator.py` | `apps/designer/src/domain/eligibility/evaluator.py` | Deterministic Kleene 3-valued AST evaluator |
| `eligibility/parser.py` | `apps/designer/src/domain/eligibility/parser.py` | Infix clinical DSL lexer & parser |
| `eligibility/__init__.py` | `apps/designer/src/domain/eligibility/__init__.py` | Eligibility package re-exports |
| `document_renderer.py` | `apps/designer/src/domain/document_renderer.py` | PDF and DOCX document rendering pipeline |

---

## 3. Import Site Catalog Across `apps/`, `packages/`, `scripts/`, `tests/`

Every import statement referencing these files or symbols across the codebase has been identified:

### 3.1. USDM & CDISC Models Import Sites
- `apps/designer/importers/usdm_importer.py` (line 1): `from cdisc.usdm_importer import USDMImporter, USDMImportResult`
- `apps/designer/routers/cascade.py` (line 8): `from cdisc.cascade_models import CascadeSummaryReport`
- `apps/designer/routers/quality_sentinel.py` (line 8): `from cdisc.sentinel_models import ProtocolQualityScore`
- `apps/designer/routers/synopsis.py` (line 10): `from designer.synopsis_transport_models import SynopsisExportRequest, SynopsisExportResponse`
- `apps/designer/services/artifact_cascade.py` (line 8): `from cdisc.cascade_models import CascadedFormTemplate, CascadeSummaryReport`
- `apps/designer/services/branch_manager.py` (line 9): `from cdisc.branch_models import ProtocolBranch, BlockDiff, AmendmentComparisonResponse`
- `apps/designer/services/quality_sentinel.py` (line 20): `from cdisc.sentinel_models import ProtocolQualityScore, QualityRuleFinding`
- `apps/gateway/routers/cdisc.py` (lines 11, 19): `from cdisc.cdisc_library_client import ...`, `from cdisc.terminology_cache import CdiscTerminologyCache`
- `apps/gateway/routers/usdm.py` (lines 6, 7): `from cdisc.usdm_importer import USDMImporter`, `from cdisc.usdm_transport_models import UsdmImportRequest, UsdmImportResponse, UsdmExportResponse`
- `apps/quality/tests/test_quality_sentinel.py` (line 390): `from cdisc.sentinel_models import ProtocolQualityScore`

### 3.2. USDM Ingestion Import Sites
- `apps/designer/usdm_ingestion.py` (lines 5-15): Dynamic module loader loading `packages/core-models/usdm_ingestion.py`
- `apps/designer/main.py` (line 150): `from apps.designer.usdm_ingestion import ...`
- `apps/designer/serialization.py` (line 11): `from apps.designer.usdm_ingestion import validate_usdm_payload`
- `apps/execution/translator.py` (line 262): `import usdm_ingestion` (**Cross-Service Import**)
- `packages/core-models/tests/test_usdm_ingestion.py` (line 10): `from apps.designer.usdm_ingestion import ...`
- `scripts/detect_duplication.py` (line 315): Whitelist entry `"packages/core-models/usdm_ingestion.py"`

### 3.3. Protocol Authoring Import Sites
- `apps/designer/adapter/repositories.py` (line 7): `from protocol_authoring.models import ProtocolBlock, ...`
- `apps/designer/delta.py` (line 11): `from protocol_authoring.models import ProtocolBlock, ...`
- `apps/designer/main.py` (line 1038): `from protocol_authoring.models import ProtocolBlock, ...`
- `apps/designer/soa_models.py` (lines 9, 42): `from protocol_authoring import ...`
- `apps/designer/tests/test_protocol_blocks.py` (line 3): `from protocol_authoring import ...`
- `apps/designer/tests/test_protocol_collaboration.py` (line 5): `from protocol_authoring.models import ...`
- `apps/execution/tests/test_shared_soa_models.py` (line 6): `from protocol_authoring import ...`
- `packages/core-models/tests/test_datetime_validation.py` (line 13): `from protocol_authoring.models import Comment`

### 3.4. Protocol Render Import Sites
- `apps/designer/content_assembly.py` (line 13): `from protocol_render import SynopsisView, ...`
- `apps/designer/main.py` (line 49): `from protocol_render import SoAMatrixView`
- `apps/designer/rendering.py` (line 11): `from protocol_render import RenderedProtocolDocument, SoAMatrixView`
- `apps/designer/soa_models.py` (lines 93, 102): `from protocol_render import ...`, `from protocol_render.models import ...`
- `apps/designer/tests/test_protocol_render.py` (line 6): `from protocol_render import ...`
- `packages/core-models/protocol_authoring/soa.py` (line 12): `from protocol_render import SoAHeaderArm, SoAHeaderEncounter, SoAHeaderEpoch, SoARowView`
- `packages/core-models/tests/test_datetime_validation.py` (line 14): `from protocol_render.models import ExportMetadata`
- `scripts/tests/test_content_assembly.py` (line 5): `from protocol_render import RenderedProtocolDocument`

### 3.5. Protocol Version Ref Import Sites
- `apps/designer/tests/test_protocol_version_ref.py` (line 11): `from protocol_version_ref import ProtocolVersionRef, ProtocolVersionStatus`
- `apps/etmf/ingestion.py` (line 3): `from protocol_version_ref import ProtocolVersionRef` (**Cross-Service Import**)
- `apps/etmf/ingestion_service.py` (line 9): `from protocol_version_ref import ProtocolVersionRef` (**Cross-Service Import**)
- `apps/etmf/main.py` (line 17): `from protocol_version_ref import ProtocolVersionRef` (**Cross-Service Import**)
- `apps/execution/main.py` (line 27): `from protocol_version_ref import ProtocolVersionRef` (**Cross-Service Import**)

### 3.6. Eligibility Import Sites
- `apps/designer/main.py` (line 34): `from eligibility import EligibilityCriterion, ExpressionNode, parse_dsl`
- `apps/designer/services/quality_sentinel.py` (lines 33-34): `from eligibility.evaluator import evaluate_node`, `from eligibility.models import ExpressionNode`
- `apps/execution/designer_client.py` (line 6): `from eligibility.models import EligibilityCriterion` (**Cross-Service Import**)
- `apps/execution/eligibility_service.py` (lines 13-14): `from eligibility.evaluator import evaluate_eligibility`, `from eligibility.models import AggregateEligibilityResult` (**Cross-Service Import**)
- `apps/execution/tests/test_execution_eligibility.py` (line 12): `from eligibility.models import EligibilityCriterion` (**Cross-Service Import**)
- `apps/interop/designer_client.py` (line 7): `from eligibility import EligibilityCriterion, ExpressionNode, parse_dsl` (**Cross-Service Import**)
- `apps/interop/main.py` (line 8): `from eligibility import evaluate_eligibility` (**Cross-Service Import**)
- `apps/interop/tests/test_interop_prescreen.py` (line 7): `from eligibility import EligibilityCriterion, parse_dsl` (**Cross-Service Import**)
- `packages/core-models/eligibility/__init__.py` (lines 9, 15, 25): internal relative / package imports
- `packages/core-models/eligibility/evaluator.py` (line 11): `from eligibility.models import ...`
- `packages/core-models/eligibility/parser.py` (line 11): `from eligibility.models import ExpressionNode, FieldReference`
- `scripts/tests/test_eligibility_engine.py` (line 9): `from eligibility import ...`

### 3.7. Document Renderer Import Sites
- `apps/ctms/routers/doa.py` (lines 10, 28): `import document_renderer`, `ProtocolDocumentRenderer = document_renderer.ProtocolDocumentRenderer` (**Cross-Service Import**)
- `apps/designer/renderers/document_renderer.py` (lines 5-21): Dynamic module loader loading `packages/core-models/document_renderer.py`
- `apps/designer/renderers/__init__.py` (line 3): `from apps.designer.renderers.document_renderer import ProtocolDocumentRenderer`
- `apps/designer/routers/synopsis.py` (line 22): `from apps.designer.renderers.document_renderer import ProtocolDocumentRenderer`
- `apps/designer/tests/test_protocol_narrative.py` (line 14): `from apps.designer.renderers.document_renderer import ProtocolDocumentRenderer`
- `scripts/tests/test_document_renderer.py` (line 11): `from apps.designer.renderers.document_renderer import ProtocolDocumentRenderer`
- `tests/validation/prd_compliance_traceability_suite.py` (line 11): `from apps.designer.renderers.document_renderer import ProtocolDocumentRenderer`

---

## 4. Import Conflicts & Circular Dependency Risks

1. **Intra-Designer Dependency: `protocol_authoring.soa` -> `protocol_render`**
   - `packages/core-models/protocol_authoring/soa.py` line 12:
     ```python
     from protocol_render import (
         SoAHeaderArm,
         SoAHeaderEncounter,
         SoAHeaderEpoch,
         SoARowView,
     )
     ```
   - Rationale: `protocol_authoring.soa` imports presentation DTOs from `protocol_render`.
   - Mitigation: Ensure `protocol_render` models are loaded first or use relative/explicit package paths (`apps.designer.src.domain.protocol_render`) when migrating. `protocol_render` does NOT import from `protocol_authoring`, so no circular loop exists between these two modules.

2. **Intra-Designer Dependency: `eligibility.evaluator` / `eligibility.parser` -> `eligibility.models`**
   - Both `evaluator.py` and `parser.py` import `ExpressionNode` and `FieldReference` from `eligibility.models`.
   - Mitigation: Maintain internal relative imports (`from .models import ExpressionNode, FieldReference`) within `apps/designer/src/domain/eligibility/`.

3. **Dynamic Loader Wrappers in `apps/designer/`**
   - `apps/designer/usdm_ingestion.py` and `apps/designer/renderers/document_renderer.py` currently load files dynamically from `packages/core-models/` via `importlib.util.spec_from_file_location`.
   - Mitigation: When `packages/core-models/` is deleted in Milestone M5, these dynamic loader files should be converted to clean standard imports pointing to `apps.designer.src.domain.usdm.usdm_ingestion` and `apps.designer.src.domain.document_renderer`.

4. **Cross-Service Import Boundary Violations (Execution, CTMS, eTMF, Interop)**
   - Microservices outside of `designer` directly import Designer domain models:
     - `apps/execution/translator.py` -> `usdm_ingestion`
     - `apps/execution/main.py` -> `protocol_version_ref`
     - `apps/execution/designer_client.py` & `eligibility_service.py` -> `eligibility`
     - `apps/etmf/ingestion.py`, `ingestion_service.py`, `main.py` -> `protocol_version_ref`
     - `apps/ctms/routers/doa.py` -> `document_renderer`
     - `apps/interop/designer_client.py` & `main.py` -> `eligibility`
   - Mitigation:
     - For Milestone M2 (Domain Migration), update import statements across `apps/execution`, `apps/etmf`, `apps/ctms`, `apps/interop`, and `apps/gateway` to reference `apps.designer.src.domain...` (or maintain compatibility aliases in `apps/designer/src/domain/`) so tests pass cleanly.
     - For Milestone M4 (ACL implementation), these direct cross-service imports will be completely eliminated and replaced by local consumer DTOs under `apps/<service>/src/domain/acl/` calling Designer REST HTTP endpoints.

---

## 5. Summary Matrix & Actionable Plan for Migration Worker

| Step | Task | Action Details |
|---|---|---|
| 1 | Create target directories | Ensure `apps/designer/src/domain/` subdirectories (`usdm/`, `protocol_authoring/`, `protocol_render/`, `protocol_version_ref/`, `eligibility/`, `cdisc/`, `amendments/`, `cascade/`, `quality/`, `synopsis/`) exist. |
| 2 | Move domain files | Copy/relocate source files from `packages/core-models/` to `apps/designer/src/domain/` according to Section 2 target mapping. |
| 3 | Update internal imports | Replace `from cdisc...`, `from eligibility...`, `from protocol_authoring...`, `from protocol_render...`, `from protocol_version_ref...` with internal relative or `apps.designer.src.domain...` imports. |
| 4 | Update consumer import sites | Update all identified import sites in `apps/designer/`, `apps/execution/`, `apps/etmf/`, `apps/ctms/`, `apps/interop/`, `apps/gateway/`, `scripts/tests/`, and `packages/core-models/tests/`. |
| 5 | Verify lint & tests | Run `uv run ruff check .`, `uv run ruff format .`, `python3 scripts/detect_duplication.py`, `uv run pytest -n auto`, `uv run python scripts/sync_gxp.py`. |
