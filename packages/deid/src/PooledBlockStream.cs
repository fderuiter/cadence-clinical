using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Deid;

/// <summary>
/// A seekable <see cref="Stream"/> backed by fixed-size blocks rented from <see cref="ArrayPool{T}.Shared"/>
/// below the .NET Large Object Heap threshold (<c>65,536</c> bytes &lt; <c>85,000</c> bytes).
/// Disposing the stream returns all rented blocks to the shared pool immediately.
/// </summary>
public sealed class PooledBlockStream : Stream
{
    public const int DefaultBlockSize = 65_536;

    private readonly int _blockSize;
    private readonly List<byte[]> _blocks = [];
    private long _length;
    private long _position;
    private bool _disposed;

    public PooledBlockStream(int blockSize = DefaultBlockSize)
    {
        if (blockSize <= 0 || blockSize >= 85_000)
        {
            throw new ArgumentOutOfRangeException(nameof(blockSize), "Block size must be positive and below the 85,000-byte LOH threshold.");
        }

        _blockSize = blockSize;
    }

    public override bool CanRead => !_disposed;

    public override bool CanSeek => !_disposed;

    public override bool CanWrite => !_disposed;

    public override long Length
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _length;
        }
    }

    public override long Position
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _position;
        }
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _position = value;
        }
    }

    /// <summary>
    /// Copies <paramref name="source"/> into a new <see cref="PooledBlockStream"/> while computing its lower-case SHA-256 hex digest in one pass.
    /// </summary>
    public static (PooledBlockStream Stream, string Sha256Hex) CopyAndHash(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var pooled = new PooledBlockStream();
        try
        {
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            while (true)
            {
                Span<byte> dest = pooled.GetWriteSpanForAppend();
                int bytesRead = source.Read(dest);
                if (bytesRead <= 0)
                {
                    break;
                }

                hasher.AppendData(dest[..bytesRead]);
                pooled.AdvanceAppend(bytesRead);
            }

            pooled._position = 0;
            string sha256 = Convert.ToHexStringLower(hasher.GetHashAndReset());
            return (pooled, sha256);
        }
        catch
        {
            pooled.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Asynchronously copies <paramref name="source"/> into a new <see cref="PooledBlockStream"/> while computing its lower-case SHA-256 hex digest in one pass.
    /// </summary>
    public static async Task<(PooledBlockStream Stream, string Sha256Hex)> CopyAndHashAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var pooled = new PooledBlockStream();
        try
        {
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            while (true)
            {
                Memory<byte> dest = pooled.GetWriteMemoryForAppend();
                int bytesRead = await source.ReadAsync(dest, cancellationToken).ConfigureAwait(false);
                if (bytesRead <= 0)
                {
                    break;
                }

                hasher.AppendData(dest.Span[..bytesRead]);
                pooled.AdvanceAppend(bytesRead);
            }

            pooled._position = 0;
            string sha256 = Convert.ToHexStringLower(hasher.GetHashAndReset());
            return (pooled, sha256);
        }
        catch
        {
            await pooled.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Zeroes <paramref name="count"/> bytes starting at <paramref name="offset"/> in place across pooled blocks,
    /// clamped to <see cref="Length"/>.
    /// </summary>
    public void ZeroRange(long offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (count <= 0 || offset >= _length)
        {
            return;
        }

        if (offset < 0)
        {
            count = (int)Math.Max(0, count + offset);
            offset = 0;
        }

        long remaining = Math.Min(count, _length - offset);
        long cursor = offset;

        while (remaining > 0)
        {
            int blockIndex = (int)(cursor / _blockSize);
            int blockOffset = (int)(cursor % _blockSize);
            int toClear = (int)Math.Min(remaining, _blockSize - blockOffset);
            _blocks[blockIndex].AsSpan(blockOffset, toClear).Clear();
            cursor += toClear;
            remaining -= toClear;
        }
    }

    public override void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return cancellationToken.IsCancellationRequested
            ? Task.FromCanceled(cancellationToken)
            : Task.CompletedTask;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty || _position >= _length)
        {
            return 0;
        }

        int totalToRead = (int)Math.Min(buffer.Length, _length - _position);
        int totalRead = 0;

        while (totalRead < totalToRead)
        {
            int blockIndex = (int)(_position / _blockSize);
            int blockOffset = (int)(_position % _blockSize);
            int chunk = Math.Min(totalToRead - totalRead, _blockSize - blockOffset);
            _blocks[blockIndex].AsSpan(blockOffset, chunk).CopyTo(buffer.Slice(totalRead, chunk));
            _position += chunk;
            totalRead += chunk;
        }

        return totalRead;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<int>(cancellationToken);
        }

        return Task.FromResult(Read(buffer.AsSpan(offset, count)));
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<int>(cancellationToken);
        }

        return new ValueTask<int>(Read(buffer.Span));
    }

    public override int ReadByte()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_position >= _length)
        {
            return -1;
        }

        int blockIndex = (int)(_position / _blockSize);
        int blockOffset = (int)(_position % _blockSize);
        _position++;
        return _blocks[blockIndex][blockOffset];
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        long target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (target < 0)
        {
            throw new IOException("Attempted to seek before the beginning of the stream.");
        }

        _position = target;
        return _position;
    }

    public override void SetLength(long value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(value);

        EnsureCapacity(value);
        if (value > _length)
        {
            long oldLength = _length;
            _length = value;
            long toZero = value - oldLength;
            long cursor = oldLength;
            while (toZero > 0)
            {
                int chunk = (int)Math.Min(toZero, int.MaxValue);
                ZeroRange(cursor, chunk);
                cursor += chunk;
                toZero -= chunk;
            }
        }
        else
        {
            _length = value;
            if (_position > _length)
            {
                _position = _length;
            }
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty)
        {
            return;
        }

        long endPosition = _position + buffer.Length;
        EnsureCapacity(endPosition);

        if (_position > _length)
        {
            long gap = _position - _length;
            long oldPosition = _position;
            _length = _position;
            long cursor = oldPosition - gap;
            while (gap > 0)
            {
                int chunk = (int)Math.Min(gap, int.MaxValue);
                ZeroRange(cursor, chunk);
                cursor += chunk;
                gap -= chunk;
            }
        }

        int written = 0;
        while (written < buffer.Length)
        {
            int blockIndex = (int)(_position / _blockSize);
            int blockOffset = (int)(_position % _blockSize);
            int chunk = Math.Min(buffer.Length - written, _blockSize - blockOffset);
            buffer.Slice(written, chunk).CopyTo(_blocks[blockIndex].AsSpan(blockOffset, chunk));
            _position += chunk;
            written += chunk;
        }

        if (_position > _length)
        {
            _length = _position;
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        Write(buffer.AsSpan(offset, count));
        return Task.CompletedTask;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    public override void WriteByte(byte value)
    {
        Span<byte> one = [value];
        Write(one);
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                foreach (byte[] block in _blocks)
                {
                    ArrayPool<byte>.Shared.Return(block);
                }

                _blocks.Clear();
            }

            _disposed = true;
        }

        base.Dispose(disposing);
    }

    private Span<byte> GetWriteSpanForAppend()
    {
        EnsureCapacity(_length + 1);
        int blockIndex = (int)(_length / _blockSize);
        int blockOffset = (int)(_length % _blockSize);
        return _blocks[blockIndex].AsSpan(blockOffset, _blockSize - blockOffset);
    }

    private Memory<byte> GetWriteMemoryForAppend()
    {
        EnsureCapacity(_length + 1);
        int blockIndex = (int)(_length / _blockSize);
        int blockOffset = (int)(_length % _blockSize);
        return _blocks[blockIndex].AsMemory(blockOffset, _blockSize - blockOffset);
    }

    private void AdvanceAppend(int count)
    {
        _length += count;
        _position = _length;
    }

    private void EnsureCapacity(long requiredBytes)
    {
        long currentCapacity = (long)_blocks.Count * _blockSize;
        while (currentCapacity < requiredBytes)
        {
            _blocks.Add(ArrayPool<byte>.Shared.Rent(_blockSize));
            currentCapacity += _blockSize;
        }
    }
}

/// <summary>
/// Write-only sink <see cref="Stream"/> that computes a SHA-256 digest and tracks total written byte length
/// without retaining any output bytes in memory.
/// </summary>
internal sealed class HashingCountingWriteStream : Stream
{
    private readonly IncrementalHash _hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private long _bytesWritten;
    private bool _disposed;

    public long BytesWritten => _bytesWritten;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => !_disposed;

    public override long Length => _bytesWritten;

    public override long Position
    {
        get => _bytesWritten;
        set => throw new NotSupportedException();
    }

    public string CompleteHashHex()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Convert.ToHexStringLower(_hasher.GetHashAndReset());
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty)
        {
            return;
        }

        _hasher.AppendData(buffer);
        _bytesWritten += buffer.Length;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        Write(buffer.AsSpan(offset, count));
        return Task.CompletedTask;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _hasher.Dispose();
            }

            _disposed = true;
        }

        base.Dispose(disposing);
    }
}
