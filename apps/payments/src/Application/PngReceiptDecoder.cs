using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Cadence.Apps.Payments.Application;

/// <summary>
/// Bounded base64/<c>data:</c> receipt payload decoder and PNG chunk/ZLib decompression decoder.
/// Enforces pre-allocation byte caps, <c>IHDR</c>-first ordering, chunk count bounds, per-chunk and
/// cumulative text metadata quotas, row-streaming <c>IDAT</c> scanline inflation, and compression-ratio caps.
/// </summary>
internal static class PngReceiptDecoder
{
    private const int PngDecompressBufferSize = 4_096;

    internal static bool TryDecodeReceiptPayload(string? payload, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > ReceiptOcrEngine.MaxEncodedReceiptPayloadChars)
        {
            return false;
        }

        ReadOnlySpan<char> span = payload.AsSpan().Trim();
        if (span.IsEmpty)
        {
            return false;
        }

        if (span.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            int commaIdx = span.IndexOf(',');
            if (commaIdx < 0)
            {
                return false;
            }

            ReadOnlySpan<char> header = span[5..commaIdx];
            ReadOnlySpan<char> dataPart = span[(commaIdx + 1)..];
            ParseDataUriHeader(header, out ReadOnlySpan<char> mediaType, out bool isBase64);

            if (!isBase64)
            {
                if (!mediaType.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase)
                    || dataPart.IsEmpty
                    || dataPart.Length > ReceiptOcrEngine.MaxEncodedReceiptPayloadChars)
                {
                    return false;
                }

                string unescaped;
                try
                {
                    unescaped = Uri.UnescapeDataString(dataPart);
                }
                catch (UriFormatException)
                {
                    return false;
                }

                if (unescaped.Length == 0 || Encoding.UTF8.GetByteCount(unescaped) > ReceiptOcrEngine.MaxReceiptPayloadBytes)
                {
                    return false;
                }

                bytes = Encoding.UTF8.GetBytes(unescaped);
                return bytes.Length > 0 && bytes.Length <= ReceiptOcrEngine.MaxReceiptPayloadBytes;
            }

            span = dataPart;
        }

        int nonWhitespaceCount = 0;
        char last1 = '\0';
        char last2 = '\0';
        foreach (char ch in span)
        {
            if (ch is ' ' or '\t' or '\r' or '\n')
            {
                continue;
            }

            nonWhitespaceCount++;
            if (nonWhitespaceCount > ReceiptOcrEngine.MaxBase64NonWhitespaceChars)
            {
                return false;
            }

            last2 = last1;
            last1 = ch;
        }

        if (nonWhitespaceCount == 0 || (nonWhitespaceCount & 3) != 0)
        {
            return false;
        }

        int padding = 0;
        if (last1 == '=')
        {
            padding++;
            if (last2 == '=')
            {
                padding++;
            }
        }

        long expectedDecodedBytes = ((long)(nonWhitespaceCount / 4) * 3) - padding;
        if (expectedDecodedBytes <= 0 || expectedDecodedBytes > ReceiptOcrEngine.MaxReceiptPayloadBytes)
        {
            return false;
        }

        byte[] decoded = new byte[(int)expectedDecodedBytes];
        if (!Convert.TryFromBase64Chars(span, decoded, out int bytesWritten)
            || bytesWritten <= 0
            || bytesWritten > ReceiptOcrEngine.MaxReceiptPayloadBytes)
        {
            return false;
        }

        bytes = bytesWritten == decoded.Length ? decoded : decoded[..bytesWritten];
        return true;
    }

    internal static bool TryDecodePngGrayscale(
        byte[] bytes,
        List<string> textChunks,
        out byte[] gray,
        out int width,
        out int height,
        out bool quotaOrStructureFailed)
    {
        gray = [];
        width = 0;
        height = 0;
        quotaOrStructureFailed = false;

        if (bytes.Length < 37 || bytes.Length > ReceiptOcrEngine.MaxReceiptPayloadBytes)
        {
            quotaOrStructureFailed = true;
            return false;
        }

        int bitDepth = 0;
        int colorType = 0;
        int interlace = 0;
        int channels = 0;
        bool seenIhdr = false;
        byte[]? palette = null;

        int firstIdatOffset = -1;
        int firstIdatLength = 0;
        MemoryStream? multiIdatStream = null;
        long totalIdatCompressedBytes = 0;

        var quotaTracker = new PngQuotaTracker();
        try
        {
            int pos = 8;
            int chunkIndex = 0;
            while (pos < bytes.Length)
            {
                if (bytes.Length - pos < 12)
                {
                    quotaOrStructureFailed = true;
                    break;
                }

                uint chunkLen = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(pos, 4));
                ReadOnlySpan<byte> chunkType = bytes.AsSpan(pos + 4, 4);
                pos += 8;

                if (chunkLen > (uint)(bytes.Length - pos - 4) || !quotaTracker.TryRecordChunk())
                {
                    quotaOrStructureFailed = true;
                    break;
                }

                var chunkSegment = new ArraySegment<byte>(bytes, pos, (int)chunkLen);
                ReadOnlySpan<byte> data = chunkSegment;

                if (chunkIndex == 0)
                {
                    if (!chunkType.SequenceEqual("IHDR"u8)
                        || !TryParseValidPngIhdr(data, out width, out height, out bitDepth, out colorType, out interlace, out channels))
                    {
                        quotaOrStructureFailed = true;
                        break;
                    }

                    seenIhdr = true;
                }
                else if (chunkType.SequenceEqual("IHDR"u8))
                {
                    quotaOrStructureFailed = true;
                    break;
                }
                else if (chunkType.SequenceEqual("PLTE"u8))
                {
                    if (data.Length > 768 || (data.Length % 3) != 0)
                    {
                        quotaOrStructureFailed = true;
                        break;
                    }

                    palette = data.ToArray();
                }
                else if (chunkType.SequenceEqual("IDAT"u8))
                {
                    totalIdatCompressedBytes += data.Length;
                    if (bitDepth == 8 && interlace == 0 && channels > 0 && data.Length > 0)
                    {
                        if (firstIdatOffset < 0)
                        {
                            firstIdatOffset = pos;
                            firstIdatLength = (int)chunkLen;
                        }
                        else
                        {
                            if (multiIdatStream is null)
                            {
                                multiIdatStream = new MemoryStream();
                                multiIdatStream.Write(bytes, firstIdatOffset, firstIdatLength);
                            }

                            multiIdatStream.Write(data);
                        }
                    }
                }
                else if (chunkType.SequenceEqual("tEXt"u8))
                {
                    if (!TryExtractPngLatin1Chunk(data, ref quotaTracker, textChunks))
                    {
                        quotaOrStructureFailed = true;
                        break;
                    }
                }
                else if (chunkType.SequenceEqual("zTXt"u8))
                {
                    if (!TryExtractPngCompressedChunk(chunkSegment, ref quotaTracker, textChunks))
                    {
                        quotaOrStructureFailed = true;
                        break;
                    }
                }
                else if (chunkType.SequenceEqual("iTXt"u8))
                {
                    if (!TryExtractPngInternationalChunk(chunkSegment, ref quotaTracker, textChunks))
                    {
                        quotaOrStructureFailed = true;
                        break;
                    }
                }
                else if (chunkType.SequenceEqual("IEND"u8))
                {
                    if (chunkLen != 0)
                    {
                        quotaOrStructureFailed = true;
                    }

                    break;
                }

                pos += (int)chunkLen + 4; // skip CRC
                chunkIndex++;
            }

            if (!seenIhdr || quotaOrStructureFailed)
            {
                quotaOrStructureFailed = true;
                textChunks.Clear();
                return false;
            }

            if (bitDepth != 8 || interlace != 0 || channels == 0 || totalIdatCompressedBytes == 0)
            {
                return false;
            }

            Stream idatSource;
            bool disposeIdatSource;
            if (multiIdatStream is not null)
            {
                multiIdatStream.Position = 0;
                idatSource = multiIdatStream;
                disposeIdatSource = false;
            }
            else if (firstIdatOffset >= 0 && firstIdatLength > 0)
            {
                idatSource = new MemoryStream(bytes, firstIdatOffset, firstIdatLength, writable: false);
                disposeIdatSource = true;
            }
            else
            {
                return false;
            }

            try
            {
                if (!TryInflateAndConvertPngScanlines(
                        idatSource,
                        totalIdatCompressedBytes,
                        width,
                        height,
                        colorType,
                        channels,
                        palette,
                        out gray))
                {
                    quotaOrStructureFailed = true;
                    textChunks.Clear();
                    return false;
                }

                return true;
            }
            finally
            {
                if (disposeIdatSource)
                {
                    idatSource.Dispose();
                }
            }
        }
        finally
        {
            multiIdatStream?.Dispose();
        }
    }

    private static void ParseDataUriHeader(
        ReadOnlySpan<char> header,
        out ReadOnlySpan<char> mediaType,
        out bool isBase64)
    {
        isBase64 = false;
        int firstSemicolon = header.IndexOf(';');
        if (firstSemicolon < 0)
        {
            mediaType = header.Trim();
            return;
        }

        mediaType = header[..firstSemicolon].Trim();
        ReadOnlySpan<char> rest = header[(firstSemicolon + 1)..];
        while (!rest.IsEmpty)
        {
            int semi = rest.IndexOf(';');
            ReadOnlySpan<char> segment = semi < 0 ? rest : rest[..semi];
            if (segment.Trim().Equals("base64", StringComparison.OrdinalIgnoreCase))
            {
                isBase64 = true;
                return;
            }

            if (semi < 0)
            {
                break;
            }

            rest = rest[(semi + 1)..];
        }
    }

    private static bool TryParseValidPngIhdr(
        ReadOnlySpan<byte> data,
        out int width,
        out int height,
        out int bitDepth,
        out int colorType,
        out int interlace,
        out int channels)
    {
        width = 0;
        height = 0;
        bitDepth = 0;
        colorType = 0;
        interlace = 0;
        channels = 0;

        if (data.Length != 13)
        {
            return false;
        }

        uint rawWidth = BinaryPrimitives.ReadUInt32BigEndian(data[..4]);
        uint rawHeight = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4));
        byte rawBitDepth = data[8];
        byte rawColorType = data[9];
        byte compressionMethod = data[10];
        byte filterMethod = data[11];
        byte rawInterlace = data[12];

        if (rawWidth == 0
            || rawWidth > ReceiptOcrEngine.MaxPngDimension
            || rawHeight == 0
            || rawHeight > ReceiptOcrEngine.MaxPngDimension
            || compressionMethod != 0
            || filterMethod != 0
            || rawInterlace > 1)
        {
            return false;
        }

        bool validColorDepth = rawColorType switch
        {
            0 => rawBitDepth is 1 or 2 or 4 or 8 or 16,
            2 => rawBitDepth is 8 or 16,
            3 => rawBitDepth is 1 or 2 or 4 or 8,
            4 => rawBitDepth is 8 or 16,
            6 => rawBitDepth is 8 or 16,
            _ => false
        };

        if (!validColorDepth)
        {
            return false;
        }

        channels = rawColorType switch
        {
            0 => 1, // Grayscale
            2 => 3, // RGB
            3 => 1, // Indexed palette
            4 => 2, // Grayscale + Alpha
            6 => 4, // RGBA
            _ => 0
        };

        width = (int)rawWidth;
        height = (int)rawHeight;
        bitDepth = rawBitDepth;
        colorType = rawColorType;
        interlace = rawInterlace;
        return true;
    }

    private static bool TryInflateAndConvertPngScanlines(
        Stream idatSource,
        long compressedIdatBytes,
        int width,
        int height,
        int colorType,
        int channels,
        byte[]? palette,
        out byte[] gray)
    {
        gray = [];
        int stride = width * channels;
        long expectedRawBytes = (long)(stride + 1) * height;
        long effectiveCompressed = Math.Max(1L, compressedIdatBytes);

        if (expectedRawBytes <= 0
            || (expectedRawBytes > ReceiptOcrEngine.PngIdatRatioFloorBytes
                && (Int128)expectedRawBytes > (Int128)effectiveCompressed * ReceiptOcrEngine.MaxPngCompressionRatio))
        {
            return false;
        }

        byte[] currScanline = ArrayPool<byte>.Shared.Rent(stride + 1);
        byte[] prevRow = ArrayPool<byte>.Shared.Rent(stride);
        byte[] currRow = ArrayPool<byte>.Shared.Rent(stride);
        prevRow.AsSpan(0, stride).Clear();

        try
        {
            using var zlib = new ZLibStream(idatSource, CompressionMode.Decompress, leaveOpen: true);
            byte[]? grayOut = null;
            int bpp = channels;

            for (int y = 0; y < height; y++)
            {
                int read = zlib.ReadAtLeast(currScanline.AsSpan(0, stride + 1), stride + 1, throwOnEndOfStream: false);
                if (read < stride + 1)
                {
                    return false;
                }

                byte filterType = currScanline[0];
                if (filterType > 4)
                {
                    return false;
                }

                grayOut ??= new byte[width * height];

                for (int x = 0; x < stride; x++)
                {
                    byte raw = currScanline[1 + x];
                    byte left = x >= bpp ? currRow[x - bpp] : (byte)0;
                    byte up = y > 0 ? prevRow[x] : (byte)0;
                    byte upLeft = (y > 0 && x >= bpp) ? prevRow[x - bpp] : (byte)0;

                    currRow[x] = filterType switch
                    {
                        0 => raw,
                        1 => (byte)(raw + left),
                        2 => (byte)(raw + up),
                        3 => (byte)(raw + ((left + up) >> 1)),
                        4 => (byte)(raw + PaethPredictor(left, up, upLeft)),
                        _ => raw
                    };
                }

                int grayRowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    int px = x * channels;
                    grayOut[grayRowOffset + x] = colorType switch
                    {
                        0 => currRow[px],
                        2 => ReceiptOcrEngine.RgbToLum(currRow[px], currRow[px + 1], currRow[px + 2]),
                        3 => PaletteToLum(palette, currRow[px]),
                        4 => BlendAlphaOnWhite(currRow[px], currRow[px + 1]),
                        6 => BlendAlphaOnWhite(
                            ReceiptOcrEngine.RgbToLum(currRow[px], currRow[px + 1], currRow[px + 2]),
                            currRow[px + 3]),
                        _ => (byte)255
                    };
                }

                (prevRow, currRow) = (currRow, prevRow);
            }

            Span<byte> trailingProbe = stackalloc byte[1];
            if (zlib.Read(trailingProbe) > 0 || grayOut is null)
            {
                return false;
            }

            gray = grayOut;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(currRow);
            ArrayPool<byte>.Shared.Return(prevRow);
            ArrayPool<byte>.Shared.Return(currScanline);
        }
    }

    private static int PaethPredictor(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
        {
            return a;
        }

        return pb <= pc ? b : c;
    }

    private static byte BlendAlphaOnWhite(byte lum, byte alpha) =>
        (byte)Math.Clamp(((lum * alpha) + (255 * (255 - alpha))) / 255, 0, 255);

    private static byte PaletteToLum(byte[]? palette, byte index)
    {
        int offset = index * 3;
        if (palette == null || offset + 2 >= palette.Length)
        {
            return index;
        }

        return ReceiptOcrEngine.RgbToLum(palette[offset], palette[offset + 1], palette[offset + 2]);
    }

    private static bool TryExtractPngLatin1Chunk(
        ReadOnlySpan<byte> data,
        ref PngQuotaTracker quotaTracker,
        List<string> chunks)
    {
        int nul = data.IndexOf((byte)0);
        if (nul <= 0 || nul > 79 || nul + 1 >= data.Length)
        {
            return true;
        }

        string keyword = Encoding.Latin1.GetString(data[..nul]);
        if (IsIgnoredMetadataKeyword(keyword))
        {
            return true;
        }

        ReadOnlySpan<byte> rawTextBytes = data[(nul + 1)..];
        if (!quotaTracker.TryRecordUncompressedMetadataBytes(rawTextBytes.Length))
        {
            return false;
        }

        string val = Encoding.UTF8.GetString(rawTextBytes).Trim();
        if (!string.IsNullOrWhiteSpace(val))
        {
            chunks.Add(val);
        }

        return true;
    }

    private static bool TryExtractPngCompressedChunk(
        ArraySegment<byte> segment,
        ref PngQuotaTracker quotaTracker,
        List<string> chunks)
    {
        ReadOnlySpan<byte> data = segment;
        int nul = data.IndexOf((byte)0);
        if (nul <= 0 || nul > 79 || nul + 2 >= data.Length || data[nul + 1] != 0)
        {
            return true;
        }

        string keyword = Encoding.Latin1.GetString(data[..nul]);
        if (IsIgnoredMetadataKeyword(keyword))
        {
            return true;
        }

        int compressedOffset = nul + 2;
        var compressedPayload = new ArraySegment<byte>(
            segment.Array!,
            segment.Offset + compressedOffset,
            segment.Count - compressedOffset);

        if (!TryInflatePngMetadataText(compressedPayload, ref quotaTracker, out string val))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(val))
        {
            chunks.Add(val);
        }

        return true;
    }

    private static bool TryExtractPngInternationalChunk(
        ArraySegment<byte> segment,
        ref PngQuotaTracker quotaTracker,
        List<string> chunks)
    {
        ReadOnlySpan<byte> data = segment;
        int nul = data.IndexOf((byte)0);
        if (nul <= 0 || nul > 79 || nul + 5 >= data.Length)
        {
            return true;
        }

        string keyword = Encoding.Latin1.GetString(data[..nul]);
        if (IsIgnoredMetadataKeyword(keyword))
        {
            return true;
        }

        byte compressionFlag = data[nul + 1];
        byte compressionMethod = data[nul + 2];
        if (compressionFlag > 1 || (compressionFlag == 1 && compressionMethod != 0))
        {
            return true;
        }

        int cursor = nul + 3; // skip compressionFlag + compressionMethod
        for (int i = 0; i < 2; i++)
        {
            int nextNull = data[cursor..].IndexOf((byte)0);
            if (nextNull < 0)
            {
                return true;
            }

            cursor += nextNull + 1;
        }

        if (cursor >= data.Length)
        {
            return true;
        }

        string val;
        if (compressionFlag == 1)
        {
            var compressedPayload = new ArraySegment<byte>(
                segment.Array!,
                segment.Offset + cursor,
                segment.Count - cursor);
            if (!TryInflatePngMetadataText(compressedPayload, ref quotaTracker, out val))
            {
                return false;
            }
        }
        else
        {
            ReadOnlySpan<byte> textBytes = data[cursor..];
            if (!quotaTracker.TryRecordUncompressedMetadataBytes(textBytes.Length))
            {
                return false;
            }

            val = Encoding.UTF8.GetString(textBytes).Trim();
        }

        if (!string.IsNullOrWhiteSpace(val))
        {
            chunks.Add(val);
        }

        return true;
    }

    private static bool TryInflatePngMetadataText(
        ArraySegment<byte> compressedPayload,
        ref PngQuotaTracker quotaTracker,
        out string text)
    {
        text = string.Empty;
        if (compressedPayload.Count <= 0)
        {
            return true;
        }

        long effectiveCompressedBytes = Math.Max(1L, compressedPayload.Count);
        byte[] scratch = ArrayPool<byte>.Shared.Rent(PngDecompressBufferSize);
        byte[] output = ArrayPool<byte>.Shared.Rent(ReceiptOcrEngine.MaxPngMetadataChunkBytes);
        try
        {
            using var ms = new MemoryStream(
                compressedPayload.Array!,
                compressedPayload.Offset,
                compressedPayload.Count,
                writable: false);
            using var zlib = new ZLibStream(ms, CompressionMode.Decompress);

            int chunkBytesRead = 0;
            while (true)
            {
                int readLimit = Math.Min(scratch.Length, PngDecompressBufferSize);
                int bytesRead = zlib.Read(scratch, 0, readLimit);
                if (bytesRead <= 0)
                {
                    break;
                }

                if (!quotaTracker.TryValidateInflatedMetadataStep(
                        chunkBytesBeforeStep: chunkBytesRead,
                        stepBytes: bytesRead,
                        effectiveCompressedBytes: effectiveCompressedBytes,
                        out int updatedChunkBytes))
                {
                    return false;
                }

                Buffer.BlockCopy(scratch, 0, output, chunkBytesRead, bytesRead);
                chunkBytesRead = updatedChunkBytes;
            }

            quotaTracker.CommitInflatedMetadataChunk(chunkBytesRead);
            text = Encoding.UTF8.GetString(output, 0, chunkBytesRead).Trim();
            return true;
        }
        catch
        {
            return !quotaTracker.QuotaExceeded;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(output);
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }

    private static bool IsIgnoredMetadataKeyword(string keyword) =>
        keyword.Equals("Software", StringComparison.OrdinalIgnoreCase)
        || keyword.Equals("Creation Time", StringComparison.OrdinalIgnoreCase)
        || keyword.Equals("Source", StringComparison.OrdinalIgnoreCase)
        || keyword.Equals("sRGB", StringComparison.OrdinalIgnoreCase);

    private struct PngQuotaTracker
    {
        private int _chunkCount;
        private long _totalMetadataBytes;

        public bool QuotaExceeded { get; private set; }

        public bool TryRecordChunk()
        {
            _chunkCount++;
            if (_chunkCount > ReceiptOcrEngine.MaxPngChunkCount)
            {
                QuotaExceeded = true;
                return false;
            }

            return true;
        }

        public bool TryRecordUncompressedMetadataBytes(int byteCount)
        {
            if (byteCount < 0
                || byteCount > ReceiptOcrEngine.MaxPngMetadataChunkBytes
                || _totalMetadataBytes + byteCount > ReceiptOcrEngine.MaxPngTotalMetadataBytes)
            {
                QuotaExceeded = true;
                return false;
            }

            _totalMetadataBytes += byteCount;
            return true;
        }

        public bool TryValidateInflatedMetadataStep(
            int chunkBytesBeforeStep,
            int stepBytes,
            long effectiveCompressedBytes,
            out int updatedChunkBytes)
        {
            long nextChunkBytes = (long)chunkBytesBeforeStep + stepBytes;
            if (nextChunkBytes > ReceiptOcrEngine.MaxPngMetadataChunkBytes
                || _totalMetadataBytes + nextChunkBytes > ReceiptOcrEngine.MaxPngTotalMetadataBytes
                || (nextChunkBytes > ReceiptOcrEngine.PngMetadataRatioFloorBytes
                    && (Int128)nextChunkBytes > (Int128)effectiveCompressedBytes * ReceiptOcrEngine.MaxPngCompressionRatio))
            {
                updatedChunkBytes = chunkBytesBeforeStep;
                QuotaExceeded = true;
                return false;
            }

            updatedChunkBytes = (int)nextChunkBytes;
            return true;
        }

        public void CommitInflatedMetadataChunk(int chunkBytes)
        {
            _totalMetadataBytes += chunkBytes;
        }
    }
}
