using System;
using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Deid;

namespace Cadence.Apps.Imaging.Services;

public sealed class BatchPayloadTooLargeException : IOException
{
    public const string DefaultMessage = "Payload exceeds maximum 2 GB batch size limit.";

    public BatchPayloadTooLargeException()
        : base(DefaultMessage)
    {
    }

    public BatchPayloadTooLargeException(string message)
        : base(message)
    {
    }

    public BatchPayloadTooLargeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class DicomInvalidDimensionsException : InvalidOperationException
{
    public DicomInvalidDimensionsException()
        : base("DICOM frame dimensions are invalid or exceed the maximum 64 MB frame buffer limit.")
    {
    }

    public DicomInvalidDimensionsException(string message)
        : base(message)
    {
    }

    public DicomInvalidDimensionsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed record BatchArchiveQuotas(
    int MaxEntryCount = BatchArchiveQuotas.DefaultMaxEntryCount,
    long MaxEntryUncompressedBytes = BatchArchiveQuotas.DefaultMaxEntryUncompressedBytes,
    long MaxTotalUncompressedBytes = BatchArchiveQuotas.DefaultMaxTotalUncompressedBytes,
    int MaxCompressionRatio = BatchArchiveQuotas.DefaultMaxCompressionRatio,
    long EntryRatioMinUncompressedBytes = BatchArchiveQuotas.DefaultEntryRatioMinUncompressedBytes,
    long ArchiveRatioMinUncompressedBytes = BatchArchiveQuotas.DefaultArchiveRatioMinUncompressedBytes,
    long MaxCompressedArchiveBytes = BatchArchiveQuotas.DefaultMaxCompressedArchiveBytes)
{
    public const int DefaultMaxEntryCount = 4_096;
    public const long DefaultMaxEntryUncompressedBytes = 128L * 1024 * 1024; // 128 MiB
    public const long DefaultMaxTotalUncompressedBytes = 512L * 1024 * 1024; // 512 MiB
    public const int DefaultMaxCompressionRatio = 100;
    public const long DefaultEntryRatioMinUncompressedBytes = 4L * 1024 * 1024; // 4 MiB
    public const long DefaultArchiveRatioMinUncompressedBytes = 64L * 1024 * 1024; // 64 MiB
    public const long DefaultMaxCompressedArchiveBytes = 2_147_483_648L; // 2 GiB

    public static BatchArchiveQuotas Default { get; } = new();
}

internal sealed class BatchQuotaTracker(BatchArchiveQuotas quotas)
{
    private int _totalEntries;
    private long _totalUncompressedBytes;

    public BatchArchiveQuotas Quotas => quotas;

    public void ValidateDeclaredArchiveEntryCount(int entryCount)
    {
        if (entryCount > quotas.MaxEntryCount)
        {
            throw new BatchPayloadTooLargeException(
                $"ZIP archive entry count ({entryCount}) exceeds maximum limit of {quotas.MaxEntryCount}.");
        }
    }

    public void RecordEntry()
    {
        int updated = Interlocked.Increment(ref _totalEntries);
        if (updated > quotas.MaxEntryCount)
        {
            throw new BatchPayloadTooLargeException(
                $"Batch archive exceeds maximum entry count limit of {quotas.MaxEntryCount}.");
        }
    }

    public void ValidateDeclaredEntryLength(string entryName, long declaredLength)
    {
        if (declaredLength > quotas.MaxEntryUncompressedBytes)
        {
            throw new BatchPayloadTooLargeException(
                $"ZIP entry '{entryName}' declared length ({declaredLength} bytes) exceeds maximum per-entry limit of {quotas.MaxEntryUncompressedBytes} bytes.");
        }
    }

    public void ValidateCompressedArchiveLength(long compressedBytes)
    {
        if (compressedBytes > quotas.MaxCompressedArchiveBytes)
        {
            throw new BatchPayloadTooLargeException();
        }
    }

    public async Task CopySpooledArchiveBoundedAsync(
        Stream source,
        Stream destination,
        long initialBytesWritten,
        CancellationToken cancellationToken)
    {
        long totalWritten = initialBytesWritten;
        ValidateCompressedArchiveLength(totalWritten);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(BatchStreamHelpers.PooledBufferSize);
        try
        {
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer.AsMemory(0, BatchStreamHelpers.PooledBufferSize), cancellationToken).ConfigureAwait(false)) > 0)
            {
                totalWritten += bytesRead;
                ValidateCompressedArchiveLength(totalWritten);
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public Task<(PooledBlockStream Stream, string Sha256Hex, long BytesRead)> CopyAndHashSliceBoundedAsync(
        Stream source,
        string itemName,
        CancellationToken cancellationToken) =>
        CopyAndHashCoreAsync(
            source,
            itemName,
            enforceRatioLimits: false,
            effectiveEntryCompressedBytes: 0,
            compressedArchiveBytes: 0,
            archiveUncompressedBeforeEntry: 0,
            cancellationToken);

    public Task<(PooledBlockStream Stream, string Sha256Hex, long BytesRead)> CopyAndHashZipEntryBoundedAsync(
        Stream entryStream,
        string entryName,
        long effectiveEntryCompressedBytes,
        long compressedArchiveBytes,
        long archiveUncompressedBeforeEntry,
        CancellationToken cancellationToken) =>
        CopyAndHashCoreAsync(
            entryStream,
            entryName,
            enforceRatioLimits: true,
            effectiveEntryCompressedBytes: Math.Max(1L, effectiveEntryCompressedBytes),
            compressedArchiveBytes: Math.Max(1L, compressedArchiveBytes),
            archiveUncompressedBeforeEntry: archiveUncompressedBeforeEntry,
            cancellationToken);

    private async Task<(PooledBlockStream Stream, string Sha256Hex, long BytesRead)> CopyAndHashCoreAsync(
        Stream source,
        string itemName,
        bool enforceRatioLimits,
        long effectiveEntryCompressedBytes,
        long compressedArchiveBytes,
        long archiveUncompressedBeforeEntry,
        CancellationToken cancellationToken)
    {
        var pooled = new PooledBlockStream();
        byte[] scratch = ArrayPool<byte>.Shared.Rent(BatchStreamHelpers.PooledBufferSize);
        try
        {
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long entryBytesRead = 0;

            while (true)
            {
                int bytesRead = await source.ReadAsync(scratch.AsMemory(0, BatchStreamHelpers.PooledBufferSize), cancellationToken).ConfigureAwait(false);
                if (bytesRead <= 0)
                {
                    break;
                }

                entryBytesRead += bytesRead;
                if (entryBytesRead > quotas.MaxEntryUncompressedBytes)
                {
                    throw new BatchPayloadTooLargeException(
                        $"Batch item '{itemName}' exceeds maximum uncompressed size limit of {quotas.MaxEntryUncompressedBytes} bytes.");
                }

                long totalUncompressed = Interlocked.Add(ref _totalUncompressedBytes, bytesRead);
                if (totalUncompressed > quotas.MaxTotalUncompressedBytes)
                {
                    throw new BatchPayloadTooLargeException(
                        $"Batch payload exceeds maximum total decompressed size limit of {quotas.MaxTotalUncompressedBytes} bytes.");
                }

                if (enforceRatioLimits)
                {
                    EnforceExpansionRatio(
                        itemName,
                        entryBytesRead,
                        effectiveEntryCompressedBytes,
                        archiveUncompressedBeforeEntry + entryBytesRead,
                        compressedArchiveBytes);
                }

                ReadOnlySpan<byte> chunk = scratch.AsSpan(0, bytesRead);
                hasher.AppendData(chunk);
                pooled.Write(chunk);
            }

            pooled.Position = 0;
            string sha256 = Convert.ToHexStringLower(hasher.GetHashAndReset());
            return (pooled, sha256, entryBytesRead);
        }
        catch
        {
            await pooled.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }

    private void EnforceExpansionRatio(
        string entryName,
        long entryBytesRead,
        long effectiveEntryCompressedBytes,
        long archiveUncompressedBytes,
        long compressedArchiveBytes)
    {
        if (entryBytesRead > quotas.EntryRatioMinUncompressedBytes
            && (Int128)entryBytesRead > (Int128)effectiveEntryCompressedBytes * quotas.MaxCompressionRatio)
        {
            throw new BatchPayloadTooLargeException(
                $"ZIP entry '{entryName}' exceeds maximum {quotas.MaxCompressionRatio}:1 decompression ratio limit.");
        }

        if (archiveUncompressedBytes > quotas.ArchiveRatioMinUncompressedBytes
            && (Int128)archiveUncompressedBytes > (Int128)compressedArchiveBytes * quotas.MaxCompressionRatio)
        {
            throw new BatchPayloadTooLargeException(
                $"ZIP archive exceeds maximum {quotas.MaxCompressionRatio}:1 decompression ratio limit.");
        }
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
