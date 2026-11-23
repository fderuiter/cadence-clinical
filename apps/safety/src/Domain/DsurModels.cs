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

/// <summary>
/// The body of a DSUR approval. The signer is never named here: it is the verified caller, who must also present a
/// step-up signature token (<c>X-Sig-Token</c>) bound to the report (#5462).
/// </summary>
/// <param name="SigningReason">The meaning of the signature (21 CFR 11.50); defaults to approval of the DSUR.</param>
/// <param name="ReasonForChange">Why the record is being locked; required.</param>
/// <param name="ExpectedVersionIndex">Optional optimistic concurrency guard matching the current VersionIndex.</param>
public record DsurApproveRequest(
    string? SigningReason,
    string? ReasonForChange,
    int? ExpectedVersionIndex = null
);

public record DsurUpdateRequest(
    string? CompoundName,
    string? StudyId,
    DateTimeOffset? PeriodStart,
    DateTimeOffset? PeriodEnd,
    string? ReasonForChange,
    int? ExpectedVersionIndex = null
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

/// <summary>
/// Patient-years of exposure in the reporting period, in total and per arm group. Each subject's arm is placebo when its
/// name contains "Placebo" (so a placebo comparator is placebo), otherwise comparator when it contains "Comparator",
/// otherwise investigational: the grouping the summary tabulation counts cases by (<see cref="TreatmentArmGroups"/>).
/// </summary>
public record SubjectExposureMetrics(
    int TotalSubjectsEnrolled,
    double TotalPatientYearsExposure,
    double InvestigationalArmPatientYears,
    double PlaceboArmPatientYears,
    double ComparatorArmPatientYears,
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

/// <summary>
/// The compiled DSUR. <see cref="TotalSubjectsEnrolled"/> counts the subjects enrolled (exposure in patient-years is in
/// <see cref="ExposureMetrics"/>); <see cref="TotalSaes"/> counts the serious cases only (ICH E2F summary tabulation of
/// serious adverse events), while the line listings hold every case compiled (#5573).
/// </summary>
public record DsurReportPayload(
    string ReportId,
    string ReportType,
    string CompoundName,
    string StudyId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    SubjectExposureMetrics ExposureMetrics,
    int TotalSubjectsEnrolled,
    int TotalSaes,
    int TotalFatalCases,
    int TotalSusars,
    int TotalAesi,
    IReadOnlyList<DsurLineListingItem> LineListings,
    IReadOnlyList<DsurSummaryTabulationRow> SummaryTabulations,
    IReadOnlyList<IReadOnlyDictionary<string, object>> RsiComparisonSummary,
    IReadOnlyList<IReadOnlyDictionary<string, object>> SignalSummary
);

/// <summary>The arm group a DSUR counts a subject or a case in.</summary>
public enum TreatmentArmGroup
{
    /// <summary>The investigational drug.</summary>
    Investigational,

    /// <summary>Placebo, including a placebo comparator.</summary>
    Placebo,

    /// <summary>An active comparator.</summary>
    Comparator,
}

/// <summary>One grouping of treatment arms for both DSUR exposure and the summary tabulation (#5573).</summary>
public static class TreatmentArmGroups
{
    /// <summary>
    /// Placebo when <paramref name="arm"/> names placebo, otherwise comparator when it names a comparator, otherwise the
    /// investigational drug.
    /// </summary>
    public static TreatmentArmGroup Of(string? arm)
    {
        if (arm is null)
        {
            return TreatmentArmGroup.Investigational;
        }

        if (arm.Contains("Placebo", StringComparison.OrdinalIgnoreCase))
        {
            return TreatmentArmGroup.Placebo;
        }

        return arm.Contains("Comparator", StringComparison.OrdinalIgnoreCase)
            ? TreatmentArmGroup.Comparator
            : TreatmentArmGroup.Investigational;
    }
}
