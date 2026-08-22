"""Pydantic request and response schemas for Interop microservice presentation layer."""

from datetime import datetime
from enum import StrEnum
from typing import Any

from pydantic import BaseModel, ConfigDict, Field, field_validator


class FHIRPrefillRequest(BaseModel):
    """Payload for pre-filling CDASH fields using a FHIR bundle."""

    study_id: str = Field(..., description="Unique identifier of the clinical study")
    bundle: dict[str, Any] = Field(
        ..., description="The standard FHIR Bundle JSON payload"
    )


class FHIRPreScreenRequest(BaseModel):
    """Payload for advisory FHIR eligibility pre-screening."""

    study_id: str = Field(..., description="Unique identifier of the clinical study")
    bundle: dict[str, Any] = Field(
        ..., description="The standard FHIR Bundle JSON payload"
    )


class CriterionExplanation(BaseModel):
    criterion_id: str = Field(..., description="The ID of the criterion evaluated.")
    criterion_type: str = Field(..., description="inclusion or exclusion.")
    description: str = Field(..., description="Human-readable text of the criterion.")
    is_met: bool = Field(
        ..., description="Indicates if the subject satisfies this criterion."
    )
    is_indeterminate: bool = Field(
        ..., description="Indicates if evaluation was indeterminate."
    )


class FHIRPreScreenResponse(BaseModel):
    eligible: bool | None = Field(
        None,
        description="Aggregated eligibility. True if all criteria met, False if any failed, None if indeterminate.",
    )
    failed_criteria: list[str] = Field(
        default_factory=list, description="List of criterion IDs that failed."
    )
    indeterminate_criteria: list[str] = Field(
        default_factory=list,
        description="List of criterion IDs that were indeterminate.",
    )
    criteria_explanations: list[CriterionExplanation] = Field(
        default_factory=list,
        description="Detailed list of criterion-level explanations.",
    )


class ConflictStrategy(StrEnum):
    """Explicit validated conflict resolution strategies."""

    CLIENT_WINS = "CLIENT_WINS"
    SERVER_WINS = "SERVER_WINS"
    MERGE = "MERGE"


class OfflineSyncMarkers(BaseModel):
    """Offline queue reconciliation and conflict resolution parameters."""

    sequence_number: int = Field(
        ..., description="The queue order sequence from device"
    )
    client_id: str = Field(..., description="Unique identifier for the mobile device")
    conflict_strategy: ConflictStrategy = Field(
        ConflictStrategy.CLIENT_WINS,
        description="Conflict strategy to resolve duplicate submissions. Supported: CLIENT_WINS, SERVER_WINS, MERGE",
    )
    signature: str | None = Field(
        None,
        description="Optional HMAC-SHA256 signature of the payload for cryptographic integrity",
    )
    timestamps: dict[str, datetime] | None = Field(
        None,
        description="Optional per-field UTC timestamps indicating when each field in 'answers' was modified",
    )

    @field_validator("conflict_strategy", mode="before")
    @classmethod
    def normalize_conflict_strategy(cls, v: Any) -> Any:
        if isinstance(v, str):
            v_upper = v.upper()
            if v_upper in ConflictStrategy.__members__:
                return ConflictStrategy[v_upper]
        return v


class EPROSubmissionPayload(BaseModel):
    """A single participant ePRO/eCOA diary submission."""

    subject_id: str = Field(..., description="Pseudonymized identifier of the subject")
    diary_id: str = Field(..., description="Unique identifier for the diary or survey")
    version_index: int = Field(
        1, description="The version index of the instrument used for compilation"
    )
    device_timestamp: datetime = Field(
        ..., description="ISO 8601 timestamp when the entry was created on device"
    )
    answers: dict[str, Any] = Field(
        ..., description="The questionnaire response key-values"
    )
    offline_sync_markers: OfflineSyncMarkers = Field(
        ..., description="The offline sync queue conflict tracking parameters"
    )


class BulkSyncPayload(BaseModel):
    """A bulk list of ePRO submissions for offline queue reconciliation."""

    submissions: list[EPROSubmissionPayload] = Field(
        ..., description="A list of queued ePRO submissions"
    )


class SubjectNotificationResponse(BaseModel):
    id: str
    subject_id: str
    assignment_id: str | None
    due_at: datetime
    channel: str
    delivery_status: str
    is_read: bool
    read_at: datetime | None
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int

    model_config = ConfigDict(from_attributes=True)


class AcknowledgeNotificationRequest(BaseModel):
    reason_for_change: str = Field(
        ..., description="21 CFR Part 11 compliant reason for change"
    )


class QuarantinedSubmissionResponse(BaseModel):
    id: str
    subject_id: str
    diary_id: str
    device_timestamp: datetime
    answers: dict[str, Any]
    original_answers: dict[str, Any]
    offline_sync_markers: dict[str, Any]
    validation_errors: list[str]
    status: str
    triage_history: list[dict[str, Any]]
    created_at: datetime
    updated_at: datetime

    model_config = ConfigDict(from_attributes=True)


class EditQuarantinedSubmissionRequest(BaseModel):
    answers: dict[str, Any] = Field(..., description="The edited ePRO/eCOA answers")
    password: str = Field(
        ...,
        description="The password for 21 CFR Part 11 compliant digital signature verification",
    )
    change_reason: str = Field(
        ..., description="Standard 21 CFR Part 11 compliant reason for the edit"
    )


class ReplayQuarantinedSubmissionRequest(BaseModel):
    password: str = Field(
        ...,
        description="The password for 21 CFR Part 11 compliant digital signature verification",
    )
    change_reason: str = Field(
        ..., description="Standard 21 CFR Part 11 compliant reason for the replay"
    )


class FHIRSemanticMapRequest(BaseModel):
    """Payload for executing hybrid semantic mapping on a FHIR bundle."""

    study_id: str = Field(..., description="Unique identifier of the clinical study")
    bundle: dict[str, Any] = Field(
        ..., description="The standard FHIR Bundle JSON payload"
    )
    enable_deterministic: bool = Field(
        default=True, description="Whether to execute Tier 1 deterministic ConceptMaps"
    )
    enable_embedding: bool = Field(
        default=True,
        description="Whether to execute Tier 2 embedding cosine similarity matching",
    )
    enable_llm_fallback: bool = Field(
        default=True,
        description="Whether to execute Tier 3 LLM semantic reasoning for narrative text",
    )
    embedding_confidence_threshold: float = Field(
        default=0.82,
        ge=0.0,
        le=1.0,
        description="Minimum cosine similarity threshold for embedding tier",
    )
    llm_confidence_threshold: float = Field(
        default=0.60,
        ge=0.0,
        le=1.0,
        description="Minimum confidence threshold for LLM extraction tier",
    )
    human_review_confidence_threshold: float = Field(
        default=0.75,
        ge=0.0,
        le=1.0,
        description="Threshold below which mappings are flagged for human Data Manager review",
    )


class SemanticMappedItemDTO(BaseModel):
    """Individual mapped clinical observation or variable."""

    source_resource_type: str
    source_id: str | None = None
    source_code: str | None = None
    source_system: str | None = None
    source_display: str | None = None
    target_domain: str
    target_variable: str
    cdash_testcd: str | None = None
    cdash_test: str | None = None
    extracted_value: Any = None
    extracted_unit: str | None = None
    observation_date: datetime | str | None = None
    mapping_tier: str
    confidence_score: float
    provenance: str
    needs_human_review: bool = False
    status: str = "MAPPED"

    model_config = ConfigDict(extra="ignore")


class MappingTierStatisticsDTO(BaseModel):
    """Aggregated metrics across mapping tiers."""

    total_extracted: int = 0
    deterministic_count: int = 0
    embedding_count: int = 0
    llm_fallback_count: int = 0
    unmapped_count: int = 0
    flagged_for_review_count: int = 0
    execution_latency_ms: float = 0.0


class FHIRSemanticMapResponse(BaseModel):
    """Response payload for hybrid FHIR semantic mapping."""

    study_id: str
    subject_pseudonym: str
    mapped_fields: dict[str, Any] = Field(
        default_factory=dict,
        description="Flattened eCRF context dictionary for form pre-filling",
    )
    mapped_items: list[SemanticMappedItemDTO] = Field(
        default_factory=list,
        description="Detailed list of all mapped items with confidence and provenance",
    )
    clinical_records: dict[str, list[dict[str, Any]]] = Field(
        default_factory=dict,
        description="Grouped clinical domain records",
    )
    statistics: MappingTierStatisticsDTO = Field(
        default_factory=MappingTierStatisticsDTO,
        description="Mapping tier telemetry and counts",
    )

    model_config = ConfigDict(extra="ignore")


class ConceptMapSummaryDTO(BaseModel):
    """Metadata summary of a single ConceptMap element."""

    source_system: str
    source_code: str
    source_display: str
    target_domain: str
    target_variable: str
    cdash_testcd: str
    cdash_test: str
    standard_unit: str | None = None
    category: str | None = None
    description: str | None = None


class ConceptMapListResponse(BaseModel):
    """Response returning the registry of supported ConceptMaps."""

    total_concepts: int
    domains_supported: list[str]
    concept_maps: list[ConceptMapSummaryDTO]
