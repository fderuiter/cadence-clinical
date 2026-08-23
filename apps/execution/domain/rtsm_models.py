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


class ExcursionEvaluationStatus(StrEnum):
    """Evaluation decision statuses for cold chain stability compliance."""

    COMPLIANT = "COMPLIANT"
    EXCURSION_ACCEPTABLE = "EXCURSION_ACCEPTABLE"
    EXCURSION_BREACH = "EXCURSION_BREACH"


class LoggerDeviceStatus(StrEnum):
    """Lifecycle states for digital temperature logger hardware."""

    ACTIVE = "ACTIVE"
    INGESTED = "INGESTED"
    DECOMMISSIONED = "DECOMMISSIONED"


class LoggerEntityType(StrEnum):
    """Supply chain entities to which a data logger is affixed."""

    SHIPMENT = "SHIPMENT"
    SITE_STORAGE = "SITE_STORAGE"
    DEPOT_STORAGE = "DEPOT_STORAGE"


class FileFormat(StrEnum):
    """Supported file formats for digital temperature logger ingestion."""

    CSV = "CSV"
    XML = "XML"
    JSON = "JSON"


class KitStatus(StrEnum):
    """Investigational product kit physical and dispensing disposition states."""

    AVAILABLE = "AVAILABLE"
    QUARANTINED = "QUARANTINED"
    DISPENSED = "DISPENSED"
    DESTROYED = "DESTROYED"
    DAMAGED = "DAMAGED"
    EXPIRED = "EXPIRED"


class ReLabelingTaskStatus(StrEnum):
    """Workflow states for GxP physical re-labeling action tasks."""

    PENDING_RELABEL = "PENDING_RELABEL"
    IN_PROGRESS = "IN_PROGRESS"
    COMPLETED = "COMPLETED"
    VERIFIED = "VERIFIED"


class RawTemperaturePoint(BaseModel):
    """Individual timeseries temperature observation point."""

    model_config = ConfigDict(strict=True)

    timestamp: datetime = Field(
        ..., description="Timestamp of the temperature reading."
    )
    temperature_celsius: float = Field(
        ..., description="Observed temperature in degrees Celsius."
    )


class DiscreteExcursionEvent(BaseModel):
    """Segment of contiguous thermal excursion above or below stability boundaries."""

    model_config = ConfigDict(strict=True)

    start_time: str = Field(..., description="ISO 8601 start timestamp of excursion.")
    end_time: str = Field(..., description="ISO 8601 end timestamp of excursion.")
    duration_hours: float = Field(
        ..., description="Duration of continuous excursion in decimal hours."
    )
    excursion_type: str = Field(
        ...,
        description="Type of excursion (e.g. HIGH_TEMPERATURE, LOW_TEMPERATURE, HARD_MAX_BREACH, HARD_MIN_BREACH).",
    )
    peak_or_nadir_temp_celsius: float = Field(
        ..., description="Extreme temperature reached during the excursion interval."
    )
    threshold_celsius: float = Field(
        ..., description="Stability boundary threshold in degrees Celsius."
    )


class StabilityBudgetConfig(BaseModel):
    """Specification of thermal stability limits and Arrhenius parameters for a kit type or lot."""

    model_config = ConfigDict(strict=True)

    study_id: str = Field(..., description="Clinical study identifier.")
    kit_type: str = Field(..., description="Blinded kit type or product SKU.")
    lot_number: str | None = Field(
        default=None, description="Optional specific lot number override."
    )
    storage_condition: str = Field(
        default="REFRIGERATED_2_TO_8",
        description="Standard storage regime label.",
    )
    min_temp_celsius: float = Field(
        default=2.0, description="Lower standard storage temperature bound in °C."
    )
    max_temp_celsius: float = Field(
        default=8.0, description="Upper standard storage temperature bound in °C."
    )
    max_mkt_celsius: float = Field(
        default=8.0, description="Maximum allowable Mean Kinetic Temperature in °C."
    )
    max_cumulative_hours_above_upper: float = Field(
        default=24.0,
        ge=0.0,
        description="Allowable cumulative time in hours above max_temp_celsius.",
    )
    max_cumulative_hours_below_lower: float = Field(
        default=0.0,
        ge=0.0,
        description="Allowable cumulative time in hours below min_temp_celsius.",
    )
    activation_energy_over_r: float = Field(
        default=10000.0,
        gt=0.0,
        description="Arrhenius activation energy over gas constant ratio (ΔH / R in K). Standard universal value is 10000 K.",
    )
    hard_min_temp_celsius: float | None = Field(
        default=0.0,
        description="Hard lower limit below which product is immediately compromised (e.g. freezing for biologics).",
    )
    hard_max_temp_celsius: float | None = Field(
        default=25.0,
        description="Hard upper limit above which product is immediately compromised.",
    )


class ExcursionAnalysisResult(BaseModel):
    """Complete mathematical results of a cold chain logger excursion evaluation."""

    model_config = ConfigDict(strict=True)

    study_id: str
    logger_id: str
    entity_type: LoggerEntityType
    entity_id: str
    evaluation_status: ExcursionEvaluationStatus
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
    excursions: list[DiscreteExcursionEvent] = Field(default_factory=list)
    evaluated_at: datetime
    evaluated_by: str
    summary_notes: str | None = None


# =========================================================================
# Cell & Gene Therapy (CGT) Vein-to-Vein COC/COI & Destruction Enums & Models
# Requirements: PRD-SCM-005, PRD-SCM-006, ADR-2199
# =========================================================================


class CGTStage(StrEnum):
    """Lifecycle stages across Cell & Gene Therapy Vein-to-Vein continuum."""

    APHERESIS_COLLECTION = "APHERESIS_COLLECTION"
    CRYOPRESERVATION = "CRYOPRESERVATION"
    COURIER_TRANSIT_OUTBOUND = "COURIER_TRANSIT_OUTBOUND"
    MANUFACTURING_PROCESSING = "MANUFACTURING_PROCESSING"
    COURIER_TRANSIT_RETURN = "COURIER_TRANSIT_RETURN"
    SITE_RECEIPT_AND_STORAGE = "SITE_RECEIPT_AND_STORAGE"
    BEDSIDE_VERIFICATION = "BEDSIDE_VERIFICATION"
    INFUSION_ADMINISTRATION = "INFUSION_ADMINISTRATION"
    POST_INFUSION_RECONCILIATION = "POST_INFUSION_RECONCILIATION"
    DESTRUCTION = "DESTRUCTION"


class CGTCustodyStatus(StrEnum):
    """Chain of Custody and Chain of Identity progression states."""

    COLLECTED = "COLLECTED"
    IN_TRANSIT = "IN_TRANSIT"
    IN_MANUFACTURING = "IN_MANUFACTURING"
    MANUFACTURED = "MANUFACTURED"
    RETURNED_TO_SITE = "RETURNED_TO_SITE"
    BEDSIDE_VERIFIED = "BEDSIDE_VERIFIED"
    INFUSED = "INFUSED"
    QUARANTINED = "QUARANTINED"
    RECONCILED = "RECONCILED"
    DESTROYED = "DESTROYED"


class CryogenicTempStatus(StrEnum):
    """Thermal integrity condition for cryogenic biologics transit."""

    OPTIMAL = "OPTIMAL"
    WARNING = "WARNING"
    EXCURSION = "EXCURSION"


class DestructionMethod(StrEnum):
    """Approved GxP destruction methodologies for investigational cell product and vials."""

    HIGH_TEMPERATURE_INCINERATION = "HIGH_TEMPERATURE_INCINERATION"
    AUTOCLAVE_BIOHAZARD = "AUTOCLAVE_BIOHAZARD"
    CHEMICAL_INACTIVATION = "CHEMICAL_INACTIVATION"
    HAZARDOUS_WASTE_FACILITY = "HAZARDOUS_WASTE_FACILITY"


class VialDisposition(StrEnum):
    """Final reconciliation disposition for patient kit and product vials."""

    INFUSED = "INFUSED"
    PARTIALLY_INFUSED = "PARTIALLY_INFUSED"
    EXPIRED = "EXPIRED"
    CONTAMINATED = "CONTAMINATED"
    DEFECTIVE = "DEFECTIVE"
    UNUSED_EXCESS = "UNUSED_EXCESS"
    RECONCILED_FOR_DESTRUCTION = "RECONCILED_FOR_DESTRUCTION"
    DESTROYED = "DESTROYED"


class TamperEvidentBarcode(BaseModel):
    """Cryptographic barcode payload binding subject identity to biological sample."""

    model_config = ConfigDict(strict=True)

    barcode_id: str = Field(..., description="Unique barcode identifier.")
    study_id: str = Field(..., description="Clinical study identifier.")
    site_id: str = Field(..., description="Clinical site identifier.")
    subject_id: str = Field(..., description="Subject UUID or primary subject ID.")
    din: str = Field(
        ...,
        description="Donation Identification Number conforming to ISBT 128 standard.",
    )
    batch_id: str | None = Field(
        default=None,
        description="Optional cell therapy manufacturing batch or lot identifier.",
    )
    created_at: datetime = Field(
        ..., description="Timestamp of cryptographic barcode generation."
    )
    crypto_hash: str = Field(
        ..., description="HMAC-SHA256 tamper-evident cryptographic signature."
    )
    barcode_payload: str = Field(
        ..., description="Encoded serialized payload suitable for 2D DataMatrix scan."
    )


class CryogenicMilestone(BaseModel):
    """Cryptographic and temperature milestone logged during cryogenic transit and handling."""

    model_config = ConfigDict(strict=True)

    milestone_id: str = Field(..., description="Unique milestone identifier.")
    chain_of_custody_id: str = Field(
        ..., description="Reference to the parent CGT COC record."
    )
    stage: CGTStage = Field(..., description="Vein-to-vein lifecycle stage.")
    timestamp: datetime = Field(..., description="Milestone event timestamp.")
    location_name: str = Field(..., description="Facility or transit waypoint name.")
    facility_id: str | None = Field(
        default=None, description="Optional facility identifier."
    )
    latitude: float | None = Field(default=None, description="GPS latitude coordinate.")
    longitude: float | None = Field(
        default=None, description="GPS longitude coordinate."
    )
    temperature_celsius: float = Field(
        ..., description="Observed cryogenic temperature in °C."
    )
    temperature_status: CryogenicTempStatus = Field(
        default=CryogenicTempStatus.OPTIMAL,
        description="Cryogenic temperature integrity classification.",
    )
    custodian_id: str = Field(..., description="User ID of the active custodian.")
    custodian_name: str = Field(..., description="Full name of the active custodian.")
    custodian_role: str = Field(..., description="Role of the active custodian.")
    carrier: str | None = Field(
        default=None, description="Courier or transport service."
    )
    tracking_number: str | None = Field(
        default=None, description="Courier waybill or tracking number."
    )
    seal_intact: bool = Field(
        default=True,
        description="Physical tamper seal integrity status.",
    )
    seal_id: str | None = Field(default=None, description="Tamper seal serial number.")
    dry_shipper_id: str | None = Field(
        default=None, description="Cryogenic dry shipper / dewar identifier."
    )
    notes: str | None = Field(
        default=None, description="Observations or handover notes."
    )


class DualBedsideVerification(BaseModel):
    """Two-person electronic bedside verification result unlocking drug infusion."""

    model_config = ConfigDict(strict=True)

    verification_id: str = Field(..., description="Unique verification identifier.")
    study_id: str = Field(..., description="Clinical study identifier.")
    site_id: str = Field(..., description="Clinical site identifier.")
    subject_id: str = Field(..., description="Subject UUID or primary subject ID.")
    din: str = Field(..., description="Donation Identification Number.")
    batch_id: str = Field(..., description="Manufacturing batch identifier.")
    scanned_wristband_subject_id: str = Field(
        ..., description="Subject ID decoded from physical patient wristband."
    )
    scanned_bag_barcode: str = Field(
        ..., description="Barcode decoded from cell therapy infusion bag."
    )
    wristband_matched: bool = Field(
        ..., description="Indicates subject wristband match."
    )
    bag_matched: bool = Field(
        ..., description="Indicates cell bag barcode cryptographic match."
    )
    temperature_chain_verified: bool = Field(
        ..., description="Indicates complete cold chain integrity verification."
    )
    primary_verifier_id: str = Field(..., description="User ID of primary verifier.")
    primary_verifier_name: str = Field(
        ..., description="Full name of primary verifier."
    )
    primary_verifier_role: str = Field(..., description="Role of primary verifier.")
    primary_signature_token: str = Field(
        ..., description="Cryptographic e-signature token of primary verifier."
    )
    primary_signed_at: datetime = Field(
        ..., description="Timestamp of primary verification."
    )
    secondary_verifier_id: str = Field(
        ..., description="User ID of secondary verifier (e.g. PI/Co-I)."
    )
    secondary_verifier_name: str = Field(
        ..., description="Full name of secondary verifier."
    )
    secondary_verifier_role: str = Field(..., description="Role of secondary verifier.")
    secondary_signature_token: str = Field(
        ..., description="Cryptographic e-signature token of secondary verifier."
    )
    secondary_signed_at: datetime = Field(
        ..., description="Timestamp of secondary verification."
    )
    status: str = Field(..., description="Verification status (VERIFIED or REJECTED).")
    unlock_token: str = Field(
        ..., description="Cryptographic unlock token passed to unlock infusion eCRF."
    )
    verification_notes: str | None = Field(
        default=None, description="Optional clinical verification notes."
    )


class CertificateOfDestruction(BaseModel):
    """Formal Certificate of Destruction record with dual witness electronic signatures."""

    model_config = ConfigDict(strict=True)

    certificate_id: str = Field(..., description="Unique certificate identifier.")
    certificate_number: str = Field(
        ..., description="Formal certificate sequential reference (e.g. COD-2026-0001)."
    )
    study_id: str = Field(..., description="Clinical study identifier.")
    site_id: str = Field(..., description="Clinical site identifier.")
    subject_id: str | None = Field(
        default=None, description="Optional subject identifier."
    )
    din: str | None = Field(
        default=None, description="Optional Donation Identification Number."
    )
    lot_number: str = Field(
        ..., description="Lot or batch number of destroyed product."
    )
    kit_numbers: list[str] = Field(
        ..., description="List of kit serial numbers included in destruction."
    )
    vial_identifiers: list[str] = Field(
        ..., description="List of vial identifiers included in destruction."
    )
    quantity_destroyed: int = Field(
        ..., description="Total quantity of units destroyed."
    )
    unit_of_measure: str = Field(
        default="VIALS", description="Unit of measure (VIALS, KITS, BAGS)."
    )
    residual_volume_ml: float | None = Field(
        default=None, description="Residual volume destroyed in mL."
    )
    destruction_reason: str = Field(..., description="Clinical or GxP reason.")
    destruction_method: DestructionMethod = Field(
        ..., description="Destruction methodology."
    )
    destruction_facility: str = Field(
        ..., description="Authorized destruction facility or location."
    )
    destruction_timestamp: datetime = Field(
        ..., description="Date and time destruction occurred."
    )
    witness_1_id: str = Field(..., description="User ID of first witness.")
    witness_1_name: str = Field(..., description="Name of first witness.")
    witness_1_role: str = Field(..., description="Role of first witness.")
    witness_1_signature_token: str = Field(
        ..., description="E-signature token of first witness."
    )
    witness_1_signed_at: datetime = Field(
        ..., description="Timestamp of first witness signature."
    )
    witness_2_id: str = Field(..., description="User ID of second witness.")
    witness_2_name: str = Field(..., description="Name of second witness.")
    witness_2_role: str = Field(..., description="Role of second witness.")
    witness_2_signature_token: str = Field(
        ..., description="E-signature token of second witness."
    )
    witness_2_signed_at: datetime = Field(
        ..., description="Timestamp of second witness signature."
    )
    cryptographic_seal: str = Field(
        ..., description="SHA-256 seal across certificate contents and signatures."
    )
    status: str = Field(default="COMPLETED", description="Certificate status.")
    reconciliation_notes: str | None = Field(
        default=None, description="Reconciliation and disposal notes."
    )


class VialReturnReconciliation(BaseModel):
    """Accountability and adherence tracking for returned product vials."""

    model_config = ConfigDict(strict=True)

    reconciliation_id: str = Field(..., description="Unique record identifier.")
    study_id: str = Field(..., description="Clinical study identifier.")
    site_id: str = Field(..., description="Clinical site identifier.")
    subject_id: str = Field(..., description="Subject identifier.")
    kit_number: str = Field(..., description="Kit identifier.")
    vial_identifier: str = Field(..., description="Vial serial number.")
    lot_number: str = Field(..., description="Lot number.")
    din: str | None = Field(default=None, description="Donation Identification Number.")
    expected_quantity: int = Field(
        ..., description="Expected units dispensed to subject."
    )
    returned_quantity: int = Field(..., description="Units returned to site.")
    intact_vials: int = Field(default=0, description="Intact unused vials.")
    used_vials: int = Field(default=0, description="Used or empty vials.")
    residual_volume_ml: float = Field(
        default=0.0, description="Measured residual volume in mL."
    )
    adherence_percentage: float = Field(
        ..., description="Calculated subject compliance / adherence percentage."
    )
    disposition: VialDisposition = Field(
        default=VialDisposition.RECONCILED_FOR_DESTRUCTION,
        description="Disposition state.",
    )
    logged_by: str = Field(..., description="User ID logging reconciliation.")
    logged_at: datetime = Field(..., description="Timestamp logged.")
    certificate_id: str | None = Field(
        default=None, description="Bound Certificate of Destruction ID if destroyed."
    )
