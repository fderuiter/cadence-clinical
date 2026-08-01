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
