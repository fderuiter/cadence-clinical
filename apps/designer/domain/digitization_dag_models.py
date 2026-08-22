"""Domain models and DTOs for Asynchronous Protocol Digitization Stage DAG.

Defines stage enums, checkpoint containers, validation gate schemas,
and job tracking structures for multi-stage protocol ingestion with USDM compilation.

Requirements: PRD-DDF-001, PRD-SYS-001, PRD-MDR-007, PRD-CRF-004, PRD-CRF-005
"""

from __future__ import annotations

from datetime import UTC, datetime
from enum import StrEnum
from typing import Any

from pydantic import BaseModel, ConfigDict, Field

from apps.designer.domain.digitization_models import (
    ExtractedActivity,
    ExtractedArm,
    ExtractedCriterion,
    ExtractedEpoch,
    ExtractedVisit,
    SynthesizedECRFForm,
    USDMProtocolExtractionResponse,
)


class DigitizationStage(StrEnum):
    """Enumeration of sequential stages in the Protocol Digitization DAG."""

    LAYOUT_PARSING = "LAYOUT_PARSING"
    SOA_EXTRACTION = "SOA_EXTRACTION"
    BIOMEDICAL_CONCEPT_MAPPING = "BIOMEDICAL_CONCEPT_MAPPING"
    ECRF_SYNTHESIS = "ECRF_SYNTHESIS"
    USDM_COMPILATION = "USDM_COMPILATION"


class DigitizationJobStatus(StrEnum):
    """Execution status of a protocol digitization DAG job."""

    PENDING = "PENDING"
    RUNNING = "RUNNING"
    COMPLETED = "COMPLETED"
    FAILED = "FAILED"
    CANCELLED = "CANCELLED"


class StageGateStatus(StrEnum):
    """Validation gate outcome at a stage transition boundary."""

    PASSED = "PASSED"
    FAILED = "FAILED"
    SKIPPED = "SKIPPED"


# =========================================================================
# STAGE CHECKPOINT SCHEMAS (Validation Gates)
# =========================================================================


class LayoutParsingCheckpoint(BaseModel):
    """Schema gate and checkpoint for Stage 1: Document Layout Parsing."""

    model_config = ConfigDict(populate_by_name=True, extra="ignore")

    protocol_title: str = Field(..., description="Extracted study title")
    protocol_id: str = Field(..., description="Extracted protocol ID")
    phase: str = Field(..., description="Extracted clinical development phase")
    therapeutic_area: str = Field(..., description="Extracted therapeutic area")
    sections: dict[str, str] = Field(
        default_factory=dict,
        description="Identified protocol section headers & content",
    )
    word_count: int = Field(default=0, description="Total word count in document")
    detected_page_count: int = Field(
        default=1, description="Number of parsed document pages"
    )
    confidence_score: float = Field(
        default=0.95, ge=0.0, le=1.0, description="Stage confidence score"
    )


class SoAExtractionCheckpoint(BaseModel):
    """Schema gate and checkpoint for Stage 2: Schedule of Activities & Timeline Extraction."""

    model_config = ConfigDict(populate_by_name=True, extra="ignore")

    arms: list[ExtractedArm] = Field(
        default_factory=list, description="Extracted study arms"
    )
    epochs: list[ExtractedEpoch] = Field(
        default_factory=list, description="Extracted trial epochs"
    )
    visits: list[ExtractedVisit] = Field(
        default_factory=list, description="Extracted encounters / visits"
    )
    activities: list[ExtractedActivity] = Field(
        default_factory=list,
        description="Extracted Schedule of Activities procedures",
    )
    confidence_score: float = Field(
        default=0.95, ge=0.0, le=1.0, description="Stage confidence score"
    )


class ConceptMappingCheckpoint(BaseModel):
    """Schema gate and checkpoint for Stage 3: Biomedical Concept & Criteria Mapping."""

    model_config = ConfigDict(populate_by_name=True, extra="ignore")

    mapped_activities: list[ExtractedActivity] = Field(
        default_factory=list,
        description="Activities enriched with NCI/SNOMED concept codes",
    )
    criteria: list[ExtractedCriterion] = Field(
        default_factory=list,
        description="Extracted inclusion/exclusion criteria with logical expressions",
    )
    concept_codes_mapped: int = Field(
        default=0, description="Count of activities mapped to standardized concepts"
    )
    confidence_score: float = Field(
        default=0.95, ge=0.0, le=1.0, description="Stage confidence score"
    )


class ECRFSynthesisCheckpoint(BaseModel):
    """Schema gate and checkpoint for Stage 4: eCRF Synthesis & Edit Check Rules."""

    model_config = ConfigDict(populate_by_name=True, extra="ignore")

    synthesized_forms: list[SynthesizedECRFForm] = Field(
        default_factory=list, description="Synthesized CDASH eCRF form definitions"
    )
    rule_count: int = Field(
        default=0, description="Total compiled validation / edit check rules"
    )
    cycle_detected: bool = Field(
        default=False, description="Whether circular skip-logic dependencies were found"
    )
    cycle_messages: list[str] = Field(
        default_factory=list,
        description="List of detected circular dependency descriptions",
    )
    confidence_score: float = Field(
        default=0.95, ge=0.0, le=1.0, description="Stage confidence score"
    )


class USDMCompilationCheckpoint(BaseModel):
    """Schema gate and checkpoint for Stage 5: USDM Protocol Graph Compilation."""

    model_config = ConfigDict(populate_by_name=True, extra="ignore")

    usdm_extraction: USDMProtocolExtractionResponse = Field(
        ..., description="Canonical integrated USDM extraction model"
    )
    study_id: str = Field(..., description="Target clinical study identifier")
    version_id: str = Field(..., description="Study version identifier (e.g. study_v1)")
    nodes_created: int = Field(
        default=0, description="Number of graph nodes prepared or committed"
    )
    relationships_created: int = Field(
        default=0, description="Number of graph relationships prepared or committed"
    )
    status: str = Field(
        default="READY_FOR_COMMIT",
        description="USDM compilation status (READY_FOR_COMMIT or COMMITTED)",
    )


# =========================================================================
# GENERIC STAGE CHECKPOINT & JOB STATE
# =========================================================================


class StageCheckpoint(BaseModel):
    """Immutable checkpoint record for a single stage within the DAG execution."""

    model_config = ConfigDict(populate_by_name=True, extra="ignore")

    stage: DigitizationStage = Field(..., description="DAG pipeline stage name")
    status: StageGateStatus = Field(..., description="Stage execution outcome")
    started_at: datetime = Field(..., description="Stage start UTC timestamp")
    completed_at: datetime | None = Field(
        default=None, description="Stage completion UTC timestamp"
    )
    duration_ms: float = Field(
        default=0.0, description="Elapsed execution duration in milliseconds"
    )
    gate_status: StageGateStatus = Field(
        default=StageGateStatus.PASSED, description="Schema validation gate status"
    )
    gate_errors: list[str] = Field(
        default_factory=list, description="Diagnostic schema validation error messages"
    )
    confidence_score: float = Field(
        default=1.0, ge=0.0, le=1.0, description="Calculated confidence score"
    )
    data: dict[str, Any] = Field(
        default_factory=dict, description="Checkpoint data payload dictionary"
    )


class DigitizationJob(BaseModel):
    """Complete persistent state container for a protocol digitization DAG job."""

    model_config = ConfigDict(populate_by_name=True, extra="ignore")

    job_id: str = Field(..., description="Unique DAG job identifier (UUID)")
    study_id: str | None = Field(
        default=None, description="Optional associated study ID"
    )
    filename: str = Field(..., description="Original protocol document filename")
    file_size_bytes: int = Field(
        default=0, description="Uploaded document size in bytes"
    )
    raw_text: str = Field(
        default="", description="Cached extracted raw text from document"
    )
    status: DigitizationJobStatus = Field(
        default=DigitizationJobStatus.PENDING, description="Current DAG job status"
    )
    current_stage: DigitizationStage | None = Field(
        default=None, description="Currently executing or last executed DAG stage"
    )
    created_at: datetime = Field(
        default_factory=lambda: datetime.now(UTC),
        description="Job creation UTC timestamp",
    )
    updated_at: datetime = Field(
        default_factory=lambda: datetime.now(UTC),
        description="Job last updated UTC timestamp",
    )
    created_by: str = Field(
        default="system",
        description="User ID or service principal that created the job",
    )
    checkpoints: dict[str, StageCheckpoint] = Field(
        default_factory=dict,
        description="Dictionary of completed stage checkpoints keyed by stage name",
    )
    error_message: str | None = Field(
        default=None, description="Detailed diagnostic error message on job failure"
    )
    final_usdm_payload: USDMProtocolExtractionResponse | None = Field(
        default=None, description="Compiled canonical USDM extraction payload"
    )
    synthesized_forms: list[SynthesizedECRFForm] = Field(
        default_factory=list, description="Synthesized CDASH eCRF forms"
    )


# =========================================================================
# API REQUEST & RESPONSE DTOS
# =========================================================================


class StartDAGJobResponse(BaseModel):
    """Response returned upon initiating an asynchronous protocol digitization DAG job."""

    job_id: str = Field(..., description="Assigned DAG job ID")
    status: DigitizationJobStatus = Field(..., description="Initial job status")
    current_stage: DigitizationStage | None = Field(
        default=None, description="Initial stage"
    )
    message: str = Field(..., description="Human-readable initialization message")


class DAGJobStatusResponse(BaseModel):
    """Response describing the real-time execution progress of a digitization DAG job."""

    job_id: str = Field(..., description="Unique DAG job identifier")
    study_id: str | None = Field(default=None, description="Associated study ID")
    status: DigitizationJobStatus = Field(..., description="Current job status")
    current_stage: DigitizationStage | None = Field(
        default=None, description="Current or last executed stage"
    )
    progress_pct: int = Field(
        ..., ge=0, le=100, description="Overall completion progress percentage"
    )
    created_at: datetime = Field(..., description="Creation UTC timestamp")
    updated_at: datetime = Field(..., description="Last updated UTC timestamp")
    checkpoints: dict[str, StageCheckpoint] = Field(
        default_factory=dict, description="Stage checkpoints record"
    )
    error_message: str | None = Field(
        default=None, description="Error details if job failed"
    )
    is_terminal: bool = Field(
        ..., description="True if job has reached COMPLETED, FAILED, or CANCELLED state"
    )
    final_usdm_payload: USDMProtocolExtractionResponse | None = Field(
        default=None, description="Final extracted USDM payload when completed"
    )
    synthesized_forms: list[SynthesizedECRFForm] = Field(
        default_factory=list, description="Synthesized eCRFs when completed"
    )


class ResumeDAGJobRequest(BaseModel):
    """Request payload to resume a paused or failed DAG job from a specific stage."""

    from_stage: DigitizationStage | None = Field(
        default=None,
        description="Optional stage to restart from. If omitted, resumes from the failed/next stage.",
    )
    change_reason: str | None = Field(
        default=None, description="Optional reason for resumption"
    )


class CompileUSDMFromJobRequest(BaseModel):
    """Request to commit the USDM protocol graph generated by a completed DAG job into Neo4j."""

    study_id: str = Field(..., description="Target clinical study ID")
    change_reason: str = Field(..., description="GxP change justification reason")
