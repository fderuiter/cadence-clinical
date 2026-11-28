using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Domain;
using Cadence.Modules.Execution.Infrastructure;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Modules.Execution.Endpoints;

internal static class FormQueryReadEndpoints
{
    public static async Task<Results<Ok<IReadOnlyList<FormQueryResponse>>, ProblemHttpResult>> ListForFormAsync(
        Guid formId,
        FormQueryTemporalFilter temporalFilter,
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
            return FormQueryFilterParser.FormNotFound(formId);
        }

        if (!await access.CanSeeAsync(tenant, NodeRef.For(header.EnrollmentId), cancellationToken).ConfigureAwait(false))
        {
            return FormQueryFilterParser.NotVisible($"form {formId:D}");
        }

        var queries = await FormQueryStoreReader.ReadAsync(contexts, tenant, q => q.FormId == form, temporalFilter.HasCoordinate, cancellationToken).ConfigureAwait(false);
        var states = FormQueryStoreReader.ProjectStates(queries, temporalFilter, clock);
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
        var queryContext = await FormQueryStoreReader.BuildFormContextAsync(
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

    public static async Task<Results<Ok<IReadOnlyList<FormQueryResponse>>, ProblemHttpResult>> ListQueriesAsync(
        FormQueryListFilter filter,
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
            return FormQueryFilterParser.BadRequest("Name the enrollment with enrollment_id or the study with study_id.");
        }

        if (hasEnrollment && hasStudy)
        {
            return FormQueryFilterParser.BadRequest("Specify either enrollment_id or study_id, not both.");
        }

        var limit = filter.Limit ?? FormQueryEndpoints.DefaultQueryLimit;
        if (limit is <= 0 or > FormQueryEndpoints.MaxQueryLimit)
        {
            return FormQueryFilterParser.BadRequest($"limit must be between 1 and {FormQueryEndpoints.MaxQueryLimit}.");
        }

        var offset = filter.Offset ?? 0;
        if (offset < 0)
        {
            return FormQueryFilterParser.BadRequest("offset must be zero or greater.");
        }

        if (!FormQueryFilterParser.TryParseFilters(filter, out var parsedFilters, out var filterError))
        {
            return FormQueryFilterParser.BadRequest(filterError!);
        }

        var tenant = KernelRequestTenant.Get(context);
        return hasEnrollment
            ? await ListByEnrollmentAsync(EnrollmentId.FromGuid(filter.EnrollmentId!.Value), parsedFilters, filter, limit, offset, tenant, forms, contexts, access, navigator, clock, cancellationToken).ConfigureAwait(false)
            : await ListByStudyAsync(StudyId.FromGuid(filter.StudyId!.Value), parsedFilters, filter, limit, offset, tenant, forms, contexts, access, navigator, clock, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Results<Ok<IReadOnlyList<FormQueryAgingDigestResponse>>, ProblemHttpResult>> ListAgingDigestsAsync(
        Guid? studyId,
        string? site,
        string? recipientRole,
        HttpContext context,
        ICollectionInstanceRecords forms,
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        IKernelReadAccess access,
        IKernelNavigator navigator,
        CancellationToken cancellationToken)
    {
        if (studyId is not { } studyGuid || studyGuid == Guid.Empty)
        {
            return FormQueryFilterParser.BadRequest("Specify study_id to list query aging digests.");
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
            return FormQueryFilterParser.NotVisible($"study {studyGuid:D}");
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

        var hasStudyWideVisibility = await access.CoversScopeAsync(tenant, studyNode, cancellationToken).ConfigureAwait(false);
        var siteNodes = digests
            .Where(d => d.StudySiteId.HasValue)
            .Select(d => NodeRef.For(d.StudySiteId!.Value))
            .Distinct()
            .ToList();
        var visibleSites = siteNodes.Count > 0
            ? await access.FilterVisibleAsync(tenant, siteNodes, cancellationToken).ConfigureAwait(false)
            : new HashSet<NodeRef>();

        var scopedDigests = new List<FormQueryAgingDigest>();
        foreach (var digest in digests)
        {
            if (digest.StudySiteId is null && !hasStudyWideVisibility)
            {
                continue;
            }

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

            scopedDigests.Add(digest);
        }

        var redactedEntryLists = await FormQueryDigestBlinding.RedactBatchAsync(
                tenant,
                [.. scopedDigests.Select(static d => d.Entries)],
                forms,
                contexts,
                access,
                alwaysMaskBlinded: false,
                cancellationToken)
            .ConfigureAwait(false);
        var visible = new List<FormQueryAgingDigestResponse>(scopedDigests.Count);
        for (var i = 0; i < scopedDigests.Count; i++)
        {
            visible.Add(FormQueryAgingDigestResponse.From(scopedDigests[i], redactedEntryLists[i]));
        }

        return TypedResults.Ok<IReadOnlyList<FormQueryAgingDigestResponse>>(visible);
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
            return FormQueryFilterParser.NotVisible($"enrollment {enrollment.Value:D}");
        }

        var hasContextFilter = !string.IsNullOrWhiteSpace(filter.Site) || !string.IsNullOrWhiteSpace(filter.Participant);
        if (!filter.HasTemporalCoordinate && !hasContextFilter && filter.ValueChanged is null && parsedFilters.Assignee is null)
        {
            var pagedQueries = await FormQueryStoreReader.ReadCurrentPageAsync(
                contexts, tenant, q => q.EnrollmentId == enrollment, parsedFilters, filter.Escalated, offset, limit, cancellationToken).ConfigureAwait(false);
            var pagedStates = FormQueryStoreReader.ProjectStates(pagedQueries, filter.Temporal, clock);
            var pagedResponses = await FormQueryStoreReader.ShapeQueriesAsync(pagedStates, null, null, tenant, forms, access, navigator, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>(pagedResponses);
        }

        var rawQueries = filter.HasTemporalCoordinate
            ? await FormQueryStoreReader.ReadAsync(contexts, tenant, q => q.EnrollmentId == enrollment, includePeriods: true, cancellationToken).ConfigureAwait(false)
            : await FormQueryStoreReader.ReadCurrentAsync(contexts, tenant, q => q.EnrollmentId == enrollment, parsedFilters, filter.Escalated, cancellationToken).ConfigureAwait(false);
        var candidateStates = FormQueryStoreReader.FilterStates(FormQueryStoreReader.ProjectStates(rawQueries, filter.Temporal, clock), parsedFilters, filter.Escalated);
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

                    return FormQueryFilterParser.MatchesSiteFilter(header, filter.Site)
                        && FormQueryFilterParser.MatchesParticipantFilter(enrollment, header, filter.Participant);
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
            candidateStates = [.. candidateStates.Where(s => FormQueryStoreReader.IsValueChanged(s, preloadedRevisions) == wantedChanged)];
            if (candidateStates.Count == 0)
            {
                return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>([]);
            }
        }

        if (parsedFilters.Assignee is not null)
        {
            candidateStates = await FormQueryStoreReader.ExcludeBlindedCandidatesAsync(
                candidateStates, tenant, forms, access, cancellationToken).ConfigureAwait(false);
            if (candidateStates.Count == 0)
            {
                return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>([]);
            }
        }

        var finalPage = candidateStates.Skip(offset).Take(limit).ToList();
        var responses = await FormQueryStoreReader.ShapeQueriesAsync(
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
            return FormQueryFilterParser.NotVisible($"study {study.Value:D}");
        }

        var visibleEnrollments = new Dictionary<EnrollmentId, EnrollmentHeader>();
        foreach (var member in studyEnrollments)
        {
            if (!visibleNodes.Contains(NodeRef.For(member.EnrollmentId)))
            {
                continue;
            }

            var header = new EnrollmentHeader(study.Value, member.StudySiteId?.Value, member.SiteCode, member.SubjectCode);
            if (!FormQueryFilterParser.MatchesSiteFilter(header, filter.Site) || !FormQueryFilterParser.MatchesParticipantFilter(member.EnrollmentId, header, filter.Participant))
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
        if (!filter.HasTemporalCoordinate && filter.ValueChanged is null && parsedFilters.Assignee is null)
        {
            var queries = await FormQueryStoreReader.ReadCurrentPageAsync(
                contexts, tenant, q => allowed.Contains(q.EnrollmentId), parsedFilters, filter.Escalated, offset, limit, cancellationToken).ConfigureAwait(false);
            var states = FormQueryStoreReader.ProjectStates(queries, filter.Temporal, clock);
            var responses = await FormQueryStoreReader.ShapeQueriesAsync(states, visibleEnrollments, null, tenant, forms, access, navigator, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>(responses);
        }

        var rawQueries = filter.HasTemporalCoordinate
            ? await FormQueryStoreReader.ReadAsync(contexts, tenant, q => allowed.Contains(q.EnrollmentId), includePeriods: true, cancellationToken).ConfigureAwait(false)
            : await FormQueryStoreReader.ReadCurrentAsync(contexts, tenant, q => allowed.Contains(q.EnrollmentId), parsedFilters, filter.Escalated, cancellationToken).ConfigureAwait(false);
        var candidateStates = FormQueryStoreReader.FilterStates(FormQueryStoreReader.ProjectStates(rawQueries, filter.Temporal, clock), parsedFilters, filter.Escalated);
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
            candidateStates = [.. candidateStates.Where(s => FormQueryStoreReader.IsValueChanged(s, preloadedRevisions) == wantedChanged)];
            if (candidateStates.Count == 0)
            {
                return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>([]);
            }
        }

        if (parsedFilters.Assignee is not null)
        {
            candidateStates = await FormQueryStoreReader.ExcludeBlindedCandidatesAsync(
                candidateStates, tenant, forms, access, cancellationToken).ConfigureAwait(false);
            if (candidateStates.Count == 0)
            {
                return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>([]);
            }
        }

        var pagedStates = candidateStates.Skip(offset).Take(limit).ToList();
        var shaped = await FormQueryStoreReader.ShapeQueriesAsync(
            pagedStates, visibleEnrollments, preloadedRevisions, tenant, forms, access, navigator, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>(shaped);
    }
}
