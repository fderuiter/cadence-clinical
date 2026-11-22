using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Execution;

/// <summary>
/// Reads bitemporal <see cref="Applicability"/> and <see cref="ConsentStatus"/> records through tenant-bound contexts
/// (ADR-2250, spec §7.5, §9.4). Read-only: no tracking.
/// </summary>
internal sealed class KernelApplicabilityReader(KernelTenantDbContextFactory contexts) : IApplicabilityReader, IConsentStatusReader
{
    public async Task<ApplicabilityResolution> ResolveAsync(
        TenantId tenant,
        EnrollmentId enrollment,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        var applicability = await db.Applicabilities.AsNoTracking()
            .SingleOrDefaultAsync(a => a.TenantId == tenant && a.EnrollmentId == enrollment, cancellationToken)
            .ConfigureAwait(false);
        if (applicability is null)
        {
            return ApplicabilityResolution.Empty;
        }

        var period = applicability.PeriodAsOf(validAt, knownAt);
        var pending = applicability.PendingAsOf(validAt, knownAt);
        var versionIds = pending.Select(p => p.TargetVersionId).ToHashSet();
        if (period is not null)
        {
            versionIds.Add(period.ProtocolVersionId);
        }

        var numbers = versionIds.Count == 0
            ? new Dictionary<ProtocolVersionId, int>()
            : await db.ProtocolVersions.AsNoTracking()
                .Where(v => v.TenantId == tenant && versionIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.Number, cancellationToken)
                .ConfigureAwait(false);

        return new ApplicabilityResolution(
            applicability.Id,
            period?.ProtocolVersionId,
            period is not null ? numbers.GetValueOrDefault(period.ProtocolVersionId) : null,
            period?.Period.Valid.Start,
            applicability.IsInconsistent,
            applicability.InconsistencyReason,
            [.. pending.Select(p => new PendingReconsentTransition(
                p.FromVersionId,
                p.TargetVersionId,
                numbers.GetValueOrDefault(p.TargetVersionId),
                p.EffectivityId,
                p.RequiredSince))]);
    }

    public async Task<ApplicabilityState?> CurrentAsync(
        TenantId tenant,
        EnrollmentId enrollment,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        var applicability = await db.Applicabilities.AsNoTracking()
            .SingleOrDefaultAsync(a => a.TenantId == tenant && a.EnrollmentId == enrollment, cancellationToken)
            .ConfigureAwait(false);
        if (applicability is null)
        {
            return null;
        }

        var pending = applicability.PendingTransitions
            .Where(t => t.Period.Recorded.Contains(knownAt) && t.Period.Valid.IsOpen)
            .OrderBy(t => t.RequiredSince)
            .ToList();
        var currentPeriod = applicability.Periods
            .SingleOrDefault(p => p.Period.Recorded.Contains(knownAt) && p.Period.Valid.IsOpen);

        var versionIds = pending.Select(p => p.TargetVersionId).ToHashSet();
        if (currentPeriod is not null)
        {
            versionIds.Add(currentPeriod.ProtocolVersionId);
        }

        var numbers = versionIds.Count == 0
            ? new Dictionary<ProtocolVersionId, int>()
            : await db.ProtocolVersions.AsNoTracking()
                .Where(v => v.TenantId == tenant && versionIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.Number, cancellationToken)
                .ConfigureAwait(false);

        return new ApplicabilityState(
            applicability.Id,
            applicability.EnrollmentId,
            applicability.ParticipantId,
            applicability.StudyId,
            applicability.StudySiteId,
            currentPeriod?.ProtocolVersionId,
            currentPeriod is not null ? numbers.GetValueOrDefault(currentPeriod.ProtocolVersionId) : null,
            currentPeriod?.Period.Valid.Start,
            applicability.IsInconsistent,
            applicability.InconsistencyReason,
            [.. pending.Select(p => new PendingReconsentTransition(
                p.FromVersionId,
                p.TargetVersionId,
                numbers.GetValueOrDefault(p.TargetVersionId),
                p.EffectivityId,
                p.RequiredSince))]);
    }

    public async Task<IReadOnlyList<ConsentStatusState>> HistoryAsync(
        TenantId tenant,
        EnrollmentId enrollment,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        var records = await db.ConsentStatuses.AsNoTracking()
            .Where(c => c.TenantId == tenant && c.EnrollmentId == enrollment)
            .OrderBy(c => c.SignedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (records.Count == 0)
        {
            return [];
        }

        var versionIds = records.Select(r => r.ProtocolVersionId).Distinct().ToList();
        var numbers = await db.ProtocolVersions.AsNoTracking()
            .Where(v => v.TenantId == tenant && versionIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.Number, cancellationToken)
            .ConfigureAwait(false);

        var currentApplicability = await CurrentAsync(tenant, enrollment, knownAt, cancellationToken).ConfigureAwait(false);

        var states = new List<ConsentStatusState>(records.Count);
        foreach (var record in records)
        {
            var period = record.Periods
                .Where(p => p.Period.Recorded.Contains(knownAt))
                .OrderByDescending(p => p.Period.Valid.Start)
                .FirstOrDefault();
            if (period is null)
            {
                continue;
            }

            states.Add(new ConsentStatusState(
                record.Id,
                record.EnrollmentId,
                record.ParticipantId,
                record.StudyId,
                record.StudySiteId,
                record.ProtocolVersionId,
                numbers.GetValueOrDefault(record.ProtocolVersionId),
                period.Signed,
                period.Period.Valid.Start,
                period.BasisKind,
                period.AttestationId,
                period.ArtifactVersionId,
                period.Scopes,
                currentApplicability));
        }

        return states;
    }
}
