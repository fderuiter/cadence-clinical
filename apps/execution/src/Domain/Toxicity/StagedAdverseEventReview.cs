using System;

using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Core.Domain;
using Cadence.Kernel;

namespace Cadence.Apps.Execution.Domain.Toxicity;

/// <summary>
/// Aggregate root representing a Grade 3/4 laboratory toxicity event staged for Investigator review.
/// @req:PRD-SYS-001
/// @req:SRS-EDC-001
/// </summary>
public sealed class StagedAdverseEventReview : AggregateRoot<string>
{
    public StudyCode StudyId { get; private set; }
    public SiteCode? SiteId { get; private set; }
    public SubjectCode SubjectId { get; private set; }
    public string? VisitId { get; private set; }
    public ObservationId? ObservationId { get; private set; }
    public FormSubmissionId? FormSubmissionId { get; private set; }
    public QueryId? QueryId { get; private set; }
    public TestCode TestCode { get; private set; }
    public string TestName { get; private set; } = string.Empty;
    public double LabValue { get; private set; }
    public string Unit { get; private set; } = string.Empty;
    public int CtcaeGrade { get; private set; }
    public string GradingScale { get; private set; } = "CTCAE_V5";
    public string ToxicityTerm { get; private set; } = string.Empty;
    public string ClinicalDescription { get; private set; } = string.Empty;
    public bool IsHysLawWarning { get; private set; }
    public bool IsDeltaCheckAlert { get; private set; }
    public double? DeltaPercentage { get; private set; }
    public string ReviewStatus { get; private set; } = "STAGED_FOR_INVESTIGATOR_REVIEW";
    public string? ReviewedBy { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? InvestigatorNotes { get; private set; }
    public int Version { get; private set; } = 1;
    public bool IsDeleted { get; private set; } = false;

    private StagedAdverseEventReview() { } // EF Core

    public static StagedAdverseEventReview Create(
        StudyCode studyId,
        SubjectCode subjectId,
        TestCode testCode,
        string testName,
        double labValue,
        string unit,
        int ctcaeGrade,
        string toxicityTerm,
        string clinicalDescription,
        bool isHysLawWarning,
        bool isDeltaCheckAlert,
        double? deltaPercentage,
        SiteCode? siteId = null,
        string? visitId = null,
        ObservationId? observationId = null,
        FormSubmissionId? formSubmissionId = null,
        QueryId? queryId = null,
        string gradingScale = "CTCAE_V5")
    {
        return new StagedAdverseEventReview
        {
            Id = Guid.NewGuid().ToString("N"),
            StudyId = studyId,
            SubjectId = subjectId,
            SiteId = siteId,
            VisitId = visitId,
            ObservationId = observationId,
            FormSubmissionId = formSubmissionId,
            QueryId = queryId,
            TestCode = testCode,
            TestName = testName,
            LabValue = labValue,
            Unit = unit,
            CtcaeGrade = ctcaeGrade,
            GradingScale = gradingScale,
            ToxicityTerm = toxicityTerm,
            ClinicalDescription = clinicalDescription,
            IsHysLawWarning = isHysLawWarning,
            IsDeltaCheckAlert = isDeltaCheckAlert,
            DeltaPercentage = deltaPercentage,
            ReviewStatus = "STAGED_FOR_INVESTIGATOR_REVIEW",
            Version = 1,
            IsDeleted = false
        };
    }

    public void LinkReferences(FormSubmissionId? formSubmissionId, QueryId? queryId)
    {
        if (formSubmissionId.HasValue) FormSubmissionId = formSubmissionId.Value;
        if (queryId.HasValue) QueryId = queryId.Value;
    }

    public void Confirm(string reviewedBy, string? notes, string reasonForChange, DateTimeOffset reviewedAt)
    {
        ReviewStatus = "CONFIRMED";
        ReviewedBy = reviewedBy;
        InvestigatorNotes = notes;
        ReviewedAt = reviewedAt;
        ReasonForChange = reasonForChange;
        Version++;
    }

    public void Dismiss(string reviewedBy, string? notes, string reasonForChange, DateTimeOffset reviewedAt)
    {
        ReviewStatus = "DISMISSED";
        ReviewedBy = reviewedBy;
        InvestigatorNotes = notes;
        ReviewedAt = reviewedAt;
        ReasonForChange = reasonForChange;
        Version++;
    }

    public void Escalate(string reviewedBy, string? notes, string reasonForChange, DateTimeOffset reviewedAt)
    {
        ReviewStatus = "ESCALATED_SAE";
        ReviewedBy = reviewedBy;
        InvestigatorNotes = notes;
        ReviewedAt = reviewedAt;
        ReasonForChange = reasonForChange;
        Version++;
    }
}
