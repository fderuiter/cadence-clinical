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

from apps.interop.domain.cdr.models import (
    PipelineExecutionResult,
)
from apps.interop.domain.cdr.submission_engine import (
    CdiscSubmissionEngine,
    SubmissionPackage,
)
from apps.interop.domain.lakehouse_models import (
    LakehouseAction,
)
from apps.interop.domain.ports import LakehouseRepositoryPort


class CDRPipelineService:
    """Core application service orchestrating continuous CDISC SDTM/ADaM pipelines and export compilers."""

    def __init__(self, repository: LakehouseRepositoryPort) -> None:
        self.repository = repository
        self._submission_engine = CdiscSubmissionEngine()
        self._execution_history: dict[str, PipelineExecutionResult] = {}
        self._cached_results: dict[str, dict[str, Any]] = {}
        self._cached_packages: dict[str, SubmissionPackage] = {}

    @property
    def submission_engine(self) -> CdiscSubmissionEngine:
        """Access the underlying unified CDISC submission engine."""
        return self._submission_engine

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
        return self._submission_engine.mask_blinding(sdtm_domains, adam_datasets)

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

        # 2. Deep single-call submission compilation
        package = self._submission_engine.compile_submission_package(
            study_id=study_id,
            raw_records=raw_records,
            snapshot_id=snapshot_id,
            is_unblinded=is_unblinded,
            dataset_json_version=dataset_json_version,
            study_name=f"Cadence Protocol {study_id}",
            protocol_name=f"PROTOCOL-{study_id}",
            include_vlm=True,
            include_where_clauses=True,
            include_origins=True,
        )

        completed_at = datetime.now(UTC)
        result = package.to_pipeline_execution_result(
            execution_id=exec_id,
            started_at=started_at,
            completed_at=completed_at,
        )

        # 3. Cache result and artifacts
        self._execution_history[exec_id] = result
        self._cached_packages[exec_id] = package
        self._cached_results[exec_id] = {
            "sdtm_domains": package.sdtm_domains,
            "adam_datasets": package.adam_datasets,
            "dataset_json_doc": package.dataset_json_document,
            "dataset_json_str": package.dataset_json_string,
            "define_xml_str": package.define_xml_string,
            "package_hash_sha256": package.package_hash_sha256,
        }

        # 4. Write 21 CFR Part 11 Audit Trail
        await self.write_audit_log(
            user_id=user_id,
            user_role=user_role,
            action=LakehouseAction.POOL_EXPORT,
            study_id=study_id,
            snapshot_id=snapshot_id,
            details={
                "execution_id": exec_id,
                "total_sdtm_records": package.total_sdtm_records,
                "total_adam_records": package.total_adam_records,
                "dataset_json_version": dataset_json_version,
                "define_xml_version": package.define_xml_version,
                "is_unblinded": is_unblinded,
                "package_hash_sha256": package.package_hash_sha256,
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

    def get_cached_submission_package(
        self, execution_id: str
    ) -> SubmissionPackage | None:
        """Retrieve cached complete SubmissionPackage for execution."""
        return self._cached_packages.get(execution_id)
