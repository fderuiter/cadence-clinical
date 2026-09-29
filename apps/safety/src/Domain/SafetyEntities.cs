using System;

namespace Cadence.Apps.Safety.Domain;

public class IcsrReportingEvaluation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CaseId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public DateTime ReceiptDate { get; set; }
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
    public DateTime? ApprovedAt { get; set; }
    public string? EsignatureManifestId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class SaeReconciliationRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public DateTime RunDate { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
    public DateTime? ApprovedAt { get; set; }
    public string? EsignatureManifestId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class SafetyAuditLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
    public DateTime? MdnReceivedAt { get; set; }
    public string? AckAStatus { get; set; }
    public DateTime? AckAReceivedAt { get; set; }
    public string? AckBStatus { get; set; }
    public DateTime? AckBReceivedAt { get; set; }
    public string AckBErrorsJson { get; set; } = "[]";
    public string? AckCStatus { get; set; }
    public DateTime? AckCReceivedAt { get; set; }
    public string AckCDetailsJson { get; set; } = "{}";
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
    public DateTime? ReviewedAt { get; set; }
    public string? EsignatureManifestId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class PeriodicSafetyReportRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ReportType { get; set; } = "DSUR";
    public string StudyId { get; set; } = string.Empty;
    public string CompoundName { get; set; } = string.Empty;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public string Status { get; set; } = "DRAFT";
    public int TotalSubjectExposure { get; set; }
    public int TotalSaes { get; set; }
    public string ReportPayloadJson { get; set; } = "{}";
    public string? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? EsignatureManifestId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
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
    public DateTime VigilanceDueDate { get; set; }
    public bool NotifiedBodyNotified { get; set; }
    public bool CompetentAuthorityNotified { get; set; }
    public string Status { get; set; } = "OPEN";
    public string? InvestigationNotes { get; set; }
    public string? RootCauseSummary { get; set; }
    public bool CapaRequired { get; set; }
    public string? CapaReference { get; set; }
    public bool FscaRequired { get; set; }
    public string? SafetyPhysicianUserId { get; set; }
    public DateTime? AdjudicatedAt { get; set; }
    public string? EsignatureManifestId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public record IcsrHeader(
    string MessageId,
    string SenderOrganization,
    string ReceiverOrganization,
    string TransmissionDate,
    string MessageType = "ichicsr"
);

public record IcsrReportIdentifiers(
    string WorldwideUniqueCaseId,
    string? LocalReportId = null,
    string? FirstSenderType = null
);

public record IcsrPatient(
    string PatientId,
    string Sex,
    int? Age = null,
    string? AgeUnit = null,
    string? BirthDate = null
);

public record IcsrMeddraCoding(
    string LltCode,
    string LltName,
    string PtCode,
    string PtName,
    string HltCode,
    string HltName,
    string HlgtCode,
    string HlgtName,
    string SocCode,
    string SocName,
    string? PrimarySocFlag = null,
    string Score = "1.0"
);

public record IcsrReaction(
    string ReactionTerm,
    IcsrMeddraCoding? MeddraCoding = null,
    string? StartDate = null,
    string? EndDate = null,
    string? Outcome = null,
    string SeriousnessDeath = "0",
    string SeriousnessLifeThreatening = "0",
    string SeriousnessHospitalization = "0",
    string SeriousnessDisability = "0",
    string SeriousnessCongenitalAnomaly = "0",
    string SeriousnessOtherMedicallyImportant = "0"
);

public record IcsrSuspectDrug(
    string DrugName,
    string? ActiveSubstanceName = null,
    string? DosageText = null,
    string? RouteOfAdministration = null,
    string? ActionTakenWithDrug = null,
    string DrugRole = "suspect"
);

public record IcsrReport(
    IcsrHeader Header,
    IcsrReportIdentifiers ReportIdentifiers,
    IcsrPatient Patient,
    IReadOnlyList<IcsrReaction> Reactions,
    IReadOnlyList<IcsrSuspectDrug> SuspectDrugs,
    int VersionIndex = 1,
    string? ReasonForChange = null
);
