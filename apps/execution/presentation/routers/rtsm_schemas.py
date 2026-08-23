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


# =========================================================================
# Cold Chain, Stability Budget & MKT Excursion Schemas
# =========================================================================


class StabilityBudgetCreateRequest(BaseModel):
    """Payload to create or configure a drug stability budget."""

    model_config = ConfigDict(strict=True)

    study_id: str
    kit_type: str
    lot_number: str | None = None
    storage_condition: str = "REFRIGERATED_2_TO_8"
    min_temp_celsius: float = 2.0
    max_temp_celsius: float = 8.0
    max_mkt_celsius: float = 8.0
    max_cumulative_hours_above_upper: float = Field(default=24.0, ge=0.0)
    max_cumulative_hours_below_lower: float = Field(default=0.0, ge=0.0)
    activation_energy_over_r: float = Field(default=10000.0, gt=0.0)
    hard_min_temp_celsius: float | None = 0.0
    hard_max_temp_celsius: float | None = 25.0


class StabilityBudgetResponse(BaseModel):
    """Response schema for a drug stability budget."""

    model_config = ConfigDict(strict=True)

    id: str
    study_id: str
    kit_type: str
    lot_number: str | None = None
    storage_condition: str
    min_temp_celsius: float
    max_temp_celsius: float
    max_mkt_celsius: float
    max_cumulative_hours_above_upper: float
    max_cumulative_hours_below_lower: float
    activation_energy_over_r: float
    hard_min_temp_celsius: float | None = None
    hard_max_temp_celsius: float | None = None


class TemperatureLoggerRegisterRequest(BaseModel):
    """Payload to register a hardware temperature logger device."""

    model_config = ConfigDict(extra="forbid")

    study_id: str
    logger_id: str
    device_model: str = "Sensitech TempTale Ultra"
    serial_number: str | None = None
    logger_type: str = "SHIPMENT"
    assigned_transfer_order_id: str | None = None
    assigned_node_id: str | None = None
    assigned_container_id: str | None = None
    calibration_expiry_date: datetime | None = None


class TemperatureLoggerResponse(BaseModel):
    """Response schema for a temperature logger device."""

    model_config = ConfigDict(strict=True)

    id: str
    study_id: str
    logger_id: str
    device_model: str
    serial_number: str | None = None
    logger_type: str
    assigned_transfer_order_id: str | None = None
    assigned_node_id: str | None = None
    assigned_container_id: str | None = None
    calibration_expiry_date: datetime | None = None
    status: str


class LoggerIngestionRequest(BaseModel):
    """Payload to upload and ingest a raw temperature logger file."""

    model_config = ConfigDict(strict=True)

    study_id: str
    logger_id: str
    file_format: str = "CSV"  # CSV, XML, JSON
    raw_payload: str
    filename: str
    entity_type: str = "SHIPMENT"  # SHIPMENT, SITE_STORAGE, DEPOT_STORAGE
    entity_id: str
    stability_budget_override: StabilityBudgetCreateRequest | None = None


class DiscreteExcursionResponse(BaseModel):
    """Segment representation of a continuous excursion window."""

    model_config = ConfigDict(strict=True)

    start_time: str
    end_time: str
    duration_hours: float
    excursion_type: str
    peak_or_nadir_temp_celsius: float
    threshold_celsius: float


class ExcursionEvaluationResponse(BaseModel):
    """Detailed response schema for an excursion evaluation and MKT analysis."""

    model_config = ConfigDict(strict=True)

    id: str | None = None
    study_id: str
    logger_id: str
    entity_type: str
    entity_id: str
    evaluation_status: str
    mean_kinetic_temperature_celsius: float
    arithmetic_mean_celsius: float
    min_temperature_celsius: float
    max_temperature_celsius: float
    cumulative_hours_above_upper: float
    cumulative_hours_below_lower: float
    total_duration_hours: float
    readings_count: int
    hard_boundary_breached: bool
    mkt_breached: bool
    cumulative_time_breached: bool
    quarantine_triggered: bool
    quarantined_kit_count: int
    excursions: list[DiscreteExcursionResponse] = Field(default_factory=list)
    evaluated_at: datetime
    evaluated_by: str
    summary_notes: str | None = None


class MktCalculationDirectRequest(BaseModel):
    """Direct standalone MKT Arrhenius mathematical calculation payload."""

    model_config = ConfigDict(strict=True)

    temperatures_celsius: list[float] = Field(..., min_length=1)
    activation_energy_over_r: float = Field(default=10000.0, gt=0.0)


class MktCalculationDirectResponse(BaseModel):
    """Direct standalone MKT calculation result."""

    model_config = ConfigDict(strict=True)

    mean_kinetic_temperature_celsius: float
    arithmetic_mean_celsius: float
    min_temperature_celsius: float
    max_temperature_celsius: float
    readings_count: int
    formula_reference: str = "USP <1079> / PDA TR 39 Arrhenius Kinetic Model"


# =========================================================================
# IP Kit Catalog & Quarantine Disposition Schemas
# =========================================================================


class IPKitCreateRequest(BaseModel):
    """Payload to catalog an investigational product kit."""

    model_config = ConfigDict(extra="forbid")

    study_id: str
    kit_number: str
    kit_type: str
    description: str | None = None
    lot_number: str | None = None
    expiration_date: datetime | None = None
    node_id: str | None = None


class IPKitResponse(BaseModel):
    """Response schema for an investigational product kit."""

    model_config = ConfigDict(strict=True)

    id: str
    study_id: str
    kit_number: str
    kit_type: str
    description: str | None = None
    status: str
    lot_number: str | None = None
    expiration_date: datetime | None = None
    node_id: str | None = None
    quarantine_reason: str | None = None
    quarantined_at: datetime | None = None
    quarantined_by: str | None = None


class KitQuarantineRequest(BaseModel):
    """Payload to manually quarantine kits with Part 11 justification."""

    model_config = ConfigDict(strict=True)

    study_id: str
    kit_numbers: list[str]
    quarantine_reason: str


class KitReleaseQuarantineRequest(BaseModel):
    """Payload for QA disposition release of quarantined kits."""

    model_config = ConfigDict(strict=True)

    study_id: str
    kit_numbers: list[str]
    qa_release_justification: str


# =========================================================================
# Lot Expiry Extension & Re-Labeling Schemas (PRD-SCM-003)
# =========================================================================


class LotExpiryExtensionRequest(BaseModel):
    """Payload to execute automated lot expiration date extension."""

    model_config = ConfigDict(extra="forbid")

    study_id: str
    lot_number: str
    kit_type: str
    extended_expiry_date: datetime
    extension_reason: str
    stability_protocol_reference: str


class LotExpiryExtensionResponse(BaseModel):
    """Response schema for a lot expiration extension record."""

    model_config = ConfigDict(strict=True)

    id: str
    study_id: str
    lot_number: str
    kit_type: str
    initial_expiry_date: datetime
    extended_expiry_date: datetime
    extension_reason: str
    stability_protocol_reference: str
    authorized_by: str
    approval_status: str
    affected_kits_count: int
    relabeling_tasks_generated: int
    created_at: datetime


class ReLabelingTaskResponse(BaseModel):
    """Response schema for a physical kit re-labeling action task."""

    model_config = ConfigDict(strict=True)

    id: str
    study_id: str
    task_number: str
    lot_number: str
    node_id: str
    affected_kit_count: int
    old_expiry_date: datetime
    new_expiry_date: datetime
    status: str
    assigned_to: str | None = None
    completed_at: datetime | None = None
    completed_by: str | None = None
    verified_at: datetime | None = None
    verified_by: str | None = None
    label_barcode_hash: str | None = None
    relabeling_notes: str | None = None


class ReLabelingTaskCompleteRequest(BaseModel):
    """Payload to record completion of a re-labeling task."""

    model_config = ConfigDict(strict=True)

    notes: str | None = None


class ReLabelingTaskVerifyRequest(BaseModel):
    """Payload for dual QA verification of a completed re-labeling task."""

    model_config = ConfigDict(strict=True)

    label_barcode_hash: str | None = None
    notes: str | None = None
