using System;

using Cadence.Core.Domain;

namespace Cadence.Apps.Safety.Domain;

public class IcsrReportingEvaluation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CaseId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public DateTimeOffset ReceiptDate { get; set; }
    public bool SeriousnessDeath { get; set; }
    public bool SeriousnessLifeThreatening { get; set; }
    public bool SeriousnessHospitalization { get; set; }
    public bool SeriousnessDisability { get; set; }
    public bool SeriousnessCongenitalAnomaly { get; set; }
    public bool SeriousnessOther { get; set; }
    public string Causality { get; set; } = string.Empty;
    public string Expectedness { get; set; } = string.Empty;
    public bool IsSerious { get; set; }
    public bool IsUnexpected { get; set; }
    public bool IsCausal { get; set; }
    public bool IsSusar { get; set; }
    public bool AnyExpedited { get; set; }
    public int? ShortestTimeframeDays { get; set; }
    public string EvaluationsJson { get; set; } = "[]";
    public string ApprovalStatus { get; set; } = "DRAFT";
    public string? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? EsignatureManifestId { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class SafetyCaseIcsr
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string WorldwideUniqueCaseId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string CaseDataJson { get; set; } = "{}";
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class ExportJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string JobName { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING";
    public string? Output { get; set; }
    public string? Error { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class SaeReconciliationRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public required DateTimeOffset RunDate { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class SaeDiscrepancy
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RunId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string CaseEventKey { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string? ExpectedValue { get; set; }
    public string? ActualValue { get; set; }
    public string? MeddraVersion { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class SaeReconciliationJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING";
    public string? RunId { get; set; }
    public string? Error { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class SafetyNarrative
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public string CaseId { get; set; } = string.Empty;
    public string SaeEventKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string SectionsJson { get; set; } = "[]";
    public string RawNarrativeText { get; set; } = string.Empty;
    public string TimelineEventsJson { get; set; } = "[]";
    public string GroundedClaimsJson { get; set; } = "[]";
    public string ModelIdentifier { get; set; } = string.Empty;
    public string PromptHash { get; set; } = string.Empty;
    public double ConfidenceScore { get; set; } = 1.0;
    public string ReviewStatus { get; set; } = "DRAFT_AI";
    public string? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? EsignatureManifestId { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class SafetyNarrativeRevision
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SafetyNarrativeId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string SectionsJson { get; set; } = "[]";
    public string RawNarrativeText { get; set; } = string.Empty;
    public string TimelineEventsJson { get; set; } = "[]";
    public string GroundedClaimsJson { get; set; } = "[]";
    public string ReviewStatus { get; set; } = "DRAFT_AI";
    public string? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? EsignatureManifestId { get; set; }
    public required DateTimeOffset RecordedAt { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;

    public static SafetyNarrativeRevision FromNarrative(SafetyNarrative narrative, DateTimeOffset recordedAt, string recordedBy) =>
        new()
        {
            SafetyNarrativeId = narrative.Id,
            RevisionNumber = narrative.VersionIndex,
            Title = narrative.Title,
            SectionsJson = narrative.SectionsJson,
            RawNarrativeText = narrative.RawNarrativeText,
            TimelineEventsJson = narrative.TimelineEventsJson,
            GroundedClaimsJson = narrative.GroundedClaimsJson,
            ReviewStatus = narrative.ReviewStatus,
            ApprovedByUserId = narrative.ApprovedByUserId,
            ApprovedAt = narrative.ApprovedAt,
            EsignatureManifestId = narrative.EsignatureManifestId,
            RecordedAt = recordedAt,
            RecordedBy = recordedBy,
            ReasonForChange = narrative.ReasonForChange,
        };
}

public class SafetyAuditLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? ReasonForChange { get; set; }
    public int VersionIndex { get; set; } = 1;
    public string Action { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string? RecordId { get; set; }
}

public class As2Transmission
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TransmissionId { get; set; } = string.Empty;
    public string? CaseId { get; set; }
    public string? StudyId { get; set; }
    public string? WorldwideUniqueCaseId { get; set; }
    public string MessageId { get; set; } = string.Empty;
    public string As2From { get; set; } = string.Empty;
    public string As2To { get; set; } = string.Empty;
    public string TargetAuthority { get; set; } = string.Empty;
    public string Environment { get; set; } = "PRODUCTION";
    public string Status { get; set; } = "PENDING";
    public string PayloadDigestSha256 { get; set; } = string.Empty;
    public string Mic { get; set; } = string.Empty;
    public string EncryptionAlgorithm { get; set; } = "AES-256-GCM";
    public string SignatureAlgorithm { get; set; } = "SHA-256-RSA";
    public int OutboundPayloadSize { get; set; }
    public string? OutboundXml { get; set; }
    public string? MdnMessageId { get; set; }
    public string? MdnDisposition { get; set; }
    public string? MdnMic { get; set; }
    public string? MdnStatus { get; set; }
    public DateTimeOffset? MdnReceivedAt { get; set; }
    public string? AckAStatus { get; set; }
    public DateTimeOffset? AckAReceivedAt { get; set; }
    public string? AckBStatus { get; set; }
    public DateTimeOffset? AckBReceivedAt { get; set; }
    public string AckBErrorsJson { get; set; } = "[]";
    public string? AckCStatus { get; set; }
    public DateTimeOffset? AckCReceivedAt { get; set; }
    public string AckCDetailsJson { get; set; } = "{}";
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class As2TransmissionAuditLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TransmissionId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string Details { get; set; } = string.Empty;
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? ReasonForChange { get; set; }
    public int VersionIndex { get; set; } = 1;
}

public class SafetySignalDetectionRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? StudyId { get; set; }
    public string TargetDrug { get; set; } = string.Empty;
    public string BenchmarkDataset { get; set; } = "INTERNAL_TRIAL_POOL";
    public int TotalCasesAnalyzed { get; set; }
    public int SignalsDetectedCount { get; set; }
    public string SummaryMetricsJson { get; set; } = "[]";
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class SafetySignalAlert
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? DetectionRunId { get; set; }
    public string? StudyId { get; set; }
    public string TargetDrug { get; set; } = string.Empty;
    public string MeddraTerm { get; set; } = string.Empty;
    public string TermLevel { get; set; } = "PT";
    public string? MeddraSoc { get; set; }
    public int CaseCountA { get; set; }
    public double ExpectedCountE { get; set; }
    public double Prr { get; set; }
    public double PrrCiLower { get; set; }
    public double PrrCiUpper { get; set; }
    public double ChiSquareYates { get; set; }
    public double Ror { get; set; }
    public double RorCiLower { get; set; }
    public double RorCiUpper { get; set; }
    public double Ebgm { get; set; }
    public double Eb05 { get; set; }
    public double Eb95 { get; set; }
    public string Severity { get; set; } = "HIGH";
    public string Status { get; set; } = "TRIGGERED";
    public string TriageSummary { get; set; } = string.Empty;
    public string? AssignedSafetyOfficer { get; set; }
    public string? ReviewDecision { get; set; }
    public string? ReviewJustification { get; set; }
    public string? ClinicalSummary { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? EsignatureManifestId { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

/// <summary>
/// A periodic safety report (DSUR). It is <see cref="IGxPAuditable"/>, so each compile, update and approval writes an
/// audit entry carrying the record's own <see cref="VersionIndex"/> (#5573).
/// </summary>
public class PeriodicSafetyReportRecord : IGxPAuditable
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ReportType { get; set; } = "DSUR";
    public string StudyId { get; set; } = string.Empty;
    public string CompoundName { get; set; } = string.Empty;
    public DateTimeOffset PeriodStart { get; set; }
    public DateTimeOffset PeriodEnd { get; set; }
    public string Status { get; set; } = "DRAFT";

    /// <summary>Patient-years of exposure in the reporting period, unrounded (formerly the rounded TotalSubjectExposure).</summary>
    public double TotalPatientYearsExposure { get; set; }

    /// <summary>The serious adverse events among the cases compiled.</summary>
    public int TotalSaes { get; set; }
    public string ReportPayloadJson { get; set; } = "{}";
    public string? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? EsignatureManifestId { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;

    DateTime IGxPAuditable.CreatedAt => CreatedAt.UtcDateTime;

    long IGxPAuditable.VersionIndex => VersionIndex;
}

public class PeriodicSafetyReportRevision
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ReportId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string ReportType { get; set; } = "DSUR";
    public string StudyCode { get; set; } = string.Empty;
    public string CompoundName { get; set; } = string.Empty;
    public DateTimeOffset PeriodStart { get; set; }
    public DateTimeOffset PeriodEnd { get; set; }
    public string Status { get; set; } = "DRAFT";
    public double TotalPatientYearsExposure { get; set; }
    public int TotalSaes { get; set; }
    public string ReportPayloadJson { get; set; } = "{}";
    public string? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? EsignatureManifestId { get; set; }
    public required DateTimeOffset RecordedAt { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;

    public static PeriodicSafetyReportRevision FromRecord(PeriodicSafetyReportRecord record, DateTimeOffset recordedAt, string recordedBy) =>
        new()
        {
            ReportId = record.Id,
            RevisionNumber = record.VersionIndex,
            ReportType = record.ReportType,
            StudyCode = record.StudyId,
            CompoundName = record.CompoundName,
            PeriodStart = record.PeriodStart,
            PeriodEnd = record.PeriodEnd,
            Status = record.Status,
            TotalPatientYearsExposure = record.TotalPatientYearsExposure,
            TotalSaes = record.TotalSaes,
            ReportPayloadJson = record.ReportPayloadJson,
            ApprovedByUserId = record.ApprovedByUserId,
            ApprovedAt = record.ApprovedAt,
            EsignatureManifestId = record.EsignatureManifestId,
            RecordedAt = recordedAt,
            RecordedBy = recordedBy,
            ReasonForChange = record.ReasonForChange,
        };

    public PeriodicSafetyReportRecord ToRecord(DateTimeOffset createdAt, string createdBy) =>
        new()
        {
            Id = ReportId,
            ReportType = ReportType,
            StudyId = StudyCode,
            CompoundName = CompoundName,
            PeriodStart = PeriodStart,
            PeriodEnd = PeriodEnd,
            Status = Status,
            TotalPatientYearsExposure = TotalPatientYearsExposure,
            TotalSaes = TotalSaes,
            ReportPayloadJson = ReportPayloadJson,
            ApprovedByUserId = ApprovedByUserId,
            ApprovedAt = ApprovedAt,
            EsignatureManifestId = EsignatureManifestId,
            CreatedAt = createdAt,
            CreatedBy = createdBy,
            ReasonForChange = ReasonForChange,
            VersionIndex = RevisionNumber,
        };
}

public class DeviceSafetyInvestigation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CaseNumber { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string? SubjectId { get; set; }
    public string DeficiencyId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceUdiDi { get; set; } = string.Empty;
    public string LotNumber { get; set; } = string.Empty;
    public string? SerialNumber { get; set; }
    public string Classification { get; set; } = "ADE";
    public string Causality { get; set; } = "POSSIBLE";
    public bool IsUsade { get; set; }
    public bool VigilanceReportingRequired { get; set; } = true;
    public int VigilanceDeadlineDays { get; set; } = 15;
    public DateTimeOffset VigilanceDueDate { get; set; }
    public bool NotifiedBodyNotified { get; set; }
    public bool CompetentAuthorityNotified { get; set; }
    public string Status { get; set; } = "OPEN";
    public string? InvestigationNotes { get; set; }
    public string? RootCauseSummary { get; set; }
    public bool CapaRequired { get; set; }
    public string? CapaReference { get; set; }
    public bool FscaRequired { get; set; }
    public string? SafetyPhysicianUserId { get; set; }
    public DateTimeOffset? AdjudicatedAt { get; set; }
    public string? EsignatureManifestId { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}
