"""Application service orchestrating the automated CDISC SDTM/ADaM Pipeline & Dataset-JSON / Define-XML Compiler.

Coordinates:
- Extraction of harmonized clinical observations from Lakehouse snapshots or active pools
- Automated SDTM 10-domain transformation (DM, AE, CM, VS, LB, EX, DS, TU, TR, RS)
- Automated ADaM derivation rules (ADSL, ADAE, ADTTE, BDS)
- Role-gated blinding masking for treatment arms and sensitive unblinded variables
- CDISC Dataset-JSON 1.1 / 2.0 serialization and strict schema validation
- CDISC Define-XML v2.1 compilation with VLM, WhereClauses, Origins, and Methods
- 21 CFR Part 11 immutable audit trail recording for all transformations and exports

Requirements: PRD-CDR-002, PRD-CDR-003, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

import hashlib
import json
import uuid
from datetime import UTC, datetime
from typing import Any

from apps.interop.domain.cdr.adam_pipeline import ADaMPipelineEngine
from apps.interop.domain.cdr.dataset_json_compiler import DatasetJSONCompiler
from apps.interop.domain.cdr.define_xml_compiler import DefineXMLCompiler
from apps.interop.domain.cdr.models import (
    DefineXMLCompilationRequest,
    PipelineExecutionResult,
    PipelineExecutionStatus,
)
from apps.interop.domain.cdr.sdtm_pipeline import SDTMPipelineEngine
from apps.interop.domain.lakehouse_models import (
    LakehouseAction,
)
from apps.interop.domain.ports import LakehouseRepositoryPort


class CDRPipelineService:
    """Core application service orchestrating continuous CDISC SDTM/ADaM pipelines and export compilers."""

    def __init__(self, repository: LakehouseRepositoryPort) -> None:
        self.repository = repository
        self._execution_history: dict[str, PipelineExecutionResult] = {}
        self._cached_results: dict[str, dict[str, Any]] = {}

    async def write_audit_log(
        self,
        user_id: str,
        user_role: str,
        action: LakehouseAction | str,
        study_id: str,
        snapshot_id: str | None,
        details: dict[str, Any],
        reason_for_change: str | None = None,
    ) -> None:
        """Write an immutable entry to the 21 CFR Part 11 audit log."""
        timestamp = datetime.now(UTC)
        action_val = action.value if hasattr(action, "value") else str(action)
        seal_payload = {
            "timestamp": timestamp.isoformat(),
            "user_id": user_id,
            "user_role": user_role,
            "action": action_val,
            "study_id": study_id,
            "snapshot_id": snapshot_id,
            "details": details,
        }
        canonical_str = json.dumps(seal_payload, sort_keys=True, separators=(",", ":"))
        sha256_seal = hashlib.sha256(canonical_str.encode("utf-8")).hexdigest()

        await self.repository.save_audit_log(
            audit_id=str(uuid.uuid4()),
            timestamp=timestamp,
            user_id=user_id,
            user_role=user_role,
            action=action_val,
            study_id=study_id,
            snapshot_id=snapshot_id,
            details=details,
            reason_for_change=reason_for_change,
            sha256_seal=sha256_seal,
        )

    def _apply_blinding_mask(
        self,
        sdtm_domains: dict[str, list[Any]],
        adam_datasets: dict[str, list[Any]],
    ) -> tuple[dict[str, list[Any]], dict[str, list[Any]]]:
        """Mask unblinded treatment arm variables for blinded roles."""
        masked_sdtm: dict[str, list[Any]] = {}
        for d_name, recs in sdtm_domains.items():
            masked_recs = []
            for rec in recs:
                r_copy = rec.model_copy(deep=True)
                if hasattr(r_copy, "ARM") and r_copy.ARM:
                    r_copy.ARM = "Masked Treatment Arm"
                if hasattr(r_copy, "ARMCD") and r_copy.ARMCD:
                    r_copy.ARMCD = "MASKED"
                if hasattr(r_copy, "ACTARM") and r_copy.ACTARM:
                    r_copy.ACTARM = "Masked Actual Arm"
                if hasattr(r_copy, "ACTARMCD") and r_copy.ACTARMCD:
                    r_copy.ACTARMCD = "MASKED"
                if hasattr(r_copy, "EXTRT") and r_copy.EXTRT:
                    r_copy.EXTRT = "Blind Study Medication"
                masked_recs.append(r_copy)
            masked_sdtm[d_name] = masked_recs

        masked_adam: dict[str, list[Any]] = {}
        for ds_name, recs in adam_datasets.items():
            masked_recs = []
            for rec in recs:
                r_copy = rec.model_copy(deep=True)
                if hasattr(r_copy, "ARM") and r_copy.ARM:
                    r_copy.ARM = "Masked Treatment Arm"
                if hasattr(r_copy, "ARMCD") and r_copy.ARMCD:
                    r_copy.ARMCD = "MASKED"
                if hasattr(r_copy, "ACTARM") and r_copy.ACTARM:
                    r_copy.ACTARM = "Masked Actual Arm"
                if hasattr(r_copy, "ACTARMCD") and r_copy.ACTARMCD:
                    r_copy.ACTARMCD = "MASKED"
                if hasattr(r_copy, "TRT01P") and r_copy.TRT01P:
                    r_copy.TRT01P = "Masked Treatment Arm"
                if hasattr(r_copy, "TRT01A") and r_copy.TRT01A:
                    r_copy.TRT01A = "Masked Actual Arm"
                masked_recs.append(r_copy)
            masked_adam[ds_name] = masked_recs

        return masked_sdtm, masked_adam

    async def execute_continuous_pipeline(
        self,
        study_id: str,
        snapshot_id: str | None = None,
        user_id: str = "system",
        user_role: str = "data_manager",
        is_unblinded: bool = False,
        dataset_json_version: str = "1.1.0",
        reason_for_change: str = "Automated CDISC SDTM/ADaM continuous transformation run",
    ) -> PipelineExecutionResult:
        """Execute end-to-end continuous transformation pipeline from raw observations to SDTM, ADaM, Dataset-JSON, and Define-XML."""
        exec_id = f"EXEC-{uuid.uuid4().hex[:12].upper()}"
        started_at = datetime.now(UTC)

        # 1. Fetch raw observations
        if snapshot_id:
            raw_records = await self.repository.get_snapshot_records(
                snapshot_id=snapshot_id,
                study_id=study_id,
                is_unblinded=is_unblinded,
            )
        else:
            raw_records = await self.repository.get_records(
                study_id=study_id,
            )

        # 2. Run SDTM Pipeline Engine (10 domains)
        sdtm_engine = SDTMPipelineEngine(study_id=study_id)
        sdtm_domains = sdtm_engine.build_sdtm_domains(raw_records)

        # 3. Run ADaM Pipeline Engine (4 datasets)
        adam_engine = ADaMPipelineEngine(study_id=study_id)
        adam_datasets = adam_engine.build_adam_datasets(sdtm_domains)

        # 4. Role-based Blinding Masking
        if not is_unblinded:
            sdtm_domains, adam_datasets = self._apply_blinding_mask(
                sdtm_domains, adam_datasets
            )

        # 5. Compile into Dataset-JSON 1.1 / 2.0
        json_compiler = DatasetJSONCompiler(
            study_id=study_id, spec_version=dataset_json_version
        )
        all_datasets: dict[str, list[Any]] = {**sdtm_domains, **adam_datasets}
        dataset_json_doc = json_compiler.serialize_datasets(all_datasets)
        json_string = json_compiler.to_json(dataset_json_doc)
        json_validation = json_compiler.validate_dataset_json(json_string)

        # 6. Auto-compile Define-XML 2.1
        define_req = DefineXMLCompilationRequest(
            study_id=study_id,
            study_name=f"Cadence Protocol {study_id}",
            domains=list(sdtm_domains.keys()),
            adam_datasets=list(adam_datasets.keys()),
            include_vlm=True,
            include_where_clauses=True,
            include_origins=True,
        )
        define_compiler = DefineXMLCompiler(define_req)
        define_xml_string = define_compiler.compile()
        define_validation = DefineXMLCompiler.validate_define_xml(define_xml_string)

        sdtm_counts = {k: len(v) for k, v in sdtm_domains.items()}
        adam_counts = {k: len(v) for k, v in adam_datasets.items()}
        total_sdtm = sum(sdtm_counts.values())
        total_adam = sum(adam_counts.values())

        completed_at = datetime.now(UTC)

        result = PipelineExecutionResult(
            execution_id=exec_id,
            study_id=study_id,
            snapshot_id=snapshot_id,
            status=PipelineExecutionStatus.COMPLETED,
            started_at=started_at,
            completed_at=completed_at,
            sdtm_counts=sdtm_counts,
            adam_counts=adam_counts,
            total_sdtm_records=total_sdtm,
            total_adam_records=total_adam,
            dataset_json_manifest={
                "datasetJSONVersion": dataset_json_version,
                "fileOID": dataset_json_doc.fileOID,
                "dataset_count": len(all_datasets),
                "is_valid": json_validation["is_valid"],
            },
            define_xml_summary={
                "define_version": "2.1.0",
                "standard_name": "SDTM-IG / ADaM-IG",
                "is_valid": define_validation.is_valid,
                "item_group_count": define_validation.item_group_count,
                "item_def_count": define_validation.item_def_count,
                "vlm_count": define_validation.value_level_metadata_count,
                "where_clause_count": define_validation.where_clause_count,
                "origin_count": define_validation.item_def_count,
            },
            validation_summary={
                "dataset_json": json_validation,
                "define_xml": define_validation.model_dump(),
            },
            errors=[],
        )

        # Cache result
        self._execution_history[exec_id] = result
        self._cached_results[exec_id] = {
            "sdtm_domains": sdtm_domains,
            "adam_datasets": adam_datasets,
            "dataset_json_doc": dataset_json_doc,
            "dataset_json_str": json_string,
            "define_xml_str": define_xml_string,
        }

        # 7. Write 21 CFR Part 11 Audit Trail
        await self.write_audit_log(
            user_id=user_id,
            user_role=user_role,
            action=LakehouseAction.POOL_EXPORT,
            study_id=study_id,
            snapshot_id=snapshot_id,
            details={
                "execution_id": exec_id,
                "total_sdtm_records": total_sdtm,
                "total_adam_records": total_adam,
                "dataset_json_version": dataset_json_version,
                "define_xml_version": "2.1.0",
                "is_unblinded": is_unblinded,
            },
            reason_for_change=reason_for_change,
        )

        return result

    def get_execution_result(self, execution_id: str) -> PipelineExecutionResult | None:
        """Retrieve stored execution manifest."""
        return self._execution_history.get(execution_id)

    def get_cached_dataset_json(self, execution_id: str) -> str | None:
        """Retrieve cached Dataset-JSON string for execution."""
        cached = self._cached_results.get(execution_id)
        return cached.get("dataset_json_str") if cached else None

    def get_cached_define_xml(self, execution_id: str) -> str | None:
        """Retrieve cached Define-XML string for execution."""
        cached = self._cached_results.get(execution_id)
        return cached.get("define_xml_str") if cached else None
