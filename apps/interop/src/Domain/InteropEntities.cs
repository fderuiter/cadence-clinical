using System;

namespace Cadence.Apps.Interop.Domain;

public class EproSubmission
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SubjectId { get; set; } = string.Empty;
    public string DiaryId { get; set; } = string.Empty;
    public DateTime DeviceTimestamp { get; set; } = DateTime.UtcNow;
    public DateTime ServerTimestamp { get; set; } = DateTime.UtcNow;
    public string AnswersJson { get; set; } = "{}";
    public string OfflineSyncMarkersJson { get; set; } = "{}";
    public string SyncStatus { get; set; } = "RESOLVED";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "system";
    public string ReasonForChange { get; set; } = "ePRO mobile submission";
    public int VersionIndex { get; set; } = 1;
}

public class InteropAuditLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string UserId { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string? ChangeReason { get; set; }
}

public class Instrument
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ItemsJson { get; set; } = "{}";
    public string ResponseTypesJson { get; set; } = "{}";
    public string ScoringMetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class SubjectAssignment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SubjectId { get; set; } = string.Empty;
    public string InstrumentId { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? RecurrencePattern { get; set; }
    public DateTime? DueAt { get; set; }
    public DateTime? LastMissedAlertAt { get; set; }
    public DateTime? LastMissedAlertNotifiedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class SubjectNotification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SubjectId { get; set; } = string.Empty;
    public string? AssignmentId { get; set; }
    public DateTime DueAt { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string DeliveryStatus { get; set; } = "PENDING";
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class EproSubmissionDefeated
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SubjectId { get; set; } = string.Empty;
    public string DiaryId { get; set; } = string.Empty;
    public DateTime DeviceTimestamp { get; set; }
    public string AnswersJson { get; set; } = "{}";
    public string OfflineSyncMarkersJson { get; set; } = "{}";
    public string Status { get; set; } = "Defeated by online-merge conflict resolution";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ClinicalQuery
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public string? VisitId { get; set; }
    public string? Domain { get; set; }
    public string TestCode { get; set; } = string.Empty;
    public string Status { get; set; } = "OPEN";
    public string? Explanation { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class EproSubmissionQuarantine
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SubjectId { get; set; } = string.Empty;
    public string DiaryId { get; set; } = string.Empty;
    public DateTime DeviceTimestamp { get; set; }
    public string AnswersJson { get; set; } = "{}";
    public string OriginalAnswersJson { get; set; } = "{}";
    public string OfflineSyncMarkersJson { get; set; } = "{}";
    public string ValidationErrorsJson { get; set; } = "[]";
    public string Status { get; set; } = "QUARANTINED";
    public string TriageHistoryJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class EhrEndpointRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string SystemType { get; set; } = "SMART_FHIR_GENERIC";
    public string FhirBaseUrl { get; set; } = string.Empty;
    public string TokenUrl { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string? PublicKeyJwksUrl { get; set; }
    public string ScopesJson { get; set; } = "[]";
    public int PollingIntervalSec { get; set; } = 300;
    public bool IsActive { get; set; } = true;
    public DateTime? LastPolledAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "system";
    public string ReasonForChange { get; set; } = "Hospital EHR endpoint configuration";
    public int VersionIndex { get; set; } = 1;
}

public class EhrReconciliationCandidateRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public string SubjectPseudonym { get; set; } = string.Empty;
    public string EhrSystemId { get; set; } = string.Empty;
    public string FhirResourceType { get; set; } = string.Empty;
    public string FhirResourceId { get; set; } = string.Empty;
    public DateTime SourceTimestamp { get; set; }
    public string TargetDomain { get; set; } = string.Empty;
    public string TargetVariable { get; set; } = string.Empty;
    public string? CdashTestcd { get; set; }
    public string? CdashTest { get; set; }
    public string CandidateValueJson { get; set; } = "{}";
    public string? CandidateUnit { get; set; }
    public string? CurrentEcrfValueJson { get; set; }
    public DateTime? CurrentEcrfUpdatedAt { get; set; }
    public string DiscrepancyType { get; set; } = "NEW_FIELD";
    public double ConfidenceScore { get; set; } = 1.0;
    public string MappingProvenance { get; set; } = string.Empty;
    public string RawFhirResourceJson { get; set; } = "{}";
    public string Status { get; set; } = "PENDING_REVIEW";
    public string? ResolutionAction { get; set; }
    public string? ResolutionReason { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ElectronicSignatureJson { get; set; }
    public string? ClinicalQueryId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "esource_ingestion_pipeline";
    public string ReasonForChange { get; set; } = "Automated SMART on FHIR eSource Ingestion";
    public int VersionIndex { get; set; } = 1;
}

public class DeviceRegistrationRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public string? SiteId { get; set; }
    public string SubjectId { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string UdiDi { get; set; } = string.Empty;
    public string? UdiPiSerial { get; set; }
    public string? UdiPiLot { get; set; }
    public string? MacAddressMasked { get; set; }
    public string DeviceToken { get; set; } = string.Empty;
    public string? FirmwareVersion { get; set; }
    public string CalibrationStatus { get; set; } = "CALIBRATED";
    public DateTime? CalibrationDate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "system";
    public string ReasonForChange { get; set; } = "Initial device registration";
    public int VersionIndex { get; set; } = 1;
}

public class SensorTelemetryStreamRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public string? SiteId { get; set; }
    public string SubjectId { get; set; } = string.Empty;
    public string DeviceToken { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public string BatchId { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public double ClockDriftOffsetMs { get; set; }
    public int ReadingCount { get; set; }
    public DateTime StartTimestamp { get; set; }
    public DateTime EndTimestamp { get; set; }
    public string OmhPayloadJson { get; set; } = "[]";
    public string FhirObservationsJson { get; set; } = "[]";
    public string RawDataHash { get; set; } = string.Empty;
    public string FeedLakehouseRecordIdsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class SensorDailySummaryRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public string? SiteId { get; set; }
    public string SubjectId { get; set; } = string.Empty;
    public DateTime SummaryDate { get; set; }
    public string DeviceType { get; set; } = string.Empty;
    public string DeviceToken { get; set; } = string.Empty;
    public int ReadingsCount { get; set; }
    public double ActiveWearHours { get; set; }
    public decimal? TirPercentage { get; set; }
    public decimal? TbrLevel1Percentage { get; set; }
    public decimal? TbrLevel2Percentage { get; set; }
    public decimal? TarLevel1Percentage { get; set; }
    public decimal? TarLevel2Percentage { get; set; }
    public decimal? MeanGlucoseMgDl { get; set; }
    public decimal? GlucoseSd { get; set; }
    public decimal? GlucoseCvPercent { get; set; }
    public decimal? GlucoseManagementIndicator { get; set; }
    public int? TotalSteps { get; set; }
    public decimal? MeanCadenceSpm { get; set; }
    public decimal? PeakCadenceSpm { get; set; }
    public decimal? ActiveMinutes { get; set; }
    public decimal? SedentaryMinutes { get; set; }
    public decimal? SleepDurationMinutes { get; set; }
    public decimal? SleepEfficiencyPercent { get; set; }
    public decimal? RestingHeartRateBpm { get; set; }
    public decimal? BestFev1Liters { get; set; }
    public decimal? BestFvcLiters { get; set; }
    public decimal? Fev1FvcRatio { get; set; }
    public decimal? Fev1PercentPredicted { get; set; }
    public decimal? PeakExpiratoryFlowLMin { get; set; }
    public decimal? MeanSystolicMmhg { get; set; }
    public decimal? MeanDiastolicMmhg { get; set; }
    public decimal? MeanArterialPressureMmhg { get; set; }
    public string SummaryManifestJson { get; set; } = "{}";
    public string? LakehouseRecordId { get; set; }
    public string Sha256Hash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class SensorSafetyAlertRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AlertId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string? SiteId { get; set; }
    public string SubjectId { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public string DeviceToken { get; set; } = string.Empty;
    public string AlertType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string ParameterName { get; set; } = string.Empty;
    public decimal ObservedValue { get; set; }
    public decimal ThresholdValue { get; set; }
    public string Unit { get; set; } = string.Empty;
    public DateTime TriggeredAt { get; set; }
    public bool IsAcknowledged { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public string? AcknowledgmentNotes { get; set; }
    public string? AuditSignature { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
