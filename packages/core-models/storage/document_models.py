from datetime import datetime
from typing import Literal, Optional

from pydantic import BaseModel


class DocumentMetadataResponse(BaseModel):
    """Schema representing complete document metadata.

    Requirements: PRD-SYS-001
    """

    document_id: str
    filename: str
    version_index: str
    sha256_hash: str
    dia_tmf_code: str
    status: str
    created_by: str
    created_at: datetime


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

    job_id: str
    study_id: str
    status: Literal["PENDING", "PROCESSING", "COMPLETED", "FAILED"]
    download_url: Optional[str] = None
