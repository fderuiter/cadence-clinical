"""Domain models and enums for laboratory catalogs and operations.

Requirements: PRD-SYS-001
"""

from datetime import datetime
from enum import StrEnum

from pydantic import BaseModel, Field


class LabSourceEnum(StrEnum):
    """Source of the laboratory testing.

    Requirements: PRD-SYS-001
    """

    CENTRAL = "CENTRAL"
    LOCAL = "LOCAL"


class SexApplicability(StrEnum):
    """Sex applicability for laboratory reference ranges.

    Requirements: PRD-SYS-001
    """

    M = "M"
    F = "F"
    ALL = "ALL"


class LabTestMasterRecord(BaseModel):
    """Domain record representing a single laboratory test in the master catalog.

    Requirements: PRD-SYS-001
    """

    id: str = Field(..., description="Unique database ID of the master record")
    study_id: str = Field(..., description="Unique study trial identifier")
    test_code: str = Field(
        ..., description="Standardized code for the lab test, e.g. 'HEMOGLOBIN'"
    )
    test_name: str = Field(..., description="Full descriptive name of the test")
    default_unit: str = Field(
        ..., description="The standard baseline/default unit of measurement"
    )
    normalized_unit: str = Field(
        ..., description="Standardized normalized unit of measurement"
    )
    loinc_code: str | None = Field(
        None, description="Optional LOINC identifier mapping"
    )

    # GxP 21 CFR Part 11 Audit fields
    created_at: datetime = Field(
        ..., description="Chronological timestamp of record creation"
    )
    created_by: str | None = Field(
        None, description="Identifier of user who created the record"
    )
    reason_for_change: str | None = Field(
        None, description="GxP justification for creation or change"
    )
    version_index: int = Field(
        ..., description="Incremental row version sequence counter"
    )


class LabUnitConversionRecord(BaseModel):
    """Domain record representing a standardized unit conversion configuration.

    Requirements: PRD-SYS-001
    """

    id: str = Field(..., description="Unique database ID of the conversion record")
    study_id: str = Field(..., description="Unique study trial identifier")
    test_code: str = Field(..., description="Standardized code for the lab test")
    from_unit: str = Field(
        ..., description="The original unit of measurement to convert from"
    )
    to_unit: str = Field(
        ..., description="The target unit of measurement to convert to"
    )
    factor: float = Field(
        ..., description="Multiplicative conversion multiplier factor"
    )
    offset: float | None = Field(
        None,
        description="Optional additive offset value for temperature conversions",
    )

    # GxP 21 CFR Part 11 Audit fields
    created_at: datetime = Field(
        ..., description="Chronological timestamp of record creation"
    )
    created_by: str | None = Field(
        None, description="Identifier of user who created the record"
    )
    reason_for_change: str | None = Field(
        None, description="GxP justification for creation or change"
    )
    version_index: int = Field(
        ..., description="Incremental row version sequence counter"
    )


class CertificationStatusEnum(StrEnum):
    """Accreditation and certification lifecycle status of a local laboratory."""

    ACTIVE = "ACTIVE"
    EXPIRED = "EXPIRED"
    SUSPENDED = "SUSPENDED"
    PENDING_RENEWAL = "PENDING_RENEWAL"


class GradingScaleEnum(StrEnum):
    """Toxicity grading system standard."""

    CTCAE_V5 = "CTCAE_V5"
    DAIDS_V2_1 = "DAIDS_V2_1"


class ReviewStatusEnum(StrEnum):
    """Investigator review lifecycle states for automated Grade 3/4 AE triggers."""

    STAGED_FOR_INVESTIGATOR_REVIEW = "STAGED_FOR_INVESTIGATOR_REVIEW"
    CONFIRMED_AS_AE = "CONFIRMED_AS_AE"
    DISMISSED_NOT_CLINICALLY_SIGNIFICANT = "DISMISSED_NOT_CLINICALLY_SIGNIFICANT"
    ESCALATED_SAE = "ESCALATED_SAE"


class LocalLabCertificationRecord(BaseModel):
    """Domain record representing local laboratory certification and accreditation."""

    id: str = Field(..., description="Unique database ID of the certification")
    study_id: str = Field(..., description="Unique study trial identifier")
    site_id: str = Field(..., description="Target investigator site identifier")
    lab_name: str = Field(..., description="Name of the local laboratory facility")
    lab_identifier: str | None = Field(
        None, description="Optional facility identifier code"
    )
    certification_number: str = Field(
        ..., description="Certification credential number, e.g. CLIA/CAP number"
    )
    accreditation_body: str = Field(
        ..., description="Accrediting agency (e.g. CLIA, CAP, ISO 15189)"
    )
    effective_date: datetime = Field(
        ..., description="Date on which accreditation becomes active"
    )
    expiration_date: datetime = Field(
        ..., description="Expiration date of certification"
    )
    status: CertificationStatusEnum = Field(
        default=CertificationStatusEnum.ACTIVE,
        description="Current operational status",
    )
    director_name: str | None = Field(
        None, description="Name of the laboratory director"
    )
    is_accredited: bool = Field(default=True, description="Accreditation status flag")
    notes: str | None = Field(None, description="Operational notes")

    # GxP 21 CFR Part 11 Audit fields
    created_at: datetime = Field(
        ..., description="Chronological timestamp of record creation"
    )
    created_by: str | None = Field(
        None, description="Identifier of user who created the record"
    )
    reason_for_change: str | None = Field(
        None, description="GxP justification for creation or change"
    )
    version_index: int = Field(
        ..., description="Incremental row version sequence counter"
    )


class StagedAdverseEventReviewRecord(BaseModel):
    """Domain record representing a staged Grade 3/4 AE laboratory toxicity trigger."""

    id: str = Field(..., description="Unique ID of the staged AE review")
    study_id: str = Field(..., description="Unique study trial identifier")
    site_id: str | None = Field(None, description="Investigator site identifier")
    subject_id: str = Field(..., description="Subject identifier")
    visit_id: str | None = Field(None, description="Encounter or visit identifier")
    observation_id: str | None = Field(
        None, description="Linked ClinicalObservation record ID"
    )
    form_submission_id: str | None = Field(
        None, description="Linked drafted AE FormSubmission ID"
    )
    query_id: str | None = Field(
        None, description="Linked discrepancy ClinicalQuery ID"
    )
    test_code: str = Field(..., description="Laboratory test code")
    test_name: str = Field(..., description="Descriptive test name")
    lab_value: float = Field(..., description="Reported laboratory measurement value")
    unit: str = Field(..., description="Reported unit of measurement")
    ctcae_grade: int = Field(..., description="Toxicity grade integer (3 or 4)")
    grading_scale: GradingScaleEnum = Field(
        default=GradingScaleEnum.CTCAE_V5,
        description="Protocol-pinned toxicity grading scale",
    )
    toxicity_term: str = Field(
        ..., description="Standardized adverse event verbatim/toxicity term"
    )
    clinical_description: str = Field(
        ..., description="Detailed clinical severity description"
    )
    is_hys_law_warning: bool = Field(
        default=False, description="Flag for potential Hy's Law DILI alert"
    )
    is_delta_check_alert: bool = Field(
        default=False, description="Flag for acute delta baseline deviation alert"
    )
    delta_percentage: float | None = Field(
        None, description="Percentage change from baseline"
    )
    review_status: ReviewStatusEnum = Field(
        default=ReviewStatusEnum.STAGED_FOR_INVESTIGATOR_REVIEW,
        description="Investigator workflow review status",
    )
    reviewed_by: str | None = Field(
        None, description="User ID of the reviewing Investigator"
    )
    reviewed_at: datetime | None = Field(
        None, description="Timestamp of Investigator review"
    )
    investigator_notes: str | None = Field(
        None, description="Clinical sign-off notes from Investigator"
    )
    electronic_signature_manifest: dict | None = Field(
        None, description="21 CFR Part 11 digital signature manifest"
    )

    # GxP 21 CFR Part 11 Audit fields
    created_at: datetime = Field(
        ..., description="Chronological timestamp of record creation"
    )
    created_by: str | None = Field(
        None, description="Identifier of user who created the record"
    )
    reason_for_change: str | None = Field(
        None, description="GxP justification for creation or change"
    )
    version_index: int = Field(
        ..., description="Incremental row version sequence counter"
    )
