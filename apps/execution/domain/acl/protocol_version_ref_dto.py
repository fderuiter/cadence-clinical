"""Anti-Corruption Layer DTO for Protocol Version Reference in Execution Service."""

from enum import StrEnum

from pydantic import BaseModel, Field, field_validator


class ProtocolVersionStatusEnum(StrEnum):
    """Controlled status vocabulary for protocol version references."""

    DRAFT = "DRAFT"
    ACTIVE = "ACTIVE"
    LOCKED = "LOCKED"
    PUBLISHED = "PUBLISHED"
    ARCHIVED = "ARCHIVED"
    FROZEN = "FROZEN"


class ProtocolVersionRefDTO(BaseModel):
    """Local Execution ACL DTO representing a reference to a specific protocol version."""

    study_id: str = Field(
        ..., description="Unique identifier of the clinical study (e.g. 'STUDY-101')."
    )
    version_tag: str = Field(
        ..., description="Semantic or alphanumeric version tag (e.g. '1.0', 'v2.1')."
    )
    version_index: int = Field(
        ..., description="Chronological, incrementing version index (must be >= 1)."
    )
    status: ProtocolVersionStatusEnum = Field(
        ..., description="Current controlled status of protocol version."
    )

    @field_validator("study_id")
    @classmethod
    def validate_study_id(cls, v: str) -> str:
        if not isinstance(v, str) or not v.strip():
            raise ValueError("Study ID cannot be empty or consist only of whitespace.")
        return v.strip()

    @field_validator("version_tag")
    @classmethod
    def validate_version_tag(cls, v: str) -> str:
        if not isinstance(v, str) or not v.strip():
            raise ValueError(
                "Version tag cannot be empty or consist only of whitespace."
            )
        return v.strip()

    @field_validator("version_index")
    @classmethod
    def validate_version_index(cls, v: int) -> int:
        if v < 1:
            raise ValueError("Version index must be a positive integer >= 1.")
        return v
