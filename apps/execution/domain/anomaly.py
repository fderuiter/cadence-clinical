"""Domain models for cross-domain eCRF anomaly detection.

Requirements: PRD-QRY-008
"""

import uuid
from datetime import UTC, datetime
from enum import StrEnum

from pydantic import BaseModel, Field


class CrossDomainAnomalyType(StrEnum):
    """Enumeration of cross-domain clinical anomaly and discrepancy categories."""

    AE_WITHOUT_CONCOMITANT_MED = "AE_WITHOUT_CONCOMITANT_MED"
    CONCOMITANT_MED_WITHOUT_AE = "CONCOMITANT_MED_WITHOUT_AE"
    MARKED_LAB_ABNORMALITY_WITHOUT_AE = "MARKED_LAB_ABNORMALITY_WITHOUT_AE"
    CRITICAL_VITALS_WITHOUT_AE = "CRITICAL_VITALS_WITHOUT_AE"
    DRUG_DISCONTINUATION_WITHOUT_AE = "DRUG_DISCONTINUATION_WITHOUT_AE"
    TEMPORAL_SEQUENCE_MISMATCH = "TEMPORAL_SEQUENCE_MISMATCH"
    AI_CONTEXTUAL_INCONSISTENCY = "AI_CONTEXTUAL_INCONSISTENCY"


class AnomalySeverity(StrEnum):
    """Priority and severity level of a detected cross-domain anomaly."""

    HIGH = "HIGH"
    MEDIUM = "MEDIUM"
    LOW = "LOW"


class AdjudicationAction(StrEnum):
    """Permitted Data Manager adjudication actions on staged candidate queries."""

    APPROVE = "APPROVE"
    REJECT = "REJECT"


class CrossDomainAnomaly(BaseModel):
    """Domain representation of an identified cross-domain inconsistency or anomaly."""

    anomaly_id: str = Field(
        default_factory=lambda: str(uuid.uuid4()),
        description="Unique identifier for the detected anomaly instance.",
    )
    anomaly_type: CrossDomainAnomalyType = Field(
        ...,
        description="Category of the cross-domain inconsistency.",
    )
    study_id: str = Field(
        ...,
        description="Clinical study scope identifier.",
    )
    subject_id: str = Field(
        ...,
        description="Clinical subject identifier.",
    )
    site_id: str | None = Field(
        default=None,
        description="Clinical site identifier if known.",
    )
    visit_id: str | None = Field(
        default=None,
        description="Clinical visit identifier associated with primary finding.",
    )
    primary_domain: str = Field(
        ...,
        description="Primary CDISC domain (e.g. AE, CM, LB, VS, DS, EX).",
    )
    primary_test_code: str = Field(
        ...,
        description="Primary observation test code.",
    )
    correlated_domain: str = Field(
        ...,
        description="Correlated CDISC domain that exhibits inconsistency.",
    )
    correlated_test_code: str | None = Field(
        default=None,
        description="Optional correlated test code in the related domain.",
    )
    severity: AnomalySeverity = Field(
        default=AnomalySeverity.MEDIUM,
        description="Clinical severity or query priority.",
    )
    message: str = Field(
        ...,
        description="Short, human-readable summary of the discrepancy.",
    )
    explanation: str = Field(
        ...,
        description="Detailed clinical explanation and rationale.",
    )
    confidence_score: float = Field(
        default=1.0,
        ge=0.0,
        le=1.0,
        description="Confidence score (1.0 for deterministic rules, 0.0-1.0 for AI reasoners).",
    )
    model_identifier: str | None = Field(
        default=None,
        description="AI model identifier if generated or assisted by AI Gateway.",
    )
    prompt_hash: str | None = Field(
        default=None,
        description="Cryptographic SHA-256 prompt hash for AI Gateway attribution.",
    )
    observation_ids: list[str] = Field(
        default_factory=list,
        description="List of associated ClinicalObservation IDs involved in this finding.",
    )
    form_id: str | None = Field(
        default=None,
        description="Associated eCRF form or page ID.",
    )
    field_id: str | None = Field(
        default=None,
        description="Associated form field ID.",
    )


class AnomalyEvaluationResult(BaseModel):
    """Aggregate result of evaluating cross-domain anomalies for a subject or study."""

    subject_id: str = Field(..., description="Subject identifier evaluated.")
    study_id: str = Field(..., description="Study identifier evaluated.")
    anomalies: list[CrossDomainAnomaly] = Field(
        default_factory=list,
        description="List of detected anomalies.",
    )
    evaluated_at: datetime = Field(
        default_factory=lambda: datetime.now(UTC),
        description="Timestamp of evaluation.",
    )
    queries_staged_count: int = Field(
        default=0,
        description="Count of new CANDIDATE queries successfully staged in the database.",
    )
