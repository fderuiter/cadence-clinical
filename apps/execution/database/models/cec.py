"""SQLAlchemy Database Models for Clinical Endpoint Adjudication Committee (CEC).

Requirements: PRD-CEC-001, PRD-CEC-002, PRD-CEC-003, PRD-SYS-124, 21 CFR Part 11, ADR-2199
"""

from datetime import datetime

from sqlalchemy import (
    JSON,
    Boolean,
    DateTime,
    Float,
    Index,
    Integer,
    String,
    Text,
    func,
)
from sqlalchemy.orm import Mapped, mapped_column

from .audit import AuditedModel


class CecDossierRecord(AuditedModel):
    """Represents a packaged de-identified clinical event dossier and its 2+1 review lifecycle."""

    __tablename__ = "cec_dossiers"
    __table_args__ = (
        Index("idx_cec_dossier_lookup", "study_id", "subject_id", "workflow_status"),
        Index("idx_cec_dossier_event", "event_id"),
    )

    study_id: Mapped[str] = mapped_column(String(255), index=True, nullable=False)
    site_id: Mapped[str] = mapped_column(String(255), index=True, nullable=False)
    subject_id: Mapped[str] = mapped_column(String(255), index=True, nullable=False)
    masked_site_id: Mapped[str] = mapped_column(
        String(255), default="SITE-BLINDED", nullable=False
    )
    masked_subject_id: Mapped[str] = mapped_column(
        String(255), default="SUBJ-BLINDED", nullable=False
    )
    event_id: Mapped[str] = mapped_column(String(255), index=True, nullable=False)
    candidate_event_type: Mapped[str] = mapped_column(String(100), nullable=False)
    reported_term: Mapped[str] = mapped_column(String(255), nullable=False)
    verbatim_description: Mapped[str | None] = mapped_column(Text, nullable=True)
    reported_onset_date: Mapped[str | None] = mapped_column(String(50), nullable=True)
    medical_narrative: Mapped[str | None] = mapped_column(Text, nullable=True)

    # Blinding & Reader Assignments
    reader_1_id: Mapped[str] = mapped_column(
        String(255), default="reader_01", nullable=False
    )
    reader_1_name: Mapped[str] = mapped_column(
        String(255), default="Reader 1", nullable=False
    )
    reader_1_status: Mapped[str] = mapped_column(
        String(50), default="PENDING", nullable=False
    )
    reader_1_assessment: Mapped[dict | None] = mapped_column(JSON, nullable=True)

    reader_2_id: Mapped[str] = mapped_column(
        String(255), default="reader_02", nullable=False
    )
    reader_2_name: Mapped[str] = mapped_column(
        String(255), default="Reader 2", nullable=False
    )
    reader_2_status: Mapped[str] = mapped_column(
        String(50), default="PENDING", nullable=False
    )
    reader_2_assessment: Mapped[dict | None] = mapped_column(JSON, nullable=True)

    chair_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    chair_name: Mapped[str | None] = mapped_column(String(255), nullable=True)
    chair_determination: Mapped[dict | None] = mapped_column(JSON, nullable=True)

    # Workflow & Concordance State
    workflow_status: Mapped[str] = mapped_column(
        String(50), default="PENDING_PRIMARY_REVIEWS", nullable=False
    )
    is_discordant: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    discordance_reasons: Mapped[list | None] = mapped_column(JSON, nullable=True)

    final_consensus_classification: Mapped[str | None] = mapped_column(
        String(100), nullable=True
    )
    final_consensus_subtype: Mapped[str | None] = mapped_column(
        String(255), nullable=True
    )
    final_adjudicated_onset_date: Mapped[str | None] = mapped_column(
        String(50), nullable=True
    )
    binding_determination_source: Mapped[str | None] = mapped_column(
        String(100), nullable=True
    )
    adjudication_rationale: Mapped[str | None] = mapped_column(Text, nullable=True)

    # Dossier Content Payload (Bundled eCRFs, Labs, ECGs, Attachments, De-ID metadata)
    dossier_payload: Mapped[dict | None] = mapped_column(JSON, nullable=True)
    electronic_signature: Mapped[dict | None] = mapped_column(JSON, nullable=True)

    # EDC Writeback & SDTM CE Compilation
    edc_writeback_status: Mapped[str] = mapped_column(
        String(50), default="PENDING", nullable=False
    )
    edc_writeback_payload: Mapped[dict | None] = mapped_column(JSON, nullable=True)
    sdtm_ce_record: Mapped[dict | None] = mapped_column(JSON, nullable=True)
    sdtm_suppce_records: Mapped[list | None] = mapped_column(JSON, nullable=True)
    dossier_digest: Mapped[str | None] = mapped_column(String(64), nullable=True)

    # 21 CFR Part 11 Audit fields
    created_at: Mapped[datetime | None] = mapped_column(
        DateTime, server_default=func.now(), nullable=True
    )
    created_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class CecCharterRecord(AuditedModel):
    """Committee charter configuration, voting rules, and diagnostic criteria templates."""

    __tablename__ = "cec_charters"
    __table_args__ = (Index("idx_cec_charter_study", "study_id", "is_active"),)

    study_id: Mapped[str] = mapped_column(String(255), index=True, nullable=False)
    committee_name: Mapped[str] = mapped_column(String(255), nullable=False)
    version: Mapped[str] = mapped_column(String(50), default="1.0", nullable=False)
    voting_rule: Mapped[str] = mapped_column(
        String(50), default="CHAIR_TIE_BREAKER", nullable=False
    )
    minimum_quorum_percentage: Mapped[float] = mapped_column(
        Float, default=66.67, nullable=False
    )
    minimum_members_count: Mapped[int] = mapped_column(
        Integer, default=3, nullable=False
    )
    eligible_members: Mapped[list | None] = mapped_column(JSON, nullable=True)
    endpoint_definitions: Mapped[list | None] = mapped_column(JSON, nullable=True)
    diagnostic_criteria_templates: Mapped[dict | None] = mapped_column(
        JSON, nullable=True
    )
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)

    # 21 CFR Part 11 Audit fields
    created_at: Mapped[datetime | None] = mapped_column(
        DateTime, server_default=func.now(), nullable=True
    )
    created_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class CecMeetingRecord(AuditedModel):
    """Committee meeting quorum tracking and minutes sign-off ledger."""

    __tablename__ = "cec_meetings"
    __table_args__ = (Index("idx_cec_meeting_study", "study_id", "charter_id"),)

    study_id: Mapped[str] = mapped_column(String(255), index=True, nullable=False)
    charter_id: Mapped[str] = mapped_column(String(255), index=True, nullable=False)
    meeting_date: Mapped[str] = mapped_column(String(50), nullable=False)
    attendees: Mapped[list | None] = mapped_column(JSON, nullable=True)
    total_eligible_members: Mapped[int] = mapped_column(Integer, nullable=False)
    quorum_percentage: Mapped[float] = mapped_column(Float, nullable=False)
    quorum_met: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    dossiers_reviewed: Mapped[list | None] = mapped_column(JSON, nullable=True)
    voting_results: Mapped[dict | None] = mapped_column(JSON, nullable=True)
    meeting_minutes: Mapped[str | None] = mapped_column(Text, nullable=True)
    minutes_digest: Mapped[str | None] = mapped_column(String(64), nullable=True)
    signed_by_chair: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    chair_signature: Mapped[dict | None] = mapped_column(JSON, nullable=True)

    # 21 CFR Part 11 Audit fields
    created_at: Mapped[datetime | None] = mapped_column(
        DateTime, server_default=func.now(), nullable=True
    )
    created_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
