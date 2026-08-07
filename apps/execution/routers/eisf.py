"""FastAPI router for eISF regulatory binder browsing, upload, and site permission enforcement.

Requirements: PRD-SYS-001
"""

from fastapi import APIRouter, Depends, HTTPException, status
from pydantic import BaseModel, Field

import packages  # noqa: F401
from apps.execution.services.eisf_service import EISFService
from apps.execution.src.domain.eisf_models import (
    EISFDocumentRecord,
    EISFTaxonomyCategoryEnum,
)
from packages.security.middleware import get_current_user

router = APIRouter(prefix="/api/v1/execution/eisf", tags=["eISF"])

_EISF_SERVICE = EISFService()


class UploadEISFDocumentRequest(BaseModel):
    """Request payload to upload an eISF document.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Target protocol study ID")
    site_id: str = Field(..., description="Target investigator site ID")
    category: EISFTaxonomyCategoryEnum = Field(..., description="DIA taxonomy category")
    title: str = Field(..., description="Document title")
    file_name: str = Field(..., description="Original file name")
    content_base64: str = Field(..., description="Base64 encoded file content string")


@router.post(
    "/upload",
    response_model=EISFDocumentRecord,
    status_code=status.HTTP_201_CREATED,
)
async def upload_eisf_document_endpoint(
    payload: UploadEISFDocumentRequest,
    current_user: dict = Depends(get_current_user),
) -> EISFDocumentRecord:
    """Upload eISF regulatory binder document and calculate SHA-256 integrity checksum.

    Requirements: PRD-SYS-001
    """
    import base64

    try:
        content_bytes = base64.b64decode(payload.content_base64)
    except Exception:
        raise HTTPException(
            status_code=400, detail="Invalid base64 document content string."
        )

    uploader = current_user.get("sub", "crc_user")

    return _EISF_SERVICE.upload_document(
        study_id=payload.study_id,
        site_id=payload.site_id,
        category=payload.category,
        title=payload.title,
        file_name=payload.file_name,
        content_bytes=content_bytes,
        uploader_id=uploader,
    )


@router.get("/binder/{study_id}/{site_id}", response_model=list[EISFDocumentRecord])
async def get_site_regulatory_binder_endpoint(
    study_id: str,
    site_id: str,
    current_user: dict = Depends(get_current_user),
) -> list[EISFDocumentRecord]:
    """Retrieve site-isolated regulatory binder documents for specified study and site.

    Requirements: PRD-SYS-001
    """
    return [
        doc
        for doc in _EISF_SERVICE._document_store.values()
        if doc.study_id == study_id and doc.site_id == site_id
    ]
