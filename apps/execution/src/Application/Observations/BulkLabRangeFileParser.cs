using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

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

        List<string> headers = SplitCsvLine(headerLine);
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

            List<string> values = SplitCsvLine(line);
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
        using var stream = new MemoryStream(fileBytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        var sharedStrings = new List<string>();
        var sharedStringsEntry = zip.GetEntry("xl/sharedStrings.xml");
        if (sharedStringsEntry != null)
        {
            using var entryStream = sharedStringsEntry.Open();
            var xdoc = XDocument.Load(entryStream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"; // deid-ignore

            foreach (var si in xdoc.Descendants(ns + "si"))
            {
                var textNodes = si.Descendants(ns + "t").Select(t => t.Value);
                sharedStrings.Add(string.Concat(textNodes));
            }
        }

        var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml") ?? zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase));
        if (sheetEntry == null)
        {
            throw new InvalidOperationException("Invalid XLSX format: missing worksheet data.");
        }

        using var sheetStream = sheetEntry.Open();
        var sheetDoc = XDocument.Load(sheetStream);
        XNamespace sns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"; // deid-ignore

        var sheetData = sheetDoc.Descendants(sns + "sheetData").FirstOrDefault();
        if (sheetData == null)
        {
            return new List<ParsedLabRangeRow>();
        }

        var xmlRows = sheetData.Elements(sns + "row").ToList();
        if (xmlRows.Count == 0)
        {
            return new List<ParsedLabRangeRow>();
        }

        var headerRow = xmlRows[0];
        List<string> headers = ExtractRowValues(headerRow, sns, sharedStrings);
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
            List<string> values = ExtractRowValues(xmlRow, sns, sharedStrings);

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
        string testCode = GetValue(values, cols.TestCodeIndex);
        string testName = GetValue(values, cols.TestNameIndex);
        string source = GetValue(values, cols.SourceIndex);
        string siteId = GetValue(values, cols.SiteIdIndex);
        string unit = GetValue(values, cols.UnitIndex);
        string normUnit = GetValue(values, cols.NormalizedUnitIndex);
        string sex = GetValue(values, cols.SexIndex);
        string studyId = GetValue(values, cols.StudyIdIndex);
        string loinc = GetValue(values, cols.LoincIndex);

        double? ageLow = ParseNullableDouble(GetValue(values, cols.AgeLowIndex));
        double? ageHigh = ParseNullableDouble(GetValue(values, cols.AgeHighIndex));
        double? lowBound = ParseNullableDouble(GetValue(values, cols.LowBoundIndex));
        double? highBound = ParseNullableDouble(GetValue(values, cols.HighBoundIndex));
        double? critLow = ParseNullableDouble(GetValue(values, cols.CriticalLowIndex));
        double? critHigh = ParseNullableDouble(GetValue(values, cols.CriticalHighIndex));

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

    private static List<string> ExtractRowValues(XElement xmlRow, XNamespace sns, List<string> sharedStrings)
    {
        var values = new List<string>();
        int currentColIndex = 0;

        foreach (var c in xmlRow.Elements(sns + "c"))
        {
            string r = c.Attribute("r")?.Value ?? string.Empty;
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

    private static string GetValue(List<string> values, int index)
    {
        if (index < 0 || index >= values.Count) return string.Empty;
        return values[index]?.Trim() ?? string.Empty;
    }
}
