using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Application;
using Cadence.Apps.Imaging.Domain;
using Cadence.Apps.Imaging.Infrastructure.Streaming;
using Cadence.Apps.Imaging.Services;
using Cadence.Deid;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;
using Cadence.Storage;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Imaging.Endpoints;

public static class DicomWebEndpoints
{
    public const long MaxBatchSizeBytes = 2_147_483_648L; // 2 GB
    public const long MaxSingleInstanceSizeBytes = BatchArchiveQuotas.DefaultMaxEntryUncompressedBytes; // 128 MB
    public const long MaxFrameSizeBytes = DicomIngestionService.MaxFrameSizeBytes; // 64 MB

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
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
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
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/certificates/{certificateId}", GetCertificateByIdAsync)
            .WithName("GetDeidCertificate")
            .RequirePermission("imaging_study:read").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<CertificateResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    public static async Task<IResult> StoreInstancesAsync(
        HttpRequest request,
        IDicomIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ingestionService);

        if (request.ContentLength.HasValue && request.ContentLength.Value > MaxSingleInstanceSizeBytes)
        {
            return DicomBatchIngestionStreamCoordinator.PayloadTooLargeResult(
                $"Payload exceeds maximum {MaxSingleInstanceSizeBytes / (1024 * 1024)} MB single-instance size limit.");
        }

        var metadata = DicomMultipartHeaderHelpers.CreateInitialMetadata(request);

        await using var pooledPayload = new PooledBlockStream();
        try
        {
            if (request.HasFormContentType && request.Form.Files.Count > 0)
            {
                DicomMultipartHeaderHelpers.ApplyFormMetadataField(metadata, "subjectId", request.Form["subjectId"]);
                DicomMultipartHeaderHelpers.ApplyFormMetadataField(metadata, "siteId", request.Form["siteId"]);
                DicomMultipartHeaderHelpers.ApplyFormMetadataField(metadata, "studyId", request.Form["studyId"]);

                var file = request.Form.Files[0];
                if (file.Length > MaxSingleInstanceSizeBytes)
                {
                    return DicomBatchIngestionStreamCoordinator.PayloadTooLargeResult(
                        $"Payload exceeds maximum {MaxSingleInstanceSizeBytes / (1024 * 1024)} MB single-instance size limit.");
                }

                if (file.Length == 0)
                {
                    return TypedResults.BadRequest(new { code = "EMPTY_PAYLOAD", message = "Request payload cannot be empty." });
                }

                if (!metadata.HasAuthorizableStudyAndSiteScope)
                {
                    return DicomBatchIngestionStreamCoordinator.MissingUploadScopeResult();
                }

                if (ClinicalScopeGuard.RequireStudyAndSite(request.HttpContext, metadata.StudyValue, metadata.SiteValue) is not null)
                {
                    return ClinicalScopeGuard.OutsideStudy();
                }

                if (!metadata.HasCompleteUploadScope)
                {
                    return DicomBatchIngestionStreamCoordinator.MissingUploadScopeResult();
                }

                await using var fileStream = file.OpenReadStream();
                using var countingStream = new MaxBytesReadStream(fileStream, MaxSingleInstanceSizeBytes);
                await BatchStreamHelpers.CopyPooledAsync(countingStream, pooledPayload, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                using var countingStream = new MaxBytesReadStream(request.Body, MaxSingleInstanceSizeBytes);
                byte[] prefix = new byte[4];
                int prefixLength = await countingStream.ReadAtLeastAsync(prefix, prefix.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
                if (prefixLength == 0)
                {
                    return TypedResults.BadRequest(new { code = "EMPTY_PAYLOAD", message = "Request payload cannot be empty." });
                }

                if (!metadata.HasAuthorizableStudyAndSiteScope)
                {
                    return DicomBatchIngestionStreamCoordinator.MissingUploadScopeResult();
                }

                if (ClinicalScopeGuard.RequireStudyAndSite(request.HttpContext, metadata.StudyValue, metadata.SiteValue) is not null)
                {
                    return ClinicalScopeGuard.OutsideStudy();
                }

                if (!metadata.HasCompleteUploadScope)
                {
                    return DicomBatchIngestionStreamCoordinator.MissingUploadScopeResult();
                }

                await pooledPayload.WriteAsync(prefix.AsMemory(0, prefixLength), cancellationToken).ConfigureAwait(false);
                await BatchStreamHelpers.CopyPooledAsync(countingStream, pooledPayload, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (DicomPayloadLimitClassifier.TryGetPayloadTooLargeDetail(ex, out string? detail))
        {
            return DicomBatchIngestionStreamCoordinator.PayloadTooLargeResult(detail);
        }

        if (pooledPayload.Length == 0)
        {
            return TypedResults.BadRequest(new { code = "EMPTY_PAYLOAD", message = "Request payload cannot be empty." });
        }

        pooledPayload.Position = 0;
        byte[] payloadBytes = new byte[(int)pooledPayload.Length];
        await pooledPayload.ReadExactlyAsync(payloadBytes, cancellationToken).ConfigureAwait(false);

        try
        {
            var result = await ingestionService.IngestInstanceAsync(
                payloadBytes,
                subjectId: metadata.SubjectValue,
                siteId: metadata.SiteValue,
                studyId: metadata.StudyValue,
                operatorIdentity: CallerScope.RequiredUserId(request.HttpContext, "An admitted imaging upload has no signed caller."),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return TypedResults.Created($"/api/v1/imaging/studies/{result.AnonymizedStudyUid}", result);
        }
        catch (Exception ex) when (DicomPayloadLimitClassifier.TryGetPayloadTooLargeDetail(ex, out string? detail))
        {
            return DicomBatchIngestionStreamCoordinator.PayloadTooLargeResult(detail);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return TypedResults.BadRequest(new { code = "DICOM_INGESTION_FAILED", message = ex.Message });
        }
    }

    public static Task<IResult> StoreBatchInstancesAsync(
        HttpRequest request,
        IDicomIngestionService ingestionService,
        CancellationToken cancellationToken = default,
        [FromServices] IScratchWorkspaceProvider? scratchWorkspace = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ingestionService);

        return DicomBatchIngestionStreamCoordinator.ProcessBatchUploadAsync(
            request,
            MaxBatchSizeBytes,
            ingestionService,
            scratchWorkspace,
            cancellationToken);
    }

    public static Task<IResult> GetStudyByUidAsync(
        string studyUid,
        [FromServices] IImagingQueryService queryService,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        queryService.GetStudyByUidAsync(studyUid, httpContext, cancellationToken);

    public static Task<IResult> GetInstanceFramesAsync(
        string studyUid,
        string seriesUid,
        string instanceUid,
        string frameList,
        [FromServices] IImagingQueryService queryService,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        queryService.GetInstanceFramesAsync(studyUid, seriesUid, instanceUid, frameList, httpContext, cancellationToken);

    public static Task<IResult> GetCertificateByIdAsync(
        string certificateId,
        [FromServices] IImagingQueryService queryService,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        queryService.GetCertificateByIdAsync(certificateId, httpContext, cancellationToken);
}
