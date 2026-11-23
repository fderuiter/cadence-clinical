using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Domain;
using Cadence.Apps.Imaging.Infrastructure.Persistence;
using Cadence.Apps.Imaging.Services;
using Cadence.Deid;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Imaging.Endpoints;

public static class DicomWebEndpoints
{
    public static IEndpointRouteBuilder MapDicomWebEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/imaging")
            .WithTags("DICOMweb Integration");

        group.MapPost("/studies", StoreInstancesAsync)
            .WithName("StowRsStoreInstances")
            .RequirePermission("imaging_study:upload").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<IngestionResult>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .DisableAntiforgery();

        group.MapPost("/studies/batch", StoreBatchInstancesAsync)
            .WithName("BatchIngestStudyFolder")
            .RequirePermission("imaging_study:upload").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<BatchIngestionResult>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .DisableAntiforgery();

        group.MapGet("/studies/{studyUid}", GetStudyByUidAsync)
            .WithName("WadoRsGetStudy")
            .RequirePermission("imaging_study:read").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<ImagingStudyEnvelope>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/studies/{studyUid}/series/{seriesUid}/instances/{instanceUid}/frames/{frameList}", GetInstanceFramesAsync)
            .WithName("WadoRsGetInstanceFrames")
            .RequirePermission("imaging_study:read").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/certificates/{certificateId}", GetCertificateByIdAsync)
            .WithName("GetDeidCertificate")
            .RequirePermission("imaging_study:read").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<CertificateResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    // The operator recorded on a de-identification certificate is the signed caller, never a request header (ADR-2516).
    private static string OperatorIdentity(HttpContext httpContext) =>
        CallerScope.UserId(httpContext) ?? throw new InvalidOperationException("An admitted imaging upload has no signed caller.");

    private static IResult OutsideStudy() =>
        TypedResults.Problem(
            title: "Outside your studies",
            detail: "You are not assigned to this study.",
            statusCode: StatusCodes.Status403Forbidden);

    public static async Task<IResult> StoreInstancesAsync(
        HttpRequest request,
        IDicomIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        string subjectId = request.Query["subjectId"].ToString();
        string siteId = request.Query["siteId"].ToString();
        string studyId = request.Query["studyId"].ToString();

        byte[] payloadBytes;

        if (request.HasFormContentType && request.Form.Files.Count > 0)
        {
            var file = request.Form.Files[0];
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            payloadBytes = ms.ToArray();
        }
        else
        {
            using var ms = new MemoryStream();
            await request.Body.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            payloadBytes = ms.ToArray();
        }

        if (payloadBytes.Length == 0)
        {
            return TypedResults.BadRequest(new { code = "EMPTY_PAYLOAD", message = "Request payload cannot be empty." });
        }

        if (ClinicalScopeGuard.RequireStudyAndOptionalSite(request.HttpContext, studyId, siteId) is not null)
        {
            return OutsideStudy();
        }

        try
        {
            var result = await ingestionService.IngestInstanceAsync(
                payloadBytes,
                subjectId: subjectId,
                siteId: siteId,
                studyId: studyId,
                operatorIdentity: OperatorIdentity(request.HttpContext),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return TypedResults.Created($"/api/v1/imaging/studies/{result.AnonymizedStudyUid}", result);
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest(new { code = "DICOM_INGESTION_FAILED", message = ex.Message });
        }
    }

    public static async Task<IResult> StoreBatchInstancesAsync(
        HttpRequest request,
        IDicomIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        const long maxBatchSize = 2147483648L; // 2 GB
        if (request.ContentLength.HasValue && request.ContentLength.Value > maxBatchSize)
        {
            return TypedResults.Problem(
                detail: "Payload exceeds maximum 2 GB batch size limit.",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        string subjectId = request.Query["subjectId"].ToString();
        string siteId = request.Query["siteId"].ToString();
        string studyId = request.Query["studyId"].ToString();
        string timepointName = request.Query["timepointName"].ToString();

        var batchFiles = new System.Collections.Generic.List<BatchFileItem>();

        if (request.HasFormContentType && request.Form.Files.Count > 0)
        {
            foreach (var file in request.Form.Files)
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
                batchFiles.Add(new BatchFileItem(file.FileName, ms.ToArray()));
            }

            if (request.Form.TryGetValue("subjectId", out var formSubj) && string.IsNullOrWhiteSpace(subjectId))
            {
                subjectId = formSubj.ToString();
            }
            if (request.Form.TryGetValue("siteId", out var formSite) && string.IsNullOrWhiteSpace(siteId))
            {
                siteId = formSite.ToString();
            }
            if (request.Form.TryGetValue("studyId", out var formStudy) && string.IsNullOrWhiteSpace(studyId))
            {
                studyId = formStudy.ToString();
            }
            if (request.Form.TryGetValue("timepointName", out var formTimepoint) && string.IsNullOrWhiteSpace(timepointName))
            {
                timepointName = formTimepoint.ToString();
            }
        }
        else
        {
            using var ms = new MemoryStream();
            await request.Body.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            byte[] payloadBytes = ms.ToArray();
            if (payloadBytes.Length > 0)
            {
                bool isZipContent = request.ContentType?.Contains("zip", StringComparison.OrdinalIgnoreCase) ?? false;
                string filename = isZipContent ? "batch.zip" : "batch.dcm";
                batchFiles.Add(new BatchFileItem(filename, payloadBytes));
            }
        }

        if (batchFiles.Count == 0)
        {
            return TypedResults.BadRequest(new { code = "EMPTY_PAYLOAD", message = "Batch payload cannot be empty." });
        }

        if (ClinicalScopeGuard.RequireStudyAndOptionalSite(request.HttpContext, studyId, siteId) is not null)
        {
            return OutsideStudy();
        }

        var batchRequest = new BatchIngestionRequest(
            Files: batchFiles,
            SubjectId: subjectId,
            SiteId: siteId,
            StudyId: studyId,
            TimepointName: timepointName,
            OperatorIdentity: OperatorIdentity(request.HttpContext));

        try
        {
            var result = await ingestionService.IngestBatchAsync(batchRequest, cancellationToken).ConfigureAwait(false);
            return TypedResults.Created($"/api/v1/imaging/studies/{result.AnonymizedStudyUid}", result);
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest(new { code = "DICOM_BATCH_INGESTION_FAILED", message = ex.Message });
        }
    }

    private static async Task<IResult> GetStudyByUidAsync(
        string studyUid,
        ImagingDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var study = await dbContext.ImagingStudyEnvelopes
            .FirstOrDefaultAsync(s => s.StudyInstanceUid == studyUid || s.Id == studyUid, cancellationToken)
            .ConfigureAwait(false);

        if (study == null
            || ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, study.StudyId, study.SiteId, allowUnassignedSiteForScopedCaller: true) is not null)
        {
            return TypedResults.NotFound(new { code = "STUDY_NOT_FOUND", message = $"Study '{studyUid}' not found." });
        }

        return TypedResults.Ok(study);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "SonarAnalyzer.CSharp",
        "S2245:Using pseudorandom number generators (PRNGs) is security-sensitive",
        Justification = "Deterministic synthetic pixel frame payload seeded by SOP Instance UID, not cryptographic randomness.")]
    private static async Task<IResult> GetInstanceFramesAsync(
        string studyUid,
        string seriesUid,
        string instanceUid,
        string frameList,
        ImagingDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        _ = studyUid;
        _ = seriesUid;
        _ = frameList;

        var instance = await dbContext.ImagingInstanceEnvelopes
            .FirstOrDefaultAsync(i => i.SopInstanceUid == instanceUid || i.Id == instanceUid, cancellationToken)
            .ConfigureAwait(false);

        var parentStudy = instance == null
            ? null
            : await (from series in dbContext.ImagingSeriesEnvelopes
                     join study in dbContext.ImagingStudyEnvelopes on series.StudyEnvelopeId equals study.Id
                     where series.Id == instance.SeriesEnvelopeId
                     select new { study.StudyId, study.SiteId })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

        if (instance == null
            || parentStudy == null
            || ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, parentStudy.StudyId, parentStudy.SiteId, allowUnassignedSiteForScopedCaller: true) is not null)
        {
            return TypedResults.NotFound(new { code = "INSTANCE_NOT_FOUND", message = $"Instance '{instanceUid}' not found." });
        }

        byte[] frameBytes = new byte[instance.Rows * instance.Columns * 2];
        new Random(instanceUid.GetHashCode()).NextBytes(frameBytes);

        return TypedResults.Bytes(frameBytes, contentType: "application/octet-stream");
    }

    public static async Task<IResult> GetCertificateByIdAsync(
        string certificateId,
        ImagingDbContext dbContext,
        [FromServices] DicomDeidentifier deidentifier,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var cert = await dbContext.DeidentificationCertificates
            .FirstOrDefaultAsync(c => c.Id == certificateId, cancellationToken)
            .ConfigureAwait(false);

        if (cert == null || ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, cert.StudyId, cert.SiteId, allowUnassignedSiteForScopedCaller: true) is not null)
        {
            return TypedResults.NotFound(new { code = "CERTIFICATE_NOT_FOUND", message = $"Certificate '{certificateId}' not found." });
        }

        var domainCert = new DicomDeidCertificate
        {
            CertificateId = cert.Id,
            SourceSha256 = cert.SourceSha256,
            TargetSha256 = cert.TargetSha256,
            StudyId = cert.StudyId,
            SiteId = cert.SiteId,
            SubjectId = cert.SubjectId,
            OriginalStudyInstanceUidHash = cert.OriginalStudyInstanceUidHash,
            OriginalSopInstanceUidHash = cert.OriginalSopInstanceUidHash,
            AnonymizedStudyInstanceUid = cert.AnonymizedStudyInstanceUid,
            AnonymizedSopInstanceUid = cert.AnonymizedSopInstanceUid,
            ScrubbedTagsCount = cert.ScrubbedTagsCount,
            ScrubbedTags = JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(cert.ScrubbedTagsJson) ?? [],
            PixelMaskApplied = cert.PixelMaskApplied,
            PixelMasks = JsonSerializer.Deserialize<System.Collections.Generic.List<PixelMaskBoundingBox>>(cert.PixelMasksJson) ?? [],
            OperatorIdentity = cert.OperatorIdentity,
            Timestamp = cert.Timestamp,
            Signature = cert.Signature
        };

        bool isValid = deidentifier.VerifyCertificate(domainCert);

        return TypedResults.Ok(new CertificateResponse(
            Certificate: cert,
            IsSignatureValid: isValid));
    }
}

public record CertificateResponse(
    DeidentificationCertificate Certificate,
    bool IsSignatureValid);
