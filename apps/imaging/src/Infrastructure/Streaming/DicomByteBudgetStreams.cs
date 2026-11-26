using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Services;

using Microsoft.AspNetCore.Http;

namespace Cadence.Apps.Imaging.Infrastructure.Streaming;

public static class DicomPayloadLimitClassifier
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
}

public sealed class SharedByteBudget(long maxBytes)
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

public sealed class MaxBytesReadStream(Stream inner, long maxBytes) : Stream
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

public sealed class BudgetedReadStream(Stream inner, SharedByteBudget budget) : Stream
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
