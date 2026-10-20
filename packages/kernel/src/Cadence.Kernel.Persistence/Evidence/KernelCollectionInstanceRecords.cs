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
/// Reads a form instance, its member observations, the assertions and locks that concern them and the signatures over
/// the form through a context bound to the caller's tenant (ADR-2250). Read-only: no tracking.
/// </summary>
internal sealed class KernelCollectionInstanceRecords(KernelTenantDbContextFactory contexts) : ICollectionInstanceRecords
{
    public async Task<CollectionInstanceRecord?> FindAsync(TenantId tenant, CollectionInstanceId instance, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var found = await db.CollectionInstances.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == instance, cancellationToken)
            .ConfigureAwait(false);
        if (found is null)
        {
            return null;
        }

        var memberIds = found.Members.Select(m => m.ObservationId).ToList();
        var observations = (await db.Observations.AsNoTracking()
                .Where(o => memberIds.Contains(o.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(o => o.Id);
        var values = found.Members
            .OrderBy(m => m.ItemId, StringComparer.Ordinal)
            .Select(m => (m.ItemId, Observation: observations[m.ObservationId]))
            .Select(x => (x.ItemId, x.Observation, Current: x.Observation.Revisions.Single(r => r.Number == x.Observation.Revision)))
            .Select(x => new CollectedValueView(
                x.ItemId, x.Observation.Id, x.Observation.Concept,
                x.Observation.Value, x.Observation.ObservedAt, x.Observation.Revision, x.Current.RecordedBy, x.Current.RecordedAt))
            .ToList();

        // The locks at every scope covering the form or a value, and the assertions about its values.
        var scopes = LockScopes.Covering(found.StudyId, found.StudySiteId, found.EnrollmentId, found.Id, memberIds);
        var scoped = await ScopedAssertions.ReadAsync(db, scopes, cancellationToken).ConfigureAwait(false);
        var memberGuids = memberIds.Select(m => m.Value).ToHashSet();
        var assertions = scoped.About(a => a.TargetKind == NodeKinds.Observation && memberGuids.Contains(a.TargetId));

        // The form's layout: the specification's items in the version that governs it.
        var content = await db.ProtocolVersions.AsNoTracking()
            .Where(v => v.Id == found.ProtocolVersionId)
            .Select(v => v.Content)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        var items = ProtocolDesignDocument.Read(content).Design?.CollectionSpecs
            .FirstOrDefault(s => string.Equals(s.Id, found.CollectionSpecId, StringComparison.Ordinal))?.Items ?? [];

        var instanceGuid = found.Id.Value;
        var attestations = await db.Attestations.AsNoTracking()
            .Where(a => a.TargetKind == NodeKinds.CollectionInstance && a.TargetId == instanceGuid)
            .OrderBy(a => a.RecordedAt).ThenBy(a => a.Id)
            .Select(a => new FormAttestationView(a.Id, a.Meaning, a.ActorId, a.RecordedAt, a.TargetContentHash, a.Reason))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new CollectionInstanceRecord(
            found.Id,
            found.EnrollmentId,
            found.ParticipantId,
            found.StudyId,
            found.StudySiteId,
            found.ProtocolVersionId,
            found.CollectionSpecId,
            found.PlannedActivityId,
            found.CollectedAt,
            found.OpenedBy,
            found.OpenedAt,
            found.VersionIndex,
            items,
            values,
            scoped.Locks(),
            assertions,
            attestations);
    }
}
