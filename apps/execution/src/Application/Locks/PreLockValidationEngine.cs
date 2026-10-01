using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Execution.Application.Locks;

/// <summary>
/// Pre-lock discrepancy and source data validation engine implementation.
/// Aggregates open queries, unanswered queries, and unverified source data across Study, Site, Subject, Visit, Form, and Field hierarchy levels.
/// Executes in under 500 milliseconds without blocking UI responsiveness.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-007
/// @req:PRD-SYS-053
/// @req:SRS-EDC-004
/// </summary>
public sealed class PreLockValidationEngine : IPreLockValidationEngine
{
    private readonly ExecutionDbContext _dbContext;
    private readonly IClock _clock;

    public PreLockValidationEngine(ExecutionDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<PreflightCheckResponse> RunPreflightCheckAsync(PreflightCheckRequest request, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var checkedAt = _clock.UtcNow;

        var scopeType = (request.ScopeType ?? "STUDY").ToUpperInvariant();
        var scopeId = request.ScopeId ?? request.StudyId ?? "ALL";

        var studyCode = !string.IsNullOrWhiteSpace(request.StudyId) ? new StudyCode(request.StudyId) : (StudyCode?)null;
        var siteCode = !string.IsNullOrWhiteSpace(request.SiteId) ? new SiteCode(request.SiteId) : (SiteCode?)null;
        var subjectCode = !string.IsNullOrWhiteSpace(request.SubjectId) ? new SubjectCode(request.SubjectId) : (SubjectCode?)null;
        var visitId = request.VisitId;
        var formId = request.FormId;
        var fieldName = request.FieldName;

        // Fetch queries
        var queriesQuery = _dbContext.ClinicalQueries
            .AsNoTracking()
            .Where(q => q.Status != QueryStatus.Closed && q.Status != QueryStatus.Cancelled);

        if (studyCode.HasValue)
        {
            queriesQuery = queriesQuery.Where(q => q.StudyId == studyCode.Value);
        }

        if (siteCode.HasValue)
        {
            queriesQuery = queriesQuery.Where(q => q.SiteId == siteCode.Value);
        }

        if (subjectCode.HasValue)
        {
            queriesQuery = queriesQuery.Where(q => q.SubjectId == subjectCode.Value);
        }

        if (!string.IsNullOrWhiteSpace(visitId))
        {
            queriesQuery = queriesQuery.Where(q => q.VisitId == visitId);
        }

        if (!string.IsNullOrWhiteSpace(formId))
        {
            queriesQuery = queriesQuery.Where(q => q.FormId == formId || q.VisitId == visitId);
        }

        if (!string.IsNullOrWhiteSpace(fieldName))
        {
            queriesQuery = queriesQuery.Where(q => q.FieldId == fieldName || q.FieldLink == fieldName || q.FormId == formId);
        }

        var candidateQueries = await queriesQuery.ToListAsync(ct).ConfigureAwait(false);

        // Fetch observations for unverified source data
        var obsQuery = _dbContext.ClinicalObservations
            .AsNoTracking()
            .Where(o => !o.IsSdvVerified || o.IsSdvFlagged);

        if (studyCode.HasValue)
        {
            obsQuery = obsQuery.Where(o => o.StudyId == studyCode.Value);
        }

        if (siteCode.HasValue)
        {
            obsQuery = obsQuery.Where(o => o.SiteId == siteCode.Value);
        }

        if (subjectCode.HasValue)
        {
            obsQuery = obsQuery.Where(o => o.SubjectId == subjectCode.Value);
        }

        if (!string.IsNullOrWhiteSpace(visitId))
        {
            obsQuery = obsQuery.Where(o => o.VisitId == visitId);
        }

        if (!string.IsNullOrWhiteSpace(formId))
        {
            obsQuery = obsQuery.Where(o => o.PageId == formId || o.VisitId == visitId);
        }

        if (!string.IsNullOrWhiteSpace(fieldName))
        {
            obsQuery = obsQuery.Where(o => o.TestCode.Value == fieldName || o.PageId == formId);
        }

        var unverifiedObs = await obsQuery.ToListAsync(ct).ConfigureAwait(false);

        // Fetch unapproved form submissions as unverified source data
        var formsQuery = _dbContext.FormSubmissions
            .AsNoTracking()
            .Where(f => f.Status != FormSubmissionStatus.Approved);

        if (studyCode.HasValue)
        {
            formsQuery = formsQuery.Where(f => f.StudyId == studyCode.Value);
        }

        if (siteCode.HasValue)
        {
            formsQuery = formsQuery.Where(f => f.SiteId == siteCode.Value);
        }

        if (subjectCode.HasValue)
        {
            formsQuery = formsQuery.Where(f => f.SubjectId == subjectCode.Value);
        }

        if (!string.IsNullOrWhiteSpace(visitId))
        {
            formsQuery = formsQuery.Where(f => f.VisitId == visitId);
        }

        if (!string.IsNullOrWhiteSpace(formId))
        {
            formsQuery = formsQuery.Where(f => f.FormId.Value == formId);
        }

        var unverifiedForms = await formsQuery.ToListAsync(ct).ConfigureAwait(false);

        // Discrepancy counts
        int openQueriesCount = candidateQueries.Count(q => q.Status is QueryStatus.Open or QueryStatus.Reopened or QueryStatus.Candidate or QueryStatus.Answered);
        int unansweredQueriesCount = candidateQueries.Count(q => q.Status is QueryStatus.Open or QueryStatus.Reopened);
        int unverifiedSdvCount = unverifiedObs.Count + unverifiedForms.Count;
        int totalDiscrepancies = openQueriesCount + unverifiedSdvCount;

        var items = new List<PreLockDiscrepancyItem>();

        foreach (var q in candidateQueries)
        {
            var isUnanswered = q.Status is QueryStatus.Open or QueryStatus.Reopened;
            items.Add(new PreLockDiscrepancyItem(
                Id: q.Id.Value.ToString(),
                DiscrepancyType: isUnanswered ? "UNANSWERED_QUERY" : "OPEN_QUERY",
                ScopeType: DetermineQueryScopeType(q),
                ScopeId: q.FieldId ?? q.FormId ?? q.VisitId ?? q.SubjectId.Value,
                SubjectId: q.SubjectId.Value,
                VisitId: q.VisitId,
                FormId: q.FormId,
                FieldId: q.FieldId,
                Description: q.Explanation ?? q.Message ?? "Unresolved clinical query.",
                Severity: q.Priority ?? "MEDIUM",
                Status: q.Status.ToString().ToUpperInvariant()
            ));
        }

        foreach (var obs in unverifiedObs)
        {
            items.Add(new PreLockDiscrepancyItem(
                Id: obs.Id.Value.ToString(),
                DiscrepancyType: "UNVERIFIED_SOURCE_DATA",
                ScopeType: DetermineObservationScopeType(obs),
                ScopeId: obs.TestCode.Value ?? obs.PageId ?? obs.SubjectId.Value,
                SubjectId: obs.SubjectId.Value,
                VisitId: obs.VisitId,
                FormId: obs.PageId,
                FieldId: obs.TestCode.Value,
                Description: obs.IsSdvFlagged ? $"SDV Flagged: {obs.SdvFlagReason}" : $"Unverified Observation ({obs.TestName})",
                Severity: obs.IsSdvFlagged ? "HIGH" : "MEDIUM",
                Status: obs.IsSdvFlagged ? "SDV_FLAGGED" : "UNVERIFIED"
            ));
        }

        foreach (var form in unverifiedForms)
        {
            items.Add(new PreLockDiscrepancyItem(
                Id: form.Id.Value.ToString(),
                DiscrepancyType: "UNVERIFIED_SOURCE_DATA",
                ScopeType: "FORM",
                ScopeId: form.FormId.Value,
                SubjectId: form.SubjectId.Value,
                VisitId: form.VisitId,
                FormId: form.FormId.Value,
                FieldId: null,
                Description: $"Form Submission in {form.Status} status pending final sign-off.",
                Severity: "MEDIUM",
                Status: form.Status.ToString().ToUpperInvariant()
            ));
        }

        stopwatch.Stop();
        double elapsedMs = stopwatch.Elapsed.TotalMilliseconds;

        bool hasDiscrepancies = totalDiscrepancies > 0;
        var status = hasDiscrepancies ? "BLOCKED" : "PASSED";
        var canProceed = !hasDiscrepancies;
        var requiresOverride = hasDiscrepancies;

        var summary = hasDiscrepancies
            ? $"Pre-lock validation check failed: {openQueriesCount} open queries ({unansweredQueriesCount} unanswered) and {unverifiedSdvCount} unverified source data records found in target scope ({scopeType}: {scopeId}). Lock execution blocked unless authorized override justification is provided."
            : $"Pre-lock validation check passed: 0 open queries and 0 unverified source data items found across target scope hierarchy ({scopeType}: {scopeId}).";

        return new PreflightCheckResponse(
            ScopeType: scopeType,
            ScopeId: scopeId,
            Status: status,
            CanProceed: canProceed,
            RequiresOverride: requiresOverride,
            Summary: summary,
            DiscrepancyCounts: new DiscrepancyCounts(openQueriesCount, unansweredQueriesCount, unverifiedSdvCount, totalDiscrepancies),
            Discrepancies: items,
            CheckedAt: checkedAt,
            ExecutionTimeMs: elapsedMs
        );
    }

    private static string DetermineQueryScopeType(ClinicalQuery q)
    {
        if (!string.IsNullOrWhiteSpace(q.FieldId))
        {
            return "FIELD";
        }

        if (!string.IsNullOrWhiteSpace(q.FormId))
        {
            return "FORM";
        }

        if (!string.IsNullOrWhiteSpace(q.VisitId))
        {
            return "VISIT";
        }

        return "SUBJECT";
    }

    private static string DetermineObservationScopeType(ClinicalObservation obs)
    {
        if (!string.IsNullOrWhiteSpace(obs.PageId))
        {
            return "FORM";
        }

        if (!string.IsNullOrWhiteSpace(obs.VisitId))
        {
            return "VISIT";
        }

        return "SUBJECT";
    }
}
