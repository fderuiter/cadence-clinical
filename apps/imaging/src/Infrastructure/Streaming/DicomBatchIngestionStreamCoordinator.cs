using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Services;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace Cadence.Apps.Imaging.Infrastructure.Streaming;

public static class DicomBatchIngestionStreamCoordinator
{
    public static IResult PayloadTooLargeResult(string? detail = null) =>
        TypedResults.Problem(
            detail: string.IsNullOrWhiteSpace(detail) ? BatchPayloadTooLargeException.DefaultMessage : detail,
            statusCode: StatusCodes.Status413PayloadTooLarge);

    public static IResult EmptyBatchPayloadResult() =>
        TypedResults.BadRequest(new { code = "EMPTY_PAYLOAD", message = "Batch payload cannot be empty." });

    public static IResult MissingUploadScopeResult() =>
        TypedResults.BadRequest(new
        {
            code = "MISSING_SCOPE",
            message = "studyId, siteId, and subjectId are required and cannot be empty."
        });

    public static async Task<IResult> ProcessBatchUploadAsync(
        HttpRequest request,
        long maxBatchSizeBytes,
        IDicomIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength.HasValue && request.ContentLength.Value > maxBatchSizeBytes)
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
                    maxBatchSizeBytes,
                    metadata,
                    ingestionService,
                    cancellationToken).ConfigureAwait(false);
            }

            using var countingBody = new MaxBytesReadStream(request.Body, maxBatchSizeBytes);

            if (DicomMultipartHeaderHelpers.TryGetMultipartBoundary(request.ContentType, out string? boundary))
            {
                return await ProcessMultipartBatchAsync(
                    request,
                    countingBody,
                    boundary!,
                    maxBatchSizeBytes,
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
        catch (Exception ex) when (DicomPayloadLimitClassifier.TryGetPayloadTooLargeDetail(ex, out string? detail))
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
        long maxBatchSizeBytes,
        BatchRequestMetadata metadata,
        IDicomIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        var budget = new SharedByteBudget(maxBatchSizeBytes);
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

        DicomMultipartHeaderHelpers.ApplyFormMetadataField(metadata, "subjectId", form["subjectId"]);
        DicomMultipartHeaderHelpers.ApplyFormMetadataField(metadata, "siteId", form["siteId"]);
        DicomMultipartHeaderHelpers.ApplyFormMetadataField(metadata, "studyId", form["studyId"]);
        DicomMultipartHeaderHelpers.ApplyFormMetadataField(metadata, "timepointName", form["timepointName"]);

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

            if (file.Length > 0 && file.Length > maxBatchSizeBytes)
            {
                return PayloadTooLargeResult();
            }
        }

        if (!metadata.HasCompleteUploadScope)
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
        var batchRequest = DicomMultipartHeaderHelpers.CreateStreamingBatchRequest(
            request.HttpContext,
            metadata,
            DicomMultipartHeaderHelpers.EnumerateFormFilesAsync(form.Files, budget, state, cancellationToken));

        var result = await ingestionService.IngestBatchAsync(batchRequest, cancellationToken).ConfigureAwait(false);

        for (int i = state.NextFileIndex; i < form.Files.Count; i++)
        {
            await using var remainingStream = form.Files[i].OpenReadStream();
            await using var guarded = new BudgetedReadStream(remainingStream, budget);
            await BatchStreamHelpers.DrainAsync(guarded, cancellationToken).ConfigureAwait(false);
        }

        return TypedResults.Created($"/api/v1/imaging/studies/{result.AnonymizedStudyUid}", result);
    }

    private static async Task<IResult> ProcessMultipartBatchAsync(
        HttpRequest request,
        MaxBytesReadStream countingBody,
        string boundary,
        long maxBatchSizeBytes,
        BatchRequestMetadata metadata,
        IDicomIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        var reader = new MultipartReader(boundary, countingBody)
        {
            HeadersLengthLimit = 65_536,
            BodyLengthLimit = maxBatchSizeBytes
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

            if (DicomMultipartHeaderHelpers.TryGetSectionFileName(contentDisposition, out string? parsedFileName))
            {
                firstFileSection = section;
                firstFileName = parsedFileName!;
                break;
            }

            if (DicomMultipartHeaderHelpers.TryGetFormFieldName(contentDisposition, out string? fieldName))
            {
                string fieldValue = await DicomMultipartHeaderHelpers.ReadBoundedFormFieldAsync(section.Body, cancellationToken).ConfigureAwait(false);
                DicomMultipartHeaderHelpers.ApplyFormMetadataField(metadata, fieldName!, fieldValue);
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

        if (metadata.HasCompleteUploadScope)
        {
            if (ClinicalScopeGuard.RequireStudyAndSite(request.HttpContext, metadata.StudyValue, metadata.SiteValue) is not null)
            {
                if (!request.ContentLength.HasValue)
                {
                    await BatchStreamHelpers.DrainAsync(firstFileSection.Body, cancellationToken).ConfigureAwait(false);
                    await DicomMultipartHeaderHelpers.DrainMultipartSectionsAsync(reader, cancellationToken).ConfigureAwait(false);
                    await BatchStreamHelpers.DrainAsync(countingBody, cancellationToken).ConfigureAwait(false);
                }

                return ClinicalScopeGuard.OutsideStudy();
            }

            var batchRequest = DicomMultipartHeaderHelpers.CreateStreamingBatchRequest(
                request.HttpContext,
                metadata,
                DicomMultipartHeaderHelpers.EnumerateMultipartFileSectionsAsync(firstFileSection, firstFileName, reader, metadata, cancellationToken));

            var result = await ingestionService.IngestBatchAsync(batchRequest, cancellationToken).ConfigureAwait(false);

            await DicomMultipartHeaderHelpers.DrainMultipartSectionsAsync(reader, cancellationToken).ConfigureAwait(false);
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
            spooledFiles.Add((firstFileName, await DicomMultipartHeaderHelpers.SpoolSectionToTempFileAsync(firstFileSection.Body, cancellationToken).ConfigureAwait(false)));

            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var contentDisposition))
                {
                    await BatchStreamHelpers.DrainAsync(section.Body, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (DicomMultipartHeaderHelpers.TryGetSectionFileName(contentDisposition, out string? fileName))
                {
                    spooledFiles.Add((fileName!, await DicomMultipartHeaderHelpers.SpoolSectionToTempFileAsync(section.Body, cancellationToken).ConfigureAwait(false)));
                }
                else if (DicomMultipartHeaderHelpers.TryGetFormFieldName(contentDisposition, out string? fieldName))
                {
                    string fieldValue = await DicomMultipartHeaderHelpers.ReadBoundedFormFieldAsync(section.Body, cancellationToken).ConfigureAwait(false);
                    DicomMultipartHeaderHelpers.ApplyFormMetadataField(metadata, fieldName!, fieldValue);
                }
                else
                {
                    await BatchStreamHelpers.DrainAsync(section.Body, cancellationToken).ConfigureAwait(false);
                }
            }

            await BatchStreamHelpers.DrainAsync(countingBody, cancellationToken).ConfigureAwait(false);

            if (!metadata.HasCompleteUploadScope)
            {
                return MissingUploadScopeResult();
            }

            if (ClinicalScopeGuard.RequireStudyAndSite(request.HttpContext, metadata.StudyValue, metadata.SiteValue) is not null)
            {
                return ClinicalScopeGuard.OutsideStudy();
            }

            var batchRequest = DicomMultipartHeaderHelpers.CreateStreamingBatchRequest(
                request.HttpContext,
                metadata,
                DicomMultipartHeaderHelpers.EnumerateSpooledFilesAsync(spooledFiles, cancellationToken));

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

        if (!metadata.HasCompleteUploadScope)
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
        var batchRequest = DicomMultipartHeaderHelpers.CreateStreamingBatchRequest(
            request.HttpContext,
            metadata,
            DicomMultipartHeaderHelpers.SingleStreamItemAsync(new BatchStreamItem(filename, prependedStream, IsZipHint: isZipContent)));

        var result = await ingestionService.IngestBatchAsync(batchRequest, cancellationToken).ConfigureAwait(false);
        await BatchStreamHelpers.DrainAsync(countingBody, cancellationToken).ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/imaging/studies/{result.AnonymizedStudyUid}", result);
    }
}
