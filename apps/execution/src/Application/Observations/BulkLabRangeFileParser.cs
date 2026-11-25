using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using Cadence.Apps.Execution.Application.Common;

namespace Cadence.Apps.Execution.Application.Observations;

public sealed record ParsedLabRangeRow(
    int RowNumber,
    string? StudyId,
    string TestCode,
    string TestName,
    string Source,
    string? SiteId,
    string Unit,
    string NormalizedUnit,
    string SexApplicability,
    double? AgeLow,
    double? AgeHigh,
    double? LowBound,
    double? HighBound,
    double? CriticalLow,
    double? CriticalHigh,
    string? LoincCode
);

public static class BulkLabRangeFileParser
{
    public const long MaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB
    public const int MaxRowCount = 10000; // deid-ignore
    public const int MaxColumnIndex = 63;
    private const int MaxWorksheetRowElements = MaxRowCount + 1_024;

    public static List<ParsedLabRangeRow> Parse(byte[] fileBytes, string fileName)
    {
        if (fileBytes == null || fileBytes.Length == 0)
        {
            throw new ArgumentException("Uploaded file is empty.", nameof(fileBytes));
        }

        if (fileBytes.Length > MaxFileSizeBytes)
        {
            throw new InvalidOperationException($"File size exceeds maximum limit of 50 MB ({MaxFileSizeBytes} bytes).");
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
            if (fileBytes.Length > 4 && fileBytes[0] == 0x50 && fileBytes[1] == 0x4B && fileBytes[2] == 0x03 && fileBytes[3] == 0x04)
            {
                return ParseXlsx(fileBytes);
            }
            return ParseCsv(fileBytes);
        }
    }

    private static List<ParsedLabRangeRow> ParseCsv(byte[] fileBytes)
    {
        using var stream = new MemoryStream(fileBytes);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var rows = new List<ParsedLabRangeRow>();
        string? headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            return rows;
        }

        List<string> headers = TabularParsingHelpers.SplitCsvLine(headerLine);
        var colIndices = MapHeaders(headers);

        int rowNumber = 1;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            rowNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (rows.Count >= MaxRowCount)
            {
                throw new InvalidOperationException($"File exceeds maximum row limit of {MaxRowCount} rows.");
            }

            List<string> values = TabularParsingHelpers.SplitCsvLine(line);
            var parsed = BuildRow(rowNumber, values, colIndices);
            if (parsed != null)
            {
                rows.Add(parsed);
            }
        }

        return rows;
    }

    private static List<ParsedLabRangeRow> ParseXlsx(byte[] fileBytes)
    {
        using var stream = new MemoryStream(fileBytes, writable: false);
        var quotas = TabularArchiveQuotas.Default with
        {
            MaxCompressedArchiveBytes = MaxFileSizeBytes,
            MaxWorksheetRows = MaxWorksheetRowElements,
        };
        var quotaTracker = new TabularQuotaTracker(quotas, fileBytes.LongLength);
        using var zip = TabularParsingHelpers.OpenArchive(stream, quotaTracker);

        var sharedStrings = TabularParsingHelpers.LoadSharedStrings(zip, quotaTracker);
        var xmlRows = TabularParsingHelpers.LoadWorksheetRows(zip, quotaTracker);
        if (xmlRows.Count == 0)
        {
            return new List<ParsedLabRangeRow>();
        }

        var headerRow = xmlRows[0];
        List<string> headers = TabularParsingHelpers.ExtractRowValues(headerRow, sharedStrings, MaxColumnIndex);
        var colIndices = MapHeaders(headers);

        var resultRows = new List<ParsedLabRangeRow>();
        int rowNumber = 1;

        for (int i = 1; i < xmlRows.Count; i++)
        {
            rowNumber++;
            if (resultRows.Count >= MaxRowCount)
            {
                throw new InvalidOperationException($"File exceeds maximum row limit of {MaxRowCount} rows.");
            }

            var xmlRow = xmlRows[i];
            List<string> values = TabularParsingHelpers.ExtractRowValues(xmlRow, sharedStrings, MaxColumnIndex);

            var parsed = BuildRow(rowNumber, values, colIndices);
            if (parsed != null)
            {
                resultRows.Add(parsed);
            }
        }

        return resultRows;
    }

    private static ParsedLabRangeRow? BuildRow(int rowNumber, List<string> values, ColumnIndices cols)
    {
        string testCode = TabularParsingHelpers.GetValue(values, cols.TestCodeIndex);
        string testName = TabularParsingHelpers.GetValue(values, cols.TestNameIndex);
        string source = TabularParsingHelpers.GetValue(values, cols.SourceIndex);
        string siteId = TabularParsingHelpers.GetValue(values, cols.SiteIdIndex);
        string unit = TabularParsingHelpers.GetValue(values, cols.UnitIndex);
        string normUnit = TabularParsingHelpers.GetValue(values, cols.NormalizedUnitIndex);
        string sex = TabularParsingHelpers.GetValue(values, cols.SexIndex);
        string studyId = TabularParsingHelpers.GetValue(values, cols.StudyIdIndex);
        string loinc = TabularParsingHelpers.GetValue(values, cols.LoincIndex);

        double? ageLow = ParseNullableDouble(TabularParsingHelpers.GetValue(values, cols.AgeLowIndex));
        double? ageHigh = ParseNullableDouble(TabularParsingHelpers.GetValue(values, cols.AgeHighIndex));
        double? lowBound = ParseNullableDouble(TabularParsingHelpers.GetValue(values, cols.LowBoundIndex));
        double? highBound = ParseNullableDouble(TabularParsingHelpers.GetValue(values, cols.HighBoundIndex));
        double? critLow = ParseNullableDouble(TabularParsingHelpers.GetValue(values, cols.CriticalLowIndex));
        double? critHigh = ParseNullableDouble(TabularParsingHelpers.GetValue(values, cols.CriticalHighIndex));

        // Skip completely empty rows
        if (string.IsNullOrWhiteSpace(testCode) && string.IsNullOrWhiteSpace(testName) && string.IsNullOrWhiteSpace(unit) && lowBound == null && highBound == null)
        {
            return null;
        }

        return new ParsedLabRangeRow(
            RowNumber: rowNumber,
            StudyId: string.IsNullOrWhiteSpace(studyId) ? null : studyId,
            TestCode: testCode,
            TestName: string.IsNullOrWhiteSpace(testName) ? testCode : testName,
            Source: string.IsNullOrWhiteSpace(source) ? "CENTRAL" : source.ToUpperInvariant(),
            SiteId: string.IsNullOrWhiteSpace(siteId) ? null : siteId,
            Unit: unit,
            NormalizedUnit: normUnit,
            SexApplicability: string.IsNullOrWhiteSpace(sex) ? "ALL" : sex.ToUpperInvariant(),
            AgeLow: ageLow,
            AgeHigh: ageHigh,
            LowBound: lowBound,
            HighBound: highBound,
            CriticalLow: critLow,
            CriticalHigh: critHigh,
            LoincCode: string.IsNullOrWhiteSpace(loinc) ? null : loinc
        );
    }

    private static double? ParseNullableDouble(string val)
    {
        if (string.IsNullOrWhiteSpace(val)) return null;
        if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out double res))
        {
            return res;
        }
        return null;
    }

    private sealed record ColumnIndices(
        int StudyIdIndex,
        int TestCodeIndex,
        int TestNameIndex,
        int SourceIndex,
        int SiteIdIndex,
        int UnitIndex,
        int NormalizedUnitIndex,
        int SexIndex,
        int AgeLowIndex,
        int AgeHighIndex,
        int LowBoundIndex,
        int HighBoundIndex,
        int CriticalLowIndex,
        int CriticalHighIndex,
        int LoincIndex);

    private static ColumnIndices MapHeaders(List<string> headers)
    {
        int studyIdx = -1, codeIdx = -1, nameIdx = -1, sourceIdx = -1, siteIdx = -1;
        int unitIdx = -1, normUnitIdx = -1, sexIdx = -1, ageLowIdx = -1, ageHighIdx = -1;
        int lowBoundIdx = -1, highBoundIdx = -1, critLowIdx = -1, critHighIdx = -1, loincIdx = -1;

        for (int i = 0; i < headers.Count; i++)
        {
            string h = headers[i].Trim().ToLowerInvariant().Replace("_", " ").Replace("-", " ");
            if (codeIdx < 0 && (h.Contains("test code") || h.Contains("analyte code") || h == "code" || h == "test" || h == "analyte"))
                codeIdx = i;
            else if (nameIdx < 0 && (h.Contains("test name") || h.Contains("analyte name") || h == "name" || h == "parameter"))
                nameIdx = i;
            else if (sourceIdx < 0 && (h.Contains("source") || h.Contains("lab source")))
                sourceIdx = i;
            else if (siteIdx < 0 && (h.Contains("site id") || h.Contains("site")))
                siteIdx = i;
            else if (unitIdx < 0 && (h == "unit" || h.Contains("reporting unit") || h.Contains("from unit") || h.Contains("local unit")))
                unitIdx = i;
            else if (normUnitIdx < 0 && (h.Contains("normalized unit") || h.Contains("target unit") || h.Contains("si unit") || h.Contains("ucum")))
                normUnitIdx = i;
            else if (sexIdx < 0 && (h.Contains("sex") || h.Contains("gender") || h.Contains("sex applicability")))
                sexIdx = i;
            else if (ageLowIdx < 0 && (h.Contains("age low") || h.Contains("age min") || h.Contains("min age")))
                ageLowIdx = i;
            else if (ageHighIdx < 0 && (h.Contains("age high") || h.Contains("age max") || h.Contains("max age")))
                ageHighIdx = i;
            else if (lowBoundIdx < 0 && (h.Contains("low bound") || h.Contains("range low") || h == "lln" || h == "low" || h.Contains("min normal")))
                lowBoundIdx = i;
            else if (highBoundIdx < 0 && (h.Contains("high bound") || h.Contains("range high") || h == "uln" || h == "high" || h.Contains("max normal")))
                highBoundIdx = i;
            else if (critLowIdx < 0 && (h.Contains("critical low") || h.Contains("panic low")))
                critLowIdx = i;
            else if (critHighIdx < 0 && (h.Contains("critical high") || h.Contains("panic high")))
                critHighIdx = i;
            else if (loincIdx < 0 && (h.Contains("loinc")))
                loincIdx = i;
            else if (studyIdx < 0 && (h.Contains("study id") || h == "study"))
                studyIdx = i;
        }

        // Fallbacks by positional standard index if headers missing
        if (codeIdx < 0) codeIdx = 0;
        if (nameIdx < 0) nameIdx = headers.Count > 1 ? 1 : 0;
        if (sourceIdx < 0) sourceIdx = headers.Count > 2 ? 2 : -1;
        if (siteIdx < 0) siteIdx = headers.Count > 3 ? 3 : -1;

        return new ColumnIndices(
            studyIdx, codeIdx, nameIdx, sourceIdx, siteIdx,
            unitIdx, normUnitIdx, sexIdx, ageLowIdx, ageHighIdx,
            lowBoundIdx, highBoundIdx, critLowIdx, critHighIdx, loincIdx);
    }
}

