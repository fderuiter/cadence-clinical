using System;

namespace Cadence.Apps.Interop.Domain;

public class EproSubmission
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SubjectId { get; set; } = string.Empty;
    public string DiaryId { get; set; } = string.Empty;
    public required DateTimeOffset DeviceTimestamp { get; set; }
    public required DateTimeOffset ServerTimestamp { get; set; }
    public string AnswersJson { get; set; } = "{}";
    public string OfflineSyncMarkersJson { get; set; } = "{}";
    public string SyncStatus { get; set; } = "RESOLVED";
    public required DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public string ReasonForChange { get; set; } = "ePRO mobile submission";
    public int VersionIndex { get; set; } = 1;
}

public class InteropAuditLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required DateTimeOffset Timestamp { get; set; }
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
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class SubjectAssignment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SubjectId { get; set; } = string.Empty;
    public string InstrumentId { get; set; } = string.Empty;
    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset EndDate { get; set; }
    public string? RecurrencePattern { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset? LastMissedAlertAt { get; set; }
    public DateTimeOffset? LastMissedAlertNotifiedAt { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class SubjectNotification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SubjectId { get; set; } = string.Empty;
    public string? AssignmentId { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string DeliveryStatus { get; set; } = "PENDING";
    public bool IsRead { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class EproSubmissionDefeated
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SubjectId { get; set; } = string.Empty;
    public string DiaryId { get; set; } = string.Empty;
    public DateTimeOffset DeviceTimestamp { get; set; }
    public string AnswersJson { get; set; } = "{}";
    public string OfflineSyncMarkersJson { get; set; } = "{}";
    public string Status { get; set; } = "Defeated by online-merge conflict resolution";
    public required DateTimeOffset CreatedAt { get; set; }
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
    public required DateTimeOffset CreatedAt { get; set; }
    public required DateTimeOffset UpdatedAt { get; set; }
}

public class EproSubmissionQuarantine
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SubjectId { get; set; } = string.Empty;
    public string DiaryId { get; set; } = string.Empty;
    public DateTimeOffset DeviceTimestamp { get; set; }
    public string AnswersJson { get; set; } = "{}";
    public string OriginalAnswersJson { get; set; } = "{}";
    public string OfflineSyncMarkersJson { get; set; } = "{}";
    public string ValidationErrorsJson { get; set; } = "[]";
    public string Status { get; set; } = "QUARANTINED";
    public string TriageHistoryJson { get; set; } = "[]";
    public required DateTimeOffset CreatedAt { get; set; }
    public required DateTimeOffset UpdatedAt { get; set; }
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
    public DateTimeOffset? LastPolledAt { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
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
    public DateTimeOffset SourceTimestamp { get; set; }
    public string TargetDomain { get; set; } = string.Empty;
    public string TargetVariable { get; set; } = string.Empty;
    public string? CdashTestcd { get; set; }
    public string? CdashTest { get; set; }
    public string CandidateValueJson { get; set; } = "{}";
    public string? CandidateUnit { get; set; }
    public string? CurrentEcrfValueJson { get; set; }
    public DateTimeOffset? CurrentEcrfUpdatedAt { get; set; }
    public string DiscrepancyType { get; set; } = "NEW_FIELD";
    public double ConfidenceScore { get; set; } = 1.0;
    public string MappingProvenance { get; set; } = string.Empty;
    public string RawFhirResourceJson { get; set; } = "{}";
    public string Status { get; set; } = "PENDING_REVIEW";
    public string? ResolutionAction { get; set; }
    public string? ResolutionReason { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ElectronicSignatureJson { get; set; }
    public string? ClinicalQueryId { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
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
    /// <summary>Keyed HMAC-SHA256 of the device token (computed by DeviceTokenHasher); the raw token is never stored (#4770).</summary>
    public string DeviceTokenHash { get; set; } = string.Empty;
    public string? FirmwareVersion { get; set; }
    public string CalibrationStatus { get; set; } = "CALIBRATED";
    public DateTimeOffset? CalibrationDate { get; set; }
    public bool IsActive { get; set; } = true;
    public required DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public string ReasonForChange { get; set; } = "Initial device registration";
    public int VersionIndex { get; set; } = 1;
}

public class SensorTelemetryStreamRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public string? SiteId { get; set; }
    public string SubjectId { get; set; } = string.Empty;
    /// <summary>Keyed HMAC-SHA256 of the device token (computed by DeviceTokenHasher); the raw token is never stored (#4770).</summary>
    public string DeviceTokenHash { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public string BatchId { get; set; } = string.Empty;
    public required DateTimeOffset ReceivedAt { get; set; }
    public double ClockDriftOffsetMs { get; set; }
    public int ReadingCount { get; set; }
    public DateTimeOffset StartTimestamp { get; set; }
    public DateTimeOffset EndTimestamp { get; set; }
    public string OmhPayloadJson { get; set; } = "[]";
    public string FhirObservationsJson { get; set; } = "[]";
    public string RawDataHash { get; set; } = string.Empty;
    public string FeedLakehouseRecordIdsJson { get; set; } = "[]";
    public required DateTimeOffset CreatedAt { get; set; }
}

public class SensorDailySummaryRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public string? SiteId { get; set; }
    public string SubjectId { get; set; } = string.Empty;
    public DateTimeOffset SummaryDate { get; set; }
    public string DeviceType { get; set; } = string.Empty;
    /// <summary>Keyed HMAC-SHA256 of the device token (computed by DeviceTokenHasher); the raw token is never stored (#4770).</summary>
    public string DeviceTokenHash { get; set; } = string.Empty;
    public int ReadingsCount { get; set; }
    public double ActiveWearHours { get; set; }
    public double? TirPercentage { get; set; }
    public double? TbrLevel1Percentage { get; set; }
    public double? TbrLevel2Percentage { get; set; }
    public double? TarLevel1Percentage { get; set; }
    public double? TarLevel2Percentage { get; set; }
    public double? MeanGlucoseMgDl { get; set; }
    public double? GlucoseSd { get; set; }
    public double? GlucoseCvPercent { get; set; }
    public double? GlucoseManagementIndicator { get; set; }
    public int? TotalSteps { get; set; }
    public double? MeanCadenceSpm { get; set; }
    public double? PeakCadenceSpm { get; set; }
    public double? ActiveMinutes { get; set; }
    public double? SedentaryMinutes { get; set; }
    public double? SleepDurationMinutes { get; set; }
    public double? SleepEfficiencyPercent { get; set; }
    public double? RestingHeartRateBpm { get; set; }
    public double? BestFev1Liters { get; set; }
    public double? BestFvcLiters { get; set; }
    public double? Fev1FvcRatio { get; set; }
    public double? Fev1PercentPredicted { get; set; }
    public double? PeakExpiratoryFlowLMin { get; set; }
    public double? MeanSystolicMmhg { get; set; }
    public double? MeanDiastolicMmhg { get; set; }
    public double? MeanArterialPressureMmhg { get; set; }
    public string SummaryManifestJson { get; set; } = "{}";
    public string? LakehouseRecordId { get; set; }
    public string Sha256Hash { get; set; } = string.Empty;
    public required DateTimeOffset CreatedAt { get; set; }
}

public class SensorSafetyAlertRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AlertId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string? SiteId { get; set; }
    public string SubjectId { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    /// <summary>Keyed HMAC-SHA256 of the device token (computed by DeviceTokenHasher); the raw token is never stored (#4770).</summary>
    public string DeviceTokenHash { get; set; } = string.Empty;
    public string AlertType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string ParameterName { get; set; } = string.Empty;
    public double ObservedValue { get; set; }
    public double ThresholdValue { get; set; }
    public string Unit { get; set; } = string.Empty;
    public DateTimeOffset TriggeredAt { get; set; }
    public bool IsAcknowledged { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public string? AcknowledgmentNotes { get; set; }
    public string? AuditSignature { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
}
