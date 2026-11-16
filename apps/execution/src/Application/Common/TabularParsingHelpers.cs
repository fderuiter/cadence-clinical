using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Cadence.Apps.Execution.Application.Common;

/// <summary>
/// Shared parsing helpers for CSV and OpenXML Excel spreadsheets.
/// </summary>
public static class TabularParsingHelpers
{
    public static readonly XNamespace SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"; // deid-ignore

    /// <summary>
    /// Loads shared strings table from an OpenXML ZIP archive.
    /// </summary>
    public static List<string> LoadSharedStrings(ZipArchive zip, XNamespace? ns = null)
    {
        ArgumentNullException.ThrowIfNull(zip);
        XNamespace targetNs = ns ?? SpreadsheetNamespace;

        var sharedStrings = new List<string>();
        var sharedStringsEntry = zip.GetEntry("xl/sharedStrings.xml");
        if (sharedStringsEntry != null)
        {
            using var entryStream = sharedStringsEntry.Open();
            var xdoc = XDocument.Load(entryStream);

            foreach (var si in xdoc.Descendants(targetNs + "si"))
            {
                var textNodes = si.Descendants(targetNs + "t").Select(t => t.Value);
                sharedStrings.Add(string.Concat(textNodes));
            }
        }

        return sharedStrings;
    }

    /// <summary>
    /// Loads worksheet row elements from the primary worksheet in an OpenXML ZIP archive.
    /// </summary>
    public static List<XElement> LoadWorksheetRows(ZipArchive zip, XNamespace? ns = null)
    {
        ArgumentNullException.ThrowIfNull(zip);
        XNamespace targetNs = ns ?? SpreadsheetNamespace;

        var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml") ?? zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase));
        if (sheetEntry == null)
        {
            throw new InvalidOperationException("Invalid XLSX format: missing worksheet data.");
        }

        using var sheetStream = sheetEntry.Open();
        var sheetDoc = XDocument.Load(sheetStream);

        var sheetData = sheetDoc.Descendants(targetNs + "sheetData").FirstOrDefault();
        if (sheetData == null)
        {
            return new List<XElement>();
        }

        return sheetData.Elements(targetNs + "row").ToList();
    }

    /// <summary>
    /// Extracts column cell values from an OpenXML row element, padding sparse columns with empty strings.
    /// </summary>
    public static List<string> ExtractRowValues(XElement xmlRow, XNamespace sns, List<string> sharedStrings)
    {
        var values = new List<string>();
        if (xmlRow == null) return values;

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
    public static List<string> ExtractRowValues(XElement xmlRow, List<string> sharedStrings)
    {
        return ExtractRowValues(xmlRow, SpreadsheetNamespace, sharedStrings);
    }

    /// <summary>
    /// Converts an Excel column name or cell reference (e.g., "A", "C12", "AA3") to a 0-based column index.
    /// </summary>
    public static int ColumnNameToIndex(string cellRef)
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
