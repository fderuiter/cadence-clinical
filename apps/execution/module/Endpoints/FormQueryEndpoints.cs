using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Domain;
using Cadence.Modules.Execution.Infrastructure;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Modules.Execution.Endpoints;

/// <summary>
/// Data queries about eCRF values (#5461): raise a query on the current revision of a form's value or on a blank item,
/// list a form's, an enrollment's or a study's visible queries with whether the value changed since, and answer, reopen,
/// close or cancel one. Every write is a command sent through <see cref="IKernelCommandSender"/>, so the kernel authorizes
/// it against the caller's grants and audits it; there are no batch routes, so a client acting on several queries sends
/// one command each.
/// </summary>
internal static class FormQueryEndpoints
{
    internal const int DefaultQueryLimit = 500;
    internal const int MaxQueryLimit = 1000;

    public static IEndpointRouteBuilder MapFormQueryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(FormEndpoints.RoutePrefix)
            .RequireSignedInCaller("Every read checks the caller may see the enrollment; every write is a module command authorized against the caller's grants.")
            .AddEndpointFilter<KernelTenantFilter>()
            .WithTags("Execution queries");

        group.MapGet("/queries", ListQueriesAsync)
            .WithName("ExecutionListEnrollmentQueries")
            .WithSummary("Lists the queries on every form of an enrollment or across visible enrollments of a study, oldest first.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{formId:guid}/queries", ListForFormAsync)
            .WithName("ExecutionListFormQueries")
            .WithSummary("Lists a form's queries, oldest first, each with whether its value changed since it was raised.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{formId:guid}/queries", RaiseAsync)
            .WithName("ExecutionRaiseFormQuery")
            .WithSummary("Raises a query on the current revision of one of the form's values or on a blank item (execution.query.raise).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesKernelCommand<RecordUnaskedCollectionItem>()
            .ProducesKernelCommand<RaiseFormQuery>();

        group.MapPost("/{formId:guid}/queries/{queryId:guid}/answer", AnswerAsync)
            .WithName("ExecutionAnswerFormQuery")
            .WithSummary("Answers an open query (execution.query.answer).")
            .ProducesKernelCommand<AnswerFormQuery>();

        group.MapPost("/{formId:guid}/queries/{queryId:guid}/reopen", ReopenAsync)
            .WithName("ExecutionReopenFormQuery")
            .WithSummary("Reopens an answered or closed query with why it is not settled (execution.query.manage).")
            .ProducesKernelCommand<ReopenFormQuery>();

        group.MapPost("/{formId:guid}/queries/{queryId:guid}/close", CloseAsync)
            .WithName("ExecutionCloseFormQuery")
            .WithSummary("Closes an open or answered query; the change reason is the closing note (execution.query.manage).")
            .ProducesKernelCommand<CloseFormQuery>();

        group.MapPost("/{formId:guid}/queries/{queryId:guid}/cancel", CancelAsync)
            .WithName("ExecutionCancelFormQuery")
            .WithSummary("Cancels an open or answered query raised in error (execution.query.manage).")
            .ProducesKernelCommand<CancelFormQuery>();

        return endpoints;
    }

    internal static async Task<Results<Ok<IReadOnlyList<FormQueryResponse>>, ProblemHttpResult>> ListForFormAsync(
        Guid formId,
        HttpContext context,
        ICollectionInstanceRecords forms,
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        IKernelReadAccess access,
        IKernelNavigator navigator,
        CancellationToken cancellationToken)
    {
        var tenant = KernelRequestTenant.Get(context);
        if (await forms.FindAsync(tenant, CollectionInstanceId.FromGuid(formId), cancellationToken).ConfigureAwait(false) is not { } record)
        {
            return FormNotFound(formId);
        }

        // Queries are seen by whoever may see the form's enrollment, as its values are (ADR-2457).
        if (!await access.CanSeeAsync(tenant, NodeRef.For(record.EnrollmentId), cancellationToken).ConfigureAwait(false))
        {
            return NotVisible($"form {formId:D}");
        }

        var form = CollectionInstanceId.FromGuid(formId);
        var queries = await ReadAsync(contexts, tenant, q => q.FormId == form, cancellationToken).ConfigureAwait(false);
        var canUnblind = await access.CanUnblindAsync(tenant, record.EnrollmentId, cancellationToken).ConfigureAwait(false);
        var revisions = Revisions(record);
        var queryContext = await BuildFormContextAsync(navigator, tenant, record, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>([
            .. queries.Select(q => FormQueryResponse.From(
                q,
                revisions,
                queryContext,
                maskBlinded: !canUnblind && record.IsQueryBlinded(q.ItemId, q.ObservationId))),
        ]);
    }

    internal static async Task<Results<Ok<IReadOnlyList<FormQueryResponse>>, ProblemHttpResult>> ListQueriesAsync(
        [AsParameters] FormQueryListFilter filter,
        HttpContext context,
        ICollectionInstanceRecords forms,
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        IKernelReadAccess access,
        IKernelNavigator navigator,
        CancellationToken cancellationToken)
    {
        var hasEnrollment = filter.EnrollmentId is { } enrollmentGuid && enrollmentGuid != Guid.Empty;
        var hasStudy = filter.StudyId is { } studyGuid && studyGuid != Guid.Empty;
        if (!hasEnrollment && !hasStudy)
        {
            return BadRequest("Name the enrollment with enrollment_id or the study with study_id.");
        }

        if (hasEnrollment && hasStudy)
        {
            return BadRequest("Specify either enrollment_id or study_id, not both.");
        }

        var limit = filter.Limit ?? DefaultQueryLimit;
        if (limit is <= 0 or > MaxQueryLimit)
        {
            return BadRequest($"limit must be between 1 and {MaxQueryLimit}.");
        }

        var offset = filter.Offset ?? 0;
        if (offset < 0)
        {
            return BadRequest("offset must be zero or greater.");
        }

        FormQueryStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            if (!Enum.TryParse<FormQueryStatus>(filter.Status.Trim(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return BadRequest($"'{filter.Status}' is not a query status: open, answered, closed or cancelled.");
            }

            statusFilter = parsed;
        }

        var tenant = KernelRequestTenant.Get(context);
        return hasEnrollment
            ? await ListByEnrollmentAsync(EnrollmentId.FromGuid(filter.EnrollmentId!.Value), statusFilter, filter, limit, offset, tenant, forms, contexts, access, navigator, cancellationToken).ConfigureAwait(false)
            : await ListByStudyAsync(StudyId.FromGuid(filter.StudyId!.Value), statusFilter, filter, limit, offset, tenant, forms, contexts, access, navigator, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<Results<Created<FormQueryResponse>, ProblemHttpResult>> RaiseAsync(
        Guid formId,
        RaiseFormQueryRequest request,
        HttpContext context,
        ICollectionInstanceRecords forms,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IKernelReadAccess access,
        IKernelNavigator navigator,
        CancellationToken cancellationToken)
    {
        var tenant = KernelRequestTenant.Get(context);
        var form = CollectionInstanceId.FromGuid(formId);

        // The query is authorized against the form's enrollment, which the client does not send.
        if (await forms.FindAsync(tenant, form, cancellationToken).ConfigureAwait(false) is not { } record)
        {
            return FormNotFound(formId);
        }

        var key = FormEndpoints.IdempotencyKey(context);
        var reason = changeReason.Read(context) ?? string.Empty;
        ObservationId targetObservation;
        int targetRevision;

        if ((request?.ObservationId is null || request.ObservationId == Guid.Empty) && !string.IsNullOrWhiteSpace(request?.ItemId))
        {
            var itemId = request.ItemId.Trim();
            if (record.Values.FirstOrDefault(v => string.Equals(v.ItemId, itemId, StringComparison.Ordinal)) is { } existing)
            {
                if (existing.Value is not MissingValue { Reason: NullFlavor.NotAsked })
                {
                    return KernelErrorEndpoints.Problem(
                        FormQueryErrors.RevisionStale.WithMessage(
                            $"Item {itemId} now holds a value at revision {existing.Revision}; reload the form and query the current value."));
                }

                targetObservation = existing.ObservationId;
                targetRevision = existing.Revision;
            }
            else
            {
                var unaskedKey = string.IsNullOrWhiteSpace(key) ? string.Empty : $"{key}:unasked";
                var unasked = await sender.SendAsync(
                    new RecordUnaskedCollectionItem(tenant, form, record.EnrollmentId, itemId, ObservationId: null, unaskedKey, reason),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (unasked.Value is not { } seeded)
                {
                    return KernelErrorEndpoints.Problem(unasked.Error!);
                }

                targetObservation = seeded.ObservationId;
                targetRevision = seeded.Revision;
            }
        }
        else
        {
            targetObservation = ObservationId.FromGuid(request?.ObservationId ?? Guid.Empty);
            targetRevision = request?.Revision ?? 0;
        }

        var result = await sender.SendAsync(
            new RaiseFormQuery(
                tenant,
                request?.QueryId is { } given && given != Guid.Empty ? given : Guid.CreateVersion7(),
                form,
                record.EnrollmentId,
                targetObservation,
                targetRevision,
                request?.Text ?? string.Empty,
                key,
                reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.Value is not { } state)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var response = await RespondAsync(forms, navigator, access, tenant, state, cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"{FormEndpoints.RoutePrefix}/{formId:D}/queries/{state.QueryId:D}", response);
    }

    internal static Task<Results<Ok<FormQueryResponse>, ProblemHttpResult>> AnswerAsync(
        Guid formId, Guid queryId, FormQueryTextRequest request, HttpContext context, [AsParameters] FormQueryCommandServices services, CancellationToken cancellationToken) =>
        StepAsync(formId, queryId, context, services, (tenant, enrollment, key, reason) =>
            new AnswerFormQuery(tenant, queryId, enrollment, request?.Text ?? string.Empty, key, reason), cancellationToken);

    internal static Task<Results<Ok<FormQueryResponse>, ProblemHttpResult>> ReopenAsync(
        Guid formId, Guid queryId, FormQueryTextRequest request, HttpContext context, [AsParameters] FormQueryCommandServices services, CancellationToken cancellationToken) =>
        StepAsync(formId, queryId, context, services, (tenant, enrollment, key, reason) =>
            new ReopenFormQuery(tenant, queryId, enrollment, request?.Text ?? string.Empty, key, reason), cancellationToken);

    internal static Task<Results<Ok<FormQueryResponse>, ProblemHttpResult>> CloseAsync(
        Guid formId, Guid queryId, HttpContext context, [AsParameters] FormQueryCommandServices services, CancellationToken cancellationToken) =>
        StepAsync(formId, queryId, context, services, (tenant, enrollment, key, reason) =>
            new CloseFormQuery(tenant, queryId, enrollment, key, reason), cancellationToken);

    internal static Task<Results<Ok<FormQueryResponse>, ProblemHttpResult>> CancelAsync(
        Guid formId, Guid queryId, HttpContext context, [AsParameters] FormQueryCommandServices services, CancellationToken cancellationToken) =>
        StepAsync(formId, queryId, context, services, (tenant, enrollment, key, reason) =>
            new CancelFormQuery(tenant, queryId, enrollment, key, reason), cancellationToken);

    private static async Task<Results<Ok<IReadOnlyList<FormQueryResponse>>, ProblemHttpResult>> ListByEnrollmentAsync(
        EnrollmentId enrollment,
        FormQueryStatus? statusFilter,
        FormQueryListFilter filter,
        int limit,
        int offset,
        TenantId tenant,
        ICollectionInstanceRecords forms,
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        IKernelReadAccess access,
        IKernelNavigator navigator,
        CancellationToken cancellationToken)
    {
        if (!await access.CanSeeAsync(tenant, NodeRef.For(enrollment), cancellationToken).ConfigureAwait(false))
        {
            return NotVisible($"enrollment {enrollment.Value:D}");
        }

        var hasContextFilter = !string.IsNullOrWhiteSpace(filter.Site) || !string.IsNullOrWhiteSpace(filter.Participant);
        if (!hasContextFilter && filter.ValueChanged is null)
        {
            var pagedQueries = statusFilter is { } wantedStatus
                ? await ReadPageAsync(contexts, tenant, q => q.EnrollmentId == enrollment && q.Status == wantedStatus, offset, limit, cancellationToken).ConfigureAwait(false)
                : await ReadPageAsync(contexts, tenant, q => q.EnrollmentId == enrollment, offset, limit, cancellationToken).ConfigureAwait(false);
            var pagedResponses = await ShapeQueriesAsync(pagedQueries, null, null, tenant, forms, access, navigator, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>(pagedResponses);
        }

        var queries = statusFilter is { } wanted
            ? await ReadAsync(contexts, tenant, q => q.EnrollmentId == enrollment && q.Status == wanted, cancellationToken).ConfigureAwait(false)
            : await ReadAsync(contexts, tenant, q => q.EnrollmentId == enrollment, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<ObservationId, int>? preloadedRevisions = null;
        if (filter.ValueChanged is { } wantedChanged && !hasContextFilter)
        {
            preloadedRevisions = await forms.FindObservationRevisionsAsync(
                tenant,
                queries.Select(q => q.ObservationId).ToList(),
                queries.Select(q => q.FormId).ToList(),
                cancellationToken).ConfigureAwait(false);
            var filteredQueries = queries
                .Where(q => IsValueChanged(q, preloadedRevisions) == wantedChanged)
                .Skip(offset)
                .Take(limit)
                .ToList();
            var filteredResponses = await ShapeQueriesAsync(filteredQueries, null, preloadedRevisions, tenant, forms, access, navigator, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>(filteredResponses);
        }

        var responses = await ShapeQueriesAsync(queries, null, null, tenant, forms, access, navigator, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ApplyPostFilters(responses, filter, offset, limit));
    }

    private static async Task<Results<Ok<IReadOnlyList<FormQueryResponse>>, ProblemHttpResult>> ListByStudyAsync(
        StudyId study,
        FormQueryStatus? statusFilter,
        FormQueryListFilter filter,
        int limit,
        int offset,
        TenantId tenant,
        ICollectionInstanceRecords forms,
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        IKernelReadAccess access,
        IKernelNavigator navigator,
        CancellationToken cancellationToken)
    {
        var studyEnrollments = await navigator.GetStudyEnrollmentsAsync(tenant, study, cancellationToken).ConfigureAwait(false);
        if (studyEnrollments is null)
        {
            return TypedResults.Problem(title: "Not found", detail: $"Study {study.Value:D} does not exist.", statusCode: StatusCodes.Status404NotFound);
        }

        var studyNode = NodeRef.For(study);
        List<NodeRef> nodesToCheck = [studyNode, .. studyEnrollments.Select(e => NodeRef.For(e.EnrollmentId))];
        var visibleNodes = await access.FilterVisibleAsync(tenant, nodesToCheck, cancellationToken).ConfigureAwait(false);
        if (!visibleNodes.Contains(studyNode))
        {
            return NotVisible($"study {study.Value:D}");
        }

        var visibleEnrollments = new Dictionary<EnrollmentId, EnrollmentHeader>();
        foreach (var member in studyEnrollments)
        {
            if (!visibleNodes.Contains(NodeRef.For(member.EnrollmentId)))
            {
                continue;
            }

            var header = new EnrollmentHeader(study.Value, member.StudySiteId?.Value, member.SiteCode, member.SubjectCode);
            if (!MatchesSiteFilter(header, filter.Site) || !MatchesParticipantFilter(member.EnrollmentId, header, filter.Participant))
            {
                continue;
            }

            visibleEnrollments[member.EnrollmentId] = header;
        }

        if (visibleEnrollments.Count == 0)
        {
            return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>([]);
        }

        var allowed = visibleEnrollments.Keys.ToList();
        if (filter.ValueChanged is { } wantedChanged)
        {
            var candidates = statusFilter is { } wantedStatus
                ? await ReadAsync(contexts, tenant, q => allowed.Contains(q.EnrollmentId) && q.Status == wantedStatus, cancellationToken).ConfigureAwait(false)
                : await ReadAsync(contexts, tenant, q => allowed.Contains(q.EnrollmentId), cancellationToken).ConfigureAwait(false);
            if (candidates.Count == 0)
            {
                return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>([]);
            }

            var preloadedRevisions = await forms.FindObservationRevisionsAsync(
                tenant,
                candidates.Select(q => q.ObservationId).ToList(),
                candidates.Select(q => q.FormId).ToList(),
                cancellationToken).ConfigureAwait(false);
            var pagedCandidates = candidates
                .Where(q => IsValueChanged(q, preloadedRevisions) == wantedChanged)
                .Skip(offset)
                .Take(limit)
                .ToList();
            var changedResponses = await ShapeQueriesAsync(
                pagedCandidates, visibleEnrollments, preloadedRevisions, tenant, forms, access, navigator, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>(changedResponses);
        }

        var queries = statusFilter is { } wanted
            ? await ReadPageAsync(contexts, tenant, q => allowed.Contains(q.EnrollmentId) && q.Status == wanted, offset, limit, cancellationToken).ConfigureAwait(false)
            : await ReadPageAsync(contexts, tenant, q => allowed.Contains(q.EnrollmentId), offset, limit, cancellationToken).ConfigureAwait(false);
        var responses = await ShapeQueriesAsync(queries, visibleEnrollments, null, tenant, forms, access, navigator, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>(responses);
    }

    private static async Task<List<FormQueryResponse>> ShapeQueriesAsync(
        IReadOnlyList<FormQuery> queries,
        IReadOnlyDictionary<EnrollmentId, EnrollmentHeader>? knownHeaders,
        IReadOnlyDictionary<ObservationId, int>? preloadedRevisions,
        TenantId tenant,
        ICollectionInstanceRecords forms,
        IKernelReadAccess access,
        IKernelNavigator navigator,
        CancellationToken cancellationToken)
    {
        if (queries.Count == 0)
        {
            return [];
        }

        var distinctFormIds = queries.Select(q => q.FormId).Distinct().ToList();
        var recordsById = await forms.FindManyAsync(tenant, distinctFormIds, cancellationToken).ConfigureAwait(false);

        var revisions = preloadedRevisions is not null
            ? new Dictionary<ObservationId, int>(preloadedRevisions)
            : new Dictionary<ObservationId, int>();
        var unresolvedNodes = new List<NodeRef>();
        foreach (var record in recordsById.Values)
        {
            foreach (var (observation, revision) in Revisions(record))
            {
                revisions[observation] = revision;
            }

            if (knownHeaders is null || !knownHeaders.ContainsKey(record.EnrollmentId))
            {
                if (!record.StudySiteId.IsEmpty)
                {
                    unresolvedNodes.Add(NodeRef.For(record.StudySiteId));
                }

                unresolvedNodes.Add(NodeRef.For(record.EnrollmentId));
            }
        }

        var resolvedSummaries = unresolvedNodes.Count > 0
            ? await navigator.ResolveManyAsync(tenant, unresolvedNodes, cancellationToken).ConfigureAwait(false)
            : new Dictionary<NodeRef, NodeSummary>();

        var formContexts = new Dictionary<CollectionInstanceId, FormQueryContext>(recordsById.Count);
        foreach (var (formId, record) in recordsById)
        {
            Guid? studySiteId = record.StudySiteId.IsEmpty ? null : record.StudySiteId.Value;
            if (knownHeaders is not null && knownHeaders.TryGetValue(record.EnrollmentId, out var header))
            {
                formContexts[formId] = new FormQueryContext(
                    record.StudyId.Value,
                    studySiteId ?? header.StudySiteId,
                    header.SiteCode,
                    header.SubjectCode,
                    record.CollectionSpecId,
                    record.PlannedActivityId);
            }
            else
            {
                var siteSummary = record.StudySiteId.IsEmpty
                    ? null
                    : resolvedSummaries.GetValueOrDefault(NodeRef.For(record.StudySiteId));
                var enrollmentSummary = resolvedSummaries.GetValueOrDefault(NodeRef.For(record.EnrollmentId));
                formContexts[formId] = new FormQueryContext(
                    record.StudyId.Value,
                    studySiteId,
                    siteSummary?.Code ?? siteSummary?.Title,
                    enrollmentSummary?.Code ?? enrollmentSummary?.Title,
                    record.CollectionSpecId,
                    record.PlannedActivityId);
            }
        }

        var canUnblindByEnrollment = new Dictionary<EnrollmentId, bool>();
        foreach (var enrollmentId in queries.Select(q => q.EnrollmentId).Distinct())
        {
            canUnblindByEnrollment[enrollmentId] = await access.CanUnblindAsync(tenant, enrollmentId, cancellationToken).ConfigureAwait(false);
        }

        return [
            .. queries.Select(q =>
            {
                var maskBlinded = !canUnblindByEnrollment[q.EnrollmentId]
                    && recordsById.TryGetValue(q.FormId, out var rec)
                    && rec.IsQueryBlinded(q.ItemId, q.ObservationId);
                return FormQueryResponse.From(q, revisions, formContexts.GetValueOrDefault(q.FormId), maskBlinded);
            }),
        ];
    }

    private static bool IsValueChanged(FormQuery query, IReadOnlyDictionary<ObservationId, int> revisions) =>
        revisions.TryGetValue(query.ObservationId, out var currentRevision) && currentRevision > query.Revision;

    private static bool MatchesSiteFilter(EnrollmentHeader header, string? rawSite)
    {
        if (string.IsNullOrWhiteSpace(rawSite))
        {
            return true;
        }

        var site = rawSite.Trim();
        return string.Equals(header.SiteCode, site, StringComparison.OrdinalIgnoreCase)
            || (header.StudySiteId is { } siteId && string.Equals(siteId.ToString("D"), site, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesParticipantFilter(EnrollmentId enrollmentId, EnrollmentHeader header, string? rawParticipant)
    {
        if (string.IsNullOrWhiteSpace(rawParticipant))
        {
            return true;
        }

        var participant = rawParticipant.Trim();
        return (header.SubjectCode is not null && header.SubjectCode.Contains(participant, StringComparison.OrdinalIgnoreCase))
            || string.Equals(enrollmentId.Value.ToString("D"), participant, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<FormQueryResponse> ApplyPostFilters(
        IReadOnlyList<FormQueryResponse> responses,
        FormQueryListFilter filter,
        int offset,
        int limit)
    {
        IEnumerable<FormQueryResponse> filtered = responses;
        if (!string.IsNullOrWhiteSpace(filter.Site))
        {
            var site = filter.Site.Trim();
            filtered = filtered.Where(q =>
                string.Equals(q.SiteCode, site, StringComparison.OrdinalIgnoreCase)
                || (q.StudySiteId is { } siteId && string.Equals(siteId.ToString("D"), site, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(filter.Participant))
        {
            var participant = filter.Participant.Trim();
            filtered = filtered.Where(q =>
                (q.SubjectCode is not null && q.SubjectCode.Contains(participant, StringComparison.OrdinalIgnoreCase))
                || string.Equals(q.EnrollmentId.ToString("D"), participant, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.ValueChanged is { } valueChanged)
        {
            filtered = filtered.Where(q => q.ValueChanged == valueChanged);
        }

        return [.. filtered.Skip(offset).Take(limit)];
    }

    private static async Task<FormQueryContext> BuildFormContextAsync(
        IKernelNavigator navigator,
        TenantId tenant,
        CollectionInstanceRecord record,
        CancellationToken cancellationToken)
    {
        var siteSummary = await navigator.ResolveAsync(tenant, NodeRef.For(record.StudySiteId), cancellationToken).ConfigureAwait(false);
        var enrollmentSummary = await navigator.ResolveAsync(tenant, NodeRef.For(record.EnrollmentId), cancellationToken).ConfigureAwait(false);
        return new FormQueryContext(
            record.StudyId.Value,
            record.StudySiteId.Value,
            siteSummary?.Code ?? siteSummary?.Title,
            enrollmentSummary?.Code ?? enrollmentSummary?.Title,
            record.CollectionSpecId,
            record.PlannedActivityId);
    }

    /// <summary>
    /// Finds the query on the form to authorize the step against its enrollment, sends the command built by
    /// <paramref name="command"/>, and answers with the query as it now stands.
    /// </summary>
    private static async Task<Results<Ok<FormQueryResponse>, ProblemHttpResult>> StepAsync(
        Guid formId,
        Guid queryId,
        HttpContext context,
        FormQueryCommandServices services,
        Func<TenantId, EnrollmentId, string, string, ICommand<FormQueryState>> command,
        CancellationToken cancellationToken)
    {
        var tenant = KernelRequestTenant.Get(context);
        var form = CollectionInstanceId.FromGuid(formId);
        var query = (await ReadAsync(services.Contexts, tenant, q => q.Id == queryId && q.FormId == form, cancellationToken).ConfigureAwait(false))
            .SingleOrDefault();
        if (query is null)
        {
            return TypedResults.Problem(
                title: "Not found", detail: $"Form {formId:D} has no query {queryId:D}.", statusCode: StatusCodes.Status404NotFound);
        }

        var result = await services.Sender.SendAsync(
            command(tenant, query.EnrollmentId, FormEndpoints.IdempotencyKey(context), services.ChangeReason.Read(context) ?? string.Empty),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Value is { } state
            ? TypedResults.Ok(await RespondAsync(services.Forms, services.Navigator, services.Access, tenant, state, cancellationToken).ConfigureAwait(false))
            : KernelErrorEndpoints.Problem(result.Error!);
    }

    private static async Task<FormQueryResponse> RespondAsync(
        ICollectionInstanceRecords forms,
        IKernelNavigator navigator,
        IKernelReadAccess access,
        TenantId tenant,
        FormQueryState state,
        CancellationToken cancellationToken)
    {
        var record = await forms.FindAsync(tenant, state.FormId, cancellationToken).ConfigureAwait(false);
        var queryContext = record is null ? null : await BuildFormContextAsync(navigator, tenant, record, cancellationToken).ConfigureAwait(false);
        var maskBlinded = record is not null
            && record.IsQueryBlinded(state.ItemId, state.ObservationId)
            && !await access.CanUnblindAsync(tenant, state.EnrollmentId, cancellationToken).ConfigureAwait(false);
        return FormQueryResponse.From(
            state,
            record is null ? new Dictionary<ObservationId, int>() : Revisions(record),
            queryContext,
            maskBlinded);
    }

    private static async Task<List<FormQuery>> ReadAsync(
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        TenantId tenant,
        System.Linq.Expressions.Expression<Func<FormQuery, bool>> filter,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        return await db.FormQueries.AsNoTracking()
            .Where(filter)
            .OrderBy(q => q.CreatedAt)
            .ThenBy(q => q.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<List<FormQuery>> ReadPageAsync(
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        TenantId tenant,
        System.Linq.Expressions.Expression<Func<FormQuery, bool>> filter,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        return await db.FormQueries.AsNoTracking()
            .Where(filter)
            .OrderBy(q => q.CreatedAt)
            .ThenBy(q => q.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static Dictionary<ObservationId, int> Revisions(CollectionInstanceRecord record) =>
        record.Values.ToDictionary(v => v.ObservationId, v => v.Revision);

    private static ProblemHttpResult BadRequest(string detail) =>
        TypedResults.Problem(title: "Invalid request", detail: detail, statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult FormNotFound(Guid formId) =>
        TypedResults.Problem(title: "Not found", detail: $"Form {formId:D} does not exist.", statusCode: StatusCodes.Status404NotFound);

    private static ProblemHttpResult NotVisible(string what) =>
        TypedResults.Problem(title: "Not visible", detail: $"You may not see {what}.", statusCode: StatusCodes.Status403Forbidden);

    private sealed record EnrollmentHeader(Guid StudyId, Guid? StudySiteId, string? SiteCode, string? SubjectCode);
}

/// <summary>Query-string parameters for <c>GET /api/v1/execution/forms/queries</c>.</summary>
/// <param name="EnrollmentId">Lists queries for one enrollment.</param>
/// <param name="StudyId">Lists queries across visible enrollments of one study.</param>
/// <param name="Status">Optional status filter (<c>open</c>, <c>answered</c>, <c>closed</c>, <c>cancelled</c>).</param>
/// <param name="Site">Optional study site code or id filter.</param>
/// <param name="Participant">Optional participant subject code or enrollment id filter.</param>
/// <param name="ValueChanged">Optional filter for whether the value changed after the query opened.</param>
/// <param name="Limit">Optional page size between 1 and 1000 (defaults to 500).</param>
/// <param name="Offset">Optional zero-based page offset (defaults to 0).</param>
internal sealed record FormQueryListFilter(
    [FromQuery(Name = "enrollment_id")] Guid? EnrollmentId = null,
    [FromQuery(Name = "study_id")] Guid? StudyId = null,
    [FromQuery(Name = "status")] string? Status = null,
    [FromQuery(Name = "site")] string? Site = null,
    [FromQuery(Name = "participant")] string? Participant = null,
    [FromQuery(Name = "value_changed")] bool? ValueChanged = null,
    [FromQuery(Name = "limit")] int? Limit = null,
    [FromQuery(Name = "offset")] int? Offset = null);

/// <summary>The services a query step needs, bound from DI as one parameter.</summary>
/// <param name="Forms">The kernel's form records, for the value's current revision.</param>
/// <param name="Contexts">The module's tenant-scoped reads.</param>
/// <param name="Sender">The kernel command path.</param>
/// <param name="ChangeReason">The signed change reason.</param>
/// <param name="Navigator">Kernel navigation, for site and subject codes.</param>
/// <param name="Access">Read-access and unblinding probe for masking responses.</param>
internal sealed record FormQueryCommandServices(
    [FromServices] ICollectionInstanceRecords Forms,
    [FromServices] IModuleTenantContextFactory<ExecutionModuleDbContext> Contexts,
    [FromServices] IKernelCommandSender Sender,
    [FromServices] IKernelChangeReason ChangeReason,
    [FromServices] IKernelNavigator Navigator,
    [FromServices] IKernelReadAccess Access);
