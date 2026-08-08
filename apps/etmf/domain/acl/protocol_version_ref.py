"""Local Anti-Corruption Layer (ACL) DTOs for eTMF Service."""

from enum import StrEnum

from pydantic import BaseModel, Field, field_validator


class ProtocolVersionStatusDTO(StrEnum):
    """Controlled vocabulary of statuses for a clinical protocol or study version."""

    DRAFT = "DRAFT"
    ACTIVE = "ACTIVE"
    LOCKED = "LOCKED"
    PUBLISHED = "PUBLISHED"
    ARCHIVED = "ARCHIVED"
    FROZEN = "FROZEN"


class ProtocolVersionRefDTO(BaseModel):
    """Pydantic v2 ACL model representing a reference to a specific clinical trial protocol version."""

    study_id: str = Field(
        ...,
        description="Unique identifier of the clinical study (e.g. 'STUDY-101').",
    )
    version_tag: str = Field(
        ...,
        description="The semantic or alphanumeric version tag representing the protocol version (e.g. '1.0', 'v2.1').",
    )
    version_index: int = Field(
        ...,
        description="Chronological, incrementing index of the protocol version (must be >= 1).",
    )
    status: ProtocolVersionStatusDTO = Field(
        ...,
        description="Current controlled status of this protocol version.",
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


ProtocolVersionRef = ProtocolVersionRefDTO
ProtocolVersionStatus = ProtocolVersionStatusDTO
