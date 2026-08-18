"""Domain models, enums, and data entities for the eTMF microservice."""

from enum import StrEnum


class DocumentStatus(StrEnum):
    """Quality control lifecycle statuses for eTMF documents."""

    DRAFT = "DRAFT"
    TECHNICAL_QC = "TECHNICAL_QC"
    CLINICAL_QC = "CLINICAL_QC"
    APPROVED = "APPROVED"
    ARCHIVED = "ARCHIVED"
    REJECTED = "REJECTED"
    SIGNED = "SIGNED"


class DocumentExpirationAlertState(StrEnum):
    """Lifecycle state for automated document expiration monitoring."""

    ACTIVE = "ACTIVE"
    EXPIRING_SOON = "EXPIRING_SOON"
    EXPIRED = "EXPIRED"
    ACKNOWLEDGED = "ACKNOWLEDGED"


class TMFDocumentType(StrEnum):
    """Classification scope of an eTMF artifact."""

    STUDY_LEVEL = "STUDY_LEVEL"
    SITE_LEVEL = "SITE_LEVEL"
