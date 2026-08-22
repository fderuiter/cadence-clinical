"""Domain models for eTMF/eISF multimodal document intelligence, DIA taxonomy classification, and CRA QC staging."""

from datetime import date, datetime
from enum import StrEnum
from typing import Any

from pydantic import BaseModel, Field


class DocumentModality(StrEnum):
    """Input modality or format of the processed regulatory document."""

    TEXT = "TEXT"
    PDF_BINARY = "PDF_BINARY"
    SCANNED_IMAGE = "SCANNED_IMAGE"
    STRUCTURED_FORM = "STRUCTURED_FORM"


class SignaturePresenceStatus(StrEnum):
    """Regulatory signature completeness evaluation status."""

    FULLY_SIGNED = "FULLY_SIGNED"
    PARTIALLY_SIGNED = "PARTIALLY_SIGNED"
    UNSIGNED = "UNSIGNED"
    SIGNATURE_NOT_REQUIRED = "SIGNATURE_NOT_REQUIRED"


class ClassificationConfidenceTier(StrEnum):
    """Confidence tier based on classification score."""

    HIGH = "HIGH"  # >= 0.85
    MEDIUM = "MEDIUM"  # 0.50 - 0.84
    LOW = "LOW"  # < 0.50


class QCRecommendation(StrEnum):
    """Recommended automated action for CRA Quality Control routing."""

    AUTO_CLASSIFY = "AUTO_CLASSIFY"
    FLAG_FOR_QC_REVIEW = "FLAG_FOR_QC_REVIEW"
    MANUAL_RECLASSIFICATION_REQUIRED = "MANUAL_RECLASSIFICATION_REQUIRED"


class ExtractedSignature(BaseModel):
    """Representation of an extracted signature or signature line."""

    signer_name: str | None = Field(
        default=None, description="Name of the extracted signer"
    )
    signer_role: str | None = Field(
        default=None, description="Role of the signer (e.g. PI, Sponsor, Subject)"
    )
    signature_date: date | str | None = Field(
        default=None, description="Date stamp associated with the signature"
    )
    confidence: float = Field(
        default=1.0, ge=0.0, le=1.0, description="Detection confidence score"
    )
    location_hint: str | None = Field(
        default=None, description="Text location or anchor"
    )
    is_digital_signature: bool = Field(
        default=False, description="Whether cryptographic digital signature is present"
    )
    signature_type: str = Field(
        default="WET_OR_ELECTRONIC",
        description="Type of signature: DIGITAL_PKCS7, HMAC_MANIFEST, WET_OR_ELECTRONIC",
    )


class SignatureAnalysisResult(BaseModel):
    """Aggregate result of document signature completeness analysis."""

    status: SignaturePresenceStatus = Field(
        default=SignaturePresenceStatus.UNSIGNED,
        description="Overall signature completeness status",
    )
    extracted_signatures: list[ExtractedSignature] = Field(
        default_factory=list, description="List of identified signatures"
    )
    missing_required_signatures: list[str] = Field(
        default_factory=list, description="Roles with missing required signatures"
    )
    signature_blocks_detected: int = Field(
        default=0, description="Total number of signature blocks identified"
    )
    details: str = Field(
        default="", description="Human-readable summary of signature verification"
    )


class ExtractedRegulatoryMetadata(BaseModel):
    """Key clinical trial metadata entities extracted from document content."""

    protocol_number: str | None = Field(
        default=None, description="Clinical protocol identifier"
    )
    study_id: str | None = Field(default=None, description="Resolved study identifier")
    site_id: str | None = Field(default=None, description="Resolved site identifier")
    investigator_name: str | None = Field(
        default=None, description="Principal Investigator name"
    )
    issue_date: date | None = Field(
        default=None, description="Document issuance or effective date"
    )
    expiration_date: date | None = Field(
        default=None, description="Document expiration date"
    )
    form_identifier: str | None = Field(
        default=None, description="Official form identifier (e.g. FDA 1572, OMB number)"
    )
    version_tag: str | None = Field(default=None, description="Document version string")
    raw_extracted_fields: dict[str, Any] = Field(
        default_factory=dict, description="Additional key-value entities extracted"
    )
    phi_pii_detected: bool = Field(
        default=False, description="Whether sensitive PII/PHI was detected"
    )


class TaxonomyMatchCandidate(BaseModel):
    """Ranked taxonomy classification candidate."""

    zone_code: int = Field(..., description="DIA TMF Zone code (1-11)")
    zone_name: str = Field(..., description="DIA TMF Zone name")
    section_code: str = Field(..., description="DIA TMF Section code (e.g. 05.02)")
    section_name: str = Field(..., description="DIA TMF Section name")
    artifact_code: str = Field(
        ..., description="Canonical DIA artifact code (e.g. 05.02.01)"
    )
    artifact_name: str = Field(..., description="Canonical artifact name")
    confidence: float = Field(
        ..., ge=0.0, le=1.0, description="Classification confidence score"
    )
    matched_signals: list[str] = Field(
        default_factory=list,
        description="Evidence signals that triggered this match (e.g. form_omb, layout_anchor, keywords)",
    )
    is_extension: bool = Field(
        default=False, description="Whether artifact is a custom Cadence extension"
    )


class DocumentIntelligenceReport(BaseModel):
    """Comprehensive intelligence report containing classification, metadata, and signature analysis."""

    document_id: str | None = Field(
        default=None, description="ID of existing document if already persisted"
    )
    filename: str = Field(..., description="Source document filename")
    mime_type: str = Field(..., description="MIME type")
    sha256_hash: str = Field(..., description="Cryptographic SHA-256 digest of content")
    modality: DocumentModality = Field(
        default=DocumentModality.TEXT, description="Detected input modality"
    )
    primary_classification: TaxonomyMatchCandidate = Field(
        ..., description="Top-ranked DIA TMF Reference Model classification"
    )
    alternative_candidates: list[TaxonomyMatchCandidate] = Field(
        default_factory=list, description="Alternative taxonomy matches"
    )
    confidence_tier: ClassificationConfidenceTier = Field(
        default=ClassificationConfidenceTier.HIGH,
        description="Confidence tier of primary classification",
    )
    qc_recommendation: QCRecommendation = Field(
        default=QCRecommendation.AUTO_CLASSIFY,
        description="Recommended action for CRA Quality Control routing",
    )
    extracted_metadata: ExtractedRegulatoryMetadata = Field(
        default_factory=ExtractedRegulatoryMetadata,
        description="Extracted regulatory metadata",
    )
    signature_analysis: SignatureAnalysisResult = Field(
        default_factory=SignatureAnalysisResult,
        description="Signature completeness analysis",
    )
    ai_generation_manifest: dict[str, Any] = Field(
        default_factory=dict,
        description="21 CFR Part 11 AI provenance metadata (model, prompt hash, confidence)",
    )
    eisf_target_mapping: dict[str, Any] | None = Field(
        default=None,
        description="Corresponding eISF folder mapping and synchronization advice",
    )


class CRAQCStagingItem(BaseModel):
    """Item staged in the CRA Quality Control queue for human review."""

    document_id: str = Field(..., description="Staged document identifier")
    study_id: str = Field(..., description="Study ID")
    site_id: str | None = Field(default=None, description="Site ID")
    filename: str = Field(..., description="Filename")
    status: str = Field(
        ..., description="Current lifecycle status (e.g. TECHNICAL_QC, DRAFT_AI)"
    )
    intelligence_report: DocumentIntelligenceReport = Field(
        ..., description="Associated intelligence report"
    )
    staged_at: datetime = Field(..., description="Timestamp staged")
    staged_by: str = Field(..., description="Actor or system that staged the document")
    qc_assigned_to: str | None = Field(
        default=None, description="Assigned CRA reviewer"
    )
    discrepancy_notes: list[str] = Field(
        default_factory=list, description="Automated discrepancies flagged for review"
    )
