using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Evidence;

/// <summary>
/// Projects canonical kernel truth at <c>(validAt, knownAt)</c> for study exports and participant casebooks (K-TRUTH-1).
/// Strictly excludes <c>IdentityVault</c> direct identifiers; only pseudonymous kernel identifiers and
/// <c>Enrollment.SubjectCode</c> are read.
/// </summary>
internal sealed class KernelExportReader(KernelTenantDbContextFactory contexts) : IKernelExportReader
{
    public async Task<StudyExportSnapshot?> ReadStudySnapshotAsync(
        TenantId tenant,
        StudyId study,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var foundStudy = await db.Studies.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == study, cancellationToken)
            .ConfigureAwait(false);
        if (foundStudy is null)
        {
            return null;
        }

        var studySites = await db.StudySites.AsNoTracking()
            .Where(ss => ss.StudyId == study && ss.CreatedAt <= knownAt.UtcDateTime)
            .OrderBy(ss => ss.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var siteIds = studySites.Select(ss => ss.SiteId).Distinct().ToList();
        var sitesById = siteIds.Count == 0
            ? new Dictionary<SiteId, Site>()
            : await db.Sites.AsNoTracking()
                .Where(s => siteIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, cancellationToken)
                .ConfigureAwait(false);

        var siteSnapshots = studySites
            .Select(ss =>
            {
                sitesById.TryGetValue(ss.SiteId, out var site);
                return new ExportSiteSnapshot(
                    ss.Id,
                    ss.SiteId,
                    site?.SiteCode ?? ss.SiteId.ToString(),
                    site?.Name ?? ss.SiteId.ToString(),
                    site?.Country ?? string.Empty,
                    ss.Status);
            })
            .ToList();

        var siteCodeByStudySite = siteSnapshots.ToDictionary(s => s.StudySiteId, s => s.SiteCode);

        var enrollmentsRaw = await db.Enrollments.AsNoTracking()
            .Include(e => e.Periods)
            .Where(e => e.StudyId == study)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var enrollmentSnapshots = new List<ExportEnrollmentSnapshot>();
        var studySiteByEnrollment = new Dictionary<EnrollmentId, StudySiteId>();
        foreach (var e in enrollmentsRaw)
        {
            if (e.StatusAsOf(validAt, knownAt) is not { } statusAtCut)
            {
                continue;
            }

            studySiteByEnrollment[e.Id] = e.StudySiteId;
            enrollmentSnapshots.Add(new ExportEnrollmentSnapshot(
                e.Id,
                e.ParticipantId,
                e.StudySiteId,
                siteCodeByStudySite.GetValueOrDefault(e.StudySiteId, e.StudySiteId.ToString()),
                e.SubjectCode.Value,
                statusAtCut,
                e.EnrollmentNumber));
        }

        var includedEnrollmentIds = studySiteByEnrollment.Keys.ToHashSet();

        var collectionsRaw = await db.CollectionInstances.AsNoTracking()
            .Include(c => c.Members)
            .Where(c => c.StudyId == study && c.CollectedAt <= validAt && c.OpenedAt <= knownAt)
            .OrderBy(c => c.CollectedAt)
            .ThenBy(c => c.OpenedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        collectionsRaw = [.. collectionsRaw.Where(c => includedEnrollmentIds.Contains(c.EnrollmentId))];

        var observationsRaw = new List<Observation>();
        var observationStream = db.Observations.AsNoTracking()
            .Include(o => o.Revisions)
            .Where(o => o.StudyId == study && o.ObservedAt <= validAt)
            .OrderBy(o => o.ObservedAt)
            .ThenBy(o => o.Id)
            .AsAsyncEnumerable();
        await foreach (var obs in observationStream.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (includedEnrollmentIds.Contains(obs.EnrollmentId))
            {
                observationsRaw.Add(obs);
            }
        }

        var assertionsRaw = new List<Assertion>();
        var assertionStream = db.Assertions.AsNoTracking()
            .Where(a => a.StudyId == study && a.AssertedAt <= knownAt)
            .OrderBy(a => a.AssertedAt)
            .ThenBy(a => a.Id)
            .AsAsyncEnumerable();
        await foreach (var assertion in assertionStream.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            assertionsRaw.Add(assertion);
        }

        var (appliedLocks, releasesByLock) = IndexLocksAndEarliestReleases(assertionsRaw);

        var referencedProtocolIds = collectionsRaw.Select(c => c.ProtocolVersionId).ToHashSet();
        var protocols = await ReadProtocolsAsync(db, study, validAt, knownAt, referencedProtocolIds, cancellationToken).ConfigureAwait(false);
        var designs = protocols.Where(p => p.Design is not null).Select(p => p.Design!).ToList();

        var lockEntries = await KernelLockRecords.HydrateAsync(db, study, appliedLocks, releasesByLock, cancellationToken, validAt, knownAt)
            .ConfigureAwait(false);

        var (collectionSnapshots, observationSnapshots) = await BuildObservationsAndCollectionsAsync(
            db,
            protocols,
            designs,
            studySiteByEnrollment,
            collectionsRaw,
            observationsRaw,
            assertionsRaw,
            lockEntries,
            knownAt,
            cancellationToken).ConfigureAwait(false);

        var targetIds = new HashSet<Guid> { study.Value };
        foreach (var p in protocols) targetIds.Add(p.ProtocolVersionId.Value);
        foreach (var e in enrollmentSnapshots) targetIds.Add(e.EnrollmentId.Value);
        foreach (var c in collectionSnapshots) targetIds.Add(c.CollectionInstanceId.Value);
        foreach (var o in observationSnapshots) targetIds.Add(o.ObservationId.Value);
        foreach (var l in assertionsRaw) targetIds.Add(l.Id.Value);

        var attestations = await ReadAttestationsAsync(db, targetIds, knownAt, cancellationToken).ConfigureAwait(false);
        var auditKeys = observationSnapshots.Select(o => o.ObservationId.ToString())
            .Concat(lockEntries.Select(l => l.LockId.ToString()))
            .Concat(attestations.Select(a => a.AttestationId.ToString()))
            .ToHashSet(StringComparer.Ordinal);
        var blindedAuditKeys = BuildBlindedAuditKeys(collectionSnapshots, observationSnapshots, lockEntries, attestations, designs);
        var (audit, blindedAuditSequences, auditAggregateIdsBySequence) = await ReadAuditEntriesAsync(
            db,
            auditKeys,
            blindedAuditKeys,
            knownAt,
            cancellationToken).ConfigureAwait(false);

        return new StudyExportSnapshot(
            foundStudy.Id,
            foundStudy.StudyCode,
            foundStudy.Title,
            foundStudy.Phase,
            validAt,
            knownAt,
            protocols,
            siteSnapshots,
            enrollmentSnapshots,
            collectionSnapshots,
            observationSnapshots,
            lockEntries,
            attestations,
            audit,
            blindedAuditSequences,
            auditAggregateIdsBySequence);
    }

    public async Task<EnrollmentCasebookSnapshot?> ReadEnrollmentCasebookAsync(
        TenantId tenant,
        EnrollmentId enrollment,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var foundEnrollment = await db.Enrollments.AsNoTracking()
            .Include(e => e.Periods)
            .SingleOrDefaultAsync(e => e.Id == enrollment, cancellationToken)
            .ConfigureAwait(false);
        if (foundEnrollment is null)
        {
            return null;
        }

        var foundStudy = await db.Studies.AsNoTracking()
            .SingleAsync(s => s.Id == foundEnrollment.StudyId, cancellationToken)
            .ConfigureAwait(false);

        var studySite = await db.StudySites.AsNoTracking()
            .SingleAsync(ss => ss.Id == foundEnrollment.StudySiteId, cancellationToken)
            .ConfigureAwait(false);

        var site = await db.Sites.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == studySite.SiteId, cancellationToken)
            .ConfigureAwait(false);

        var statusAtCut = foundEnrollment.StatusAsOf(validAt, knownAt);

        var collectionsRaw = statusAtCut is null
            ? []
            : await db.CollectionInstances.AsNoTracking()
                .Include(c => c.Members)
                .Where(c => c.EnrollmentId == enrollment && c.CollectedAt <= validAt && c.OpenedAt <= knownAt)
                .OrderBy(c => c.CollectedAt)
                .ThenBy(c => c.OpenedAt)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var observationsRaw = new List<Observation>();
        if (statusAtCut is not null)
        {
            var observationStream = db.Observations.AsNoTracking()
                .Include(o => o.Revisions)
                .Where(o => o.EnrollmentId == enrollment && o.ObservedAt <= validAt)
                .OrderBy(o => o.ObservedAt)
                .ThenBy(o => o.Id)
                .AsAsyncEnumerable();
            await foreach (var obs in observationStream.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                observationsRaw.Add(obs);
            }
        }

        var assertionsRaw = await ReadEnrollmentAssertionsBoundedAsync(
            db,
            foundStudy.Id,
            foundEnrollment.StudySiteId,
            enrollment,
            knownAt,
            cancellationToken).ConfigureAwait(false);

        var (appliedLocks, releasesByLock) = IndexLocksAndEarliestReleases(assertionsRaw);

        var referencedProtocolIds = collectionsRaw.Select(c => c.ProtocolVersionId).ToHashSet();
        var protocols = await ReadProtocolsAsync(db, foundStudy.Id, validAt, knownAt, referencedProtocolIds, cancellationToken).ConfigureAwait(false);
        var designs = protocols.Where(p => p.Design is not null).Select(p => p.Design!).ToList();

        var lockEntries = await KernelLockRecords.HydrateAsync(db, foundStudy.Id, appliedLocks, releasesByLock, cancellationToken, validAt, knownAt)
            .ConfigureAwait(false);

        var studySiteByEnrollment = new Dictionary<EnrollmentId, StudySiteId> { [enrollment] = foundEnrollment.StudySiteId };
        var (collectionSnapshots, observationSnapshots) = await BuildObservationsAndCollectionsAsync(
            db,
            protocols,
            designs,
            studySiteByEnrollment,
            collectionsRaw,
            observationsRaw,
            assertionsRaw,
            lockEntries,
            knownAt,
            cancellationToken).ConfigureAwait(false);

        var obsAssertionViews = observationSnapshots
            .SelectMany(o => o.Assertions)
            .DistinctBy(a => a.AssertionId)
            .OrderBy(a => a.AssertedAt)
            .ThenBy(a => a.AssertionId)
            .ToList();

        var targetIds = new HashSet<Guid> { enrollment.Value };
        foreach (var c in collectionSnapshots) targetIds.Add(c.CollectionInstanceId.Value);
        foreach (var o in observationSnapshots) targetIds.Add(o.ObservationId.Value);
        foreach (var l in lockEntries)
        {
            targetIds.Add(l.LockId.Value);
            if (l.ReleaseAssertionId is { } relId)
            {
                targetIds.Add(relId.Value);
            }
        }

        var attestations = await ReadAttestationsAsync(db, targetIds, knownAt, cancellationToken).ConfigureAwait(false);

        var auditKeys = observationSnapshots.Select(o => o.ObservationId.ToString())
            .Concat(obsAssertionViews.Select(a => a.AssertionId.ToString()))
            .Concat(collectionSnapshots.Select(c => c.CollectionInstanceId.ToString()))
            .Concat(lockEntries.Select(l => l.LockId.ToString()))
            .Concat(attestations.Select(a => a.AttestationId.ToString()))
            .Append(enrollment.ToString())
            .ToHashSet(StringComparer.Ordinal);
        var blindedAuditKeys = BuildBlindedAuditKeys(collectionSnapshots, observationSnapshots, lockEntries, attestations, designs);
        var (audit, blindedAuditSequences, _) = await ReadAuditEntriesAsync(db, auditKeys, blindedAuditKeys, knownAt, cancellationToken).ConfigureAwait(false);

        var canonicalCutAt = ComputeEnrollmentCanonicalCut(
            foundStudy,
            studySite,
            foundEnrollment,
            collectionSnapshots,
            observationSnapshots,
            obsAssertionViews,
            lockEntries,
            attestations,
            audit,
            knownAt);

        return new EnrollmentCasebookSnapshot(
            foundStudy.Id,
            foundStudy.StudyCode,
            foundStudy.Title,
            foundEnrollment.StudySiteId,
            site?.SiteCode ?? studySite.SiteId.ToString(),
            site?.Name ?? studySite.SiteId.ToString(),
            foundEnrollment.Id,
            foundEnrollment.ParticipantId,
            foundEnrollment.SubjectCode.Value,
            statusAtCut,
            validAt,
            knownAt,
            protocols,
            collectionSnapshots,
            observationSnapshots,
            obsAssertionViews,
            lockEntries,
            attestations,
            audit,
            blindedAuditSequences,
            canonicalCutAt);
    }

    private static DateTimeOffset ComputeEnrollmentCanonicalCut(
        Study study,
        StudySite studySite,
        Enrollment enrollment,
        IReadOnlyList<ExportCollectionSnapshot> collections,
        IReadOnlyList<ExportObservationSnapshot> observations,
        IReadOnlyList<AssertionView> assertions,
        IReadOnlyList<StudyLockEntry> locks,
        IReadOnlyList<ExportAttestationSnapshot> attestations,
        IReadOnlyList<ObservationAuditView> audit,
        DateTimeOffset knownAt)
    {
        var watermark = new DateTimeOffset(DateTime.SpecifyKind(enrollment.CreatedAt, DateTimeKind.Utc));
        MaxBounded(ref watermark, new DateTimeOffset(DateTime.SpecifyKind(study.CreatedAt, DateTimeKind.Utc)), knownAt);
        MaxBounded(ref watermark, new DateTimeOffset(DateTime.SpecifyKind(studySite.CreatedAt, DateTimeKind.Utc)), knownAt);

        foreach (var period in enrollment.Periods)
        {
            MaxBounded(ref watermark, period.Period.Recorded.Start, knownAt);
            MaxBounded(ref watermark, period.Period.Valid.Start, knownAt);
        }

        foreach (var coll in collections)
        {
            MaxBounded(ref watermark, coll.OpenedAt, knownAt);
            MaxBounded(ref watermark, coll.CollectedAt, knownAt);
        }

        foreach (var obs in observations)
        {
            MaxBounded(ref watermark, obs.ObservedAt, knownAt);
            foreach (var rev in obs.Revisions)
            {
                MaxBounded(ref watermark, rev.RecordedAt, knownAt);
            }
        }

        foreach (var assertion in assertions)
        {
            MaxBounded(ref watermark, assertion.AssertedAt, knownAt);
            if (assertion.RetractedAt is { } retractedAt)
            {
                MaxBounded(ref watermark, retractedAt, knownAt);
            }
        }

        foreach (var lk in locks)
        {
            MaxBounded(ref watermark, lk.LockedAt, knownAt);
            if (lk.ReleasedAt is { } releasedAt)
            {
                MaxBounded(ref watermark, releasedAt, knownAt);
            }
        }

        foreach (var att in attestations)
        {
            MaxBounded(ref watermark, att.RecordedAt, knownAt);
        }

        foreach (var entry in audit)
        {
            MaxBounded(ref watermark, entry.RecordedAt, knownAt);
        }

        return watermark;
    }

    private static void MaxBounded(ref DateTimeOffset current, DateTimeOffset candidate, DateTimeOffset ceiling)
    {
        if (candidate <= ceiling && candidate > current)
        {
            current = candidate;
        }
    }

    internal static async Task<IReadOnlyList<ExportProtocolSnapshot>> ReadProtocolsAsync(
        KernelDbContext db,
        StudyId study,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        IReadOnlySet<ProtocolVersionId> referencedVersionIds,
        CancellationToken cancellationToken)
    {
        var knownAtUtc = knownAt.UtcDateTime;
        var versions = await db.ProtocolVersions.AsNoTracking()
            .Where(v => v.StudyId == study && v.CreatedAt <= knownAtUtc)
            .OrderBy(v => v.Number)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (versions.Count == 0)
        {
            return [];
        }

        var versionKeys = versions.Select(v => v.Id.ToString()).ToList();
        var postCutAudits = await db.AuditEntries.AsNoTracking()
            .Where(a => a.AggregateType == nameof(ProtocolVersion)
                && a.AggregateId != null
                && versionKeys.Contains(a.AggregateId)
                && a.RecordedAt > knownAt)
            .OrderByDescending(a => a.Sequence)
            .Select(a => new { a.AggregateId, a.Action, a.Changes })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var auditsByVersion = postCutAudits
            .GroupBy(a => a.AggregateId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var effectivities = await db.Effectivities.AsNoTracking()
            .Include(e => e.Periods)
            .Where(e => e.StudyId == study)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var effectiveVersionIds = effectivities
            .Where(e => e.StatusAsOf(validAt, knownAt) == EffectivityStatus.Effective)
            .Select(e => e.ProtocolVersionId)
            .ToHashSet();

        var snapshots = await db.FormVersionSnapshots.AsNoTracking()
            .Where(s => s.StudyId == study && (s.RecordedAt != default ? s.RecordedAt <= knownAt : s.CreatedAt <= knownAtUtc))
            .OrderByDescending(s => s.RecordedAt)
            .ThenByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var odmByVersion = snapshots
            .Where(s => !string.IsNullOrWhiteSpace(s.OdmXml))
            .GroupBy(s => s.ProtocolVersionId)
            .ToDictionary(g => g.Key, g => g.First().OdmXml);

        var reconstructed = new List<(ProtocolVersionId Id, int Number, ProtocolVersionStatus Status, string Content, string? ContentHash)>(versions.Count);
        foreach (var v in versions)
        {
            var status = v.Status;
            var content = v.Content;
            var contentHash = v.ContentHash;
            var createdAfterCut = false;

            if (auditsByVersion.TryGetValue(v.Id.ToString(), out var postAudits))
            {
                foreach (var audit in postAudits)
                {
                    if (string.Equals(audit.Action, "Created", StringComparison.Ordinal))
                    {
                        createdAfterCut = true;
                        break;
                    }

                    if (!string.Equals(audit.Action, "Updated", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ApplyBeforeSnapshot(audit.Changes, ref status, ref content, ref contentHash);
                }
            }

            if (createdAfterCut)
            {
                continue;
            }

            if (status == ProtocolVersionStatus.Superseded
                && !versions.Any(other => other.Number > v.Number && effectiveVersionIds.Contains(other.Id)))
            {
                status = ProtocolVersionStatus.Approved;
            }

            reconstructed.Add((v.Id, v.Number, status, content, contentHash));
        }

        if (reconstructed.Count == 0)
        {
            return [];
        }

        var filtered = reconstructed
            .Where(r => referencedVersionIds.Contains(r.Id) || effectiveVersionIds.Contains(r.Id))
            .ToList();

        return filtered
            .Select(r =>
            {
                var compiledOdm = r.Status is ProtocolVersionStatus.Approved or ProtocolVersionStatus.Superseded
                    ? odmByVersion.GetValueOrDefault(r.Id)
                    : null;
                return new ExportProtocolSnapshot(
                    r.Id,
                    r.Number,
                    r.Status,
                    ProtocolDesignDocument.Read(r.Content).Design,
                    r.Content,
                    r.ContentHash,
                    compiledOdm);
            })
            .ToList();
    }

    private static void ApplyBeforeSnapshot(
        string changesJson,
        ref ProtocolVersionStatus status,
        ref string content,
        ref string? contentHash)
    {
        if (string.IsNullOrWhiteSpace(changesJson))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(changesJson);
            if (!doc.RootElement.TryGetProperty("before", out var before) || before.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (before.TryGetProperty(nameof(ProtocolVersion.Content), out var contentProp)
                && contentProp.ValueKind == JsonValueKind.String)
            {
                content = contentProp.GetString() ?? content;
            }

            if (before.TryGetProperty(nameof(ProtocolVersion.ContentHash), out var hashProp))
            {
                contentHash = hashProp.ValueKind == JsonValueKind.String ? hashProp.GetString() : null;
            }

            if (before.TryGetProperty(nameof(ProtocolVersion.Status), out var statusProp))
            {
                if (statusProp.ValueKind == JsonValueKind.String
                    && Enum.TryParse<ProtocolVersionStatus>(statusProp.GetString(), ignoreCase: true, out var parsedStatus))
                {
                    status = parsedStatus;
                }
                else if (statusProp.ValueKind == JsonValueKind.Number
                    && statusProp.TryGetInt32(out var statusCode)
                    && Enum.IsDefined((ProtocolVersionStatus)statusCode))
                {
                    status = (ProtocolVersionStatus)statusCode;
                }
            }
        }
        catch (JsonException)
        {
            // Ignore malformed change snapshots.
        }
    }

    private static async Task<IReadOnlyList<Assertion>> ReadEnrollmentAssertionsBoundedAsync(
        KernelDbContext db,
        StudyId study,
        StudySiteId studySite,
        EnrollmentId enrollment,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        var enrollmentAssertions = await db.Assertions.AsNoTracking()
            .Where(a => a.EnrollmentId == enrollment && a.AssertedAt <= knownAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Guid[] coveringScopeIds = [study.Value, studySite.Value, enrollment.Value];
        var coveringScopeLocks = await db.Assertions.AsNoTracking()
            .Where(a => coveringScopeIds.Contains(a.TargetId)
                && (a.Kind == AssertionKind.Locked || a.Kind == AssertionKind.Frozen)
                && a.AssertedAt <= knownAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var queryIds = enrollmentAssertions
            .Where(a => a.StudyId == study && a.Kind == AssertionKind.Queried && a.RetractsId == null)
            .Select(a => a.Id.Value)
            .ToList();

        var queryAnswers = queryIds.Count == 0
            ? []
            : await db.Assertions.AsNoTracking()
                .Where(a => queryIds.Contains(a.TargetId)
                    && a.Kind == AssertionKind.QueryAnswered
                    && a.TargetKind == NodeKinds.Assertion
                    && a.AssertedAt <= knownAt)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var byId = new Dictionary<AssertionId, Assertion>(enrollmentAssertions.Count + coveringScopeLocks.Count + queryAnswers.Count);
        foreach (var assertion in enrollmentAssertions)
        {
            if (assertion.StudyId == study)
            {
                byId[assertion.Id] = assertion;
            }
        }

        foreach (var assertion in coveringScopeLocks)
        {
            if (assertion.StudyId == study)
            {
                byId.TryAdd(assertion.Id, assertion);
            }
        }

        foreach (var assertion in queryAnswers)
        {
            if (assertion.StudyId == study)
            {
                byId.TryAdd(assertion.Id, assertion);
            }
        }

        var merged = byId.Values.ToList();
        merged.Sort(AssertionOrderComparer.Instance);
        return merged;
    }

    private static (List<Assertion> AppliedLocks, Dictionary<AssertionId, Assertion> ReleasesByLock) IndexLocksAndEarliestReleases(
        IReadOnlyList<Assertion> assertions)
    {
        var appliedLocks = new List<Assertion>();
        var releasesByLock = new Dictionary<AssertionId, Assertion>();
        foreach (var assertion in assertions)
        {
            if (!assertion.IsLock)
            {
                continue;
            }

            if (assertion.RetractsId is { } retractedLockId)
            {
                if (!releasesByLock.TryGetValue(retractedLockId, out var existingRelease)
                    || IsEarlierAssertion(assertion, existingRelease))
                {
                    releasesByLock[retractedLockId] = assertion;
                }
            }
            else
            {
                appliedLocks.Add(assertion);
            }
        }

        return (appliedLocks, releasesByLock);
    }

    private static async Task<(IReadOnlyList<ExportCollectionSnapshot> Collections, IReadOnlyList<ExportObservationSnapshot> Observations)> BuildObservationsAndCollectionsAsync(
        KernelDbContext db,
        IReadOnlyList<ExportProtocolSnapshot> protocols,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<EnrollmentId, StudySiteId> studySiteByEnrollment,
        IReadOnlyList<CollectionInstance> collectionsRaw,
        IReadOnlyList<Observation> observationsRaw,
        IReadOnlyList<Assertion> assertionsRaw,
        IReadOnlyList<StudyLockEntry> lockEntries,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        var conceptIds = observationsRaw.Select(o => o.ConceptId).Distinct().ToList();
        var conceptsById = conceptIds.Count == 0
            ? new Dictionary<ConceptId, Concept>()
            : await db.Concepts.AsNoTracking()
                .Include(c => c.Revisions)
                .Where(c => conceptIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken)
                .ConfigureAwait(false);

        return AssembleObservationsAndCollections(
            protocols,
            designs,
            conceptsById,
            studySiteByEnrollment,
            collectionsRaw,
            observationsRaw,
            assertionsRaw,
            lockEntries,
            knownAt);
    }

    internal static (IReadOnlyList<ExportCollectionSnapshot> Collections, IReadOnlyList<ExportObservationSnapshot> Observations) AssembleObservationsAndCollections(
        IReadOnlyList<ExportProtocolSnapshot> protocols,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ConceptId, Concept> conceptsById,
        IReadOnlyDictionary<EnrollmentId, StudySiteId> studySiteByEnrollment,
        IReadOnlyList<CollectionInstance> collectionsRaw,
        IReadOnlyList<Observation> observationsRaw,
        IReadOnlyList<Assertion> assertionsRaw,
        IReadOnlyList<StudyLockEntry> lockEntries,
        DateTimeOffset knownAt)
    {
        var specNamesByVersion = new Dictionary<VersionedElementKey, string>(VersionedElementKeyComparer.Instance);
        var activityNamesByVersion = new Dictionary<VersionedElementKey, string>(VersionedElementKeyComparer.Instance);
        foreach (var protocol in protocols)
        {
            if (protocol.Design is not { } design)
            {
                continue;
            }

            foreach (var spec in design.CollectionSpecs)
            {
                specNamesByVersion.TryAdd(new VersionedElementKey(protocol.ProtocolVersionId, spec.Id), spec.Name);
            }

            foreach (var activity in design.PlannedActivities)
            {
                activityNamesByVersion.TryAdd(new VersionedElementKey(protocol.ProtocolVersionId, activity.Id), activity.Name);
            }
        }

        var collectionByObservation = new Dictionary<ObservationId, (CollectionInstance Instance, string ItemId)>();
        foreach (var instance in collectionsRaw)
        {
            foreach (var member in instance.Members)
            {
                if (member.JoinedAt <= knownAt)
                {
                    collectionByObservation[member.ObservationId] = (instance, member.ItemId);
                }
            }
        }

        var activeLockByScope = new Dictionary<ScopeKey, LockKind>(ScopeKeyComparer.Instance);
        foreach (var lockEntry in lockEntries)
        {
            if (lockEntry.Released)
            {
                continue;
            }

            var scopeKey = new ScopeKey(lockEntry.Scope.Kind, lockEntry.Scope.Id);
            if (lockEntry.Kind == LockKind.Locked)
            {
                activeLockByScope[scopeKey] = LockKind.Locked;
            }
            else
            {
                activeLockByScope.TryAdd(scopeKey, LockKind.Frozen);
            }
        }

        var directAssertionsByObservation = new Dictionary<Guid, List<Assertion>>();
        var answersByQueryId = new Dictionary<Guid, List<Assertion>>();
        var nonLockRetractedBy = new Dictionary<AssertionId, Assertion>();
        foreach (var assertion in assertionsRaw)
        {
            if (assertion.IsLock)
            {
                continue;
            }

            if (assertion.RetractsId is { } retractedId)
            {
                if (!nonLockRetractedBy.TryGetValue(retractedId, out var existingRetraction)
                    || IsEarlierAssertion(assertion, existingRetraction))
                {
                    nonLockRetractedBy[retractedId] = assertion;
                }

                continue;
            }

            if (string.Equals(assertion.TargetKind, NodeKinds.Observation, StringComparison.Ordinal))
            {
                if (!directAssertionsByObservation.TryGetValue(assertion.TargetId, out var obsList))
                {
                    obsList = [];
                    directAssertionsByObservation[assertion.TargetId] = obsList;
                }

                obsList.Add(assertion);
            }
            else if (assertion.Kind == AssertionKind.QueryAnswered
                && string.Equals(assertion.TargetKind, NodeKinds.Assertion, StringComparison.Ordinal))
            {
                if (!answersByQueryId.TryGetValue(assertion.TargetId, out var answerList))
                {
                    answerList = [];
                    answersByQueryId[assertion.TargetId] = answerList;
                }

                answerList.Add(assertion);
            }
        }

        var blindingLookup = BlindedObservationClassifier.CreateLookup(conceptsById, designs);
        var conceptDisplayByRef = new Dictionary<ConceptRef, (string Code, string Label)>();
        var currentAtCutByObservation = new Dictionary<ObservationId, (int Revision, ObservationValue Value)>(observationsRaw.Count);
        var observationSnapshots = new List<ExportObservationSnapshot>(observationsRaw.Count);

        foreach (var obs in observationsRaw)
        {
            var revsAtCut = SelectRevisionsAtCut(obs.Revisions, knownAt);
            if (revsAtCut.Count == 0)
            {
                continue;
            }

            var views = new List<ObservationRevisionView>(revsAtCut.Count);
            for (var idx = 0; idx < revsAtCut.Count; idx++)
            {
                var r = revsAtCut[idx];
                views.Add(new ObservationRevisionView(
                    r.Number,
                    r.Value,
                    r.ReasonForChange,
                    r.RecordedBy,
                    r.RecordedAt,
                    idx + 1 < revsAtCut.Count ? revsAtCut[idx + 1].RecordedAt : null));
            }

            var currentRev = views[^1];
            currentAtCutByObservation[obs.Id] = (currentRev.Number, currentRev.Value);

            collectionByObservation.TryGetValue(obs.Id, out var membership);
            var studySiteId = studySiteByEnrollment[obs.EnrollmentId];

            if (!conceptDisplayByRef.TryGetValue(obs.Concept, out var conceptDisplay))
            {
                conceptsById.TryGetValue(obs.ConceptId, out var concept);
                var fallbackId = concept is null ? obs.ConceptId.ToString() : null;
                var revLabel = concept?.Revisions.FirstOrDefault(r => r.Version == obs.ConceptVersion)?.PreferredLabel
                    ?? concept?.PreferredLabel
                    ?? fallbackId!;
                var conceptCode = concept?.ExternalCode ?? fallbackId ?? obs.ConceptId.ToString();
                conceptDisplay = (conceptCode, revLabel);
                conceptDisplayByRef[obs.Concept] = conceptDisplay;
            }

            bool isBlinded = blindingLookup.IsBlindedTreatment(
                obs.ConceptId,
                currentRev.Value,
                membership.Instance?.CollectionSpecId,
                membership.ItemId);

            LockKind? effectiveLock = activeLockByScope.Count == 0
                ? null
                : ResolveEffectiveLock(
                    activeLockByScope,
                    obs.StudyId,
                    studySiteId,
                    obs.EnrollmentId,
                    membership.Instance?.Id,
                    obs.Id);

            IReadOnlyList<AssertionView> aboutViews = directAssertionsByObservation.TryGetValue(obs.Id.Value, out var directAssertions)
                ? BuildObservationAssertionViews(directAssertions, answersByQueryId, nonLockRetractedBy)
                : [];

            observationSnapshots.Add(new ExportObservationSnapshot(
                obs.Id,
                obs.EnrollmentId,
                obs.ParticipantId,
                studySiteId,
                membership.Instance?.Id,
                membership.Instance?.CollectionSpecId,
                membership.Instance?.PlannedActivityId,
                membership.ItemId,
                obs.Concept,
                conceptDisplay.Code,
                conceptDisplay.Label,
                isBlinded,
                obs.ObservedAt,
                currentRev,
                views,
                effectiveLock,
                aboutViews));
        }

        var collectionSnapshots = new List<ExportCollectionSnapshot>(collectionsRaw.Count);
        foreach (var instance in collectionsRaw)
        {
            var specName = specNamesByVersion.GetValueOrDefault(
                new VersionedElementKey(instance.ProtocolVersionId, instance.CollectionSpecId),
                instance.CollectionSpecId);
            var activityName = instance.PlannedActivityId is { } plannedActivityId
                ? activityNamesByVersion.GetValueOrDefault(
                    new VersionedElementKey(instance.ProtocolVersionId, plannedActivityId),
                    plannedActivityId)
                : null;

            var itemsMap = new Dictionary<ObservationId, string>();
            var hashEntries = new List<CollectionContentEntry>();
            foreach (var member in instance.Members)
            {
                if (member.JoinedAt <= knownAt && currentAtCutByObservation.TryGetValue(member.ObservationId, out var current))
                {
                    itemsMap[member.ObservationId] = member.ItemId;
                    hashEntries.Add(new CollectionContentEntry(member.ObservationId, current.Revision, current.Value));
                }
            }

            var contentHash = hashEntries.Count == 0 ? null : CollectionInstance.ContentHashOf(hashEntries);
            collectionSnapshots.Add(new ExportCollectionSnapshot(
                instance.Id,
                instance.EnrollmentId,
                instance.StudySiteId,
                instance.ProtocolVersionId,
                instance.CollectionSpecId,
                specName,
                instance.PlannedActivityId,
                activityName,
                instance.CollectedAt,
                instance.OpenedAt,
                instance.OpenedBy,
                contentHash,
                itemsMap));
        }

        return (collectionSnapshots, observationSnapshots);
    }

    private static List<ObservationRevision> SelectRevisionsAtCut(
        IReadOnlyCollection<ObservationRevision> revisions,
        DateTimeOffset knownAt)
    {
        var revsAtCut = new List<ObservationRevision>(revisions.Count);
        var alreadySorted = true;
        var previousNumber = int.MinValue;
        foreach (var rev in revisions)
        {
            if (rev.RecordedAt <= knownAt)
            {
                if (rev.Number < previousNumber)
                {
                    alreadySorted = false;
                }

                previousNumber = rev.Number;
                revsAtCut.Add(rev);
            }
        }

        if (!alreadySorted)
        {
            revsAtCut.Sort(static (x, y) => x.Number.CompareTo(y.Number));
        }

        return revsAtCut;
    }

    private static LockKind? ResolveEffectiveLock(
        Dictionary<ScopeKey, LockKind> activeLockByScope,
        StudyId studyId,
        StudySiteId studySiteId,
        EnrollmentId enrollmentId,
        CollectionInstanceId? collectionInstanceId,
        ObservationId observationId)
    {
        var hasFrozen = false;
        if (TryProbeLock(activeLockByScope, new ScopeKey(NodeKinds.Study, studyId.Value), ref hasFrozen)
            || TryProbeLock(activeLockByScope, new ScopeKey(NodeKinds.StudySite, studySiteId.Value), ref hasFrozen)
            || TryProbeLock(activeLockByScope, new ScopeKey(NodeKinds.Enrollment, enrollmentId.Value), ref hasFrozen))
        {
            return LockKind.Locked;
        }

        if (collectionInstanceId is { } cid
            && TryProbeLock(activeLockByScope, new ScopeKey(NodeKinds.CollectionInstance, cid.Value), ref hasFrozen))
        {
            return LockKind.Locked;
        }

        if (TryProbeLock(activeLockByScope, new ScopeKey(NodeKinds.Observation, observationId.Value), ref hasFrozen))
        {
            return LockKind.Locked;
        }

        return hasFrozen ? LockKind.Frozen : null;
    }

    private static bool TryProbeLock(
        Dictionary<ScopeKey, LockKind> activeLockByScope,
        ScopeKey scope,
        ref bool hasFrozen)
    {
        if (!activeLockByScope.TryGetValue(scope, out var kind))
        {
            return false;
        }

        if (kind == LockKind.Locked)
        {
            return true;
        }

        hasFrozen = true;
        return false;
    }

    private static List<AssertionView> BuildObservationAssertionViews(
        List<Assertion> directAssertions,
        Dictionary<Guid, List<Assertion>> answersByQueryId,
        Dictionary<AssertionId, Assertion> nonLockRetractedBy)
    {
        var combined = new List<Assertion>(directAssertions.Count);
        combined.AddRange(directAssertions);
        foreach (var assertion in directAssertions)
        {
            if (assertion.Kind == AssertionKind.Queried
                && answersByQueryId.TryGetValue(assertion.Id.Value, out var answers))
            {
                combined.AddRange(answers);
            }
        }

        combined.Sort(AssertionOrderComparer.Instance);

        var views = new List<AssertionView>(combined.Count);
        foreach (var a in combined)
        {
            views.Add(nonLockRetractedBy.TryGetValue(a.Id, out var ret)
                ? new AssertionView(a.Id, a.Kind, a.Target, a.TargetRevision, a.Text, a.AssertedBy, a.AssertedAt, a.ReasonForChange, ret.Id, ret.AssertedAt)
                : new AssertionView(a.Id, a.Kind, a.Target, a.TargetRevision, a.Text, a.AssertedBy, a.AssertedAt, a.ReasonForChange, null, null));
        }

        return views;
    }

    private static bool IsEarlierAssertion(Assertion candidate, Assertion existing)
    {
        var cmp = candidate.AssertedAt.CompareTo(existing.AssertedAt);
        return cmp != 0 ? cmp < 0 : candidate.Id.CompareTo(existing.Id) < 0;
    }

    private static async Task<IReadOnlyList<ExportAttestationSnapshot>> ReadAttestationsAsync(
        KernelDbContext db,
        HashSet<Guid> targetIds,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        if (targetIds.Count == 0)
        {
            return [];
        }

        var ids = targetIds.ToList();
        var rows = await db.Attestations.AsNoTracking()
            .Where(a => ids.Contains(a.TargetId) && a.RecordedAt <= knownAt)
            .OrderBy(a => a.RecordedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(a => new ExportAttestationSnapshot(
                a.Id,
                a.ActorId,
                a.Meaning,
                a.TargetKind,
                a.TargetId,
                a.TargetContentHash,
                a.AuthenticatedAt,
                a.Method,
                a.RecordedAt,
                a.Reason))
            .ToList();
    }

    internal static HashSet<string> BuildBlindedAuditKeys(
        IReadOnlyList<ExportCollectionSnapshot> collections,
        IReadOnlyList<ExportObservationSnapshot> observations,
        IReadOnlyList<StudyLockEntry> locks,
        IReadOnlyList<ExportAttestationSnapshot> attestations,
        IReadOnlyList<ProtocolDesign> designs)
    {
        var blindedObsIds = new HashSet<ObservationId>();
        var blindedObsGuids = new HashSet<Guid>();
        var blindedCollectionGuids = new HashSet<Guid>();
        foreach (var obs in observations)
        {
            if (!obs.IsBlindedTreatment)
            {
                continue;
            }

            blindedObsIds.Add(obs.ObservationId);
            blindedObsGuids.Add(obs.ObservationId.Value);
            if (obs.CollectionInstanceId is { } cid)
            {
                blindedCollectionGuids.Add(cid.Value);
            }
        }

        var firstSpecById = new Dictionary<string, CollectionSpec>(StringComparer.Ordinal);
        foreach (var design in designs)
        {
            foreach (var spec in design.CollectionSpecs)
            {
                firstSpecById.TryAdd(spec.Id, spec);
            }
        }

        var specBlindingCache = new Dictionary<CollectionSpecBlindingKey, bool>(CollectionSpecBlindingKeyComparer.Instance);
        var emptyTextValue = new TextValue(string.Empty);
        foreach (var coll in collections)
        {
            if (blindedCollectionGuids.Contains(coll.CollectionInstanceId.Value))
            {
                continue;
            }

            var specKey = new CollectionSpecBlindingKey(coll.CollectionSpecId, coll.CollectionSpecName);
            if (!specBlindingCache.TryGetValue(specKey, out var isBlindedSpec))
            {
                isBlindedSpec = BlindedTreatmentClassifier.Instance.IsBlindedCollectionSpec(
                        coll.CollectionSpecId,
                        coll.CollectionSpecName,
                        designs: designs)
                    || (firstSpecById.TryGetValue(coll.CollectionSpecId, out var spec)
                        && spec.Items.Any(i => BlindedObservationClassifier.IsBlindedTreatment(
                            i.Concept.ConceptId,
                            emptyTextValue,
                            concept: null,
                            coll.CollectionSpecId,
                            i.Id,
                            designs)));
                specBlindingCache[specKey] = isBlindedSpec;
            }

            if (isBlindedSpec || (blindedObsIds.Count > 0 && coll.ItemIdsByObservation.Keys.Any(blindedObsIds.Contains)))
            {
                blindedCollectionGuids.Add(coll.CollectionInstanceId.Value);
            }
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var obs in observations.Where(o => o.IsBlindedTreatment))
        {
            keys.Add(obs.ObservationId.ToString());
            foreach (var assertion in obs.Assertions)
            {
                keys.Add(assertion.AssertionId.ToString());
                if (assertion.RetractedBy is { } retractedBy)
                {
                    keys.Add(retractedBy.ToString());
                }
            }
        }

        foreach (var collGuid in blindedCollectionGuids)
        {
            keys.Add(CollectionInstanceId.FromGuid(collGuid).ToString());
        }

        var blindedLockTargetGuids = new HashSet<Guid>();
        foreach (var l in locks.Where(l => l.BlindedScope))
        {
            keys.Add(l.LockId.ToString());
            blindedLockTargetGuids.Add(l.LockId.Value);
            keys.Add(l.AttestationId.ToString());
            if (l.ReleaseAssertionId is { } relId)
            {
                keys.Add(relId.ToString());
                blindedLockTargetGuids.Add(relId.Value);
            }

            if (l.ReleaseAttestationId is { } relAttId)
            {
                keys.Add(relAttId.ToString());
            }
        }

        foreach (var att in attestations)
        {
            if (blindedObsGuids.Contains(att.TargetId)
                || blindedCollectionGuids.Contains(att.TargetId)
                || blindedLockTargetGuids.Contains(att.TargetId))
            {
                keys.Add(att.AttestationId.ToString());
            }
        }

        return keys;
    }

    private static async Task<(
        IReadOnlyList<ObservationAuditView> Entries,
        IReadOnlySet<long> BlindedSequences,
        IReadOnlyDictionary<long, string> AggregateIdsBySequence)> ReadAuditEntriesAsync(
        KernelDbContext db,
        HashSet<string> aggregateKeys,
        HashSet<string> blindedAggregateKeys,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        if (aggregateKeys.Count == 0)
        {
            return ([], new HashSet<long>(), new Dictionary<long, string>());
        }

        var keys = aggregateKeys.ToList();
        var rows = await db.AuditEntries.AsNoTracking()
            .Where(a => a.AggregateId != null && keys.Contains(a.AggregateId) && a.RecordedAt <= knownAt)
            .OrderBy(a => a.Sequence)
            .Select(a => new
            {
                a.AggregateId,
                View = new ObservationAuditView(
                    a.Sequence,
                    a.RecordedAt,
                    a.ActorId,
                    a.Action,
                    a.AggregateType ?? string.Empty,
                    a.AggregateVersion,
                    a.ReasonForChange),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var blindedSequences = rows
            .Where(r => r.AggregateId is not null && blindedAggregateKeys.Contains(r.AggregateId))
            .Select(r => r.View.Sequence)
            .ToHashSet();

        var aggregateIdsBySequence = rows
            .Where(r => r.AggregateId is not null)
            .ToDictionary(r => r.View.Sequence, r => r.AggregateId!);

        return ([.. rows.Select(r => r.View)], blindedSequences, aggregateIdsBySequence);
    }

    private sealed class AssertionOrderComparer : IComparer<Assertion>
    {
        public static readonly AssertionOrderComparer Instance = new();

        public int Compare(Assertion? x, Assertion? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var cmp = x.AssertedAt.CompareTo(y.AssertedAt);
            return cmp != 0 ? cmp : x.Id.CompareTo(y.Id);
        }
    }

    private readonly record struct ScopeKey(string Kind, Guid Id);

    private sealed class ScopeKeyComparer : IEqualityComparer<ScopeKey>
    {
        public static readonly ScopeKeyComparer Instance = new();

        public bool Equals(ScopeKey x, ScopeKey y) =>
            x.Id == y.Id && string.Equals(x.Kind, y.Kind, StringComparison.Ordinal);

        public int GetHashCode(ScopeKey obj) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(obj.Kind), obj.Id);
    }

    private readonly record struct VersionedElementKey(ProtocolVersionId ProtocolVersionId, string ElementId);

    private sealed class VersionedElementKeyComparer : IEqualityComparer<VersionedElementKey>
    {
        public static readonly VersionedElementKeyComparer Instance = new();

        public bool Equals(VersionedElementKey x, VersionedElementKey y) =>
            x.ProtocolVersionId == y.ProtocolVersionId
            && string.Equals(x.ElementId, y.ElementId, StringComparison.Ordinal);

        public int GetHashCode(VersionedElementKey obj) =>
            HashCode.Combine(obj.ProtocolVersionId, StringComparer.Ordinal.GetHashCode(obj.ElementId));
    }

    private readonly record struct CollectionSpecBlindingKey(string SpecId, string SpecName);

    private sealed class CollectionSpecBlindingKeyComparer : IEqualityComparer<CollectionSpecBlindingKey>
    {
        public static readonly CollectionSpecBlindingKeyComparer Instance = new();

        public bool Equals(CollectionSpecBlindingKey x, CollectionSpecBlindingKey y) =>
            string.Equals(x.SpecId, y.SpecId, StringComparison.Ordinal)
            && string.Equals(x.SpecName, y.SpecName, StringComparison.Ordinal);

        public int GetHashCode(CollectionSpecBlindingKey obj) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.SpecId),
                StringComparer.Ordinal.GetHashCode(obj.SpecName));
    }
}
