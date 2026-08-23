from datetime import datetime

from sqlalchemy import (
    JSON,
    Boolean,
    DateTime,
    Float,
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

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    kit_number: Mapped[str] = mapped_column(
        String(255), unique=True, nullable=False, index=True
    )
    kit_type: Mapped[str] = mapped_column(String(255), nullable=False)
    description: Mapped[str] = mapped_column(String(1000), nullable=True)
    status: Mapped[str] = mapped_column(
        String(50), default="AVAILABLE", nullable=False
    )  # AVAILABLE, QUARANTINED, DISPENSED, DESTROYED, DAMAGED, EXPIRED
    lot_number: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )
    expiration_date: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    node_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    quarantine_reason: Mapped[str | None] = mapped_column(String(500), nullable=True)
    quarantined_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    quarantined_by: Mapped[str | None] = mapped_column(String(255), nullable=True)


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


class SupplyNode(AuditedModel):
    """Represents a node in the multi-echelon clinical supply hierarchy.

    Hierarchy tiers: PRIMARY_MANUFACTURER -> CENTRAL_DEPOT -> REGIONAL_DEPOT -> CLINICAL_SITE.
    """

    __tablename__ = "supply_nodes"
    __table_args__ = (
        UniqueConstraint("study_id", "node_id", name="uq_supply_nodes_study_node"),
    )

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    node_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    node_type: Mapped[str] = mapped_column(
        String(50), nullable=False
    )  # PRIMARY_MANUFACTURER, CENTRAL_DEPOT, REGIONAL_DEPOT, CLINICAL_SITE
    name: Mapped[str] = mapped_column(String(255), nullable=False)
    country: Mapped[str] = mapped_column(String(100), nullable=False)
    address: Mapped[str | None] = mapped_column(String(500), nullable=True)
    parent_node_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )
    storage_capacity: Mapped[int] = mapped_column(Integer, default=1000, nullable=False)
    current_utilization: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    lead_time_days: Mapped[int] = mapped_column(Integer, default=3, nullable=False)
    temperature_conditions: Mapped[list | None] = mapped_column(JSON, nullable=True)
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)


class SupplyNodeInventory(AuditedModel):
    """Tracks inventory levels, allocations, safety stock, and storage bounds per supply node."""

    __tablename__ = "supply_node_inventories"
    __table_args__ = (
        UniqueConstraint(
            "study_id", "node_id", "kit_type", name="uq_node_inv_study_node_kit"
        ),
    )

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    node_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    kit_type: Mapped[str] = mapped_column(String(255), nullable=False)
    lot_number: Mapped[str | None] = mapped_column(String(255), nullable=True)
    on_hand_qty: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    reserved_qty: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    in_transit_qty: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    safety_stock_threshold: Mapped[int] = mapped_column(
        Integer, default=10, nullable=False
    )
    reorder_point: Mapped[int] = mapped_column(Integer, default=20, nullable=False)
    max_capacity: Mapped[int] = mapped_column(Integer, default=500, nullable=False)
    resupply_signal: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )


class TransferOrder(AuditedModel):
    """Represents a multi-echelon stock movement order between supply nodes."""

    __tablename__ = "transfer_orders"
    __table_args__ = (
        UniqueConstraint(
            "study_id", "transfer_order_number", name="uq_transfer_orders_study_num"
        ),
    )

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    transfer_order_number: Mapped[str] = mapped_column(
        String(255), nullable=False, index=True
    )
    source_node_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    destination_node_id: Mapped[str] = mapped_column(
        String(255), nullable=False, index=True
    )
    kit_type: Mapped[str] = mapped_column(String(255), nullable=False)
    quantity: Mapped[int] = mapped_column(Integer, nullable=False)
    priority: Mapped[str] = mapped_column(
        String(50), default="ROUTINE", nullable=False
    )  # ROUTINE, EXPEDITED, EMERGENCY
    status: Mapped[str] = mapped_column(
        String(50), default="REQUESTED", nullable=False
    )  # DRAFT, REQUESTED, APPROVED, PACKING, SHIPPED, DELIVERED, CANCELLED, REJECTED
    temperature_condition: Mapped[str] = mapped_column(
        String(100), default="CONTROLLED_ROOM_TEMP", nullable=False
    )
    carrier: Mapped[str | None] = mapped_column(String(255), nullable=True)
    tracking_number: Mapped[str | None] = mapped_column(String(255), nullable=True)
    estimated_delivery_date: Mapped[datetime | None] = mapped_column(
        DateTime, nullable=True
    )
    requested_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    requested_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    approved_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    approved_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    shipped_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    shipped_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    delivered_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    delivered_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    rejection_reason: Mapped[str | None] = mapped_column(String(500), nullable=True)


class PackingManifest(AuditedModel):
    """GxP packing manifest binding serialized blinded kits and temperature loggers to a transfer order."""

    __tablename__ = "packing_manifests"
    __table_args__ = (
        UniqueConstraint("transfer_order_id", name="uq_packing_manifest_order"),
    )

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    transfer_order_id: Mapped[str] = mapped_column(
        String(255), nullable=False, index=True
    )
    manifest_number: Mapped[str] = mapped_column(
        String(255), nullable=False, unique=True
    )
    serialized_kit_numbers: Mapped[list] = mapped_column(JSON, nullable=False)
    lot_number: Mapped[str] = mapped_column(String(255), nullable=False)
    expiration_date: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    container_serial_number: Mapped[str | None] = mapped_column(
        String(255), nullable=True
    )
    data_logger_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    tamper_seal_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    packed_by: Mapped[str] = mapped_column(String(255), nullable=False)
    packed_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    verified_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    verified_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)


class PredictiveResupplyForecast(AuditedModel):
    """Persisted snapshot of a predictive site kit demand and resupply forecast."""

    __tablename__ = "predictive_resupply_forecasts"

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    kit_type: Mapped[str] = mapped_column(
        String(255), default="DEFAULT", nullable=False
    )
    forecast_generated_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    horizon_days: Mapped[int] = mapped_column(Integer, default=60, nullable=False)
    enrollment_velocity_weekly: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    active_subjects_count: Mapped[int] = mapped_column(
        Integer, default=0, nullable=False
    )
    screening_pipeline_count: Mapped[int] = mapped_column(
        Integer, default=0, nullable=False
    )
    projected_demand_qty: Mapped[int] = mapped_column(
        Integer, default=0, nullable=False
    )
    current_on_hand_qty: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    in_transit_qty: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    projected_runout_date: Mapped[datetime | None] = mapped_column(
        DateTime, nullable=True
    )
    days_of_supply_remaining: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    recommended_order_qty: Mapped[int] = mapped_column(
        Integer, default=0, nullable=False
    )
    stockout_risk_level: Mapped[str] = mapped_column(
        String(50), default="LOW", nullable=False
    )  # LOW, MEDIUM, HIGH, CRITICAL
    forecast_details: Mapped[dict | None] = mapped_column(JSON, nullable=True)


class DrugStabilityBudget(AuditedModel):
    """Defines allowable thermal excursion limits and Arrhenius activation parameters per kit type / lot.

    Conforms to USP <1079> and PDA Technical Report 39.
    """

    __tablename__ = "drug_stability_budgets"

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    kit_type: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    lot_number: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )
    storage_condition: Mapped[str] = mapped_column(
        String(100), default="REFRIGERATED_2_TO_8", nullable=False
    )
    min_temp_celsius: Mapped[float] = mapped_column(Float, default=2.0, nullable=False)
    max_temp_celsius: Mapped[float] = mapped_column(Float, default=8.0, nullable=False)
    max_mkt_celsius: Mapped[float] = mapped_column(Float, default=8.0, nullable=False)
    max_cumulative_hours_above_upper: Mapped[float] = mapped_column(
        Float, default=24.0, nullable=False
    )
    max_cumulative_hours_below_lower: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    activation_energy_over_r: Mapped[float] = mapped_column(
        Float, default=10000.0, nullable=False
    )  # delta_H / R = 10000 K
    hard_min_temp_celsius: Mapped[float | None] = mapped_column(
        Float, default=0.0, nullable=True
    )  # Freezing threshold
    hard_max_temp_celsius: Mapped[float | None] = mapped_column(
        Float, default=25.0, nullable=True
    )  # Upper critical limit


class TemperatureLogger(AuditedModel):
    """Represents a digital temperature data logger device bound to a shipment or storage facility."""

    __tablename__ = "temperature_loggers"
    __table_args__ = (
        UniqueConstraint("study_id", "logger_id", name="uq_temp_loggers_study_logger"),
    )

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    logger_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    device_model: Mapped[str] = mapped_column(
        String(255), default="Sensitech TempTale Ultra", nullable=False
    )
    serial_number: Mapped[str | None] = mapped_column(String(255), nullable=True)
    logger_type: Mapped[str] = mapped_column(
        String(50), default="SHIPMENT", nullable=False
    )  # SHIPMENT, SITE_STORAGE, DEPOT_STORAGE
    assigned_transfer_order_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )
    assigned_node_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )
    assigned_container_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True
    )
    calibration_expiry_date: Mapped[datetime | None] = mapped_column(
        DateTime, nullable=True
    )
    status: Mapped[str] = mapped_column(
        String(50), default="ACTIVE", nullable=False
    )  # ACTIVE, INGESTED, DECOMMISSIONED


class TemperatureLogIngestion(AuditedModel):
    """Audit record for an ingested temperature data logger file (CSV, XML, JSON)."""

    __tablename__ = "temperature_log_ingestions"

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    logger_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    file_format: Mapped[str] = mapped_column(
        String(50), nullable=False
    )  # CSV, XML, JSON
    filename: Mapped[str] = mapped_column(String(255), nullable=False)
    reading_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    ingested_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    ingested_by: Mapped[str] = mapped_column(String(255), nullable=False)
    raw_data_hash: Mapped[str | None] = mapped_column(
        String(255), nullable=True
    )  # SHA-256 hash for Part 11 integrity


class TemperatureReading(AuditedModel):
    """Individual timeseries temperature observation point from a data logger."""

    __tablename__ = "temperature_readings"
    __table_args__ = (
        Index(
            "idx_temp_readings_study_logger_time", "study_id", "logger_id", "timestamp"
        ),
    )

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    logger_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    timestamp: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    temperature_celsius: Mapped[float] = mapped_column(Float, nullable=False)


class ExcursionEvaluation(AuditedModel):
    """Evaluated MKT and thermal stability budget result with automated quarantine decisions."""

    __tablename__ = "excursion_evaluations"

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    logger_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    entity_type: Mapped[str] = mapped_column(
        String(50), nullable=False
    )  # SHIPMENT, SITE_STORAGE, DEPOT_STORAGE
    entity_id: Mapped[str] = mapped_column(
        String(255), nullable=False, index=True
    )  # transfer_order_id, node_id, or storage_unit_id
    evaluation_status: Mapped[str] = mapped_column(
        String(50), nullable=False
    )  # COMPLIANT, EXCURSION_ACCEPTABLE, EXCURSION_BREACH
    mean_kinetic_temperature_celsius: Mapped[float] = mapped_column(
        Float, nullable=False
    )
    arithmetic_mean_celsius: Mapped[float] = mapped_column(Float, nullable=False)
    min_temperature_celsius: Mapped[float] = mapped_column(Float, nullable=False)
    max_temperature_celsius: Mapped[float] = mapped_column(Float, nullable=False)
    cumulative_hours_above_upper: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    cumulative_hours_below_lower: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    hard_boundary_breached: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    mkt_breached: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    cumulative_time_breached: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    quarantine_triggered: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    quarantined_kit_count: Mapped[int] = mapped_column(
        Integer, default=0, nullable=False
    )
    evaluated_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    evaluated_by: Mapped[str] = mapped_column(String(255), nullable=False)
    excursion_events: Mapped[list | None] = mapped_column(JSON, nullable=True)
    summary_notes: Mapped[str | None] = mapped_column(String(1000), nullable=True)


class LotExpiryExtension(AuditedModel):
    """Tracks stability-backed lot expiration date extensions across clinical depots and sites."""

    __tablename__ = "lot_expiry_extensions"

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    lot_number: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    kit_type: Mapped[str] = mapped_column(String(255), nullable=False)
    initial_expiry_date: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    extended_expiry_date: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    extension_reason: Mapped[str] = mapped_column(String(500), nullable=False)
    stability_protocol_reference: Mapped[str] = mapped_column(
        String(255), nullable=False
    )
    authorized_by: Mapped[str] = mapped_column(String(255), nullable=False)
    qa_approver_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    approval_status: Mapped[str] = mapped_column(
        String(50), default="APPROVED", nullable=False
    )  # APPROVED, PENDING_QA, REJECTED
    affected_kits_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    relabeling_tasks_generated: Mapped[int] = mapped_column(
        Integer, default=0, nullable=False
    )
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )


class ReLabelingTask(AuditedModel):
    """GxP physical re-labeling action task for a lot expiry extension at a facility."""

    __tablename__ = "relabeling_tasks"
    __table_args__ = (
        UniqueConstraint(
            "study_id", "task_number", name="uq_relabeling_task_study_num"
        ),
    )

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    task_number: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    lot_number: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    node_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    affected_kit_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    old_expiry_date: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    new_expiry_date: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    status: Mapped[str] = mapped_column(
        String(50), default="PENDING_RELABEL", nullable=False
    )  # PENDING_RELABEL, IN_PROGRESS, COMPLETED, VERIFIED
    assigned_to: Mapped[str | None] = mapped_column(String(255), nullable=True)
    completed_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    completed_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    verified_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    verified_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    label_barcode_hash: Mapped[str | None] = mapped_column(String(255), nullable=True)
    relabeling_notes: Mapped[str | None] = mapped_column(String(1000), nullable=True)


class AdaptiveTrialConfig(AuditedModel):
    """Protocol configuration for adaptive oncology trial dose-escalation and RAR designs."""

    __tablename__ = "adaptive_trial_configs"
    __table_args__ = (
        UniqueConstraint("study_id", name="uq_adaptive_trial_configs_study"),
    )

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    design_type: Mapped[str] = mapped_column(
        String(50), nullable=False
    )  # THREE_PLUS_THREE, BOIN, CRM, RAR
    target_toxicity_rate: Mapped[float | None] = mapped_column(Float, nullable=True)
    dose_levels: Mapped[list] = mapped_column(JSON, default=list, nullable=False)
    boin_config: Mapped[dict | None] = mapped_column(JSON, nullable=True)
    crm_config: Mapped[dict | None] = mapped_column(JSON, nullable=True)
    three_plus_three_config: Mapped[dict | None] = mapped_column(JSON, nullable=True)
    rar_config: Mapped[dict | None] = mapped_column(JSON, nullable=True)
    cohort_size: Mapped[int] = mapped_column(Integer, default=3, nullable=False)
    max_sample_size: Mapped[int | None] = mapped_column(Integer, nullable=True)
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)


class DoseCohortState(AuditedModel):
    """Tracks active and historical dose escalation cohort states for a trial."""

    __tablename__ = "dose_cohort_states"
    __table_args__ = (
        UniqueConstraint(
            "study_id", "cohort_number", name="uq_dose_cohort_study_cohort"
        ),
    )

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    cohort_number: Mapped[int] = mapped_column(Integer, nullable=False)
    current_dose_level_index: Mapped[int] = mapped_column(Integer, nullable=False)
    current_dose_name: Mapped[str] = mapped_column(String(255), nullable=False)
    enrolled_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    dlt_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    cumulative_dose_evaluations: Mapped[dict] = mapped_column(
        JSON, default=dict, nullable=False
    )
    eliminated_doses: Mapped[list] = mapped_column(JSON, default=list, nullable=False)
    recommendation_action: Mapped[str] = mapped_column(
        String(50), default="STAY", nullable=False
    )
    recommended_dose_level_index: Mapped[int | None] = mapped_column(
        Integer, nullable=True
    )
    recommended_dose_name: Mapped[str | None] = mapped_column(
        String(255), nullable=True
    )
    status: Mapped[str] = mapped_column(String(50), default="ENROLLING", nullable=False)
    rationale: Mapped[str | None] = mapped_column(String(2000), nullable=True)
    mtd_declared_dose_level: Mapped[int | None] = mapped_column(Integer, nullable=True)
    mtd_declared_dose_name: Mapped[str | None] = mapped_column(
        String(255), nullable=True
    )


class AdaptiveCohortTransitionLog(AuditedModel):
    """Immutable 21 CFR Part 11 audited ledger of cohort transitions, decisions, and Bayesian updates."""

    __tablename__ = "adaptive_cohort_transition_logs"

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    cohort_number: Mapped[int] = mapped_column(Integer, nullable=False)
    from_dose_level: Mapped[int] = mapped_column(Integer, nullable=False)
    to_dose_level: Mapped[int] = mapped_column(Integer, nullable=False)
    action_taken: Mapped[str] = mapped_column(String(50), nullable=False)
    algorithm_recommendation: Mapped[str] = mapped_column(String(50), nullable=False)
    override_occurred: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    override_reason: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    evaluated_dlt_rate: Mapped[float | None] = mapped_column(Float, nullable=True)
    posterior_toxicity_estimates: Mapped[dict | None] = mapped_column(
        JSON, nullable=True
    )
    authorized_by: Mapped[str] = mapped_column(String(255), nullable=False)
    transitioned_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    signature_token: Mapped[str | None] = mapped_column(String(512), nullable=True)
    cryptographic_checksum: Mapped[str | None] = mapped_column(
        String(64), nullable=True
    )


class RARArmState(AuditedModel):
    """Current state and posterior metrics for Response-Adaptive Randomization treatment arms."""

    __tablename__ = "rar_arm_states"
    __table_args__ = (
        UniqueConstraint("study_id", "arm_id", name="uq_rar_arm_study_arm"),
    )

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    arm_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    arm_name: Mapped[str] = mapped_column(String(255), nullable=False)
    is_control: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    status: Mapped[str] = mapped_column(
        String(50), default="ACTIVE", nullable=False
    )  # ACTIVE, DROPPED_FUTILITY, GRADUATED_SUCCESS, TERMINATED
    enrolled_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    response_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    prior_alpha: Mapped[float] = mapped_column(Float, default=1.0, nullable=False)
    prior_beta: Mapped[float] = mapped_column(Float, default=1.0, nullable=False)
    posterior_alpha: Mapped[float] = mapped_column(Float, default=1.0, nullable=False)
    posterior_beta: Mapped[float] = mapped_column(Float, default=1.0, nullable=False)
    posterior_mean: Mapped[float] = mapped_column(Float, default=0.5, nullable=False)
    allocation_probability: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    superiority_vs_control_prob: Mapped[float | None] = mapped_column(
        Float, nullable=True
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )


class RARAllocationLog(AuditedModel):
    """21 CFR Part 11 audited history of dynamic RAR probability re-weighting and arm events."""

    __tablename__ = "rar_allocation_logs"

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    event_type: Mapped[str] = mapped_column(
        String(50), nullable=False
    )  # INITIALIZATION, UPDATE_PROBABILITIES, ARM_ADDED, ARM_DROPPED_FUTILITY, ARM_GRADUATED_SUCCESS
    probabilities_snapshot: Mapped[dict] = mapped_column(JSON, nullable=False)
    sample_sizes_snapshot: Mapped[dict] = mapped_column(JSON, nullable=False)
    responses_snapshot: Mapped[dict] = mapped_column(JSON, nullable=False)
    triggered_by: Mapped[str] = mapped_column(String(255), nullable=False)
    timestamp: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    change_reason: Mapped[str | None] = mapped_column(String(500), nullable=True)
    cryptographic_hash: Mapped[str | None] = mapped_column(String(64), nullable=True)


# =========================================================================
# Cell & Gene Therapy (CGT) Vein-to-Vein COC/COI & Destruction Database Models
# Requirements: PRD-SCM-005, PRD-SCM-006, ADR-2199
# =========================================================================


class CGTChainOfCustody(AuditedModel):
    """Represents an unbroken Vein-to-Vein Chain of Custody & Chain of Identity record.

    Conforms to PRD-SCM-005, binding subject identity, ISBT 128 DIN, and batch records.
    """

    __tablename__ = "cgt_chains_of_custody"
    __table_args__ = (UniqueConstraint("study_id", "din", name="uq_cgt_coc_study_din"),)

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    din: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    batch_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    current_stage: Mapped[str] = mapped_column(
        String(50), default="APHERESIS_COLLECTION", nullable=False
    )
    status: Mapped[str] = mapped_column(String(50), default="COLLECTED", nullable=False)
    tamper_evident_barcode: Mapped[str | None] = mapped_column(
        String(1000), nullable=True
    )
    barcode_signature: Mapped[str | None] = mapped_column(String(512), nullable=True)
    collection_site_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )
    collection_timestamp: Mapped[datetime | None] = mapped_column(
        DateTime, nullable=True
    )
    manufacturing_facility_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True
    )
    cryo_temp_excursion_count: Mapped[int] = mapped_column(
        Integer, default=0, nullable=False
    )
    lowest_temp_celsius: Mapped[float | None] = mapped_column(Float, nullable=True)
    highest_temp_celsius: Mapped[float | None] = mapped_column(Float, nullable=True)
    infusion_unlocked: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    infusion_unlock_token: Mapped[str | None] = mapped_column(
        String(512), nullable=True
    )
    infused_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    reconciled_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)


class CGTMilestoneEvent(AuditedModel):
    """Audit-tracked lifecycle event with GPS coordinates and cryogenic temperature logging."""

    __tablename__ = "cgt_milestone_events"
    __table_args__ = (
        Index("idx_cgt_milestone_coc_time", "chain_of_custody_id", "timestamp"),
    )

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    chain_of_custody_id: Mapped[str] = mapped_column(
        String(255), nullable=False, index=True
    )
    stage: Mapped[str] = mapped_column(String(50), nullable=False)
    milestone_type: Mapped[str] = mapped_column(
        String(50), default="STATUS_UPDATE", nullable=False
    )
    timestamp: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    location_name: Mapped[str] = mapped_column(String(255), nullable=False)
    facility_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    latitude: Mapped[float | None] = mapped_column(Float, nullable=True)
    longitude: Mapped[float | None] = mapped_column(Float, nullable=True)
    temperature_celsius: Mapped[float] = mapped_column(Float, nullable=False)
    temperature_status: Mapped[str] = mapped_column(
        String(50), default="OPTIMAL", nullable=False
    )
    custodian_id: Mapped[str] = mapped_column(String(255), nullable=False)
    custodian_name: Mapped[str] = mapped_column(String(255), nullable=False)
    custodian_role: Mapped[str] = mapped_column(String(100), nullable=False)
    carrier: Mapped[str | None] = mapped_column(String(255), nullable=True)
    tracking_number: Mapped[str | None] = mapped_column(String(255), nullable=True)
    seal_intact: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)
    seal_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    dry_shipper_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    electronic_signature: Mapped[dict | None] = mapped_column(JSON, nullable=True)
    notes: Mapped[str | None] = mapped_column(String(1000), nullable=True)


class CGTBedsideVerification(AuditedModel):
    """Mandatory dual-person electronic bedside verification record gating infusion eCRF."""

    __tablename__ = "cgt_bedside_verifications"

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    chain_of_custody_id: Mapped[str] = mapped_column(
        String(255), nullable=False, index=True
    )
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    din: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    batch_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    scanned_wristband_subject_id: Mapped[str] = mapped_column(
        String(255), nullable=False
    )
    scanned_bag_barcode: Mapped[str] = mapped_column(String(1000), nullable=False)
    wristband_matched: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    bag_matched: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    temperature_chain_verified: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    primary_verifier_id: Mapped[str] = mapped_column(String(255), nullable=False)
    primary_verifier_name: Mapped[str] = mapped_column(String(255), nullable=False)
    primary_verifier_role: Mapped[str] = mapped_column(String(100), nullable=False)
    primary_signature_token: Mapped[str] = mapped_column(String(512), nullable=False)
    primary_signed_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    secondary_verifier_id: Mapped[str] = mapped_column(String(255), nullable=False)
    secondary_verifier_name: Mapped[str] = mapped_column(String(255), nullable=False)
    secondary_verifier_role: Mapped[str] = mapped_column(String(100), nullable=False)
    secondary_signature_token: Mapped[str] = mapped_column(String(512), nullable=False)
    secondary_signed_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    status: Mapped[str] = mapped_column(String(50), default="VERIFIED", nullable=False)
    unlock_token: Mapped[str] = mapped_column(String(512), nullable=False)
    verification_notes: Mapped[str | None] = mapped_column(String(1000), nullable=True)


class CGTDestructionCertificate(AuditedModel):
    """Formal Certificate of Destruction with dual witness electronic signatures for drug reconciliation."""

    __tablename__ = "cgt_destruction_certificates"
    __table_args__ = (
        UniqueConstraint(
            "study_id", "certificate_number", name="uq_cgt_destruction_cert_num"
        ),
    )

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    certificate_number: Mapped[str] = mapped_column(
        String(255), nullable=False, index=True
    )
    chain_of_custody_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )
    subject_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )
    din: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    lot_number: Mapped[str] = mapped_column(String(255), nullable=False)
    kit_numbers: Mapped[list] = mapped_column(JSON, nullable=False)
    vial_identifiers: Mapped[list] = mapped_column(JSON, nullable=False)
    quantity_destroyed: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
    unit_of_measure: Mapped[str] = mapped_column(
        String(50), default="VIALS", nullable=False
    )
    residual_volume_ml: Mapped[float | None] = mapped_column(Float, nullable=True)
    destruction_reason: Mapped[str] = mapped_column(String(255), nullable=False)
    destruction_method: Mapped[str] = mapped_column(String(100), nullable=False)
    destruction_facility: Mapped[str] = mapped_column(String(255), nullable=False)
    destruction_timestamp: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    witness_1_id: Mapped[str] = mapped_column(String(255), nullable=False)
    witness_1_name: Mapped[str] = mapped_column(String(255), nullable=False)
    witness_1_role: Mapped[str] = mapped_column(String(100), nullable=False)
    witness_1_signature_token: Mapped[str] = mapped_column(String(512), nullable=False)
    witness_1_signed_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    witness_2_id: Mapped[str] = mapped_column(String(255), nullable=False)
    witness_2_name: Mapped[str] = mapped_column(String(255), nullable=False)
    witness_2_role: Mapped[str] = mapped_column(String(100), nullable=False)
    witness_2_signature_token: Mapped[str] = mapped_column(String(512), nullable=False)
    witness_2_signed_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    cryptographic_seal: Mapped[str] = mapped_column(String(512), nullable=False)
    status: Mapped[str] = mapped_column(String(50), default="COMPLETED", nullable=False)
    reconciliation_notes: Mapped[str | None] = mapped_column(
        String(1000), nullable=True
    )


class CGTVialReturnReconciliation(AuditedModel):
    """Accountability tracking for kit and vial returns with patient compliance calculation."""

    __tablename__ = "cgt_vial_return_reconciliations"

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    kit_number: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    vial_identifier: Mapped[str] = mapped_column(
        String(255), nullable=False, index=True
    )
    lot_number: Mapped[str] = mapped_column(String(255), nullable=False)
    din: Mapped[str | None] = mapped_column(String(255), nullable=True)
    expected_quantity: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
    returned_quantity: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
    intact_vials: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    used_vials: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    residual_volume_ml: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    adherence_percentage: Mapped[float] = mapped_column(
        Float, default=100.0, nullable=False
    )
    disposition: Mapped[str] = mapped_column(
        String(50), default="RECONCILED_FOR_DESTRUCTION", nullable=False
    )
    logged_by: Mapped[str] = mapped_column(String(255), nullable=False)
    logged_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    certificate_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )
