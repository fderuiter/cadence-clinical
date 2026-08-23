"""Domain models for SMART on FHIR eSource Ingestion Pipeline and CRC Reconciliation Drawer.

Requirements: PRD-ESR-001, PRD-ESR-002, PRD-ESR-003, PRD-SYS-122, ADR-2199, FDA Electronic Source Data Guidance
"""

import uuid
from datetime import datetime
from enum import StrEnum
from typing import Any

from pydantic import BaseModel, ConfigDict, Field

from apps.interop.domain.semantic_mapping_models import CDISCDomain


class ReconciliationStatus(StrEnum):
    """Lifecycle status of an ingested EHR source data candidate."""

    PENDING_REVIEW = "PENDING_REVIEW"
    ACCEPTED = "ACCEPTED"
    REJECTED = "REJECTED"
    QUERIED = "QUERIED"


class EhrSystemType(StrEnum):
    """Supported EHR vendor platform architectures."""

    EPIC = "EPIC"
    CERNER = "CERNER"
    ALLSCRIPTS = "ALLSCRIPTS"
    SMART_FHIR_GENERIC = "SMART_FHIR_GENERIC"


class ReconciliationAction(StrEnum):
    """Non-destructive review actions available to Clinical Research Coordinators."""

    ACCEPT_COMMIT = "ACCEPT_COMMIT"
    REJECT = "REJECT"
    QUERY = "QUERY"


class DiscrepancyType(StrEnum):
    """Comparison classifications between candidate EHR values and current eCRF fields."""

    NEW_FIELD = "NEW_FIELD"
    EXACT_MATCH = "EXACT_MATCH"
    VALUE_MISMATCH = "VALUE_MISMATCH"
    UNIT_MISMATCH = "UNIT_MISMATCH"
    DATE_MISMATCH = "DATE_MISMATCH"


class SmartClientAssertionPayload(BaseModel):
    """JWT Claims payload for SMART Backend Services Asymmetric OAuth2 client credentials.

    Adheres to RFC 7523 and HL7 SMART on FHIR Backend Services Authorization specification.
    """

    iss: str = Field(
        ..., description="Issuer: OAuth2 client_id of the Cadence Clinical gateway"
    )
    sub: str = Field(..., description="Subject: OAuth2 client_id matching iss")
    aud: str = Field(
        ..., description="Audience: Token URL of the hospital EHR authorization server"
    )
    jti: str = Field(
        default_factory=lambda: str(uuid.uuid4()),
        description="Unique cryptographic token identifier",
    )
    exp: int = Field(
        ..., description="Expiration timestamp (Unix epoch in seconds, max 300s)"
    )
    iat: int = Field(..., description="Issued-at timestamp (Unix epoch in seconds)")

    model_config = ConfigDict(extra="ignore")


class SmartTokenResponse(BaseModel):
    """OAuth2 Access Token response returned by hospital SMART Backend Services endpoint."""

    access_token: str = Field(
        ..., description="Bearer access token for FHIR API requests"
    )
    token_type: str = Field(default="Bearer", description="OAuth2 token type (Bearer)")
    expires_in: int = Field(..., description="Token validity lifetime in seconds")
    scope: str = Field(..., description="Authorized FHIR resource scopes granted")

    model_config = ConfigDict(extra="ignore")


class EhrEndpointConfig(BaseModel):
    """Configuration for an authorized hospital EHR FHIR endpoint."""

    endpoint_id: str = Field(
        default_factory=lambda: str(uuid.uuid4()),
        description="Unique endpoint identifier",
    )
    name: str = Field(
        ...,
        description="Descriptive hospital / site network name (e.g. Johns Hopkins Medicine)",
    )
    system_type: EhrSystemType = Field(
        default=EhrSystemType.SMART_FHIR_GENERIC,
        description="Target EHR vendor platform",
    )
    fhir_base_url: str = Field(
        ..., description="Base FHIR R4 API URL (e.g. https://fhir.hospital.org/r4)"
    )
    token_url: str = Field(
        ...,
        description="OAuth2 token endpoint for SMART Backend Services authentication",
    )
    client_id: str = Field(
        ..., description="Registered asymmetric OAuth2 client identifier"
    )
    public_key_jwks_url: str | None = Field(
        default=None, description="Optional public JWKS URL for key rollover"
    )
    scopes: list[str] = Field(
        default_factory=lambda: [
            "system/Patient.read",
            "system/Observation.read",
            "system/Condition.read",
            "system/MedicationRequest.read",
            "system/MedicationStatement.read",
        ],
        description="FHIR scopes requested for automated polling",
    )
    polling_interval_sec: int = Field(
        default=300, ge=30, description="Automated polling interval in seconds"
    )
    is_active: bool = Field(
        default=True, description="Whether endpoint polling is active"
    )
    last_polled_at: datetime | str | None = Field(
        default=None, description="Timestamp of last automated poll"
    )

    model_config = ConfigDict(extra="ignore")


class EhrCandidateRecord(BaseModel):
    """An individual candidate eSource data point extracted from EHR awaiting CRC review.

    Guarantees non-destructive review workflow (zero silent overwrites) and Part 11 provenance.
    """

    id: str = Field(
        default_factory=lambda: str(uuid.uuid4()), description="Candidate record UUID"
    )
    study_id: str = Field(..., description="Clinical study protocol identifier")
    site_id: str = Field(..., description="Clinical trial site identifier")
    subject_id: str = Field(..., description="Subject pseudonym / identifier")
    subject_pseudonym: str = Field(
        ..., description="Deterministic irreversible pseudonym token"
    )
    ehr_system_id: str = Field(
        ..., description="Originating hospital EHR system identifier"
    )
    fhir_resource_type: str = Field(
        ..., description="FHIR Resource Type (Patient, Observation, Condition, etc.)"
    )
    fhir_resource_id: str = Field(..., description="Source FHIR Resource ID")
    source_timestamp: datetime | str = Field(
        ..., description="Effective clinical observation or recorded timestamp"
    )
    target_domain: CDISCDomain = Field(
        ..., description="Standard CDISC CDASH / SDTM domain (DM, VS, LB, MH, CM)"
    )
    target_variable: str = Field(
        ..., description="Target CDASH field notation (e.g. eCRF.VS.SYSBP)"
    )
    cdash_testcd: str | None = Field(
        default=None, description="Standard CDASH short test code (e.g. SYSBP, GLUC)"
    )
    cdash_test: str | None = Field(
        default=None, description="Standard CDASH long test name"
    )
    candidate_value: Any = Field(..., description="Extracted candidate value from EHR")
    candidate_unit: str | None = Field(
        default=None, description="Extracted clinical unit of measure"
    )
    current_ecrf_value: Any | None = Field(
        default=None, description="Current value existing in the eCRF field"
    )
    current_ecrf_updated_at: datetime | str | None = Field(
        default=None, description="Timestamp of current eCRF value"
    )
    discrepancy_type: DiscrepancyType = Field(
        default=DiscrepancyType.NEW_FIELD,
        description="Classification of comparison against current eCRF",
    )
    confidence_score: float = Field(
        default=1.0, ge=0.0, le=1.0, description="Mapping confidence score"
    )
    mapping_provenance: str = Field(
        ..., description="Detailed semantic mapping rule or path provenance"
    )
    raw_fhir_resource: dict[str, Any] = Field(
        default_factory=dict, description="Complete raw FHIR resource JSON"
    )
    status: ReconciliationStatus = Field(
        default=ReconciliationStatus.PENDING_REVIEW,
        description="Current reconciliation review status",
    )
    resolution_action: ReconciliationAction | None = Field(
        default=None, description="Action taken: ACCEPT_COMMIT, REJECT, or QUERY"
    )
    resolution_reason: str | None = Field(
        default=None,
        description="Coordinator-supplied rationale for rejection or clinical query",
    )
    resolved_by: str | None = Field(
        default=None, description="User ID of the reviewing coordinator/investigator"
    )
    resolved_at: datetime | str | None = Field(
        default=None, description="Timestamp when review action occurred"
    )
    electronic_signature: dict[str, Any] | None = Field(
        default=None,
        description="21 CFR Part 11 electronic signature payload for committed source data",
    )
    clinical_query_id: str | None = Field(
        default=None,
        description="Associated Clinical Query ID if a discrepancy query was raised",
    )
    created_at: datetime | str = Field(
        default_factory=datetime.now, description="Record creation timestamp"
    )
    created_by: str = Field(
        default="esource_ingestion_pipeline",
        description="Creator user or automated pipeline",
    )
    reason_for_change: str = Field(
        default="Automated SMART on FHIR eSource Ingestion",
        description="Part 11 audit reason",
    )
    version_index: int = Field(default=1, description="Record version index")

    model_config = ConfigDict(extra="ignore")
