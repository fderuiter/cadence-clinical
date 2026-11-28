using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.References;

using Microsoft.AspNetCore.Http;

namespace Cadence.Kernel.Api;

internal static class KernelStudyExportProjector
{
    public static readonly Capability EnrollmentUnblindCapability = new("enrollment.unblind", VerbClass.Read);

    public static async Task<IReadOnlyList<KernelCasebookResponse>> BuildStudyArchiveCasebooksAsync(
        TenantId tenant,
        StudyExportSnapshot snapshot,
        IReadOnlyList<ExportObservationSnapshot> filteredObservations,
        StudyLockFilter lockFilter,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        DateTimeOffset generatedAt,
        string requestedBy,
        StudyExportVisibility visibility,
        IReadOnlySet<EnrollmentId> visibleEnrollmentIds,
        IKernelExportReader exportReader,
        CancellationToken cancellationToken)
    {
        var includedEnrollmentIds = lockFilter == StudyLockFilter.All
            ? visibleEnrollmentIds.ToHashSet()
            : filteredObservations.Select(o => o.EnrollmentId).ToHashSet();

        var siteById = snapshot.StudySites.ToDictionary(s => s.StudySiteId);
        var casebooks = new List<KernelCasebookResponse>(includedEnrollmentIds.Count);

        foreach (var enrollment in snapshot.Enrollments.Where(e => visibleEnrollmentIds.Contains(e.EnrollmentId) && includedEnrollmentIds.Contains(e.EnrollmentId)))
        {
            var enrollmentMask = visibility.Masks.For(NodeRef.For(enrollment.EnrollmentId))
                ?? visibility.Masks.For(NodeRef.For(enrollment.StudySiteId));
            if (enrollmentMask is { } hiddenMask && hiddenMask != MaskReason.Blinded)
            {
                continue;
            }

            var casebookSnapshot = await exportReader.ReadEnrollmentCasebookAsync(
                tenant,
                enrollment.EnrollmentId,
                validAt,
                knownAt,
                cancellationToken).ConfigureAwait(false)
                ?? SynthesizeEnrollmentCasebookSnapshot(snapshot, enrollment, siteById);

            if (lockFilter != StudyLockFilter.All)
            {
                casebookSnapshot = casebookSnapshot with
                {
                    Observations = FilterObservations(casebookSnapshot.Observations, lockFilter),
                };
            }

            var mayUnblind = enrollmentMask is null && visibility.UnblindedEnrollments.Contains(enrollment.EnrollmentId);

            casebooks.Add(KernelExportStreamer.BuildCasebookModel(
                casebookSnapshot,
                generatedAt,
                requestedBy,
                enrollmentMask: null,
                mayUnblind));
        }

        return casebooks;
    }

    public static async Task<StudyExportVisibility> EvaluateStudyVisibilityAsync(
        TenantId tenant,
        NodeRef studyNode,
        StudyExportSnapshot snapshot,
        INodeVisibility visibility,
        IAuthorizationProbe probe,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var nodes = new List<NodeRef> { studyNode };
        nodes.AddRange(snapshot.StudySites.Select(s => NodeRef.For(s.StudySiteId)));
        nodes.AddRange(snapshot.Enrollments.Select(e => NodeRef.For(e.EnrollmentId)));
        nodes.AddRange(snapshot.Observations.Select(o => NodeRef.For(o.ObservationId)));

        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, nodes, cancellationToken).ConfigureAwait(false);
        var unblindedEnrollments = new HashSet<EnrollmentId>();

        var enrollmentsWithBlindedObs = snapshot.Observations
            .Where(o => o.IsBlindedTreatment)
            .Select(o => o.EnrollmentId)
            .Distinct()
            .ToList();

        foreach (var enrollmentId in enrollmentsWithBlindedObs)
        {
            var enrollmentNode = NodeRef.For(enrollmentId);
            if (masks.For(enrollmentNode) is not null)
            {
                continue;
            }

            var decision = await probe.CanIAsync(tenant, EnrollmentUnblindCapability, enrollmentNode, cancellationToken).ConfigureAwait(false);
            if (decision.Permitted)
            {
                unblindedEnrollments.Add(enrollmentId);
            }
        }

        return new StudyExportVisibility(masks, unblindedEnrollments);
    }

    public static List<ExportObservationSnapshot> FilterObservations(
        IReadOnlyList<ExportObservationSnapshot> observations,
        StudyLockFilter filter) =>
        filter switch
        {
            StudyLockFilter.LockedOnly => [.. observations.Where(o => o.EffectiveLock == LockKind.Locked)],
            StudyLockFilter.FrozenOrLocked => [.. observations.Where(o => o.EffectiveLock is LockKind.Frozen or LockKind.Locked)],
            StudyLockFilter.UnlockedOnly => [.. observations.Where(o => o.EffectiveLock is null)],
            _ => [.. observations],
        };

    public static (List<ExportObservationSnapshot> VisibleObservations, HashSet<EnrollmentId> VisibleEnrollmentIds, HiddenScopeSummary HiddenScopes) PartitionStudyVisibility(
        StudyExportSnapshot snapshot,
        StudyExportVisibility visibility)
    {
        var hiddenReasons = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var site in snapshot.StudySites)
        {
            if (visibility.Masks.For(NodeRef.For(site.StudySiteId)) is { } siteMask && siteMask != MaskReason.Blinded)
            {
                hiddenReasons.Add(siteMask.ToString());
            }
        }

        var visibleEnrollmentIds = new HashSet<EnrollmentId>();
        foreach (var enrollment in snapshot.Enrollments)
        {
            var enrollmentMask = visibility.Masks.For(NodeRef.For(enrollment.EnrollmentId))
                ?? visibility.Masks.For(NodeRef.For(enrollment.StudySiteId));
            if (enrollmentMask is { } mask && mask != MaskReason.Blinded)
            {
                hiddenReasons.Add(mask.ToString());
            }
            else
            {
                visibleEnrollmentIds.Add(enrollment.EnrollmentId);
            }
        }

        var visibleObservations = new List<ExportObservationSnapshot>(snapshot.Observations.Count);
        foreach (var obs in snapshot.Observations)
        {
            var accessMask = visibility.Masks.For(NodeRef.For(obs.EnrollmentId))
                ?? visibility.Masks.For(NodeRef.For(obs.StudySiteId));
            if (accessMask is { } hiddenAccess && hiddenAccess != MaskReason.Blinded)
            {
                hiddenReasons.Add(hiddenAccess.ToString());
                continue;
            }

            if (!visibleEnrollmentIds.Contains(obs.EnrollmentId))
            {
                continue;
            }

            var obsMask = visibility.Masks.For(NodeRef.For(obs.ObservationId));
            if (obsMask is { } hiddenObs && hiddenObs != MaskReason.Blinded)
            {
                hiddenReasons.Add(hiddenObs.ToString());
                continue;
            }

            visibleObservations.Add(obs);
        }

        var hiddenScopes = hiddenReasons.Count > 0
            ? new HiddenScopeSummary(true, [.. hiddenReasons])
            : HiddenScopeSummary.None;
        return (visibleObservations, visibleEnrollmentIds, hiddenScopes);
    }

    public static ProjectedStudyExport ProjectStudyExport(
        StudyExportSnapshot snapshot,
        IReadOnlyList<ExportObservationSnapshot> filteredVisibleObservations,
        string lockFilterName,
        DateTimeOffset generatedAt,
        StudyExportVisibility visibility,
        IReadOnlySet<EnrollmentId> visibleEnrollmentIds,
        HiddenScopeSummary hiddenScopes)
    {
        var enrollmentById = snapshot.Enrollments.ToDictionary(e => e.EnrollmentId);
        var siteById = snapshot.StudySites.ToDictionary(s => s.StudySiteId);

        var rows = new List<ProjectedObservationRow>(filteredVisibleObservations.Count);
        foreach (var obs in filteredVisibleObservations)
        {
            enrollmentById.TryGetValue(obs.EnrollmentId, out var enrollment);
            siteById.TryGetValue(obs.StudySiteId, out var site);

            var accessMask = visibility.Masks.For(NodeRef.For(obs.EnrollmentId))
                ?? visibility.Masks.For(NodeRef.For(obs.StudySiteId));
            var obsMask = visibility.Masks.For(NodeRef.For(obs.ObservationId));
            if (accessMask == MaskReason.Blinded
                || obsMask == MaskReason.Blinded
                || (obs.IsBlindedTreatment && !visibility.UnblindedEnrollments.Contains(obs.EnrollmentId)))
            {
                rows.Add(ProjectedObservationRow.CreateBlinded(snapshot.StudyCode, enrollment, site, obs));
                continue;
            }

            rows.Add(ProjectedObservationRow.CreateVisible(snapshot.StudyCode, enrollment, site, obs));
        }

        return new ProjectedStudyExport(
            snapshot,
            lockFilterName,
            generatedAt,
            rows,
            visibility,
            visibleEnrollmentIds,
            new KernelHiddenContentResponse(hiddenScopes.MoreExist, hiddenScopes.Reasons),
            hiddenScopes);
    }

    public static string FormatHiddenScopesHeader(HiddenScopeSummary hiddenScopes) =>
        hiddenScopes.MoreExist
            ? $"more_exist=true;reasons={string.Join(',', hiddenScopes.Reasons)}"
            : "more_exist=false";

    public static string ResolveCasebookDisclosure(bool masked, bool includesUnblinded)
    {
        if (masked)
        {
            return "masked";
        }

        return includesUnblinded ? "unblinded" : "blinded";
    }

    public static string ReadOrCreateIdempotencyKey(HttpContext context, DateTimeOffset generatedAt)
    {
        var supplied = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
        return string.IsNullOrWhiteSpace(supplied)
            ? $"export-{Guid.CreateVersion7(generatedAt):N}"
            : supplied.Trim();
    }

    public static bool TryParseStudyFormat(string? raw, out StudyExportFormat format, out string normalizedFormat)
    {
        var normalized = string.IsNullOrWhiteSpace(raw) ? "csv" : raw.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "csv":
                format = StudyExportFormat.Csv;
                normalizedFormat = "csv";
                return true;
            case "odm_xml" or "odm":
                format = StudyExportFormat.OdmXml;
                normalizedFormat = "odm_xml";
                return true;
            case "dataset_json" or "dataset-json":
                format = StudyExportFormat.DatasetJson;
                normalizedFormat = "dataset_json";
                return true;
            case "sas_bundle" or "sas-bundle":
                format = StudyExportFormat.SasBundle;
                normalizedFormat = "sas_bundle";
                return true;
            case "r_bundle" or "r-bundle":
                format = StudyExportFormat.RBundle;
                normalizedFormat = "r_bundle";
                return true;
            case "study_archive" or "study-archive":
                format = StudyExportFormat.StudyArchive;
                normalizedFormat = "study_archive";
                return true;
            default:
                format = default;
                normalizedFormat = string.Empty;
                return false;
        }
    }

    public static bool TryParseCasebookFormat(string? raw, out CasebookExportFormat format, out string normalizedFormat)
    {
        var normalized = string.IsNullOrWhiteSpace(raw) ? "json" : raw.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "json":
                format = CasebookExportFormat.Json;
                normalizedFormat = "json";
                return true;
            case "odm_xml" or "odm":
                format = CasebookExportFormat.OdmXml;
                normalizedFormat = "odm_xml";
                return true;
            case "html":
                format = CasebookExportFormat.Html;
                normalizedFormat = "html";
                return true;
            case "pdf":
                format = CasebookExportFormat.Pdf;
                normalizedFormat = "pdf";
                return true;
            default:
                format = default;
                normalizedFormat = string.Empty;
                return false;
        }
    }

    public static bool TryParseLockFilter(string? raw, out StudyLockFilter filter, out string normalized)
    {
        var token = string.IsNullOrWhiteSpace(raw) ? "all" : raw.Trim().ToLowerInvariant();
        switch (token)
        {
            case "all":
                filter = StudyLockFilter.All;
                normalized = "all";
                return true;
            case "locked_only" or "locked":
                filter = StudyLockFilter.LockedOnly;
                normalized = "locked_only";
                return true;
            case "frozen_or_locked":
                filter = StudyLockFilter.FrozenOrLocked;
                normalized = "frozen_or_locked";
                return true;
            case "unlocked_only" or "unlocked":
                filter = StudyLockFilter.UnlockedOnly;
                normalized = "unlocked_only";
                return true;
            default:
                filter = default;
                normalized = string.Empty;
                return false;
        }
    }

    private static EnrollmentCasebookSnapshot SynthesizeEnrollmentCasebookSnapshot(
        StudyExportSnapshot snapshot,
        ExportEnrollmentSnapshot enrollment,
        IReadOnlyDictionary<StudySiteId, ExportSiteSnapshot> siteById)
    {
        siteById.TryGetValue(enrollment.StudySiteId, out var site);
        var collections = snapshot.Collections.Where(c => c.EnrollmentId == enrollment.EnrollmentId).ToList();
        var observations = snapshot.Observations.Where(o => o.EnrollmentId == enrollment.EnrollmentId).ToList();
        var obsIds = observations.Select(o => o.ObservationId.Value).ToHashSet();
        var formIds = collections.Select(c => c.CollectionInstanceId.Value).ToHashSet();
        var targetIds = new HashSet<Guid>(obsIds);
        targetIds.UnionWith(formIds);
        targetIds.Add(enrollment.EnrollmentId.Value);
        targetIds.Add(enrollment.StudySiteId.Value);
        targetIds.Add(snapshot.StudyId.Value);

        var assertions = observations
            .SelectMany(o => o.Assertions)
            .DistinctBy(a => a.AssertionId)
            .ToList();
        var locks = snapshot.Locks
            .Where(l => targetIds.Contains(l.Scope.Id))
            .ToList();
        var attestations = snapshot.Attestations
            .Where(a => targetIds.Contains(a.TargetId))
            .ToList();
        var allowedAuditKeys = observations.Select(o => o.ObservationId.ToString())
            .Concat(assertions.Select(a => a.AssertionId.ToString()))
            .Concat(collections.Select(c => c.CollectionInstanceId.ToString()))
            .Concat(locks.Select(l => l.LockId.ToString()))
            .Concat(attestations.Select(a => a.AttestationId.ToString()))
            .Append(enrollment.EnrollmentId.ToString())
            .ToHashSet(StringComparer.Ordinal);
        var audits = snapshot.AuditAggregateIdsBySequence is { } aggregateIds
            ? snapshot.AuditEntries
                .Where(entry => aggregateIds.TryGetValue(entry.Sequence, out var aggId) && allowedAuditKeys.Contains(aggId))
                .ToList()
            : snapshot.AuditEntries;

        return new EnrollmentCasebookSnapshot(
            StudyId: snapshot.StudyId,
            StudyCode: snapshot.StudyCode,
            StudyTitle: snapshot.StudyTitle,
            StudySiteId: enrollment.StudySiteId,
            SiteCode: site?.SiteCode ?? enrollment.SiteCode,
            SiteName: site?.SiteName ?? enrollment.SiteCode,
            EnrollmentId: enrollment.EnrollmentId,
            ParticipantId: enrollment.ParticipantId,
            SubjectCode: enrollment.SubjectCode,
            StatusAtCut: enrollment.StatusAtCut,
            ValidAt: snapshot.ValidAt,
            KnownAt: snapshot.KnownAt,
            ProtocolVersions: snapshot.ProtocolVersions,
            Collections: collections,
            Observations: observations,
            Assertions: assertions,
            Locks: locks,
            Attestations: attestations,
            AuditEntries: audits,
            BlindedAuditSequences: snapshot.BlindedAuditSequences);
    }
}
