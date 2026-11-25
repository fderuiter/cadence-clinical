using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Services;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Cadence.Apps.Imaging.Infrastructure.Streaming;

internal sealed class BatchRequestMetadata
{
    public string SubjectValue { get; set; } = string.Empty;
    public string SiteValue { get; set; } = string.Empty;
    public string StudyValue { get; set; } = string.Empty;
    public string TimepointName { get; set; } = string.Empty;
}

internal sealed class FormEnumerationState
{
    public int NextFileIndex { get; set; }
}

internal sealed class SharedByteBudget(long maxBytes)
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

internal sealed class MaxBytesReadStream(Stream inner, long maxBytes) : Stream
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

internal sealed class BudgetedReadStream(Stream inner, SharedByteBudget budget) : Stream
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

internal static class DicomMultipartSectionHelpers
{
    public static bool TryGetPayloadTooLargeDetail(Exception exception, out string? detail)
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
}
