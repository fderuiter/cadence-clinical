using System;

using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Queries.Errors;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Core.Domain;
using Cadence.Kernel;

namespace Cadence.Apps.Execution.Domain.Queries;

/// <summary>
/// Aggregate root representing a clinical query for GxP data discrepancy tracking.
/// Implements full 6-state discrepancy lifecycle (Candidate, Open, Answered, Resolved/Closed, Reopened, Cancelled).
/// @req:PRD-SYS-007
/// @req:PRD-SYS-053
/// @req:SRS-EDC-004
/// </summary>
public sealed class ClinicalQuery : AggregateRoot<QueryId>
{
    public StudyCode StudyId { get; private set; }
    public SiteCode? SiteId { get; private set; }
    public SubjectCode SubjectId { get; private set; }
    public string? VisitId { get; private set; }
    public DomainCode? Domain { get; private set; }
    public TestCode TestCode { get; private set; }
    public QueryStatus Status { get; private set; }
    public string? Explanation { get; private set; }
    public string? Response { get; private set; }
    public ObservationId? ObservationId { get; private set; }
    public string? FieldLink { get; private set; }
    public string? Message { get; private set; }
    public string? Origin { get; private set; }
    public string? Priority { get; private set; }
    public string? RuleId { get; private set; }
    public string? Responder { get; private set; }
    public string? Resolver { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTimeOffset? EscalatedAt { get; private set; }
    public string? FormId { get; private set; }
    public string? FieldId { get; private set; }
    public string? QueryType { get; private set; }
    public string? ActionRequired { get; private set; }

    private ClinicalQuery() { } // EF Core

    public static Result<ClinicalQuery> Raise(
        StudyCode studyId,
        SubjectCode subjectId,
        TestCode testCode,
        string explanation,
        QueryStatus initialStatus = QueryStatus.Open,
        SiteCode? siteId = null,
        string? visitId = null,
        DomainCode? domain = null,
        ObservationId? observationId = null,
        string? fieldLink = null,
        string? message = null,
        string? origin = "MANUAL",
        string? priority = "MEDIUM",
        string? ruleId = null,
        string? formId = null,
        string? fieldId = null,
        string? queryType = null,
        string? actionRequired = null)
    {
        if (initialStatus is not (QueryStatus.Candidate or QueryStatus.Open))
        {
            return QueryErrors.InvalidStateTransition("INITIAL", initialStatus.ToString());
        }

        var query = new ClinicalQuery
        {
            Id = QueryId.New(),
            StudyId = studyId,
            SubjectId = subjectId,
            TestCode = testCode,
            Explanation = explanation,
            Status = initialStatus,
            SiteId = siteId,
            VisitId = visitId,
            Domain = domain,
            ObservationId = observationId,
            FieldLink = fieldLink,
            Message = message ?? explanation,
            Origin = origin,
            Priority = priority,
            RuleId = ruleId,
            FormId = formId,
            FieldId = fieldId,
            QueryType = queryType,
            ActionRequired = actionRequired
        };

        return query;
    }

    public Result<bool> Open(string openedBy)
    {
        if (Status != QueryStatus.Candidate && Status != QueryStatus.None)
        {
            return QueryErrors.InvalidStateTransition(Status.ToString(), QueryStatus.Open.ToString());
        }

        Status = QueryStatus.Open;
        CreatedBy = openedBy;
        return true;
    }

    public Result<bool> Answer(string responseText, string responderUserId)
    {
        if (Status != QueryStatus.Open && Status != QueryStatus.Reopened)
        {
            return QueryErrors.InvalidStateTransition(Status.ToString(), QueryStatus.Answered.ToString());
        }

        if (string.IsNullOrWhiteSpace(responseText))
        {
            return QueryErrors.ResponseRequired();
        }

        Status = QueryStatus.Answered;
        Response = responseText.Trim();
        Responder = responderUserId;
        return true;
    }

    public Result<bool> Close(string resolverUserId, DateTimeOffset resolvedAt)
    {
        if (Status != QueryStatus.Open && Status != QueryStatus.Answered && Status != QueryStatus.Candidate && Status != QueryStatus.Reopened)
        {
            return QueryErrors.InvalidStateTransition(Status.ToString(), QueryStatus.Closed.ToString());
        }

        if (string.IsNullOrWhiteSpace(resolverUserId))
        {
            return QueryErrors.ResolverRequired();
        }

        Status = QueryStatus.Closed;
        Resolver = resolverUserId;
        ResolvedAt = resolvedAt.ToUniversalTime();
        return true;
    }

    public Result<bool> Reopen(string reason, string reopenedBy)
    {
        if (Status != QueryStatus.Answered && Status != QueryStatus.Closed)
        {
            return QueryErrors.InvalidStateTransition(Status.ToString(), QueryStatus.Reopened.ToString());
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return QueryErrors.ReasonRequired("reopen");
        }

        Status = QueryStatus.Reopened;
        Explanation = reason.Trim();
        Resolver = null;
        ResolvedAt = null;
        ReasonForChange = reason.Trim();
        return true;
    }

    public Result<bool> Cancel(string reason, string cancelledBy)
    {
        if (Status is QueryStatus.Closed or QueryStatus.Cancelled)
        {
            return QueryErrors.InvalidStateTransition(Status.ToString(), QueryStatus.Cancelled.ToString());
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return QueryErrors.ReasonRequired("cancel");
        }

        Status = QueryStatus.Cancelled;
        CancellationReason = reason.Trim();
        ReasonForChange = reason.Trim();
        return true;
    }

    public void UpdateObservationLink(ObservationId? observationId, DomainCode? domain = null)
    {
        ObservationId = observationId;
        if (domain.HasValue)
        {
            Domain = domain;
        }
    }

    public void Escalate(DateTimeOffset escalatedAt)
    {
        EscalatedAt = escalatedAt.ToUniversalTime();
    }
}
