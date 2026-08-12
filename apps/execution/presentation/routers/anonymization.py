"""FastAPI router for PHI detection, Named Entity Recognition (NER), and PDF redaction API endpoints.

Requirements: PRD-SYS-001
"""

import base64
from typing import Any

from fastapi import APIRouter, Depends, HTTPException
from fastapi.concurrency import run_in_threadpool
from pydantic import BaseModel, Field

import packages  # noqa: F401
from apps.execution.services.pdf_redactor import PDFRedactorService
from packages.deid.ner_scrubber import PHINameEntityScrubber
from packages.security.middleware import get_current_user

router = APIRouter(prefix="/api/v1/execution/anonymization", tags=["Anonymization"])

_SCRUBBER = PHINameEntityScrubber()
_REDACTOR = PDFRedactorService()


class PHIScanRequest(BaseModel):
    """Request payload to scan text for HIPAA 18 PHI identifiers.

    Requirements: PRD-SYS-001
    """

    text: str = Field(..., description="Document text content to scan for PHI")


class RedactPDFRequest(BaseModel):
    """Request payload to apply non-destructive redactions to PDF document.

    Requirements: PRD-SYS-001
    """

    pdf_base64: str = Field(..., description="Base64 encoded PDF document content")
    target_snippets: list[str] = Field(
        default_factory=list, description="Target PHI strings to redact"
    )


@router.post("/scan-phi")
async def scan_phi_endpoint(
    payload: PHIScanRequest,
    current_user: dict = Depends(get_current_user),
) -> dict[str, Any]:
    """Scan text payload for Protected Health Information (PHI) identifiers.

    Requirements: PRD-SYS-001
    """
    entities = await run_in_threadpool(_SCRUBBER.detect_phi, payload.text)
    scrubbed_preview = await run_in_threadpool(_SCRUBBER.scrub_phi, payload.text)
    return {
        "phi_detected_count": len(entities),
        "entities": entities,
        "scrubbed_text_preview": scrubbed_preview,
    }


@router.post("/redact-pdf")
async def redact_pdf_endpoint(
    payload: RedactPDFRequest,
    current_user: dict = Depends(get_current_user),
) -> dict[str, Any]:
    """Apply non-destructive PHI redaction overlays to PDF document.

    Requirements: PRD-SYS-001
    """
    try:
        pdf_bytes = base64.b64decode(payload.pdf_base64)
    except Exception:
        raise HTTPException(status_code=400, detail="Invalid base64 PDF payload.")

    result = await run_in_threadpool(
        _REDACTOR.apply_redaction_overlay, pdf_bytes, payload.target_snippets
    )
    redacted_b64 = base64.b64encode(result["redacted_content"]).decode("utf-8")

    return {
        "redacted_pdf_base64": redacted_b64,
        "redacted_entities_count": result["redacted_entities_count"],
        "sha256_checksum": result["sha256_checksum"],
        "is_clean": result["is_clean"],
    }
