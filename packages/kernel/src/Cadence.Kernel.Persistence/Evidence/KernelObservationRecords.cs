using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Evidence;

/// <summary>
/// Reads an observation, its revisions, its lock and its kernel audit entries through a context bound to the caller's
/// tenant (ADR-2250). Read-only: no tracking.
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

        var lockId = ObservationLockId.For(observation);
        var locked = await db.ObservationLocks.AsNoTracking()
            .SingleOrDefaultAsync(l => l.Id == lockId, cancellationToken)
            .ConfigureAwait(false);

        // The transaction stamps AggregateId with the key's text, which for both aggregates is the observation's UUID.
        var key = observation.ToString();
        var audit = await db.AuditEntries.AsNoTracking()
            .Where(a => a.AggregateId == key && (a.AggregateType == nameof(Observation) || a.AggregateType == nameof(ObservationLock)))
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
            locked is null ? null : new ObservationLockView(locked.LockedAt, locked.CreatedBy, locked.ReasonForChange),
            views,
            audit);
    }
}
