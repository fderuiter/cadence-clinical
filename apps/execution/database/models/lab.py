from datetime import datetime

from sqlalchemy import JSON, Boolean, DateTime, Float, Index, Integer, String, func
from sqlalchemy.orm import Mapped, mapped_column, synonym

from .audit import AuditedModel


class LabReferenceRange(AuditedModel):
    """Represents lab reference range settings for clinical trials, enabling validation of lab values."""

    __tablename__ = "lab_reference_ranges"
    __table_args__ = (
        Index("idx_lab_range_lookup", "study_id", "test_code", "lab_source", "site_id"),
    )

    study_id: Mapped[str] = mapped_column(String(255), index=True, nullable=False)
    test_code: Mapped[str] = mapped_column(String(100), index=True, nullable=False)
    test_name: Mapped[str] = mapped_column(String(255), nullable=False)
    lab_source: Mapped[str] = mapped_column(
        String(50), nullable=False
    )  # "CENTRAL" or "LOCAL"
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    unit: Mapped[str | None] = mapped_column(String(50), nullable=True)
    normalized_unit: Mapped[str | None] = mapped_column(String(50), nullable=True)
    sex: Mapped[str | None] = mapped_column(String(50), nullable=True)
    ethnicity: Mapped[str | None] = mapped_column(String(100), nullable=True)
    age_low: Mapped[float | None] = mapped_column(Float, nullable=True)
    age_high: Mapped[float | None] = mapped_column(Float, nullable=True)
    range_low: Mapped[float | None] = mapped_column(Float, nullable=True)
    range_high: Mapped[float | None] = mapped_column(Float, nullable=True)
    critical_low: Mapped[float | None] = mapped_column(Float, nullable=True)
    critical_high: Mapped[float | None] = mapped_column(Float, nullable=True)

    # GxP 21 CFR Part 11 Audit fields
    created_at: Mapped[datetime | None] = mapped_column(
        DateTime, server_default=func.now(), nullable=True
    )
    created_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Synonyms for backward compatibility
    source = synonym("lab_source")
    sex_applicability = synonym("sex")
    ethnicity_applicability = synonym("ethnicity")
    low_bound = synonym("range_low")
    high_bound = synonym("range_high")


class LocalLabCertification(AuditedModel):
    """Represents local laboratory accreditation and certification tracking."""

    __tablename__ = "local_lab_certifications"
    __table_args__ = (
        Index("idx_local_lab_cert_lookup", "study_id", "site_id", "status"),
    )

    study_id: Mapped[str] = mapped_column(String(255), index=True, nullable=False)
    site_id: Mapped[str] = mapped_column(String(255), index=True, nullable=False)
    lab_name: Mapped[str] = mapped_column(String(255), nullable=False)
    lab_identifier: Mapped[str | None] = mapped_column(String(100), nullable=True)
    certification_number: Mapped[str] = mapped_column(
        String(100), nullable=False
    )  # e.g. CLIA/CAP number
    accreditation_body: Mapped[str] = mapped_column(
        String(100), nullable=False
    )  # e.g. "CLIA", "CAP", "ISO 15189"
    effective_date: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    expiration_date: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    status: Mapped[str] = mapped_column(
        String(50), default="ACTIVE", nullable=False
    )  # "ACTIVE", "EXPIRED", "SUSPENDED", "PENDING_RENEWAL"
    director_name: Mapped[str | None] = mapped_column(String(255), nullable=True)
    is_accredited: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)
    notes: Mapped[str | None] = mapped_column(String(1000), nullable=True)

    # GxP 21 CFR Part 11 Audit fields
    created_at: Mapped[datetime | None] = mapped_column(
        DateTime, server_default=func.now(), nullable=True
    )
    created_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class StagedAdverseEventReview(AuditedModel):
    """Represents an automated Grade 3/4 CTCAE/DAIDS laboratory toxicity trigger staged for Investigator review."""

    __tablename__ = "staged_adverse_event_reviews"
    __table_args__ = (
        Index(
            "idx_staged_ae_lookup",
            "study_id",
            "site_id",
            "subject_id",
            "review_status",
        ),
    )

    study_id: Mapped[str] = mapped_column(String(255), index=True, nullable=False)
    site_id: Mapped[str | None] = mapped_column(String(255), index=True, nullable=True)
    subject_id: Mapped[str] = mapped_column(String(255), index=True, nullable=False)
    visit_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    observation_id: Mapped[str | None] = mapped_column(
        String(36), nullable=True, index=True
    )
    form_submission_id: Mapped[str | None] = mapped_column(String(36), nullable=True)
    query_id: Mapped[str | None] = mapped_column(String(36), nullable=True)
    test_code: Mapped[str] = mapped_column(String(100), nullable=False)
    test_name: Mapped[str] = mapped_column(String(255), nullable=False)
    lab_value: Mapped[float] = mapped_column(Float, nullable=False)
    unit: Mapped[str] = mapped_column(String(50), nullable=False)
    ctcae_grade: Mapped[int] = mapped_column(Integer, nullable=False)  # 3 or 4
    grading_scale: Mapped[str] = mapped_column(
        String(50), default="CTCAE_V5", nullable=False
    )
    toxicity_term: Mapped[str] = mapped_column(String(255), nullable=False)
    clinical_description: Mapped[str] = mapped_column(String(1000), nullable=False)
    is_hys_law_warning: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    is_delta_check_alert: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    delta_percentage: Mapped[float | None] = mapped_column(Float, nullable=True)
    review_status: Mapped[str] = mapped_column(
        String(50), default="STAGED_FOR_INVESTIGATOR_REVIEW", nullable=False
    )  # "STAGED_FOR_INVESTIGATOR_REVIEW", "CONFIRMED_AS_AE", "DISMISSED_NOT_CLINICALLY_SIGNIFICANT", "ESCALATED_SAE"
    reviewed_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    reviewed_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    investigator_notes: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    electronic_signature_manifest: Mapped[dict | None] = mapped_column(
        JSON, nullable=True
    )

    # GxP 21 CFR Part 11 Audit fields
    created_at: Mapped[datetime | None] = mapped_column(
        DateTime, server_default=func.now(), nullable=True
    )
    created_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class LabTestMasterLegacy(AuditedModel):
    """Represents the legacy lab test master catalog."""

    __tablename__ = "lab_test_master"
    __table_args__ = (Index("idx_lab_master_legacy_lookup", "study_id", "test_code"),)

    study_id: Mapped[str] = mapped_column(String(255), index=True, nullable=False)
    test_code: Mapped[str] = mapped_column(String(100), index=True, nullable=False)
    test_name: Mapped[str] = mapped_column(String(255), nullable=False)
    default_unit: Mapped[str | None] = mapped_column(String(50), nullable=True)
    normalized_unit: Mapped[str | None] = mapped_column(String(50), nullable=True)
    loinc_code: Mapped[str | None] = mapped_column(String(50), nullable=True)

    created_at: Mapped[datetime | None] = mapped_column(
        DateTime, server_default=func.now(), nullable=True
    )
    created_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int | None] = mapped_column(Integer, default=1, nullable=True)


class LabUnitConversion(AuditedModel):
    """Represents a unit conversion formula for laboratory values."""

    __tablename__ = "lab_unit_conversions"
    __table_args__ = (
        Index(
            "idx_lab_unit_conversion_lookup",
            "study_id",
            "test_code",
            "from_unit",
            "to_unit",
        ),
    )

    study_id: Mapped[str] = mapped_column(String(255), index=True, nullable=False)
    test_code: Mapped[str] = mapped_column(String(100), index=True, nullable=False)
    from_unit: Mapped[str] = mapped_column(String(50), nullable=False)
    to_unit: Mapped[str] = mapped_column(String(50), nullable=False)
    factor: Mapped[float] = mapped_column(Float, nullable=False)
    offset: Mapped[float | None] = mapped_column(Float, nullable=True)

    created_at: Mapped[datetime | None] = mapped_column(
        DateTime, server_default=func.now(), nullable=True
    )
    created_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int | None] = mapped_column(Integer, default=1, nullable=True)


class LabTestMaster(AuditedModel):
    """Represents a laboratory test catalog master record, enabling standardized catalog definition."""

    __tablename__ = "lab_test_masters"
    __table_args__ = (Index("idx_lab_test_master_lookup", "study_id", "test_code"),)

    study_id: Mapped[str] = mapped_column(String(255), index=True, nullable=False)
    test_code: Mapped[str] = mapped_column(String(100), index=True, nullable=False)
    test_name: Mapped[str] = mapped_column(String(255), nullable=False)
    default_unit: Mapped[str] = mapped_column(String(50), nullable=False)
    normalized_unit: Mapped[str] = mapped_column(String(50), nullable=False)
    loinc_code: Mapped[str | None] = mapped_column(String(50), nullable=True)

    # GxP 21 CFR Part 11 Audit fields
    created_at: Mapped[datetime | None] = mapped_column(
        DateTime, server_default=func.now(), nullable=True
    )
    created_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
