# Handoff Report — Designer Domain Models Investigation & Mapping (M2 Task 1)

## 1. Observation

Direct observations from examining `packages/core-models/`, `apps/`, `packages/`, `scripts/`, `tests/`:

1. **Designer Source Files in `packages/core-models/`**:
   - USDM & CDISC models:
     - `packages/core-models/cdisc/usdm_models.py` (126 lines)
     - `packages/core-models/cdisc/usdm_transport_models.py` (39 lines)
     - `packages/core-models/cdisc/usdm_importer.py` (111 lines)
     - `packages/core-models/cdisc/branch_models.py` (63 lines)
     - `packages/core-models/cdisc/cascade_models.py` (46 lines)
     - `packages/core-models/cdisc/cdisc_library_client.py` (412 lines)
     - `packages/core-models/cdisc/sentinel_models.py` (154 lines)
     - `packages/core-models/cdisc/terminology_cache.py` (276 lines)
     - `packages/core-models/cdisc/__init__.py` (52 lines)
     - `packages/core-models/designer/synopsis_transport_models.py` (39 lines)
     - `packages/core-models/usdm_ingestion.py` (551 lines)
   - Protocol Authoring models:
     - `packages/core-models/protocol_authoring/models.py` (397 lines)
     - `packages/core-models/protocol_authoring/soa.py` (658 lines)
     - `packages/core-models/protocol_authoring/__init__.py` (114 lines)
   - Protocol Render models:
     - `packages/core-models/protocol_render/models.py` (254 lines)
     - `packages/core-models/protocol_render/__init__.py` (32 lines)
   - Protocol Version Ref models:
     - `packages/core-models/protocol_version_ref/models.py` (85 lines)
     - `packages/core-models/protocol_version_ref/__init__.py` (14 lines)
   - Eligibility models:
     - `packages/core-models/eligibility/models.py` (394 lines)
     - `packages/core-models/eligibility/evaluator.py` (364 lines)
     - `packages/core-models/eligibility/parser.py` (249 lines)
     - `packages/core-models/eligibility/__init__.py` (42 lines)
   - Document Renderer model:
     - `packages/core-models/document_renderer.py` (192 lines)

2. **Defined Symbols Cataloged**:
   - `usdm_models.py`: `Code`, `SyntaxTemplate`, `EligibilityCriterion`, `Activity`, `Encounter`, `StudyArm`, `StudyEpoch`, `StudyDesign`, `USDMStudy`
   - `usdm_transport_models.py`: `UsdmImportRequest`, `UsdmImportResponse`, `UsdmExportResponse`
   - `usdm_importer.py`: `USDMImportResult`, `USDMImporter`
   - `branch_models.py`: `ProtocolBranch`, `BlockDiff`, `AmendmentComparisonResponse`
   - `cascade_models.py`: `CascadedFormTemplate`, `CascadeSummaryReport`
   - `cdisc_library_client.py`: `CdiscLibraryConfig`, `CdiscProductSummary`, `CdashDomainDefinition`, `SdtmDomainDefinition`, `CodelistTerm`, `CodelistDefinition`, `CdiscLibraryClient`
   - `sentinel_models.py`: `QualityRuleFinding`, `ReadabilityReport`, `BurdenTraceItem`, `BurdenTraceReport`, `AmendmentImpactReport`, `AttritionStep`, `FeasibilityReport`, `ProtocolQualityScore`
   - `terminology_cache.py`: `CdiscTerminologyCache`
   - `synopsis_transport_models.py`: `SynopsisExportRequest`, `SynopsisExportResponse`
   - `usdm_ingestion.py`: `ValidationIssue`, `USDMValidationReport`, `FieldReference`, `ExpressionNode`, `extract_field_references`, `detect_circular_dependencies`, `safe_parse_payload`, `resolve_usdm_version`, `normalize_usdm_payload`, `traverse_rules_in_payload`, `detect_stochastic_operators`, `validate_usdm_payload`
   - `protocol_authoring/models.py`: `BlockType`, `SectionReviewStatus`, `SuggestionStatus`, `ProtocolBlock`, `NarrativeBlock`, `ObjectiveBlock`, `EligibilityBlock`, `SoADerivedBlock`, `ICHSection`, `Comment`, `CommentThread`, `Suggestion`, `SectionReviewTransition`, `ProtocolBlockUnion`, `build_canonical_ich_skeleton`, `CANONICAL_ICH_SKELETON`
   - `protocol_authoring/soa.py`: `StudyArm`, `Epoch`, `Visit`, `Procedure`, `TimingWindow`, `StudyArmProperties`, `EpochProperties`, `VisitProperties`, `ProcedureProperties`, `TimingWindowProperties`, `CreateStudyArmRequest`, `UpdateStudyArmRequest`, `CreateEpochRequest`, `UpdateEpochRequest`, `CreateVisitRequest`, `UpdateVisitRequest`, `CreateProcedureRequest`, `UpdateProcedureRequest`, `CreateTimingWindowRequest`, `UpdateTimingWindowRequest`, `LinkEpochVisitRequest`, `LinkVisitProcedureRequest`, `LinkTimingRequest`, `LinkArmApplicabilityRequest`, `SoALinkResponse`, `SoAEntityCreatedResponse`, `SoAEntityDetail`, `AuditMetadata`, `ProjectionCell`, `SoAMatrixProjectionResponse`, `VisitReorderItem`, `VisitReorderRequest`, `ActivityAssignmentRequest`, `ArmReorderItem`, `ArmReorderRequest`, `EpochReorderItem`, `EpochReorderRequest`, `ProcedureReorderItem`, `ProcedureReorderRequest`, `VisitToArmAssignmentRequest`, `VisitToEpochAssignmentRequest`
   - `protocol_render/models.py`: `ExportMetadata`, `NarrativeItemView`, `NarrativeSectionView`, `SynopsisView`, `SoAHeaderArm`, `SoAHeaderEpoch`, `SoAHeaderEncounter`, `SoACellView`, `SoARowView`, `SoAMatrixView`, `RenderedProtocolDocument`
   - `protocol_version_ref/models.py`: `ProtocolVersionStatus`, `ProtocolVersionRef`
   - `eligibility/models.py`: `ComparisonOperator`, `LogicalOperator`, `FieldReference`, `ExpressionNode`, `EligibilityCriterion`, `NodeEvaluation`, `CriterionEvaluation`, `AggregateEligibilityResult`, `FIELD_REF_RE`
   - `eligibility/evaluator.py`: `evaluate_node`, `evaluate_eligibility`, `evaluate_structured_expression`, `evaluate_criteria_group`
   - `eligibility/parser.py`: `Token`, `DSLParser`, `tokenize`, `parse_dsl`, `TOKEN_SPEC`
   - `document_renderer.py`: `ProtocolDocumentRenderer`

3. **Key Import Sites Across Microservices**:
   - `apps/designer/`: `importers/usdm_importer.py`, `routers/cascade.py`, `routers/quality_sentinel.py`, `routers/synopsis.py`, `services/artifact_cascade.py`, `services/branch_manager.py`, `services/quality_sentinel.py`, `main.py`, `serialization.py`, `adapter/repositories.py`, `delta.py`, `soa_models.py`, `content_assembly.py`, `rendering.py`, `renderers/document_renderer.py`, `renderers/__init__.py`.
   - `apps/gateway/`: `routers/cdisc.py`, `routers/usdm.py`.
   - `apps/execution/`: `translator.py` (`import usdm_ingestion`), `main.py` (`from protocol_version_ref import ProtocolVersionRef`), `designer_client.py` (`from eligibility.models import EligibilityCriterion`), `eligibility_service.py` (`from eligibility.evaluator import evaluate_eligibility`, `from eligibility.models import AggregateEligibilityResult`).
   - `apps/etmf/`: `ingestion.py`, `ingestion_service.py`, `main.py` (`from protocol_version_ref import ProtocolVersionRef`).
   - `apps/ctms/`: `routers/doa.py` (`import document_renderer`).
   - `apps/interop/`: `designer_client.py`, `main.py` (`from eligibility import EligibilityCriterion, ExpressionNode, parse_dsl`, `from eligibility import evaluate_eligibility`).

4. **Internal Model Imports**:
   - `packages/core-models/protocol_authoring/soa.py:12`: `from protocol_render import SoAHeaderArm, SoAHeaderEncounter, SoAHeaderEpoch, SoARowView`
   - `packages/core-models/eligibility/evaluator.py:11`: `from eligibility.models import ...`
   - `packages/core-models/eligibility/parser.py:11`: `from eligibility.models import ExpressionNode, FieldReference`
   - `packages/core-models/cdisc/usdm_importer.py:14`: `from cdisc.usdm_models import USDMStudy`
   - `packages/core-models/cdisc/terminology_cache.py:14`: `from cdisc.cdisc_library_client import CodelistDefinition, CodelistTerm`

---

## 2. Logic Chain

1. **Observation 1 & 2** establish the full list of 23 source files and over 70 classes/functions belonging to Designer domain models in `packages/core-models/`.
2. **Feature #2 in `PROJECT.md`** specifies moving all Designer domain models (USDM, Protocol Authoring, Protocol Render, Protocol Version Ref, Eligibility, USDM Ingestion, Document Renderer) to `apps/designer/src/domain/`.
3. **Observation 3** shows that while most import sites are within `apps/designer/` and `apps/gateway/`, there are direct cross-service imports from `apps/execution/`, `apps/etmf/`, `apps/ctms/`, and `apps/interop/`.
4. Therefore, when relocating files to `apps/designer/src/domain/`, all import statements across all identified files in `apps/designer/`, `apps/gateway/`, `apps/execution/`, `apps/etmf/`, `apps/ctms/`, `apps/interop/`, `scripts/`, and `tests/` must be updated to target `apps.designer.src.domain...` (or re-exported via backward-compatible aliases in `apps/designer/src/domain/`).
5. **Observation 4** indicates internal dependencies between modules (e.g. `protocol_authoring.soa` -> `protocol_render`). These must use clean relative or absolute imports pointing to `apps.designer.src.domain...` to prevent import errors.

---

## 3. Caveats

- **Scope boundary**: This report covers investigation and mapping only. No source code modifications were performed per read-only exploration directives.
- **Microservice ACLs (Milestone M4)**: `execution`, `etmf`, `ctms`, and `interop` currently import Designer domain models directly. During M2 relocation, these import sites will be updated to point to `apps.designer.src.domain...`. In Milestone M4, these direct cross-service imports will be replaced with local ACL DTOs under `apps/<service>/src/domain/acl/`.
- **Dynamic Module Loaders**: `apps/designer/usdm_ingestion.py` and `apps/designer/renderers/document_renderer.py` load files dynamically from `packages/core-models/`. When files are relocated, these loaders must be updated or converted to standard module imports.

---

## 4. Conclusion

All 23 source files corresponding to Designer domain models in `packages/core-models/` have been mapped to their target locations under `apps/designer/src/domain/`. All model classes, functions, and import sites repository-wide have been cataloged. Import conflicts and circular dependency risks have been identified with clear mitigation strategies.

Detailed findings are documented in `/Users/fred/Code/cadence-clinical/.agents/teamwork_preview_explorer_m2_1/analysis.md`.

---

## 5. Verification Method

To independently verify the mapping and import catalog:

1. **Verify target file existence & locations**:
   Check `analysis.md` Section 2 target mapping table against existing files in `packages/core-models/`.

2. **Verify import statements**:
   Run grep commands to verify all listed import sites:
   ```bash
   grep -rn "from cdisc" apps/ packages/ scripts/ tests/
   grep -rn "from eligibility" apps/ packages/ scripts/ tests/
   grep -rn "from protocol_authoring" apps/ packages/ scripts/ tests/
   grep -rn "from protocol_render" apps/ packages/ scripts/ tests/
   grep -rn "from protocol_version_ref" apps/ packages/ scripts/ tests/
   grep -rn "usdm_ingestion" apps/ packages/ scripts/ tests/
   grep -rn "document_renderer" apps/ packages/ scripts/ tests/
   ```

3. **Verify post-migration test suite (for Worker)**:
   ```bash
   uv run ruff check .
   uv run ruff format --check .
   python3 scripts/detect_duplication.py
   uv run pytest -n auto
   uv run python scripts/sync_gxp.py
   ```
