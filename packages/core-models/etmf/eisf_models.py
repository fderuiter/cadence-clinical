"""Pydantic v2 transport schemas for eISF regulatory binder document taxonomy and versioning.

Requirements: PRD-SYS-001
"""

from datetime import date, datetime
from typing import Optional

from pydantic import BaseModel, Field


class EISFSectionTaxonomyResponse(BaseModel):
    """Pydantic schema for EISFSectionTaxonomy response serialization.

    Requirements: PRD-SYS-001
    """

    section_code: str = Field(..., description="The unique code of the section")
    section_number: str = Field(..., description="The section number")
    title: str = Field(..., description="The title of the section")
    description: str = Field(..., description="The description of the section")
    is_mandatory: bool = Field(..., description="Whether the section is mandatory")


class EISFDocumentRecordResponse(BaseModel):
    """Pydantic schema for EISFDocumentRecord response serialization.

    Requirements: PRD-SYS-001
    """

    id: str = Field(..., description="The unique identifier of the document record")
    site_id: str = Field(..., description="The investigator site ID")
    study_id: str = Field(..., description="The protocol study ID")
    section_code: str = Field(..., description="The section taxonomy code")
    filename: str = Field(..., description="The filename")
    file_path: str = Field(..., description="The path where the file is stored")
    sha256_checksum: str = Field(
        ..., description="The SHA-256 integrity checksum hex string"
    )
    version_major: int = Field(..., description="The major version of the document")
    version_minor: int = Field(..., description="The minor version of the document")
    status: str = Field(..., description="The lifecycle state status of the document")
    expiration_date: Optional[date] = Field(
        None, description="Optional document expiration date"
    )
    created_at: datetime = Field(..., description="The creation timestamp in UTC")
    created_by: str = Field(..., description="The user ID of the creator")
    reason_for_change: str = Field(
        ..., description="The justification/reason for the change/creation"
    )
    version_index: int = Field(
        ..., description="The dynamic version index (defaults to 1)"
    )
    is_active: bool = Field(..., description="Whether the record is active")
    is_deleted: bool = Field(..., description="Whether the record is deleted")
