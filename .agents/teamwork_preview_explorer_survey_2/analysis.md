# Core Models & Cross-Service Import Analysis Report

## Executive Summary
This report presents a comprehensive survey of all import sites referencing `packages/core-models` and cross-service model dependencies across `apps/`, `packages/`, `tests/`, and `scripts/` in the `cadence-clinical` repository.

`packages/core-models` consists of **25 top-level items** (23 Python packages/modules comprising 66 Python files). Because `packages/__init__.py` injects `packages/core-models` into `sys.path`, services currently import domain models, transport DTOs, and business logic directly via top-level module names (e.g. `from cdisc.usdm_models import ...`, `from eligibility.models import ...`).

---

## 1. Inventory of `packages/core-models`

The table below lists all 23 Python packages and modules inside `packages/core-models`, their inferred service ownership, and their total file count.

| # | Package / Module | Inferred Owner | File Count | Main Description / Responsibility |
|---|------------------|----------------|------------|-----------------------------------|
| 1 | `audit.py` | Shared Base / `packages.database` | 1 | GxP 21 CFR Part 11 audit fields mixin (`AuditFields`, `Part11AuditMixin`). |
| 2 | `cdisc/` | `apps/designer` | 9 | CDISC USDM v3/v4 models, USDM importer, Sentinel quality models, Branch models, Cascade models, CDISC Library client & terminology cache. |
| 3 | `ctms/` | `apps/ctms` | 3 | Delegation of Authority (DOA) domain models & transport DTOs. |
| 4 | `datetime_helpers.py` | Shared Base | 1 | Pydantic timezone-aware datetime validation helper (`AwareDatetime`). |
| 5 | `designer/` | `apps/designer` | 2 | Synopsis transport models & protocol export DTOs. |
| 6 | `document_renderer.py` | `apps/designer` | 1 | Protocol document rendering engine (`ProtocolDocumentRenderer` PDF/DOCX generation). |
| 7 | `eligibility/` | `apps/designer` | 4 | Eligibility criteria DSL parser (`parser.py`), evaluator (`evaluator.py`), and models (`models.py`). |
| 8 | `etmf/` | `apps/etmf` / `apps/eisf` | 3 | eISF transport models & taxonomy mapping DTOs. |
| 9 | `execution/` | `apps/execution` | 14 | Execution domain & transport models: DOA, eConsent, eISF, ePRO/eCOA, Lab, Lock, Offline, Safety, SDV, Signatures. |
| 10 | `localization/` | `apps/econsent` / Shared | 2 | Language code validation & translation metadata models. |
| 11 | `notifications/` | `apps/notifications` | 2 | Domain event schemas (`SystemDomainEvent`, `NotificationEventPayload`). |
| 12 | `organization_domain/` | `apps/org` | 2 | Clinical staff roles (`ClinicalStaffRole`), organization types (`OrganizationType`), site staff models. |
| 13 | `protocol_authoring/` | `apps/designer` | 3 | Protocol authoring models (study arms, visits, procedures, comments) & Schedule of Activities (`soa.py`). |
| 14 | `protocol_render/` | `apps/designer` | 2 | Protocol document rendering views (`SoAMatrixView`, `RenderedProtocolDocument`). |
| 15 | `protocol_version_ref/` | `apps/designer` | 2 | Protocol version reference DTOs (`ProtocolVersionRef`, `ProtocolVersionStatus`). |
| 16 | `sae_icsr/` | `apps/safety` | 2 | Individual Case Safety Report (`IndividualCaseSafetyReport`), E2B(R3) XML models, MedDRA coding DTOs. |
| 17 | `sdtm/` | `apps/execution` | 5 | Strongly-typed SDTM models (DM, VS, LB, AE, CM), enums, terminology, Dataset JSON models, scrubber models. |
| 18 | `signature.py` | Shared Base / GxP | 1 | E-signature manifestation (`SignatureManifestation`), signing reasons (`SigningReason`), approval status. |
| 19 | `storage/` | `apps/etmf` / `apps/execution` | 2 | Document storage DTOs (`ArchiveJobResponse`, storage metadata). |
| 20 | `sync_engine.py` | `apps/interop` | 1 | Data sync reconciliation engine (`SyncRecord`, `SyncMetadata`, `reconcile_records`). |
| 21 | `tmf_reference_model/` | `apps/etmf` | 2 | DIA TMF Reference Model v3.0 catalog, artifacts, sections, zones, and milestone mandatory lists. |
| 22 | `usdm_ingestion.py` | `apps/designer` | 1 | USDM JSON payload ingestion, normalization, and version validation. |
| 23 | `watermark.py` | Shared Base / `apps/etmf` | 1 | Attributable security watermark application engine (`apply_watermark`). |

---

## 2. Import Sites Inventory Grouped by Consuming Service

### 2.1 `apps/designer`
- **Import Sites**:
  - `apps/designer/importers/usdm_importer.py`: `from cdisc.usdm_importer import USDMImporter, USDMImportResult`
  - `apps/designer/routers/cascade.py`: `from cdisc.cascade_models import CascadeSummaryReport`
  - `apps/designer/routers/quality_sentinel.py`: `from cdisc.sentinel_models import ProtocolQualityScore`
  - `apps/designer/services/artifact_cascade.py`: `from cdisc.cascade_models import CascadedFormTemplate, CascadeSummaryReport`
  - `apps/designer/services/branch_manager.py`: `from cdisc.branch_models import BranchConfig, BranchMergeResult`
  - `apps/designer/services/quality_sentinel.py`: `from cdisc.sentinel_models import ProtocolQualityScore`, `from eligibility.evaluator import evaluate_node`, `from eligibility.models import ExpressionNode`
  - `apps/designer/routers/synopsis.py`: `from designer.synopsis_transport_models import SynopsisRequest, SynopsisResponse`
  - `apps/designer/renderers/document_renderer.py`: Loads `packages/core-models/document_renderer.py` dynamically
  - `apps/designer/main.py`: `from eligibility import EligibilityCriterion, ExpressionNode, parse_dsl`, `from protocol_authoring.models import ...`, `from protocol_render import SoAMatrixView`, `from signature import SigningReason, SignatureManifestation`
  - `apps/designer/adapter/repositories.py`: `from protocol_authoring.models import ...`
  - `apps/designer/delta.py`: `from protocol_authoring.models import ...`
  - `apps/designer/soa_models.py`: `from protocol_authoring import ...`, `from protocol_render import ...`
  - `apps/designer/content_assembly.py`: `from protocol_render import ...`
  - `apps/designer/rendering.py`: `from protocol_render import RenderedProtocolDocument, SoAMatrixView`
  - `apps/designer/usdm_ingestion.py`: Loads `packages/core-models/usdm_ingestion.py` dynamically
- **Classification**: Internal service usages (all imported models belong natively to `designer`).

### 2.2 `apps/execution`
- **Import Sites**:
  - `apps/execution/exporters/e2b_xml_builder.py`: `from execution.safety_models import SAECaseRecord`
  - `apps/execution/routers/doa.py`: `from execution.doa_models import ...`
  - `apps/execution/routers/eisf.py`: `from execution.eisf_models import ...`
  - `apps/execution/routers/locks.py`: `from execution.lock_models import ...`, `from execution.lock_transport_models import ...`
  - `apps/execution/routers/offline.py`: `from execution.offline_models import ...`
  - `apps/execution/routers/safety.py`: `from execution.safety_transport_models import ...`
  - `apps/execution/routers/sdv.py`: `from execution.sdv_transport_models import ...`
  - `apps/execution/routers/signatures.py`: `from execution.signature_transport_models import ...`
  - `apps/execution/services/doa_service.py`: `from execution.doa_models import ...`
  - `apps/execution/services/e2b_parser.py`: `from execution.safety_models import ...`
  - `apps/execution/services/econsent_capture_service.py`: `from execution.econsent_models import ...`
  - `apps/execution/services/eisf_service.py`: `from execution.eisf_models import ...`
  - `apps/execution/services/lock_enforcement.py`: `from execution.lock_models import DataLockRecord, LockScopeEnum, LockStatusEnum`
  - `apps/execution/services/sae_reconciler.py`: `from execution.safety_models import SAECaseRecord`
  - `apps/execution/biostat/terminology.py`: `from sdtm.enums import ...`, `from sdtm.terminology import ...`
  - `apps/execution/biostat/validator.py`: `from sdtm.enums import ...`
  - `apps/execution/exports/sdtm_json_builder.py`: `from sdtm.scrubber_models import DeidentConfig`
  - `apps/execution/sdtm_mapper.py`: `from sdtm.models import AE, CM, DM, LB, VS`, `from sdtm.terminology import ...`
  - `apps/execution/services/dataset_json_builder.py`: `from sdtm.dataset_json_models import ...`
  - `apps/execution/services/deident_scrubber.py`: `from sdtm.scrubber_models import DeidentConfig, DeidentSummary`
  - `apps/execution/services/sdtm_mapper.py`: `from sdtm.sdtm_models import ...`
  - `apps/execution/routers/documents.py`: `from storage.document_models import ...`, `from watermark import apply_watermark`
  - `apps/execution/designer_client.py`: `from eligibility.models import EligibilityCriterion` (**Cross-Service Model Dependency on `designer`**)
  - `apps/execution/eligibility_service.py`: `from eligibility.evaluator import evaluate_eligibility`, `from eligibility.models import AggregateEligibilityResult` (**Cross-Service Model Dependency on `designer`**)
  - `apps/execution/main.py`: `from protocol_version_ref import ProtocolVersionRef` (**Cross-Service Model Dependency on `designer`**)
  - `apps/execution/translator.py`: `import usdm_ingestion` (**Cross-Service Model Dependency on `designer`**)

### 2.3 `apps/ctms`
- **Import Sites**:
  - `apps/ctms/routers/doa.py`: `from ctms.doa_transport_models import ...` (Internal service usage)
  - `apps/ctms/routers/doa.py`: `import document_renderer` -> `document_renderer.ProtocolDocumentRenderer` (**Cross-Service Model Dependency on `designer`**)
  - `apps/ctms/main.py`: `import sync_engine` (**Cross-Service Model Dependency on `interop`**)

### 2.4 `apps/econsent`
- **Import Sites**:
  - `apps/econsent/main.py`: `from audit import AuditFields` (Shared audit base)
  - `apps/econsent/main.py`: `from localization import validate_language_code` (Internal / shared localization)
  - `apps/econsent/main.py`: `from signature import SignatureManifestation, SigningReason` (Shared signature base)

### 2.5 `apps/eisf`
- **Import Sites**:
  - `apps/eisf/routers/eisf.py`: `from etmf.eisf_transport_models import ...` (Internal/sister service model usage)

### 2.6 `apps/etmf`
- **Import Sites**:
  - `apps/etmf/classification_service.py`: `from tmf_reference_model import ...` (Internal service usage)
  - `apps/etmf/ingestion_service.py`: `from tmf_reference_model import ...`, `from signature import SignatureManifestation, SigningReason` (Internal & signature base)
  - `apps/etmf/main.py`: `from tmf_reference_model import ...`, `from signature import SigningReason, SignatureManifestation`
  - `apps/etmf/routers/taxonomy.py`: `from tmf_reference_model import ...`
  - `apps/etmf/routers/archive.py`: `from storage.document_models import ArchiveJobResponse`
  - `apps/etmf/watermark.py`: Loads `packages/core-models/watermark.py` dynamically
  - `apps/etmf/ingestion.py`: `from protocol_version_ref import ProtocolVersionRef` (**Cross-Service Model Dependency on `designer`**)
  - `apps/etmf/ingestion_service.py`: `from protocol_version_ref import ProtocolVersionRef` (**Cross-Service Model Dependency on `designer`**)
  - `apps/etmf/main.py`: `from protocol_version_ref import ProtocolVersionRef` (**Cross-Service Model Dependency on `designer`**)

### 2.7 `apps/gateway`
- **Import Sites**:
  - `apps/gateway/routers/cdisc.py`: `from cdisc.cdisc_library_client import ...`, `from cdisc.terminology_cache import CdiscTerminologyCache`
  - `apps/gateway/routers/usdm.py`: `from cdisc.usdm_importer import USDMImporter`, `from cdisc.usdm_transport_models import ...`
  - `apps/gateway/routers/ecoa.py`: `from execution.epro_transport_models import ...`, `from execution.offline_models import ...`

### 2.8 `apps/interop`
- **Import Sites**:
  - `apps/interop/designer_client.py`: `from eligibility import EligibilityCriterion, ExpressionNode, parse_dsl` (**Cross-Service Model Dependency on `designer`**)
  - `apps/interop/main.py`: `from eligibility import evaluate_eligibility` (**Cross-Service Model Dependency on `designer`**)
  - `apps/interop/main.py`: `from execution.epro_transport_models import ...` (**Cross-Service Model Dependency on `execution`**)
  - `apps/interop/sync_engine.py`: Loads `packages/core-models/sync_engine.py` dynamically (Internal service usage)

### 2.9 `apps/notifications`
- **Import Sites**:
  - `apps/notifications/workers/notification_worker.py`: `from notifications.event_models import SystemDomainEvent` (Internal service usage)

### 2.10 `apps/org`
- **Import Sites**:
  - `apps/org/main.py`: `from organization_domain import ClinicalStaffRole, OrganizationType` (Internal service usage)

### 2.11 `apps/safety`
- **Import Sites**:
  - `apps/safety/main.py`: `from sae_icsr import IndividualCaseSafetyReport` (Internal service usage)
  - `apps/safety/reconciliation.py`: `from sae_icsr import IndividualCaseSafetyReport, MedDRACoding, SeriousAdverseEvent` (Internal service usage)
  - `apps/safety/renderer.py`: `from sae_icsr import IndividualCaseSafetyReport` (Internal service usage)

### 2.12 `packages/security`
- **Import Sites**:
  - `packages/security/delegation.py`: `from organization_domain import ClinicalStaffRole`

---

## 3. Cross-Service Model Dependencies Audit & Anti-Corruption Layer (ACL) Requirements

The table below summarizes all **illegal cross-service model couplings** that violate AGENTS.md REST API-First & Decoupling standards, and defines the required Anti-Corruption Layer (ACL) DTO replacement for each.

| Consuming Service | Target Model / Source Package | Owning Service | Nature of Coupling | Anti-Corruption Layer (ACL) Requirement |
|-------------------|-------------------------------|----------------|--------------------|------------------------------------------|
| `apps/ctms` | `document_renderer.ProtocolDocumentRenderer` | `apps/designer` | CTMS directly invokes Designer's document rendering module to generate confirmation letters. | Define `CTMSProtocolDocumentDTO` inside `apps/ctms/domain/dtos.py` and call Designer REST endpoint `/api/v1/designer/documents/render`. |
| `apps/ctms` | `sync_engine.SyncRecord` / `reconcile_records` | `apps/interop` | CTMS imports sync reconciliation logic directly from Interop's sync engine. | Define local `CTMSSyncRecordDTO` inside `apps/ctms/domain/dtos.py` and delegate sync calls to Interop service endpoint. |
| `apps/execution` | `eligibility.models.EligibilityCriterion` & `eligibility.evaluator.evaluate_eligibility` | `apps/designer` | Execution imports Designer's eligibility criteria models & evaluator directly for subject prescreening. | Define `ExecutionEligibilityDTO` in `apps/execution/domain/dtos.py` and query Designer's `/api/v1/designer/eligibility/evaluate` REST endpoint. |
| `apps/execution` | `protocol_version_ref.ProtocolVersionRef` | `apps/designer` | Execution imports Designer's protocol versioning reference class directly. | Define local `ExecutionProtocolVersionDTO` in `apps/execution/domain/dtos.py`. |
| `apps/execution` | `usdm_ingestion.validate_usdm_payload` | `apps/designer` | Execution imports Designer's USDM ingestion & version resolution helper directly in translator.py. | Define local USDM payload validator DTO or route USDM validation requests via REST client. |
| `apps/etmf` | `protocol_version_ref.ProtocolVersionRef` | `apps/designer` | eTMF imports Designer's protocol version reference DTO for document tagging. | Define local `ETMFProtocolVersionDTO` in `apps/etmf/domain/dtos.py`. |
| `apps/interop` | `eligibility.models.EligibilityCriterion` & `parse_dsl` | `apps/designer` | Interop imports Designer's eligibility parser & criteria models directly for EHR prescreening. | Define `InteropEligibilityDTO` in `apps/interop/domain/dtos.py`. |
| `apps/interop` | `execution.epro_transport_models` | `apps/execution` | Interop imports Execution's ePRO transport models directly for FHIR survey sync. | Define `InteropEPROSurveyDTO` in `apps/interop/domain/dtos.py`. |

---

## 4. Sibling Database Model Imports Audit

A repository-wide check of Python `import` statements across `apps/` confirmed:
- **No direct Python imports of sibling database models exist between `apps/` packages** (e.g. `apps/execution` does NOT contain `from apps.designer.models import ...` or `from apps.designer.db import ...`).
- All cross-service model sharing currently occurs through `packages/core-models`. Therefore, eradicating `packages/core-models` and establishing local ACL DTOs for the 8 cross-service couplings identified in Section 3 will achieve 100% microservice decoupling and GxP compliance across the platform.

---

## 5. Proposed Domain Target Structure for Refactoring `packages/core-models`

To satisfy **Requirement R1**, all 23 packages/modules in `packages/core-models` must be relocated to the `src/domain/` (or `domain/`) directory of their respective owning microservice or foundational package:

| Module / Package | Destination Target Path |
|------------------|-------------------------|
| `cdisc/` | `apps/designer/domain/cdisc/` |
| `designer/` | `apps/designer/domain/synopsis/` |
| `eligibility/` | `apps/designer/domain/eligibility/` |
| `protocol_authoring/` | `apps/designer/domain/protocol_authoring/` |
| `protocol_render/` | `apps/designer/domain/protocol_render/` |
| `protocol_version_ref/` | `apps/designer/domain/protocol_version_ref/` |
| `document_renderer.py` | `apps/designer/domain/renderers/document_renderer.py` |
| `usdm_ingestion.py` | `apps/designer/domain/usdm_ingestion.py` |
| `execution/` | `apps/execution/domain/models/` |
| `sdtm/` | `apps/execution/domain/sdtm/` |
| `ctms/` | `apps/ctms/domain/doa/` |
| `etmf/` | `apps/etmf/domain/models/` |
| `tmf_reference_model/` | `apps/etmf/domain/tmf_reference_model/` |
| `watermark.py` | `apps/etmf/domain/watermark.py` (or shared) |
| `sae_icsr/` | `apps/safety/domain/sae_icsr/` |
| `notifications/` | `apps/notifications/domain/events/` |
| `organization_domain/` | `apps/org/domain/models/` |
| `localization/` | `apps/econsent/domain/localization/` |
| `sync_engine.py` | `apps/interop/domain/sync_engine.py` |
| `audit.py` | `packages/database/audit_mixin.py` (or shared base) |
| `datetime_helpers.py` | `packages/database/datetime_helpers.py` (or shared base) |
| `signature.py` | `packages/security/signature_models.py` (or shared base) |
| `storage/` | `packages/storage/document_models.py` (or shared base) |
