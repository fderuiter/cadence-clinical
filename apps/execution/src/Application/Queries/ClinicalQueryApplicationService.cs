using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Apps.Execution.Domain.Queries.Errors;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Contracts;
using Cadence.Core.Domain;
using Cadence.Kernel;
using Cadence.Kernel.Time;

using MassTransit;

namespace Cadence.Apps.Execution.Application.Queries;

/// <summary>
/// Application service implementing clinical discrepancy query workflows, state transitions, and GxP role enforcement.
/// @req:PRD-SYS-007
/// @req:SRS-EDC-004
/// </summary>
public sealed class ClinicalQueryApplicationService(
    IClinicalQueryRepository queryRepository,
    ICurrentAuditContextAccessor auditContextAccessor,
    IClock clock,
    IPublishEndpoint? publishEndpoint = null) : IClinicalQueryApplicationService
{
    public async Task<Result<ClinicalQueryResponse>> CreateAsync(
        QueryCreateRequest request,
        CancellationToken cancellationToken)
    {
        StudyCode studyCode;
        try
        {
            studyCode = new StudyCode(request.StudyId);
        }
        catch (ArgumentException ex)
        {
            return DomainError.Validation("INVALID_STUDY_CODE", ex.Message);
        }

        SubjectCode subjectCode;
        try
        {
            subjectCode = new SubjectCode(request.SubjectId);
        }
        catch (ArgumentException ex)
        {
            return DomainError.Validation("INVALID_SUBJECT_CODE", ex.Message);
        }

        TestCode testCode;
        try
        {
            testCode = new TestCode(request.TestCode);
        }
        catch (ArgumentException ex)
        {
            return DomainError.Validation("INVALID_TEST_CODE", ex.Message);
        }

        SiteCode? siteCode = null;
        if (!string.IsNullOrWhiteSpace(request.SiteId))
        {
            try
            {
                siteCode = new SiteCode(request.SiteId);
            }
            catch (ArgumentException ex)
            {
                return DomainError.Validation("INVALID_SITE_CODE", ex.Message);
            }
        }

        DomainCode? domainCode = null;
        if (!string.IsNullOrWhiteSpace(request.Domain))
        {
            try
            {
                domainCode = new DomainCode(request.Domain);
            }
            catch (ArgumentException ex)
            {
                return DomainError.Validation("INVALID_DOMAIN_CODE", ex.Message);
            }
        }

        ObservationId? observationId = null;
        if (!string.IsNullOrWhiteSpace(request.ObservationId))
        {
            if (!ObservationId.TryParse(request.ObservationId, out var parsedObsId) || parsedObsId.IsEmpty)
            {
                return DomainError.Validation("INVALID_OBSERVATION_ID", $"Observation ID '{request.ObservationId}' is not a valid GUID.");
            }
            observationId = parsedObsId;
        }

        // Check whether active query already exists on this coordinate
        var activeQuery = await queryRepository.GetActiveQueryOnCoordinatesAsync(
            studyCode,
            subjectCode,
            request.VisitId,
            domainCode,
            testCode,
            cancellationToken);

        if (activeQuery is not null)
        {
            var coords = $"{request.StudyId}/{request.SubjectId}/{request.VisitId}/{request.Domain}/{request.TestCode}";
            return QueryErrors.ActiveQueryAlreadyExists(coords);
        }

        var initialStatus = QueryStatus.Open;
        if (!string.IsNullOrWhiteSpace(request.Status) &&
            QueryStatusCodec.TryParse(request.Status, out var parsedStatus))
        {
            initialStatus = parsedStatus;
        }

        var raiseResult = ClinicalQuery.Raise(
            studyId: studyCode,
            subjectId: subjectCode,
            testCode: testCode,
            explanation: request.Explanation,
            initialStatus: initialStatus,
            siteId: siteCode,
            visitId: request.VisitId,
            domain: domainCode,
            observationId: observationId,
            fieldLink: request.FieldLink,
            message: request.Message,
            origin: request.Origin ?? "MANUAL",
            priority: request.Priority ?? "MEDIUM",
            ruleId: request.RuleId,
            formId: request.FormId,
            fieldId: request.FieldId,
            queryType: request.QueryType,
            actionRequired: request.ActionRequired);

        if (raiseResult.IsFailure)
        {
            return raiseResult.Error!;
        }

        var query = raiseResult.Value!;
        query.CreatedBy = auditContextAccessor.UserId;
        query.ReasonForChange = auditContextAccessor.ChangeReason;

        await queryRepository.AddAsync(query, cancellationToken);

        if (publishEndpoint is not null)
        {
            var raisedAt = query.CreatedAt == default ? clock.UtcNow : new DateTimeOffset(DateTime.SpecifyKind(query.CreatedAt, DateTimeKind.Utc));
            await publishEndpoint.Publish(new ClinicalQueryRaised(
                QueryId: query.Id.ToString(),
                StudyId: query.StudyId.Value,
                SubjectId: query.SubjectId.Value,
                SiteId: query.SiteId?.Value,
                TestCode: query.TestCode.Value,
                Explanation: query.Explanation ?? request.Explanation,
                RaisedBy: string.IsNullOrWhiteSpace(query.CreatedBy) ? auditContextAccessor.UserId : query.CreatedBy,
                RaisedAt: raisedAt,
                QueryType: query.QueryType,
                Priority: query.Priority
            ), cancellationToken);
        }

        await queryRepository.SaveChangesAsync(cancellationToken);

        return MapToResponse(query);
    }

    public async Task<Result<ClinicalQueryResponse>> GetByIdAsync(
        string id,
        CancellationToken cancellationToken)
    {
        if (!QueryId.TryParse(id, out var queryId))
        {
            return DomainError.Validation("INVALID_QUERY_ID", $"Query ID '{id}' is not a valid GUID.");
        }

        var query = await queryRepository.GetByIdAsync(queryId, cancellationToken);
        if (query is null)
        {
            return QueryErrors.NotFound(id);
        }

        return MapToResponse(query);
    }

    public async Task<Result<IReadOnlyList<ClinicalQueryResponse>>> ListAsync(
        string? studyId,
        string? subjectId,
        string? visitId,
        string? status,
        CancellationToken cancellationToken)
    {
        StudyCode? studyCode = null;
        if (!string.IsNullOrWhiteSpace(studyId))
        {
            try
            {
                studyCode = new StudyCode(studyId);
            }
            catch (ArgumentException ex)
            {
                return DomainError.Validation("INVALID_STUDY_CODE", ex.Message);
            }
        }

        SubjectCode? subjectCode = null;
        if (!string.IsNullOrWhiteSpace(subjectId))
        {
            try
            {
                subjectCode = new SubjectCode(subjectId);
            }
            catch (ArgumentException ex)
            {
                return DomainError.Validation("INVALID_SUBJECT_CODE", ex.Message);
            }
        }

        QueryStatus? queryStatus = null;
        if (!string.IsNullOrWhiteSpace(status) &&
            QueryStatusCodec.TryParse(status, out var parsedStatus))
        {
            queryStatus = parsedStatus;
        }

        var queries = await queryRepository.ListAsync(
            studyCode,
            subjectCode,
            visitId,
            queryStatus,
            cancellationToken);

        var list = queries.Select(MapToResponse).ToList();
        return list;
    }

    public async Task<Result<ClinicalQueryResponse>> AnswerAsync(
        string id,
        QueryRespondRequest request,
        CancellationToken cancellationToken)
    {
        if (!QueryId.TryParse(id, out var queryId))
        {
            return DomainError.Validation("INVALID_QUERY_ID", $"Query ID '{id}' is not a valid GUID.");
        }

        var query = await queryRepository.GetForUpdateAsync(queryId, cancellationToken);
        if (query is null)
        {
            return QueryErrors.NotFound(id);
        }

        // The responder is the authenticated caller; the request cannot name someone else (#4920).
        var answerResult = query.Answer(request.Response, auditContextAccessor.UserId);
        if (answerResult.IsFailure)
        {
            return answerResult.Error!;
        }

        query.ReasonForChange = auditContextAccessor.ChangeReason;
        await queryRepository.SaveChangesAsync(cancellationToken);

        return MapToResponse(query);
    }

    public async Task<Result<ClinicalQueryResponse>> CloseAsync(
        string id,
        CancellationToken cancellationToken,
        string? userRole = null)
    {
        if (!IsAuthorizedCraOrDm(userRole))
        {
            return QueryErrors.UnauthorizedRole(userRole ?? string.Empty, "close");
        }

        if (!QueryId.TryParse(id, out var queryId))
        {
            return DomainError.Validation("INVALID_QUERY_ID", $"Query ID '{id}' is not a valid GUID.");
        }

        var query = await queryRepository.GetForUpdateAsync(queryId, cancellationToken);
        if (query is null)
        {
            return QueryErrors.NotFound(id);
        }

        // The resolver is the authenticated caller; the request cannot name someone else (#4920).
        var closeResult = query.Close(auditContextAccessor.UserId, clock.UtcNow);
        if (closeResult.IsFailure)
        {
            return closeResult.Error!;
        }

        query.ReasonForChange = auditContextAccessor.ChangeReason;
        await queryRepository.SaveChangesAsync(cancellationToken);

        return MapToResponse(query);
    }

    public async Task<Result<ClinicalQueryResponse>> ReopenAsync(
        string id,
        QueryReopenRequest request,
        CancellationToken cancellationToken,
        string? userRole = null)
    {
        if (!IsAuthorizedCraOrDm(userRole))
        {
            return QueryErrors.UnauthorizedRole(userRole ?? string.Empty, "reopen");
        }

        if (!QueryId.TryParse(id, out var queryId))
        {
            return DomainError.Validation("INVALID_QUERY_ID", $"Query ID '{id}' is not a valid GUID.");
        }

        var query = await queryRepository.GetForUpdateAsync(queryId, cancellationToken);
        if (query is null)
        {
            return QueryErrors.NotFound(id);
        }

        var reopenResult = query.Reopen(request.Reason, auditContextAccessor.UserId);
        if (reopenResult.IsFailure)
        {
            return reopenResult.Error!;
        }

        await queryRepository.SaveChangesAsync(cancellationToken);

        return MapToResponse(query);
    }

    public async Task<Result<ClinicalQueryResponse>> CancelAsync(
        string id,
        QueryCancelRequest request,
        CancellationToken cancellationToken,
        string? userRole = null)
    {
        if (!IsAuthorizedCraOrDm(userRole))
        {
            return QueryErrors.UnauthorizedRole(userRole ?? string.Empty, "cancel");
        }

        if (!QueryId.TryParse(id, out var queryId))
        {
            return DomainError.Validation("INVALID_QUERY_ID", $"Query ID '{id}' is not a valid GUID.");
        }

        var query = await queryRepository.GetForUpdateAsync(queryId, cancellationToken);
        if (query is null)
        {
            return QueryErrors.NotFound(id);
        }

        var cancelResult = query.Cancel(request.Reason, auditContextAccessor.UserId);
        if (cancelResult.IsFailure)
        {
            return cancelResult.Error!;
        }

        await queryRepository.SaveChangesAsync(cancellationToken);

        return MapToResponse(query);
    }

    public async Task<Result<BatchQueryResponse>> BatchCloseAsync(
        BatchQueryRequest request,
        CancellationToken cancellationToken,
        string? userRole = null)
    {
        if (!IsAuthorizedCraOrDm(userRole))
        {
            return QueryErrors.UnauthorizedRole(userRole ?? string.Empty, "batch close");
        }

        if (request.QueryIds is null || request.QueryIds.Count == 0)
        {
            return DomainError.Validation("INVALID_BATCH_REQUEST", "At least one query ID must be provided.");
        }

        if (request.QueryIds.Count > 100)
        {
            return DomainError.Validation("BATCH_CAP_EXCEEDED", "Batch target size exceeds maximum limit of 100 items per transaction.");
        }

        var parsedIds = new List<QueryId>(request.QueryIds.Count);
        foreach (var idStr in request.QueryIds)
        {
            if (!QueryId.TryParse(idStr, out var qId))
            {
                return DomainError.Validation("INVALID_QUERY_ID", $"Query ID '{idStr}' is not a valid GUID.");
            }
            parsedIds.Add(qId);
        }

        var changeReason = !string.IsNullOrWhiteSpace(auditContextAccessor.ChangeReason)
            ? auditContextAccessor.ChangeReason
            : request.Reason;

        if (string.IsNullOrWhiteSpace(changeReason))
        {
            return QueryErrors.ReasonRequired("batch close");
        }

        var queries = await queryRepository.GetForUpdateAsync(parsedIds, cancellationToken).ConfigureAwait(false);
        if (queries.Count != parsedIds.Distinct().Count())
        {
            var missingId = parsedIds.FirstOrDefault(id => !queries.Any(q => q.Id == id));
            return QueryErrors.NotFound(missingId.ToString());
        }

        var queryMap = queries.ToDictionary(q => q.Id);

        foreach (var qId in parsedIds)
        {
            var query = queryMap[qId];
            if (query.Status is not (QueryStatus.Open or QueryStatus.Answered or QueryStatus.Candidate or QueryStatus.Reopened))
            {
                return QueryErrors.InvalidStateTransition(query.Status.ToString(), QueryStatus.Closed.ToString());
            }
        }

        var updatedList = new List<ClinicalQuery>(queries.Count);

        foreach (var qId in parsedIds)
        {
            var query = queryMap[qId];
            var closeResult = query.Close(auditContextAccessor.UserId, clock.UtcNow);
            if (closeResult.IsFailure)
            {
                return closeResult.Error!;
            }
            query.ReasonForChange = changeReason;
            updatedList.Add(query);
        }

        await queryRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var responses = updatedList.Select(MapToResponse).ToList();
        return new BatchQueryResponse(responses, responses.Count);
    }

    public async Task<Result<BatchQueryResponse>> BatchCancelAsync(
        BatchQueryRequest request,
        CancellationToken cancellationToken,
        string? userRole = null)
    {
        if (!IsAuthorizedCraOrDm(userRole))
        {
            return QueryErrors.UnauthorizedRole(userRole ?? string.Empty, "batch cancel");
        }

        if (request.QueryIds is null || request.QueryIds.Count == 0)
        {
            return DomainError.Validation("INVALID_BATCH_REQUEST", "At least one query ID must be provided.");
        }

        if (request.QueryIds.Count > 100)
        {
            return DomainError.Validation("BATCH_CAP_EXCEEDED", "Batch target size exceeds maximum limit of 100 items per transaction.");
        }

        var parsedIds = new List<QueryId>(request.QueryIds.Count);
        foreach (var idStr in request.QueryIds)
        {
            if (!QueryId.TryParse(idStr, out var qId))
            {
                return DomainError.Validation("INVALID_QUERY_ID", $"Query ID '{idStr}' is not a valid GUID.");
            }
            parsedIds.Add(qId);
        }

        var cancelReason = !string.IsNullOrWhiteSpace(request.Reason)
            ? request.Reason
            : auditContextAccessor.ChangeReason;

        if (string.IsNullOrWhiteSpace(cancelReason))
        {
            return QueryErrors.ReasonRequired("cancel");
        }

        var queries = await queryRepository.GetForUpdateAsync(parsedIds, cancellationToken).ConfigureAwait(false);
        if (queries.Count != parsedIds.Distinct().Count())
        {
            var missingId = parsedIds.FirstOrDefault(id => !queries.Any(q => q.Id == id));
            return QueryErrors.NotFound(missingId.ToString());
        }

        var queryMap = queries.ToDictionary(q => q.Id);

        foreach (var qId in parsedIds)
        {
            var query = queryMap[qId];
            if (query.Status is QueryStatus.Closed or QueryStatus.Cancelled)
            {
                return QueryErrors.InvalidStateTransition(query.Status.ToString(), QueryStatus.Cancelled.ToString());
            }
        }

        var updatedList = new List<ClinicalQuery>(queries.Count);

        foreach (var qId in parsedIds)
        {
            var query = queryMap[qId];
            var cancelResult = query.Cancel(cancelReason, auditContextAccessor.UserId);
            if (cancelResult.IsFailure)
            {
                return cancelResult.Error!;
            }
            query.ReasonForChange = !string.IsNullOrWhiteSpace(auditContextAccessor.ChangeReason)
                ? auditContextAccessor.ChangeReason
                : cancelReason;
            updatedList.Add(query);
        }

        await queryRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var responses = updatedList.Select(MapToResponse).ToList();
        return new BatchQueryResponse(responses, responses.Count);
    }

    public async Task<Result<BatchQueryResponse>> BatchRespondAsync(
        BatchQueryRespondRequest request,
        CancellationToken cancellationToken)
    {
        if (request.QueryIds is null || request.QueryIds.Count == 0)
        {
            return DomainError.Validation("INVALID_BATCH_REQUEST", "At least one query ID must be provided.");
        }

        if (request.QueryIds.Count > 100)
        {
            return DomainError.Validation("BATCH_CAP_EXCEEDED", "Batch target size exceeds maximum limit of 100 items per transaction.");
        }

        if (string.IsNullOrWhiteSpace(request.Response))
        {
            return QueryErrors.ResponseRequired();
        }

        var parsedIds = new List<QueryId>(request.QueryIds.Count);
        foreach (var idStr in request.QueryIds)
        {
            if (!QueryId.TryParse(idStr, out var qId))
            {
                return DomainError.Validation("INVALID_QUERY_ID", $"Query ID '{idStr}' is not a valid GUID.");
            }
            parsedIds.Add(qId);
        }

        var changeReason = !string.IsNullOrWhiteSpace(auditContextAccessor.ChangeReason)
            ? auditContextAccessor.ChangeReason
            : request.Reason;

        var queries = await queryRepository.GetForUpdateAsync(parsedIds, cancellationToken).ConfigureAwait(false);
        if (queries.Count != parsedIds.Distinct().Count())
        {
            var missingId = parsedIds.FirstOrDefault(id => !queries.Any(q => q.Id == id));
            return QueryErrors.NotFound(missingId.ToString());
        }

        var queryMap = queries.ToDictionary(q => q.Id);

        foreach (var qId in parsedIds)
        {
            var query = queryMap[qId];
            if (query.Status is not (QueryStatus.Open or QueryStatus.Reopened))
            {
                return QueryErrors.InvalidStateTransition(query.Status.ToString(), QueryStatus.Answered.ToString());
            }
        }

        var updatedList = new List<ClinicalQuery>(queries.Count);

        foreach (var qId in parsedIds)
        {
            var query = queryMap[qId];
            var answerResult = query.Answer(request.Response, auditContextAccessor.UserId);
            if (answerResult.IsFailure)
            {
                return answerResult.Error!;
            }

            if (!string.IsNullOrWhiteSpace(changeReason))
            {
                query.ReasonForChange = changeReason;
            }

            updatedList.Add(query);
        }

        await queryRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var responses = updatedList.Select(MapToResponse).ToList();
        return new BatchQueryResponse(responses, responses.Count);
    }

    /// <summary>Whether <paramref name="role"/> may close, reopen or cancel a query (CRA or data manager).</summary>
    public static bool CanManageQueryLifecycle(string role) => IsAuthorizedCraOrDm(role);

    private static bool IsAuthorizedCraOrDm(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        var normalized = role.Trim().ToLowerInvariant();
        return normalized is "cra" or "data_manager" or "data manager" or "datamanager" or "data-manager" or "dm" or "sponsor_dm" or "sponsor dm" or "sponsor-dm" or "admin";
    }

    private static ClinicalQueryResponse MapToResponse(ClinicalQuery q) =>
        new(
            Id: q.Id.ToString(),
            StudyId: q.StudyId.Value,
            SubjectId: q.SubjectId.Value,
            TestCode: q.TestCode.Value,
            Status: q.Status,
            SiteId: q.SiteId?.Value,
            VisitId: q.VisitId,
            Domain: q.Domain?.Value,
            Explanation: q.Explanation,
            Response: q.Response,
            ObservationId: q.ObservationId?.ToString(),
            FieldLink: q.FieldLink,
            Message: q.Message,
            Origin: q.Origin,
            Priority: q.Priority,
            RuleId: q.RuleId,
            CreatedBy: q.CreatedBy,
            Responder: q.Responder,
            Resolver: q.Resolver,
            ResolvedAt: q.ResolvedAt?.UtcDateTime,
            CancellationReason: q.CancellationReason,
            EscalatedAt: q.EscalatedAt?.UtcDateTime,
            FormId: q.FormId,
            FieldId: q.FieldId,
            QueryType: q.QueryType,
            ActionRequired: q.ActionRequired,
            CreatedAt: q.CreatedAt,
            Version: q.VersionIndex);
}
