"""Pydantic v2 domain schemas for the Predictive Supply Chain & DTP Logistics Engine.

Requirements: PRD-SCM-001, PRD-SCM-002, PRD-SYS-001, GMP Annex 13, 21 CFR Part 11
"""

from datetime import UTC, datetime
from enum import StrEnum

from pydantic import BaseModel, Field


class SupplyNodeType(StrEnum):
    """Hierarchical tiers in the clinical trial supply distribution network."""

    PRIMARY_MANUFACTURER = "PRIMARY_MANUFACTURER"
    CENTRAL_DEPOT = "CENTRAL_DEPOT"
    REGIONAL_DEPOT = "REGIONAL_DEPOT"
    CLINICAL_SITE = "CLINICAL_SITE"


class StockoutRiskLevel(StrEnum):
    """Stockout vulnerability classification levels."""

    LOW = "LOW"
    MEDIUM = "MEDIUM"
    HIGH = "HIGH"
    CRITICAL = "CRITICAL"


# =========================================================================
# Multi-Echelon Supply Hierarchy Models
# =========================================================================


class MultiEchelonNode(BaseModel):
    """A single node within the multi-echelon supply distribution network."""

    node_id: str
    study_id: str
    node_type: SupplyNodeType
    name: str
    country: str = "US"
    parent_node_id: str | None = None
    storage_capacity: int = Field(default=1000, gt=0)
    on_hand_qty: int = Field(default=0, ge=0)
    reserved_qty: int = Field(default=0, ge=0)
    in_transit_qty: int = Field(default=0, ge=0)

    @property
    def current_utilization(self) -> int:
        """Total capacity utilized (on_hand + reserved)."""
        return self.on_hand_qty + self.reserved_qty

    @property
    def utilization_percentage(self) -> float:
        """Percentage of storage capacity currently consumed."""
        if self.storage_capacity <= 0:
            return 0.0
        return round((self.current_utilization / self.storage_capacity) * 100.0, 2)


class MultiEchelonRollup(BaseModel):
    """Aggregated inventory rollups across all tiers of the supply hierarchy."""

    study_id: str
    total_nodes: int
    total_on_hand: int
    total_reserved: int
    total_in_transit: int
    tier_counts: dict[str, int] = Field(default_factory=dict)
    tier_on_hand: dict[str, int] = Field(default_factory=dict)
    nodes: list[MultiEchelonNode] = Field(default_factory=list)


# =========================================================================
# Predictive Demand & Resupply Forecasting Models
# =========================================================================


class ScheduledVisitDispensation(BaseModel):
    """Protocol-defined dispensation event linked to study visits."""

    visit_day_offset: int = Field(
        ...,
        ge=0,
        description="Visit day relative to subject randomization (e.g. Day 1, 28, 56).",
    )
    dispensation_qty: int = Field(
        default=1, ge=1, description="Quantity of IP kits dispensed at this visit."
    )


class LotExpiryInfo(BaseModel):
    """Investigational product lot expiry information."""

    lot_number: str
    expiry_day_offset: int = Field(
        ..., ge=0, description="Calendar days from today until lot expiration."
    )
    quantity: int = Field(..., gt=0)


class DemandForecastParameters(BaseModel):
    """Parameters governing predictive stochastic demand and inventory simulation."""

    study_id: str
    site_id: str
    active_subjects: int = Field(
        default=0, ge=0, description="Current active enrolled subjects at the site."
    )
    monthly_recruitment_rate: float = Field(
        default=2.0, ge=0.0, description="Expected new subjects enrolled per 30 days."
    )
    simulation_horizon_days: int = Field(
        default=90, ge=7, le=365, description="Projection time window in days."
    )
    lead_time_days: int = Field(
        default=14,
        ge=1,
        description="Reorder transit lead time from depot to site in days.",
    )
    safety_buffer_days: int = Field(
        default=7,
        ge=0,
        description="Additional safety stock threshold in days of demand.",
    )
    resupply_lot_size: int = Field(
        default=10, ge=1, description="Standard batch / shipper pack kit quantity."
    )
    current_on_hand: int = Field(
        default=0, ge=0, description="Immediate usable on-hand kits at the site."
    )
    current_in_transit: int = Field(
        default=0, ge=0, description="Kits currently in transit expected to arrive."
    )
    in_transit_arrival_day: int = Field(
        default=3, ge=0, description="Day offset when in_transit stock is received."
    )
    dispensation_schedule: list[ScheduledVisitDispensation] = Field(
        default_factory=lambda: [
            ScheduledVisitDispensation(visit_day_offset=1, dispensation_qty=1),
            ScheduledVisitDispensation(visit_day_offset=28, dispensation_qty=1),
            ScheduledVisitDispensation(visit_day_offset=56, dispensation_qty=1),
            ScheduledVisitDispensation(visit_day_offset=84, dispensation_qty=1),
        ]
    )
    active_lots: list[LotExpiryInfo] = Field(default_factory=list)


class DailyDemandProjection(BaseModel):
    """Single-day simulation state in the predictive inventory projection."""

    day_index: int
    projected_active_subjects: int
    new_enrollments: int
    expected_demand_kits: int
    projected_on_hand: int
    expired_kits_deducted: int = 0
    in_transit_received: int = 0
    is_reorder_triggered: bool = False
    stockout_risk: StockoutRiskLevel = StockoutRiskLevel.LOW


class PredictiveForecastResult(BaseModel):
    """Output summary of predictive resupply and stockout risk simulation."""

    study_id: str
    site_id: str
    simulation_horizon_days: int
    starting_on_hand: int
    total_projected_demand: int
    days_until_stockout: int | None = None
    recommended_reorder_day_offset: int | None = None
    recommended_reorder_qty: int = 0
    stockout_risk_level: StockoutRiskLevel = StockoutRiskLevel.LOW
    lot_expiry_warnings: list[str] = Field(default_factory=list)
    daily_projections: list[DailyDemandProjection] = Field(default_factory=list)


# =========================================================================
# Direct-to-Patient (DTP) & Cold Chain Logistics Models
# =========================================================================


class TemperatureReading(BaseModel):
    """Discrete temperature telemetry reading during transit or storage."""

    timestamp: str | None = None
    temperature_celsius: float


class DTPDispatchAuthorization(BaseModel):
    """Decision manifest for Direct-to-Patient home shipment authorization."""

    dispatch_id: str
    study_id: str
    site_id: str
    subject_id: str
    kit_number: str
    lot_number: str
    is_eligible_for_dispatch: bool
    rejection_reasons: list[str] = Field(default_factory=list)
    calculated_mkt_celsius: float | None = None
    tamper_seal_id: str | None = None
    authorized_timestamp: str = Field(
        default_factory=lambda: datetime.now(UTC).isoformat()
    )


# =========================================================================
# 21 CFR Part 11 Emergency Unblinding Models
# =========================================================================


class EmergencyUnblindingRequest(BaseModel):
    """Request payload for authorized 21 CFR Part 11 emergency unblinding."""

    study_id: str
    subject_id: str
    requester_user_id: str
    requester_role: str
    reason_for_change: str = Field(
        ...,
        min_length=10,
        description="Mandatory clinical justification for breaking the blind.",
    )
    subject_actual_arm: str = Field(
        ...,
        description="Encrypted or secured treatment arm assigned in trial database.",
    )


class EmergencyUnblindingResult(BaseModel):
    """Tamper-evident 21 CFR Part 11 emergency unblinding manifest."""

    study_id: str
    subject_id: str
    unblinded_arm: str
    authorized_by_user_id: str
    requester_role: str
    reason_for_change: str
    timestamp: str = Field(default_factory=lambda: datetime.now(UTC).isoformat())
    digital_seal: str
    is_authorized: bool
    safety_notification_required: bool = True
    audit_event_type: str = "EMERGENCY_SUBJECT_UNBLINDING"
