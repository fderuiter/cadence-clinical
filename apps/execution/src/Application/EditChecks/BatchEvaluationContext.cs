using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Observations;

namespace Cadence.Apps.Execution.Application.EditChecks;

/// <summary>
/// Pre-fetched in-memory evaluation container loaded via consolidated batch queries to eliminate
/// N+1 database roundtrips during longitudinal and cross-form edit check evaluations.
/// @req:PRD-SYS-004
/// @req:SRS-EDC-005
/// </summary>
public sealed class BatchEvaluationContext
{
    public string? StudyId { get; }
    public string? SubjectId { get; }
    public IReadOnlyList<FormSubmission> Submissions { get; }
    public IReadOnlyList<ClinicalObservation> Observations { get; }

    private readonly Dictionary<string, List<ClinicalObservation>> _obsByCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string VisitId, string TestCode), List<ClinicalObservation>> _obsByVisitAndCode = new();
    private readonly Dictionary<string, List<FormSubmission>> _submissionsByVisit = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<FormSubmission>> _submissionsByFormId = new(StringComparer.OrdinalIgnoreCase);

    public BatchEvaluationContext(
        IReadOnlyList<FormSubmission> submissions,
        IReadOnlyList<ClinicalObservation> observations)
        : this(null, null, observations, submissions)
    {
    }

    public BatchEvaluationContext(
        string? studyId,
        string? subjectId,
        IReadOnlyList<ClinicalObservation> observations,
        IReadOnlyList<FormSubmission> formSubmissions)
    {
        StudyId = studyId;
        SubjectId = subjectId;
        var activeSubmissions = formSubmissions.Where(f => f.IsActive).ToList();
        var activeObservations = observations.Where(o => o.IsActive).ToList();
        Submissions = activeSubmissions;
        Observations = activeObservations;

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
}
