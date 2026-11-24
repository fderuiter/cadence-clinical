using System;
using System.Collections.Frozen;
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

        var queries = await ReadAsync(contexts, tenant, q => q.FormId == form, cancellationToken).ConfigureAwait(false);
        var states = ProjectStates(queries, temporalFilter, clock);
        if (states.Count == 0)
        {
            return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>([]);
        }

        var canUnblind = await access.CanUnblindAsync(tenant, header.EnrollmentId, cancellationToken).ConfigureAwait(false);
        IReadOnlySet<EnrollmentId> unblindedSet = canUnblind
            ? new HashSet<EnrollmentId> { header.EnrollmentId }
            : FrozenSet<EnrollmentId>.Empty;
        var targets = await forms.FindQueryTargetsAsync(tenant, [form], unblindedSet, cancellationToken).ConfigureAwait(false);
        targets.TryGetValue(form, out var target);

        var revisions = target?.ObservationRevisions ?? new Dictionary<ObservationId, int>();
        var itemRevisions = target?.ItemRevisions ?? new Dictionary<string, int>(StringComparer.Ordinal);
        var queryContext = await BuildFormContextAsync(
            navigator,
            tenant,
            header.StudyId,
            header.StudySiteId,
            header.EnrollmentId,
            header.CollectionSpecId,
            header.PlannedActivityId,
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>([
            .. states.Select(s => FormQueryResponse.From(
                s,
                revisions,
                queryContext,
                maskBlinded: !canUnblind && (target?.IsQueryBlinded(s.ItemId, s.ObservationId) ?? false),
                currentRevisionOverride: itemRevisions.TryGetValue(s.ItemId, out var itemRev) ? itemRev : null)),
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
        CollectionInstanceHeader resolvedHeader;
        ObservationId targetObservation;
        int targetRevision;

        if ((request?.ObservationId is null || request.ObservationId == Guid.Empty) && !string.IsNullOrWhiteSpace(request?.ItemId))
        {
            if (await forms.FindScopeStateAsync(tenant, form, cancellationToken).ConfigureAwait(false) is not { } scopeState)
            {
                return FormNotFound(formId);
            }

            var itemId = request.ItemId.Trim();
            if (scopeState.Values.FirstOrDefault(v => string.Equals(v.ItemId, itemId, StringComparison.Ordinal)) is { } existing)
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
                    new RecordUnaskedCollectionItem(tenant, form, scopeState.EnrollmentId, itemId, ObservationId: null, unaskedKey, reason),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (unasked.Value is not { } seeded)
                {
                    return KernelErrorEndpoints.Problem(unasked.Error!);
                }

                targetObservation = seeded.ObservationId;
                targetRevision = seeded.Revision;
            }

            resolvedHeader = new CollectionInstanceHeader(
                scopeState.CollectionInstanceId,
                scopeState.EnrollmentId,
                ParticipantId.Empty,
                scopeState.StudyId,
                scopeState.StudySiteId,
                scopeState.ProtocolVersionId,
                scopeState.CollectionSpecId,
                scopeState.PlannedActivityId,
                default,
                default,
                scopeState.Version);
        }
        else
        {
            if (await forms.FindHeaderAsync(tenant, form, cancellationToken).ConfigureAwait(false) is not { } header)
            {
                return FormNotFound(formId);
            }

            resolvedHeader = header;
            targetObservation = ObservationId.FromGuid(request?.ObservationId ?? Guid.Empty);
            targetRevision = request?.Revision ?? 0;
        }

        var queryGuid = request?.QueryId is { } given && given != Guid.Empty ? given : Guid.CreateVersion7();
        var result = await sender.SendAsync(
            new RaiseFormQuery(
                tenant,
                FormQueryId.FromGuid(queryGuid),
                form,
                resolvedHeader.EnrollmentId,
                targetObservation,
                targetRevision,
                request?.Text ?? string.Empty,
                key,
                reason,
                Origin: FormQueryOrigin.Manual,
                OwnerRole: ownerRole ?? FormQueryOwnerRole.Site,
                Priority: priority ?? FormQueryPriority.Normal,
                Assignee: request?.Assignee,
                ResolvedHeader: resolvedHeader),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.Value is not { } state)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var response = await RespondAsync(forms, navigator, access, tenant, state, result.Replayed, cancellationToken).ConfigureAwait(false);
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

        var hasContextFilter = !string.IsNullOrWhiteSpace(filter.Site) || !string.IsNullOrWhiteSpace(filter.Participant);
        if (!filter.HasTemporalCoordinate && !hasContextFilter && filter.ValueChanged is null)
        {
            var pagedQueries = await ReadCurrentPageAsync(
                contexts, tenant, q => q.EnrollmentId == enrollment, parsedFilters, filter.Escalated, offset, limit, cancellationToken).ConfigureAwait(false);
            var pagedStates = ProjectStates(pagedQueries, filter.Temporal, clock);
            var pagedResponses = await ShapeQueriesAsync(pagedStates, null, null, tenant, forms, access, navigator, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>(pagedResponses);
        }

        var rawQueries = filter.HasTemporalCoordinate
            ? await ReadAsync(contexts, tenant, q => q.EnrollmentId == enrollment, cancellationToken).ConfigureAwait(false)
            : await ReadCurrentAsync(contexts, tenant, q => q.EnrollmentId == enrollment, parsedFilters, filter.Escalated, cancellationToken).ConfigureAwait(false);
        var candidateStates = FilterStates(ProjectStates(rawQueries, filter.Temporal, clock), parsedFilters, filter.Escalated);
        if (candidateStates.Count == 0)
        {
            return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>([]);
        }

        Dictionary<EnrollmentId, EnrollmentHeader>? knownHeaders = null;
        if (hasContextFilter)
        {
            var formHeaders = await forms.FindHeadersAsync(
                tenant,
                candidateStates.Select(s => s.FormId).Distinct().ToList(),
                cancellationToken).ConfigureAwait(false);
            var nodesToResolve = new List<NodeRef> { NodeRef.For(enrollment) };
            nodesToResolve.AddRange(formHeaders.Values
                .Where(h => !h.StudySiteId.IsEmpty)
                .Select(h => NodeRef.For(h.StudySiteId))
                .Distinct());
            var summaries = await navigator.ResolveManyAsync(tenant, nodesToResolve, cancellationToken).ConfigureAwait(false);
            var enrollmentSummary = summaries.GetValueOrDefault(NodeRef.For(enrollment));
            var subjectCode = enrollmentSummary?.Code ?? enrollmentSummary?.Title;

            candidateStates = [
                .. candidateStates.Where(s =>
                {
                    formHeaders.TryGetValue(s.FormId, out var formHeader);
                    Guid? siteId = formHeader is not null && !formHeader.StudySiteId.IsEmpty
                        ? formHeader.StudySiteId.Value
                        : null;
                    var siteSummary = formHeader is not null && !formHeader.StudySiteId.IsEmpty
                        ? summaries.GetValueOrDefault(NodeRef.For(formHeader.StudySiteId))
                        : null;
                    var header = new EnrollmentHeader(
                        formHeader?.StudyId.Value ?? Guid.Empty,
                        siteId,
                        siteSummary?.Code ?? siteSummary?.Title,
                        subjectCode);
                    if (knownHeaders is null && formHeader is not null)
                    {
                        knownHeaders = new Dictionary<EnrollmentId, EnrollmentHeader> { [enrollment] = header };
                    }

                    return MatchesSiteFilter(header, filter.Site)
                        && MatchesParticipantFilter(enrollment, header, filter.Participant);
                }),
            ];
            if (candidateStates.Count == 0)
            {
                return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>([]);
            }
        }

        IReadOnlyDictionary<ObservationId, int>? preloadedRevisions = null;
        if (filter.ValueChanged is { } wantedChanged)
        {
            preloadedRevisions = await forms.FindObservationRevisionsAsync(
                tenant,
                candidateStates.Select(s => s.ObservationId).Distinct().ToList(),
                candidateStates.Select(s => s.FormId).Distinct().ToList(),
                cancellationToken).ConfigureAwait(false);
            candidateStates = [.. candidateStates.Where(s => IsValueChanged(s, preloadedRevisions) == wantedChanged)];
            if (candidateStates.Count == 0)
            {
                return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>([]);
            }
        }

        var finalPage = candidateStates.Skip(offset).Take(limit).ToList();
        var responses = await ShapeQueriesAsync(
            finalPage, knownHeaders, preloadedRevisions, tenant, forms, access, navigator, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>(responses);
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
        if (!filter.HasTemporalCoordinate && filter.ValueChanged is null)
        {
            var queries = await ReadCurrentPageAsync(
                contexts, tenant, q => allowed.Contains(q.EnrollmentId), parsedFilters, filter.Escalated, offset, limit, cancellationToken).ConfigureAwait(false);
            var states = ProjectStates(queries, filter.Temporal, clock);
            var responses = await ShapeQueriesAsync(states, visibleEnrollments, null, tenant, forms, access, navigator, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>(responses);
        }

        var rawQueries = filter.HasTemporalCoordinate
            ? await ReadAsync(contexts, tenant, q => allowed.Contains(q.EnrollmentId), cancellationToken).ConfigureAwait(false)
            : await ReadCurrentAsync(contexts, tenant, q => allowed.Contains(q.EnrollmentId), parsedFilters, filter.Escalated, cancellationToken).ConfigureAwait(false);
        var candidateStates = FilterStates(ProjectStates(rawQueries, filter.Temporal, clock), parsedFilters, filter.Escalated);
        if (candidateStates.Count == 0)
        {
            return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>([]);
        }

        IReadOnlyDictionary<ObservationId, int>? preloadedRevisions = null;
        if (filter.ValueChanged is { } wantedChanged)
        {
            preloadedRevisions = await forms.FindObservationRevisionsAsync(
                tenant,
                candidateStates.Select(s => s.ObservationId).Distinct().ToList(),
                candidateStates.Select(s => s.FormId).Distinct().ToList(),
                cancellationToken).ConfigureAwait(false);
            candidateStates = [.. candidateStates.Where(s => IsValueChanged(s, preloadedRevisions) == wantedChanged)];
            if (candidateStates.Count == 0)
            {
                return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>([]);
            }
        }

        var pagedStates = candidateStates.Skip(offset).Take(limit).ToList();
        var shaped = await ShapeQueriesAsync(
            pagedStates, visibleEnrollments, preloadedRevisions, tenant, forms, access, navigator, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>(shaped);
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
        var targetsById = await forms.FindQueryTargetsAsync(
            tenant,
            distinctFormIds,
            FrozenSet<EnrollmentId>.Empty,
            cancellationToken).ConfigureAwait(false);

        var blindedQueryIds = queries
            .Where(q => targetsById.TryGetValue(q.FormId, out var target) && target.IsQueryBlinded(q.ItemId, q.ObservationId))
            .Select(q => q.QueryId)
            .ToHashSet();
        var blindedEnrollmentIds = queries
            .Where(q => blindedQueryIds.Contains(q.QueryId))
            .Select(q => q.EnrollmentId)
            .Distinct()
            .ToList();
        var unblindedEnrollments = blindedEnrollmentIds.Count > 0
            ? await access.CanUnblindManyAsync(tenant, blindedEnrollmentIds, cancellationToken).ConfigureAwait(false)
            : FrozenSet<EnrollmentId>.Empty;

        var revisions = preloadedRevisions is not null
            ? new Dictionary<ObservationId, int>(preloadedRevisions)
            : new Dictionary<ObservationId, int>();
        var itemRevisions = new Dictionary<(CollectionInstanceId FormId, string ItemId), int>();
        var unresolvedNodes = new List<NodeRef>();
        foreach (var target in targetsById.Values)
        {
            foreach (var (observation, revision) in target.ObservationRevisions)
            {
                revisions[observation] = revision;
            }

            foreach (var (itemId, revision) in target.ItemRevisions)
            {
                itemRevisions[(target.CollectionInstanceId, itemId)] = revision;
            }

            if (knownHeaders is null || !knownHeaders.ContainsKey(target.EnrollmentId))
            {
                if (!target.StudySiteId.IsEmpty)
                {
                    unresolvedNodes.Add(NodeRef.For(target.StudySiteId));
                }

                unresolvedNodes.Add(NodeRef.For(target.EnrollmentId));
            }
        }

        var resolvedSummaries = unresolvedNodes.Count > 0
            ? await navigator.ResolveManyAsync(tenant, unresolvedNodes, cancellationToken).ConfigureAwait(false)
            : new Dictionary<NodeRef, NodeSummary>();

        var formContexts = new Dictionary<CollectionInstanceId, FormQueryContext>(targetsById.Count);
        foreach (var (formId, target) in targetsById)
        {
            Guid? studySiteId = target.StudySiteId.IsEmpty ? null : target.StudySiteId.Value;
            if (knownHeaders is not null && knownHeaders.TryGetValue(target.EnrollmentId, out var header))
            {
                formContexts[formId] = new FormQueryContext(
                    target.StudyId.Value,
                    studySiteId ?? header.StudySiteId,
                    header.SiteCode,
                    header.SubjectCode,
                    target.CollectionSpecId,
                    target.PlannedActivityId);
            }
            else
            {
                var siteSummary = target.StudySiteId.IsEmpty
                    ? null
                    : resolvedSummaries.GetValueOrDefault(NodeRef.For(target.StudySiteId));
                var enrollmentSummary = resolvedSummaries.GetValueOrDefault(NodeRef.For(target.EnrollmentId));
                formContexts[formId] = new FormQueryContext(
                    target.StudyId.Value,
                    studySiteId,
                    siteSummary?.Code ?? siteSummary?.Title,
                    enrollmentSummary?.Code ?? enrollmentSummary?.Title,
                    target.CollectionSpecId,
                    target.PlannedActivityId);
            }
        }

        return [
            .. queries.Select(q =>
            {
                var maskBlinded = blindedQueryIds.Contains(q.QueryId)
                    && !unblindedEnrollments.Contains(q.EnrollmentId);
                int? itemRev = itemRevisions.TryGetValue((q.FormId, q.ItemId), out var foundRev) ? foundRev : null;
                return FormQueryResponse.From(q, revisions, formContexts.GetValueOrDefault(q.FormId), maskBlinded, itemRev);
            }),
        ];
    }

    private static bool IsValueChanged(FormQueryState query, IReadOnlyDictionary<ObservationId, int> revisions) =>
        revisions.TryGetValue(query.ObservationId, out var currentRevision) && currentRevision > query.Revision;

    private static List<FormQueryState> FilterStates(
        List<FormQueryState> states,
        ParsedQueryFilters parsed,
        bool? escalated)
    {
        if (parsed.Status is null
            && parsed.OwnerRole is null
            && parsed.Origin is null
            && parsed.Priority is null
            && escalated is null)
        {
            return states;
        }

        return [
            .. states.Where(s =>
                (parsed.Status is not { } status || s.Status == status)
                && (parsed.OwnerRole is not { } owner || s.OwnerRole == owner)
                && (parsed.Origin is not { } origin || s.Origin == origin)
                && (parsed.Priority is not { } priority || s.Priority == priority)
                && (escalated is not { } esc || (s.EscalatedAt is not null) == esc)),
        ];
    }

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
        StudyId studyId,
        StudySiteId studySiteId,
        EnrollmentId enrollmentId,
        string collectionSpecId,
        string? plannedActivityId,
        CancellationToken cancellationToken)
    {
        var nodes = studySiteId.IsEmpty
            ? new List<NodeRef> { NodeRef.For(enrollmentId) }
            : new List<NodeRef> { NodeRef.For(studySiteId), NodeRef.For(enrollmentId) };
        var summaries = await navigator.ResolveManyAsync(tenant, nodes, cancellationToken).ConfigureAwait(false);
        var siteSummary = studySiteId.IsEmpty ? null : summaries.GetValueOrDefault(NodeRef.For(studySiteId));
        var enrollmentSummary = summaries.GetValueOrDefault(NodeRef.For(enrollmentId));
        return new FormQueryContext(
            studyId.Value,
            studySiteId.IsEmpty ? null : studySiteId.Value,
            siteSummary?.Code ?? siteSummary?.Title,
            enrollmentSummary?.Code ?? enrollmentSummary?.Title,
            collectionSpecId,
            plannedActivityId);
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

        if (enrollmentId is not { } resolvedEnrollment)
        {
            return TypedResults.Problem(
                title: "Not found", detail: $"Form {formId:D} has no query {queryId:D}.", statusCode: StatusCodes.Status404NotFound);
        }

        var result = await services.Sender.SendAsync(
            command(tenant, resolvedEnrollment, FormEndpoints.IdempotencyKey(context), services.ChangeReason.Read(context) ?? string.Empty),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Value is { } state
            ? TypedResults.Ok(await RespondAsync(services.Forms, services.Navigator, services.Access, tenant, state, result.Replayed, cancellationToken).ConfigureAwait(false))
            : KernelErrorEndpoints.Problem(result.Error!);
    }

    private static async Task<FormQueryResponse> RespondAsync(
        ICollectionInstanceRecords forms,
        IKernelNavigator navigator,
        IKernelReadAccess access,
        TenantId tenant,
        FormQueryState state,
        bool replayed,
        CancellationToken cancellationToken)
    {
        var canUnblind = await access.CanUnblindAsync(tenant, state.EnrollmentId, cancellationToken).ConfigureAwait(false);
        if (canUnblind && !replayed && state.Scope is { } scope)
        {
            var contextFromScope = await BuildFormContextAsync(
                navigator,
                tenant,
                scope.StudyId,
                scope.StudySiteId,
                state.EnrollmentId,
                scope.CollectionSpecId,
                scope.PlannedActivityId,
                cancellationToken).ConfigureAwait(false);
            return FormQueryResponse.From(
                state,
                new Dictionary<ObservationId, int>(),
                contextFromScope,
                maskBlinded: false,
                currentRevisionOverride: scope.CurrentRevision);
        }

        IReadOnlySet<EnrollmentId> unblindedSet = canUnblind
            ? new HashSet<EnrollmentId> { state.EnrollmentId }
            : FrozenSet<EnrollmentId>.Empty;
        var targets = await forms.FindQueryTargetsAsync(tenant, [state.FormId], unblindedSet, cancellationToken).ConfigureAwait(false);
        targets.TryGetValue(state.FormId, out var target);
        var queryContext = target is null
            ? null
            : await BuildFormContextAsync(
                navigator,
                tenant,
                target.StudyId,
                target.StudySiteId,
                target.EnrollmentId,
                target.CollectionSpecId,
                target.PlannedActivityId,
                cancellationToken).ConfigureAwait(false);
        var maskBlinded = target is not null
            && !canUnblind
            && target.IsQueryBlinded(state.ItemId, state.ObservationId);
        int? itemRevision = target is not null && target.ItemRevisions.TryGetValue(state.ItemId, out var foundRev)
            ? foundRev
            : null;
        return FormQueryResponse.From(
            state,
            target?.ObservationRevisions ?? new Dictionary<ObservationId, int>(),
            queryContext,
            maskBlinded,
            itemRevision);
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

    private static async Task<List<FormQuery>> ReadCurrentAsync(
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        TenantId tenant,
        System.Linq.Expressions.Expression<Func<FormQuery, bool>> scopeFilter,
        ParsedQueryFilters parsed,
        bool? escalated,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        return await ApplyCurrentStateFilters(db.FormQueries.AsNoTracking().Where(scopeFilter), parsed, escalated)
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
        return await ApplyCurrentStateFilters(db.FormQueries.AsNoTracking().Where(scopeFilter), parsed, escalated)
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
