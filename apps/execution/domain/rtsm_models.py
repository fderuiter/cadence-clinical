"""Domain models and enums for RTSM Multi-Echelon Supply Hierarchy & Predictive Resupply.

Requirements: PRD-SCM-001, GMP Annex 13
"""

from datetime import datetime
from enum import StrEnum

from pydantic import BaseModel, ConfigDict, Field


class SupplyNodeType(StrEnum):
    """Hierarchy tiers for clinical trial drug supply chain."""

    PRIMARY_MANUFACTURER = "PRIMARY_MANUFACTURER"
    CENTRAL_DEPOT = "CENTRAL_DEPOT"
    REGIONAL_DEPOT = "REGIONAL_DEPOT"
    CLINICAL_SITE = "CLINICAL_SITE"


class TransferOrderPriority(StrEnum):
    """Urgency priorities for stock transfer replenishment orders."""

    ROUTINE = "ROUTINE"
    EXPEDITED = "EXPEDITED"
    EMERGENCY = "EMERGENCY"


class TransferOrderStatus(StrEnum):
    """Lifecycle states for multi-echelon stock movement orders."""

    DRAFT = "DRAFT"
    REQUESTED = "REQUESTED"
    APPROVED = "APPROVED"
    PACKING = "PACKING"
    SHIPPED = "SHIPPED"
    DELIVERED = "DELIVERED"
    CANCELLED = "CANCELLED"
    REJECTED = "REJECTED"


class StockoutRiskLevel(StrEnum):
    """Categorized risk of site kit stockout before replenishment arrival."""

    LOW = "LOW"
    MEDIUM = "MEDIUM"
    HIGH = "HIGH"
    CRITICAL = "CRITICAL"


class TemperatureCondition(StrEnum):
    """Storage and transport temperature regimes for investigational product."""

    ULTRA_COLD_MINUS_80 = "ULTRA_COLD_MINUS_80"
    FROZEN_MINUS_20 = "FROZEN_MINUS_20"
    REFRIGERATED_2_TO_8 = "REFRIGERATED_2_TO_8"
    CONTROLLED_ROOM_TEMP = "CONTROLLED_ROOM_TEMP"


class DailyDemandProjection(BaseModel):
    """Daily projected kit requirement and stock trajectory."""

    model_config = ConfigDict(strict=True)

    day_index: int = Field(..., description="Day index from forecast start (1..N).")
    date: str = Field(..., description="Projected calendar date (YYYY-MM-DD).")
    scheduled_dispensations: int = Field(
        ..., description="Kits required for active subject visits on this day."
    )
    projected_new_subject_dispensations: int = Field(
        ...,
        description="Kits required for newly randomized subjects entering on this day.",
    )
    total_daily_demand: int = Field(
        ..., description="Sum of scheduled and newly projected kit demands."
    )
    cumulative_demand: int = Field(
        ..., description="Cumulative demand from day 1 to day t."
    )
    projected_closing_balance: int = Field(
        ..., description="Projected inventory at close of day."
    )


class PredictiveForecastInput(BaseModel):
    """Input parameters for generating a site-specific predictive resupply forecast."""

    model_config = ConfigDict(strict=True)

    study_id: str = Field(..., description="Clinical study identifier.")
    site_id: str = Field(..., description="Clinical site identifier.")
    kit_type: str = Field(
        default="DEFAULT", description="Blinded kit type or SKU identifier."
    )
    horizon_days: int = Field(
        default=60,
        ge=7,
        le=365,
        description="Forecasting time horizon in days (default 60).",
    )
    enrollment_velocity_weekly: float = Field(
        default=0.5,
        ge=0.0,
        description="Projected or observed new subject enrollment velocity per week.",
    )
    screening_pipeline_count: int = Field(
        default=0,
        ge=0,
        description="Count of active subjects currently in screening pipeline at site.",
    )
    screening_conversion_rate: float = Field(
        default=0.75,
        ge=0.0,
        le=1.0,
        description="Historical or configured screening-to-randomization conversion probability.",
    )
    lead_time_days: int = Field(
        default=3,
        ge=1,
        description="Shipping transit lead time in days from upstream supplier depot.",
    )
    buffer_days: int = Field(
        default=5,
        ge=0,
        description="Safety stock buffer period in days to absorb shipping delays.",
    )
    safety_stock_threshold: int = Field(
        default=5,
        ge=0,
        description="Minimum threshold quantity of buffer kits below which risk is triggered.",
    )
    storage_capacity: int = Field(
        default=500,
        ge=1,
        description="Maximum physical temperature-controlled kit capacity at site.",
    )


class PredictiveForecastResult(BaseModel):
    """Comprehensive output results from the predictive demand forecasting engine."""

    model_config = ConfigDict(strict=True)

    study_id: str
    site_id: str
    kit_type: str
    forecast_generated_at: datetime
    horizon_days: int
    current_on_hand_qty: int
    current_reserved_qty: int
    current_in_transit_qty: int
    effective_available_stock: int
    active_subjects_count: int
    screening_pipeline_count: int
    enrollment_velocity_weekly: float
    total_projected_demand: int
    average_daily_burn_rate: float
    days_of_supply_remaining: float
    projected_runout_date: str | None
    stockout_risk_level: StockoutRiskLevel
    recommended_order_qty: int
    reorder_recommended: bool
    reorder_trigger_day: int | None
    timeline: list[DailyDemandProjection]


class SupplyHierarchyNode(BaseModel):
    """Node in the hierarchical supply chain tree representation."""

    model_config = ConfigDict(strict=True)

    node_id: str
    study_id: str
    node_type: SupplyNodeType
    name: str
    country: str
    address: str | None = None
    parent_node_id: str | None = None
    storage_capacity: int
    current_utilization: int
    utilization_percentage: float
    lead_time_days: int
    temperature_conditions: list[str] = Field(default_factory=list)
    is_active: bool
    total_on_hand: int = 0
    total_reserved: int = 0
    total_in_transit: int = 0
    children: list[SupplyHierarchyNode] = Field(default_factory=list)


class MultiEchelonRollup(BaseModel):
    """Aggregated global and echelon-level supply metrics."""

    model_config = ConfigDict(strict=True)

    study_id: str
    total_nodes_count: int
    manufacturers_count: int
    central_depots_count: int
    regional_depots_count: int
    clinical_sites_count: int
    total_global_on_hand_stock: int
    total_global_in_transit_stock: int
    total_global_reserved_stock: int
    total_global_storage_capacity: int
    overall_capacity_utilization_pct: float
    hierarchy_tree: list[SupplyHierarchyNode]
