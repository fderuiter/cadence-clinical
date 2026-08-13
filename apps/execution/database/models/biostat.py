from datetime import datetime

from sqlalchemy import JSON, DateTime, Index, Integer, String, func
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


class DatasetExportJob(AuditedModel):
    """Represents an asynchronous dataset export and validation job.

    Inherits from AuditedModel to maintain audit logs.

    Attributes:
        status (str): PENDING, PROCESSING, COMPLETED, FAILED.
        progress (int): Percentage completed (0 to 100).
        study_id (str): Identifying clinical trial ID.
        dataset_name (str): Identifying dataset name.
        download_url (str): The URL/path to retrieve the file.
        file_path (str): The server file path where the export is stored.
        error_message (str): Exception details for failed/interrupted jobs.
    """

    __tablename__ = "dataset_export_jobs"

    status: Mapped[str] = mapped_column(String(50), nullable=False, default="PENDING")
    progress: Mapped[int] = mapped_column(Integer, nullable=False, default=0)
    study_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    dataset_name: Mapped[str | None] = mapped_column(String(255), nullable=True)
    download_url: Mapped[str | None] = mapped_column(String(512), nullable=True)
    file_path: Mapped[str | None] = mapped_column(String(512), nullable=True)
    error_message: Mapped[str | None] = mapped_column(String, nullable=True)
    initiated_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
