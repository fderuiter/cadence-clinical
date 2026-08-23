"""Pydantic request and response schemas for SMART on FHIR eSource Ingestion & CRC Reconciliation.

Requirements: PRD-ESR-001, PRD-ESR-002, PRD-ESR-003, PRD-SYS-122, ADR-2199
"""

from __future__ import annotations

from datetime import datetime
from typing import Any

from pydantic import BaseModel, ConfigDict, Field

from apps.interop.domain.esource_models import (
    EhrSystemType,
)


class EhrEndpointCreateRequest(BaseModel):
    """Payload to register a new hospital EHR FHIR endpoint configuration."""

    name: str = Field(..., description="Hospital or health system network name")
    system_type: EhrSystemType = Field(
        default=EhrSystemType.SMART_FHIR_GENERIC, description="EHR system vendor"
    )
    fhir_base_url: str = Field(..., description="FHIR R4 API base URL")
    token_url: str = Field(
        ..., description="SMART Backend Services OAuth2 token endpoint"
    )
    client_id: str = Field(
        ..., description="OAuth2 client_id registered with hospital EHR"
    )
    public_key_jwks_url: str | None = Field(
        default=None, description="Optional public JWKS URL"
    )
    scopes: list[str] = Field(
        default_factory=lambda: [
            "system/Patient.read",
            "system/Observation.read",
            "system/Condition.read",
            "system/MedicationRequest.read",
            "system/MedicationStatement.read",
        ],
        description="FHIR scopes requested",
    )
    polling_interval_sec: int = Field(
        default=300, ge=30, description="Automated polling interval in seconds"
    )
    is_active: bool = Field(
        default=True, description="Whether endpoint polling is active"
    )

    model_config = ConfigDict(extra="ignore")


class EhrEndpointResponse(BaseModel):
    """Response DTO representing a registered hospital EHR endpoint."""

    endpoint_id: str
    name: str
    system_type: str
    fhir_base_url: str
    token_url: str
    client_id: str
    public_key_jwks_url: str | None = None
    scopes: list[str]
    polling_interval_sec: int
    is_active: bool
    last_polled_at: datetime | str | None = None
    created_at: datetime | str | None = None
    version_index: int = 1

    model_config = ConfigDict(extra="ignore")


class SmartClientAssertionRequest(BaseModel):
    """Request payload to generate a signed asymmetric client assertion JWT."""

    client_id: str = Field(..., description="OAuth2 client identifier")
    token_endpoint: str = Field(
        ..., description="Target EHR OAuth2 token URL (aud claim)"
    )
    private_key_pem: str = Field(..., description="RSA private key in PEM format")
    algorithm: str = Field(
        default="RS384", description="Asymmetric signing algorithm (RS384 or RS256)"
    )
    key_id: str | None = Field(default=None, description="Optional kid header")
    expiration_seconds: int = Field(
        default=300, le=300, description="Assertion validity lifetime in seconds"
    )

    model_config = ConfigDict(extra="ignore")


class SmartClientAssertionResponse(BaseModel):
    """Response containing the signed client assertion JWT."""

    client_assertion_jwt: str
    token_endpoint: str
    algorithm: str
    expires_in_seconds: int

    model_config = ConfigDict(extra="ignore")


class SmartAssertionVerifyRequest(BaseModel):
    """Payload to verify an inbound or generated SMART client assertion JWT."""

    assertion_jwt: str = Field(..., description="Raw client assertion JWT")
    public_key_pem: str = Field(..., description="RSA public key in PEM format")
    expected_audience: str | None = Field(
        default=None, description="Expected token endpoint URL"
    )
    expected_client_id: str | None = Field(
        default=None, description="Expected client_id claim"
    )

    model_config = ConfigDict(extra="ignore")


class SmartAssertionVerifyResponse(BaseModel):
    """Response indicating assertion verification validity and claims."""

    is_valid: bool
    claims: dict[str, Any] = Field(default_factory=dict)
    error_message: str | None = None

    model_config = ConfigDict(extra="ignore")


class EhrPollRequest(BaseModel):
    """Request to trigger automated polling against a configured hospital EHR endpoint."""

    endpoint_id: str = Field(..., description="ID of registered EHR endpoint")
    study_id: str = Field(..., description="Clinical study protocol identifier")
    site_id: str = Field(
        default="SITE-01", description="Clinical trial site identifier"
    )
    subject_id: str = Field(
        ..., description="Subject identifier/MRN to poll from hospital EHR"
    )
    private_key_pem: str = Field(
        ..., description="RSA private key for SMART Backend Services auth"
    )
    current_ecrf_context: dict[str, Any] | None = Field(
        default=None,
        description="Optional current eCRF context dictionary for live comparison",
    )

    model_config = ConfigDict(extra="ignore")


class EhrIngestBundleRequest(BaseModel):
    """Request payload to directly ingest and normalize a FHIR Bundle into candidate queue."""

    study_id: str = Field(..., description="Clinical study protocol identifier")
    site_id: str = Field(
        default="SITE-01", description="Clinical trial site identifier"
    )
    ehr_system_id: str = Field(
        default="FHIR_EHR_DIRECT", description="Source EHR system name"
    )
    bundle: dict[str, Any] = Field(..., description="Raw HL7 FHIR Bundle JSON payload")
    current_ecrf_context: dict[str, Any] | None = Field(
        default=None,
        description="Current eCRF field values for side-by-side comparison",
    )

    model_config = ConfigDict(extra="ignore")


class EhrCandidateItemDTO(BaseModel):
    """Response DTO for an individual candidate eSource reconciliation record."""

    id: str
    study_id: str
    site_id: str
    subject_id: str
    subject_pseudonym: str
    ehr_system_id: str
    fhir_resource_type: str
    fhir_resource_id: str
    source_timestamp: datetime | str
    target_domain: str
    target_variable: str
    cdash_testcd: str | None = None
    cdash_test: str | None = None
    candidate_value: Any
    candidate_unit: str | None = None
    current_ecrf_value: Any | None = None
    current_ecrf_updated_at: datetime | str | None = None
    discrepancy_type: str = "NEW_FIELD"
    confidence_score: float = 1.0
    mapping_provenance: str
    raw_fhir_resource: dict[str, Any] = Field(default_factory=dict)
    status: str = "PENDING_REVIEW"
    resolution_action: str | None = None
    resolution_reason: str | None = None
    resolved_by: str | None = None
    resolved_at: datetime | str | None = None
    electronic_signature: dict[str, Any] | None = None
    clinical_query_id: str | None = None
    created_at: datetime | str | None = None
    version_index: int = 1

    model_config = ConfigDict(extra="ignore")


class EhrCandidateListResponse(BaseModel):
    """Paginated list response of candidate eSource reconciliation items."""

    total_count: int
    candidates: list[EhrCandidateItemDTO]

    model_config = ConfigDict(extra="ignore")


class EhrPollResponse(BaseModel):
    """Response returned after executing an EHR poll operation."""

    study_id: str
    site_id: str
    subject_id: str
    total_ingested: int
    candidates: list[EhrCandidateItemDTO]

    model_config = ConfigDict(extra="ignore")


class EhrIngestBundleResponse(BaseModel):
    """Response returned after ingesting a raw FHIR Bundle."""

    study_id: str
    total_ingested: int
    candidates: list[EhrCandidateItemDTO]

    model_config = ConfigDict(extra="ignore")


class EhrReconcileAcceptRequest(BaseModel):
    """Request payload for 1-click Accept & Commit action."""

    candidate_id: str = Field(..., description="UUID of candidate record to accept")
    signature_token: str | None = Field(
        default=None, description="Optional Part 11 electronic signature token"
    )
    commit_reason: str | None = Field(
        default=None, description="Optional commit justification"
    )
    change_reason: str = Field(
        default="CRC eSource Reconciliation Accept & Commit",
        description="Part 11 audit change reason",
    )

    model_config = ConfigDict(extra="ignore")


class EhrReconcileRejectRequest(BaseModel):
    """Request payload for 1-click Reject with Reason action."""

    candidate_id: str = Field(..., description="UUID of candidate record to reject")
    rejection_reason: str = Field(
        ..., min_length=3, description="Mandatory clinical rationale for rejection"
    )
    change_reason: str = Field(
        default="CRC eSource Reconciliation Reject",
        description="Part 11 audit change reason",
    )

    model_config = ConfigDict(extra="ignore")


class EhrReconcileQueryRequest(BaseModel):
    """Request payload for 1-click Raise Clinical Query action."""

    candidate_id: str = Field(
        ..., description="UUID of candidate record with discrepancy"
    )
    query_explanation: str = Field(
        ..., min_length=3, description="Clinical query discrepancy explanation"
    )
    discrepancy_type: str = Field(
        default="VALUE_MISMATCH", description="Type of discrepancy"
    )
    change_reason: str = Field(
        default="CRC Raised eSource Discrepancy Query",
        description="Part 11 audit change reason",
    )

    model_config = ConfigDict(extra="ignore")


class EhrReconcileActionResponse(BaseModel):
    """Response after executing a reconciliation review action."""

    success: bool = True
    candidate: EhrCandidateItemDTO
    clinical_query_id: str | None = None
    message: str

    model_config = ConfigDict(extra="ignore")


class EhrProvenanceResponse(BaseModel):
    """Complete provenance and audit attribution metadata response for a candidate/committed record."""

    candidate_id: str
    study_id: str
    site_id: str
    subject_id: str
    subject_pseudonym: str
    ehr_system_id: str
    fhir_resource_type: str
    fhir_resource_id: str
    source_timestamp: str | None
    target_domain: str
    target_variable: str
    cdash_testcd: str | None
    cdash_test: str | None
    candidate_value: Any
    candidate_unit: str | None
    current_ecrf_value: Any | None
    discrepancy_type: str
    confidence_score: float
    mapping_provenance: str
    raw_fhir_resource: dict[str, Any]
    status: str
    resolution_action: str | None
    resolution_reason: str | None
    resolved_by: str | None
    resolved_at: str | None
    electronic_signature: dict[str, Any] | None
    clinical_query_id: str | None
    audit_trail: list[dict[str, Any]] = Field(default_factory=list)

    model_config = ConfigDict(extra="ignore")
