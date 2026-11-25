using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
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
using Cadence.Security.DependencyInjection;

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

    private static readonly Capability EnrollmentUnblindCapability = new("enrollment.unblind", VerbClass.Read);
    private static readonly Capability StudyExportCapability = new("study.export", VerbClass.Execute);
    private static readonly Capability EnrollmentExportCapability = new("enrollment.export", VerbClass.Execute);

    internal static void Map(RouteGroupBuilder reads)
    {
        reads.MapGet("/studies/{studyRef}/exports", StreamStudyExportAsync)
            .WithName("KernelExportStudySnapshot")
            .WithSummary("Streams a bitemporal study snapshot as CSV, CDISC ODM v1.3.2 XML, CDISC Dataset-JSON, SAS bundle ZIP, R bundle ZIP, or Study Archive ZIP.")
            .WithDescription("Projects canonical kernel observations at as_of(valid_at, known_at), filtering to the caller's visible scope, reporting omitted scopes as a collapsed indicator, and masking blinded treatment values in place (K-TRUTH-1, K-AUTH-4, ADR-2590).")
            .WithIdempotencyKeyHeader(
                required: false,
                description: "Optional caller-supplied idempotency key recorded on the ExportStudySnapshot audit command; when omitted, the kernel generates a deterministic v7 export key.")
            .Produces<KernelDatasetJsonExportResponse>(StatusCodes.Status200OK, "application/json", "text/csv", "application/xml", "application/zip")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        reads.MapGet("/enrollments/{enrollmentRef}/casebook", StreamEnrollmentCasebookAsync)
            .WithName("KernelExportEnrollmentCasebook")
            .WithSummary("Streams a participant casebook with revisions, queries, verifications, attestations and SHA-256 digests.")
            .WithDescription("Streams JSON, CDISC ODM v1.3.2 XML, printable HTML, or certified PDF (%PDF-1.7) for one enrollment at as_of(valid_at, known_at), masking unauthorized or blinded content under K-AUTH-4.")
            .WithIdempotencyKeyHeader(
                required: false,
                description: "Optional caller-supplied idempotency key recorded on the ExportEnrollmentCasebook audit command; when omitted, the kernel generates a deterministic v7 export key.")
            .Produces<KernelCasebookResponse>(StatusCodes.Status200OK, "application/json", "application/xml", "text/html", "application/pdf")
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

        if (!TryParseStudyFormat(format, out var exportFormat, out var formatToken))
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
                    ReadOrCreateIdempotencyKey(context, generatedAt),
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

        var visibilityMap = await EvaluateStudyVisibilityAsync(
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

        var (visibleObservations, visibleEnrollmentIds, hiddenScopes) = PartitionStudyVisibility(snapshot, visibilityMap);
        var filteredObservations = FilterObservations(visibleObservations, parsedLockFilter);
        var projected = ProjectStudyExport(
            snapshot,
            filteredObservations,
            normalizedLockFilter,
            snapshot.KnownAt,
            visibilityMap,
            visibleEnrollmentIds,
            hiddenScopes);

        var safeCode = SanitizeFileName(snapshot.StudyCode);
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
                        archiveCasebooks = await BuildStudyArchiveCasebooksAsync(
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
        var idempotencyKey = ReadOrCreateIdempotencyKey(context, generatedAt);
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

        context.Response.Headers[HiddenScopesHeader] = FormatHiddenScopesHeader(
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

        if (!TryParseCasebookFormat(format, out var casebookFormat, out var formatToken))
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
                    ReadOrCreateIdempotencyKey(context, requestNow),
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
            var unblindDecision = await probe.CanIAsync(tenant, EnrollmentUnblindCapability, enrollmentNode, cancellationToken).ConfigureAwait(false);
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
        var fileLabel = projected.Masked ? "masked-participant" : SanitizeFileName(snapshot.SubjectCode);
        bool includesUnblinded = enrollmentMask is null && mayUnblind && snapshot.Observations.Any(o => o.IsBlindedTreatment);
        var disclosure = ResolveCasebookDisclosure(projected.Masked, includesUnblinded);
        var artifactId = ArtifactId.New(requestNow);
        var versionId = ArtifactVersionId.New(requestNow);
        var idempotencyKey = ReadOrCreateIdempotencyKey(context, requestNow);
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

    private static (List<ExportObservationSnapshot> VisibleObservations, HashSet<EnrollmentId> VisibleEnrollmentIds, HiddenScopeSummary HiddenScopes) PartitionStudyVisibility(
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

    private static ProjectedStudyExport ProjectStudyExport(
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

    private static string FormatHiddenScopesHeader(HiddenScopeSummary hiddenScopes) =>
        hiddenScopes.MoreExist
            ? $"more_exist=true;reasons={string.Join(',', hiddenScopes.Reasons)}"
            : "more_exist=false";

    private static string ResolveCasebookDisclosure(bool masked, bool includesUnblinded)
    {
        if (masked)
        {
            return "masked";
        }

        return includesUnblinded ? "unblinded" : "blinded";
    }

    private static string ReadOrCreateIdempotencyKey(HttpContext context, DateTimeOffset generatedAt)
    {
        var supplied = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
        return string.IsNullOrWhiteSpace(supplied)
            ? $"export-{Guid.CreateVersion7(generatedAt):N}"
            : supplied.Trim();
    }

    private static bool TryParseStudyFormat(string? raw, out StudyExportFormat format, out string normalizedFormat)
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

    private static bool TryParseCasebookFormat(string? raw, out CasebookExportFormat format, out string normalizedFormat)
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

/// <summary>Source system metadata block in a CDISC Dataset-JSON v1.1.0 study export.</summary>
/// <param name="Name">Producer component name.</param>
/// <param name="Version">Producer semantic version.</param>
public sealed record KernelDatasetJsonSourceSystem(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version);

/// <summary>Bitemporal cut coordinates recorded on a CDISC Dataset-JSON v1.1.0 study export.</summary>
/// <param name="ValidAt">Clinical validity cut timestamp.</param>
/// <param name="KnownAt">System knowledge cut timestamp.</param>
/// <param name="LockFilter">Effective lock filter applied to exported observations.</param>
public sealed record KernelDatasetJsonAsOf(
    [property: JsonPropertyName("valid_at")] DateTimeOffset ValidAt,
    [property: JsonPropertyName("known_at")] DateTimeOffset KnownAt,
    [property: JsonPropertyName("lock_filter")] string LockFilter);

/// <summary>CDISC Dataset-JSON v1.1.0 envelope streamed when <c>format=dataset_json</c> is requested.</summary>
/// <param name="DatasetJsonCreationDateTime">Creation timestamp of the exported Dataset-JSON payload.</param>
/// <param name="DatasetJsonVersion">Dataset-JSON specification version (<c>1.1.0</c>).</param>
/// <param name="FileOid">Unique file OID identifying the study and knowledge cut.</param>
/// <param name="Originator">Originating clinical platform.</param>
/// <param name="SourceSystem">Producer component and version metadata.</param>
/// <param name="StudyOid">Study protocol code.</param>
/// <param name="MetaDataVersionOid">Protocol metadata version OID.</param>
/// <param name="MetaDataRef">Relative URI to the study's protocol version catalog.</param>
/// <param name="AsOf">Bitemporal cut and lock filter applied to the export.</param>
/// <param name="ItemGroupData">Keyed CDISC Dataset-JSON item groups projected from kernel observations.</param>
public sealed record KernelDatasetJsonExportResponse(
    [property: JsonPropertyName("datasetJSONCreationDateTime")] DateTimeOffset DatasetJsonCreationDateTime,
    [property: JsonPropertyName("datasetJSONVersion")] string DatasetJsonVersion,
    [property: JsonPropertyName("fileOID")] string FileOid,
    [property: JsonPropertyName("originator")] string Originator,
    [property: JsonPropertyName("sourceSystem")] KernelDatasetJsonSourceSystem SourceSystem,
    [property: JsonPropertyName("studyOID")] string StudyOid,
    [property: JsonPropertyName("metaDataVersionOID")] string MetaDataVersionOid,
    [property: JsonPropertyName("metaDataRef")] string MetaDataRef,
    [property: JsonPropertyName("asOf")] KernelDatasetJsonAsOf AsOf,
    [property: JsonPropertyName("itemGroupData")] IReadOnlyDictionary<string, object?> ItemGroupData);

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

internal sealed record HiddenScopeSummary(bool MoreExist, IReadOnlyList<string> Reasons)
{
    public static HiddenScopeSummary None { get; } = new(false, []);
}

internal sealed record StudyExportVisibility(
    IReadOnlyDictionary<NodeRef, MaskReason> Masks,
    HashSet<EnrollmentId> UnblindedEnrollments);

internal sealed record ProjectedStudyExport(
    StudyExportSnapshot Snapshot,
    string LockFilter,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ProjectedObservationRow> Rows,
    StudyExportVisibility Visibility,
    IReadOnlySet<EnrollmentId> VisibleEnrollmentIds,
    KernelHiddenContentResponse Hidden,
    HiddenScopeSummary HiddenScopes);

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
    ExportObservationSnapshot? SourceObservation,
    CollectionInstanceId? GroupingCollectionInstanceId = null)
{
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
            Revision: null,
            ValueKind: "masked",
            ResultText: "masked",
            ResultDecimal: null,
            ResultUnit: null,
            LockState: obs.EffectiveLock?.ToString(),
            Masked: true,
            MaskReason: Cadence.Kernel.References.MaskReason.Blinded.ToString(),
            RecordedBy: null,
            RecordedAt: obs.CurrentRevision.RecordedAt,
            SourceObservation: null,
            GroupingCollectionInstanceId: obs.CollectionInstanceId);

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
            SourceObservation: obs,
            GroupingCollectionInstanceId: obs.CollectionInstanceId);
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
/// <param name="Masked">True when the enrollment itself is masked for the caller.</param>
/// <param name="Mask">Why the enrollment is masked, or null when visible.</param>
/// <param name="Forms">Form instances collected for the participant.</param>
/// <param name="Observations">Recorded observations with current and superseded revisions.</param>
/// <param name="Queries">Raised queries and answers on the participant's observations.</param>
/// <param name="Verifications">Source data verifications recorded on the participant's observations.</param>
/// <param name="Locks">Locks covering the study, site, enrollment, forms or observations.</param>
/// <param name="Attestations">Electronic signatures recorded on forms and lock assertions.</param>
/// <param name="AuditTrail">Kernel audit-chain entries for the participant's clinical records.</param>
/// <param name="ProtocolVersions">Internal protocol version snapshots used when rendering structured XML casebooks (omitted from JSON wire output).</param>
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
    bool Masked,
    string? Mask,
    IReadOnlyList<KernelCasebookFormView> Forms,
    IReadOnlyList<KernelCasebookObservationView> Observations,
    IReadOnlyList<KernelCasebookQueryView> Queries,
    IReadOnlyList<KernelCasebookVerificationView> Verifications,
    IReadOnlyList<KernelLockResponse> Locks,
    IReadOnlyList<KernelCasebookAttestationView> Attestations,
    IReadOnlyList<ObservationAuditView> AuditTrail,
    [property: JsonIgnore] IReadOnlyList<ExportProtocolSnapshot>? ProtocolVersions = null);

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
    string? Mask,
    [property: JsonIgnore] Guid? GroupingFormId = null);

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
    IReadOnlyList<KernelCasebookRevisionView> Revisions,
    [property: JsonIgnore] Guid? GroupingFormId = null);

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
