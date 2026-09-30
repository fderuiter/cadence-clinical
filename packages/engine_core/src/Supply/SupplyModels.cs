using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Cadence.Core.Domain.Supply;

public enum SupplyNodeType
{
    PRIMARY_MANUFACTURER,
    CENTRAL_DEPOT,
    REGIONAL_DEPOT,
    CLINICAL_SITE
}

public enum StockoutRiskLevel
{
    LOW,
    MEDIUM,
    HIGH,
    CRITICAL
}

public record MultiEchelonNode(
    [property: JsonPropertyName("node_id")] string NodeId,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("node_type")] SupplyNodeType NodeType,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("country")] string Country = "US",
    [property: JsonPropertyName("parent_node_id")] string? ParentNodeId = null,
    [property: JsonPropertyName("storage_capacity")] int StorageCapacity = 1000,
    [property: JsonPropertyName("on_hand_qty")] int OnHandQty = 0,
    [property: JsonPropertyName("reserved_qty")] int ReservedQty = 0,
    [property: JsonPropertyName("in_transit_qty")] int InTransitQty = 0
)
{
    [JsonPropertyName("current_utilization")]
    public int CurrentUtilization => OnHandQty + ReservedQty;

    [JsonPropertyName("utilization_percentage")]
    public double UtilizationPercentage => StorageCapacity <= 0 ? 0.0 : Math.Round(((double)CurrentUtilization / StorageCapacity) * 100.0, 2);
}

public record MultiEchelonRollup(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("total_nodes")] int TotalNodes,
    [property: JsonPropertyName("total_on_hand")] int TotalOnHand,
    [property: JsonPropertyName("total_reserved")] int TotalReserved,
    [property: JsonPropertyName("total_in_transit")] int TotalInTransit,
    [property: JsonPropertyName("tier_counts")] Dictionary<string, int>? TierCounts = null,
    [property: JsonPropertyName("tier_on_hand")] Dictionary<string, int>? TierOnHand = null,
    [property: JsonPropertyName("nodes")] List<MultiEchelonNode>? Nodes = null
);

public record ScheduledVisitDispensation(
    [property: JsonPropertyName("visit_day_offset")] int VisitDayOffset,
    [property: JsonPropertyName("dispensation_qty")] int DispensationQty = 1
);

public record LotExpiryInfo(
    [property: JsonPropertyName("lot_number")] string LotNumber,
    [property: JsonPropertyName("expiry_day_offset")] int ExpiryDayOffset,
    [property: JsonPropertyName("quantity")] int Quantity
);

public record DemandForecastParameters(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("active_subjects")] int ActiveSubjects = 0,
    [property: JsonPropertyName("monthly_recruitment_rate")] double MonthlyRecruitmentRate = 2.0,
    [property: JsonPropertyName("simulation_horizon_days")] int SimulationHorizonDays = 90,
    [property: JsonPropertyName("lead_time_days")] int LeadTimeDays = 14,
    [property: JsonPropertyName("safety_buffer_days")] int SafetyBufferDays = 7,
    [property: JsonPropertyName("resupply_lot_size")] int ResupplyLotSize = 10,
    [property: JsonPropertyName("current_on_hand")] int CurrentOnHand = 0,
    [property: JsonPropertyName("current_in_transit")] int CurrentInTransit = 0,
    [property: JsonPropertyName("in_transit_arrival_day")] int InTransitArrivalDay = 3,
    [property: JsonPropertyName("dispensation_schedule")] List<ScheduledVisitDispensation>? DispensationSchedule = null,
    [property: JsonPropertyName("active_lots")] List<LotExpiryInfo>? ActiveLots = null
);

public record DailyDemandProjection(
    [property: JsonPropertyName("day_index")] int DayIndex,
    [property: JsonPropertyName("projected_active_subjects")] int ProjectedActiveSubjects,
    [property: JsonPropertyName("new_enrollments")] int NewEnrollments,
    [property: JsonPropertyName("expected_demand_kits")] int ExpectedDemandKits,
    [property: JsonPropertyName("projected_on_hand")] int ProjectedOnHand,
    [property: JsonPropertyName("expired_kits_deducted")] int ExpiredKitsDeducted = 0,
    [property: JsonPropertyName("in_transit_received")] int InTransitReceived = 0,
    [property: JsonPropertyName("is_reorder_triggered")] bool IsReorderTriggered = false,
    [property: JsonPropertyName("stockout_risk")] StockoutRiskLevel StockoutRisk = StockoutRiskLevel.LOW
);

public record PredictiveForecastResult(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("simulation_horizon_days")] int SimulationHorizonDays,
    [property: JsonPropertyName("starting_on_hand")] int StartingOnHand,
    [property: JsonPropertyName("total_projected_demand")] int TotalProjectedDemand,
    [property: JsonPropertyName("days_until_stockout")] int? DaysUntilStockout = null,
    [property: JsonPropertyName("recommended_reorder_day_offset")] int? RecommendedReorderDayOffset = null,
    [property: JsonPropertyName("recommended_reorder_qty")] int RecommendedReorderQty = 0,
    [property: JsonPropertyName("stockout_risk_level")] StockoutRiskLevel StockoutRiskLevel = StockoutRiskLevel.LOW,
    [property: JsonPropertyName("lot_expiry_warnings")] List<string>? LotExpiryWarnings = null,
    [property: JsonPropertyName("daily_projections")] List<DailyDemandProjection>? DailyProjections = null
);

public record TemperatureReading(
    [property: JsonPropertyName("temperature_celsius")] double TemperatureCelsius,
    [property: JsonPropertyName("timestamp")] string? Timestamp = null
);

[SuppressMessage("Naming", "S101:Types should be named in PascalCase",
    Justification = "DTP (direct-to-patient) is the supply-chain term used in the protocol and IRT specifications.")]
public record DTPDispatchAuthorization(
    [property: JsonPropertyName("dispatch_id")] string DispatchId,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("kit_number")] string KitNumber,
    [property: JsonPropertyName("lot_number")] string LotNumber,
    [property: JsonPropertyName("is_eligible_for_dispatch")] bool IsEligibleForDispatch,
    [property: JsonPropertyName("rejection_reasons")] List<string> RejectionReasons,
    [property: JsonPropertyName("calculated_mkt_celsius")] double? CalculatedMktCelsius = null,
    [property: JsonPropertyName("tamper_seal_id")] string? TamperSealId = null,
    [property: JsonPropertyName("authorized_timestamp")] string AuthorizedTimestamp = ""
);

public record EmergencyUnblindingRequest(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("requester_user_id")] string RequesterUserId,
    [property: JsonPropertyName("requester_role")] string RequesterRole,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange,
    [property: JsonPropertyName("subject_actual_arm")] string SubjectActualArm
);

public record EmergencyUnblindingResult(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("unblinded_arm")] string UnblindedArm,
    [property: JsonPropertyName("authorized_by_user_id")] string AuthorizedByUserId,
    [property: JsonPropertyName("requester_role")] string RequesterRole,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange,
    [property: JsonPropertyName("digital_seal")] string DigitalSeal,
    [property: JsonPropertyName("is_authorized")] bool IsAuthorized,
    [property: JsonPropertyName("timestamp")] string Timestamp = "",
    [property: JsonPropertyName("safety_notification_required")] bool SafetyNotificationRequired = true,
    [property: JsonPropertyName("audit_event_type")] string AuditEventType = "EMERGENCY_SUBJECT_UNBLINDING"
);
