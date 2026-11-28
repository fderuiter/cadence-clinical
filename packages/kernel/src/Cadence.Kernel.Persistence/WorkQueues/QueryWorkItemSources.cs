using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.WorkQueues;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.WorkQueues;

/// <summary>The <see cref="KernelWorkItemSources.QueriesOpen"/> source: un-retracted queries awaiting an answer.</summary>
internal sealed class QueriesOpenSource(KernelTenantDbContextFactory contexts) : IWorkItemSource
{
    public string Name => KernelWorkItemSources.QueriesOpen;

    public Task<IReadOnlyList<WorkItemCandidate>> CandidatesAsync(
        TenantId tenant, NodeRef? within, int take, CancellationToken cancellationToken) =>
        QueryCandidatesReader.ReadAsync(contexts, tenant, within, answered: false, take, cancellationToken);
}

/// <summary>The <see cref="KernelWorkItemSources.QueriesAnswered"/> source: answered queries awaiting closure.</summary>
internal sealed class QueriesAnsweredSource(KernelTenantDbContextFactory contexts) : IWorkItemSource
{
    public string Name => KernelWorkItemSources.QueriesAnswered;

    public Task<IReadOnlyList<WorkItemCandidate>> CandidatesAsync(
        TenantId tenant, NodeRef? within, int take, CancellationToken cancellationToken) =>
        QueryCandidatesReader.ReadAsync(contexts, tenant, within, answered: true, take, cancellationToken);
}

internal static class QueryCandidatesReader
{
    public static async Task<IReadOnlyList<WorkItemCandidate>> ReadAsync(
        KernelTenantDbContextFactory contexts,
        TenantId tenant,
        NodeRef? within,
        bool answered,
        int take,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        await using var db = contexts.Create(tenant);
        var queries = db.Assertions.AsNoTracking()
            .Where(q => q.TenantId == tenant && q.Kind == AssertionKind.Queried && q.RetractsId == null)
            .Where(q => !db.Assertions.Any(r => r.TenantId == tenant && r.Kind == AssertionKind.Queried && r.RetractsId == q.Id));

        switch (within)
        {
            case null:
                break;
            case { Kind: NodeKinds.Tenant } when within.Id == tenant.Value:
                break;
            case { Kind: NodeKinds.Organization }:
                var org = OrganizationId.FromGuid(within.Id);
                queries = queries.Where(q => db.Studies.Any(s => s.Id == q.StudyId && s.SponsorId == org));
                break;
            case { Kind: NodeKinds.Study }:
                var study = StudyId.FromGuid(within.Id);
                queries = queries.Where(q => q.StudyId == study);
                break;
            case { Kind: NodeKinds.StudySite }:
                var site = StudySiteId.FromGuid(within.Id);
                queries = queries.Where(q => q.EnrollmentId != null && db.Enrollments.Any(e => e.Id == q.EnrollmentId && e.StudySiteId == site));
                break;
            case { Kind: NodeKinds.Enrollment }:
                var enrollment = EnrollmentId.FromGuid(within.Id);
                queries = queries.Where(q => q.EnrollmentId == enrollment);
                break;
            case { Kind: NodeKinds.CollectionInstance }:
                var formId = CollectionInstanceId.FromGuid(within.Id);
                var form = await db.CollectionInstances.AsNoTracking()
                    .SingleOrDefaultAsync(c => c.Id == formId, cancellationToken)
                    .ConfigureAwait(false);
                if (form is null || form.Members.Count == 0)
                {
                    return [];
                }

                var memberGuids = form.Members.Select(m => m.ObservationId.Value).ToList();
                queries = queries.Where(q => q.TargetKind == NodeKinds.Observation && memberGuids.Contains(q.TargetId));
                break;
            case { Kind: NodeKinds.Observation }:
                queries = queries.Where(q => q.TargetKind == NodeKinds.Observation && q.TargetId == within.Id);
                break;
            case { Kind: NodeKinds.Assertion }:
                var assertionId = AssertionId.FromGuid(within.Id);
                queries = queries.Where(q => q.Id == assertionId);
                break;
            default:
                return [];
        }

        var activeQueries = await queries
            .Select(q => new { q.Id, q.VersionIndex, q.EnrollmentId, q.Text, q.AssertedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (activeQueries.Count == 0)
        {
            return [];
        }

        var queryGuids = activeQueries.Select(q => q.Id.Value).ToList();
        var activeAnswers = await db.Assertions.AsNoTracking()
            .Where(a => a.TenantId == tenant
                && a.Kind == AssertionKind.QueryAnswered
                && a.RetractsId == null
                && a.TargetKind == NodeKinds.Assertion
                && queryGuids.Contains(a.TargetId)
                && !db.Assertions.Any(r => r.TenantId == tenant && r.Kind == AssertionKind.QueryAnswered && r.RetractsId == a.Id))
            .Select(a => new { a.TargetId, a.AssertedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var latestAnswerByQuery = activeAnswers
            .GroupBy(a => a.TargetId)
            .ToDictionary(g => g.Key, g => g.Max(a => a.AssertedAt));

        var matching = activeQueries
            .Where(q => latestAnswerByQuery.ContainsKey(q.Id.Value) == answered)
            .ToList();
        if (matching.Count == 0)
        {
            return [];
        }

        var enrollmentIds = matching.Where(q => q.EnrollmentId is not null).Select(q => q.EnrollmentId!.Value).Distinct().ToList();
        var enrollmentRows = enrollmentIds.Count == 0
            ? []
            : await db.Enrollments.AsNoTracking()
                .Where(e => enrollmentIds.Contains(e.Id))
                .Select(e => new { e.Id, e.SubjectCode })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        var subjectsByEnrollment = enrollmentRows.ToDictionary(e => e.Id, e => e.SubjectCode.Value);

        return
        [
            .. matching
                .Select(q =>
                {
                    var since = answered ? latestAnswerByQuery[q.Id.Value] : q.AssertedAt;
                    var prefix = q.EnrollmentId is { } eid && subjectsByEnrollment.TryGetValue(eid, out var code)
                        ? string.Create(CultureInfo.InvariantCulture, $"Query on {code}: ")
                        : "Query: ";
                    return new WorkItemCandidate(
                        NodeRef.For(q.Id, q.VersionIndex),
                        prefix + WorkItemFormatting.Snippet(q.Text, 80),
                        since);
                })
                .OrderBy(c => c.Since)
                .ThenBy(c => c.Subject.Id)
                .Take(take),
        ];
    }
}
