using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Imaging.Services;

internal sealed class BatchPayloadTooLargeException : IOException
{
    public BatchPayloadTooLargeException()
        : base("Payload exceeds maximum 2 GB batch size limit.")
    {
    }
}

internal static class BatchStreamHelpers
{
    public const int PooledBufferSize = 65_536;

    public static async Task CopyPooledAsync(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(PooledBufferSize);
        try
        {
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer.AsMemory(0, PooledBufferSize), cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static async Task DrainAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] scratch = ArrayPool<byte>.Shared.Rent(PooledBufferSize);
        try
        {
            while (await stream.ReadAsync(scratch.AsMemory(0, PooledBufferSize), cancellationToken).ConfigureAwait(false) > 0)
            {
                // Discard unread trailing bytes while enforcing byte-budget limits.
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }

    public static void DeleteTempFileSafely(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup if OS handle release is still completing.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup on restricted temp directories.
        }
    }
}

internal sealed class PrefixReadStream(byte[] prefix, int prefixLength, Stream remainder) : Stream
{
    private int _offset;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        if (_offset < prefixLength)
        {
            int toCopy = Math.Min(buffer.Length, prefixLength - _offset);
            prefix.AsSpan(_offset, toCopy).CopyTo(buffer);
            _offset += toCopy;
            return toCopy;
        }

        return remainder.Read(buffer);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        if (_offset < prefixLength)
        {
            int toCopy = Math.Min(buffer.Length, prefixLength - _offset);
            prefix.AsMemory(_offset, toCopy).CopyTo(buffer);
            _offset += toCopy;
            return toCopy;
        }

        return await remainder.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
