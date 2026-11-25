using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Domain;
using Cadence.Apps.Imaging.Infrastructure.Streaming;
using Cadence.Apps.Imaging.Services;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Imaging.Endpoints;

public static class DicomWebEndpoints
{
    public const long MaxBatchSizeBytes = DicomUploadStreamProcessor.MaxBatchSizeBytes;
    public const long MaxSingleInstanceSizeBytes = DicomUploadStreamProcessor.MaxSingleInstanceSizeBytes;
    public const long MaxFrameSizeBytes = DicomIngestionService.MaxFrameSizeBytes;

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

    public static Task<IResult> StoreInstancesAsync(
        HttpRequest request,
        IDicomIngestionService ingestionService,
        CancellationToken cancellationToken) =>
        DicomSingleInstanceUploadProcessor.StoreInstancesAsync(request, ingestionService, cancellationToken);

    public static Task<IResult> StoreBatchInstancesAsync(
        HttpRequest request,
        IDicomIngestionService ingestionService,
        CancellationToken cancellationToken) =>
        DicomUploadStreamProcessor.StoreBatchInstancesAsync(request, ingestionService, cancellationToken);

    public static Task<IResult> GetStudyByUidAsync(
        string studyUid,
        DicomQueryService queryService,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        queryService.GetStudyByUidAsync(studyUid, httpContext, cancellationToken);

    public static Task<IResult> GetInstanceFramesAsync(
        string studyUid,
        string seriesUid,
        string instanceUid,
        string frameList,
        DicomQueryService queryService,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        queryService.GetInstanceFramesAsync(studyUid, seriesUid, instanceUid, frameList, httpContext, cancellationToken);

    public static Task<IResult> GetCertificateByIdAsync(
        string certificateId,
        DicomQueryService queryService,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        queryService.GetCertificateByIdAsync(certificateId, httpContext, cancellationToken);
}
