using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Cadence.Apps.Execution.Application.Coding;

public static class SynonymFileParser
{
    public const long MaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB
    public const int MaxRowCount = 50000;

    public static List<SynonymRowDto> Parse(byte[] fileBytes, string fileName)
    {
        if (fileBytes == null || fileBytes.Length == 0)
        {
            throw new ArgumentException("Uploaded file is empty.", nameof(fileBytes));
        }

        if (fileBytes.Length > MaxFileSizeBytes)
        {
            throw new InvalidOperationException($"File size exceeds the maximum limit of 50 MB ({MaxFileSizeBytes} bytes).");
        }

        string ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (ext == ".xlsx")
        {
            return ParseXlsx(fileBytes);
        }
        else if (ext == ".csv" || ext == ".txt")
        {
            return ParseCsv(fileBytes);
        }
        else
        {
            // Default attempt CSV or XLSX by header bytes
            if (fileBytes.Length > 4 && fileBytes[0] == 0x50 && fileBytes[1] == 0x4B && fileBytes[2] == 0x03 && fileBytes[3] == 0x04)
            {
                return ParseXlsx(fileBytes);
            }
            return ParseCsv(fileBytes);
        }
    }

    private static List<SynonymRowDto> ParseCsv(byte[] fileBytes)
    {
        using var stream = new MemoryStream(fileBytes);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var rows = new List<SynonymRowDto>();
        string? headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            return rows;
        }

        List<string> headers = SplitCsvLine(headerLine);
        var colIndices = MapHeaders(headers);

        int rowNumber = 1; // 1-indexed (data rows start at row 2)
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            rowNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (rows.Count >= MaxRowCount)
            {
                throw new InvalidOperationException($"File exceeds the maximum row limit of {MaxRowCount} rows.");
            }

            List<string> values = SplitCsvLine(line);
            string verbatim = GetValue(values, colIndices.VerbatimIndex);
            string dictType = GetValue(values, colIndices.DictionaryTypeIndex);
            string version = GetValue(values, colIndices.VersionIndex);
            string code = GetValue(values, colIndices.CodeIndex);
            string term = GetValue(values, colIndices.TermIndex);
            string? level = colIndices.LevelIndex >= 0 ? GetValue(values, colIndices.LevelIndex) : null;

            rows.Add(new SynonymRowDto(
                RowNumber: rowNumber,
                VerbatimText: verbatim,
                DictionaryType: dictType,
                DictionaryVersion: version,
                TargetCode: code,
                TargetTerm: term,
                TargetLevel: level));
        }

        return rows;
    }

    private static List<SynonymRowDto> ParseXlsx(byte[] fileBytes)
    {
        using var stream = new MemoryStream(fileBytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        // 1. Read shared strings
        var sharedStrings = new List<string>();
        var sharedStringsEntry = zip.GetEntry("xl/sharedStrings.xml");
        if (sharedStringsEntry != null)
        {
            using var entryStream = sharedStringsEntry.Open();
            var xdoc = XDocument.Load(entryStream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            foreach (var si in xdoc.Descendants(ns + "si"))
            {
                var textNodes = si.Descendants(ns + "t").Select(t => t.Value);
                sharedStrings.Add(string.Concat(textNodes));
            }
        }

        // 2. Read sheet1.xml
        var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml") ?? zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase));
        if (sheetEntry == null)
        {
            throw new InvalidOperationException("Invalid XLSX format: missing worksheet data.");
        }

        using var sheetStream = sheetEntry.Open();
        var sheetDoc = XDocument.Load(sheetStream);
        XNamespace sns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var sheetData = sheetDoc.Descendants(sns + "sheetData").FirstOrDefault();
        if (sheetData == null)
        {
            return new List<SynonymRowDto>();
        }

        var xmlRows = sheetData.Elements(sns + "row").ToList();
        if (xmlRows.Count == 0)
        {
            return new List<SynonymRowDto>();
        }

        // Row 1: Header
        var headerRow = xmlRows[0];
        List<string> headers = ExtractRowValues(headerRow, sns, sharedStrings);
        var colIndices = MapHeaders(headers);

        var resultRows = new List<SynonymRowDto>();
        int rowNumber = 1;

        for (int i = 1; i < xmlRows.Count; i++)
        {
            rowNumber++;
            if (resultRows.Count >= MaxRowCount)
            {
                throw new InvalidOperationException($"File exceeds the maximum row limit of {MaxRowCount} rows.");
            }

            var xmlRow = xmlRows[i];
            List<string> values = ExtractRowValues(xmlRow, sns, sharedStrings);

            string verbatim = GetValue(values, colIndices.VerbatimIndex);
            string dictType = GetValue(values, colIndices.DictionaryTypeIndex);
            string version = GetValue(values, colIndices.VersionIndex);
            string code = GetValue(values, colIndices.CodeIndex);
            string term = GetValue(values, colIndices.TermIndex);
            string? level = colIndices.LevelIndex >= 0 ? GetValue(values, colIndices.LevelIndex) : null;

            if (string.IsNullOrWhiteSpace(verbatim) && string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(dictType))
            {
                continue; // empty row
            }

            resultRows.Add(new SynonymRowDto(
                RowNumber: rowNumber,
                VerbatimText: verbatim,
                DictionaryType: dictType,
                DictionaryVersion: version,
                TargetCode: code,
                TargetTerm: term,
                TargetLevel: level));
        }

        return resultRows;
    }

    private static List<string> ExtractRowValues(XElement xmlRow, XNamespace sns, List<string> sharedStrings)
    {
        var values = new List<string>();
        int currentColIndex = 0;

        foreach (var c in xmlRow.Elements(sns + "c"))
        {
            string r = c.Attribute("r")?.Value ?? string.Empty; // e.g. "B2"
            int colIndex = ColumnNameToIndex(r);
            while (currentColIndex < colIndex)
            {
                values.Add(string.Empty);
                currentColIndex++;
            }

            string t = c.Attribute("t")?.Value ?? string.Empty;
            string val = c.Element(sns + "v")?.Value ?? string.Empty;

            if (t == "s" && int.TryParse(val, out int sharedIndex) && sharedIndex >= 0 && sharedIndex < sharedStrings.Count)
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

    private static int ColumnNameToIndex(string cellRef)
    {
        if (string.IsNullOrEmpty(cellRef)) return 0;
        string colName = Regex.Match(cellRef, @"^[A-Za-z]+").Value.ToUpperInvariant();
        int sum = 0;
        foreach (char ch in colName)
        {
            sum *= 26;
            sum += (ch - 'A' + 1);
        }
        return Math.Max(0, sum - 1);
    }

    private static List<string> SplitCsvLine(string line)
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

    private sealed record ColumnIndices(
        int VerbatimIndex,
        int DictionaryTypeIndex,
        int VersionIndex,
        int CodeIndex,
        int TermIndex,
        int LevelIndex);

    private static ColumnIndices MapHeaders(List<string> headers)
    {
        int verbatimIdx = -1;
        int dictIdx = -1;
        int versionIdx = -1;
        int codeIdx = -1;
        int termIdx = -1;
        int levelIdx = -1;

        for (int i = 0; i < headers.Count; i++)
        {
            string h = headers[i].Trim().ToLowerInvariant().Replace("_", " ").Replace("-", " ");
            if (verbatimIdx < 0 && (h.Contains("verbatim") || h.Contains("source term") || h == "term"))
            {
                verbatimIdx = i;
            }
            else if (dictIdx < 0 && (h.Contains("dictionary") || h.Contains("dict") || h.Contains("type")))
            {
                dictIdx = i;
            }
            else if (versionIdx < 0 && (h.Contains("version")))
            {
                versionIdx = i;
            }
            else if (codeIdx < 0 && (h.Contains("code") || h.Contains("target code") || h.Contains("pt code") || h.Contains("llt code")))
            {
                codeIdx = i;
            }
            else if (termIdx < 0 && (h.Contains("target term") || h.Contains("preferred term") || h.Contains("term name") || h.Contains("target name") || h.Contains("pt term") || h.Contains("llt term") || h == "preferred term"))
            {
                termIdx = i;
            }
            else if (levelIdx < 0 && (h.Contains("level")))
            {
                levelIdx = i;
            }
        }

        // Fallbacks by standard column positions if headers didn't match
        if (verbatimIdx < 0) verbatimIdx = 0;
        if (dictIdx < 0) dictIdx = headers.Count > 1 ? 1 : -1;
        if (versionIdx < 0) versionIdx = headers.Count > 2 ? 2 : -1;
        if (codeIdx < 0) codeIdx = headers.Count > 3 ? 3 : -1;
        if (termIdx < 0) termIdx = headers.Count > 4 ? 4 : -1;
        if (levelIdx < 0 && headers.Count > 5) levelIdx = 5;

        return new ColumnIndices(verbatimIdx, dictIdx, versionIdx, codeIdx, termIdx, levelIdx);
    }

    private static string GetValue(List<string> values, int index)
    {
        if (index < 0 || index >= values.Count) return string.Empty;
        return values[index]?.Trim() ?? string.Empty;
    }
}
