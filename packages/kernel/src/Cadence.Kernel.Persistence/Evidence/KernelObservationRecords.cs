using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Evidence;

/// <summary>
/// Reads an observation, its revisions, the locks that cover it, the assertions about it and its kernel audit entries
/// through a context bound to the caller's tenant (ADR-2250). Read-only: no tracking.
/// </summary>
internal sealed class KernelObservationRecords(KernelTenantDbContextFactory contexts) : IObservationRecords
{
    public async Task<ObservationRecord?> FindAsync(TenantId tenant, ObservationId observation, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var found = await db.Observations.AsNoTracking()
            .SingleOrDefaultAsync(o => o.Id == observation, cancellationToken)
            .ConfigureAwait(false);
        if (found is null)
        {
            return null;
        }

        var studySite = await db.Enrollments.AsNoTracking()
            .Where(e => e.Id == found.EnrollmentId)
            .Select(e => e.StudySiteId)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        var scopes = LockScopes.Covering(found.StudyId, studySite, found.EnrollmentId, found.Id);
        var scopeIds = scopes.Select(s => s.Id).ToList();

        // The assertions about the observation, the answers to its queries, the locks at its scopes and every retraction
        // of them: assertions are few per observation, so one read and an in-memory join.
        var direct = await db.Assertions.AsNoTracking()
            .Where(a => scopeIds.Contains(a.TargetId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var queryIds = direct.Where(a => a.Kind == AssertionKind.Queried && a.RetractsId == null).Select(a => a.Id.Value).ToList();
        var answers = queryIds.Count == 0
            ? []
            : await db.Assertions.AsNoTracking()
                .Where(a => a.Kind == AssertionKind.QueryAnswered && queryIds.Contains(a.TargetId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        var all = direct.Concat(answers)
            .Where(a => scopes.Any(s => s.Kind == a.TargetKind && s.Id == a.TargetId) || a.TargetKind == NodeKinds.Assertion)
            .ToList();
        var retractedBy = all.Where(a => a.RetractsId is not null).ToDictionary(a => a.RetractsId!.Value);
        var stated = all.Where(a => a.RetractsId is null).OrderBy(a => a.AssertedAt).ThenBy(a => a.Id).ToList();

        var locks = stated
            .Where(a => a.IsLock && !retractedBy.ContainsKey(a.Id))
            .OrderBy(a => Width(scopes, a))
            .Select(a => new LockView(a.Id, (LockKind)a.Kind, a.Target, a.AttestationId!.Value, a.AssertedAt, a.AssertedBy, a.ReasonForChange))
            .ToList();
        var assertions = stated
            .Where(a => !a.IsLock && (a.TargetId == found.Id.Value || a.TargetKind == NodeKinds.Assertion))
            .Select(a => retractedBy.TryGetValue(a.Id, out var retraction)
                ? View(a, retraction.Id, retraction.AssertedAt)
                : View(a, null, null))
            .ToList();

        // The transaction stamps AggregateId with the key's text, the observation's UUID.
        var key = observation.ToString();
        var audit = await db.AuditEntries.AsNoTracking()
            .Where(a => a.AggregateId == key && a.AggregateType == nameof(Observation))
            .OrderBy(a => a.Sequence)
            .Select(a => new ObservationAuditView(a.Sequence, a.RecordedAt, a.ActorId, a.Action, a.AggregateType!, a.AggregateVersion, a.ReasonForChange))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var revisions = found.Revisions.OrderBy(r => r.Number).ToList();
        var views = revisions
            .Select((r, i) => new ObservationRevisionView(
                r.Number, r.Value, r.ReasonForChange, r.RecordedBy, r.RecordedAt, i + 1 < revisions.Count ? revisions[i + 1].RecordedAt : (DateTimeOffset?)null))
            .ToList();
        return new ObservationRecord(
            found.Id,
            found.EnrollmentId,
            found.ParticipantId,
            found.StudyId,
            found.Concept,
            found.Value,
            found.ObservedAt,
            found.VersionIndex,
            locks,
            views,
            assertions,
            audit);
    }

    private static int Width(IReadOnlyList<NodeRef> scopes, Assertion assertion)
    {
        for (var i = 0; i < scopes.Count; i++)
        {
            if (scopes[i].Kind == assertion.TargetKind && scopes[i].Id == assertion.TargetId)
            {
                return i;
            }
        }

        return scopes.Count;
    }

    private static AssertionView View(Assertion a, AssertionId? retractedBy, DateTimeOffset? retractedAt) =>
        new(a.Id, a.Kind, a.Target, a.TargetRevision, a.Text, a.AssertedBy, a.AssertedAt, a.ReasonForChange, retractedBy, retractedAt);
}
