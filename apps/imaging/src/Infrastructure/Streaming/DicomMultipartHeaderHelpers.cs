using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Services;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Cadence.Apps.Imaging.Infrastructure.Streaming;

public sealed class BatchRequestMetadata
{
    public string SubjectValue { get; set; } = string.Empty;
    public string SiteValue { get; set; } = string.Empty;
    public string StudyValue { get; set; } = string.Empty;
    public string TimepointName { get; set; } = string.Empty;

    public bool HasCompleteUploadScope =>
        !IsMissingUploadScope(StudyValue, SiteValue, SubjectValue);

    public static bool IsMissingUploadScope(string? studyValue, string? siteValue, string? subjectValue) =>
        string.IsNullOrWhiteSpace(studyValue)
        || string.IsNullOrWhiteSpace(siteValue)
        || string.IsNullOrWhiteSpace(subjectValue);
}

public sealed class FormEnumerationState
{
    public int NextFileIndex { get; set; }
}

public static class DicomMultipartHeaderHelpers
{
    public static BatchIngestionRequest CreateStreamingBatchRequest(
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

    public static bool TryGetMultipartBoundary(string? contentType, out string? boundary)
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

    public static bool TryGetSectionFileName(ContentDispositionHeaderValue contentDisposition, out string? fileName)
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

    public static bool TryGetFormFieldName(ContentDispositionHeaderValue contentDisposition, out string? fieldName)
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

    public static void ApplyFormMetadataField(BatchRequestMetadata metadata, string fieldName, StringValues values)
    {
        if (values.Count == 0)
        {
            return;
        }

        ApplyFormMetadataField(metadata, fieldName, values.ToString());
    }

    public static void ApplyFormMetadataField(BatchRequestMetadata metadata, string fieldName, string value)
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

    public static async Task<string> ReadBoundedFormFieldAsync(Stream body, CancellationToken cancellationToken)
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

    public static async Task<string> SpoolSectionToTempFileAsync(Stream source, CancellationToken cancellationToken)
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

    public static async Task DrainMultipartSectionsAsync(MultipartReader reader, CancellationToken cancellationToken)
    {
        MultipartSection? remainingSection;
        while ((remainingSection = await reader.ReadNextSectionAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            await BatchStreamHelpers.DrainAsync(remainingSection.Body, cancellationToken).ConfigureAwait(false);
        }
    }

    public static async IAsyncEnumerable<BatchStreamItem> EnumerateFormFilesAsync(
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

    public static async IAsyncEnumerable<BatchStreamItem> EnumerateMultipartFileSectionsAsync(
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

    public static async IAsyncEnumerable<BatchStreamItem> EnumerateSpooledFilesAsync(
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

    public static async IAsyncEnumerable<BatchStreamItem> SingleStreamItemAsync(BatchStreamItem item)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield return item;
    }
}
