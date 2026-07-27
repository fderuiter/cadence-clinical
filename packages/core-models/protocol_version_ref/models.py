"""
ProtocolVersionRef shared domain-model and controlled vocabularies.

This module provides the minimal shared protocol-version reference model for the
Cadence Clinical Execution and eTMF services. Enforces standard validation and
serialization expectations for cross-service payloads in compliance with
Pydantic v2 conventions and GxP standards.
"""

from enum import Enum

from pydantic import BaseModel, Field, field_validator


class ProtocolVersionStatus(str, Enum):
    """
    Controlled vocabulary of statuses for a clinical protocol or study version.
    """

    DRAFT = "DRAFT"
    ACTIVE = "ACTIVE"
    LOCKED = "LOCKED"
    PUBLISHED = "PUBLISHED"
    ARCHIVED = "ARCHIVED"
    FROZEN = "FROZEN"


class ProtocolVersionRef(BaseModel):
    """
    Pydantic v2 model representing a reference to a specific clinical trial protocol version.

    This contract is shared between Execution, eTMF, and other services to prevent
    the duplication of ad-hoc protocol reference fields and ensure consistent cross-service
    payload structures, validation, and serialization.
    """

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
    status: ProtocolVersionStatus = Field(
        ...,
        description="Current controlled status of this protocol version.",
    )

    @field_validator("study_id")
    @classmethod
    def validate_study_id(cls, v: str) -> str:
        """
        Validate that the study_id is a non-empty, non-blank string.
        """
        if not isinstance(v, str) or not v.strip():
            raise ValueError("Study ID cannot be empty or consist only of whitespace.")
        return v.strip()

    @field_validator("version_tag")
    @classmethod
    def validate_version_tag(cls, v: str) -> str:
        """
        Validate that the version_tag is a non-empty, non-blank string.
        """
        if not isinstance(v, str) or not v.strip():
            raise ValueError(
                "Version tag cannot be empty or consist only of whitespace."
            )
        return v.strip()

    @field_validator("version_index")
    @classmethod
    def validate_version_index(cls, v: int) -> int:
        """
        Validate that the version_index is a positive integer >= 1.
        """
        if v < 1:
            raise ValueError("Version index must be a positive integer >= 1.")
        return v
