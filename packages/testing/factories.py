"""Standardized clinical domain entity factories for unit and integration testing."""

import uuid
from datetime import UTC, datetime
from typing import Any

from pydantic import BaseModel, Field


class SubjectFactory(BaseModel):
    """Factory for creating Subject domain entities in tests."""

    id: str = Field(default_factory=lambda: f"SUBJ-{uuid.uuid4().hex[:6].upper()}")
    study_id: str = "study_oncology_phase3"
    site_id: str = "SITE-101"
    status: str = "ENROLLED"
    protocol_version: str = "1.0"
    created_at: datetime = Field(default_factory=lambda: datetime.now(UTC))

    @classmethod
    def create(cls, **kwargs: Any) -> SubjectFactory:
        return cls(**kwargs)


class ProtocolDefinitionFactory(BaseModel):
    """Factory for creating Protocol Definition domain entities in tests."""

    id: str = Field(default_factory=lambda: f"PROT-{uuid.uuid4().hex[:6].upper()}")
    protocol_title: str = "Phase 3 Oncology Randomized Double-Blind Trial"
    protocol_version: str = "1.0"
    is_active: bool = True
    arms: list[str] = Field(
        default_factory=lambda: ["Arm-A-Investigational", "Arm-B-Control"]
    )
    created_at: datetime = Field(default_factory=lambda: datetime.now(UTC))

    @classmethod
    def create(cls, **kwargs: Any) -> ProtocolDefinitionFactory:
        return cls(**kwargs)


class ClinicalObservationFactory(BaseModel):
    """Factory for creating Clinical Observation records."""

    id: str = Field(default_factory=lambda: f"OBS-{uuid.uuid4().hex[:8]}")
    subject_id: str = "SUBJ-001"
    item_group_oid: str = "VS"
    item_oid: str = "SYSBP"
    value: str = "120"
    status: str = "VALID"
    version_index: int = 1
    created_at: datetime = Field(default_factory=lambda: datetime.now(UTC))

    @classmethod
    def create(cls, **kwargs: Any) -> ClinicalObservationFactory:
        return cls(**kwargs)


class QueryDiscrepancyFactory(BaseModel):
    """Factory for creating clinical discrepancy queries."""

    id: str = Field(default_factory=lambda: f"QRY-{uuid.uuid4().hex[:6].upper()}")
    subject_id: str = "SUBJ-001"
    field_name: str = "SYSBP"
    query_text: str = "Systolic blood pressure out of expected physiological range."
    status: str = "OPEN"
    created_by: str = "cra.monitor@example.com"
    created_at: datetime = Field(default_factory=lambda: datetime.now(UTC))

    @classmethod
    def create(cls, **kwargs: Any) -> QueryDiscrepancyFactory:
        return cls(**kwargs)


class ConsentRecordFactory(BaseModel):
    """Factory for creating Informed Consent Form (ICF) records."""

    id: str = Field(default_factory=lambda: f"CONSENT-{uuid.uuid4().hex[:6].upper()}")
    subject_pseudonym: str = "SUBJ-001"
    study_id: str = "study_oncology_phase3"
    site_id: str = "SITE-101"
    protocol_version: str = "1.0"
    signature_manifest: dict[str, Any] = Field(
        default_factory=lambda: {"algorithm": "ES256", "signature_type": "ELECTRONIC"}
    )
    created_at: datetime = Field(default_factory=lambda: datetime.now(UTC))

    @classmethod
    def create(cls, **kwargs: Any) -> ConsentRecordFactory:
        return cls(**kwargs)


class DocumentMetadataFactory(BaseModel):
    """Factory for creating eTMF / eISF document metadata."""

    id: str = Field(default_factory=lambda: f"DOC-{uuid.uuid4().hex[:6].upper()}")
    study_id: str = "study_oncology_phase3"
    site_id: str | None = "SITE-101"
    zone: str = "01_TRIAL_MANAGEMENT"
    filename: str = "Protocol_Signature_Page_v1.pdf"
    version_index: int = 1
    created_at: datetime = Field(default_factory=lambda: datetime.now(UTC))

    @classmethod
    def create(cls, **kwargs: Any) -> DocumentMetadataFactory:
        return cls(**kwargs)


class AuditLogFactory(BaseModel):
    """Factory for creating 21 CFR Part 11 compliant audit trail log entries."""

    id: str = Field(default_factory=lambda: f"AUDIT-{uuid.uuid4().hex[:8]}")
    user_id: str = "site.crc@example.com"
    action: str = "UPDATE_OBSERVATION"
    resource_type: str = "ClinicalObservation"
    resource_id: str = "OBS-001"
    reason_for_change: str = (
        "Correcting transcription error from source paper worksheet"
    )
    version_index: int = 2
    timestamp: datetime = Field(default_factory=lambda: datetime.now(UTC))

    @classmethod
    def create(cls, **kwargs: Any) -> AuditLogFactory:
        return cls(**kwargs)
