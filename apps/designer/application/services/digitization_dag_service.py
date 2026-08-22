"""Asynchronous Protocol Digitization Stage DAG Orchestrator and Runner.

Decomposes clinical protocol ingestion into checkpointed, resumable transformations:
1. Document Layout Parsing (LAYOUT_PARSING)
2. Schedule of Activities & Timeline Extraction (SOA_EXTRACTION)
3. Biomedical Concept & Criteria Mapping (BIOMEDICAL_CONCEPT_MAPPING)
4. eCRF Synthesis & Edit Check Rules (ECRF_SYNTHESIS)
5. CDISC USDM v4.0 Graph Compilation (USDM_COMPILATION)

Enforces strict Pydantic v2 validation gates at each stage boundary.

Requirements: PRD-DDF-001, PRD-SYS-001, PRD-MDR-007, PRD-CRF-004, PRD-CRF-005
"""

from __future__ import annotations

import logging
import time
import uuid
from datetime import UTC, datetime
from typing import Any

from pydantic import ValidationError

from apps.designer.application.services.digitization_service import (
    _heuristic_protocol_extraction,
    extract_text_from_document,
    synthesize_ecrf_forms,
    validate_extracted_rules,
)
from apps.designer.domain.digitization_dag_models import (
    ConceptMappingCheckpoint,
    DigitizationJob,
    DigitizationJobStatus,
    DigitizationStage,
    ECRFSynthesisCheckpoint,
    LayoutParsingCheckpoint,
    SoAExtractionCheckpoint,
    StageCheckpoint,
    StageGateStatus,
    USDMCompilationCheckpoint,
)
from apps.designer.domain.digitization_models import (
    ExtractedActivity,
    ExtractedArm,
    ExtractedCriterion,
    ExtractedEpoch,
    ExtractedVisit,
    SynthesizedECRFForm,
    USDMProtocolExtractionResponse,
)
from apps.designer.domain.ports import DigitizationJobRepositoryPort

logger = logging.getLogger(__name__)

# Ordered execution stages for the Protocol Digitization DAG
STAGE_EXECUTION_ORDER: list[DigitizationStage] = [
    DigitizationStage.LAYOUT_PARSING,
    DigitizationStage.SOA_EXTRACTION,
    DigitizationStage.BIOMEDICAL_CONCEPT_MAPPING,
    DigitizationStage.ECRF_SYNTHESIS,
    DigitizationStage.USDM_COMPILATION,
]

STAGE_PROGRESS_MAP: dict[DigitizationStage, int] = {
    DigitizationStage.LAYOUT_PARSING: 20,
    DigitizationStage.SOA_EXTRACTION: 40,
    DigitizationStage.BIOMEDICAL_CONCEPT_MAPPING: 60,
    DigitizationStage.ECRF_SYNTHESIS: 80,
    DigitizationStage.USDM_COMPILATION: 100,
}


class DigitizationDAGRunner:
    """Orchestrates asynchronous execution of the Protocol Digitization DAG pipeline."""

    def __init__(self, job_store: DigitizationJobRepositoryPort | None = None) -> None:
        """Initializes the DAG runner with an underlying job repository port.

        Args:
            job_store: Optional DigitizationJobRepositoryPort instance.
        """
        self.job_store = job_store

    @property
    def store(self) -> DigitizationJobRepositoryPort:
        """Ensures repository port is available for job operations."""
        if self.job_store is None:
            raise RuntimeError(
                "DigitizationJobRepositoryPort was not configured for DigitizationDAGRunner."
            )
        return self.job_store

    async def initialize_job(
        self,
        file_content: bytes,
        filename: str,
        study_id: str | None = None,
        user_id: str = "system",
    ) -> DigitizationJob:
        """Parses document text, creates a new DigitizationJob, and persists it in PENDING state.

        Args:
            file_content: Raw byte stream of uploaded protocol document.
            filename: Name of the uploaded file.
            study_id: Optional clinical study ID.
            user_id: User ID initiating the ingestion.

        Returns:
            Initialized DigitizationJob record.
        """
        job_id = f"job_dag_{uuid.uuid4().hex[:12]}"
        raw_text = extract_text_from_document(file_content, filename)

        job = DigitizationJob(
            job_id=job_id,
            study_id=study_id,
            filename=filename,
            file_size_bytes=len(file_content),
            raw_text=raw_text,
            status=DigitizationJobStatus.PENDING,
            current_stage=None,
            created_by=user_id,
        )
        return await self.store.create_job(job)

    async def run_job(
        self,
        job_id: str,
        start_from_stage: DigitizationStage | None = None,
    ) -> DigitizationJob:
        """Executes the DAG stages sequentially with validation gates and checkpointing.

        Args:
            job_id: Unique job identifier.
            start_from_stage: Optional stage to start execution from (resumption).

        Returns:
            Updated DigitizationJob reflecting final status and checkpoints.
        """
        job = await self.store.get_job(job_id)
        if not job:
            raise KeyError(f"Digitization job '{job_id}' not found.")

        job.status = DigitizationJobStatus.RUNNING
        job.error_message = None
        await self.store.update_job(job)

        # Determine starting stage index
        start_idx = 0
        if start_from_stage:
            try:
                start_idx = STAGE_EXECUTION_ORDER.index(start_from_stage)
            except ValueError:
                start_idx = 0

        stages_to_run = STAGE_EXECUTION_ORDER[start_idx:]

        for stage in stages_to_run:
            job.current_stage = stage
            await self.store.update_job(job)

            stage_checkpoint = await self._execute_stage(stage, job)
            job = await self.store.save_checkpoint(job_id, stage_checkpoint)

            # Check schema validation gate
            if stage_checkpoint.gate_status != StageGateStatus.PASSED:
                error_summary = "; ".join(stage_checkpoint.gate_errors) or (
                    f"Stage {stage.value} schema validation gate failed."
                )
                job.status = DigitizationJobStatus.FAILED
                job.error_message = error_summary
                await self.store.update_job(job)
                logger.error(
                    "Digitization job %s failed at stage %s: %s",
                    job_id,
                    stage.value,
                    error_summary,
                )
                return job

        # All stages passed successfully
        usdm_checkpoint = job.checkpoints.get(DigitizationStage.USDM_COMPILATION.value)
        ecrf_checkpoint = job.checkpoints.get(DigitizationStage.ECRF_SYNTHESIS.value)

        if usdm_checkpoint and "usdm_extraction" in usdm_checkpoint.data:
            job.final_usdm_payload = USDMProtocolExtractionResponse(
                **usdm_checkpoint.data["usdm_extraction"]
            )

        if ecrf_checkpoint and "synthesized_forms" in ecrf_checkpoint.data:
            job.synthesized_forms = [
                form
                if isinstance(form, SynthesizedECRFForm)
                else SynthesizedECRFForm(**form)
                for form in ecrf_checkpoint.data["synthesized_forms"]
            ]

        job.status = DigitizationJobStatus.COMPLETED
        job.error_message = None
        await self.store.update_job(job)
        logger.info(
            "Digitization job %s completed all %d stages successfully.",
            job_id,
            len(STAGE_EXECUTION_ORDER),
        )
        return job

    async def _execute_stage(
        self, stage: DigitizationStage, job: DigitizationJob
    ) -> StageCheckpoint:
        """Executes a single DAG stage with timer, schema validation gate, and diagnostic capture.

        Args:
            stage: The DigitizationStage to execute.
            job: Current DigitizationJob state.

        Returns:
            Completed StageCheckpoint.
        """
        started_at = datetime.now(UTC)
        t0 = time.perf_counter()
        gate_errors: list[str] = []
        gate_status = StageGateStatus.PASSED
        checkpoint_data: dict[str, Any] = {}
        confidence = 0.95

        try:
            if stage == DigitizationStage.LAYOUT_PARSING:
                checkpoint_data, confidence = await self._run_layout_parsing(job)
                # Gate validation
                LayoutParsingCheckpoint(**checkpoint_data)

            elif stage == DigitizationStage.SOA_EXTRACTION:
                checkpoint_data, confidence = await self._run_soa_extraction(job)
                # Gate validation
                SoAExtractionCheckpoint(**checkpoint_data)

            elif stage == DigitizationStage.BIOMEDICAL_CONCEPT_MAPPING:
                checkpoint_data, confidence = await self._run_concept_mapping(job)
                # Gate validation
                ConceptMappingCheckpoint(**checkpoint_data)

            elif stage == DigitizationStage.ECRF_SYNTHESIS:
                checkpoint_data, confidence = await self._run_ecrf_synthesis(job)
                # Gate validation
                ECRFSynthesisCheckpoint(**checkpoint_data)

            elif stage == DigitizationStage.USDM_COMPILATION:
                checkpoint_data, confidence = await self._run_usdm_compilation(job)
                # Gate validation
                USDMCompilationCheckpoint(**checkpoint_data)

        except ValidationError as val_err:
            gate_status = StageGateStatus.FAILED
            gate_errors = [f"{e['loc']}: {e['msg']}" for e in val_err.errors()]
            logger.warning(
                "Stage gate validation error in %s: %s", stage.value, gate_errors
            )
        except Exception as exc:
            gate_status = StageGateStatus.FAILED
            gate_errors = [f"Unhandled stage execution error: {str(exc)}"]
            logger.exception("Stage execution exception in %s: %s", stage.value, exc)

        duration_ms = (time.perf_counter() - t0) * 1000.0
        completed_at = datetime.now(UTC)

        return StageCheckpoint(
            stage=stage,
            status=gate_status,
            started_at=started_at,
            completed_at=completed_at,
            duration_ms=round(duration_ms, 2),
            gate_status=gate_status,
            gate_errors=gate_errors,
            confidence_score=confidence,
            data=checkpoint_data,
        )

    # -------------------------------------------------------------------------
    # STAGE HANDLERS
    # -------------------------------------------------------------------------

    async def _run_layout_parsing(
        self, job: DigitizationJob
    ) -> tuple[dict[str, Any], float]:
        """Stage 1: Extracts document sections, title, protocol ID, phase, and therapeutic area."""
        extracted = _heuristic_protocol_extraction(job.raw_text, job.filename)
        words = job.raw_text.split()
        word_count = len(words)

        sections: dict[str, str] = {}
        for line in job.raw_text.split("\n"):
            line_str = line.strip()
            if line_str.startswith("Section ") or ":" in line_str:
                parts = line_str.split(":", 1)
                if len(parts) == 2 and len(parts[0]) < 60:
                    sections[parts[0].strip()] = parts[1].strip()

        data = {
            "protocol_title": extracted.study_title,
            "protocol_id": extracted.protocol_id,
            "phase": extracted.phase,
            "therapeutic_area": extracted.therapeutic_area,
            "sections": sections,
            "word_count": word_count,
            "detected_page_count": max(1, word_count // 300),
            "confidence_score": extracted.confidence_score,
        }
        return data, extracted.confidence_score

    async def _run_soa_extraction(
        self, job: DigitizationJob
    ) -> tuple[dict[str, Any], float]:
        """Stage 2: Extracts Arms, Epochs, Encounters/Visits, and SoA activity schedules."""
        extracted = _heuristic_protocol_extraction(job.raw_text, job.filename)
        data = {
            "arms": [a.model_dump() for a in extracted.arms],
            "epochs": [e.model_dump() for e in extracted.epochs],
            "visits": [v.model_dump() for v in extracted.visits],
            "activities": [act.model_dump() for act in extracted.activities],
            "confidence_score": extracted.confidence_score,
        }
        return data, extracted.confidence_score

    async def _run_concept_mapping(
        self, job: DigitizationJob
    ) -> tuple[dict[str, Any], float]:
        """Stage 3: Maps procedures to CDISC Biomedical Concepts and compiles Eligibility Criteria."""
        extracted = _heuristic_protocol_extraction(job.raw_text, job.filename)
        mapped_count = sum(
            1 for act in extracted.activities if act.biomedical_concept_code
        )
        data = {
            "mapped_activities": [act.model_dump() for act in extracted.activities],
            "criteria": [c.model_dump() for c in extracted.criteria],
            "concept_codes_mapped": mapped_count,
            "confidence_score": extracted.confidence_score,
        }
        return data, extracted.confidence_score

    async def _run_ecrf_synthesis(
        self, job: DigitizationJob
    ) -> tuple[dict[str, Any], float]:
        """Stage 4: Synthesizes CDASH eCRF forms, widgets, and verifies circular edit check rules."""
        extracted = _heuristic_protocol_extraction(job.raw_text, job.filename)
        forms = synthesize_ecrf_forms(extracted)

        all_rules: list[dict[str, Any]] = []
        for f in forms:
            all_rules.extend(f.rules)

        cycles = validate_extracted_rules(all_rules)

        data = {
            "synthesized_forms": [f.model_dump() for f in forms],
            "rule_count": len(all_rules),
            "cycle_detected": len(cycles) > 0,
            "cycle_messages": cycles,
            "confidence_score": extracted.confidence_score,
        }
        return data, extracted.confidence_score

    async def _run_usdm_compilation(
        self, job: DigitizationJob
    ) -> tuple[dict[str, Any], float]:
        """Stage 5: Integrates extracted components into canonical USDM v4.0 study model."""
        # Read from prior checkpoints if available
        layout_ckpt = job.checkpoints.get(DigitizationStage.LAYOUT_PARSING.value)
        soa_ckpt = job.checkpoints.get(DigitizationStage.SOA_EXTRACTION.value)
        concept_ckpt = job.checkpoints.get(
            DigitizationStage.BIOMEDICAL_CONCEPT_MAPPING.value
        )

        title = (
            layout_ckpt.data.get("protocol_title")
            if layout_ckpt
            else "Compiled Clinical Study"
        )
        proto_id = (
            layout_ckpt.data.get("protocol_id") if layout_ckpt else "CDNC-2026-001"
        )
        phase = layout_ckpt.data.get("phase") if layout_ckpt else "PHASE_II"
        ta = layout_ckpt.data.get("therapeutic_area") if layout_ckpt else "Oncology"

        arms = (
            [ExtractedArm(**a) for a in soa_ckpt.data.get("arms", [])]
            if soa_ckpt
            else []
        )
        epochs = (
            [ExtractedEpoch(**e) for e in soa_ckpt.data.get("epochs", [])]
            if soa_ckpt
            else []
        )
        visits = (
            [ExtractedVisit(**v) for v in soa_ckpt.data.get("visits", [])]
            if soa_ckpt
            else []
        )

        activities = (
            [
                ExtractedActivity(**act)
                for act in concept_ckpt.data.get("mapped_activities", [])
            ]
            if concept_ckpt
            else []
        )
        criteria = (
            [ExtractedCriterion(**c) for c in concept_ckpt.data.get("criteria", [])]
            if concept_ckpt
            else []
        )

        if not arms or not epochs:
            # Fallback to heuristic extract
            extracted = _heuristic_protocol_extraction(job.raw_text, job.filename)
            arms = extracted.arms
            epochs = extracted.epochs
            visits = extracted.visits
            activities = extracted.activities
            criteria = extracted.criteria

        confidence = (
            layout_ckpt.confidence_score if layout_ckpt else extracted.confidence_score
        )

        usdm_model = USDMProtocolExtractionResponse(
            study_title=title,
            protocol_id=proto_id,
            phase=phase,
            therapeutic_area=ta,
            arms=arms,
            epochs=epochs,
            visits=visits,
            activities=activities,
            criteria=criteria,
            confidence_score=confidence,
        )

        study_id = job.study_id or f"study_{proto_id.lower().replace('-', '_')}"
        version_id = f"{study_id}_v1"

        total_nodes = (
            1 + len(epochs) + len(arms) + len(visits) + len(activities) + len(criteria)
        )
        total_rels = (
            len(epochs)
            + len(arms)
            + len(visits)
            + len(activities)
            + sum(len(a.assigned_visit_names) for a in activities)
            + len(criteria)
        )

        data = {
            "usdm_extraction": usdm_model.model_dump(),
            "study_id": study_id,
            "version_id": version_id,
            "nodes_created": total_nodes,
            "relationships_created": total_rels,
            "status": "READY_FOR_COMMIT",
        }
        return data, confidence
