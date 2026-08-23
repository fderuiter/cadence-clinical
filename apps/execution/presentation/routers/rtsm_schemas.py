"""Pydantic schemas for RTSM multi-echelon hierarchy, forecasting, and transfer orders.

Requirements: PRD-SCM-001, GMP Annex 13
"""

from datetime import datetime

from pydantic import BaseModel, ConfigDict, Field

from apps.execution.domain.rtsm_models import (
    DailyDemandProjection,
)


class SupplyNodeCreateRequest(BaseModel):
    """Payload to register or update a supply node in the clinical supply hierarchy."""

    study_id: str
    node_id: str
    node_type: str = "CLINICAL_SITE"
    name: str
    country: str
    address: str | None = None
    parent_node_id: str | None = None
    storage_capacity: int = Field(default=1000, ge=1)
    lead_time_days: int = Field(default=3, ge=1)
    temperature_conditions: list[str] = Field(
        default_factory=lambda: ["CONTROLLED_ROOM_TEMP"]
    )


class SupplyNodeResponse(BaseModel):
    """Response schema for a supply node."""

    model_config = ConfigDict(strict=True)

    id: str
    study_id: str
    node_id: str
    node_type: str
    name: str
    country: str
    address: str | None = None
    parent_node_id: str | None = None
    storage_capacity: int
    current_utilization: int
    lead_time_days: int
    temperature_conditions: list[str] = Field(default_factory=list)
    is_active: bool


class SupplyNodeInventoryAdjustRequest(BaseModel):
    """Payload to initialize or adjust inventory levels at a supply node."""

    model_config = ConfigDict(strict=True)

    study_id: str
    node_id: str
    kit_type: str
    lot_number: str | None = None
    on_hand_qty: int = Field(ge=0)
    safety_stock_threshold: int = Field(default=10, ge=0)
    reorder_point: int = Field(default=20, ge=0)
    max_capacity: int = Field(default=500, ge=1)


class SupplyNodeInventoryResponse(BaseModel):
    """Response schema for node inventory."""

    model_config = ConfigDict(strict=True)

    id: str
    study_id: str
    node_id: str
    kit_type: str
    lot_number: str | None = None
    on_hand_qty: int
    reserved_qty: int
    in_transit_qty: int
    safety_stock_threshold: int
    reorder_point: int
    max_capacity: int
    resupply_signal: bool


class PredictiveForecastRequest(BaseModel):
    """Request payload to execute a predictive resupply forecast."""

    model_config = ConfigDict(strict=True)

    study_id: str
    site_id: str
    kit_type: str = "DEFAULT"
    horizon_days: int = Field(default=60, ge=7, le=365)
    enrollment_velocity_weekly: float = Field(default=0.5, ge=0.0)
    screening_pipeline_count: int = Field(default=0, ge=0)
    screening_conversion_rate: float = Field(default=0.75, ge=0.0, le=1.0)
    lead_time_days: int = Field(default=3, ge=1)
    buffer_days: int = Field(default=5, ge=0)
    safety_stock_threshold: int = Field(default=5, ge=0)
    storage_capacity: int = Field(default=500, ge=1)


class PredictiveForecastResponse(BaseModel):
    """Detailed response schema for predictive demand forecast."""

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
    stockout_risk_level: str
    recommended_order_qty: int
    reorder_recommended: bool
    reorder_trigger_day: int | None
    timeline: list[DailyDemandProjection]


class TransferOrderCreateRequest(BaseModel):
    """Payload to create a multi-echelon stock transfer order."""

    study_id: str
    source_node_id: str
    destination_node_id: str
    kit_type: str
    quantity: int = Field(ge=1)
    priority: str = "ROUTINE"
    temperature_condition: str = "CONTROLLED_ROOM_TEMP"
    carrier: str | None = None
    transfer_order_number: str | None = None


class TransferOrderResponse(BaseModel):
    """Response schema for transfer orders."""

    model_config = ConfigDict(strict=True)

    id: str
    study_id: str
    transfer_order_number: str
    source_node_id: str
    destination_node_id: str
    kit_type: str
    quantity: int
    priority: str
    status: str
    temperature_condition: str
    carrier: str | None = None
    tracking_number: str | None = None
    estimated_delivery_date: datetime | None = None
    requested_at: datetime
    requested_by: str | None = None
    approved_at: datetime | None = None
    approved_by: str | None = None
    shipped_at: datetime | None = None
    shipped_by: str | None = None
    delivered_at: datetime | None = None
    delivered_by: str | None = None
    rejection_reason: str | None = None


class TransferOrderApprovalRequest(BaseModel):
    """Approval or rejection justification payload."""

    model_config = ConfigDict(strict=True)

    change_justification: str


class PackingManifestCreateRequest(BaseModel):
    """Payload to generate a packing manifest for an approved transfer order."""

    model_config = ConfigDict(strict=True)

    manifest_number: str
    serialized_kit_numbers: list[str] = Field(min_length=1)
    lot_number: str
    expiration_date: datetime | None = None
    container_serial_number: str | None = None
    data_logger_id: str | None = None
    tamper_seal_id: str | None = None


class PackingManifestResponse(BaseModel):
    """Response schema for packing manifests."""

    model_config = ConfigDict(strict=True)

    id: str
    study_id: str
    transfer_order_id: str
    manifest_number: str
    serialized_kit_numbers: list[str]
    lot_number: str
    expiration_date: datetime | None = None
    container_serial_number: str | None = None
    data_logger_id: str | None = None
    tamper_seal_id: str | None = None
    packed_by: str
    packed_at: datetime
    verified_by: str | None = None
    verified_at: datetime | None = None


class TransferOrderShipRequest(BaseModel):
    """Payload to mark a transfer order as shipped."""

    model_config = ConfigDict(strict=True)

    tracking_number: str
    carrier: str | None = None
    estimated_delivery_date: datetime | None = None
    change_justification: str


class TransferOrderReceiveRequest(BaseModel):
    """Payload to mark a transfer order as delivered/received."""

    model_config = ConfigDict(strict=True)

    change_justification: str
