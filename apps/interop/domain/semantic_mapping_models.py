"""Domain models for Hybrid FHIR-to-CDISC Semantic Interoperability Mapping.

Requirements: PRD-CRF-007, PRD-SYS-001, PRD-SYS-051
"""

from dataclasses import dataclass, field
from datetime import datetime
from enum import StrEnum
from typing import Any

from pydantic import BaseModel, ConfigDict, Field


class MappingTier(StrEnum):
    """Execution tier utilized to resolve a FHIR-to-CDISC mapping."""

    DETERMINISTIC = "DETERMINISTIC"
    EMBEDDING = "EMBEDDING"
    LLM_FALLBACK = "LLM_FALLBACK"


class MappingStatus(StrEnum):
    """Lifecycle status of a clinical field mapping resolution."""

    MAPPED = "MAPPED"
    UNMAPPED = "UNMAPPED"
    AMBIGUOUS = "AMBIGUOUS"
    FLAGGED_FOR_REVIEW = "FLAGGED_FOR_REVIEW"


class CDISCDomain(StrEnum):
    """Standard CDISC SDTM/CDASH clinical domains."""

    DM = "DM"  # Demographics
    VS = "VS"  # Vital Signs
    LB = "LB"  # Laboratory Test Results
    MH = "MH"  # Medical History
    CM = "CM"  # Concomitant Medications
    AE = "AE"  # Adverse Events
    PE = "PE"  # Physical Examination
    PR = "PR"  # Procedures
    SV = "SV"  # Subject Visits
    QS = "QS"  # Questionnaires
    DA = "DA"  # Drug Accountability


@dataclass(frozen=True)
class ConceptMapElement:
    """Pre-compiled or dynamically learned concept mapping rule."""

    source_system: str
    source_code: str
    source_display: str
    target_domain: CDISCDomain
    target_variable: str
    cdash_testcd: str
    cdash_test: str
    standard_unit: str | None = None
    conversion_factor: float | None = None
    category: str | None = None
    description: str | None = None


class SemanticMappedItem(BaseModel):
    """An individual mapped clinical observation, variable, or narrative concept."""

    source_resource_type: str = Field(
        ..., description="FHIR resource type (Observation, Condition, etc.)"
    )
    source_id: str | None = Field(
        default=None, description="Identifier of the source FHIR resource"
    )
    source_code: str | None = Field(
        default=None, description="Source ontology code (LOINC, SNOMED, RxNorm)"
    )
    source_system: str | None = Field(
        default=None, description="Source code system URI"
    )
    source_display: str | None = Field(
        default=None, description="Source display name or verbatim text"
    )
    target_domain: CDISCDomain = Field(
        ..., description="Target CDISC domain (DM, VS, LB, MH, CM, etc.)"
    )
    target_variable: str = Field(
        ...,
        description="Full target variable notation (e.g. eCRF.VS.SYSBP or VS.SYSBP)",
    )
    cdash_testcd: str | None = Field(
        default=None, description="Standard CDASH short test code (e.g. SYSBP, GLUC)"
    )
    cdash_test: str | None = Field(
        default=None, description="Standard CDASH long test name"
    )
    extracted_value: Any = Field(
        default=None, description="Extracted and normalized clinical value"
    )
    extracted_unit: str | None = Field(
        default=None, description="Normalized clinical unit of measure"
    )
    observation_date: datetime | str | None = Field(
        default=None, description="Clinical observation timestamp or ISO date string"
    )
    mapping_tier: MappingTier = Field(
        ..., description="Execution tier that generated this mapping"
    )
    confidence_score: float = Field(
        ..., ge=0.0, le=1.0, description="Mapping confidence score (0.0 - 1.0)"
    )
    provenance: str = Field(
        ..., description="Auditable rationale or rule provenance for the mapping"
    )
    needs_human_review: bool = Field(
        default=False,
        description="Flag indicating human Data Manager review is required",
    )
    status: MappingStatus = Field(
        default=MappingStatus.MAPPED, description="Current mapping status"
    )

    model_config = ConfigDict(extra="ignore")


@dataclass
class HybridMappingConfig:
    """Runtime configuration thresholds and options for hybrid semantic mapping."""

    enable_deterministic: bool = True
    enable_embedding: bool = True
    enable_llm_fallback: bool = True
    embedding_confidence_threshold: float = 0.82
    llm_confidence_threshold: float = 0.60
    human_review_confidence_threshold: float = 0.75
    study_id: str = "DEFAULT_STUDY"
    target_domains: list[CDISCDomain] | None = None
    custom_concept_maps: list[ConceptMapElement] = field(default_factory=list)


class MappingTierStatistics(BaseModel):
    """Execution telemetry and counts per mapping tier."""

    total_extracted: int = 0
    deterministic_count: int = 0
    embedding_count: int = 0
    llm_fallback_count: int = 0
    unmapped_count: int = 0
    flagged_for_review_count: int = 0
    execution_latency_ms: float = 0.0


class FHIRSemanticMapResult(BaseModel):
    """Aggregated result of hybrid FHIR-to-CDISC semantic mapping."""

    study_id: str
    subject_pseudonym: str
    de_identified_patient: dict[str, Any] | None = None
    mapped_fields: dict[str, Any] = Field(
        default_factory=dict,
        description="Flattened eCRF context dictionary (e.g. eCRF.VS.SYSBP -> 120)",
    )
    mapped_items: list[SemanticMappedItem] = Field(
        default_factory=list,
        description="Detailed list of all mapped items with confidence and provenance",
    )
    clinical_records: dict[str, list[dict[str, Any]]] = Field(
        default_factory=dict,
        description="Grouped clinical domain records (vital_signs, labs, conditions, medications, adverse_events, procedures)",
    )
    unstructured_notes: list[dict[str, Any]] = Field(
        default_factory=list,
        description="De-identified narrative notes processed via LLM fallback",
    )
    statistics: MappingTierStatistics = Field(
        default_factory=MappingTierStatistics,
        description="Mapping tier distribution and telemetry statistics",
    )
    audit_event: dict[str, Any] | None = Field(
        default=None, description="GxP Part 11 audit event envelope"
    )

    model_config = ConfigDict(extra="ignore")
