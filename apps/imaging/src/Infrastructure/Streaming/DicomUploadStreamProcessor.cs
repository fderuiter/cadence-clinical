using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Services;
using Cadence.Deid;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace Cadence.Apps.Imaging.Infrastructure.Streaming;

public static class DicomUploadStreamProcessor
{
    public const long MaxBatchSizeBytes = 2_147_483_648L; // 2 GB
    public const long MaxSingleInstanceSizeBytes = DicomSingleInstanceUploadProcessor.MaxSingleInstanceSizeBytes;

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

            if (DicomMultipartSectionHelpers.TryGetMultipartBoundary(request.ContentType, out string? boundary))
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
        catch (Exception ex) when (DicomMultipartSectionHelpers.TryGetPayloadTooLargeDetail(ex, out string? detail))
        {
            return PayloadTooLargeResult(detail);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return TypedResults.BadRequest(new { code = "DICOM_BATCH_INGESTION_FAILED", message = ex.Message });
        }
    }

    private static IResult PayloadTooLargeResult(string? detail = null) =>
        DicomSingleInstanceUploadProcessor.PayloadTooLargeResult(detail);

    private static IResult EmptyBatchPayloadResult() =>
        DicomSingleInstanceUploadProcessor.EmptyBatchPayloadResult();

    private static IResult MissingUploadScopeResult() =>
        DicomSingleInstanceUploadProcessor.MissingUploadScopeResult();

    private static bool HasCompleteUploadScope(BatchRequestMetadata metadata) =>
        !DicomSingleInstanceUploadProcessor.IsMissingUploadScope(metadata.StudyValue, metadata.SiteValue, metadata.SubjectValue);

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

        DicomMultipartSectionHelpers.ApplyFormMetadataField(metadata, "subjectId", form["subjectId"]);
        DicomMultipartSectionHelpers.ApplyFormMetadataField(metadata, "siteId", form["siteId"]);
        DicomMultipartSectionHelpers.ApplyFormMetadataField(metadata, "studyId", form["studyId"]);
        DicomMultipartSectionHelpers.ApplyFormMetadataField(metadata, "timepointName", form["timepointName"]);

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

            if (DicomMultipartSectionHelpers.TryGetSectionFileName(contentDisposition, out string? parsedFileName))
            {
                firstFileSection = section;
                firstFileName = parsedFileName!;
                break;
            }

            if (DicomMultipartSectionHelpers.TryGetFormFieldName(contentDisposition, out string? fieldName))
            {
                string fieldValue = await DicomMultipartSectionHelpers.ReadBoundedFormFieldAsync(section.Body, cancellationToken).ConfigureAwait(false);
                DicomMultipartSectionHelpers.ApplyFormMetadataField(metadata, fieldName!, fieldValue);
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
                    await DicomMultipartSectionHelpers.DrainMultipartSectionsAsync(reader, cancellationToken).ConfigureAwait(false);
                    await BatchStreamHelpers.DrainAsync(countingBody, cancellationToken).ConfigureAwait(false);
                }

                return ClinicalScopeGuard.OutsideStudy();
            }

            var batchRequest = CreateStreamingBatchRequest(
                request.HttpContext,
                metadata,
                EnumerateMultipartFileSectionsAsync(firstFileSection, firstFileName, reader, metadata, cancellationToken));

            var result = await ingestionService.IngestBatchAsync(batchRequest, cancellationToken).ConfigureAwait(false);

            await DicomMultipartSectionHelpers.DrainMultipartSectionsAsync(reader, cancellationToken).ConfigureAwait(false);
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

            if (DicomMultipartSectionHelpers.TryGetSectionFileName(contentDisposition, out string? fileName))
            {
                yield return new BatchStreamItem(fileName!, section.Body);
                await BatchStreamHelpers.DrainAsync(section.Body, cancellationToken).ConfigureAwait(false);
            }
            else if (DicomMultipartSectionHelpers.TryGetFormFieldName(contentDisposition, out string? fieldName))
            {
                string fieldValue = await DicomMultipartSectionHelpers.ReadBoundedFormFieldAsync(section.Body, cancellationToken).ConfigureAwait(false);
                DicomMultipartSectionHelpers.ApplyFormMetadataField(metadata, fieldName!, fieldValue);
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
            spooledFiles.Add((firstFileName, await DicomMultipartSectionHelpers.SpoolSectionToTempFileAsync(firstFileSection.Body, cancellationToken).ConfigureAwait(false)));

            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var contentDisposition))
                {
                    await BatchStreamHelpers.DrainAsync(section.Body, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (DicomMultipartSectionHelpers.TryGetSectionFileName(contentDisposition, out string? fileName))
                {
                    spooledFiles.Add((fileName!, await DicomMultipartSectionHelpers.SpoolSectionToTempFileAsync(section.Body, cancellationToken).ConfigureAwait(false)));
                }
                else if (DicomMultipartSectionHelpers.TryGetFormFieldName(contentDisposition, out string? fieldName))
                {
                    string fieldValue = await DicomMultipartSectionHelpers.ReadBoundedFormFieldAsync(section.Body, cancellationToken).ConfigureAwait(false);
                    DicomMultipartSectionHelpers.ApplyFormMetadataField(metadata, fieldName!, fieldValue);
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
}
