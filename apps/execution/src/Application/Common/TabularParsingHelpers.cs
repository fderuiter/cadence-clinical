using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Cadence.Apps.Execution.Application.Common;

/// <summary>
/// Archive, decompression ratio, and XML complexity quotas for OpenXML spreadsheet parsing.
/// </summary>
public sealed record TabularArchiveQuotas(
    int MaxEntryCount = TabularArchiveQuotas.DefaultMaxEntryCount,
    long MaxEntryUncompressedBytes = TabularArchiveQuotas.DefaultMaxEntryUncompressedBytes,
    long MaxTotalUncompressedBytes = TabularArchiveQuotas.DefaultMaxTotalUncompressedBytes,
    int MaxCompressionRatio = TabularArchiveQuotas.DefaultMaxCompressionRatio,
    long EntryRatioMinUncompressedBytes = TabularArchiveQuotas.DefaultEntryRatioMinUncompressedBytes,
    long ArchiveRatioMinUncompressedBytes = TabularArchiveQuotas.DefaultArchiveRatioMinUncompressedBytes,
    long MaxCompressedArchiveBytes = TabularArchiveQuotas.DefaultMaxCompressedArchiveBytes,
    long MaxCharactersInDocument = TabularArchiveQuotas.DefaultMaxCharactersInDocument,
    int MaxSharedStrings = TabularArchiveQuotas.DefaultMaxSharedStrings,
    int MaxWorksheetRows = TabularArchiveQuotas.DefaultMaxWorksheetRows)
{
    public const int DefaultMaxEntryCount = 256;
    public const long DefaultMaxEntryUncompressedBytes = 32L * 1024 * 1024; // 32 MiB
    public const long DefaultMaxTotalUncompressedBytes = 64L * 1024 * 1024; // 64 MiB
    public const int DefaultMaxCompressionRatio = 100;
    public const long DefaultEntryRatioMinUncompressedBytes = 1_024L; // 1 KiB
    public const long DefaultArchiveRatioMinUncompressedBytes = 4_096L; // 4 KiB
    public const long DefaultMaxCompressedArchiveBytes = 50L * 1024 * 1024; // 50 MiB
    public const long DefaultMaxCharactersInDocument = 32L * 1024 * 1024; // 32M characters
    public const int DefaultMaxSharedStrings = 262_144;
    public const int DefaultMaxWorksheetRows = 65_536;

    public static TabularArchiveQuotas Default { get; } = new();
}

internal sealed class TabularQuotaTracker
{
    private readonly TabularArchiveQuotas _quotas;
    private readonly long? _compressedArchiveBytes;
    private int _entriesOpened;
    private long _totalUncompressedBytes;

    public TabularQuotaTracker(TabularArchiveQuotas quotas, long? compressedArchiveBytes = null)
    {
        ArgumentNullException.ThrowIfNull(quotas);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quotas.MaxEntryCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quotas.MaxEntryUncompressedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quotas.MaxTotalUncompressedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quotas.MaxCompressionRatio);
        ArgumentOutOfRangeException.ThrowIfNegative(quotas.EntryRatioMinUncompressedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(quotas.ArchiveRatioMinUncompressedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quotas.MaxCompressedArchiveBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quotas.MaxCharactersInDocument);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quotas.MaxSharedStrings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quotas.MaxWorksheetRows);

        _quotas = quotas;
        _compressedArchiveBytes = compressedArchiveBytes;
    }

    public TabularArchiveQuotas Quotas => _quotas;

    public void ValidateCompressedArchiveLength(long compressedBytes)
    {
        if (compressedBytes > _quotas.MaxCompressedArchiveBytes)
        {
            throw new InvalidOperationException(
                $"XLSX archive compressed size ({compressedBytes} bytes) exceeds maximum limit of {_quotas.MaxCompressedArchiveBytes} bytes.");
        }
    }

    public void ValidateDeclaredArchiveEntryCount(int entryCount)
    {
        if (entryCount > _quotas.MaxEntryCount)
        {
            throw new InvalidOperationException(
                $"XLSX archive entry count ({entryCount}) exceeds maximum limit of {_quotas.MaxEntryCount}.");
        }
    }

    public Stream OpenBoundedEntryStream(ZipArchiveEntry entry)
    {
        int updatedEntries = Interlocked.Increment(ref _entriesOpened);
        if (updatedEntries > _quotas.MaxEntryCount)
        {
            throw new InvalidOperationException(
                $"XLSX archive exceeds maximum opened entry limit of {_quotas.MaxEntryCount}.");
        }

        long effectiveEntryCompressedBytes = ResolveEffectiveEntryCompressedBytes(entry.CompressedLength);

        long declaredLength = entry.Length;
        if (declaredLength > _quotas.MaxEntryUncompressedBytes)
        {
            throw new InvalidOperationException(
                $"ZIP entry '{entry.FullName}' declared length ({declaredLength} bytes) exceeds maximum per-entry limit of {_quotas.MaxEntryUncompressedBytes} bytes.");
        }

        long uncompressedBefore = Interlocked.Read(ref _totalUncompressedBytes);
        if (declaredLength > 0 && uncompressedBefore + declaredLength > _quotas.MaxTotalUncompressedBytes)
        {
            throw new InvalidOperationException(
                $"XLSX archive declared payload exceeds maximum total decompressed size limit of {_quotas.MaxTotalUncompressedBytes} bytes.");
        }

        EnforceExpansionRatio(
            entry.FullName,
            declaredLength,
            effectiveEntryCompressedBytes,
            uncompressedBefore + Math.Max(0L, declaredLength));

        Stream rawStream = entry.Open();
        return new TabularQuotaReadStream(rawStream, entry.FullName, effectiveEntryCompressedBytes, this);
    }

    public void RecordBytesRead(string entryName, long entryBytesRead, int deltaBytes, long effectiveEntryCompressedBytes)
    {
        if (entryBytesRead > _quotas.MaxEntryUncompressedBytes)
        {
            throw new InvalidOperationException(
                $"ZIP entry '{entryName}' exceeds maximum uncompressed size limit of {_quotas.MaxEntryUncompressedBytes} bytes.");
        }

        long totalUncompressed = Interlocked.Add(ref _totalUncompressedBytes, deltaBytes);
        if (totalUncompressed > _quotas.MaxTotalUncompressedBytes)
        {
            throw new InvalidOperationException(
                $"XLSX archive exceeds maximum total decompressed size limit of {_quotas.MaxTotalUncompressedBytes} bytes.");
        }

        EnforceExpansionRatio(entryName, entryBytesRead, effectiveEntryCompressedBytes, totalUncompressed);
    }

    private void EnforceExpansionRatio(
        string entryName,
        long entryUncompressedBytes,
        long effectiveEntryCompressedBytes,
        long archiveUncompressedBytes)
    {
        if (entryUncompressedBytes > _quotas.EntryRatioMinUncompressedBytes
            && (Int128)entryUncompressedBytes > (Int128)effectiveEntryCompressedBytes * _quotas.MaxCompressionRatio)
        {
            throw new InvalidOperationException(
                $"ZIP entry '{entryName}' exceeds maximum {_quotas.MaxCompressionRatio}:1 decompression ratio limit.");
        }

        if (_compressedArchiveBytes is > 0L and var archiveCompressed
            && archiveUncompressedBytes > _quotas.ArchiveRatioMinUncompressedBytes
            && (Int128)archiveUncompressedBytes > (Int128)archiveCompressed * _quotas.MaxCompressionRatio)
        {
            throw new InvalidOperationException(
                $"XLSX archive exceeds maximum {_quotas.MaxCompressionRatio}:1 decompression ratio limit.");
        }
    }

    private long ResolveEffectiveEntryCompressedBytes(long rawCompressedLength)
    {
        if (_compressedArchiveBytes is not (> 0L and var archiveBytes))
        {
            return Math.Max(1L, rawCompressedLength);
        }

        return rawCompressedLength > 0L ? Math.Min(rawCompressedLength, archiveBytes) : 1L;
    }
}

internal sealed class TabularQuotaReadStream(
    Stream inner,
    string entryName,
    long effectiveEntryCompressedBytes,
    TabularQuotaTracker tracker) : Stream
{
    private long _entryBytesRead;

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

        int bytesRead;
        try
        {
            bytesRead = inner.Read(buffer);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException(
                $"Invalid XLSX compressed stream for '{entryName}': {ex.Message}", ex);
        }

        if (bytesRead > 0)
        {
            _entryBytesRead += bytesRead;
            tracker.RecordBytesRead(entryName, _entryBytesRead, bytesRead, effectiveEntryCompressedBytes);
        }

        return bytesRead;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        int bytesRead;
        try
        {
            bytesRead = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException(
                $"Invalid XLSX compressed stream for '{entryName}': {ex.Message}", ex);
        }

        if (bytesRead > 0)
        {
            _entryBytesRead += bytesRead;
            tracker.RecordBytesRead(entryName, _entryBytesRead, bytesRead, effectiveEntryCompressedBytes);
        }

        return bytesRead;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Shared parsing helpers for CSV and OpenXML Excel spreadsheets.
/// </summary>
public static class TabularParsingHelpers
{
    public static readonly XNamespace SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"; // deid-ignore

    /// <summary>
    /// Maximum 0-based column index permitted by OpenXML SpreadsheetML (column <c>XFD</c>, 16,384th column).
    /// </summary>
    public const int MaxColumnIndex = 16_383;

    /// <summary>
    /// Opens a <see cref="ZipArchive"/> for reading while enforcing archive size and entry-count quotas.
    /// </summary>
    public static ZipArchive OpenArchive(Stream stream, TabularArchiveQuotas? quotas = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        long? compressedBytes = stream.CanSeek ? Math.Max(0L, stream.Length - stream.Position) : null;
        var tracker = new TabularQuotaTracker(quotas ?? TabularArchiveQuotas.Default, compressedBytes);
        return OpenArchive(stream, tracker);
    }

    internal static ZipArchive OpenArchive(Stream stream, TabularQuotaTracker quotaTracker)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(quotaTracker);

        if (stream.CanSeek)
        {
            quotaTracker.ValidateCompressedArchiveLength(Math.Max(0L, stream.Length - stream.Position));
        }

        ZipArchive? zip = null;
        try
        {
            zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            quotaTracker.ValidateDeclaredArchiveEntryCount(zip.Entries.Count);
            return zip;
        }
        catch (InvalidDataException ex)
        {
            zip?.Dispose();
            throw new InvalidOperationException($"Invalid XLSX archive: {ex.Message}", ex);
        }
        catch
        {
            zip?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Loads shared strings table from an OpenXML ZIP archive.
    /// </summary>
    public static List<string> LoadSharedStrings(ZipArchive zip, XNamespace? ns = null) =>
        LoadSharedStrings(zip, ns, TabularArchiveQuotas.Default);

    /// <summary>
    /// Loads shared strings table from an OpenXML ZIP archive using explicit archive and XML quotas.
    /// </summary>
    public static List<string> LoadSharedStrings(ZipArchive zip, XNamespace? ns, TabularArchiveQuotas? quotas)
    {
        ArgumentNullException.ThrowIfNull(zip);
        var tracker = new TabularQuotaTracker(quotas ?? TabularArchiveQuotas.Default);
        return LoadSharedStrings(zip, tracker, ns);
    }

    internal static List<string> LoadSharedStrings(ZipArchive zip, TabularQuotaTracker quotaTracker, XNamespace? ns = null)
    {
        ArgumentNullException.ThrowIfNull(zip);
        ArgumentNullException.ThrowIfNull(quotaTracker);
        XNamespace targetNs = ns ?? SpreadsheetNamespace;

        try
        {
            quotaTracker.ValidateDeclaredArchiveEntryCount(zip.Entries.Count);

            var sharedStrings = new List<string>();
            var sharedStringsEntry = zip.GetEntry("xl/sharedStrings.xml");
            if (sharedStringsEntry == null)
            {
                return sharedStrings;
            }

            using var entryStream = quotaTracker.OpenBoundedEntryStream(sharedStringsEntry);
            using var reader = XmlReader.Create(entryStream, CreateSafeXmlReaderSettings(quotaTracker.Quotas));

            reader.MoveToContent();
            while (!reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.Element
                    && reader.LocalName == "si"
                    && reader.NamespaceURI == targetNs.NamespaceName)
                {
                    if (sharedStrings.Count >= quotaTracker.Quotas.MaxSharedStrings)
                    {
                        throw new InvalidOperationException(
                            $"Shared strings table exceeds maximum limit of {quotaTracker.Quotas.MaxSharedStrings} entries.");
                    }

                    if (XNode.ReadFrom(reader) is XElement si)
                    {
                        var textNodes = si.Descendants(targetNs + "t").Select(t => t.Value);
                        sharedStrings.Add(string.Concat(textNodes));
                    }

                    continue;
                }

                reader.Read();
            }

            return sharedStrings;
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException)
        {
            throw new InvalidOperationException($"Invalid XLSX shared strings XML: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Loads worksheet row elements from the primary worksheet in an OpenXML ZIP archive.
    /// </summary>
    public static List<XElement> LoadWorksheetRows(ZipArchive zip, XNamespace? ns = null) =>
        LoadWorksheetRows(zip, ns, TabularArchiveQuotas.Default);

    /// <summary>
    /// Loads worksheet row elements from the primary worksheet in an OpenXML ZIP archive with an explicit row cap.
    /// </summary>
    public static List<XElement> LoadWorksheetRows(ZipArchive zip, int maxRows, XNamespace? ns = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRows);
        return LoadWorksheetRows(zip, ns, TabularArchiveQuotas.Default with { MaxWorksheetRows = maxRows });
    }

    /// <summary>
    /// Loads worksheet row elements from the primary worksheet in an OpenXML ZIP archive using explicit archive and XML quotas.
    /// </summary>
    public static List<XElement> LoadWorksheetRows(ZipArchive zip, XNamespace? ns, TabularArchiveQuotas? quotas)
    {
        ArgumentNullException.ThrowIfNull(zip);
        var tracker = new TabularQuotaTracker(quotas ?? TabularArchiveQuotas.Default);
        return LoadWorksheetRows(zip, tracker, ns);
    }

    internal static List<XElement> LoadWorksheetRows(ZipArchive zip, TabularQuotaTracker quotaTracker, XNamespace? ns = null)
    {
        ArgumentNullException.ThrowIfNull(zip);
        ArgumentNullException.ThrowIfNull(quotaTracker);
        XNamespace targetNs = ns ?? SpreadsheetNamespace;

        ZipArchiveEntry? sheetEntry;
        try
        {
            quotaTracker.ValidateDeclaredArchiveEntryCount(zip.Entries.Count);
            sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml")
                ?? zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase));
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException($"Invalid XLSX archive structure: {ex.Message}", ex);
        }

        if (sheetEntry == null)
        {
            throw new InvalidOperationException("Invalid XLSX format: missing worksheet data.");
        }

        try
        {
            var rows = new List<XElement>();
            bool processedSheetData = false;

            using var sheetStream = quotaTracker.OpenBoundedEntryStream(sheetEntry);
            using var reader = XmlReader.Create(sheetStream, CreateSafeXmlReaderSettings(quotaTracker.Quotas));

            reader.MoveToContent();
            while (!reader.EOF)
            {
                if (!processedSheetData
                    && reader.NodeType == XmlNodeType.Element
                    && reader.LocalName == "sheetData"
                    && reader.NamespaceURI == targetNs.NamespaceName)
                {
                    processedSheetData = true;
                    if (reader.IsEmptyElement)
                    {
                        reader.Read();
                        continue;
                    }

                    reader.Read();
                    while (!reader.EOF)
                    {
                        if (reader.NodeType == XmlNodeType.EndElement
                            && reader.LocalName == "sheetData"
                            && reader.NamespaceURI == targetNs.NamespaceName)
                        {
                            reader.Read();
                            break;
                        }

                        if (reader.NodeType == XmlNodeType.Element)
                        {
                            if (reader.LocalName == "row" && reader.NamespaceURI == targetNs.NamespaceName)
                            {
                                if (rows.Count >= quotaTracker.Quotas.MaxWorksheetRows)
                                {
                                    throw new InvalidOperationException(
                                        $"Worksheet exceeds maximum limit of {quotaTracker.Quotas.MaxWorksheetRows} row elements.");
                                }

                                if (XNode.ReadFrom(reader) is XElement rowElement)
                                {
                                    rows.Add(rowElement);
                                }

                                continue;
                            }

                            reader.Skip();
                            continue;
                        }

                        reader.Read();
                    }

                    continue;
                }

                reader.Read();
            }

            return rows;
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException)
        {
            throw new InvalidOperationException($"Invalid XLSX worksheet XML: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Extracts column cell values from an OpenXML row element, padding sparse columns with empty strings.
    /// </summary>
    public static List<string> ExtractRowValues(XElement xmlRow, XNamespace sns, List<string> sharedStrings) =>
        ExtractRowValues(xmlRow, sns, sharedStrings, MaxColumnIndex);

    /// <summary>
    /// Extracts column cell values from an OpenXML row element up to <paramref name="maxColumnIndex"/>, padding sparse columns with empty strings.
    /// </summary>
    public static List<string> ExtractRowValues(XElement xmlRow, XNamespace sns, List<string> sharedStrings, int maxColumnIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxColumnIndex);
        int effectiveMaxColumnIndex = Math.Min(maxColumnIndex, MaxColumnIndex);

        var values = new List<string>();
        if (xmlRow == null) return values;

        int currentColIndex = 0;

        foreach (var c in xmlRow.Elements(sns + "c"))
        {
            string? r = c.Attribute("r")?.Value;
            int colIndex = ColumnNameToIndex(r!, effectiveMaxColumnIndex);
            if (colIndex < currentColIndex)
            {
                throw new InvalidOperationException(
                    $"Invalid cell reference '{r}': cell coordinates must appear in strictly increasing column order.");
            }

            while (currentColIndex < colIndex)
            {
                values.Add(string.Empty);
                currentColIndex++;
            }

            string t = c.Attribute("t")?.Value ?? string.Empty;
            string val = c.Element(sns + "v")?.Value ?? string.Empty;

            if (t == "s" && int.TryParse(val, out int sharedIndex) && sharedStrings != null && sharedIndex >= 0 && sharedIndex < sharedStrings.Count)
            {
                val = sharedStrings[sharedIndex];
            }
            else if (t == "inlineStr")
            {
                val = c.Element(sns + "is")?.Element(sns + "t")?.Value ?? string.Empty;
            }

            values.Add(val);
            currentColIndex++;
        }

        return values;
    }

    /// <summary>
    /// Extracts column cell values from an OpenXML row element using the default SpreadsheetML namespace.
    /// </summary>
    public static List<string> ExtractRowValues(XElement xmlRow, List<string> sharedStrings) =>
        ExtractRowValues(xmlRow, SpreadsheetNamespace, sharedStrings, MaxColumnIndex);

    /// <summary>
    /// Extracts column cell values from an OpenXML row element using the default SpreadsheetML namespace up to <paramref name="maxColumnIndex"/>.
    /// </summary>
    public static List<string> ExtractRowValues(XElement xmlRow, List<string> sharedStrings, int maxColumnIndex) =>
        ExtractRowValues(xmlRow, SpreadsheetNamespace, sharedStrings, maxColumnIndex);

    /// <summary>
    /// Converts an Excel column name or cell reference (e.g., "A", "C12", "AA3") to a 0-based column index.
    /// </summary>
    public static int ColumnNameToIndex(string cellRef) =>
        ColumnNameToIndex(cellRef, MaxColumnIndex);

    /// <summary>
    /// Converts an Excel column name or cell reference (e.g., "A", "C12", "AA3") to a 0-based column index bounded by <paramref name="maxColumnIndex"/> (at most OpenXML <c>XFD</c>, 16,383).
    /// </summary>
    public static int ColumnNameToIndex(string cellRef, int maxColumnIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxColumnIndex);
        int effectiveMaxColumnIndex = Math.Min(maxColumnIndex, MaxColumnIndex);

        if (string.IsNullOrWhiteSpace(cellRef))
        {
            throw new InvalidOperationException("Invalid cell reference: cell coordinates cannot be null or empty.");
        }

        int letterCount = 0;
        while (letterCount < cellRef.Length && char.IsAsciiLetter(cellRef[letterCount]))
        {
            letterCount++;
        }

        if (letterCount == 0)
        {
            throw new InvalidOperationException($"Invalid cell reference '{cellRef}': missing column coordinates.");
        }

        if (letterCount < cellRef.Length)
        {
            if (cellRef[letterCount] == '0')
            {
                throw new InvalidOperationException($"Invalid cell reference '{cellRef}': row coordinate must be 1-based.");
            }

            for (int i = letterCount; i < cellRef.Length; i++)
            {
                if (!char.IsAsciiDigit(cellRef[i]))
                {
                    throw new InvalidOperationException($"Invalid cell reference '{cellRef}': malformed cell coordinates.");
                }
            }
        }

        int sum = 0;
        try
        {
            for (int i = 0; i < letterCount; i++)
            {
                char upper = char.ToUpperInvariant(cellRef[i]);
                int digit = upper - 'A' + 1;
                sum = checked((sum * 26) + digit);
                if (checked(sum - 1) > effectiveMaxColumnIndex)
                {
                    throw new InvalidOperationException(
                        $"Cell reference '{cellRef}' exceeds the maximum allowed column index of {effectiveMaxColumnIndex}.");
                }
            }

            return checked(sum - 1);
        }
        catch (OverflowException ex)
        {
            throw new InvalidOperationException(
                $"Cell reference '{cellRef}' overflows column index bounds.", ex);
        }
    }

    private static XmlReaderSettings CreateSafeXmlReaderSettings(TabularArchiveQuotas quotas) =>
        new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = quotas.MaxCharactersInDocument,
            MaxCharactersFromEntities = 1_024,
            CloseInput = false,
        };

    /// <summary>
    /// Splits a CSV line into tokens, respecting double-quote escapes ("") and commas within quoted strings.
    /// </summary>
    public static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        if (line == null) return result;

        var sb = new StringBuilder();
        bool inQuotes = false;
        int i = 0;

        while (i < line.Length)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i += 2;
                    continue;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
            i++;
        }
        result.Add(sb.ToString().Trim());
        return result;
    }

    /// <summary>
    /// Safely gets a trimmed string value at the specified index or returns empty string if index is out of bounds.
    /// </summary>
    public static string GetValue(IReadOnlyList<string>? values, int index)
    {
        if (values == null || index < 0 || index >= values.Count) return string.Empty;
        return values[index]?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Safely gets a trimmed string value at the specified index or returns empty string if index is out of bounds.
    /// </summary>
    public static string GetValue(List<string>? values, int index)
    {
        return GetValue((IReadOnlyList<string>?)values, index);
    }
}
