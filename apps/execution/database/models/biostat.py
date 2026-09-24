from datetime import datetime

from sqlalchemy import JSON, DateTime, Index, String, func
from sqlalchemy.orm import Mapped, mapped_column

from .audit import AuditedModel


class BiostatExport(AuditedModel):
    """Tracks Dataset-JSON biostat export records."""

    __tablename__ = "biostat_exports"
    __table_args__ = (Index("idx_biostat_exports_coords", "study_id", "export_type"),)

    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    export_type: Mapped[str] = mapped_column(
        String(50), nullable=False
    )  # "SDTM", "ADaM", "BUNDLE"
    dataset_name: Mapped[str] = mapped_column(
        String(50), nullable=True
    )  # e.g., "DM", "ADSL", or NULL
    status: Mapped[str] = mapped_column(
        String(50), nullable=False
    )  # "SUCCESS", "FAILED"
    error_message: Mapped[str] = mapped_column(String, nullable=True)
    created_at: Mapped[datetime] = mapped_column(DateTime, default=func.now())


class SDTMDomainRecord(AuditedModel):
    """Represents a transformed, strongly-typed, validated SDTM domain record in the database."""

    __tablename__ = "sdtm_domain_records"

    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    domain: Mapped[str] = mapped_column(String(50), nullable=False)
    usubjid: Mapped[str] = mapped_column(String(255), nullable=False)
    record_data: Mapped[dict] = mapped_column(JSON, nullable=False)
