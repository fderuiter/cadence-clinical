from datetime import datetime

from sqlalchemy import DateTime, Float, Index, Integer, String, func
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
    site_id: Mapped[str] = mapped_column(String(255), nullable=True)
    unit: Mapped[str] = mapped_column(String(50), nullable=True)
    normalized_unit: Mapped[str] = mapped_column(String(50), nullable=True)
    sex: Mapped[str] = mapped_column(String(50), nullable=True)
    age_low: Mapped[float] = mapped_column(Float, nullable=True)
    age_high: Mapped[float] = mapped_column(Float, nullable=True)
    range_low: Mapped[float] = mapped_column(Float, nullable=True)
    range_high: Mapped[float] = mapped_column(Float, nullable=True)
    critical_low: Mapped[float] = mapped_column(Float, nullable=True)
    critical_high: Mapped[float] = mapped_column(Float, nullable=True)

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
    low_bound = synonym("range_low")
    high_bound = synonym("range_high")

    created_at: Mapped[datetime | None] = mapped_column(
        DateTime, server_default=func.now(), nullable=True
    )
    created_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int | None] = mapped_column(Integer, default=1, nullable=True)


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
