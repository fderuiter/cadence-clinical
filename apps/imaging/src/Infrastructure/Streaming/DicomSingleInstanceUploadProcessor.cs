using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Services;
using Cadence.Deid;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;

namespace Cadence.Apps.Imaging.Infrastructure.Streaming;

public static class DicomSingleInstanceUploadProcessor
{
    public const long MaxSingleInstanceSizeBytes = BatchArchiveQuotas.DefaultMaxEntryUncompressedBytes; // 128 MB

    public static async Task<IResult> StoreInstancesAsync(
        HttpRequest request,
        IDicomIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ingestionService);

        if (request.ContentLength.HasValue && request.ContentLength.Value > MaxSingleInstanceSizeBytes)
        {
            return PayloadTooLargeResult($"Payload exceeds maximum {MaxSingleInstanceSizeBytes / (1024 * 1024)} MB single-instance size limit.");
        }

        string subjectId = request.Query["subjectId"].ToString().Trim();
        string siteId = request.Query["siteId"].ToString().Trim();
        string studyId = request.Query["studyId"].ToString().Trim();

        await using var pooledPayload = new PooledBlockStream();
        try
        {
            if (request.HasFormContentType && request.Form.Files.Count > 0)
            {
                if (string.IsNullOrWhiteSpace(subjectId))
                {
                    subjectId = request.Form["subjectId"].ToString().Trim();
                }

                if (string.IsNullOrWhiteSpace(siteId))
                {
                    siteId = request.Form["siteId"].ToString().Trim();
                }

                if (string.IsNullOrWhiteSpace(studyId))
                {
                    studyId = request.Form["studyId"].ToString().Trim();
                }

                var file = request.Form.Files[0];
                if (file.Length > MaxSingleInstanceSizeBytes)
                {
                    return PayloadTooLargeResult($"Payload exceeds maximum {MaxSingleInstanceSizeBytes / (1024 * 1024)} MB single-instance size limit.");
                }

                await using var fileStream = file.OpenReadStream();
                using var countingStream = new MaxBytesReadStream(fileStream, MaxSingleInstanceSizeBytes);
                await BatchStreamHelpers.CopyPooledAsync(countingStream, pooledPayload, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                using var countingStream = new MaxBytesReadStream(request.Body, MaxSingleInstanceSizeBytes);
                await BatchStreamHelpers.CopyPooledAsync(countingStream, pooledPayload, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (DicomMultipartSectionHelpers.TryGetPayloadTooLargeDetail(ex, out string? detail))
        {
            return PayloadTooLargeResult(detail);
        }

        if (pooledPayload.Length == 0)
        {
            return TypedResults.BadRequest(new { code = "EMPTY_PAYLOAD", message = "Request payload cannot be empty." });
        }

        if (IsMissingUploadScope(studyId, siteId, subjectId))
        {
            return MissingUploadScopeResult();
        }

        if (ClinicalScopeGuard.RequireStudyAndSite(request.HttpContext, studyId, siteId) is not null)
        {
            return ClinicalScopeGuard.OutsideStudy();
        }

        pooledPayload.Position = 0;
        byte[] payloadBytes = new byte[(int)pooledPayload.Length];
        await pooledPayload.ReadExactlyAsync(payloadBytes, cancellationToken).ConfigureAwait(false);

        try
        {
            var result = await ingestionService.IngestInstanceAsync(
                payloadBytes,
                subjectId: subjectId,
                siteId: siteId,
                studyId: studyId,
                operatorIdentity: CallerScope.RequiredUserId(request.HttpContext, "An admitted imaging upload has no signed caller."),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return TypedResults.Created($"/api/v1/imaging/studies/{result.AnonymizedStudyUid}", result);
        }
        catch (Exception ex) when (DicomMultipartSectionHelpers.TryGetPayloadTooLargeDetail(ex, out string? detail))
        {
            return PayloadTooLargeResult(detail);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return TypedResults.BadRequest(new { code = "DICOM_INGESTION_FAILED", message = ex.Message });
        }
    }

    internal static IResult PayloadTooLargeResult(string? detail = null) =>
        TypedResults.Problem(
            detail: string.IsNullOrWhiteSpace(detail) ? BatchPayloadTooLargeException.DefaultMessage : detail,
            statusCode: StatusCodes.Status413PayloadTooLarge);

    internal static IResult EmptyBatchPayloadResult() =>
        TypedResults.BadRequest(new { code = "EMPTY_PAYLOAD", message = "Batch payload cannot be empty." });

    internal static IResult MissingUploadScopeResult() =>
        TypedResults.BadRequest(new
        {
            code = "MISSING_SCOPE",
            message = "studyId, siteId, and subjectId are required and cannot be empty."
        });

    internal static bool IsMissingUploadScope(string? studyValue, string? siteValue, string? subjectValue) =>
        string.IsNullOrWhiteSpace(studyValue)
        || string.IsNullOrWhiteSpace(siteValue)
        || string.IsNullOrWhiteSpace(subjectValue);
}
