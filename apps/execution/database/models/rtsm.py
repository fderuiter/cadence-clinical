from datetime import datetime

from sqlalchemy import (
    JSON,
    Boolean,
    DateTime,
    Index,
    Integer,
    String,
    UniqueConstraint,
    func,
)
from sqlalchemy.orm import Mapped, mapped_column

from .audit import AuditedModel


class RandomizationConfig(AuditedModel):
    """Represents a trial's randomization configuration."""

    __tablename__ = "randomization_configs"

    study_id: Mapped[str] = mapped_column(String(255), unique=True, nullable=False)
    algorithm_type: Mapped[str] = mapped_column(
        String(50), nullable=False
    )  # e.g. "PERMUTED_BLOCK", "MINIMIZATION"
    arms_ratios: Mapped[dict] = mapped_column(
        JSON, nullable=False
    )  # dictionary mapping arms to ratios
    stratification_factors: Mapped[dict] = mapped_column(
        JSON, nullable=True
    )  # definition of stratification factors
    encrypted_block_config: Mapped[str] = mapped_column(
        String, nullable=True
    )  # sensitive block config encrypted-at-rest
    seed: Mapped[int] = mapped_column(Integer, nullable=True)


class StratumState(AuditedModel):
    """Tracks state independently per stratum."""

    __tablename__ = "stratum_states"
    __table_args__ = (
        UniqueConstraint(
            "study_id", "stratum_key", name="uq_stratum_state_study_stratum"
        ),
    )
    __mapper_args__ = {"version_id_col": AuditedModel.version}

    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    stratum_key: Mapped[str] = mapped_column(
        String(255), nullable=False
    )  # e.g., "M_<18" or "DEFAULT"
    block_index: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    encrypted_sequence: Mapped[str] = mapped_column(
        String, nullable=True
    )  # pre-generated treatments list encrypted-at-rest


class SubjectRandomization(AuditedModel):
    """Represents a randomized subject's treatment assignment."""

    __tablename__ = "subject_randomizations"
    __table_args__ = (Index("idx_subject_randomization_study", "study_id"),)

    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    site_id: Mapped[str] = mapped_column(String(255), nullable=True)
    subject_id: Mapped[str] = mapped_column(
        String(255), unique=True, nullable=False
    )  # Enforces exactly one assignment per subject
    stratum_key: Mapped[str] = mapped_column(String(255), nullable=True)
    encrypted_allocation: Mapped[str] = mapped_column(
        String, nullable=False
    )  # assigned treatment arm encrypted-at-rest
    kit_reference: Mapped[str] = mapped_column(
        String(255), nullable=True
    )  # trial kit/IP reference
    randomized_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )


class AllocationKeyMetadata(AuditedModel):
    """Acts as a key-metadata store for derived RTSM allocation keys."""

    __tablename__ = "allocation_key_metadata"
    __table_args__ = (
        UniqueConstraint("key_version", name="uq_allocation_key_metadata_version"),
    )

    key_version: Mapped[int] = mapped_column(Integer, nullable=False)
    salt: Mapped[str] = mapped_column(String(255), nullable=False)
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )


class PendingPredecessorCheck(AuditedModel):
    """Tracks edit check executions deferred because of missing/incomplete predecessor visit data."""

    __tablename__ = "pending_predecessor_checks"
    __table_args__ = (Index("idx_pending_pred_subject_rule", "subject_id", "rule_id"),)

    subject_id: Mapped[str] = mapped_column(String(255), index=True, nullable=False)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    study_id: Mapped[str] = mapped_column(String(255), index=True, nullable=False)
    current_visit_id: Mapped[str] = mapped_column(String(255), nullable=True)
    current_visit_name: Mapped[str] = mapped_column(String(255), nullable=False)
    predecessor_visit_name: Mapped[str] = mapped_column(String(255), nullable=False)
    rule_id: Mapped[str] = mapped_column(String(255), nullable=False)
    observation_id: Mapped[str] = mapped_column(String(255), nullable=False)
    test_code: Mapped[str] = mapped_column(String(100), nullable=False)


class IPKit(AuditedModel):
    """Represents a kit in the investigational product (IP) catalog.

    Preserves blinding: only contains blinded kit identifiers. Treatment or
    drug-code resolution remains confined to an authorized unblinded layer.
    """

    __tablename__ = "ip_kits"

    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    kit_number: Mapped[str] = mapped_column(String(255), unique=True, nullable=False)
    kit_type: Mapped[str] = mapped_column(String(255), nullable=False)
    description: Mapped[str] = mapped_column(String(1000), nullable=True)


class SiteInventory(AuditedModel):
    """Tracks per-site inventory levels, thresholds, and triggers resupply signals."""

    __tablename__ = "site_inventories"
    __table_args__ = (
        UniqueConstraint("site_id", "kit_id", name="uq_site_inventory_site_kit"),
    )

    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False)
    kit_id: Mapped[str] = mapped_column(String(255), nullable=False)
    on_hand_qty: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    reorder_threshold: Mapped[int] = mapped_column(Integer, default=5, nullable=False)
    resupply_signal: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )


class KitDispensation(AuditedModel):
    """Tracks investigational product (IP) kit dispensations to trial subjects."""

    __tablename__ = "kit_dispensations"

    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False)
    kit_id: Mapped[str] = mapped_column(String(255), nullable=False)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False)
    visit_id: Mapped[str] = mapped_column(String(255), nullable=False)
    quantity: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
    timestamp: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )


class ResupplyEvent(AuditedModel):
    """Tracks threshold-triggered resupply events and requests for site inventories.

    Resupply events are persistable and auditable without writing directly to
    the audit ledger.
    """

    __tablename__ = "resupply_events"

    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False)
    kit_id: Mapped[str] = mapped_column(String(255), nullable=False)
    requested_qty: Mapped[int] = mapped_column(Integer, nullable=False)
    status: Mapped[str] = mapped_column(String(50), default="PENDING", nullable=False)
    triggered_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
