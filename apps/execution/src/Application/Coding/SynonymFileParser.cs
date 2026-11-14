using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using Cadence.Apps.Execution.Application.Common;

namespace Cadence.Apps.Execution.Application.Coding;

public static class SynonymFileParser
{
    public const long MaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB
    public const int MaxRowCount = 50000; // deid-ignore

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

        List<string> headers = TabularParsingHelpers.SplitCsvLine(headerLine);
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

            List<string> values = TabularParsingHelpers.SplitCsvLine(line);
            string verbatim = TabularParsingHelpers.GetValue(values, colIndices.VerbatimIndex);
            string dictType = TabularParsingHelpers.GetValue(values, colIndices.DictionaryTypeIndex);
            string version = TabularParsingHelpers.GetValue(values, colIndices.VersionIndex);
            string code = TabularParsingHelpers.GetValue(values, colIndices.CodeIndex);
            string term = TabularParsingHelpers.GetValue(values, colIndices.TermIndex);
            string? level = colIndices.LevelIndex >= 0 ? TabularParsingHelpers.GetValue(values, colIndices.LevelIndex) : null;

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

        var sharedStrings = TabularParsingHelpers.LoadSharedStrings(zip);
        var xmlRows = TabularParsingHelpers.LoadWorksheetRows(zip);
        if (xmlRows.Count == 0)
        {
            return new List<SynonymRowDto>();
        }

        // Row 1: Header
        var headerRow = xmlRows[0];
        List<string> headers = TabularParsingHelpers.ExtractRowValues(headerRow, sharedStrings);
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
            List<string> values = TabularParsingHelpers.ExtractRowValues(xmlRow, sharedStrings);

            string verbatim = TabularParsingHelpers.GetValue(values, colIndices.VerbatimIndex);
            string dictType = TabularParsingHelpers.GetValue(values, colIndices.DictionaryTypeIndex);
            string version = TabularParsingHelpers.GetValue(values, colIndices.VersionIndex);
            string code = TabularParsingHelpers.GetValue(values, colIndices.CodeIndex);
            string term = TabularParsingHelpers.GetValue(values, colIndices.TermIndex);
            string? level = colIndices.LevelIndex >= 0 ? TabularParsingHelpers.GetValue(values, colIndices.LevelIndex) : null;

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
}
