using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.Apps.Execution.Application.EditChecks;

/// <summary>
/// Pre-fetched in-memory evaluation container loaded via consolidated batch queries to eliminate
/// N+1 database roundtrips during longitudinal and cross-form edit check evaluations.
/// Exposes consent gating state (<see cref="IsConsented"/> and <see cref="ConsentStatus"/>).
/// @req:PRD-SYS-004
/// @req:SRS-EDC-005
/// </summary>
public sealed class BatchEvaluationContext
{
    public string? StudyId { get; }
    public string? SubjectId { get; }
    public IReadOnlyList<FormSubmission> Submissions { get; }
    public IReadOnlyList<ClinicalObservation> Observations { get; }
    public bool IsConsented { get; }
    public string ConsentStatus { get; }

    private readonly Dictionary<string, List<ClinicalObservation>> _obsByCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string VisitId, string TestCode), List<ClinicalObservation>> _obsByVisitAndCode = new();
    private readonly Dictionary<string, List<FormSubmission>> _submissionsByVisit = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<FormSubmission>> _submissionsByFormId = new(StringComparer.OrdinalIgnoreCase);

    public BatchEvaluationContext(
        IReadOnlyList<FormSubmission> submissions,
        IReadOnlyList<ClinicalObservation> observations,
        bool isConsented = true,
        string consentStatus = "CONSENTED")
        : this(null, null, observations, submissions, isConsented, consentStatus)
    {
    }

    public BatchEvaluationContext(
        string? studyId,
        string? subjectId,
        IReadOnlyList<ClinicalObservation> observations,
        IReadOnlyList<FormSubmission> formSubmissions,
        ClinicalSubject? subject,
        IReadOnlyList<SubjectConsent>? consents)
        : this(studyId, subjectId, observations, formSubmissions,
               EvaluateConsent(subject, consents).IsConsented,
               EvaluateConsent(subject, consents).ConsentStatus)
    {
    }

    public BatchEvaluationContext(
        string? studyId,
        string? subjectId,
        IReadOnlyList<ClinicalObservation> observations,
        IReadOnlyList<FormSubmission> formSubmissions,
        bool isConsented = true,
        string consentStatus = "CONSENTED")
    {
        StudyId = studyId;
        SubjectId = subjectId;
        var activeSubmissions = formSubmissions.Where(f => f.IsActive).ToList();
        var activeObservations = observations.Where(o => o.IsActive).ToList();
        Submissions = activeSubmissions;
        Observations = activeObservations;
        IsConsented = isConsented;
        ConsentStatus = consentStatus;

        foreach (var sub in activeSubmissions)
        {
            if (!string.IsNullOrWhiteSpace(sub.VisitId))
            {
                if (!_submissionsByVisit.TryGetValue(sub.VisitId, out var visitList))
                {
                    visitList = new List<FormSubmission>();
                    _submissionsByVisit[sub.VisitId] = visitList;
                }
                visitList.Add(sub);
            }

            string formKey = sub.FormId.Value;
            if (!_submissionsByFormId.TryGetValue(formKey, out var formList))
            {
                formList = new List<FormSubmission>();
                _submissionsByFormId[formKey] = formList;
            }
            formList.Add(sub);
        }

        foreach (var obs in activeObservations)
        {
            string code = obs.TestCode.Value;
            if (!_obsByCode.TryGetValue(code, out var codeList))
            {
                codeList = new List<ClinicalObservation>();
                _obsByCode[code] = codeList;
            }
            codeList.Add(obs);

            if (!string.IsNullOrWhiteSpace(obs.VisitId))
            {
                var key = (obs.VisitId, code);
                if (!_obsByVisitAndCode.TryGetValue(key, out var visitCodeList))
                {
                    visitCodeList = new List<ClinicalObservation>();
                    _obsByVisitAndCode[key] = visitCodeList;
                }
                visitCodeList.Add(obs);
            }
        }
    }

    public IReadOnlyList<ClinicalObservation> GetObservationsByTestCode(string testCode) =>
        _obsByCode.TryGetValue(testCode, out var list) ? list : Array.Empty<ClinicalObservation>();

    public IReadOnlyList<ClinicalObservation> GetObservationsByVisitAndTestCode(string visitId, string testCode) =>
        _obsByVisitAndCode.TryGetValue((visitId, testCode), out var list) ? list : Array.Empty<ClinicalObservation>();

    public IReadOnlyList<FormSubmission> GetSubmissionsByVisit(string visitId) =>
        _submissionsByVisit.TryGetValue(visitId, out var list) ? list : Array.Empty<FormSubmission>();

    public IReadOnlyList<FormSubmission> GetSubmissionsByFormId(string formId) =>
        _submissionsByFormId.TryGetValue(formId, out var list) ? list : Array.Empty<FormSubmission>();

    public static (bool IsConsented, string ConsentStatus) EvaluateConsent(
        ClinicalSubject? subject,
        IReadOnlyList<SubjectConsent>? consents)
    {
        if (subject is not null)
        {
            if (subject.Status == SubjectState.Withdrawn)
            {
                return (false, "WITHDRAWN");
            }
            if (subject.Status == SubjectState.ScreenFailed)
            {
                return (false, "SCREEN_FAILED");
            }
        }

        bool hasSignedConsent = consents is not null && consents.Any(c => c.IcfSigned || c.IsPaperOverride);
        if (!hasSignedConsent)
        {
            return (false, "UNCONSENTED");
        }

        return (true, "CONSENTED");
    }
}
