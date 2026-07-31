"""Pydantic transport schemas for Electronic Investigator Site File (eISF) regulatory binder browsing.

Requirements: PRD-SYS-001
"""

from datetime import datetime
from typing import List, Optional

from pydantic import BaseModel, Field


class EISFFolderNode(BaseModel):
    """Tree view of eISF folders and document counts.

    Requirements: PRD-SYS-001
    """

    section_code: str = Field(..., description="Unique folder section code")
    title: str = Field(..., description="Human-readable title of the folder")
    document_count: int = Field(
        ..., description="Count of documents inside this folder"
    )
    subfolders: List["EISFFolderNode"] = Field(
        default_factory=list, description="List of child folder nodes"
    )


class EISFDocumentDetail(BaseModel):
    """Fetch document details and download URL.

    Requirements: PRD-SYS-001
    """

    id: str = Field(..., description="Unique document identifier")
    site_id: str = Field(..., description="Unique clinical site identifier")
    section_code: str = Field(
        ..., description="Section code classification of the document"
    )
    filename: str = Field(..., description="Document file name")
    version: str = Field(..., description="Document version index/string")
    expiration_date: Optional[str] = Field(
        None, description="Optional document expiration date (YYYY-MM-DD)"
    )
    created_at: datetime = Field(
        ..., description="Chronological UTC timestamp when record was created"
    )
    created_by: str = Field(..., description="Unique identifier of creator")
    download_url: Optional[str] = Field(
        None, description="Direct download URL for document streaming"
    )


class EISFDocumentUploadRequest(BaseModel):
    """Request payload for uploading a new site document with GxP metadata.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Unique protocol study identifier")
    section_code: str = Field(
        ..., description="Section code classification for the document"
    )
    filename: str = Field(..., description="Document file name")
    content: str = Field(..., description="Base64 or raw text content of the document")
    mime_type: str = Field(..., description="MIME type of the document")
    reason_for_change: str = Field(
        ..., min_length=10, max_length=1000, description="Part 11 change justification"
    )
    expiration_date: Optional[str] = Field(
        None, description="Optional document expiration date (YYYY-MM-DD)"
    )
