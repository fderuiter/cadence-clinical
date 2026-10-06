using System;
using System.Collections.Generic;

namespace Cadence.Apps.Safety.Domain;

public record SubjectEnrollmentRecord(
    string SubjectId,
    string StudyId,
    string TreatmentArm,
    DateTimeOffset EnrollmentDate,
    DateTimeOffset? DiscontinuationDate
);

public record SafetyCaseIcsrInput(
    string CaseId,
    string SubjectId,
    string StudyId,
    DateTimeOffset ReceiptDate,
    string AeTerm,
    string MeddraPt,
    string MeddraSoc,
    bool IsSerious,
    bool IsFatal,
    bool IsAesi,
    string? AesiCategory,
    string Causality,
    string Expectedness,
    string TreatmentArm,
    string ActionTaken,
    DateTimeOffset OnsetDate,
    DateTimeOffset? ResolutionDate
);

public record DsurCompileRequest(
    string CompoundName,
    string? StudyId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    string? ReasonForChange,
    IReadOnlyList<SubjectEnrollmentRecord>? SubjectEnrollments = null,
    IReadOnlyList<SafetyCaseIcsrInput>? SafetyCases = null
);

public record DsurApproveRequest(
    string? SignerUserId,
    string? SigningReason,
    string? ReasonForChange
);

public record DsurUpdateRequest(
    string? CompoundName,
    string? StudyId,
    DateTimeOffset? PeriodStart,
    DateTimeOffset? PeriodEnd,
    string? ReasonForChange
);

public record SubjectExposureDetail(
    string SubjectId,
    string StudyId,
    string TreatmentArm,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    double ExposureDays,
    double PatientYears
);

public record SubjectExposureMetrics(
    int TotalSubjectsEnrolled,
    double TotalPatientYearsExposure,
    double InvestigationalArmPatientYears,
    double PlaceboArmPatientYears,
    IReadOnlyList<SubjectExposureDetail> ExposureDetails
);

public record DsurLineListingItem(
    string CaseId,
    string SubjectId,
    string StudyId,
    string TreatmentArm,
    string? DosageCohort,
    int? Age,
    string? Sex,
    string AeTerm,
    string MeddraPt,
    string MeddraSoc,
    DateTimeOffset OnsetDate,
    DateTimeOffset? ResolutionDate,
    bool IsSerious,
    bool IsFatal,
    bool IsAesi,
    string? AesiCategory,
    string Causality,
    string Expectedness,
    string ActionTaken
);

public record DsurSummaryTabulationRow(
    string MeddraSoc,
    string MeddraPt,
    bool IsAesi,
    string? AesiCategory,
    bool IsFatal,
    int InvestigationalDrugCount,
    int PlaceboCount,
    int ComparatorCount,
    int TotalCount,
    string RsiStatus
);

public record DsurReportPayload(
    string ReportId,
    string ReportType,
    string CompoundName,
    string StudyId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    SubjectExposureMetrics ExposureMetrics,
    int TotalSubjectExposure,
    int TotalSaes,
    int TotalFatalCases,
    int TotalSusars,
    int TotalAesi,
    IReadOnlyList<DsurLineListingItem> LineListings,
    IReadOnlyList<DsurSummaryTabulationRow> SummaryTabulations,
    IReadOnlyList<IReadOnlyDictionary<string, object>> RsiComparisonSummary,
    IReadOnlyList<IReadOnlyDictionary<string, object>> SignalSummary
);

public record DsurSnapshotSummary(
    string Id,
    string ReportId,
    string ReportType,
    string CompoundName,
    string StudyId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    string Status,
    string PayloadDigestSha256,
    long VersionIndex,
    DateTime CreatedAt,
    string CreatedBy,
    string? ApprovedByUserId = null,
    DateTimeOffset? ApprovedAt = null
);

public record DsurSnapshotDetail(
    string Id,
    string ReportId,
    string ReportType,
    string CompoundName,
    string StudyId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    string Status,
    DsurReportPayload Payload,
    string PayloadDigestSha256,
    long VersionIndex,
    DateTime CreatedAt,
    string CreatedBy,
    string? ApprovedByUserId = null,
    DateTimeOffset? ApprovedAt = null,
    string? EsignatureManifestId = null
);

public record DsurSnapshotVerificationResult(
    string SnapshotId,
    string ReportId,
    bool HashValid,
    string PayloadHash,
    string ExpectedHash,
    bool SignatureValid,
    string? SignatureStatus,
    bool MerkleChainValid,
    string Message
);
