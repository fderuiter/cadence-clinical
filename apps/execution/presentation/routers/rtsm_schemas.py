"""Pydantic schemas for RTSM multi-echelon hierarchy, forecasting, and transfer orders.

Requirements: PRD-SCM-001, GMP Annex 13
"""

from datetime import datetime

from pydantic import BaseModel, ConfigDict, Field

from apps.execution.domain.adaptive_models import (
    AdaptiveTrialConfigDTO as AdaptiveTrialConfigDTO,
)
from apps.execution.domain.adaptive_models import (
    BoinBoundariesCalculationRequest as BoinBoundariesCalculationRequest,
)
from apps.execution.domain.adaptive_models import (
    BoinBoundariesCalculationResponse as BoinBoundariesCalculationResponse,
)
from apps.execution.domain.adaptive_models import (
    BoinConfig as BoinConfig,
)
from apps.execution.domain.adaptive_models import (
    CohortEvaluationInput as CohortEvaluationInput,
)
from apps.execution.domain.adaptive_models import (
    CohortEvaluationResult as CohortEvaluationResult,
)
from apps.execution.domain.adaptive_models import (
    CohortRecommendationAction as CohortRecommendationAction,
)
from apps.execution.domain.adaptive_models import (
    CohortStatus as CohortStatus,
)
from apps.execution.domain.adaptive_models import (
    CohortTransitionLogResponse as CohortTransitionLogResponse,
)
from apps.execution.domain.adaptive_models import (
    CohortTransitionRequest as CohortTransitionRequest,
)
from apps.execution.domain.adaptive_models import (
    CrmConfig as CrmConfig,
)
from apps.execution.domain.adaptive_models import (
    CrmPosteriorCalculationRequest as CrmPosteriorCalculationRequest,
)
from apps.execution.domain.adaptive_models import (
    CrmPosteriorCalculationResponse as CrmPosteriorCalculationResponse,
)
from apps.execution.domain.adaptive_models import (
    DoseEscalationDesignType as DoseEscalationDesignType,
)
from apps.execution.domain.adaptive_models import (
    DoseLevelDef as DoseLevelDef,
)
from apps.execution.domain.adaptive_models import (
    DoseLevelEvaluationSummary as DoseLevelEvaluationSummary,
)
from apps.execution.domain.adaptive_models import (
    RARArmEvaluationDTO as RARArmEvaluationDTO,
)
from apps.execution.domain.adaptive_models import (
    RARArmStatus as RARArmStatus,
)
from apps.execution.domain.adaptive_models import (
    RarConfig as RarConfig,
)
from apps.execution.domain.adaptive_models import (
    RAREvaluationResult as RAREvaluationResult,
)
from apps.execution.domain.adaptive_models import (
    RARExpandOrTerminateArmRequest as RARExpandOrTerminateArmRequest,
)
from apps.execution.domain.adaptive_models import (
    ThreePlusThreeConfig as ThreePlusThreeConfig,
)
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


# =========================================================================
# Cell & Gene Therapy (CGT) Vein-to-Vein COC/COI & Destruction Schemas
# Requirements: PRD-SCM-005, PRD-SCM-006, ADR-2199
# =========================================================================


class CGTApheresisCreateRequest(BaseModel):
    """Payload to register initial apheresis collection and start Vein-to-Vein tracking."""

    model_config = ConfigDict(strict=True)

    study_id: str
    site_id: str
    subject_id: str
    collector_id: str
    collector_name: str
    collector_role: str = "Apheresis Nurse"
    initial_temp_celsius: float = -196.0
    din: str | None = None
    dry_shipper_id: str | None = None
    seal_id: str | None = None
    notes: str | None = None


class CGTChainOfCustodyResponse(BaseModel):
    """Response schema for Vein-to-Vein Chain of Custody & Identity record."""

    model_config = ConfigDict(strict=True)

    id: str
    study_id: str
    subject_id: str
    din: str
    batch_id: str | None = None
    current_stage: str
    status: str
    tamper_evident_barcode: str | None = None
    barcode_signature: str | None = None
    collection_site_id: str | None = None
    collection_timestamp: datetime | None = None
    manufacturing_facility_id: str | None = None
    cryo_temp_excursion_count: int = 0
    lowest_temp_celsius: float | None = None
    highest_temp_celsius: float | None = None
    infusion_unlocked: bool = False
    infused_at: datetime | None = None
    reconciled_at: datetime | None = None


class CGTMilestoneCreateRequest(BaseModel):
    """Payload to record a cryogenic transit milestone with GPS and temperature."""

    model_config = ConfigDict(strict=True)

    coc_id: str
    stage: str
    location_name: str
    temperature_celsius: float
    custodian_id: str
    custodian_name: str
    custodian_role: str
    facility_id: str | None = None
    latitude: float | None = None
    longitude: float | None = None
    carrier: str | None = None
    tracking_number: str | None = None
    seal_intact: bool = True
    seal_id: str | None = None
    dry_shipper_id: str | None = None
    signature_token: str | None = None
    notes: str | None = None


class CGTMilestoneResponse(BaseModel):
    """Response schema for a cryogenic transit milestone."""

    model_config = ConfigDict(strict=True)

    id: str
    study_id: str
    chain_of_custody_id: str
    stage: str
    milestone_type: str
    timestamp: datetime
    location_name: str
    facility_id: str | None = None
    latitude: float | None = None
    longitude: float | None = None
    temperature_celsius: float
    temperature_status: str
    custodian_id: str
    custodian_name: str
    custodian_role: str
    carrier: str | None = None
    tracking_number: str | None = None
    seal_intact: bool
    seal_id: str | None = None
    dry_shipper_id: str | None = None
    electronic_signature: dict | None = None
    notes: str | None = None


class CGTManufacturingBindRequest(BaseModel):
    """Payload to bind a cell engineering manufacturing batch record to original subject identity."""

    model_config = ConfigDict(strict=True)

    coc_id: str
    batch_id: str
    manufacturing_facility_id: str
    operator_id: str
    operator_name: str
    operator_role: str = "Cell Manufacturing Specialist"
    temperature_celsius: float = -196.0
    notes: str | None = None


class CGTBedsideVerificationRequest(BaseModel):
    """Payload for mandatory bedside two-person electronic verification gating infusion eCRF."""

    model_config = ConfigDict(strict=True)

    coc_id: str
    scanned_wristband_subject_id: str
    scanned_bag_barcode: str
    primary_verifier_id: str
    primary_verifier_name: str
    primary_verifier_role: str
    primary_signature_token: str
    secondary_verifier_id: str
    secondary_verifier_name: str
    secondary_verifier_role: str
    secondary_signature_token: str
    notes: str | None = None


class CGTBedsideVerificationResponse(BaseModel):
    """Response schema for bedside two-person verification."""

    model_config = ConfigDict(strict=True)

    id: str
    study_id: str
    site_id: str
    chain_of_custody_id: str
    subject_id: str
    din: str
    batch_id: str
    wristband_matched: bool
    bag_matched: bool
    temperature_chain_verified: bool
    primary_verifier_id: str
    primary_verifier_name: str
    primary_verifier_role: str
    primary_signed_at: datetime
    secondary_verifier_id: str
    secondary_verifier_name: str
    secondary_verifier_role: str
    secondary_signed_at: datetime
    status: str
    unlock_token: str
    verification_notes: str | None = None


class CGTInfusionExecuteRequest(BaseModel):
    """Payload to execute infusion administration with unlock token verification."""

    model_config = ConfigDict(strict=True)

    coc_id: str
    unlock_token: str
    administrator_id: str
    administrator_name: str
    administrator_role: str = "Infusion Nurse"
    volume_infused_ml: float = 50.0
    vital_signs_pre_temp_celsius: float = 36.8
    vital_signs_post_temp_celsius: float = 37.1
    notes: str | None = None


class CGTVialReturnReconciliationRequest(BaseModel):
    """Payload to log returned kit/vial accountability and compute adherence percentage."""

    model_config = ConfigDict(strict=True)

    study_id: str
    site_id: str
    subject_id: str
    kit_number: str
    vial_identifier: str
    lot_number: str
    expected_quantity: int = 1
    returned_quantity: int = 1
    intact_vials: int = 0
    used_vials: int = 1
    residual_volume_ml: float = 0.0
    din: str | None = None
    logged_by: str
    disposition: str = "RECONCILED_FOR_DESTRUCTION"


class CGTVialReturnReconciliationResponse(BaseModel):
    """Response schema for vial return reconciliation."""

    model_config = ConfigDict(strict=True)

    id: str
    study_id: str
    site_id: str
    subject_id: str
    kit_number: str
    vial_identifier: str
    lot_number: str
    din: str | None = None
    expected_quantity: int
    returned_quantity: int
    intact_vials: int
    used_vials: int
    residual_volume_ml: float
    adherence_percentage: float
    disposition: str
    logged_by: str
    logged_at: datetime
    certificate_id: str | None = None


class CGTDestructionCertificateCreateRequest(BaseModel):
    """Payload to generate formal Certificate of Destruction with dual witness e-signatures."""

    model_config = ConfigDict(strict=True)

    study_id: str
    site_id: str
    lot_number: str
    kit_numbers: list[str] = Field(min_length=1)
    vial_identifiers: list[str] = Field(min_length=1)
    destruction_reason: str
    destruction_method: str = "HIGH_TEMPERATURE_INCINERATION"
    destruction_facility: str
    witness_1_id: str
    witness_1_name: str
    witness_1_role: str
    witness_1_signature_token: str
    witness_2_id: str
    witness_2_name: str
    witness_2_role: str
    witness_2_signature_token: str
    quantity_destroyed: int = 1
    residual_volume_ml: float | None = None
    subject_id: str | None = None
    din: str | None = None
    coc_id: str | None = None
    notes: str | None = None


class CGTDestructionCertificateResponse(BaseModel):
    """Response schema for formal Certificate of Destruction."""

    model_config = ConfigDict(strict=True)

    id: str
    study_id: str
    site_id: str
    certificate_number: str
    chain_of_custody_id: str | None = None
    subject_id: str | None = None
    din: str | None = None
    lot_number: str
    kit_numbers: list[str]
    vial_identifiers: list[str]
    quantity_destroyed: int
    unit_of_measure: str
    residual_volume_ml: float | None = None
    destruction_reason: str
    destruction_method: str
    destruction_facility: str
    destruction_timestamp: datetime
    witness_1_id: str
    witness_1_name: str
    witness_1_role: str
    witness_1_signed_at: datetime
    witness_2_id: str
    witness_2_name: str
    witness_2_role: str
    witness_2_signed_at: datetime
    cryptographic_seal: str
    status: str
    reconciliation_notes: str | None = None


class CGTBarcodeVerifyRequest(BaseModel):
    """Payload to verify cryptographic barcode authenticity."""

    model_config = ConfigDict(strict=True)

    barcode_payload: str
    expected_subject_id: str | None = None
    expected_din: str | None = None
    expected_batch_id: str | None = None


class CGTBarcodeVerifyResponse(BaseModel):
    """Response schema for barcode verification."""

    model_config = ConfigDict(strict=True)

    is_valid: bool
    failure_reason: str | None = None
    barcode_id: str | None = None
    study_id: str | None = None
    site_id: str | None = None
    subject_id: str | None = None
    din: str | None = None
    batch_id: str | None = None
    crypto_hash: str | None = None


class CGTFullTraceabilityResponse(BaseModel):
    """Comprehensive response schema for end-to-end Vein-to-Vein tracking."""

    model_config = ConfigDict(strict=True)

    chain_of_custody: CGTChainOfCustodyResponse
    milestones: list[CGTMilestoneResponse]
    bedside_verification: CGTBedsideVerificationResponse | None = None
    destruction_certificate: CGTDestructionCertificateResponse | None = None
