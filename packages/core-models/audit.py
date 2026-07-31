"""
Standard Part 11 compliant audit and metadata fields for Pydantic v2.
"""

from datetime import datetime, timezone

from datetime_helpers import AwareDatetime
from pydantic import BaseModel, Field, field_validator


class AuditFields(BaseModel):
    """
    A reusable Pydantic v2 model/mixin containing standard 21 CFR Part 11
    compliant audit and metadata fields.
    """

    created_at: AwareDatetime = Field(
        default_factory=lambda: datetime.now(timezone.utc),
        description="Chronological UTC timestamp when the record was created.",
    )
    created_by: str = Field(
        ...,
        description="Unique identifier (e.g. username/OIDC user_id) of the user who created the record.",
    )
    reason_for_change: str = Field(
        ...,
        description="Mandatory explanation or audit justification for creating or mutating this record.",
    )
    version_index: int = Field(
        default=1,
        description="Optimistic locking or row version counter, initialized to 1.",
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason_for_change(cls, v: str) -> str:
        """
        Validate that the reason_for_change is a non-empty, non-blank string.
        """
        if not isinstance(v, str) or not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v
