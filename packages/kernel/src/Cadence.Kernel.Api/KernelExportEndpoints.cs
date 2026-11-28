using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;

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
    /// <summary>Response header carrying the lowercase hexadecimal SHA-256 digest of a streamed export response.</summary>
    public const string ContentSha256Header = "X-Content-Sha256";

    /// <summary>Response header carrying the wall-clock UTC timestamp when the export response was generated.</summary>
    public const string GeneratedAtHeader = "X-Generated-At";

    /// <summary>Response header indicating whether records outside the caller's visible scope were omitted.</summary>
    public const string HiddenMoreExistHeader = "X-Hidden-More-Exist";

    /// <summary>Response header listing distinct mask reasons when <see cref="HiddenMoreExistHeader"/> is true.</summary>
    public const string HiddenReasonsHeader = "X-Hidden-Reasons";

    /// <summary>Response header reporting collapsed hidden scopes without leaking cross-site record counts (ADR-2590).</summary>
    public const string HiddenScopesHeader = "X-Cadence-Hidden-Scopes";

    private static readonly Capability StudyExportCapability = new("study.export", VerbClass.Execute);
    private static readonly Capability EnrollmentExportCapability = new("enrollment.export", VerbClass.Execute);

    internal static void Map(RouteGroupBuilder reads)
    {
        reads.MapGet("/studies/{studyRef}/exports", StreamStudyExportAsync)
            .WithName("KernelExportStudySnapshot")
            .WithSummary("Streams a bitemporal study snapshot as CSV, CDISC ODM v1.3.2 XML, CDISC Dataset-JSON, SAS bundle ZIP, R bundle ZIP, or Study Archive ZIP.")
            .WithDescription("Projects canonical kernel observations at as_of(valid_at, known_at), filtering to the caller's visible scope, reporting omitted scopes as a collapsed indicator, and masking blinded treatment values in place (K-TRUTH-1, K-AUTH-4, ADR-2590).")
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
        IKernelCommandSender sender,
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

        if (!KernelStudyExportProjector.TryParseStudyFormat(format, out var exportFormat, out var formatToken))
        {
            return KernelProblems.BadRequest(
                "Invalid export format",
                "Supported study export formats are csv, odm_xml, dataset_json, sas_bundle, r_bundle, and study_archive.");
        }

        if (!KernelStudyExportProjector.TryParseLockFilter(lockFilter, out var parsedLockFilter, out var normalizedLockFilter))
        {
            return KernelProblems.BadRequest(
                "Invalid lock_filter",
                "Supported lock_filter values are all, locked_only, frozen_or_locked, and unlocked_only.");
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await navigator.ResolveAsync(tenant, studyNode, cancellationToken).ConfigureAwait(false) is null)
        {
            return KernelProblems.NotFound(studyNode);
        }

        var generatedAt = clock.UtcNow;
        var effectiveValidAt = validAt ?? asOf ?? generatedAt;
        var effectiveKnownAt = knownAt ?? asOf ?? generatedAt;
        var studyId = StudyId.FromGuid(studyNode.Id);
        var (contentType, mediaType, extension) = exportFormat switch
        {
            StudyExportFormat.Csv => ("text/csv; charset=utf-8", "text/csv", "csv"),
            StudyExportFormat.OdmXml => ("application/xml; charset=utf-8", "application/xml", "odm.xml"),
            StudyExportFormat.DatasetJson => ("application/json; charset=utf-8", "application/json", "dataset.json"),
            StudyExportFormat.SasBundle => ("application/zip", "application/zip", "sas_bundle.zip"),
            StudyExportFormat.RBundle => ("application/zip", "application/zip", "r_bundle.zip"),
            StudyExportFormat.StudyArchive => ("application/zip", "application/zip", "study_archive.zip"),
            _ => ("application/json; charset=utf-8", "application/json", "dataset.json"),
        };

        var exportDecision = await probe.CanIAsync(tenant, StudyExportCapability, studyNode, cancellationToken).ConfigureAwait(false);
        if (!exportDecision.Permitted)
        {
            var deniedVersionId = ArtifactVersionId.New(generatedAt);
            var deniedReceipt = await sender.SendAsync(
                new ExportStudySnapshot(
                    tenant,
                    studyId,
                    ArtifactId.New(generatedAt),
                    deniedVersionId,
                    $"Study snapshot export ({formatToken}, {normalizedLockFilter})",
                    new ArtifactContent(ContentHash.OfBytes([]), 1L, mediaType, $"export://studies/{studyId}/{deniedVersionId}"),
                    formatToken,
                    normalizedLockFilter,
                    effectiveValidAt,
                    effectiveKnownAt,
                    IncludesUnblindedData: false,
                    KernelStudyExportProjector.ReadOrCreateIdempotencyKey(context, generatedAt),
                    $"Export study snapshot ({studyId}, format={formatToken}, lock_filter={normalizedLockFilter})"),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return deniedReceipt.IsSuccess
                ? KernelProblems.NotVisible("You are not permitted to export this study.")
                : KernelErrorEndpoints.Problem(deniedReceipt.Error!);
        }

        var snapshot = await exportReader.ReadStudySnapshotAsync(
            tenant,
            studyId,
            effectiveValidAt,
            effectiveKnownAt,
            cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return KernelProblems.NotFound(studyNode);
        }

        var visibilityMap = await KernelStudyExportProjector.EvaluateStudyVisibilityAsync(
            tenant,
            studyNode,
            snapshot,
            visibility,
            probe,
            actor,
            cancellationToken).ConfigureAwait(false);
        if (visibilityMap.Masks.For(studyNode) is { } studyMask)
        {
            return KernelProblems.NotVisible($"You may not see this study ({studyMask}), so its clinical exports are refused.");
        }

        var (visibleObservations, visibleEnrollmentIds, hiddenScopes) = KernelStudyExportProjector.PartitionStudyVisibility(snapshot, visibilityMap);
        var filteredObservations = KernelStudyExportProjector.FilterObservations(visibleObservations, parsedLockFilter);
        var projected = KernelStudyExportProjector.ProjectStudyExport(
            snapshot,
            filteredObservations,
            normalizedLockFilter,
            snapshot.KnownAt,
            visibilityMap,
            visibleEnrollmentIds,
            hiddenScopes);

        var safeCode = KernelExportFormatting.SanitizeFileName(snapshot.StudyCode);
        var effectiveHidden = projected.Hidden;

        await using var buffer = new MemoryStream();
        switch (exportFormat)
        {
            case StudyExportFormat.Csv:
                await KernelExportStreamer.WriteStudyCsvAsync(buffer, projected, cancellationToken).ConfigureAwait(false);
                break;
            case StudyExportFormat.OdmXml:
                await KernelExportStreamer.WriteStudyOdmXmlAsync(buffer, projected, cancellationToken).ConfigureAwait(false);
                break;
            case StudyExportFormat.DatasetJson:
                await KernelExportStreamer.WriteStudyDatasetJsonAsync(buffer, projected, cancellationToken).ConfigureAwait(false);
                break;
            case StudyExportFormat.SasBundle:
            case StudyExportFormat.RBundle:
            case StudyExportFormat.StudyArchive:
                {
                    var callerId = actor.Actor?.Id ?? "anonymous";
                    IReadOnlyList<KernelCasebookResponse> archiveCasebooks = [];
                    if (exportFormat == StudyExportFormat.StudyArchive)
                    {
                        archiveCasebooks = await KernelStudyExportProjector.BuildStudyArchiveCasebooksAsync(
                            tenant,
                            snapshot,
                            filteredObservations,
                            parsedLockFilter,
                            effectiveValidAt,
                            effectiveKnownAt,
                            snapshot.KnownAt,
                            callerId,
                            visibilityMap,
                            visibleEnrollmentIds,
                            exportReader,
                            cancellationToken).ConfigureAwait(false);
                    }

                    effectiveHidden = await KernelExportStreamer.WriteStudyBundleZipAsync(
                        buffer,
                        projected,
                        exportFormat,
                        callerId,
                        archiveCasebooks,
                        cancellationToken).ConfigureAwait(false);
                    break;
                }
        }

        var payloadBytes = buffer.ToArray();
        var wireSha256 = Convert.ToHexString(SHA256.HashData(payloadBytes)).ToLowerInvariant();
        var contentHash = ContentHash.OfBytes(payloadBytes);
        bool includesUnblinded = filteredObservations.Any(o =>
            o.IsBlindedTreatment
            && visibilityMap.Masks.For(NodeRef.For(o.EnrollmentId)) is null
            && visibilityMap.Masks.For(NodeRef.For(o.StudySiteId)) is null
            && visibilityMap.UnblindedEnrollments.Contains(o.EnrollmentId));
        var disclosure = includesUnblinded ? "unblinded" : "blinded";
        var artifactId = ArtifactId.New(generatedAt);
        var versionId = ArtifactVersionId.New(generatedAt);
        var idempotencyKey = KernelStudyExportProjector.ReadOrCreateIdempotencyKey(context, generatedAt);
        var reason = $"Export study snapshot ({snapshot.StudyCode}, format={formatToken}, lock_filter={normalizedLockFilter}, disclosure={disclosure})";

        var receipt = await sender.SendAsync(
            new ExportStudySnapshot(
                tenant,
                studyId,
                artifactId,
                versionId,
                $"Study snapshot export {snapshot.StudyCode} ({formatToken}, {normalizedLockFilter}, {disclosure})",
                new ArtifactContent(contentHash, Math.Max(1L, payloadBytes.LongLength), mediaType, $"export://studies/{studyId}/{versionId}"),
                formatToken,
                normalizedLockFilter,
                effectiveValidAt,
                effectiveKnownAt,
                includesUnblinded,
                idempotencyKey,
                reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!receipt.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(receipt.Error!);
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = contentType;
        context.Response.Headers[ContentSha256Header] = wireSha256;
        context.Response.Headers[GeneratedAtHeader] = generatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        context.Response.Headers[HiddenMoreExistHeader] = effectiveHidden.MoreExist ? "true" : "false";
        if (effectiveHidden.MoreExist)
        {
            context.Response.Headers[HiddenReasonsHeader] = string.Join(',', effectiveHidden.Reasons);
        }

        context.Response.Headers[HiddenScopesHeader] = KernelStudyExportProjector.FormatHiddenScopesHeader(
            new HiddenScopeSummary(effectiveHidden.MoreExist, effectiveHidden.Reasons));
        context.Response.Headers.ETag = $"\"{wireSha256}\"";
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{safeCode}-{normalizedLockFilter}.{extension}\"";
        await context.Response.Body.WriteAsync(payloadBytes, cancellationToken).ConfigureAwait(false);
        return Results.Empty;
    }

    internal static async Task<IResult> StreamEnrollmentCasebookAsync(
        string enrollmentRef,
        [FromQuery(Name = "format")] string? format,
        [FromQuery(Name = "as_of")] DateTimeOffset? asOf,
        [FromQuery(Name = "valid_at")] DateTimeOffset? validAt,
        [FromQuery(Name = "known_at")] DateTimeOffset? knownAt,
        HttpContext context,
        IKernelNavigator navigator,
        IKernelExportReader exportReader,
        IKernelCommandSender sender,
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

        if (!KernelStudyExportProjector.TryParseCasebookFormat(format, out var casebookFormat, out var formatToken))
        {
            return KernelProblems.BadRequest(
                "Invalid casebook format",
                "Supported casebook formats are json, odm_xml, html, and pdf.");
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await navigator.ResolveAsync(tenant, enrollmentNode, cancellationToken).ConfigureAwait(false) is null)
        {
            return KernelProblems.NotFound(enrollmentNode);
        }

        var requestNow = clock.UtcNow;
        var hasExplicitCut = asOf is not null || validAt is not null || knownAt is not null;
        var queryValidAt = validAt ?? asOf ?? requestNow;
        var queryKnownAt = knownAt ?? asOf ?? validAt ?? requestNow;
        var enrollmentId = EnrollmentId.FromGuid(enrollmentNode.Id);
        var (contentType, mediaType, extension) = casebookFormat switch
        {
            CasebookExportFormat.OdmXml => ("application/xml; charset=utf-8", "application/xml", "casebook.odm.xml"),
            CasebookExportFormat.Html => ("text/html; charset=utf-8", "text/html", "casebook.html"),
            CasebookExportFormat.Pdf => ("application/pdf", "application/pdf", "casebook.pdf"),
            _ => ("application/json; charset=utf-8", "application/json", "casebook.json"),
        };

        var exportDecision = await probe.CanIAsync(tenant, EnrollmentExportCapability, enrollmentNode, cancellationToken).ConfigureAwait(false);
        if (!exportDecision.Permitted)
        {
            var deniedVersionId = ArtifactVersionId.New(requestNow);
            var deniedReceipt = await sender.SendAsync(
                new ExportEnrollmentCasebook(
                    tenant,
                    enrollmentId,
                    StudyId.FromGuid(Guid.Empty),
                    ArtifactId.New(requestNow),
                    deniedVersionId,
                    $"Participant casebook export ({formatToken})",
                    new ArtifactContent(ContentHash.OfBytes([]), 1L, mediaType, $"export://enrollments/{enrollmentId}/{deniedVersionId}"),
                    formatToken,
                    queryValidAt,
                    queryKnownAt,
                    IncludesUnblindedData: false,
                    KernelStudyExportProjector.ReadOrCreateIdempotencyKey(context, requestNow),
                    $"Export participant casebook ({enrollmentId}, format={formatToken})"),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return deniedReceipt.IsSuccess
                ? KernelProblems.NotVisible("You are not permitted to export this participant casebook.")
                : KernelErrorEndpoints.Problem(deniedReceipt.Error!);
        }

        var snapshot = await exportReader.ReadEnrollmentCasebookAsync(
            tenant,
            enrollmentId,
            queryValidAt,
            queryKnownAt,
            cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return KernelProblems.NotFound(enrollmentNode);
        }

        DateTimeOffset casebookGeneratedAt;
        if (!hasExplicitCut)
        {
            var canonicalCut = snapshot.CanonicalCutAt ?? snapshot.KnownAt;
            snapshot = snapshot with { ValidAt = canonicalCut, KnownAt = canonicalCut };
            casebookGeneratedAt = canonicalCut;
        }
        else
        {
            casebookGeneratedAt = snapshot.KnownAt;
        }

        var effectiveValidAt = snapshot.ValidAt;
        var effectiveKnownAt = snapshot.KnownAt;

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
        if (enrollmentMask is { } hiddenEnrollmentMask && hiddenEnrollmentMask != MaskReason.Blinded)
        {
            return KernelProblems.NotVisible($"You may not see this participant enrollment ({hiddenEnrollmentMask}), so its casebook is refused.");
        }

        var hasBlindedNodeMask = snapshot.Observations.Any(o => masks.For(NodeRef.For(o.ObservationId)) == MaskReason.Blinded);
        bool mayUnblind = false;
        if (enrollmentMask is null && !hasBlindedNodeMask && snapshot.Observations.Any(o => o.IsBlindedTreatment))
        {
            var unblindDecision = await probe.CanIAsync(
                tenant,
                KernelStudyExportProjector.EnrollmentUnblindCapability,
                enrollmentNode,
                cancellationToken).ConfigureAwait(false);
            mayUnblind = unblindDecision.Permitted;
        }

        var projected = KernelExportStreamer.BuildCasebookModel(
            snapshot,
            casebookGeneratedAt,
            actor.Actor?.Id ?? "anonymous",
            enrollmentMask,
            mayUnblind);

        await using var buffer = new MemoryStream();
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
        var contentHash = ContentHash.OfBytes(payloadBytes);
        var fileLabel = projected.Masked ? "masked-participant" : KernelExportFormatting.SanitizeFileName(snapshot.SubjectCode);
        bool includesUnblinded = enrollmentMask is null && mayUnblind && snapshot.Observations.Any(o => o.IsBlindedTreatment);
        var disclosure = KernelStudyExportProjector.ResolveCasebookDisclosure(projected.Masked, includesUnblinded);
        var artifactId = ArtifactId.New(requestNow);
        var versionId = ArtifactVersionId.New(requestNow);
        var idempotencyKey = KernelStudyExportProjector.ReadOrCreateIdempotencyKey(context, requestNow);
        var reason = $"Export participant casebook ({snapshot.StudyCode}/{fileLabel}, format={formatToken}, disclosure={disclosure})";

        var receipt = await sender.SendAsync(
            new ExportEnrollmentCasebook(
                tenant,
                enrollmentId,
                snapshot.StudyId,
                artifactId,
                versionId,
                $"Participant casebook export {snapshot.StudyCode}/{fileLabel} ({formatToken}, {disclosure})",
                new ArtifactContent(contentHash, Math.Max(1L, payloadBytes.LongLength), mediaType, $"export://enrollments/{enrollmentId}/{versionId}"),
                formatToken,
                effectiveValidAt,
                effectiveKnownAt,
                includesUnblinded,
                idempotencyKey,
                reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!receipt.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(receipt.Error!);
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = contentType;
        context.Response.Headers[ContentSha256Header] = wireSha256;
        context.Response.Headers[GeneratedAtHeader] = requestNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        context.Response.Headers[HiddenScopesHeader] = projected.Masked
            ? $"more_exist=true;reasons={projected.Mask ?? "NotPermitted"}"
            : "more_exist=false";
        context.Response.Headers.ETag = $"\"{wireSha256}\"";
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{KernelExportFormatting.SanitizeFileName(snapshot.StudyCode)}-{fileLabel}.{extension}\"";
        await context.Response.Body.WriteAsync(payloadBytes, cancellationToken).ConfigureAwait(false);
        return Results.Empty;
    }
}
