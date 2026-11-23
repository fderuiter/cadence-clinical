using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Kernel.Api;

/// <summary>
/// Streaming bitemporal clinical snapshot exports and signed participant casebooks (spec §10.1, §10.5, K-TRUTH-1,
/// K-AUTH-4). Projects canonical kernel truth at <c>as_of(valid_at, known_at)</c>, evaluates caller visibility via
/// <see cref="KernelReadMasking"/> and unblinding capabilities, and excludes <c>IdentityVault</c> direct identifiers.
/// </summary>
public static class KernelExportEndpoints
{
    /// <summary>Response header carrying the lowercase hexadecimal SHA-256 digest of a streamed casebook response.</summary>
    public const string ContentSha256Header = "X-Content-Sha256";

    private static readonly Capability EnrollmentUnblindCapability = new("enrollment.unblind", VerbClass.Read);

    internal static void Map(RouteGroupBuilder reads)
    {
        reads.MapGet("/studies/{studyRef}/exports", StreamStudyExportAsync)
            .WithName("KernelExportStudySnapshot")
            .WithSummary("Streams a bitemporal study snapshot as CSV, CDISC ODM v1.3.2 XML, CDISC Dataset-JSON, SAS bundle ZIP, R bundle ZIP, or Study Archive ZIP.")
            .WithDescription("Projects canonical kernel observations at as_of(valid_at, known_at), applying lock_filter and masking unauthorized sites or blinded treatment values in place (K-TRUTH-1, K-AUTH-4).")
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv")
            .Produces(StatusCodes.Status200OK, contentType: "application/xml")
            .Produces(StatusCodes.Status200OK, contentType: "application/zip")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        reads.MapGet("/enrollments/{enrollmentRef}/casebook", StreamEnrollmentCasebookAsync)
            .WithName("KernelExportEnrollmentCasebook")
            .WithSummary("Streams a participant casebook with revisions, queries, verifications, attestations and SHA-256 digests.")
            .WithDescription("Streams JSON, CDISC ODM v1.3.2 XML, printable HTML, or certified PDF (%PDF-1.7) for one enrollment at as_of(valid_at, known_at), masking unauthorized or blinded content under K-AUTH-4.")
            .Produces<KernelCasebookResponse>(StatusCodes.Status200OK, "application/json")
            .Produces(StatusCodes.Status200OK, contentType: "application/xml")
            .Produces(StatusCodes.Status200OK, contentType: "text/html")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    internal static async Task<IResult> StreamStudyExportAsync(
        string studyRef,
        [FromQuery(Name = "format")] string? format,
        [FromQuery(Name = "lock_filter")] string? lockFilter,
        [FromQuery(Name = "as_of")] DateTimeOffset? asOf,
        [FromQuery(Name = "valid_at")] DateTimeOffset? validAt,
        [FromQuery(Name = "known_at")] DateTimeOffset? knownAt,
        HttpContext context,
        IKernelNavigator navigator,
        IKernelExportReader exportReader,
        INodeVisibility visibility,
        IAuthorizationProbe probe,
        ICurrentActor actor,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(studyRef, out var studyNode))
        {
            return KernelProblems.InvalidNodeRef(studyRef);
        }

        if (studyNode.Kind != NodeKinds.Study || studyNode.Version is not null)
        {
            return KernelProblems.WrongKind(studyNode, NodeKinds.Study);
        }

        if (!TryParseStudyFormat(format, out var exportFormat))
        {
            return KernelProblems.BadRequest(
                "Invalid export format",
                "Supported study export formats are csv, odm_xml, dataset_json, sas_bundle, r_bundle, and study_archive.");
        }

        if (!TryParseLockFilter(lockFilter, out var parsedLockFilter, out var normalizedLockFilter))
        {
            return KernelProblems.BadRequest(
                "Invalid lock_filter",
                "Supported lock_filter values are all, locked_only, frozen_or_locked, and unlocked_only.");
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await navigator.ResolveAsync(tenant, studyNode, cancellationToken).ConfigureAwait(false) is not { } resolvedStudy)
        {
            return KernelProblems.NotFound(studyNode);
        }

        var studyMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [resolvedStudy.Ref], cancellationToken).ConfigureAwait(false);
        if (studyMasks.For(resolvedStudy.Ref) is { } studyMask)
        {
            return KernelProblems.NotVisible($"You may not see this study ({studyMask}), so its clinical exports are refused.");
        }

        var generatedAt = clock.UtcNow;
        var effectiveValidAt = validAt ?? asOf ?? generatedAt;
        var effectiveKnownAt = knownAt ?? asOf ?? generatedAt;

        var snapshot = await exportReader.ReadStudySnapshotAsync(
            tenant,
            StudyId.FromGuid(studyNode.Id),
            effectiveValidAt,
            effectiveKnownAt,
            cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return KernelProblems.NotFound(studyNode);
        }

        var visibilityMap = await EvaluateStudyVisibilityAsync(
            tenant,
            studyNode,
            snapshot,
            visibility,
            probe,
            actor,
            cancellationToken).ConfigureAwait(false);

        var filteredObservations = FilterObservations(snapshot.Observations, parsedLockFilter);
        var projected = ProjectStudyExport(snapshot, filteredObservations, parsedLockFilter, normalizedLockFilter, generatedAt, visibilityMap);

        var safeCode = SanitizeFileName(snapshot.StudyCode);
        if (exportFormat is StudyExportFormat.SasBundle or StudyExportFormat.RBundle or StudyExportFormat.StudyArchive)
        {
            var callerId = actor.Actor?.Id ?? "anonymous";
            IReadOnlyList<KernelCasebookResponse> archiveCasebooks = [];
            if (exportFormat == StudyExportFormat.StudyArchive)
            {
                archiveCasebooks = await BuildStudyArchiveCasebooksAsync(
                    tenant,
                    snapshot,
                    filteredObservations,
                    parsedLockFilter,
                    effectiveValidAt,
                    effectiveKnownAt,
                    generatedAt,
                    callerId,
                    visibilityMap,
                    exportReader,
                    cancellationToken).ConfigureAwait(false);
            }

            var zipExtension = exportFormat switch
            {
                StudyExportFormat.SasBundle => "sas_bundle.zip",
                StudyExportFormat.RBundle => "r_bundle.zip",
                _ => "study_archive.zip",
            };

            await using var zipBuffer = new MemoryStream();
            await KernelExportStreamer.WriteStudyBundleZipAsync(
                zipBuffer,
                projected,
                exportFormat,
                callerId,
                archiveCasebooks,
                cancellationToken).ConfigureAwait(false);

            var zipBytes = zipBuffer.ToArray();
            var zipSha256 = Convert.ToHexString(SHA256.HashData(zipBytes)).ToLowerInvariant();

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/zip";
            context.Response.Headers[ContentSha256Header] = zipSha256;
            context.Response.Headers.ETag = $"\"{zipSha256}\"";
            context.Response.Headers.ContentDisposition = $"attachment; filename=\"{safeCode}-{normalizedLockFilter}.{zipExtension}\"";
            await context.Response.Body.WriteAsync(zipBytes, cancellationToken).ConfigureAwait(false);
            return Results.Empty;
        }

        var (contentType, extension) = exportFormat switch
        {
            StudyExportFormat.Csv => ("text/csv; charset=utf-8", "csv"),
            StudyExportFormat.OdmXml => ("application/xml; charset=utf-8", "odm.xml"),
            _ => ("application/json; charset=utf-8", "dataset.json"),
        };

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = contentType;
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{safeCode}-{normalizedLockFilter}.{extension}\"";

        switch (exportFormat)
        {
            case StudyExportFormat.Csv:
                await KernelExportStreamer.WriteStudyCsvAsync(context.Response.Body, projected, cancellationToken).ConfigureAwait(false);
                break;
            case StudyExportFormat.OdmXml:
                await KernelExportStreamer.WriteStudyOdmXmlAsync(context.Response.Body, projected, cancellationToken).ConfigureAwait(false);
                break;
            case StudyExportFormat.DatasetJson:
                await KernelExportStreamer.WriteStudyDatasetJsonAsync(context.Response.Body, projected, cancellationToken).ConfigureAwait(false);
                break;
        }

        return Results.Empty;
    }

    internal static async Task<IResult> StreamEnrollmentCasebookAsync(
        string enrollmentRef,
        [FromQuery(Name = "format")] string? format,
        [FromQuery(Name = "as_of")] DateTimeOffset? asOf,
        [FromQuery(Name = "valid_at")] DateTimeOffset? validAt,
        [FromQuery(Name = "known_at")] DateTimeOffset? knownAt,
        HttpContext context,
        IKernelExportReader exportReader,
        INodeVisibility visibility,
        IAuthorizationProbe probe,
        ICurrentActor actor,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(enrollmentRef, out var enrollmentNode))
        {
            return KernelProblems.InvalidNodeRef(enrollmentRef);
        }

        if (enrollmentNode.Kind != NodeKinds.Enrollment || enrollmentNode.Version is not null)
        {
            return KernelProblems.WrongKind(enrollmentNode, NodeKinds.Enrollment);
        }

        if (!TryParseCasebookFormat(format, out var casebookFormat))
        {
            return KernelProblems.BadRequest(
                "Invalid casebook format",
                "Supported casebook formats are json, odm_xml, html, and pdf.");
        }

        var tenant = KernelRequestTenant.Get(context);
        var generatedAt = clock.UtcNow;
        var effectiveValidAt = validAt ?? asOf ?? generatedAt;
        var effectiveKnownAt = knownAt ?? asOf ?? generatedAt;

        var snapshot = await exportReader.ReadEnrollmentCasebookAsync(
            tenant,
            EnrollmentId.FromGuid(enrollmentNode.Id),
            effectiveValidAt,
            effectiveKnownAt,
            cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return KernelProblems.NotFound(enrollmentNode);
        }

        var studyNode = NodeRef.For(snapshot.StudyId);
        var siteNode = NodeRef.For(snapshot.StudySiteId);
        var nodes = new List<NodeRef> { studyNode, siteNode, enrollmentNode };
        nodes.AddRange(snapshot.Observations.Select(o => NodeRef.For(o.ObservationId)));

        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, nodes, cancellationToken).ConfigureAwait(false);
        if (masks.For(studyNode) is { } studyMask)
        {
            return KernelProblems.NotVisible($"You may not see this study ({studyMask}), so its participant casebook is refused.");
        }

        var enrollmentMask = masks.For(enrollmentNode) ?? masks.For(siteNode);
        var hasBlindedNodeMask = snapshot.Observations.Any(o => masks.For(NodeRef.For(o.ObservationId)) == MaskReason.Blinded);
        bool mayUnblind = false;
        if (enrollmentMask is null && !hasBlindedNodeMask && snapshot.Observations.Any(o => o.IsBlindedTreatment))
        {
            var unblindDecision = await probe.CanIAsync(tenant, EnrollmentUnblindCapability, enrollmentNode, cancellationToken).ConfigureAwait(false);
            mayUnblind = unblindDecision.Permitted;
        }

        var projected = KernelExportStreamer.BuildCasebookModel(
            snapshot,
            generatedAt,
            actor.Actor?.Id ?? "anonymous",
            enrollmentMask,
            mayUnblind);

        await using var buffer = new MemoryStream();
        var (contentType, extension) = casebookFormat switch
        {
            CasebookExportFormat.OdmXml => ("application/xml; charset=utf-8", "casebook.odm.xml"),
            CasebookExportFormat.Html => ("text/html; charset=utf-8", "casebook.html"),
            CasebookExportFormat.Pdf => ("application/pdf", "casebook.pdf"),
            _ => ("application/json; charset=utf-8", "casebook.json"),
        };

        switch (casebookFormat)
        {
            case CasebookExportFormat.OdmXml:
                await KernelExportStreamer.WriteCasebookOdmXmlAsync(buffer, projected, cancellationToken).ConfigureAwait(false);
                break;
            case CasebookExportFormat.Html:
                await KernelExportStreamer.WriteCasebookHtmlAsync(buffer, projected, cancellationToken).ConfigureAwait(false);
                break;
            case CasebookExportFormat.Pdf:
                await KernelExportStreamer.WriteCasebookPdfAsync(buffer, projected, cancellationToken).ConfigureAwait(false);
                break;
            default:
                await KernelExportStreamer.WriteCasebookJsonAsync(buffer, projected, cancellationToken).ConfigureAwait(false);
                break;
        }

        var payloadBytes = buffer.ToArray();
        var wireSha256 = Convert.ToHexString(SHA256.HashData(payloadBytes)).ToLowerInvariant();
        var fileLabel = projected.Masked ? "masked-participant" : SanitizeFileName(snapshot.SubjectCode);

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = contentType;
        context.Response.Headers[ContentSha256Header] = wireSha256;
        context.Response.Headers.ETag = $"\"{wireSha256}\"";
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{SanitizeFileName(snapshot.StudyCode)}-{fileLabel}.{extension}\"";
        await context.Response.Body.WriteAsync(payloadBytes, cancellationToken).ConfigureAwait(false);
        return Results.Empty;
    }

    private static async Task<IReadOnlyList<KernelCasebookResponse>> BuildStudyArchiveCasebooksAsync(
        TenantId tenant,
        StudyExportSnapshot snapshot,
        IReadOnlyList<ExportObservationSnapshot> filteredObservations,
        StudyLockFilter lockFilter,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        DateTimeOffset generatedAt,
        string requestedBy,
        StudyExportVisibility visibility,
        IKernelExportReader exportReader,
        CancellationToken cancellationToken)
    {
        var includedEnrollmentIds = lockFilter == StudyLockFilter.All
            ? snapshot.Enrollments.Select(e => e.EnrollmentId).ToHashSet()
            : filteredObservations.Select(o => o.EnrollmentId).ToHashSet();

        var siteById = snapshot.StudySites.ToDictionary(s => s.StudySiteId);
        var casebooks = new List<KernelCasebookResponse>(includedEnrollmentIds.Count);

        foreach (var enrollment in snapshot.Enrollments.Where(e => includedEnrollmentIds.Contains(e.EnrollmentId)))
        {
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

            var enrollmentMask = visibility.Masks.For(NodeRef.For(enrollment.EnrollmentId))
                ?? visibility.Masks.For(NodeRef.For(enrollment.StudySiteId));
            var mayUnblind = enrollmentMask is null && visibility.UnblindedEnrollments.Contains(enrollment.EnrollmentId);

            casebooks.Add(KernelExportStreamer.BuildCasebookModel(
                casebookSnapshot,
                generatedAt,
                requestedBy,
                enrollmentMask,
                mayUnblind));
        }

        return casebooks;
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
        var audits = snapshot.AuditEntries;

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
            EnrollmentNumber: enrollment.EnrollmentNumber,
            ValidAt: snapshot.ValidAt,
            KnownAt: snapshot.KnownAt,
            ProtocolVersions: snapshot.ProtocolVersions,
            Collections: collections,
            Observations: observations,
            Assertions: assertions,
            Locks: locks,
            Attestations: attestations,
            AuditEntries: audits);
    }

    private static async Task<StudyExportVisibility> EvaluateStudyVisibilityAsync(
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

    private static List<ExportObservationSnapshot> FilterObservations(
        IReadOnlyList<ExportObservationSnapshot> observations,
        StudyLockFilter filter) =>
        filter switch
        {
            StudyLockFilter.LockedOnly => [.. observations.Where(o => o.EffectiveLock == LockKind.Locked)],
            StudyLockFilter.FrozenOrLocked => [.. observations.Where(o => o.EffectiveLock is LockKind.Frozen or LockKind.Locked)],
            StudyLockFilter.UnlockedOnly => [.. observations.Where(o => o.EffectiveLock is null)],
            _ => [.. observations],
        };

    private static ProjectedStudyExport ProjectStudyExport(
        StudyExportSnapshot snapshot,
        IReadOnlyList<ExportObservationSnapshot> filteredObservations,
        StudyLockFilter lockFilter,
        string lockFilterName,
        DateTimeOffset generatedAt,
        StudyExportVisibility visibility)
    {
        var enrollmentById = snapshot.Enrollments.ToDictionary(e => e.EnrollmentId);
        var siteById = snapshot.StudySites.ToDictionary(s => s.StudySiteId);

        var rows = new List<ProjectedObservationRow>(filteredObservations.Count);
        var representedMaskedEnrollments = new HashSet<EnrollmentId>();

        foreach (var obs in filteredObservations)
        {
            var siteNode = NodeRef.For(obs.StudySiteId);
            var enrollmentNode = NodeRef.For(obs.EnrollmentId);

            // Observations and collection instances inherit visibility from their enrollment and site (ADR-2457).
            var accessMask = visibility.Masks.For(enrollmentNode)
                ?? visibility.Masks.For(siteNode);

            if (accessMask is { } accessReason)
            {
                representedMaskedEnrollments.Add(obs.EnrollmentId);
                rows.Add(ProjectedObservationRow.CreateAccessMasked(snapshot.StudyCode, accessReason));
                continue;
            }

            enrollmentById.TryGetValue(obs.EnrollmentId, out var enrollment);
            siteById.TryGetValue(obs.StudySiteId, out var site);

            var obsMask = visibility.Masks.For(NodeRef.For(obs.ObservationId));
            if (obsMask == MaskReason.Blinded || (obs.IsBlindedTreatment && !visibility.UnblindedEnrollments.Contains(obs.EnrollmentId)))
            {
                rows.Add(ProjectedObservationRow.CreateBlinded(snapshot.StudyCode, enrollment, site, obs));
                continue;
            }

            if (obsMask is { } hiddenReason)
            {
                representedMaskedEnrollments.Add(obs.EnrollmentId);
                rows.Add(ProjectedObservationRow.CreateAccessMasked(snapshot.StudyCode, hiddenReason));
                continue;
            }

            rows.Add(ProjectedObservationRow.CreateVisible(snapshot.StudyCode, enrollment, site, obs));
        }

        // When exporting all records, ensure unauthorized enrollments with zero observations still emit a masked placeholder.
        if (lockFilter == StudyLockFilter.All)
        {
            foreach (var enrollment in snapshot.Enrollments)
            {
                if (representedMaskedEnrollments.Contains(enrollment.EnrollmentId))
                {
                    continue;
                }

                var mask = visibility.Masks.For(NodeRef.For(enrollment.EnrollmentId))
                    ?? visibility.Masks.For(NodeRef.For(enrollment.StudySiteId));
                if (mask is { } maskReason && snapshot.Observations.All(o => o.EnrollmentId != enrollment.EnrollmentId))
                {
                    rows.Add(ProjectedObservationRow.CreateAccessMasked(snapshot.StudyCode, maskReason));
                }
            }
        }

        return new ProjectedStudyExport(
            snapshot,
            lockFilterName,
            generatedAt,
            rows,
            visibility);
    }

    private static bool TryParseStudyFormat(string? raw, out StudyExportFormat format)
    {
        var normalized = string.IsNullOrWhiteSpace(raw) ? "csv" : raw.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "csv":
                format = StudyExportFormat.Csv;
                return true;
            case "odm_xml" or "odm":
                format = StudyExportFormat.OdmXml;
                return true;
            case "dataset_json" or "dataset-json":
                format = StudyExportFormat.DatasetJson;
                return true;
            case "sas_bundle" or "sas-bundle":
                format = StudyExportFormat.SasBundle;
                return true;
            case "r_bundle" or "r-bundle":
                format = StudyExportFormat.RBundle;
                return true;
            case "study_archive" or "study-archive":
                format = StudyExportFormat.StudyArchive;
                return true;
            default:
                format = default;
                return false;
        }
    }

    private static bool TryParseCasebookFormat(string? raw, out CasebookExportFormat format)
    {
        var normalized = string.IsNullOrWhiteSpace(raw) ? "json" : raw.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "json":
                format = CasebookExportFormat.Json;
                return true;
            case "odm_xml" or "odm":
                format = CasebookExportFormat.OdmXml;
                return true;
            case "html":
                format = CasebookExportFormat.Html;
                return true;
            case "pdf":
                format = CasebookExportFormat.Pdf;
                return true;
            default:
                format = default;
                return false;
        }
    }

    private static bool TryParseLockFilter(string? raw, out StudyLockFilter filter, out string normalized)
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

    private static string SanitizeFileName(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_').ToArray();
        return chars.Length == 0 ? "export" : new string(chars);
    }
}

internal enum StudyExportFormat
{
    Csv,
    OdmXml,
    DatasetJson,
    SasBundle,
    RBundle,
    StudyArchive,
}

internal enum CasebookExportFormat
{
    Json,
    OdmXml,
    Html,
    Pdf,
}

internal enum StudyLockFilter
{
    All,
    LockedOnly,
    FrozenOrLocked,
    UnlockedOnly,
}

internal sealed record StudyExportVisibility(
    IReadOnlyDictionary<NodeRef, MaskReason> Masks,
    HashSet<EnrollmentId> UnblindedEnrollments);

internal sealed record ProjectedStudyExport(
    StudyExportSnapshot Snapshot,
    string LockFilter,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ProjectedObservationRow> Rows,
    StudyExportVisibility Visibility);

internal sealed record ProjectedObservationRow(
    string StudyCode,
    string SiteCode,
    string SubjectCode,
    string EnrollmentRef,
    string ObservationRef,
    string? CollectionInstanceRef,
    string CollectionSpecId,
    string? PlannedActivityId,
    string ItemId,
    string ConceptRef,
    string ConceptCode,
    string ConceptLabel,
    DateTimeOffset? ObservedAt,
    int? Revision,
    string ValueKind,
    string ResultText,
    decimal? ResultDecimal,
    string? ResultUnit,
    string? LockState,
    bool Masked,
    string? MaskReason,
    string? RecordedBy,
    DateTimeOffset? RecordedAt,
    ExportObservationSnapshot? SourceObservation)
{
    public static ProjectedObservationRow CreateAccessMasked(string studyCode, MaskReason reason) =>
        new(
            StudyCode: studyCode,
            SiteCode: "masked",
            SubjectCode: "masked",
            EnrollmentRef: "masked",
            ObservationRef: "masked",
            CollectionInstanceRef: null,
            CollectionSpecId: "masked",
            PlannedActivityId: null,
            ItemId: "masked",
            ConceptRef: "masked",
            ConceptCode: "masked",
            ConceptLabel: "masked",
            ObservedAt: null,
            Revision: null,
            ValueKind: "masked",
            ResultText: "masked",
            ResultDecimal: null,
            ResultUnit: null,
            LockState: null,
            Masked: true,
            MaskReason: reason.ToString(),
            RecordedBy: null,
            RecordedAt: null,
            SourceObservation: null);

    public static ProjectedObservationRow CreateBlinded(
        string studyCode,
        ExportEnrollmentSnapshot? enrollment,
        ExportSiteSnapshot? site,
        ExportObservationSnapshot obs) =>
        new(
            StudyCode: studyCode,
            SiteCode: site?.SiteCode ?? enrollment?.SiteCode ?? obs.StudySiteId.ToString(),
            SubjectCode: enrollment?.SubjectCode ?? obs.EnrollmentId.ToString(),
            EnrollmentRef: NodeRef.For(obs.EnrollmentId).ToString(),
            ObservationRef: "masked",
            CollectionInstanceRef: null,
            CollectionSpecId: obs.CollectionSpecId ?? "unscheduled",
            PlannedActivityId: obs.PlannedActivityId,
            ItemId: obs.ItemId ?? obs.ConceptCode,
            ConceptRef: obs.Concept.ToString(),
            ConceptCode: obs.ConceptCode,
            ConceptLabel: obs.ConceptLabel,
            ObservedAt: obs.ObservedAt,
            Revision: obs.CurrentRevision.Number,
            ValueKind: "masked",
            ResultText: "masked",
            ResultDecimal: null,
            ResultUnit: null,
            LockState: obs.EffectiveLock?.ToString(),
            Masked: true,
            MaskReason: Cadence.Kernel.References.MaskReason.Blinded.ToString(),
            RecordedBy: null,
            RecordedAt: obs.CurrentRevision.RecordedAt,
            SourceObservation: obs);

    public static ProjectedObservationRow CreateVisible(
        string studyCode,
        ExportEnrollmentSnapshot? enrollment,
        ExportSiteSnapshot? site,
        ExportObservationSnapshot obs)
    {
        var current = obs.CurrentRevision.Value;
        decimal? numeric = current is QuantityValue qv ? qv.Value : null;
        string? unit = current is QuantityValue qu ? qu.Unit : null;
        string display = current switch
        {
            QuantityValue quantity => quantity.ToResultString(),
            _ => current.ToDisplayString(),
        };

        return new ProjectedObservationRow(
            StudyCode: studyCode,
            SiteCode: site?.SiteCode ?? enrollment?.SiteCode ?? obs.StudySiteId.ToString(),
            SubjectCode: enrollment?.SubjectCode ?? obs.EnrollmentId.ToString(),
            EnrollmentRef: NodeRef.For(obs.EnrollmentId).ToString(),
            ObservationRef: NodeRef.For(obs.ObservationId, obs.CurrentRevision.Number).ToString(),
            CollectionInstanceRef: obs.CollectionInstanceId is { } cid ? NodeRef.For(cid).ToString() : null,
            CollectionSpecId: obs.CollectionSpecId ?? "unscheduled",
            PlannedActivityId: obs.PlannedActivityId,
            ItemId: obs.ItemId ?? obs.ConceptCode,
            ConceptRef: obs.Concept.ToString(),
            ConceptCode: obs.ConceptCode,
            ConceptLabel: obs.ConceptLabel,
            ObservedAt: obs.ObservedAt,
            Revision: obs.CurrentRevision.Number,
            ValueKind: current.Kind.ToString(),
            ResultText: display,
            ResultDecimal: numeric,
            ResultUnit: unit,
            LockState: obs.EffectiveLock?.ToString(),
            Masked: false,
            MaskReason: null,
            RecordedBy: obs.CurrentRevision.RecordedBy,
            RecordedAt: obs.CurrentRevision.RecordedAt,
            SourceObservation: obs);
    }
}

/// <summary>Streamed JSON response shape for <c>GET /api/v1/kernel/enrollments/{enrollmentRef}/casebook</c>.</summary>
/// <param name="Producer">Component that generated the certified casebook projection.</param>
/// <param name="GeneratedAt">Server timestamp from <see cref="IClock"/> when the casebook was projected.</param>
/// <param name="RequestedBy">Authenticated caller identifier.</param>
/// <param name="ValidAt">Evaluated valid-time coordinate of the bitemporal snapshot.</param>
/// <param name="KnownAt">Evaluated recorded-time coordinate of the bitemporal snapshot.</param>
/// <param name="CasebookDigestSha256">Canonical SHA-256 digest over the projected casebook contents.</param>
/// <param name="StudyRef">Study NodeRef.</param>
/// <param name="StudyCode">Study code.</param>
/// <param name="StudyTitle">Study title.</param>
/// <param name="SiteRef">Study site NodeRef, or <c>masked</c> when hidden.</param>
/// <param name="SiteCode">Site code, or <c>masked</c> when hidden.</param>
/// <param name="EnrollmentRef">Enrollment NodeRef, or <c>masked</c> when hidden.</param>
/// <param name="SubjectCode">Pseudonymous subject code (never a direct identifier), or <c>masked</c> when hidden.</param>
/// <param name="EnrollmentStatus">Enrollment status at the evaluated coordinate, or null when masked.</param>
/// <param name="EnrollmentNumber">Gap-free study enrollment number once enrolled at the evaluated coordinate, or null before enrollment or when masked.</param>
/// <param name="Masked">True when the enrollment itself is masked for the caller.</param>
/// <param name="Mask">Why the enrollment is masked, or null when visible.</param>
/// <param name="Forms">Form instances collected for the participant.</param>
/// <param name="Observations">Recorded observations with current and superseded revisions.</param>
/// <param name="Queries">Raised queries and answers on the participant's observations.</param>
/// <param name="Verifications">Source data verifications recorded on the participant's observations.</param>
/// <param name="Locks">Locks covering the study, site, enrollment, forms or observations.</param>
/// <param name="Attestations">Electronic signatures recorded on forms and lock assertions.</param>
/// <param name="AuditTrail">Kernel audit-chain entries for the participant's clinical records.</param>
public sealed record KernelCasebookResponse(
    string Producer,
    DateTimeOffset GeneratedAt,
    string RequestedBy,
    DateTimeOffset ValidAt,
    DateTimeOffset KnownAt,
    string CasebookDigestSha256,
    string StudyRef,
    string StudyCode,
    string StudyTitle,
    string SiteRef,
    string SiteCode,
    string EnrollmentRef,
    string SubjectCode,
    string? EnrollmentStatus,
    int? EnrollmentNumber,
    bool Masked,
    string? Mask,
    IReadOnlyList<KernelCasebookFormView> Forms,
    IReadOnlyList<KernelCasebookObservationView> Observations,
    IReadOnlyList<KernelCasebookQueryView> Queries,
    IReadOnlyList<KernelCasebookVerificationView> Verifications,
    IReadOnlyList<KernelLockResponse> Locks,
    IReadOnlyList<KernelCasebookAttestationView> Attestations,
    IReadOnlyList<ObservationAuditView> AuditTrail);

/// <summary>One form instance in a participant casebook.</summary>
public sealed record KernelCasebookFormView(
    string Ref,
    string CollectionSpecId,
    string Title,
    string? PlannedActivityId,
    string? PlannedActivityName,
    DateTimeOffset? CollectedAt,
    DateTimeOffset? OpenedAt,
    string? OpenedBy,
    string? ContentHash,
    bool Masked,
    string? Mask);

/// <summary>One observation with its current and superseded revisions in a participant casebook.</summary>
public sealed record KernelCasebookObservationView(
    string Ref,
    string? CollectionInstanceRef,
    string? CollectionSpecId,
    string? ItemId,
    string ConceptRef,
    string ConceptCode,
    string ConceptLabel,
    DateTimeOffset? ObservedAt,
    int? CurrentRevision,
    string ValueKind,
    string DisplayValue,
    decimal? NumericValue,
    string? Unit,
    string? EffectiveLock,
    bool Masked,
    string? Mask,
    IReadOnlyList<KernelCasebookRevisionView> Revisions);

/// <summary>One revision (current or superseded) of an observation in a participant casebook.</summary>
public sealed record KernelCasebookRevisionView(
    int Revision,
    string ValueKind,
    string DisplayValue,
    decimal? NumericValue,
    string? Unit,
    string? ReasonForChange,
    string? RecordedBy,
    DateTimeOffset RecordedAt,
    DateTimeOffset? SupersededAt,
    bool Superseded);

/// <summary>One query or query answer in a participant casebook.</summary>
public sealed record KernelCasebookQueryView(
    string Ref,
    string Kind,
    string TargetRef,
    string? Text,
    string? AssertedBy,
    DateTimeOffset AssertedAt,
    string? ReasonForChange,
    bool Closed,
    DateTimeOffset? ClosedAt,
    int? Revision = null);

/// <summary>One source data verification entry in a participant casebook.</summary>
public sealed record KernelCasebookVerificationView(
    string Ref,
    string ObservationRef,
    int? Revision,
    string? VerifiedBy,
    DateTimeOffset VerifiedAt,
    string? ReasonForChange,
    bool Retracted,
    DateTimeOffset? RetractedAt);

/// <summary>One 21 CFR Part 11 electronic signature (<c>Attestation</c>) in a participant casebook.</summary>
public sealed record KernelCasebookAttestationView(
    string Ref,
    string TargetRef,
    string Meaning,
    string SignerId,
    DateTimeOffset AuthenticatedAt,
    string Method,
    DateTimeOffset SignedAt,
    string TargetContentHash,
    string Reason);
