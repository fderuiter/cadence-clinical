using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Protocol.Design;
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
        var foundInstance = await db.CollectionInstances.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Members.Any(m => m.ObservationId == found.Id), cancellationToken)
            .ConfigureAwait(false);
        CollectionInstanceId? instance = foundInstance?.Id;
        string? itemId = null;
        CollectionItem? specItem = null;
        if (foundInstance is not null)
        {
            itemId = foundInstance.Members.FirstOrDefault(m => m.ObservationId == found.Id)?.ItemId;
            if (itemId is not null)
            {
                var content = await db.ProtocolVersions.AsNoTracking()
                    .Where(v => v.Id == foundInstance.ProtocolVersionId)
                    .Select(v => v.Content)
                    .SingleOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (content is not null)
                {
                    specItem = ProtocolDesignDocument.Read(content).Design?.CollectionSpecs
                        .FirstOrDefault(s => string.Equals(s.Id, foundInstance.CollectionSpecId, StringComparison.Ordinal))
                        ?.Items.FirstOrDefault(i => string.Equals(i.Id, itemId, StringComparison.Ordinal));
                }
            }
        }

        var scopes = LockScopes.Covering(found.StudyId, studySite, found.EnrollmentId, found.Id, instance);
        var assertions = await ScopedAssertions.ReadAsync(db, scopes, cancellationToken).ConfigureAwait(false);
        var locks = assertions.Locks();
        var about = assertions.About(a => a.TargetId == found.Id.Value);

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
            about,
            audit,
            instance,
            itemId,
            specItem?.ExpectedKind,
            specItem?.Unit,
            specItem?.Terminology?.CodeSystemVersionId);
    }
}
