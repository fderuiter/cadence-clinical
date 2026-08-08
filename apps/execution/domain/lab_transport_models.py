"""Pydantic transport schemas for laboratory master, conversions, and reference ranges.

Requirements: PRD-SYS-001
"""

from datetime import datetime

from pydantic import BaseModel, Field

from .lab_models import (
    LabSourceEnum,
)


class LabReferenceRangeCreate(BaseModel):
    """Payload to create a new clinical laboratory reference range.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(
        ..., description="Unique protocol study identifier, e.g. 'STUDY-101'"
    )
    test_code: str = Field(
        ..., description="Standardized laboratory test code, e.g. 'WBC'"
    )
    test_name: str = Field(..., description="Descriptive name of the test parameter")
    source: LabSourceEnum = Field(
        ..., description="Source type of the range, either 'CENTRAL' or 'LOCAL'"
    )
    site_id: str | None = Field(
        None, description="Optional investigator site identifier"
    )
    unit: str = Field(..., description="The original/captured unit of measurement")
    normalized_unit: str = Field(
        ..., description="Standardized target unit of measurement"
    )
    sex_applicability: str = Field(
        ..., description="Sex applicability of the reference range"
    )
    age_low: float | None = Field(
        None, description="Nullable lower bound of age applicability"
    )
    age_high: float | None = Field(
        None, description="Nullable upper bound of age applicability"
    )
    low_bound: float | None = Field(
        None, description="Nullable lower limit of normal range"
    )
    high_bound: float | None = Field(
        None, description="Nullable upper limit of normal range"
    )
    critical_low: float | None = Field(
        None, description="Nullable lower limit of critical/panic alert range"
    )
    critical_high: float | None = Field(
        None, description="Nullable upper limit of critical/panic alert range"
    )
    reason_for_change: str = Field(
        ..., description="Mandatory GxP 21 CFR Part 11 justification reason"
    )


class LabReferenceRangeUpdate(BaseModel):
    """Payload to modify an existing laboratory reference range.

    Requirements: PRD-SYS-001
    """

    study_id: str | None = Field(None, description="Unique protocol study identifier")
    test_code: str | None = Field(None, description="Standardized laboratory test code")
    test_name: str | None = Field(
        None, description="Updated descriptive name of the test"
    )
    source: LabSourceEnum | None = Field(
        None, description="Updated source type of the range"
    )
    site_id: str | None = Field(
        None, description="Updated investigator site identifier"
    )
    unit: str | None = Field(None, description="Updated original unit of measurement")
    normalized_unit: str | None = Field(
        None, description="Updated standardized unit of measurement"
    )
    sex_applicability: str | None = Field(None, description="Updated sex applicability")
    age_low: float | None = Field(
        None, description="Updated lower bound of age applicability"
    )
    age_high: float | None = Field(
        None, description="Updated upper bound of age applicability"
    )
    low_bound: float | None = Field(
        None, description="Updated lower limit of normal range"
    )
    high_bound: float | None = Field(
        None, description="Updated upper limit of normal range"
    )
    critical_low: float | None = Field(
        None, description="Updated lower limit of critical alert range"
    )
    critical_high: float | None = Field(
        None, description="Updated upper limit of critical alert range"
    )
    reason_for_change: str = Field(
        ..., description="Mandatory GxP 21 CFR Part 11 justification reason"
    )


class LabReferenceRangeResponse(BaseModel):
    """Response payload representing a registered laboratory reference range.

    Requirements: PRD-SYS-001
    """

    id: str = Field(..., description="Unique database identifier of the range")
    study_id: str = Field(..., description="Unique trial study identifier")
    test_code: str = Field(..., description="Standardized lab test code")
    test_name: str = Field(..., description="Descriptive test name")
    source: LabSourceEnum = Field(..., description="Source type of the range")
    site_id: str | None = Field(
        None, description="Optional investigator site identifier"
    )
    unit: str = Field(..., description="Original unit of measurement")
    normalized_unit: str = Field(..., description="Standardized unit")
    sex_applicability: str = Field(..., description="Sex applicability")
    age_low: float | None = Field(None, description="Lower age applicability limit")
    age_high: float | None = Field(None, description="Upper age applicability limit")
    low_bound: float | None = Field(None, description="Lower normal limit")
    high_bound: float | None = Field(None, description="Upper normal limit")
    critical_low: float | None = Field(None, description="Lower critical bound")
    critical_high: float | None = Field(None, description="Upper critical bound")
    version: int = Field(
        ..., description="Optimistic locking entity version identifier"
    )
    is_deleted: bool = Field(
        ..., description="Flag indicating if the record is soft-deleted"
    )


class LabTestMasterCreate(BaseModel):
    """Payload to create a new laboratory test master catalog entry.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Unique trial study identifier")
    test_code: str = Field(
        ..., description="Unique standardized test code, e.g. 'HEMOGLOBIN'"
    )
    test_name: str = Field(..., description="Descriptive test name")
    default_unit: str = Field(..., description="Default/Captured unit of measurement")
    normalized_unit: str = Field(..., description="Standardized normalized target unit")
    loinc_code: str | None = Field(
        None, description="Optional LOINC dictionary standard code"
    )
    reason_for_change: str = Field(
        ..., description="Mandatory GxP 21 CFR Part 11 justification reason"
    )


class LabTestMasterResponse(BaseModel):
    """Response payload representing a registered laboratory test master catalog entry.

    Requirements: PRD-SYS-001
    """

    id: str = Field(..., description="Unique database identifier of the master")
    study_id: str = Field(..., description="Unique trial study identifier")
    test_code: str = Field(..., description="Unique standardized test code")
    test_name: str = Field(..., description="Descriptive test name")
    default_unit: str = Field(..., description="Default unit of measurement")
    normalized_unit: str = Field(..., description="Standardized normalized target unit")
    loinc_code: str | None = Field(None, description="Optional LOINC standard code")
    created_at: datetime | None = Field(
        None, description="Chronological creation timestamp"
    )
    created_by: str | None = Field(
        None, description="Identifier of the user who created this master"
    )
    reason_for_change: str | None = Field(
        None, description="GxP Part 11 justification description"
    )
    version_index: int = Field(..., description="Sequence row version identifier")
    version: int = Field(
        ..., description="Optimistic locking entity version identifier"
    )
    is_deleted: bool = Field(
        ..., description="Flag indicating if the record is soft-deleted"
    )


class LabUnitConversionCreate(BaseModel):
    """Payload to create a new laboratory unit conversion factor record.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Unique trial study identifier")
    test_code: str = Field(
        ..., description="Unique standardized test code, e.g. 'HEMOGLOBIN'"
    )
    from_unit: str = Field(..., description="Original unit of measurement")
    to_unit: str = Field(..., description="Target unit of measurement")
    factor: float = Field(
        ..., description="Multiplicative conversion multiplier factor"
    )
    offset: float | None = Field(None, description="Optional additive offset value")
    reason_for_change: str = Field(
        ..., description="Mandatory GxP 21 CFR Part 11 justification reason"
    )


class LabUnitConversionResponse(BaseModel):
    """Response payload representing a registered laboratory unit conversion factor record.

    Requirements: PRD-SYS-001
    """

    id: str = Field(..., description="Unique database identifier of the conversion")
    study_id: str = Field(..., description="Unique trial study identifier")
    test_code: str = Field(..., description="Unique standardized test code")
    from_unit: str = Field(..., description="Original unit of measurement")
    to_unit: str = Field(..., description="Target unit of measurement")
    factor: float = Field(
        ..., description="Multiplicative conversion multiplier factor"
    )
    offset: float | None = Field(None, description="Optional additive offset value")
    created_at: datetime | None = Field(
        None, description="Chronological creation timestamp"
    )
    created_by: str | None = Field(
        None, description="Identifier of the user who created this conversion"
    )
    reason_for_change: str | None = Field(
        None, description="GxP Part 11 justification description"
    )
    version_index: int = Field(..., description="Sequence row version identifier")
    version: int = Field(
        ..., description="Optimistic locking entity version identifier"
    )
    is_deleted: bool = Field(
        ..., description="Flag indicating if the record is soft-deleted"
    )


class LabRangeRecalculateRequest(BaseModel):
    """Pydantic schema for triggering lab range recalculations.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Unique trial study identifier")
    test_code: str = Field(
        ..., description="Standardized laboratory test code, e.g. 'WBC'"
    )


class LabRangeRecalculateResponse(BaseModel):
    """Pydantic schema returning recalculation status.

    Requirements: PRD-SYS-001
    """

    status: str = Field(..., description="Status of the recalculation action")
    study_id: str = Field(..., description="Unique trial study identifier")
    test_code: str = Field(..., description="Standardized laboratory test code")
    updated_count: int = Field(
        ..., description="Total number of impacted records successfully updated"
    )
