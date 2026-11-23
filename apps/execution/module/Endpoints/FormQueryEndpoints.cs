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
using Cadence.Kernel.Time;
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
/// Data queries about eCRF values (#5461, <c>PRD-QRY-001</c>, <c>PRD-QRY-002</c>): raise a query on the current revision
/// of a form's value or on a blank item, list a form's, an enrollment's or a study's visible queries (including point-in-time
/// <c>as_of</c> bitemporal reconstruction and owner/origin/priority/escalated filters), answer, reopen, close or cancel one,
/// run 14-day SLA escalations, and read de-identified daily aging digests.
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

        group.MapGet("/queries/digests", ListAgingDigestsAsync)
            .WithName("ExecutionListFormQueryAgingDigests")
            .WithSummary("Lists daily de-identified query aging digests for visible scopes of a study.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/queries/escalate", EscalateOverdueQueriesAsync)
            .WithName("ExecutionEscalateOverdueFormQueries")
            .WithSummary("Escalates open queries past their 14-day SLA on IClock and emits daily aging digests (execution.query.manage).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesKernelCommand<EscalateFormQuery>()
            .ProducesKernelCommand<GenerateFormQueryAgingDigest>();

        group.MapGet("/{formId:guid}/queries", ListForFormAsync)
            .WithName("ExecutionListFormQueries")
            .WithSummary("Lists a form's queries, oldest first, each with whether its value changed since it was raised.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
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
            .WithSummary("Closes an answered manual query (separation of duties) or system query (execution.query.manage).")
            .Accepts<FormQueryNoteRequest>(isOptional: true, contentType: "application/json")
            .ProducesKernelCommand<CloseFormQuery>();

        group.MapPost("/{formId:guid}/queries/{queryId:guid}/cancel", CancelAsync)
            .WithName("ExecutionCancelFormQuery")
            .WithSummary("Cancels an open or answered query raised in error (execution.query.manage).")
            .Accepts<FormQueryNoteRequest>(isOptional: true, contentType: "application/json")
            .ProducesKernelCommand<CancelFormQuery>();

        return endpoints;
    }

    internal static async Task<Results<Ok<IReadOnlyList<FormQueryResponse>>, ProblemHttpResult>> ListForFormAsync(
        Guid formId,
        [AsParameters] FormQueryTemporalFilter temporalFilter,
        HttpContext context,
        ICollectionInstanceRecords forms,
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        IKernelReadAccess access,
        IKernelNavigator navigator,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var tenant = KernelRequestTenant.Get(context);
        var form = CollectionInstanceId.FromGuid(formId);
        if (await forms.FindHeaderAsync(tenant, form, cancellationToken).ConfigureAwait(false) is not { } header)
        {
            return FormNotFound(formId);
        }

        if (!await access.CanSeeAsync(tenant, NodeRef.For(header.EnrollmentId), cancellationToken).ConfigureAwait(false))
        {
            return NotVisible($"form {formId:D}");
        }

        var queries = await ReadAsync(contexts, tenant, q => q.FormId == form, temporalFilter.HasCoordinate, cancellationToken).ConfigureAwait(false);
        var states = ProjectStates(queries, temporalFilter, clock);
        if (states.Count == 0)
        {
            return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>([]);
        }

        var observationIds = states.Select(s => s.ObservationId).Distinct().ToList();
        var projections = await forms.FindObservationProjectionsAsync(tenant, observationIds, [form], cancellationToken).ConfigureAwait(false);
        var revisions = projections.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Revision);
        var anyBlinded = states.Any(s => IsQueryBlinded(header.CollectionSpecId, s.ItemId, s.ObservationId, projections));
        var canUnblind = !anyBlinded || await access.CanUnblindAsync(tenant, header.EnrollmentId, cancellationToken).ConfigureAwait(false);
        var queryContext = await BuildFormContextAsync(navigator, tenant, header, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>([
            .. states.Select(s => FormQueryResponse.From(
                s,
                revisions,
                queryContext,
                maskBlinded: !canUnblind && IsQueryBlinded(header.CollectionSpecId, s.ItemId, s.ObservationId, projections))),
        ]);
    }

    internal static async Task<Results<Ok<IReadOnlyList<FormQueryResponse>>, ProblemHttpResult>> ListQueriesAsync(
        [AsParameters] FormQueryListFilter filter,
        HttpContext context,
        ICollectionInstanceRecords forms,
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        IKernelReadAccess access,
        IKernelNavigator navigator,
        IClock clock,
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

        if (!TryParseFilters(filter, out var parsedFilters, out var filterError))
        {
            return BadRequest(filterError!);
        }

        var tenant = KernelRequestTenant.Get(context);
        return hasEnrollment
            ? await ListByEnrollmentAsync(EnrollmentId.FromGuid(filter.EnrollmentId!.Value), parsedFilters, filter, limit, offset, tenant, forms, contexts, access, navigator, clock, cancellationToken).ConfigureAwait(false)
            : await ListByStudyAsync(StudyId.FromGuid(filter.StudyId!.Value), parsedFilters, filter, limit, offset, tenant, forms, contexts, access, navigator, clock, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<Results<Ok<IReadOnlyList<FormQueryAgingDigestResponse>>, ProblemHttpResult>> ListAgingDigestsAsync(
        [FromQuery(Name = "study_id")] Guid? studyId,
        [FromQuery(Name = "site")] string? site,
        [FromQuery(Name = "recipient_role")] string? recipientRole,
        HttpContext context,
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        IKernelReadAccess access,
        IKernelNavigator navigator,
        CancellationToken cancellationToken)
    {
        if (studyId is not { } studyGuid || studyGuid == Guid.Empty)
        {
            return BadRequest("Specify study_id to list query aging digests.");
        }

        var tenant = KernelRequestTenant.Get(context);
        var study = StudyId.FromGuid(studyGuid);
        var studyNode = NodeRef.For(study);
        if (await navigator.ResolveAsync(tenant, studyNode, cancellationToken).ConfigureAwait(false) is null)
        {
            return TypedResults.Problem(title: "Not found", detail: $"Study {studyGuid:D} does not exist.", statusCode: StatusCodes.Status404NotFound);
        }

        if (!await access.CanSeeAsync(tenant, studyNode, cancellationToken).ConfigureAwait(false))
        {
            return NotVisible($"study {studyGuid:D}");
        }

        List<FormQueryAgingDigest> digests;
        await using (var db = contexts.Create(tenant))
        {
            digests = await db.FormQueryAgingDigests.AsNoTracking()
                .Where(d => d.StudyId == study)
                .OrderByDescending(d => d.DigestDate)
                .ThenBy(d => d.ScopeKey)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var siteNodes = digests
            .Where(d => d.StudySiteId.HasValue)
            .Select(d => NodeRef.For(d.StudySiteId!.Value))
            .Distinct()
            .ToList();
        var visibleSites = siteNodes.Count > 0
            ? await access.FilterVisibleAsync(tenant, siteNodes, cancellationToken).ConfigureAwait(false)
            : new HashSet<NodeRef>();

        var visible = new List<FormQueryAgingDigestResponse>();
        foreach (var digest in digests)
        {
            if (digest.StudySiteId is { } siteId && !visibleSites.Contains(NodeRef.For(siteId)))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(site)
                && !string.Equals(digest.SiteCode, site.Trim(), StringComparison.OrdinalIgnoreCase)
                && !(digest.StudySiteId is { } sId && string.Equals(sId.Value.ToString("D"), site.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(recipientRole)
                && !string.Equals(digest.RecipientRole, recipientRole.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            visible.Add(FormQueryAgingDigestResponse.From(digest));
        }

        return TypedResults.Ok<IReadOnlyList<FormQueryAgingDigestResponse>>(visible);
    }

    internal static async Task<Results<Ok<FormQueryEscalationPassResponse>, ProblemHttpResult>> EscalateOverdueQueriesAsync(
        [FromQuery(Name = "study_id")] Guid? studyId,
        HttpContext context,
        IFormQueryEscalationService escalation,
        IKernelReadAccess access,
        IKernelChangeReason changeReason,
        IKernelNavigator navigator,
        CancellationToken cancellationToken)
    {
        if (studyId is not { } studyGuid || studyGuid == Guid.Empty)
        {
            return BadRequest("Specify study_id to run query escalation.");
        }

        var tenant = KernelRequestTenant.Get(context);
        var study = StudyId.FromGuid(studyGuid);
        var studyNode = NodeRef.For(study);
        if (await navigator.ResolveAsync(tenant, studyNode, cancellationToken).ConfigureAwait(false) is null)
        {
            return TypedResults.Problem(title: "Not found", detail: $"Study {studyGuid:D} does not exist.", statusCode: StatusCodes.Status404NotFound);
        }

        if (!await access.CanSeeAsync(tenant, studyNode, cancellationToken).ConfigureAwait(false))
        {
            return NotVisible($"study {studyGuid:D}");
        }

        var reason = changeReason.Read(context);
        var result = await escalation.RunStudyPassAsync(tenant, study, reason, cancellationToken).ConfigureAwait(false);
        return result.Value is { } outcome
            ? TypedResults.Ok(new FormQueryEscalationPassResponse(
                outcome.EscalatedQueries,
                [.. outcome.Digests.Select(FormQueryAgingDigestResponse.From)]))
            : KernelErrorEndpoints.Problem(result.Error!);
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

        if (!TryParseOwnerRole(request?.OwnerRole, out var ownerRole))
        {
            return BadRequest($"'{request?.OwnerRole}' is not an owner role: site, monitor or data_manager.");
        }

        if (!TryParsePriority(request?.Priority, out var priority))
        {
            return BadRequest($"'{request?.Priority}' is not a priority: low, normal or high.");
        }

        var key = FormEndpoints.IdempotencyKey(context);
        var reason = changeReason.Read(context) ?? string.Empty;
        EnrollmentId enrollmentId;
        ObservationId targetObservation;
        int targetRevision;

        if ((request?.ObservationId is null || request.ObservationId == Guid.Empty) && !string.IsNullOrWhiteSpace(request?.ItemId))
        {
            var itemId = request.ItemId.Trim();
            if (await forms.FindQueryTargetAsync(
                    tenant,
                    form,
                    itemId: itemId,
                    checkLocks: false,
                    cancellationToken: cancellationToken).ConfigureAwait(false) is not { } target)
            {
                return FormNotFound(formId);
            }

            enrollmentId = target.EnrollmentId;
            if (target.ObservationId is { } existingObservation && target.Revision is { } existingRevision)
            {
                if (!target.IsUnaskedPlaceholder)
                {
                    return KernelErrorEndpoints.Problem(
                        FormQueryErrors.RevisionStale.WithMessage(
                            $"Item {itemId} now holds a value at revision {existingRevision}; reload the form and query the current value."));
                }

                targetObservation = existingObservation;
                targetRevision = existingRevision;
            }
            else
            {
                var unaskedKey = string.IsNullOrWhiteSpace(key) ? string.Empty : $"{key}:unasked";
                var unasked = await sender.SendAsync(
                    new RecordUnaskedCollectionItem(tenant, form, enrollmentId, itemId, ObservationId: null, unaskedKey, reason),
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
            if (await forms.FindHeaderAsync(tenant, form, cancellationToken).ConfigureAwait(false) is not { } header)
            {
                return FormNotFound(formId);
            }

            enrollmentId = header.EnrollmentId;
            targetObservation = ObservationId.FromGuid(request?.ObservationId ?? Guid.Empty);
            targetRevision = request?.Revision ?? 0;
        }

        var queryGuid = request?.QueryId is { } given && given != Guid.Empty ? given : Guid.CreateVersion7();
        var result = await sender.SendAsync(
            new RaiseFormQuery(
                tenant,
                FormQueryId.FromGuid(queryGuid),
                form,
                enrollmentId,
                targetObservation,
                targetRevision,
                request?.Text ?? string.Empty,
                key,
                reason,
                Origin: FormQueryOrigin.Manual,
                OwnerRole: ownerRole ?? FormQueryOwnerRole.Site,
                Priority: priority ?? FormQueryPriority.Normal,
                Assignee: request?.Assignee),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.Value is not { } state)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var response = await RespondAsync(forms, navigator, access, tenant, state, cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"{FormEndpoints.RoutePrefix}/{formId:D}/queries/{state.QueryId.Value:D}", response);
    }

    internal static Task<Results<Ok<FormQueryResponse>, ProblemHttpResult>> AnswerAsync(
        Guid formId, Guid queryId, FormQueryTextRequest request, HttpContext context, [AsParameters] FormQueryCommandServices services, CancellationToken cancellationToken) =>
        StepAsync(formId, queryId, context, services, (tenant, enrollment, key, reason) =>
            new AnswerFormQuery(tenant, FormQueryId.FromGuid(queryId), enrollment, request?.Text ?? string.Empty, key, reason), cancellationToken);

    internal static Task<Results<Ok<FormQueryResponse>, ProblemHttpResult>> ReopenAsync(
        Guid formId, Guid queryId, FormQueryTextRequest request, HttpContext context, [AsParameters] FormQueryCommandServices services, CancellationToken cancellationToken) =>
        StepAsync(formId, queryId, context, services, (tenant, enrollment, key, reason) =>
            new ReopenFormQuery(tenant, FormQueryId.FromGuid(queryId), enrollment, request?.Text ?? string.Empty, key, reason), cancellationToken);

    internal static async Task<Results<Ok<FormQueryResponse>, ProblemHttpResult>> CloseAsync(
        Guid formId, Guid queryId, HttpContext context, [AsParameters] FormQueryCommandServices services, CancellationToken cancellationToken)
    {
        var note = await ReadOptionalNoteAsync(context, cancellationToken).ConfigureAwait(false);
        return await StepAsync(formId, queryId, context, services, (tenant, enrollment, key, reason) =>
            new CloseFormQuery(tenant, FormQueryId.FromGuid(queryId), enrollment, key, reason, note?.Text), cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<Results<Ok<FormQueryResponse>, ProblemHttpResult>> CancelAsync(
        Guid formId, Guid queryId, HttpContext context, [AsParameters] FormQueryCommandServices services, CancellationToken cancellationToken)
    {
        var note = await ReadOptionalNoteAsync(context, cancellationToken).ConfigureAwait(false);
        return await StepAsync(formId, queryId, context, services, (tenant, enrollment, key, reason) =>
            new CancelFormQuery(tenant, FormQueryId.FromGuid(queryId), enrollment, key, reason, note?.Text), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<FormQueryNoteRequest?> ReadOptionalNoteAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (!context.Request.HasJsonContentType() || context.Request.ContentLength == 0)
        {
            return null;
        }

        return await context.Request.ReadFromJsonAsync<FormQueryNoteRequest>(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Results<Ok<IReadOnlyList<FormQueryResponse>>, ProblemHttpResult>> ListByEnrollmentAsync(
        EnrollmentId enrollment,
        ParsedQueryFilters parsedFilters,
        FormQueryListFilter filter,
        int limit,
        int offset,
        TenantId tenant,
        ICollectionInstanceRecords forms,
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        IKernelReadAccess access,
        IKernelNavigator navigator,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!await access.CanSeeAsync(tenant, NodeRef.For(enrollment), cancellationToken).ConfigureAwait(false))
        {
            return NotVisible($"enrollment {enrollment.Value:D}");
        }

        if (filter.HasTemporalCoordinate)
        {
            var historicalQueries = await ReadAsync(contexts, tenant, q => q.EnrollmentId == enrollment, includePeriods: true, cancellationToken).ConfigureAwait(false);
            var historicalStates = ProjectStates(historicalQueries, filter.Temporal, clock);
            var historicalResponses = await ShapeQueriesAsync(historicalStates, null, null, tenant, forms, access, navigator, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(ApplyPostFilters(historicalResponses, parsedFilters, filter, offset, limit));
        }

        var hasContextFilter = !string.IsNullOrWhiteSpace(filter.Site) || !string.IsNullOrWhiteSpace(filter.Participant);
        if (!hasContextFilter && filter.ValueChanged is null)
        {
            var pagedQueries = await ReadCurrentPageAsync(
                contexts, tenant, q => q.EnrollmentId == enrollment, parsedFilters, filter.Escalated, offset, limit, cancellationToken).ConfigureAwait(false);
            var pagedStates = ProjectStates(pagedQueries, filter.Temporal, clock);
            var pagedResponses = await ShapeQueriesAsync(pagedStates, null, null, tenant, forms, access, navigator, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>(pagedResponses);
        }

        var queries = await ReadCurrentAsync(
            contexts, tenant, q => q.EnrollmentId == enrollment, parsedFilters, filter.Escalated, cancellationToken).ConfigureAwait(false);
        if (filter.ValueChanged is { } wantedChanged && !hasContextFilter)
        {
            if (queries.Count == 0)
            {
                return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>([]);
            }

            var preloadedProjections = await forms.FindObservationProjectionsAsync(
                tenant,
                queries.Select(q => q.ObservationId).Distinct().ToList(),
                queries.Select(q => q.FormId).Distinct().ToList(),
                cancellationToken).ConfigureAwait(false);
            var filteredQueries = queries
                .Where(q => IsValueChanged(q, preloadedProjections) == wantedChanged)
                .Skip(offset)
                .Take(limit)
                .ToList();
            var filteredStates = ProjectStates(filteredQueries, filter.Temporal, clock);
            var filteredResponses = await ShapeQueriesAsync(filteredStates, null, preloadedProjections, tenant, forms, access, navigator, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>(filteredResponses);
        }

        var states = ProjectStates(queries, filter.Temporal, clock);
        var responses = await ShapeQueriesAsync(states, null, null, tenant, forms, access, navigator, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ApplyPostFilters(responses, parsedFilters, filter, offset, limit));
    }

    private static async Task<Results<Ok<IReadOnlyList<FormQueryResponse>>, ProblemHttpResult>> ListByStudyAsync(
        StudyId study,
        ParsedQueryFilters parsedFilters,
        FormQueryListFilter filter,
        int limit,
        int offset,
        TenantId tenant,
        ICollectionInstanceRecords forms,
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        IKernelReadAccess access,
        IKernelNavigator navigator,
        IClock clock,
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
        if (filter.HasTemporalCoordinate)
        {
            var historicalQueries = await ReadAsync(contexts, tenant, q => allowed.Contains(q.EnrollmentId), includePeriods: true, cancellationToken).ConfigureAwait(false);
            var historicalStates = ProjectStates(historicalQueries, filter.Temporal, clock);
            var historicalResponses = await ShapeQueriesAsync(historicalStates, visibleEnrollments, null, tenant, forms, access, navigator, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(ApplyPostFilters(historicalResponses, parsedFilters, filter, offset, limit));
        }

        if (filter.ValueChanged is { } wantedChanged)
        {
            var candidates = await ReadCurrentAsync(
                contexts, tenant, q => allowed.Contains(q.EnrollmentId), parsedFilters, filter.Escalated, cancellationToken).ConfigureAwait(false);
            if (candidates.Count == 0)
            {
                return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>([]);
            }

            var preloadedProjections = await forms.FindObservationProjectionsAsync(
                tenant,
                candidates.Select(q => q.ObservationId).Distinct().ToList(),
                candidates.Select(q => q.FormId).Distinct().ToList(),
                cancellationToken).ConfigureAwait(false);
            var pagedCandidates = candidates
                .Where(q => IsValueChanged(q, preloadedProjections) == wantedChanged)
                .Skip(offset)
                .Take(limit)
                .ToList();
            var changedStates = ProjectStates(pagedCandidates, filter.Temporal, clock);
            var changedResponses = await ShapeQueriesAsync(
                changedStates, visibleEnrollments, preloadedProjections, tenant, forms, access, navigator, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>(changedResponses);
        }

        var queries = await ReadCurrentPageAsync(
            contexts, tenant, q => allowed.Contains(q.EnrollmentId), parsedFilters, filter.Escalated, offset, limit, cancellationToken).ConfigureAwait(false);
        var states = ProjectStates(queries, filter.Temporal, clock);
        var responses = await ShapeQueriesAsync(states, visibleEnrollments, null, tenant, forms, access, navigator, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>(responses);
    }

    private static List<FormQueryState> ProjectStates(
        IReadOnlyList<FormQuery> queries,
        FormQueryTemporalFilter temporal,
        IClock clock)
    {
        if (!temporal.HasCoordinate)
        {
            return [.. queries.Select(q => FormQueryState.Of(q, q.VersionIndex))];
        }

        var now = clock.UtcNow;
        var validAt = temporal.ValidAt ?? temporal.AsOf ?? now;
        var knownAt = temporal.KnownAt ?? temporal.AsOf ?? now;
        var projected = new List<FormQueryState>(queries.Count);
        foreach (var query in queries)
        {
            if (query.StateAsOf(validAt, knownAt) is { } snapshot)
            {
                projected.Add(FormQueryState.OfSnapshot(query, snapshot));
            }
        }

        return projected;
    }

    private static async Task<List<FormQueryResponse>> ShapeQueriesAsync(
        IReadOnlyList<FormQueryState> queries,
        IReadOnlyDictionary<EnrollmentId, EnrollmentHeader>? knownHeaders,
        IReadOnlyDictionary<ObservationId, CollectionObservationProjection>? preloadedProjections,
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
        var headersById = await forms.FindHeadersAsync(tenant, distinctFormIds, cancellationToken).ConfigureAwait(false);
        var projections = preloadedProjections
            ?? await forms.FindObservationProjectionsAsync(
                tenant,
                queries.Select(q => q.ObservationId).Distinct().ToList(),
                distinctFormIds,
                cancellationToken).ConfigureAwait(false);
        var revisions = projections.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Revision);

        var unresolvedNodes = new HashSet<NodeRef>();
        foreach (var header in headersById.Values)
        {
            if (knownHeaders is null || !knownHeaders.ContainsKey(header.EnrollmentId))
            {
                if (!header.StudySiteId.IsEmpty)
                {
                    unresolvedNodes.Add(NodeRef.For(header.StudySiteId));
                }

                unresolvedNodes.Add(NodeRef.For(header.EnrollmentId));
            }
        }

        var resolvedSummaries = unresolvedNodes.Count > 0
            ? await navigator.ResolveManyAsync(tenant, unresolvedNodes, cancellationToken).ConfigureAwait(false)
            : new Dictionary<NodeRef, NodeSummary>();

        var formContexts = new Dictionary<CollectionInstanceId, FormQueryContext>(headersById.Count);
        foreach (var (formId, formHeader) in headersById)
        {
            Guid? studySiteId = formHeader.StudySiteId.IsEmpty ? null : formHeader.StudySiteId.Value;
            if (knownHeaders is not null && knownHeaders.TryGetValue(formHeader.EnrollmentId, out var known))
            {
                formContexts[formId] = new FormQueryContext(
                    formHeader.StudyId.Value,
                    studySiteId ?? known.StudySiteId,
                    known.SiteCode,
                    known.SubjectCode,
                    formHeader.CollectionSpecId,
                    formHeader.PlannedActivityId);
            }
            else
            {
                var siteSummary = formHeader.StudySiteId.IsEmpty
                    ? null
                    : resolvedSummaries.GetValueOrDefault(NodeRef.For(formHeader.StudySiteId));
                var enrollmentSummary = resolvedSummaries.GetValueOrDefault(NodeRef.For(formHeader.EnrollmentId));
                formContexts[formId] = new FormQueryContext(
                    formHeader.StudyId.Value,
                    studySiteId,
                    siteSummary?.Code ?? siteSummary?.Title,
                    enrollmentSummary?.Code ?? enrollmentSummary?.Title,
                    formHeader.CollectionSpecId,
                    formHeader.PlannedActivityId);
            }
        }

        var canUnblindByEnrollment = new Dictionary<EnrollmentId, bool>();
        foreach (var query in queries)
        {
            var specId = headersById.GetValueOrDefault(query.FormId)?.CollectionSpecId;
            if (IsQueryBlinded(specId, query.ItemId, query.ObservationId, projections)
                && !canUnblindByEnrollment.ContainsKey(query.EnrollmentId))
            {
                canUnblindByEnrollment[query.EnrollmentId] = await access.CanUnblindAsync(tenant, query.EnrollmentId, cancellationToken).ConfigureAwait(false);
            }
        }

        return [
            .. queries.Select(q =>
            {
                var specId = headersById.GetValueOrDefault(q.FormId)?.CollectionSpecId;
                var maskBlinded = IsQueryBlinded(specId, q.ItemId, q.ObservationId, projections)
                    && !canUnblindByEnrollment.GetValueOrDefault(q.EnrollmentId);
                return FormQueryResponse.From(q, revisions, formContexts.GetValueOrDefault(q.FormId), maskBlinded);
            }),
        ];
    }

    private static bool IsValueChanged(FormQuery query, IReadOnlyDictionary<ObservationId, CollectionObservationProjection> projections) =>
        projections.TryGetValue(query.ObservationId, out var current) && current.Revision > query.Revision;

    private static bool IsQueryBlinded(
        string? collectionSpecId,
        string itemId,
        ObservationId observationId,
        IReadOnlyDictionary<ObservationId, CollectionObservationProjection>? projections) =>
        (projections is not null && projections.TryGetValue(observationId, out var projection) && projection.IsBlinded)
        || BlindedTreatmentClassifier.MatchesMarkers(collectionSpecId)
        || BlindedTreatmentClassifier.MatchesMarkers(itemId);

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
        ParsedQueryFilters parsed,
        FormQueryListFilter filter,
        int offset,
        int limit)
    {
        IEnumerable<FormQueryResponse> filtered = responses;
        if (parsed.Status is { } status)
        {
            var statusWire = status.ToString().ToLowerInvariant();
            filtered = filtered.Where(q => string.Equals(q.Status, statusWire, StringComparison.OrdinalIgnoreCase));
        }

        if (parsed.OwnerRole is { } ownerRole)
        {
            var ownerWire = FormQueryEscalationService.ToSnakeCase(ownerRole);
            filtered = filtered.Where(q => string.Equals(q.OwnerRole, ownerWire, StringComparison.OrdinalIgnoreCase));
        }

        if (parsed.Origin is { } origin)
        {
            var originWire = origin.ToString().ToLowerInvariant();
            filtered = filtered.Where(q => string.Equals(q.Origin, originWire, StringComparison.OrdinalIgnoreCase));
        }

        if (parsed.Priority is { } priority)
        {
            var priorityWire = priority.ToString().ToLowerInvariant();
            filtered = filtered.Where(q => string.Equals(q.Priority, priorityWire, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.Escalated is { } escalated)
        {
            filtered = filtered.Where(q => (q.EscalatedAt is not null) == escalated);
        }

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

    private static bool TryParseFilters(FormQueryListFilter filter, out ParsedQueryFilters parsed, out string? error)
    {
        FormQueryStatus? status = null;
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            if (!Enum.TryParse<FormQueryStatus>(filter.Status.Trim(), ignoreCase: true, out var s) || !Enum.IsDefined(s))
            {
                parsed = default;
                error = $"'{filter.Status}' is not a query status: open, answered, closed or cancelled.";
                return false;
            }

            status = s;
        }

        if (!TryParseOwnerRole(filter.Owner, out var ownerRole))
        {
            parsed = default;
            error = $"'{filter.Owner}' is not an owner role: site, monitor or data_manager.";
            return false;
        }

        FormQueryOrigin? origin = null;
        if (!string.IsNullOrWhiteSpace(filter.Origin))
        {
            if (!Enum.TryParse<FormQueryOrigin>(filter.Origin.Trim(), ignoreCase: true, out var o) || !Enum.IsDefined(o))
            {
                parsed = default;
                error = $"'{filter.Origin}' is not a query origin: manual or system.";
                return false;
            }

            origin = o;
        }

        if (!TryParsePriority(filter.Priority, out var priority))
        {
            parsed = default;
            error = $"'{filter.Priority}' is not a query priority: low, normal or high.";
            return false;
        }

        parsed = new ParsedQueryFilters(status, ownerRole, origin, priority);
        error = null;
        return true;
    }

    private static bool TryParseOwnerRole(string? raw, out FormQueryOwnerRole? role)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            role = null;
            return true;
        }

        var normalized = raw.Trim().Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        if (Enum.TryParse<FormQueryOwnerRole>(normalized, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            role = parsed;
            return true;
        }

        role = null;
        return false;
    }

    private static bool TryParsePriority(string? raw, out FormQueryPriority? priority)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            priority = null;
            return true;
        }

        if (Enum.TryParse<FormQueryPriority>(raw.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            priority = parsed;
            return true;
        }

        priority = null;
        return false;
    }

    private static async Task<FormQueryContext> BuildFormContextAsync(
        IKernelNavigator navigator,
        TenantId tenant,
        CollectionInstanceHeader header,
        CancellationToken cancellationToken)
    {
        var nodes = new HashSet<NodeRef>();
        if (!header.StudySiteId.IsEmpty)
        {
            nodes.Add(NodeRef.For(header.StudySiteId));
        }

        var enrollmentNode = NodeRef.For(header.EnrollmentId);
        nodes.Add(enrollmentNode);
        var summaries = await navigator.ResolveManyAsync(tenant, nodes, cancellationToken).ConfigureAwait(false);
        var siteSummary = header.StudySiteId.IsEmpty ? null : summaries.GetValueOrDefault(NodeRef.For(header.StudySiteId));
        var enrollmentSummary = summaries.GetValueOrDefault(enrollmentNode);
        return new FormQueryContext(
            header.StudyId.Value,
            header.StudySiteId.IsEmpty ? null : header.StudySiteId.Value,
            siteSummary?.Code ?? siteSummary?.Title,
            enrollmentSummary?.Code ?? enrollmentSummary?.Title,
            header.CollectionSpecId,
            header.PlannedActivityId);
    }

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
        var typedQueryId = FormQueryId.FromGuid(queryId);
        EnrollmentId? enrollmentId;
        await using (var db = services.Contexts.Create(tenant))
        {
            enrollmentId = await db.FormQueries.AsNoTracking()
                .IgnoreAutoIncludes()
                .Where(q => q.Id == typedQueryId && q.FormId == form)
                .Select(q => (EnrollmentId?)q.EnrollmentId)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (enrollmentId is not { } enrollment)
        {
            return TypedResults.Problem(
                title: "Not found", detail: $"Form {formId:D} has no query {queryId:D}.", statusCode: StatusCodes.Status404NotFound);
        }

        var result = await services.Sender.SendAsync(
            command(tenant, enrollment, FormEndpoints.IdempotencyKey(context), services.ChangeReason.Read(context) ?? string.Empty),
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
        var header = await forms.FindHeaderAsync(tenant, state.FormId, cancellationToken).ConfigureAwait(false);
        if (header is null)
        {
            return FormQueryResponse.From(state, new Dictionary<ObservationId, int>(), null, maskBlinded: false);
        }

        var queryContext = await BuildFormContextAsync(navigator, tenant, header, cancellationToken).ConfigureAwait(false);
        var projections = await forms.FindObservationProjectionsAsync(
            tenant,
            [state.ObservationId],
            [state.FormId],
            cancellationToken).ConfigureAwait(false);
        var revisions = projections.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Revision);
        var isBlinded = IsQueryBlinded(header.CollectionSpecId, state.ItemId, state.ObservationId, projections);
        var maskBlinded = isBlinded
            && !await access.CanUnblindAsync(tenant, state.EnrollmentId, cancellationToken).ConfigureAwait(false);
        return FormQueryResponse.From(
            state,
            revisions,
            queryContext,
            maskBlinded);
    }

    private static async Task<List<FormQuery>> ReadAsync(
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        TenantId tenant,
        System.Linq.Expressions.Expression<Func<FormQuery, bool>> filter,
        bool includePeriods,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        IQueryable<FormQuery> query = includePeriods
            ? db.FormQueries.AsNoTracking()
            : db.FormQueries.AsNoTracking().IgnoreAutoIncludes().Include(q => q.MessageRecords);
        return await query
            .Where(filter)
            .OrderBy(q => q.CreatedAt)
            .ThenBy(q => q.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<List<FormQuery>> ReadCurrentAsync(
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        TenantId tenant,
        System.Linq.Expressions.Expression<Func<FormQuery, bool>> scopeFilter,
        ParsedQueryFilters parsed,
        bool? escalated,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var baseQuery = db.FormQueries.AsNoTracking()
            .IgnoreAutoIncludes()
            .Include(q => q.MessageRecords)
            .Where(scopeFilter);
        return await ApplyCurrentStateFilters(baseQuery, parsed, escalated)
            .OrderBy(q => q.CreatedAt)
            .ThenBy(q => q.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<List<FormQuery>> ReadCurrentPageAsync(
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        TenantId tenant,
        System.Linq.Expressions.Expression<Func<FormQuery, bool>> scopeFilter,
        ParsedQueryFilters parsed,
        bool? escalated,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var baseQuery = db.FormQueries.AsNoTracking()
            .IgnoreAutoIncludes()
            .Include(q => q.MessageRecords)
            .Where(scopeFilter);
        return await ApplyCurrentStateFilters(baseQuery, parsed, escalated)
            .OrderBy(q => q.CreatedAt)
            .ThenBy(q => q.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static IQueryable<FormQuery> ApplyCurrentStateFilters(
        IQueryable<FormQuery> query,
        ParsedQueryFilters parsed,
        bool? escalated)
    {
        if (parsed.Status is { } status)
        {
            query = query.Where(q => q.Status == status);
        }

        if (parsed.OwnerRole is { } ownerRole)
        {
            query = query.Where(q => q.OwnerRole == ownerRole);
        }

        if (parsed.Origin is { } origin)
        {
            query = query.Where(q => q.Origin == origin);
        }

        if (parsed.Priority is { } priority)
        {
            query = query.Where(q => q.Priority == priority);
        }

        if (escalated is true)
        {
            query = query.Where(q => q.EscalatedAt != null);
        }
        else if (escalated is false)
        {
            query = query.Where(q => q.EscalatedAt == null);
        }

        return query;
    }

    private static ProblemHttpResult BadRequest(string detail) =>
        TypedResults.Problem(title: "Invalid request", detail: detail, statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult FormNotFound(Guid formId) =>
        TypedResults.Problem(title: "Not found", detail: $"Form {formId:D} does not exist.", statusCode: StatusCodes.Status404NotFound);

    private static ProblemHttpResult NotVisible(string what) =>
        TypedResults.Problem(title: "Not visible", detail: $"You may not see {what}.", statusCode: StatusCodes.Status403Forbidden);

    private readonly record struct ParsedQueryFilters(
        FormQueryStatus? Status,
        FormQueryOwnerRole? OwnerRole,
        FormQueryOrigin? Origin,
        FormQueryPriority? Priority);

    private sealed record EnrollmentHeader(Guid StudyId, Guid? StudySiteId, string? SiteCode, string? SubjectCode);
}

/// <summary>Bitemporal query-string parameters for point-in-time query reads.</summary>
internal sealed record FormQueryTemporalFilter(
    [FromQuery(Name = "as_of")] DateTimeOffset? AsOf = null,
    [FromQuery(Name = "valid_at")] DateTimeOffset? ValidAt = null,
    [FromQuery(Name = "known_at")] DateTimeOffset? KnownAt = null)
{
    public bool HasCoordinate => AsOf.HasValue || ValidAt.HasValue || KnownAt.HasValue;
}

/// <summary>Query-string parameters for <c>GET /api/v1/execution/forms/queries</c>.</summary>
/// <param name="EnrollmentId">Lists queries for one enrollment.</param>
/// <param name="StudyId">Lists queries across visible enrollments of one study.</param>
/// <param name="Status">Optional status filter (<c>open</c>, <c>answered</c>, <c>closed</c>, <c>cancelled</c>).</param>
/// <param name="Owner">Optional owner role filter (<c>site</c>, <c>monitor</c>, <c>data_manager</c>).</param>
/// <param name="Origin">Optional query origin filter (<c>manual</c>, <c>system</c>).</param>
/// <param name="Priority">Optional query priority filter (<c>low</c>, <c>normal</c>, <c>high</c>).</param>
/// <param name="Escalated">Optional filter for whether the query has been escalated past its 14-day SLA.</param>
/// <param name="Site">Optional study site code or id filter.</param>
/// <param name="Participant">Optional participant subject code or enrollment id filter.</param>
/// <param name="ValueChanged">Optional filter for whether the value changed after the query opened.</param>
/// <param name="AsOf">Optional point-in-time coordinate applied to both valid and recorded time.</param>
/// <param name="ValidAt">Optional valid-time coordinate for bitemporal query reconstruction.</param>
/// <param name="KnownAt">Optional recorded-time coordinate for bitemporal query reconstruction.</param>
/// <param name="Limit">Optional page size between 1 and 1000 (defaults to 500).</param>
/// <param name="Offset">Optional zero-based page offset (defaults to 0).</param>
internal sealed record FormQueryListFilter(
    [FromQuery(Name = "enrollment_id")] Guid? EnrollmentId = null,
    [FromQuery(Name = "study_id")] Guid? StudyId = null,
    [FromQuery(Name = "status")] string? Status = null,
    [FromQuery(Name = "owner")] string? Owner = null,
    [FromQuery(Name = "origin")] string? Origin = null,
    [FromQuery(Name = "priority")] string? Priority = null,
    [FromQuery(Name = "escalated")] bool? Escalated = null,
    [FromQuery(Name = "site")] string? Site = null,
    [FromQuery(Name = "participant")] string? Participant = null,
    [FromQuery(Name = "value_changed")] bool? ValueChanged = null,
    [FromQuery(Name = "as_of")] DateTimeOffset? AsOf = null,
    [FromQuery(Name = "valid_at")] DateTimeOffset? ValidAt = null,
    [FromQuery(Name = "known_at")] DateTimeOffset? KnownAt = null,
    [FromQuery(Name = "limit")] int? Limit = null,
    [FromQuery(Name = "offset")] int? Offset = null)
{
    public FormQueryTemporalFilter Temporal => new(AsOf, ValidAt, KnownAt);

    public bool HasTemporalCoordinate => Temporal.HasCoordinate;
}

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
