using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
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

using Microsoft.EntityFrameworkCore;

namespace Cadence.Modules.Execution.Endpoints;

internal static class FormQueryStoreReader
{
    public static List<FormQueryState> ProjectStates(
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

    public static async Task<List<FormQueryResponse>> ShapeQueriesAsync(
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

    public static bool IsValueChanged(FormQueryState query, IReadOnlyDictionary<ObservationId, int> revisions) =>
        revisions.TryGetValue(query.ObservationId, out var currentRevision) && currentRevision > query.Revision;

    public static List<FormQueryState> FilterStates(
        List<FormQueryState> states,
        ParsedQueryFilters parsed,
        bool? escalated)
    {
        if (parsed.Status is null
            && parsed.OwnerRole is null
            && parsed.Origin is null
            && parsed.Priority is null
            && parsed.Assignee is null
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
                && (parsed.Assignee is not { } assignee || FormQueryFilterParser.MatchesAssigneeFilter(s.Assignee, assignee))
                && (escalated is not { } esc || (s.EscalatedAt is not null) == esc)),
        ];
    }

    public static async Task<List<FormQueryState>> ExcludeBlindedCandidatesAsync(
        List<FormQueryState> states,
        TenantId tenant,
        ICollectionInstanceRecords forms,
        IKernelReadAccess access,
        CancellationToken cancellationToken)
    {
        if (states.Count == 0)
        {
            return states;
        }

        var candidateFormIds = states
            .Select(static s => s.FormId)
            .Distinct()
            .ToList();
        var targetsById = await forms.FindQueryTargetsAsync(
            tenant,
            candidateFormIds,
            FrozenSet<EnrollmentId>.Empty,
            cancellationToken).ConfigureAwait(false);

        var blindedQueryIds = states
            .Where(s => targetsById.TryGetValue(s.FormId, out var target) && target.IsQueryBlinded(s.ItemId, s.ObservationId))
            .Select(static s => s.QueryId)
            .ToHashSet();
        if (blindedQueryIds.Count == 0)
        {
            return states;
        }

        var blindedEnrollmentIds = states
            .Where(s => blindedQueryIds.Contains(s.QueryId))
            .Select(static s => s.EnrollmentId)
            .Distinct()
            .ToList();
        var unblindedEnrollments = await access.CanUnblindManyAsync(
            tenant,
            blindedEnrollmentIds,
            cancellationToken).ConfigureAwait(false);

        return [
            .. states.Where(s =>
                !blindedQueryIds.Contains(s.QueryId)
                || unblindedEnrollments.Contains(s.EnrollmentId)),
        ];
    }

    public static async Task<FormQueryContext> BuildFormContextAsync(
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

    public static async Task<List<FormQuery>> ReadAsync(
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        TenantId tenant,
        Expression<Func<FormQuery, bool>> filter,
        bool includePeriods,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        IQueryable<FormQuery> query = includePeriods
            ? db.FormQueries.AsNoTracking()
            : db.FormQueries.AsNoTracking()
                .IgnoreAutoIncludes()
                .Include(q => q.MessageRecords);
        return await query
            .Where(filter)
            .OrderBy(q => q.CreatedAt)
            .ThenBy(q => q.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<List<FormQuery>> ReadCurrentAsync(
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        TenantId tenant,
        Expression<Func<FormQuery, bool>> scopeFilter,
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

    public static async Task<List<FormQuery>> ReadCurrentPageAsync(
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        TenantId tenant,
        Expression<Func<FormQuery, bool>> scopeFilter,
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

        if (parsed.Assignee is { } assignee)
        {
            var lower = assignee.ToLowerInvariant();
            var prefixedLower = lower.StartsWith("u:", StringComparison.Ordinal) ? lower : "u:" + lower;
            var unprefixedLower = lower.StartsWith("u:", StringComparison.Ordinal) ? lower[2..] : lower;
            query = query.Where(q => q.Assignee != null
                && (q.Assignee.ToLower() == lower
                    || q.Assignee.ToLower() == prefixedLower
                    || q.Assignee.ToLower() == unprefixedLower));
        }

        return query;
    }
}
