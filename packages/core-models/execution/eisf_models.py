"""Pydantic data models for Electronic Investigator Site File (eISF) regulatory binder document taxonomy.

Requirements: PRD-SYS-001
"""

from enum import StrEnum

from pydantic import BaseModel, Field


class EISFTaxonomyCategoryEnum(StrEnum):
    """DIA eISF / Regulatory Binder document taxonomy categories.

    Requirements: PRD-SYS-001
    """

    INVESTIGATOR_CV = "1_INVESTIGATOR_CV"
    MEDICAL_LICENSE = "2_MEDICAL_LICENSE"
    PROTOCOL_APPROVAL = "3_PROTOCOL_APPROVAL"
    IRB_IEC_APPROVAL = "4_IRB_IEC_APPROVAL"
    INFORMED_CONSENT = "5_INFORMED_CONSENT"
    FINANCIAL_DISCLOSURE = "6_FINANCIAL_DISCLOSURE"
    DELEGATION_OF_AUTHORITY = "7_DELEGATION_OF_AUTHORITY"
    SAFETY_REPORT = "8_SAFETY_REPORT"


class EISFDocumentRecord(BaseModel):
    """eISF regulatory binder document metadata record.

    Requirements: PRD-SYS-001
    """

    document_id: str = Field(..., description="Unique eISF document identifier")
    study_id: str = Field(..., description="Target protocol study ID")
    site_id: str = Field(
        ..., description="Target investigator site ID (for site-scoped isolation)"
    )
    category: EISFTaxonomyCategoryEnum = Field(..., description="DIA taxonomy category")
    title: str = Field(..., description="Human-readable document title")
    version: str = Field("1.0", description="Document version string")
    file_name: str = Field(..., description="Original uploaded filename")
    file_size_bytes: int = Field(..., description="File size in bytes")
    sha256_hash: str = Field(..., description="SHA-256 integrity checksum hex string")
    uploaded_by: str = Field(..., description="User ID of uploader")
    uploaded_at: str = Field(..., description="UTC ISO timestamp of upload")
    expiration_date: str | None = Field(
        None, description="Optional document expiration date (YYYY-MM-DD)"
    )
    is_redacted: bool = Field(
        False, description="True if document contains non-destructive PHI redactions"
    )
