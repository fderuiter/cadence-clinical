# Handoff Report: Survey of `packages/core-models` & Domain Ownership Mapping

## 1. Observation
- Executed `find packages/core-models -type f -name "*.py"`: Identified 66 total Python source files spanning 16 domain packages and top-level utility modules.
- Evaluated AST definitions across all 66 files using `uv run python -c "import ast..."`: Parsed 100% of classes, Pydantic models, Enums, functions, and docstrings. Key domain models identified include:
  - `cdisc/usdm_models.py` (line 15): `USDMStudy`, `StudyDesign`, `EligibilityCriterion`, `Activity`, `Encounter`.
  - `protocol_authoring/models.py` (line 22): `ProtocolBlock`, `NarrativeBlock`, `ObjectiveBlock`, `EligibilityBlock`, `SoADerivedBlock`.
  - `protocol_authoring/soa.py` (line 45): `StudyArm`, `Epoch`, `Visit`, `Procedure`, `TimingWindow`, `SoAMatrixProjectionResponse`.
  - `execution/offline_models.py` (line 27): `OfflineDeltaItem`, `OfflineBatchSyncRequest`, `EPROOfflineEntry`, `EPROBulkSyncRequest`.
  - `execution/safety_models.py` (line 15): `SAECaseRecord`, `SeriousnessCriteriaEnum`, `CausalityEnum`.
  - `tmf_reference_model/models.py` (line 12): `Artifact`, `Section`, `Zone`, `TaxonomyCatalog`.
  - `sae_icsr/models.py` (line 18): `IndividualCaseSafetyReport`, `SeriousAdverseEvent`, `MedDRACoding`.
  - `audit.py` (line 8): `Part11AuditMixin`, `AuditFields`.
  - `datetime_helpers.py` (line 12): `validate_timezone_aware_datetime`, `serialize_utc_z`.
  - `signature.py` (line 10): `SigningReason`, `ApprovalStatus`, `SignatureManifestation`.
- Surveyed workspace configuration & scripts:
  - `pyproject.toml:26`: `packages-core-models = { workspace = true }`.
  - `scripts/validate_schemas.py:40`: `for name in ["core-models", "database", "deid", "security", "ui"]:`.
  - `scripts/detect_duplication.py:252-272`: Contains whitelist entries referencing `packages/core-models/audit.py` and `packages/core-models/sdtm/models.py`.

## 2. Logic Chain
- Step 1 (Discovery): All 66 files in `packages/core-models` represent domain models, schemas, or shared utilities currently placed in a monolithic shared package.
- Step 2 (Domain Ownership): Each domain file naturally maps to a specific microservice based on its business responsibility (e.g. protocol authoring and USDM to `apps/designer`, EDC data capture and SDTM to `apps/execution`, ICSR/SAE safety to `apps/safety`, TMF reference model to `apps/etmf`, CTMS DOA to `apps/ctms`, gateway proxies to `apps/gateway`).
- Step 3 (Technical Utilities): Cross-cutting GxP utilities (`Part11AuditMixin` in `audit.py`, `serialize_utc_z` in `datetime_helpers.py`, `SignatureManifestation` in `signature.py`) belong in core technical packages (`packages/database` and `packages/security`).
- Step 4 (ACL Architecture): Cross-service communication must strictly use REST endpoints with local Anti-Corruption Layer (ACL) DTOs in consuming services (e.g., `apps/execution/src/domain/acl/ctms_doa_dto.py` and `apps/execution/src/domain/acl/protocol_version_dto.py`), preventing any direct model or database imports across service boundaries.
- Step 5 (Tooling Cleanup): Deleting `packages/core-models` requires updating `pyproject.toml`, `scripts/validate_schemas.py`, and `scripts/detect_duplication.py` to prevent CI pipeline failures.

## 3. Caveats
- No source code modifications were performed during this phase, as this subagent was dispatched for read-only exploration and domain analysis.
- Some shared enums (e.g., `OrganizationType` in `apps/org`) may require minor string enum duplication in consuming services (`packages/security`, `apps/ctms`) if REST API calls for enum lookups incur unnecessary overhead.

## 4. Conclusion
All 66 source files in `packages/core-models` have been completely mapped with explicit domain ownership, target paths under `src/domain/`, and ACL DTO conversion strategies. The full detailed report is available at `/Users/fred/Code/cadence-clinical/.agents/teamwork_preview_explorer_survey_1/analysis.md`.

## 5. Verification Method
1. Inspect analysis output:
   `view_file` at `/Users/fred/Code/cadence-clinical/.agents/teamwork_preview_explorer_survey_1/analysis.md`
2. Verify all files exist in core-models:
   `find packages/core-models -type f -name "*.py" | wc -l` (matches 66 files).
3. Validate python syntax across core-models:
   `uv run python -c "import glob, ast; [ast.parse(open(f).read()) for f in glob.glob('packages/core-models/**/*.py', recursive=True)]"` (exits with code 0).
