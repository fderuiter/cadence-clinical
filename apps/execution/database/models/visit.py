from datetime import datetime

from sqlalchemy import DateTime, Integer, String, func
from sqlalchemy.orm import Mapped, mapped_column

from .audit import AuditedModel


class ClinicalVisit(AuditedModel):
    """Represents a scheduled clinical event/visit for a subject.

    Maintains scheduled trial encounters like Screening, Baseline, Week 4, etc.
    """

    __tablename__ = "clinical_visits"

    subject_id: Mapped[str] = mapped_column(String(255), nullable=False)
    visit_name: Mapped[str] = mapped_column(String(255), nullable=False)
    visit_date: Mapped[datetime] = mapped_column(DateTime, default=func.now())
    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    protocol_version_tag: Mapped[str | None] = mapped_column(String(50), nullable=True)
    protocol_version_index: Mapped[int | None] = mapped_column(Integer, nullable=True)
    planned_date: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    window_start: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    window_end: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    window_status: Mapped[str | None] = mapped_column(String(50), nullable=True)
