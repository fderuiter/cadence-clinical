using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Cadence.Apps.Payments.Application;

/// <summary>
/// Extracted receipt fields and legibility metrics produced by <see cref="ReceiptOcrEngine"/>.
/// Never fabricates fallback merchant names, amounts, dates, or confidence scores when an image is unreadable.
/// </summary>
public sealed record ReceiptOcrExtractionResult(
    bool OcrReadable,
    string MerchantName,
    string ExpenseDate,
    decimal GrossAmount,
    decimal TaxDeduction,
    decimal NetAmount,
    string Category,
    double ConfidenceScore,
    string RawText,
    string? Warning = null
);

/// <summary>
/// Deterministic receipt Optical Character Recognition (OCR) and structured metadata parser (ADR-2810, PRD-PAT-002).
/// Supports raster PNG/BMP/PNM glyph recognition, PNG/JPEG/PDF/SVG embedded text layers, and raw text inputs,
/// with strict zero-fabrication guardrails when an uploaded image is blank, blurry, or unreadable.
/// </summary>
public static class ReceiptOcrEngine
{
    private const int GlyphWidth = 5;
    private const int GlyphHeight = 7;

    public const int MaxReceiptPayloadBytes = 10 * 1024 * 1024; // 10 MiB
    public const int MaxBase64NonWhitespaceChars = ((MaxReceiptPayloadBytes + 2) / 3) * 4;
    public const int MaxEncodedReceiptPayloadChars = 15 * 1024 * 1024; // Covers 10 MiB base64 + data: header + line wraps
    public const int MaxPngDimension = 4_096;
    public const int MaxPngChunkCount = 2_048;
    public const int MaxPngMetadataChunkBytes = 64 * 1024; // 64 KiB
    public const int MaxPngTotalMetadataBytes = 256 * 1024; // 256 KiB
    public const int MaxPngCompressionRatio = 100;
    public const int PngMetadataRatioFloorBytes = 16 * 1024; // 16 KiB
    public const int PngIdatRatioFloorBytes = 4 * 1024 * 1024; // 4 MiB
    public const int MaxExtractedRawTextChars = 256 * 1024; // 262,144 chars
    private const int PngDecompressBufferSize = 4_096;

    private static readonly Dictionary<char, string[]> CanonicalFont5x7 = BuildCanonicalFont5x7();
    private static readonly List<(char Ch, double[] Grid, double Aspect)> GlyphTemplates = BuildNormalizedTemplates();

    private static readonly Regex IsoDateRegex = new(
        @"\b(20\d{2})[-/](0[1-9]|1[0-2])[-/](0[1-9]|[12]\d|3[01])\b",
        RegexOptions.Compiled);

    private static readonly Regex UsDateRegex = new(
        @"\b(0?[1-9]|1[0-2])[/-](0?[1-9]|[12]\d|3[01])[/-](20\d{2})\b",
        RegexOptions.Compiled);

    private static readonly Regex NamedMonthDateRegex = new(
        @"\b(Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\s+(\d{1,2}),?\s+(20\d{2})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LabeledTotalRegex = new(
        @"(?<!Sub[-\s]?)(?:Grand\s+Total|Total\s+Paid|Total\s+Due|Total\s+Amount|Total\s+Charge|Amount\s+Paid|Amount\s+Due|Balance\s+Due|Fare\s+Paid|Meal\s+Charge|Room\s+Charge|Fuel\s+Total|Net\s+Total|Total|Fare|Paid|Charge)\s*[:#-]?\s*(?:USD\s*)?\$?\s*(\d{1,6}\.\d{2})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CurrencyAmountRegex = new(
        @"(?:\$|USD\s*)\s*(\d{1,6}\.\d{2})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex StandaloneAmountRegex = new(
        @"(?<!\d[/-])\b(\d{1,5}\.\d{2})\b(?![/-]\d)",
        RegexOptions.Compiled);

    private static readonly Regex ExplicitMerchantRegex = new(
        @"^\s*(?:Merchant|Vendor|Store|Station|Establishment|Payee)\s*:\s*(.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly string[] IgnoredHeaderPhrases =
    [
        "RECEIPT",
        "TAX INVOICE",
        "INVOICE",
        "CUSTOMER COPY",
        "MERCHANT COPY",
        "TRANSACTION RECEIPT",
        "PAYMENT RECEIPT",
        "THANK YOU"
    ];

    /// <summary>
    /// Extracts receipt metadata from raw text or a base64-encoded image/document payload.
    /// </summary>
    public static ReceiptOcrExtractionResult Extract(
        string? rawText,
        string? imageBase64,
        string? categoryHint = null)
    {
        string extractedText = string.Empty;
        double legibilityFactor = 0.0;
        string? warning = null;

        if (!string.IsNullOrWhiteSpace(rawText))
        {
            extractedText = CapRawText(NormalizeText(rawText));
            legibilityFactor = string.IsNullOrWhiteSpace(extractedText) ? 0.0 : 1.0;
        }
        else if (!string.IsNullOrWhiteSpace(imageBase64))
        {
            if (!TryDecodeReceiptPayload(imageBase64, out byte[] bytes) || bytes.Length == 0)
            {
                return UnreadableResult("The uploaded receipt file could not be decoded as valid image data.");
            }

            return ExtractFromBytes(bytes, categoryHint);
        }
        else
        {
            return UnreadableResult("No receipt image or text content was provided for OCR extraction.");
        }

        return BuildExtractionResult(extractedText, legibilityFactor, warning, categoryHint);
    }

    internal static ReceiptOcrExtractionResult ExtractFromBytes(byte[] bytes, string? categoryHint = null)
    {
        if (bytes.Length == 0 || bytes.Length > MaxReceiptPayloadBytes)
        {
            return UnreadableResult("The uploaded receipt file could not be decoded as valid image data.");
        }

        var (extractedText, legibilityFactor, warning) = ExtractTextFromBytes(bytes);
        return BuildExtractionResult(CapRawText(extractedText), legibilityFactor, warning, categoryHint);
    }

    private static ReceiptOcrExtractionResult BuildExtractionResult(
        string extractedText,
        double legibilityFactor,
        string? warning,
        string? categoryHint)
    {

        if (string.IsNullOrWhiteSpace(extractedText) || !ContainsMeaningfulTokens(extractedText))
        {
            return UnreadableResult(warning ?? "Unable to extract readable receipt text from the uploaded image. Please verify the image clarity or enter details manually.");
        }

        string expenseDate = ParseExpenseDate(extractedText);
        decimal grossAmount = ParseGrossAmount(extractedText);
        string merchantName = ParseMerchantName(extractedText);
        string category = InferCategory(extractedText, merchantName, categoryHint);

        bool hasMerchant = !string.IsNullOrWhiteSpace(merchantName);
        bool hasAmount = grossAmount > 0m;
        bool hasDate = !string.IsNullOrWhiteSpace(expenseDate);

        if (!hasMerchant && !hasAmount)
        {
            return new ReceiptOcrExtractionResult(
                OcrReadable: false,
                MerchantName: string.Empty,
                ExpenseDate: expenseDate,
                GrossAmount: 0m,
                TaxDeduction: 0m,
                NetAmount: 0m,
                Category: category,
                ConfidenceScore: 0.0,
                RawText: extractedText,
                Warning: warning ?? "No recognizable merchant or payment amount was found on the uploaded receipt."
            );
        }

        double completeness = 0.0;
        if (hasMerchant)
        {
            completeness += 0.36;
        }
        if (hasAmount)
        {
            completeness += 0.46;
        }
        if (hasDate)
        {
            completeness += 0.16;
        }

        // Clean complete receipt -> 0.98; missing date -> ~0.82; missing merchant -> ~0.62; blurry image scales down proportionally.
        double rawConfidence = Math.Clamp(completeness * legibilityFactor, 0.0, 0.98);
        if (!hasAmount)
        {
            rawConfidence = Math.Min(rawConfidence, 0.45);
        }
        else if (!hasMerchant)
        {
            rawConfidence = Math.Min(rawConfidence, 0.65);
        }

        double confidenceScore = Math.Round(rawConfidence, 2, MidpointRounding.AwayFromZero);

        decimal taxDeduction = 0m;
        if (string.Equals(category, "MEAL", StringComparison.OrdinalIgnoreCase) && grossAmount > 75.00m)
        {
            taxDeduction = Math.Round((grossAmount - 75.00m) * 0.15m, 2);
        }

        decimal netAmount = Math.Round(Math.Max(0m, grossAmount - taxDeduction), 2);
        string? lowClarityNote = confidenceScore < 0.90
            ? (warning ?? "Receipt scan confidence is below 90%; please review extracted fields before submitting.")
            : warning;

        return new ReceiptOcrExtractionResult(
            OcrReadable: hasAmount && hasMerchant,
            MerchantName: merchantName,
            ExpenseDate: expenseDate,
            GrossAmount: grossAmount,
            TaxDeduction: taxDeduction,
            NetAmount: netAmount,
            Category: category,
            ConfidenceScore: confidenceScore,
            RawText: extractedText,
            Warning: lowClarityNote
        );
    }

    /// <summary>
    /// Renders a genuine raster PNG image containing printed receipt lines (with zero embedded text metadata chunks),
    /// optionally applying box-blur and low-contrast degradation to simulate blurry mobile camera captures.
    /// </summary>
    public static byte[] RenderReceiptPng(IReadOnlyList<string> lines, int scale = 3, int blurRadius = 0, byte inkLevel = 20, byte paperLevel = 250)
    {
        ArgumentNullException.ThrowIfNull(lines);
        int effectiveScale = Math.Clamp(scale, 2, 6);
        int charStepX = (GlyphWidth + 2) * effectiveScale;
        int lineStepY = (GlyphHeight + 5) * effectiveScale;
        int marginX = 12 * effectiveScale;
        int marginY = 10 * effectiveScale;

        int maxChars = lines.Count == 0 ? 10 : Math.Max(10, lines.Max(l => l?.Length ?? 0));
        int width = marginX * 2 + maxChars * charStepX;
        int height = marginY * 2 + Math.Max(1, lines.Count) * lineStepY;

        byte[] gray = new byte[width * height];
        Array.Fill(gray, paperLevel);

        for (int lineIdx = 0; lineIdx < lines.Count; lineIdx++)
        {
            string line = (lines[lineIdx] ?? string.Empty).ToUpperInvariant();
            int baseY = marginY + lineIdx * lineStepY;
            for (int charIdx = 0; charIdx < line.Length; charIdx++)
            {
                char ch = line[charIdx];
                if (ch == ' ' || !CanonicalFont5x7.TryGetValue(ch, out string[]? rows))
                {
                    continue;
                }

                int baseX = marginX + charIdx * charStepX;
                for (int gy = 0; gy < GlyphHeight; gy++)
                {
                    string row = rows[gy];
                    for (int gx = 0; gx < GlyphWidth; gx++)
                    {
                        if (row[gx] != '#')
                        {
                            continue;
                        }

                        for (int sy = 0; sy < effectiveScale; sy++)
                        {
                            int py = baseY + gy * effectiveScale + sy;
                            if ((uint)py >= (uint)height)
                            {
                                continue;
                            }

                            for (int sx = 0; sx < effectiveScale; sx++)
                            {
                                int px = baseX + gx * effectiveScale + sx;
                                if ((uint)px < (uint)width)
                                {
                                    gray[py * width + px] = inkLevel;
                                }
                            }
                        }
                    }
                }
            }
        }

        if (blurRadius > 0)
        {
            gray = ApplyBoxBlur(gray, width, height, blurRadius);
        }

        return EncodeGrayscalePng(gray, width, height);
    }

    private static ReceiptOcrExtractionResult UnreadableResult(string warning) =>
        new(
            OcrReadable: false,
            MerchantName: string.Empty,
            ExpenseDate: string.Empty,
            GrossAmount: 0m,
            TaxDeduction: 0m,
            NetAmount: 0m,
            Category: string.Empty,
            ConfidenceScore: 0.0,
            RawText: string.Empty,
            Warning: warning
        );

    public static bool TryDecodeReceiptPayload(string? payload, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > MaxEncodedReceiptPayloadChars)
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
                    || dataPart.Length > MaxEncodedReceiptPayloadChars)
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

                if (unescaped.Length == 0 || Encoding.UTF8.GetByteCount(unescaped) > MaxReceiptPayloadBytes)
                {
                    return false;
                }

                bytes = Encoding.UTF8.GetBytes(unescaped);
                return bytes.Length > 0 && bytes.Length <= MaxReceiptPayloadBytes;
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
            if (nonWhitespaceCount > MaxBase64NonWhitespaceChars)
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
        if (expectedDecodedBytes <= 0 || expectedDecodedBytes > MaxReceiptPayloadBytes)
        {
            return false;
        }

        byte[] decoded = new byte[(int)expectedDecodedBytes];
        if (!Convert.TryFromBase64Chars(span, decoded, out int bytesWritten)
            || bytesWritten <= 0
            || bytesWritten > MaxReceiptPayloadBytes)
        {
            return false;
        }

        bytes = bytesWritten == decoded.Length ? decoded : decoded[..bytesWritten];
        return true;
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

    private static string CapRawText(string text) =>
        text.Length <= MaxExtractedRawTextChars ? text : text[..MaxExtractedRawTextChars];

    private static (string Text, double Legibility, string? Warning) ExtractTextFromBytes(byte[] bytes)
    {
        // 1. PNG (89 50 4E 47 0D 0A 1A 0A)
        if (bytes.Length >= 8
            && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
        {
            return ExtractFromPng(bytes);
        }

        // 2. JPEG (FF D8 FF)
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ExtractFromJpeg(bytes);
        }

        // 3. BMP ("BM")
        if (bytes.Length >= 54 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M')
        {
            if (TryDecodeBmpGrayscale(bytes, out byte[] bmpGray, out int bmpW, out int bmpH))
            {
                return RecognizeGrayscaleBitmap(bmpGray, bmpW, bmpH);
            }
            return (string.Empty, 0.0, "Unsupported BMP encoding.");
        }

        // 4. PGM / PPM ("P5" or "P6")
        if (bytes.Length >= 10
            && bytes[0] == (byte)'P'
            && (bytes[1] == (byte)'5' || bytes[1] == (byte)'6')
            && TryDecodePnmGrayscale(bytes, out byte[] pnmGray, out int pnmW, out int pnmH))
        {
            return RecognizeGrayscaleBitmap(pnmGray, pnmW, pnmH);
        }

        // 5. PDF ("%PDF-")
        if (bytes.Length >= 5
            && bytes[0] == (byte)'%' && bytes[1] == (byte)'P' && bytes[2] == (byte)'D' && bytes[3] == (byte)'F')
        {
            string pdfText = ExtractFromPdf(bytes);
            return string.IsNullOrWhiteSpace(pdfText)
                ? (string.Empty, 0.0, "Uploaded PDF contains no extractable receipt text.")
                : (pdfText, 0.96, null);
        }

        // 6. SVG XML document
        if (TryDecodePrintableUtf8(bytes, out string utf8Text) && IsSvgXmlDocument(utf8Text))
        {
            string svgText = ExtractFromSvg(utf8Text);
            return string.IsNullOrWhiteSpace(svgText)
                ? (string.Empty, 0.0, "Uploaded SVG image contains no readable receipt text.")
                : (svgText, 0.98, null);
        }

        return (string.Empty, 0.0, "Uploaded image format could not be read as receipt text.");
    }

    private static (string Text, double Legibility, string? Warning) ExtractFromPng(byte[] bytes)
    {
        var embeddedChunks = new List<string>();
        bool decodedPixels = TryDecodePngGrayscale(
            bytes,
            embeddedChunks,
            out byte[] gray,
            out int width,
            out int height,
            out bool quotaOrStructureFailed);

        if (quotaOrStructureFailed)
        {
            return (string.Empty, 0.0, "Uploaded PNG image could not be decoded within safety limits.");
        }

        double imageClarity = decodedPixels ? MeasureImageClarity(gray, width, height) : 1.0;

        if (decodedPixels)
        {
            var (rasterText, rasterLegibility, rasterWarning) = RecognizeGrayscaleBitmap(gray, width, height);
            if (!string.IsNullOrWhiteSpace(rasterText) && ContainsMeaningfulTokens(rasterText))
            {
                // If PNG also carries exact OCR text metadata chunk (e.g. tEXt/iTXt), use its text while preserving true pixel clarity!
                string joinedMeta = CapRawText(NormalizeText(string.Join("\n", embeddedChunks)));
                if (!string.IsNullOrWhiteSpace(joinedMeta) && ContainsMeaningfulTokens(joinedMeta))
                {
                    double combinedLegibility = Math.Min(rasterLegibility, imageClarity);
                    string? clarityWarning = combinedLegibility < 0.90
                        ? "Image appears blurry or low contrast; please confirm extracted details."
                        : null;
                    return (joinedMeta, combinedLegibility, clarityWarning);
                }

                return (CapRawText(rasterText), rasterLegibility, rasterWarning);
            }
        }

        string metaText = CapRawText(NormalizeText(string.Join("\n", embeddedChunks)));
        if (!string.IsNullOrWhiteSpace(metaText) && ContainsMeaningfulTokens(metaText))
        {
            double legibility = decodedPixels ? Math.Clamp(imageClarity, 0.35, 0.98) : 0.96;
            string? warn = legibility < 0.90 ? "Receipt image clarity is low; please verify extracted fields." : null;
            return (metaText, legibility, warn);
        }

        return (string.Empty, 0.0, "Uploaded PNG image contains no readable receipt text.");
    }

    private struct PngQuotaTracker
    {
        private int _chunkCount;
        private long _totalMetadataBytes;

        public bool QuotaExceeded { get; private set; }

        public bool TryRecordChunk()
        {
            _chunkCount++;
            if (_chunkCount > MaxPngChunkCount)
            {
                QuotaExceeded = true;
                return false;
            }

            return true;
        }

        public bool TryRecordUncompressedMetadataBytes(int byteCount)
        {
            if (byteCount < 0
                || byteCount > MaxPngMetadataChunkBytes
                || _totalMetadataBytes + byteCount > MaxPngTotalMetadataBytes)
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
            if (nextChunkBytes > MaxPngMetadataChunkBytes
                || _totalMetadataBytes + nextChunkBytes > MaxPngTotalMetadataBytes
                || (nextChunkBytes > PngMetadataRatioFloorBytes
                    && (Int128)nextChunkBytes > (Int128)effectiveCompressedBytes * MaxPngCompressionRatio))
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

    private static bool TryDecodePngGrayscale(
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

        if (bytes.Length < 37 || bytes.Length > MaxReceiptPayloadBytes)
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

                if (chunkLen > (uint)(bytes.Length - pos - 4))
                {
                    quotaOrStructureFailed = true;
                    break;
                }

                if (!quotaTracker.TryRecordChunk())
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
            || rawWidth > MaxPngDimension
            || rawHeight == 0
            || rawHeight > MaxPngDimension
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
            || (expectedRawBytes > PngIdatRatioFloorBytes
                && (Int128)expectedRawBytes > (Int128)effectiveCompressed * MaxPngCompressionRatio))
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

                grayOut ??= new byte[width * height];
                byte filterType = currScanline[0];

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
                        2 => RgbToLum(currRow[px], currRow[px + 1], currRow[px + 2]),
                        3 => PaletteToLum(palette, currRow[px]),
                        4 => BlendAlphaOnWhite(currRow[px], currRow[px + 1]),
                        6 => BlendAlphaOnWhite(RgbToLum(currRow[px], currRow[px + 1], currRow[px + 2]), currRow[px + 3]),
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

    private static byte RgbToLum(byte r, byte g, byte b) =>
        (byte)Math.Clamp((77 * r + 150 * g + 29 * b) >> 8, 0, 255);

    private static byte BlendAlphaOnWhite(byte lum, byte alpha) =>
        (byte)Math.Clamp(((lum * alpha) + (255 * (255 - alpha))) / 255, 0, 255);

    private static byte PaletteToLum(byte[]? palette, byte index)
    {
        int offset = index * 3;
        if (palette == null || offset + 2 >= palette.Length)
        {
            return index;
        }
        return RgbToLum(palette[offset], palette[offset + 1], palette[offset + 2]);
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
        // Skip language tag (null-terminated) and translated keyword (null-terminated)
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
        byte[] output = ArrayPool<byte>.Shared.Rent(MaxPngMetadataChunkBytes);
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

    private static (string Text, double Legibility, string? Warning) ExtractFromJpeg(byte[] bytes)
    {
        var extracted = new List<string>();
        int pos = 2;
        while (pos + 4 <= bytes.Length && bytes[pos] == 0xFF)
        {
            byte marker = bytes[pos + 1];
            if (marker == 0xDA || marker == 0xD9) // SOS or EOI
            {
                break;
            }
            if (marker == 0xFF)
            {
                pos++;
                continue;
            }

            int segLen = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(pos + 2, 2));
            if (segLen < 2 || pos + 2 + segLen > bytes.Length)
            {
                break;
            }

            ReadOnlySpan<byte> seg = bytes.AsSpan(pos + 4, segLen - 2);
            if (marker == 0xFE) // JPEG COM comment
            {
                string comment = Encoding.UTF8.GetString(seg).Trim('\0', ' ', '\t', '\r', '\n');
                if (!string.IsNullOrWhiteSpace(comment))
                {
                    extracted.Add(comment);
                }
            }
            else if (marker == 0xE1) // APP1 EXIF or XMP
            {
                string segStr = Encoding.UTF8.GetString(seg);
                if (segStr.Contains("http://ns.adobe.com/xap/1.0/", StringComparison.Ordinal)) // deid-ignore
                {
                    int xmlStart = segStr.IndexOf('<', StringComparison.Ordinal);
                    if (xmlStart >= 0)
                    {
                        string xmpText = ExtractFromSvg(segStr[xmlStart..]);
                        if (!string.IsNullOrWhiteSpace(xmpText))
                        {
                            extracted.Add(xmpText);
                        }
                    }
                }
                else if (seg.Length > 8 && seg[..4].SequenceEqual("Exif"u8))
                {
                    string exifAscii = ExtractPrintableRuns(seg[6..]);
                    if (!string.IsNullOrWhiteSpace(exifAscii))
                    {
                        extracted.Add(exifAscii);
                    }
                }
            }

            pos += 2 + segLen;
        }

        string text = NormalizeText(string.Join("\n", extracted));
        if (string.IsNullOrWhiteSpace(text) || !ContainsMeaningfulTokens(text))
        {
            return (string.Empty, 0.0, "Uploaded JPEG image could not be read without raster conversion or embedded receipt text.");
        }

        return (text, 0.96, null);
    }

    private static string ExtractPrintableRuns(ReadOnlySpan<byte> data)
    {
        var runs = new List<string>();
        var sb = new StringBuilder();
        foreach (byte b in data)
        {
            if ((b >= 32 && b <= 126) || b == '\n' || b == '\r')
            {
                sb.Append((char)b);
            }
            else
            {
                if (sb.Length >= 6)
                {
                    string candidate = sb.ToString().Trim();
                    if (!candidate.Equals("UNICODE", StringComparison.OrdinalIgnoreCase)
                        && !candidate.Equals("ASCII", StringComparison.OrdinalIgnoreCase))
                    {
                        runs.Add(candidate);
                    }
                }
                sb.Clear();
            }
        }

        if (sb.Length >= 6)
        {
            runs.Add(sb.ToString().Trim());
        }

        return string.Join("\n", runs);
    }

    private static bool TryDecodeBmpGrayscale(byte[] bytes, out byte[] gray, out int width, out int height)
    {
        gray = [];
        width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18, 4));
        int rawHeight = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22, 4));
        bool topDown = rawHeight < 0;
        height = Math.Abs(rawHeight);
        int pixelOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(10, 4));
        ushort bpp = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28, 2));
        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(30, 4));

        if (width <= 0 || height <= 0 || width > 4096 || height > 4096 || compression != 0 || (bpp != 24 && bpp != 32))
        {
            return false;
        }

        int bytesPerPixel = bpp / 8;
        int rowStride = ((width * bytesPerPixel + 3) / 4) * 4;
        if (pixelOffset < 54 || pixelOffset + rowStride * height > bytes.Length)
        {
            return false;
        }

        gray = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            int srcY = topDown ? y : (height - 1 - y);
            int srcRow = pixelOffset + srcY * rowStride;
            for (int x = 0; x < width; x++)
            {
                int px = srcRow + x * bytesPerPixel;
                byte b = bytes[px];
                byte g = bytes[px + 1];
                byte r = bytes[px + 2];
                gray[y * width + x] = RgbToLum(r, g, b);
            }
        }
        return true;
    }

    private static bool TryDecodePnmGrayscale(byte[] bytes, out byte[] gray, out int width, out int height)
    {
        gray = [];
        width = 0;
        height = 0;
        bool isRgb = bytes[1] == (byte)'6';
        int cursor = 2;

        string NextToken()
        {
            while (cursor < bytes.Length)
            {
                if (bytes[cursor] == (byte)'#')
                {
                    while (cursor < bytes.Length && bytes[cursor] != (byte)'\n')
                    {
                        cursor++;
                    }
                }
                else if (char.IsWhiteSpace((char)bytes[cursor]))
                {
                    cursor++;
                }
                else
                {
                    break;
                }
            }
            int start = cursor;
            while (cursor < bytes.Length && !char.IsWhiteSpace((char)bytes[cursor]))
            {
                cursor++;
            }
            return Encoding.ASCII.GetString(bytes, start, cursor - start);
        }

        if (!int.TryParse(NextToken(), out width) || !int.TryParse(NextToken(), out height) || !int.TryParse(NextToken(), out int maxVal))
        {
            return false;
        }
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096 || maxVal <= 0 || cursor >= bytes.Length)
        {
            return false;
        }

        cursor++; // single whitespace after maxVal
        int pixelCount = width * height;
        int needed = isRgb ? pixelCount * 3 : pixelCount;
        if (cursor + needed > bytes.Length)
        {
            return false;
        }

        gray = new byte[pixelCount];
        for (int i = 0; i < pixelCount; i++)
        {
            gray[i] = isRgb
                ? RgbToLum(bytes[cursor + i * 3], bytes[cursor + i * 3 + 1], bytes[cursor + i * 3 + 2])
                : bytes[cursor + i];
        }
        return true;
    }

    private static bool IsSvgXmlDocument(string utf8Text)
    {
        string trimmed = utf8Text.Trim('\uFEFF', '\0', ' ', '\t', '\r', '\n');
        if (!trimmed.Contains("<svg", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var doc = XDocument.Parse(trimmed, LoadOptions.PreserveWhitespace);
            return doc.Root is not null
                && doc.Root.Name.LocalName.Equals("svg", StringComparison.OrdinalIgnoreCase);
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    private static string ExtractFromSvg(string xmlText)
    {
        try
        {
            var doc = XDocument.Parse(xmlText, LoadOptions.PreserveWhitespace);
            var lines = doc.Descendants()
                .Where(el => el.Name.LocalName is "text" or "desc" or "title" or "li")
                .Select(el => Regex.Replace(el.Value, @"\s+", " ").Trim())
                .Where(val => !string.IsNullOrWhiteSpace(val))
                .ToList();
            return NormalizeText(string.Join("\n", lines));
        }
        catch
        {
            string stripped = Regex.Replace(xmlText, @"<[^>]+>", "\n");
            return NormalizeText(WebUtilityDecode(stripped));
        }
    }

    private static string WebUtilityDecode(string value) =>
        value.Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase)
             .Replace("&lt;", "<", StringComparison.OrdinalIgnoreCase)
             .Replace("&gt;", ">", StringComparison.OrdinalIgnoreCase)
             .Replace("&quot;", "\"", StringComparison.OrdinalIgnoreCase)
             .Replace("&#39;", "'", StringComparison.OrdinalIgnoreCase)
             .Replace("&apos;", "'", StringComparison.OrdinalIgnoreCase);

    private static string ExtractFromPdf(byte[] bytes)
    {
        string latin = Encoding.Latin1.GetString(bytes);
        var sb = new StringBuilder();
        foreach (Match m in Regex.Matches(latin, @"\(([^()\\]*(?:\\.[^()\\]*)*)\)\s*Tj"))
        {
            sb.AppendLine(m.Groups[1].Value);
        }
        return NormalizeText(sb.ToString());
    }

    private static bool TryDecodePrintableUtf8(byte[] bytes, out string text)
    {
        text = string.Empty;
        try
        {
            var utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            string candidate = utf8Strict.GetString(bytes);
            if (candidate.Length == 0)
            {
                return false;
            }

            int printable = 0;
            foreach (char ch in candidate)
            {
                if (ch is '\r' or '\n' or '\t' || (ch >= 32 && ch != 127))
                {
                    printable++;
                }
            }

            if ((double)printable / candidate.Length < 0.92)
            {
                return false;
            }

            text = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static double MeasureImageClarity(byte[] gray, int width, int height)
    {
        if (gray.Length == 0 || width < 8 || height < 8)
        {
            return 0.0;
        }

        byte minVal = 255;
        byte maxVal = 0;
        long sum = 0;
        for (int i = 0; i < gray.Length; i++)
        {
            byte v = gray[i];
            if (v < minVal) minVal = v;
            if (v > maxVal) maxVal = v;
            sum += v;
        }

        int dynamicRange = maxVal - minVal;
        if (dynamicRange < 25)
        {
            return 0.0;
        }

        // Measure edge sharpness across foreground transitions: crisp print has steep transitions (>=120),
        // blurred images have shallow gradients across multiple pixels.
        long strongEdges = 0;
        long weakEdges = 0;
        for (int y = 1; y < height - 1; y++)
        {
            int row = y * width;
            for (int x = 1; x < width - 1; x++)
            {
                int gx = Math.Abs(gray[row + x + 1] - gray[row + x - 1]);
                int gy = Math.Abs(gray[row + width + x] - gray[row - width + x]);
                int grad = Math.Max(gx, gy);
                if (grad >= 95)
                {
                    strongEdges++;
                }
                else if (grad >= 20)
                {
                    weakEdges++;
                }
            }
        }

        if (strongEdges + weakEdges == 0)
        {
            return 0.0;
        }

        double sharpnessRatio = (double)strongEdges / (strongEdges + weakEdges);
        double rangeScore = Math.Clamp((dynamicRange - 25) / 150.0, 0.25, 1.0);
        double sharpnessScore = Math.Clamp(0.35 + sharpnessRatio * 0.95, 0.35, 1.0);
        return Math.Round(Math.Clamp(rangeScore * sharpnessScore, 0.0, 1.0), 3);
    }

    private static (string Text, double Legibility, string? Warning) RecognizeGrayscaleBitmap(byte[] gray, int width, int height)
    {
        if (width < 12 || height < 8)
        {
            return (string.Empty, 0.0, "Uploaded receipt image is too small to contain readable text.");
        }

        double clarity = MeasureImageClarity(gray, width, height);
        if (clarity <= 0.05)
        {
            return (string.Empty, 0.0, "Uploaded receipt image is blank or lacks readable contrast.");
        }

        byte minV = 255;
        byte maxV = 0;
        long totalLum = 0;
        for (int i = 0; i < gray.Length; i++)
        {
            if (gray[i] < minV) minV = gray[i];
            if (gray[i] > maxV) maxV = gray[i];
            totalLum += gray[i];
        }

        double meanLum = (double)totalLum / gray.Length;
        bool darkBackground = meanLum < 110;
        int threshold = (minV + maxV) / 2;

        bool[] ink = new bool[gray.Length];
        int inkCount = 0;
        for (int i = 0; i < gray.Length; i++)
        {
            bool isInk = darkBackground ? gray[i] > threshold : gray[i] < threshold;
            ink[i] = isInk;
            if (isInk)
            {
                inkCount++;
            }
        }

        double inkRatio = (double)inkCount / gray.Length;
        if (inkRatio < 0.001 || inkRatio > 0.70)
        {
            return (string.Empty, 0.0, "Uploaded receipt image contains no recognizable text regions.");
        }

        // 1. Segment horizontal text lines
        var lineSpans = FindActiveSpans(height, y =>
        {
            int row = y * width;
            int count = 0;
            for (int x = 0; x < width; x++)
            {
                if (ink[row + x]) count++;
            }
            return count >= 2;
        }, mergeGap: 1);

        if (lineSpans.Count == 0 || lineSpans.Count > 120)
        {
            return (string.Empty, 0.0, "No text lines detected in receipt image.");
        }

        var recognizedLines = new List<string>();
        double totalCharScore = 0.0;
        int totalChars = 0;

        foreach (var (lineY0, lineY1) in lineSpans)
        {
            int lineH = lineY1 - lineY0 + 1;
            if (lineH < 5)
            {
                continue;
            }

            var charSpans = FindActiveSpans(width, x =>
            {
                for (int y = lineY0; y <= lineY1; y++)
                {
                    if (ink[y * width + x]) return true;
                }
                return false;
            }, mergeGap: 0);

            if (charSpans.Count == 0)
            {
                continue;
            }

            // Estimate typical character width + inter-character gap so spaces are inserted cleanly
            var widths = charSpans.Select(s => s.End - s.Start + 1).OrderBy(w => w).ToList();
            double medianCharWidth = widths[widths.Count / 2];
            double spaceGapThreshold = Math.Max(medianCharWidth * 1.35, lineH * 0.85);

            var lineBuilder = new StringBuilder();
            int prevEnd = -1;

            foreach (var (charX0, charX1) in charSpans)
            {
                if (prevEnd >= 0 && (charX0 - prevEnd - 1) >= spaceGapThreshold)
                {
                    lineBuilder.Append(' ');
                }

                // Trim vertical bounds tightly for punctuation vs full-height glyphs while keeping line context
                int tightY0 = lineY1;
                int tightY1 = lineY0;
                for (int y = lineY0; y <= lineY1; y++)
                {
                    for (int x = charX0; x <= charX1; x++)
                    {
                        if (ink[y * width + x])
                        {
                            if (y < tightY0) tightY0 = y;
                            if (y > tightY1) tightY1 = y;
                        }
                    }
                }

                if (tightY0 > tightY1)
                {
                    continue;
                }

                var (ch, score) = MatchGlyph(ink, width, charX0, charX1, tightY0, tightY1, lineY0, lineY1);
                lineBuilder.Append(ch);
                totalCharScore += score;
                totalChars++;
                prevEnd = charX1;
            }

            string built = Regex.Replace(lineBuilder.ToString().Trim(), @"(\d)\s*\.\s*(\d)", "$1.$2");
            if (!string.IsNullOrWhiteSpace(built))
            {
                recognizedLines.Add(built);
            }
        }

        if (recognizedLines.Count == 0 || totalChars < 3)
        {
            return (string.Empty, 0.0, "No readable characters detected in receipt image.");
        }

        double avgGlyphScore = totalCharScore / totalChars;
        if (avgGlyphScore < 0.42)
        {
            return (string.Empty, 0.0, "Uploaded image does not contain legible receipt text.");
        }

        double glyphFactor = Math.Clamp((avgGlyphScore - 0.35) / 0.55, 0.35, 1.0);
        double legibility = Math.Round(Math.Clamp(clarity * glyphFactor, 0.30, 1.0), 3);
        string? warning = legibility < 0.90
            ? "Receipt image appears blurry or low-clarity; claim may require coordinator review."
            : null;

        return (string.Join("\n", recognizedLines), legibility, warning);
    }

    private static List<(int Start, int End)> FindActiveSpans(int length, Func<int, bool> isActive, int mergeGap)
    {
        var raw = new List<(int Start, int End)>();
        int start = -1;
        for (int i = 0; i < length; i++)
        {
            if (isActive(i))
            {
                if (start < 0) start = i;
            }
            else if (start >= 0)
            {
                raw.Add((start, i - 1));
                start = -1;
            }
        }
        if (start >= 0)
        {
            raw.Add((start, length - 1));
        }

        if (mergeGap <= 0 || raw.Count <= 1)
        {
            return raw;
        }

        var merged = new List<(int Start, int End)> { raw[0] };
        for (int i = 1; i < raw.Count; i++)
        {
            var prev = merged[^1];
            if (raw[i].Start - prev.End - 1 <= mergeGap)
            {
                merged[^1] = (prev.Start, raw[i].End);
            }
            else
            {
                merged.Add(raw[i]);
            }
        }
        return merged;
    }

    private static (char Ch, double Score) MatchGlyph(
        bool[] ink,
        int imageWidth,
        int x0,
        int x1,
        int tightY0,
        int tightY1,
        int lineY0,
        int lineY1)
    {
        int boxW = x1 - x0 + 1;
        int boxH = tightY1 - tightY0 + 1;
        int lineH = Math.Max(1, lineY1 - lineY0 + 1);
        double heightRatio = (double)boxH / lineH;
        double centerRelY = ((tightY0 + tightY1) * 0.5 - lineY0) / lineH;

        // Disambiguate small punctuation marks by vertical position and height relative to text line
        if (heightRatio < 0.34)
        {
            if (centerRelY > 0.62 && boxW <= Math.Max(4, lineH * 0.40))
            {
                return ('.', 0.96);
            }
            if (centerRelY >= 0.34 && centerRelY <= 0.66 && boxW > boxH)
            {
                return ('-', 0.96);
            }
        }

        // Check colon (two vertically separated blobs within a narrow column)
        if (boxW <= Math.Max(4, lineH * 0.36) && HasVerticalGap(ink, imageWidth, x0, x1, tightY0, tightY1))
        {
            return (':', 0.96);
        }

        double[] sample = SampleNormalizedGrid(ink, imageWidth, x0, tightY0, boxW, boxH, 10, 14);
        double sampleAspect = (double)boxW / Math.Max(1, boxH);

        char bestChar = '?';
        double bestScore = -1.0;

        foreach (var (ch, templateGrid, templateAspect) in GlyphTemplates)
        {
            if (ch is '.' or '-' or ':')
            {
                continue;
            }

            double dot = 0.0;
            double normA = 0.0;
            double normB = 0.0;
            for (int i = 0; i < sample.Length; i++)
            {
                double a = sample[i];
                double b = templateGrid[i];
                dot += a * b;
                normA += a * a;
                normB += b * b;
            }

            double cosine = (normA > 0 && normB > 0) ? dot / Math.Sqrt(normA * normB) : 0.0;
            double aspectDiff = Math.Abs(sampleAspect - templateAspect);
            double aspectPenalty = Math.Min(0.18, aspectDiff * 0.14);
            double score = cosine - aspectPenalty;

            if (score > bestScore)
            {
                bestScore = score;
                bestChar = ch;
            }
        }

        return (bestChar, Math.Clamp(bestScore, 0.0, 1.0));
    }

    private static bool HasVerticalGap(bool[] ink, int imageWidth, int x0, int x1, int y0, int y1)
    {
        bool seenTopInk = false;
        bool seenGap = false;
        for (int y = y0; y <= y1; y++)
        {
            bool rowHasInk = false;
            for (int x = x0; x <= x1; x++)
            {
                if (ink[y * imageWidth + x])
                {
                    rowHasInk = true;
                    break;
                }
            }

            if (rowHasInk && !seenTopInk)
            {
                seenTopInk = true;
            }
            else if (!rowHasInk && seenTopInk)
            {
                seenGap = true;
            }
            else if (rowHasInk && seenGap)
            {
                return true;
            }
        }
        return false;
    }

    private static double[] SampleNormalizedGrid(
        bool[] ink,
        int stride,
        int x0,
        int y0,
        int boxW,
        int boxH,
        int gridW,
        int gridH)
    {
        double[] grid = new double[gridW * gridH];
        for (int gy = 0; gy < gridH; gy++)
        {
            double py0 = y0 + (gy * (double)boxH) / gridH;
            double py1 = y0 + ((gy + 1) * (double)boxH) / gridH;
            int iy0 = Math.Max(y0, (int)Math.Floor(py0));
            int iy1 = Math.Max(iy0, (int)Math.Ceiling(py1) - 1);

            for (int gx = 0; gx < gridW; gx++)
            {
                double px0 = x0 + (gx * (double)boxW) / gridW;
                double px1 = x0 + ((gx + 1) * (double)boxW) / gridW;
                int ix0 = Math.Max(x0, (int)Math.Floor(px0));
                int ix1 = Math.Max(ix0, (int)Math.Ceiling(px1) - 1);

                int count = 0;
                int total = 0;
                for (int y = iy0; y <= iy1; y++)
                {
                    for (int x = ix0; x <= ix1; x++)
                    {
                        total++;
                        if (ink[y * stride + x]) count++;
                    }
                }
                grid[gy * gridW + gx] = total > 0 ? (double)count / total : 0.0;
            }
        }
        return grid;
    }

    private static string NormalizeText(string input) =>
        input.Replace("\r\n", "\n", StringComparison.Ordinal)
             .Replace('\r', '\n')
             .Trim();

    private static bool ContainsMeaningfulTokens(string text) =>
        text.Count(char.IsLetterOrDigit) >= 3;

    private static string ParseExpenseDate(string text)
    {
        var iso = IsoDateRegex.Match(text);
        if (iso.Success)
        {
            return $"{iso.Groups[1].Value}-{iso.Groups[2].Value}-{iso.Groups[3].Value}";
        }

        var us = UsDateRegex.Match(text);
        if (us.Success
            && int.TryParse(us.Groups[1].Value, out int m)
            && int.TryParse(us.Groups[2].Value, out int d)
            && int.TryParse(us.Groups[3].Value, out int y))
        {
            return $"{y:D4}-{m:D2}-{d:D2}";
        }

        var named = NamedMonthDateRegex.Match(text);
        if (named.Success && DateTime.TryParse($"{named.Groups[1].Value} {named.Groups[2].Value}, {named.Groups[3].Value}", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
        {
            return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return string.Empty;
    }

    private static decimal ParseGrossAmount(string text)
    {
        var labeledMatches = LabeledTotalRegex.Matches(text);
        if (labeledMatches.Count > 0)
        {
            string val = labeledMatches[^1].Groups[1].Value;
            if (decimal.TryParse(val, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsedLabeled) && parsedLabeled > 0m)
            {
                return parsedLabeled;
            }
        }

        var currencyMatches = CurrencyAmountRegex.Matches(text);
        if (currencyMatches.Count > 0)
        {
            string val = currencyMatches[^1].Groups[1].Value;
            if (decimal.TryParse(val, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsedCurrency) && parsedCurrency > 0m)
            {
                return parsedCurrency;
            }
        }

        var standaloneMatches = StandaloneAmountRegex.Matches(text);
        if (standaloneMatches.Count > 0)
        {
            string val = standaloneMatches[^1].Groups[1].Value;
            if (decimal.TryParse(val, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsedStandalone) && parsedStandalone > 0m)
            {
                return parsedStandalone;
            }
        }

        return 0m;
    }

    private static string ParseMerchantName(string text)
    {
        var explicitMatch = ExplicitMerchantRegex.Match(text);
        if (explicitMatch.Success)
        {
            string candidate = explicitMatch.Groups[1].Value.Trim();
            if (candidate.Length is >= 2 and <= 60)
            {
                return candidate;
            }
        }

        foreach (string rawLine in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim().Trim('-', '=', '*', '#').Trim();
            if (line.Length is < 2 or > 60)
            {
                continue;
            }

            if (IgnoredHeaderPhrases.Any(h => line.Equals(h, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (line.StartsWith("DATE", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("TOTAL", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("SUBTOTAL", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("AMOUNT", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("FARE", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("CARD", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("CATEGORY", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsoDateRegex.IsMatch(line) && line.Length <= 12)
            {
                continue;
            }

            if (Regex.IsMatch(line, @"^\$?\d+\.\d{2}$"))
            {
                continue;
            }

            int letters = line.Count(char.IsLetter);
            if (letters >= 2)
            {
                return line;
            }
        }

        return string.Empty;
    }

    private static string InferCategory(string text, string merchantName, string? categoryHint)
    {
        string combined = $"{merchantName}\n{text}".ToLowerInvariant();

        if (ContainsAny(combined, ["mileage", "odometer", "irs mileage"]))
        {
            return "MILEAGE";
        }

        if (ContainsAny(combined, ["shell", "chevron", "exxon", "mobil", "texaco", "sunoco", "valero", "citgo", "arco", "fuel", "gasoline", "unleaded", "diesel", "gallons", "pump", "petrol", "gas station"]))
        {
            return "FUEL";
        }

        if (ContainsAny(combined, ["parking", "garage", "valet", "parkwhiz", "spothero", "impark", "meter"]))
        {
            return "PARKING";
        }

        if (ContainsAny(combined, ["cafe", "coffee", "bistro", "restaurant", "diner", "grill", "bakery", "kitchen", "lunch", "dinner", "breakfast", "meal", "burger", "pizza", "deli", "starbucks", "panera", "catering"]))
        {
            return "MEAL";
        }

        if (ContainsAny(combined, ["hotel", "inn", "suites", "marriott", "hilton", "hyatt", "lodging", "motel", "room charge", "nightly"]))
        {
            return "LODGING";
        }

        if (ContainsAny(combined, ["transit", "metro", "subway", "rail", "train", "amtrak", "bus", "fare", "uber", "lyft", "taxi", "cab", "shuttle", "toll", "turnpike"]))
        {
            return "TRANSIT";
        }

        if (!string.IsNullOrWhiteSpace(categoryHint))
        {
            return categoryHint.Trim().ToUpperInvariant();
        }

        return "OTHER";
    }

    private static bool ContainsAny(string haystack, string[] needles) =>
        needles.Any(needle => haystack.Contains(needle, StringComparison.Ordinal));

    private static byte[] ApplyBoxBlur(byte[] src, int width, int height, int radius)
    {
        byte[] dst = new byte[src.Length];
        for (int y = 0; y < height; y++)
        {
            int y0 = Math.Max(0, y - radius);
            int y1 = Math.Min(height - 1, y + radius);
            for (int x = 0; x < width; x++)
            {
                int x0 = Math.Max(0, x - radius);
                int x1 = Math.Min(width - 1, x + radius);
                int sum = 0;
                int count = 0;
                for (int yy = y0; yy <= y1; yy++)
                {
                    int row = yy * width;
                    for (int xx = x0; xx <= x1; xx++)
                    {
                        sum += src[row + xx];
                        count++;
                    }
                }
                // Compress contrast toward mid-gray as blur increases to model out-of-focus camera capture
                int avg = sum / Math.Max(1, count);
                int washed = (avg * 2 + 195) / 3;
                dst[y * width + x] = (byte)Math.Clamp(washed, 0, 255);
            }
        }
        return dst;
    }

    private static byte[] EncodeGrayscalePng(byte[] gray, int width, int height)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[..4], (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.Slice(4, 4), (uint)height);
        ihdr[8] = 8; // bit depth 8
        ihdr[9] = 0; // grayscale
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        WritePngChunk(ms, "IHDR"u8, ihdr);

        byte[] scanlines = new byte[(width + 1) * height];
        for (int y = 0; y < height; y++)
        {
            scanlines[y * (width + 1)] = 0; // filter None
            Buffer.BlockCopy(gray, y * width, scanlines, y * (width + 1) + 1, width);
        }

        using var idatCompressed = new MemoryStream();
        using (var zlib = new ZLibStream(idatCompressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(scanlines);
        }
        WritePngChunk(ms, "IDAT"u8, idatCompressed.ToArray());
        WritePngChunk(ms, "IEND"u8, []);

        return ms.ToArray();
    }

    private static void WritePngChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> lenBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lenBytes, (uint)data.Length);
        stream.Write(lenBytes);
        stream.Write(type);
        if (!data.IsEmpty)
        {
            stream.Write(data);
        }

        uint crc = ComputePngCrc32(type, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static uint ComputePngCrc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        crc = UpdateCrc32(crc, type);
        crc = UpdateCrc32(crc, data);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint UpdateCrc32(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }
        return crc;
    }

    private static List<(char Ch, double[] Grid, double Aspect)> BuildNormalizedTemplates()
    {
        var list = new List<(char, double[], double)>();
        const int scale = 4;
        foreach (var (ch, rows) in CanonicalFont5x7)
        {
            int w = GlyphWidth * scale;
            int h = GlyphHeight * scale;
            bool[] bmp = new bool[w * h];
            int minX = w, maxX = -1, minY = h, maxY = -1;

            for (int gy = 0; gy < GlyphHeight; gy++)
            {
                for (int gx = 0; gx < GlyphWidth; gx++)
                {
                    if (rows[gy][gx] != '#') continue;
                    for (int sy = 0; sy < scale; sy++)
                    {
                        for (int sx = 0; sx < scale; sx++)
                        {
                            int px = gx * scale + sx;
                            int py = gy * scale + sy;
                            bmp[py * w + px] = true;
                            if (px < minX) minX = px;
                            if (px > maxX) maxX = px;
                            if (py < minY) minY = py;
                            if (py > maxY) maxY = py;
                        }
                    }
                }
            }

            if (maxX < minX || maxY < minY)
            {
                continue;
            }

            int bw = maxX - minX + 1;
            int bh = maxY - minY + 1;
            double[] grid = SampleNormalizedGrid(bmp, w, minX, minY, bw, bh, 10, 14);
            list.Add((ch, grid, (double)bw / Math.Max(1, bh)));
        }
        return list;
    }

    private static Dictionary<char, string[]> BuildCanonicalFont5x7() => new()
    {
        ['A'] = [" ### ", "#   #", "#   #", "#####", "#   #", "#   #", "#   #"],
        ['B'] = ["#### ", "#   #", "#   #", "#### ", "#   #", "#   #", "#### "],
        ['C'] = [" ####", "#    ", "#    ", "#    ", "#    ", "#    ", " ####"],
        ['D'] = ["#### ", "#   #", "#   #", "#   #", "#   #", "#   #", "#### "],
        ['E'] = ["#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#####"],
        ['F'] = ["#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#    "],
        ['G'] = [" ####", "#    ", "#    ", "# ###", "#   #", "#   #", " ####"],
        ['H'] = ["#   #", "#   #", "#   #", "#####", "#   #", "#   #", "#   #"],
        ['I'] = [" ### ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### "],
        ['J'] = ["  ###", "   # ", "   # ", "   # ", "#  # ", "#  # ", " ##  "],
        ['K'] = ["#   #", "#  # ", "# #  ", "##   ", "# #  ", "#  # ", "#   #"],
        ['L'] = ["#    ", "#    ", "#    ", "#    ", "#    ", "#    ", "#####"],
        ['M'] = ["#   #", "## ##", "# # #", "#   #", "#   #", "#   #", "#   #"],
        ['N'] = ["#   #", "##  #", "# # #", "#  ##", "#   #", "#   #", "#   #"],
        ['O'] = [" ### ", "#   #", "#   #", "#   #", "#   #", "#   #", " ### "],
        ['P'] = ["#### ", "#   #", "#   #", "#### ", "#    ", "#    ", "#    "],
        ['Q'] = [" ### ", "#   #", "#   #", "#   #", "# # #", "#  # ", " ## #"],
        ['R'] = ["#### ", "#   #", "#   #", "#### ", "# #  ", "#  # ", "#   #"],
        ['S'] = [" ####", "#    ", "#    ", " ### ", "    #", "    #", "#### "],
        ['T'] = ["#####", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  "],
        ['U'] = ["#   #", "#   #", "#   #", "#   #", "#   #", "#   #", " ### "],
        ['V'] = ["#   #", "#   #", "#   #", "#   #", "#   #", " # # ", "  #  "],
        ['W'] = ["#   #", "#   #", "#   #", "#   #", "# # #", "## ##", "#   #"],
        ['X'] = ["#   #", "#   #", " # # ", "  #  ", " # # ", "#   #", "#   #"],
        ['Y'] = ["#   #", "#   #", " # # ", "  #  ", "  #  ", "  #  ", "  #  "],
        ['Z'] = ["#####", "    #", "   # ", "  #  ", " #   ", "#    ", "#####"],
        ['0'] = [" ### ", "#  ##", "# # #", "# # #", "##  #", "#   #", " ### "],
        ['1'] = ["  #  ", " ##  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### "],
        ['2'] = [" ### ", "#   #", "    #", "  ## ", " #   ", "#    ", "#####"],
        ['3'] = ["#### ", "    #", "    #", " ### ", "    #", "    #", "#### "],
        ['4'] = ["#   #", "#   #", "#   #", "#####", "    #", "    #", "    #"],
        ['5'] = ["#####", "#    ", "#    ", "#### ", "    #", "    #", "#### "],
        ['6'] = [" ### ", "#    ", "#    ", "#### ", "#   #", "#   #", " ### "],
        ['7'] = ["#####", "    #", "   # ", "  #  ", " #   ", " #   ", " #   "],
        ['8'] = [" ### ", "#   #", "#   #", " ### ", "#   #", "#   #", " ### "],
        ['9'] = [" ### ", "#   #", "#   #", " ####", "    #", "    #", " ### "],
        ['$'] = ["  #  ", " ####", "# #  ", " ### ", "  # #", "#### ", "  #  "],
        ['.'] = ["     ", "     ", "     ", "     ", "     ", " ##  ", " ##  "],
        ['-'] = ["     ", "     ", "     ", "#####", "     ", "     ", "     "],
        ['/'] = ["    #", "    #", "   # ", "  #  ", " #   ", "#    ", "#    "],
        [':'] = ["     ", " ##  ", " ##  ", "     ", " ##  ", " ##  ", "     "],
        ['&'] = [" ##  ", "#  # ", " ##  ", "### #", "#  # ", "#  # ", " ### "]
    };
}
