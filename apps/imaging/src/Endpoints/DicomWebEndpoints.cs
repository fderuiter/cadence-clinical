using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
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
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

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
            .WithSummary("Ingests a DICOM instance via STOW-RS with PS3.15 de-identification and SHA-256 certificate generation.")
            .RequirePermission("imaging_study:upload").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<IngestionResult>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .DisableAntiforgery();

        group.MapPost("/studies/batch", StoreBatchInstancesAsync)
            .WithName("BatchIngestStudyFolder")
            .WithSummary("Ingests a multi-file DICOM study folder in batch with PS3.15 de-identification.")
            .RequirePermission("imaging_study:upload").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<BatchIngestionResult>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .DisableAntiforgery();

        group.MapGet("/studies/{studyUid}", GetStudyByUidAsync)
            .WithName("WadoRsGetStudy")
            .WithSummary("Retrieves WADO-RS study, series, and de-identified instance metadata by Study Instance UID.")
            .RequirePermission("imaging_study:read").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<ImagingStudyEnvelope>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/studies/{studyUid}/series/{seriesUid}/instances/{instanceUid}/frames/{frameList}", GetInstanceFramesAsync)
            .WithName("WadoRsGetInstanceFrames")
            .WithSummary("Streams WADO-RS binary pixel frame bytes for a de-identified DICOM instance.")
            .RequirePermission("imaging_study:read").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/certificates/{certificateId}", GetCertificateByIdAsync)
            .WithName("GetDeidCertificate")
            .WithSummary("Retrieves a DICOM PS3.15 de-identification audit certificate by certificate ID.")
            .RequirePermission("imaging_study:read").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<CertificateResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    private static IResult PayloadTooLargeResult(string? detail = null) =>
        TypedResults.Problem(
            detail: string.IsNullOrWhiteSpace(detail) ? BatchPayloadTooLargeException.DefaultMessage : detail,
            statusCode: StatusCodes.Status413PayloadTooLarge);

    private static IResult EmptyBatchPayloadResult() =>
        TypedResults.BadRequest(new { code = "EMPTY_PAYLOAD", message = "Batch payload cannot be empty." });

    private static IResult MissingUploadScopeResult() =>
        TypedResults.BadRequest(new
        {
            code = "MISSING_SCOPE",
            message = "studyId, siteId, and subjectId are required and cannot be empty."
        });

    private static bool IsMissingUploadScope(string? studyValue, string? siteValue, string? subjectValue) =>
        string.IsNullOrWhiteSpace(studyValue)
        || string.IsNullOrWhiteSpace(siteValue)
        || string.IsNullOrWhiteSpace(subjectValue);

    private static bool HasCompleteUploadScope(BatchRequestMetadata metadata) =>
        !IsMissingUploadScope(metadata.StudyValue, metadata.SiteValue, metadata.SubjectValue);

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
        catch (Exception ex) when (TryGetPayloadTooLargeDetail(ex, out string? detail))
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
        catch (Exception ex) when (TryGetPayloadTooLargeDetail(ex, out string? detail))
        {
            return PayloadTooLargeResult(detail);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return TypedResults.BadRequest(new { code = "DICOM_INGESTION_FAILED", message = ex.Message });
        }
    }

    public static async Task<IResult> StoreBatchInstancesAsync(
        HttpRequest request,
        IDicomIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ingestionService);

        if (request.ContentLength.HasValue && request.ContentLength.Value > MaxBatchSizeBytes)
        {
            return PayloadTooLargeResult();
        }

        if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySizeFeature)
        {
            bodySizeFeature.MaxRequestBodySize = null;
        }

        var metadata = new BatchRequestMetadata
        {
            SubjectValue = request.Query["subjectId"].ToString().Trim(),
            SiteValue = request.Query["siteId"].ToString().Trim(),
            StudyValue = request.Query["studyId"].ToString().Trim(),
            TimepointName = request.Query["timepointName"].ToString().Trim()
        };

        try
        {
            var formFeature = request.HttpContext.Features.Get<IFormFeature>();
            if (formFeature?.Form is { } prePopulatedForm)
            {
                return await ProcessPrePopulatedFormBatchAsync(
                    request,
                    prePopulatedForm,
                    metadata,
                    ingestionService,
                    cancellationToken).ConfigureAwait(false);
            }

            using var countingBody = new MaxBytesReadStream(request.Body, MaxBatchSizeBytes);

            if (TryGetMultipartBoundary(request.ContentType, out string? boundary))
            {
                return await ProcessMultipartBatchAsync(
                    request,
                    countingBody,
                    boundary!,
                    metadata,
                    ingestionService,
                    cancellationToken).ConfigureAwait(false);
            }

            if (request.HasFormContentType)
            {
                await BatchStreamHelpers.DrainAsync(countingBody, cancellationToken).ConfigureAwait(false);
                return EmptyBatchPayloadResult();
            }

            return await ProcessRawBodyBatchAsync(
                request,
                countingBody,
                metadata,
                ingestionService,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (TryGetPayloadTooLargeDetail(ex, out string? detail))
        {
            return PayloadTooLargeResult(detail);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return TypedResults.BadRequest(new { code = "DICOM_BATCH_INGESTION_FAILED", message = ex.Message });
        }
    }

    private static async Task<IResult> ProcessPrePopulatedFormBatchAsync(
        HttpRequest request,
        IFormCollection form,
        BatchRequestMetadata metadata,
        IDicomIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        var budget = new SharedByteBudget(MaxBatchSizeBytes);
        foreach (var kvp in form)
        {
            budget.AddBytes(Encoding.UTF8.GetByteCount(kvp.Key));
            foreach (string? val in kvp.Value)
            {
                if (val is not null)
                {
                    budget.AddBytes(Encoding.UTF8.GetByteCount(val));
                }
            }
        }

        ApplyFormMetadataField(metadata, "subjectId", form["subjectId"]);
        ApplyFormMetadataField(metadata, "siteId", form["siteId"]);
        ApplyFormMetadataField(metadata, "studyId", form["studyId"]);
        ApplyFormMetadataField(metadata, "timepointName", form["timepointName"]);

        if (form.Files.Count == 0)
        {
            return EmptyBatchPayloadResult();
        }

        foreach (var file in form.Files)
        {
            if (file.Headers is not null)
            {
                foreach (var header in file.Headers)
                {
                    budget.AddBytes(Encoding.UTF8.GetByteCount(header.Key));
                    foreach (string? val in header.Value)
                    {
                        if (val is not null)
                        {
                            budget.AddBytes(Encoding.UTF8.GetByteCount(val));
                        }
                    }
                }
            }

            if (file.Length > 0 && file.Length > MaxBatchSizeBytes)
            {
                return PayloadTooLargeResult();
            }
        }

        if (!HasCompleteUploadScope(metadata))
        {
            if (!request.ContentLength.HasValue)
            {
                foreach (var file in form.Files)
                {
                    await using var rawStream = file.OpenReadStream();
                    await using var guarded = new BudgetedReadStream(rawStream, budget);
                    await BatchStreamHelpers.DrainAsync(guarded, cancellationToken).ConfigureAwait(false);
                }
            }

            return MissingUploadScopeResult();
        }

        if (ClinicalScopeGuard.RequireStudyAndSite(request.HttpContext, metadata.StudyValue, metadata.SiteValue) is not null)
        {
            if (!request.ContentLength.HasValue)
            {
                foreach (var file in form.Files)
                {
                    await using var rawStream = file.OpenReadStream();
                    await using var guarded = new BudgetedReadStream(rawStream, budget);
                    await BatchStreamHelpers.DrainAsync(guarded, cancellationToken).ConfigureAwait(false);
                }
            }

            return ClinicalScopeGuard.OutsideStudy();
        }

        var state = new FormEnumerationState();
        var batchRequest = CreateStreamingBatchRequest(
            request.HttpContext,
            metadata,
            EnumerateFormFilesAsync(form.Files, budget, state, cancellationToken));

        var result = await ingestionService.IngestBatchAsync(batchRequest, cancellationToken).ConfigureAwait(false);

        for (int i = state.NextFileIndex; i < form.Files.Count; i++)
        {
            await using var remainingStream = form.Files[i].OpenReadStream();
            await using var guarded = new BudgetedReadStream(remainingStream, budget);
            await BatchStreamHelpers.DrainAsync(guarded, cancellationToken).ConfigureAwait(false);
        }

        return TypedResults.Created($"/api/v1/imaging/studies/{result.AnonymizedStudyUid}", result);
    }

    private static async IAsyncEnumerable<BatchStreamItem> EnumerateFormFilesAsync(
        IFormFileCollection files,
        SharedByteBudget budget,
        FormEnumerationState state,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (state.NextFileIndex < files.Count)
        {
            var file = files[state.NextFileIndex++];
            await using var rawStream = file.OpenReadStream();
            await using var guarded = new BudgetedReadStream(rawStream, budget);
            yield return new BatchStreamItem(file.FileName, guarded);
            await BatchStreamHelpers.DrainAsync(guarded, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<IResult> ProcessMultipartBatchAsync(
        HttpRequest request,
        MaxBytesReadStream countingBody,
        string boundary,
        BatchRequestMetadata metadata,
        IDicomIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        var reader = new MultipartReader(boundary, countingBody)
        {
            HeadersLengthLimit = 65_536,
            BodyLengthLimit = MaxBatchSizeBytes
        };

        MultipartSection? firstFileSection = null;
        string firstFileName = string.Empty;

        while (true)
        {
            MultipartSection? section;
            try
            {
                section = await reader.ReadNextSectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException && ex is not BatchPayloadTooLargeException)
            {
                if (countingBody.TotalBytesRead == 0)
                {
                    return EmptyBatchPayloadResult();
                }

                throw;
            }

            if (section is null)
            {
                break;
            }

            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var contentDisposition))
            {
                await BatchStreamHelpers.DrainAsync(section.Body, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (TryGetSectionFileName(contentDisposition, out string? parsedFileName))
            {
                firstFileSection = section;
                firstFileName = parsedFileName!;
                break;
            }

            if (TryGetFormFieldName(contentDisposition, out string? fieldName))
            {
                string fieldValue = await ReadBoundedFormFieldAsync(section.Body, cancellationToken).ConfigureAwait(false);
                ApplyFormMetadataField(metadata, fieldName!, fieldValue);
            }
            else
            {
                await BatchStreamHelpers.DrainAsync(section.Body, cancellationToken).ConfigureAwait(false);
            }
        }

        if (firstFileSection is null)
        {
            return EmptyBatchPayloadResult();
        }

        if (HasCompleteUploadScope(metadata))
        {
            if (ClinicalScopeGuard.RequireStudyAndSite(request.HttpContext, metadata.StudyValue, metadata.SiteValue) is not null)
            {
                if (!request.ContentLength.HasValue)
                {
                    await BatchStreamHelpers.DrainAsync(firstFileSection.Body, cancellationToken).ConfigureAwait(false);
                    await DrainMultipartSectionsAsync(reader, cancellationToken).ConfigureAwait(false);
                    await BatchStreamHelpers.DrainAsync(countingBody, cancellationToken).ConfigureAwait(false);
                }

                return ClinicalScopeGuard.OutsideStudy();
            }

            var batchRequest = CreateStreamingBatchRequest(
                request.HttpContext,
                metadata,
                EnumerateMultipartFileSectionsAsync(firstFileSection, firstFileName, reader, metadata, cancellationToken));

            var result = await ingestionService.IngestBatchAsync(batchRequest, cancellationToken).ConfigureAwait(false);

            await DrainMultipartSectionsAsync(reader, cancellationToken).ConfigureAwait(false);
            await BatchStreamHelpers.DrainAsync(countingBody, cancellationToken).ConfigureAwait(false);

            return TypedResults.Created($"/api/v1/imaging/studies/{result.AnonymizedStudyUid}", result);
        }

        return await ProcessDeferredMetadataMultipartBatchAsync(
            request,
            countingBody,
            reader,
            firstFileSection,
            firstFileName,
            metadata,
            ingestionService,
            cancellationToken).ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<BatchStreamItem> EnumerateMultipartFileSectionsAsync(
        MultipartSection firstFileSection,
        string firstFileName,
        MultipartReader reader,
        BatchRequestMetadata metadata,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new BatchStreamItem(firstFileName, firstFileSection.Body);
        await BatchStreamHelpers.DrainAsync(firstFileSection.Body, cancellationToken).ConfigureAwait(false);

        MultipartSection? section;
        while ((section = await reader.ReadNextSectionAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var contentDisposition))
            {
                await BatchStreamHelpers.DrainAsync(section.Body, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (TryGetSectionFileName(contentDisposition, out string? fileName))
            {
                yield return new BatchStreamItem(fileName!, section.Body);
                await BatchStreamHelpers.DrainAsync(section.Body, cancellationToken).ConfigureAwait(false);
            }
            else if (TryGetFormFieldName(contentDisposition, out string? fieldName))
            {
                string fieldValue = await ReadBoundedFormFieldAsync(section.Body, cancellationToken).ConfigureAwait(false);
                ApplyFormMetadataField(metadata, fieldName!, fieldValue);
            }
            else
            {
                await BatchStreamHelpers.DrainAsync(section.Body, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<IResult> ProcessDeferredMetadataMultipartBatchAsync(
        HttpRequest request,
        MaxBytesReadStream countingBody,
        MultipartReader reader,
        MultipartSection firstFileSection,
        string firstFileName,
        BatchRequestMetadata metadata,
        IDicomIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        var spooledFiles = new List<(string FileName, string TempPath)>();
        try
        {
            spooledFiles.Add((firstFileName, await SpoolSectionToTempFileAsync(firstFileSection.Body, cancellationToken).ConfigureAwait(false)));

            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var contentDisposition))
                {
                    await BatchStreamHelpers.DrainAsync(section.Body, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (TryGetSectionFileName(contentDisposition, out string? fileName))
                {
                    spooledFiles.Add((fileName!, await SpoolSectionToTempFileAsync(section.Body, cancellationToken).ConfigureAwait(false)));
                }
                else if (TryGetFormFieldName(contentDisposition, out string? fieldName))
                {
                    string fieldValue = await ReadBoundedFormFieldAsync(section.Body, cancellationToken).ConfigureAwait(false);
                    ApplyFormMetadataField(metadata, fieldName!, fieldValue);
                }
                else
                {
                    await BatchStreamHelpers.DrainAsync(section.Body, cancellationToken).ConfigureAwait(false);
                }
            }

            await BatchStreamHelpers.DrainAsync(countingBody, cancellationToken).ConfigureAwait(false);

            if (!HasCompleteUploadScope(metadata))
            {
                return MissingUploadScopeResult();
            }

            if (ClinicalScopeGuard.RequireStudyAndSite(request.HttpContext, metadata.StudyValue, metadata.SiteValue) is not null)
            {
                return ClinicalScopeGuard.OutsideStudy();
            }

            var batchRequest = CreateStreamingBatchRequest(
                request.HttpContext,
                metadata,
                EnumerateSpooledFilesAsync(spooledFiles, cancellationToken));

            var result = await ingestionService.IngestBatchAsync(batchRequest, cancellationToken).ConfigureAwait(false);
            return TypedResults.Created($"/api/v1/imaging/studies/{result.AnonymizedStudyUid}", result);
        }
        finally
        {
            foreach (var (_, tempPath) in spooledFiles)
            {
                BatchStreamHelpers.DeleteTempFileSafely(tempPath);
            }
        }
    }

    private static async IAsyncEnumerable<BatchStreamItem> EnumerateSpooledFilesAsync(
        List<(string FileName, string TempPath)> spooledFiles,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var (fileName, tempPath) in spooledFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var fs = new FileStream(
                tempPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None,
                bufferSize: BatchStreamHelpers.PooledBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            yield return new BatchStreamItem(fileName, fs);
        }
    }

    private static async Task<IResult> ProcessRawBodyBatchAsync(
        HttpRequest request,
        MaxBytesReadStream countingBody,
        BatchRequestMetadata metadata,
        IDicomIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        byte[] prefix = new byte[4];
        int prefixLength = await countingBody.ReadAtLeastAsync(prefix, prefix.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
        if (prefixLength == 0)
        {
            return EmptyBatchPayloadResult();
        }

        if (!HasCompleteUploadScope(metadata))
        {
            if (!request.ContentLength.HasValue)
            {
                await BatchStreamHelpers.DrainAsync(countingBody, cancellationToken).ConfigureAwait(false);
            }

            return MissingUploadScopeResult();
        }

        if (ClinicalScopeGuard.RequireStudyAndSite(request.HttpContext, metadata.StudyValue, metadata.SiteValue) is not null)
        {
            if (!request.ContentLength.HasValue)
            {
                await BatchStreamHelpers.DrainAsync(countingBody, cancellationToken).ConfigureAwait(false);
            }

            return ClinicalScopeGuard.OutsideStudy();
        }

        bool isZipContent = request.ContentType?.Contains("zip", StringComparison.OrdinalIgnoreCase) ?? false;
        string filename = isZipContent ? "batch.zip" : "batch.dcm";

        await using var prependedStream = new PrefixReadStream(prefix, prefixLength, countingBody);
        var batchRequest = CreateStreamingBatchRequest(
            request.HttpContext,
            metadata,
            SingleStreamItemAsync(new BatchStreamItem(filename, prependedStream, IsZipHint: isZipContent)));

        var result = await ingestionService.IngestBatchAsync(batchRequest, cancellationToken).ConfigureAwait(false);
        await BatchStreamHelpers.DrainAsync(countingBody, cancellationToken).ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/imaging/studies/{result.AnonymizedStudyUid}", result);
    }

    private static async IAsyncEnumerable<BatchStreamItem> SingleStreamItemAsync(BatchStreamItem item)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield return item;
    }

    private static BatchIngestionRequest CreateStreamingBatchRequest(
        HttpContext httpContext,
        BatchRequestMetadata metadata,
        IAsyncEnumerable<BatchStreamItem> streamFiles) =>
        new(
            Files: [],
            SubjectId: metadata.SubjectValue,
            SiteId: metadata.SiteValue,
            StudyId: metadata.StudyValue,
            TimepointName: metadata.TimepointName,
            OperatorIdentity: CallerScope.RequiredUserId(httpContext, "An admitted imaging upload has no signed caller."))
        {
            StreamFiles = streamFiles
        };

    private static bool TryGetMultipartBoundary(string? contentType, out string? boundary)
    {
        boundary = null;
        if (string.IsNullOrWhiteSpace(contentType)
            || !MediaTypeHeaderValue.TryParse(contentType, out var mediaType)
            || !mediaType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        StringSegment cleanBoundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary);
        if (StringSegment.IsNullOrEmpty(cleanBoundary))
        {
            return false;
        }

        boundary = cleanBoundary.Value;
        return !string.IsNullOrWhiteSpace(boundary);
    }

    private static bool TryGetSectionFileName(ContentDispositionHeaderValue contentDisposition, out string? fileName)
    {
        fileName = null;
        if (!contentDisposition.DispositionType.Equals("form-data", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        StringSegment candidate = !StringSegment.IsNullOrEmpty(contentDisposition.FileNameStar)
            ? HeaderUtilities.RemoveQuotes(contentDisposition.FileNameStar)
            : HeaderUtilities.RemoveQuotes(contentDisposition.FileName);

        if (StringSegment.IsNullOrEmpty(candidate))
        {
            return false;
        }

        fileName = candidate.Value;
        return !string.IsNullOrEmpty(fileName);
    }

    private static bool TryGetFormFieldName(ContentDispositionHeaderValue contentDisposition, out string? fieldName)
    {
        fieldName = null;
        if (!contentDisposition.DispositionType.Equals("form-data", StringComparison.OrdinalIgnoreCase)
            || !StringSegment.IsNullOrEmpty(contentDisposition.FileName)
            || !StringSegment.IsNullOrEmpty(contentDisposition.FileNameStar))
        {
            return false;
        }

        StringSegment cleanName = HeaderUtilities.RemoveQuotes(contentDisposition.Name);
        if (StringSegment.IsNullOrEmpty(cleanName))
        {
            return false;
        }

        fieldName = cleanName.Value;
        return !string.IsNullOrEmpty(fieldName);
    }

    private static void ApplyFormMetadataField(BatchRequestMetadata metadata, string fieldName, StringValues values)
    {
        if (values.Count == 0)
        {
            return;
        }

        ApplyFormMetadataField(metadata, fieldName, values.ToString());
    }

    private static void ApplyFormMetadataField(BatchRequestMetadata metadata, string fieldName, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        string trimmed = value.Trim();
        if (fieldName.Equals("subjectId", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(metadata.SubjectValue))
        {
            metadata.SubjectValue = trimmed;
        }
        else if (fieldName.Equals("siteId", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(metadata.SiteValue))
        {
            metadata.SiteValue = trimmed;
        }
        else if (fieldName.Equals("studyId", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(metadata.StudyValue))
        {
            metadata.StudyValue = trimmed;
        }
        else if (fieldName.Equals("timepointName", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(metadata.TimepointName))
        {
            metadata.TimepointName = trimmed;
        }
    }

    private static async Task<string> ReadBoundedFormFieldAsync(Stream body, CancellationToken cancellationToken)
    {
        const int maxFieldChars = 8_192;
        using var reader = new StreamReader(body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1_024, leaveOpen: true);
        char[] buffer = ArrayPool<char>.Shared.Rent(1_024);
        try
        {
            var sb = new StringBuilder();
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(0, 1_024), cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (sb.Length < maxFieldChars)
                {
                    int toAppend = Math.Min(read, maxFieldChars - sb.Length);
                    sb.Append(buffer, 0, toAppend);
                }
            }

            return sb.ToString();
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private static async Task<string> SpoolSectionToTempFileAsync(Stream source, CancellationToken cancellationToken)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"cadence-multipart-{Guid.NewGuid():N}.tmp");
        try
        {
            await using var fs = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: BatchStreamHelpers.PooledBufferSize,
                FileOptions.Asynchronous);

            await BatchStreamHelpers.CopyPooledAsync(source, fs, cancellationToken).ConfigureAwait(false);
            return tempPath;
        }
        catch
        {
            BatchStreamHelpers.DeleteTempFileSafely(tempPath);
            throw;
        }
    }

    private static async Task DrainMultipartSectionsAsync(MultipartReader reader, CancellationToken cancellationToken)
    {
        MultipartSection? remainingSection;
        while ((remainingSection = await reader.ReadNextSectionAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            await BatchStreamHelpers.DrainAsync(remainingSection.Body, cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task<IResult> GetStudyByUidAsync(
        string studyUid,
        ImagingDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var study = await dbContext.ImagingStudyEnvelopes
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.StudyInstanceUid == studyUid || s.Id == studyUid, cancellationToken)
            .ConfigureAwait(false);

        if (study == null
            || ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, study.StudyId, study.SiteId, allowUnassignedSiteForScopedCaller: true) is not null)
        {
            return TypedResults.NotFound(new { code = "STUDY_NOT_FOUND", message = $"Study '{studyUid}' not found." });
        }

        return TypedResults.Ok(study);
    }

    public static async Task<IResult> GetInstanceFramesAsync(
        string studyUid,
        string seriesUid,
        string instanceUid,
        string frameList,
        ImagingDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        _ = frameList;

        if (string.IsNullOrWhiteSpace(studyUid)
            || string.IsNullOrWhiteSpace(seriesUid)
            || string.IsNullOrWhiteSpace(instanceUid))
        {
            return TypedResults.NotFound(new { code = "INSTANCE_NOT_FOUND", message = $"Instance '{instanceUid}' not found." });
        }

        var instance = await dbContext.ImagingInstanceEnvelopes
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.SopInstanceUid == instanceUid || i.Id == instanceUid, cancellationToken)
            .ConfigureAwait(false);

        var parentEnvelope = instance == null
            ? null
            : await (from series in dbContext.ImagingSeriesEnvelopes.AsNoTracking()
                     join study in dbContext.ImagingStudyEnvelopes.AsNoTracking() on series.StudyEnvelopeId equals study.Id
                     where series.Id == instance.SeriesEnvelopeId
                     select new
                     {
                         study.Id,
                         study.StudyInstanceUid,
                         study.StudyId,
                         study.SiteId,
                         SeriesEnvelopeId = series.Id,
                         series.SeriesInstanceUid
                     })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

        if (instance == null
            || parentEnvelope == null
            || (parentEnvelope.StudyInstanceUid != studyUid && parentEnvelope.Id != studyUid)
            || (parentEnvelope.SeriesInstanceUid != seriesUid && parentEnvelope.SeriesEnvelopeId != seriesUid)
            || ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, parentEnvelope.StudyId, parentEnvelope.SiteId, allowUnassignedSiteForScopedCaller: true) is not null)
        {
            return TypedResults.NotFound(new { code = "INSTANCE_NOT_FOUND", message = $"Instance '{instanceUid}' not found." });
        }

        long frameByteLength = instance.Rows > 0 && instance.Columns > 0
            ? (long)instance.Rows * instance.Columns * 2L
            : 0L;

        if (instance.Rows <= 0 || instance.Columns <= 0 || frameByteLength <= 0 || frameByteLength > MaxFrameSizeBytes)
        {
            return TypedResults.Problem(
                title: "Invalid DICOM frame dimensions",
                detail: $"Instance '{instanceUid}' frame dimensions ({instance.Rows}x{instance.Columns}) must be positive and require at most {MaxFrameSizeBytes / (1024 * 1024)} MB.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        byte[] frameBytes = new byte[(int)frameByteLength];
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
            ScrubbedTags = JsonSerializer.Deserialize<List<string>>(cert.ScrubbedTagsJson) ?? [],
            PixelMaskApplied = cert.PixelMaskApplied,
            PixelMasks = JsonSerializer.Deserialize<List<PixelMaskBoundingBox>>(cert.PixelMasksJson) ?? [],
            OperatorIdentity = cert.OperatorIdentity,
            Timestamp = cert.Timestamp,
            Signature = cert.Signature
        };

        bool isValid = deidentifier.VerifyCertificate(domainCert);

        return TypedResults.Ok(new CertificateResponse(
            Certificate: cert,
            IsSignatureValid: isValid));
    }

    private sealed class BatchRequestMetadata
    {
        public string SubjectValue { get; set; } = string.Empty;
        public string SiteValue { get; set; } = string.Empty;
        public string StudyValue { get; set; } = string.Empty;
        public string TimepointName { get; set; } = string.Empty;
    }

    private sealed class FormEnumerationState
    {
        public int NextFileIndex { get; set; }
    }

    private static bool TryGetPayloadTooLargeDetail(Exception exception, out string? detail)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is BatchPayloadTooLargeException quotaException)
            {
                detail = quotaException.Message;
                return true;
            }

            if (current is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } badRequest)
            {
                detail = badRequest.Message;
                return true;
            }

            if (current is InvalidDataException && current.Message.Contains("Multipart body length limit", StringComparison.OrdinalIgnoreCase))
            {
                detail = BatchPayloadTooLargeException.DefaultMessage;
                return true;
            }
        }

        detail = null;
        return false;
    }

    private sealed class SharedByteBudget(long maxBytes)
    {
        private long _totalBytes;

        public void AddBytes(long count)
        {
            if (count <= 0)
            {
                return;
            }

            long updated = Interlocked.Add(ref _totalBytes, count);
            if (updated > maxBytes)
            {
                throw new BatchPayloadTooLargeException();
            }
        }
    }

    private sealed class MaxBytesReadStream(Stream inner, long maxBytes) : Stream
    {
        private long _totalBytesRead;

        public long TotalBytesRead => _totalBytesRead;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            RecordBytes(inner.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) =>
            RecordBytes(inner.Read(buffer));

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            RecordBytes(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            RecordBytes(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int RecordBytes(int read)
        {
            if (read > 0)
            {
                _totalBytesRead += read;
                if (_totalBytesRead > maxBytes)
                {
                    throw new BatchPayloadTooLargeException();
                }
            }

            return read;
        }
    }

    private sealed class BudgetedReadStream(Stream inner, SharedByteBudget budget) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = inner.Read(buffer, offset, count);
            budget.AddBytes(read);
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            int read = inner.Read(buffer);
            budget.AddBytes(read);
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int read = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            budget.AddBytes(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            budget.AddBytes(read);
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

public record CertificateResponse(
    DeidentificationCertificate Certificate,
    bool IsSignatureValid);
