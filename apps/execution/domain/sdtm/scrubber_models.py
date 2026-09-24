"""Pydantic v2 schemas for de-identification scrubber configuration and summaries.

Requirements: PRD-SYS-001
"""

from pydantic import BaseModel, Field


class DeidentConfig(BaseModel):
    """Configuration for de-identification process.

    Requirements: PRD-SYS-001
    """

    study_salt: str = Field(
        ...,
        description="The study-specific salt used for deterministic pseudonymization and date-shifting.",
    )
    enable_date_shift: bool = Field(
        ..., description="Flag to enable or disable date-shifting."
    )
    max_date_shift_days: int = Field(
        default=365,
        description="The maximum number of days to shift dates, default is 365.",
    )
    scrub_free_text: bool = Field(
        ..., description="Flag to enable or disable free-text PII scrubbing."
    )


class DeidentSummary(BaseModel):
    """Summary metrics of the de-identification run.

    Requirements: PRD-SYS-001
    """

    records_processed: int = Field(
        default=0, description="Total number of records processed."
    )
    fields_pseudonymized: int = Field(
        default=0, description="Total number of fields pseudonymized."
    )
    dates_shifted: int = Field(default=0, description="Total number of dates shifted.")
