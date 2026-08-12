"""Document storage and archival Pydantic models."""

from datetime import datetime
from typing import Literal

from pydantic import BaseModel, ConfigDict


class DocumentMetadataResponse(BaseModel):
    """Schema representing complete document metadata.

    Requirements: PRD-SYS-001
    """

    model_config = ConfigDict(extra="allow")

    document_id: str
    filename: str
    version_index: str
    sha256_hash: str
    dia_tmf_code: str
    status: str
    created_by: str
    created_at: datetime
    custom_tags: dict[str, str] = {}


class DocumentUploadResponse(BaseModel):
    """Schema representing upload response.

    Requirements: PRD-SYS-001
    """

    document_id: str
    filename: str
    version_index: str
    sha256_hash: str


class ArchiveJobResponse(BaseModel):
    """Schema representing study archival job details and status.

    Requirements: PRD-SYS-001
    """

    model_config = ConfigDict(extra="allow")

    job_id: str
    scope_id: str
    status: Literal["PENDING", "PROCESSING", "COMPLETED", "FAILED"]
    download_url: str | None = None


__all__ = [
    "ArchiveJobResponse",
    "DocumentMetadataResponse",
    "DocumentUploadResponse",
]
