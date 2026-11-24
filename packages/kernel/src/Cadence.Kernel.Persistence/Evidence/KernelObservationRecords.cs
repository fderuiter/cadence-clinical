using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
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
internal sealed class KernelObservationRecords(
    KernelTenantDbContextFactory contexts,
    IAuthorizationProbe? authorizationProbe = null) : IObservationRecords
{
    private static readonly Capability EnrollmentUnblindCapability = new("enrollment.unblind", VerbClass.Read);

    public async Task<ObservationRecord?> FindForReaderAsync(
        TenantId tenant,
        ObservationId observation,
        bool unblinded,
        CancellationToken cancellationToken)
    {
        var record = await ReadCoreAsync(
            tenant,
            observation,
            includeFullBlindedClassification: !unblinded,
            cancellationToken).ConfigureAwait(false);
        if (record is null || unblinded)
        {
            return record;
        }

        return record.IsBlindedTreatment ? record.MaskBlinded() : record;
    }

    public async Task<ObservationRecord?> FindForReaderAsync(
        TenantId tenant,
        ObservationId observation,
        Func<EnrollmentId, CancellationToken, ValueTask<bool>> canUnblindAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canUnblindAsync);
        var record = await ReadCoreAsync(
            tenant,
            observation,
            includeFullBlindedClassification: true,
            cancellationToken).ConfigureAwait(false);
        if (record is null || !record.IsBlindedTreatment)
        {
            return record;
        }

        var unblinded = await canUnblindAsync(record.EnrollmentId, cancellationToken).ConfigureAwait(false);
        return unblinded ? record : record.MaskBlinded();
    }

    public async Task<ObservationRecord?> FindForReaderAsync(
        TenantId tenant,
        ObservationId observation,
        CancellationToken cancellationToken)
    {
        var record = await ReadCoreAsync(
            tenant,
            observation,
            includeFullBlindedClassification: true,
            cancellationToken).ConfigureAwait(false);
        if (record is null || !record.IsBlindedTreatment)
        {
            return record;
        }

        if (authorizationProbe is null)
        {
            return record.MaskBlinded();
        }

        var decision = await authorizationProbe.CanIAsync(
            tenant,
            EnrollmentUnblindCapability,
            NodeRef.For(record.EnrollmentId),
            cancellationToken).ConfigureAwait(false);
        return decision.Permitted ? record : record.MaskBlinded();
    }

    public Task<ObservationRecord?> FindAsync(TenantId tenant, ObservationId observation, CancellationToken cancellationToken) =>
        ReadCoreAsync(tenant, observation, includeFullBlindedClassification: false, cancellationToken);

    private async Task<ObservationRecord?> ReadCoreAsync(
        TenantId tenant,
        ObservationId observation,
        bool includeFullBlindedClassification,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
            List<ProtocolDesign> designs = [];

            if (includeFullBlindedClassification)
            {
                var studyVersions = await db.ProtocolVersions.AsNoTracking()
                    .Where(v => v.StudyId == found.StudyId)
                    .Select(v => new { v.Id, v.Content })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                designs = studyVersions
                    .Select(v => ProtocolDesignDocument.Read(v.Content).Design)
                    .Where(d => d is not null)
                    .Select(d => d!)
                    .ToList();

                if (foundInstance is not null)
                {
                    itemId = foundInstance.Members.FirstOrDefault(m => m.ObservationId == found.Id)?.ItemId;
                    if (itemId is not null)
                    {
                        var content = studyVersions.SingleOrDefault(v => v.Id == foundInstance.ProtocolVersionId)?.Content;
                        if (content is not null)
                        {
                            specItem = ProtocolDesignDocument.Read(content).Design?.CollectionSpecs
                                .FirstOrDefault(s => string.Equals(s.Id, foundInstance.CollectionSpecId, StringComparison.Ordinal))
                                ?.Items.FirstOrDefault(i => string.Equals(i.Id, itemId, StringComparison.Ordinal));
                        }
                    }
                }
            }
            else if (foundInstance is not null)
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
                        var parsedDesign = ProtocolDesignDocument.Read(content).Design;
                        if (parsedDesign is not null)
                        {
                            designs = [parsedDesign];
                            specItem = parsedDesign.CollectionSpecs
                                .FirstOrDefault(s => string.Equals(s.Id, foundInstance.CollectionSpecId, StringComparison.Ordinal))
                                ?.Items.FirstOrDefault(i => string.Equals(i.Id, itemId, StringComparison.Ordinal));
                        }
                    }
                }
            }

            Concept? concept = null;
            if (includeFullBlindedClassification)
            {
                concept = await db.Concepts.AsNoTracking()
                    .SingleOrDefaultAsync(c => c.Id == found.ConceptId, cancellationToken)
                    .ConfigureAwait(false);
            }

            var isBlindedTreatment = BlindedObservationClassifier.IsBlindedTreatment(
                found.ConceptId,
                found.Value,
                concept,
                foundInstance?.CollectionSpecId,
                itemId,
                designs)
                || found.Revisions.Any(r => BlindedObservationClassifier.IsBlindedTreatment(
                    found.ConceptId,
                    r.Value,
                    concept,
                    foundInstance?.CollectionSpecId,
                    itemId,
                    designs));

            var scopes = LockScopes.Covering(found.StudyId, studySite, found.EnrollmentId, found.Id, instance);
            var assertions = await ScopedAssertions.ReadAsync(db, scopes, cancellationToken).ConfigureAwait(false);
            var locks = assertions.Locks();
            var about = assertions.About(a => a.TargetId == found.Id.Value);

            // The transaction stamps AggregateId with the key's text, the aggregate's UUID.
            var key = observation.ToString();
            var assertionKeys = assertions
                .CoveringAuditIds(a => a.TargetKind == NodeKinds.Observation && a.TargetId == found.Id.Value)
                .Select(id => id.ToString())
                .ToList();
            var auditQuery = assertionKeys.Count == 0
                ? db.AuditEntries.AsNoTracking()
                    .Where(a => a.TenantId == tenant && a.AggregateType == nameof(Observation) && a.AggregateId == key)
                : db.AuditEntries.AsNoTracking()
                    .Where(a => a.TenantId == tenant
                        && ((a.AggregateType == nameof(Observation) && a.AggregateId == key)
                            || (a.AggregateType == nameof(Assertion) && a.AggregateId != null && assertionKeys.Contains(a.AggregateId))));
            var audit = await auditQuery
                .OrderBy(a => a.Sequence)
                .Select(a => new ObservationAuditView(a.Sequence, a.RecordedAt, a.ActorId, a.Action, a.AggregateType!, a.AggregateVersion, a.ReasonForChange))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            string? expectedCodelist = string.IsNullOrWhiteSpace(specItem?.Terminology?.Codelist)
                ? null
                : specItem.Terminology.Codelist;
            IReadOnlyList<CodelistOptionView>? expectedCodelistOptions = null;
            if (expectedCodelist is not null && specItem?.Terminology is { } terminology)
            {
                var rawEntries = await db.CodeSystemEntries.AsNoTracking()
                    .Where(e => e.CodeSystemVersionId == terminology.CodeSystemVersionId && e.GroupCode == expectedCodelist)
                    .OrderBy(e => e.Code)
                    .ThenBy(e => e.Ordinal)
                    .Select(e => new { e.Code, e.Display, e.SubmissionValue })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                expectedCodelistOptions = rawEntries
                    .DistinctBy(e => e.Code, StringComparer.Ordinal)
                    .Select(e => new CodelistOptionView(e.Code, e.Display, e.SubmissionValue))
                    .ToList();
            }

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
                specItem?.Terminology?.CodeSystemVersionId,
                expectedCodelist,
                expectedCodelistOptions,
                isBlindedTreatment);
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }
}
