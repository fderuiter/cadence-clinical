using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Knowledge;

namespace Cadence.Kernel.Application.Knowledge.Terminology;

/// <summary>
/// Turns a publisher's terminology file into code-system entries. Parsers read the published format as released; they
/// never fetch anything and never contain terminology content themselves (K-SEM-2).
/// </summary>
public interface ITerminologyParser
{
    /// <summary>The code system the format belongs to (<see cref="KnownCodeSystems"/>).</summary>
    string CodeSystemUri { get; }

    /// <summary>A short name for the file format, stored with the package (for example <c>nci-evs-tsv</c>).</summary>
    string Format { get; }

    IReadOnlyList<CodeSystemEntryData> Parse(Stream content);
}

/// <summary>
/// CDISC Controlled Terminology as NCI EVS publishes it: the tab-delimited text file per standard (for example
/// "SDTM Terminology.txt"), with the columns Code, Codelist Code, Codelist Extensible (Yes/No), Codelist Name, CDISC
/// Submission Value, CDISC Synonym(s), CDISC Definition and NCI Preferred Term. A codelist row (no codelist code) becomes
/// an entry with no group; each term becomes an entry grouped under its codelist's C-code.
/// </summary>
public sealed class CdiscControlledTerminologyParser : ITerminologyParser
{
    public string CodeSystemUri => KnownCodeSystems.NciThesaurus;

    public string Format => "nci-evs-tsv";

    public IReadOnlyList<CodeSystemEntryData> Parse(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var header = reader.ReadLine() ?? throw new FormatException("The CDISC CT file is empty.");
        var columns = Columns(header.Split('\t'), "Code", "Codelist Code", "Codelist Name", "CDISC Submission Value",
            "CDISC Synonym(s)", "CDISC Definition", "NCI Preferred Term");

        var entries = new List<CodeSystemEntryData>();
        var lineNumber = 1;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (line.Length == 0)
            {
                continue;
            }

            var cells = line.Split('\t');
            string Cell(string name) => columns[name] < cells.Length ? cells[columns[name]].Trim().Trim('"') : string.Empty;

            var code = Cell("Code");
            if (code.Length == 0)
            {
                throw new FormatException($"Line {lineNumber} has no Code.");
            }

            var codelist = Cell("Codelist Code");
            var isCodelist = codelist.Length == 0;
            var synonyms = Cell("CDISC Synonym(s)").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            entries.Add(new CodeSystemEntryData(
                code,
                isCodelist ? Cell("Codelist Name") : Cell("NCI Preferred Term"),
                isCodelist ? null : codelist,
                Cell("CDISC Submission Value"),
                Cell("CDISC Definition"),
                synonyms));
        }

        return entries;
    }

    internal static Dictionary<string, int> Columns(string[] header, params string[] required)
    {
        var index = header.Select((name, i) => (Name: name.Trim().Trim('"'), i))
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().i, StringComparer.OrdinalIgnoreCase);
        var missing = required.Where(r => !index.ContainsKey(r)).ToList();
        if (missing.Count > 0)
        {
            throw new FormatException($"The file is missing the column(s): {string.Join(", ", missing)}.");
        }

        return index;
    }
}

/// <summary>
/// UCUM from <c>ucum-essence.xml</c>: every base unit and defined unit becomes an entry grouped by its property
/// (for example <c>mass</c>), and prefixes are grouped under <c>prefix</c>. Unit expressions such as <c>mg/dL</c> are
/// built from these atoms by UCUM's grammar; validating an expression is a rule (spec §10), not a registry lookup.
/// </summary>
public sealed class UcumEssenceParser : ITerminologyParser
{
    public string CodeSystemUri => KnownCodeSystems.Ucum;

    public string Format => "ucum-essence-xml";

    public IReadOnlyList<CodeSystemEntryData> Parse(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var xml = XmlReader.Create(content, settings);
        var root = XDocument.Load(xml).Root ?? throw new FormatException("The UCUM file has no root element.");

        var entries = new List<CodeSystemEntryData>();
        foreach (var element in root.Elements())
        {
            var code = (string?)element.Attribute("Code");
            var name = element.Element("name")?.Value;
            if (code is null || name is null)
            {
                continue;
            }

            var group = element.Name.LocalName switch
            {
                "prefix" => "prefix",
                "base-unit" or "unit" => element.Element("property")?.Value,
                _ => null,
            };
            if (group is null && element.Name.LocalName != "unit")
            {
                continue;
            }

            entries.Add(new CodeSystemEntryData(
                code,
                name,
                group,
                SubmissionValue: element.Element("printSymbol")?.Value,
                Definition: null,
                Synonyms: [.. element.Elements("name").Skip(1).Select(n => n.Value)]));
        }

        return entries;
    }
}

/// <summary>
/// LOINC from the <c>Loinc.csv</c> table in the LOINC release: LOINC_NUM is the code, LONG_COMMON_NAME the display,
/// CLASS the group, SHORTNAME a synonym and DefinitionDescription the definition. Deprecated codes are kept, so data that used them still
/// resolves. LOINC's licence is accepted per tenant; its content is never in this repository.
/// </summary>
public sealed class LoincTableParser : ITerminologyParser
{
    public string CodeSystemUri => KnownCodeSystems.Loinc;

    public string Format => "loinc-csv";

    public IReadOnlyList<CodeSystemEntryData> Parse(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var rows = CsvReader.Read(reader).GetEnumerator();
        if (!rows.MoveNext())
        {
            throw new FormatException("The LOINC table is empty.");
        }

        var columns = CdiscControlledTerminologyParser.Columns([.. rows.Current], "LOINC_NUM", "LONG_COMMON_NAME", "CLASS");
        columns.TryGetValue("DefinitionDescription", out var definitionColumn);
        columns.TryGetValue("SHORTNAME", out var shortNameColumn);
        var hasDefinition = columns.ContainsKey("DefinitionDescription");
        var hasShortName = columns.ContainsKey("SHORTNAME");

        var entries = new List<CodeSystemEntryData>();
        while (rows.MoveNext())
        {
            var row = rows.Current;
            string Cell(int i) => i < row.Count ? row[i] : string.Empty;

            var shortName = hasShortName ? Cell(shortNameColumn) : string.Empty;
            entries.Add(new CodeSystemEntryData(
                Cell(columns["LOINC_NUM"]),
                Cell(columns["LONG_COMMON_NAME"]),
                GroupCode: Cell(columns["CLASS"]),
                SubmissionValue: null,
                Definition: hasDefinition ? Cell(definitionColumn) : null,
                Synonyms: shortName.Length > 0 ? [shortName] : []));
        }

        return entries;
    }
}

/// <summary>A minimal RFC 4180 reader: quoted fields, doubled quotes and line breaks inside quotes.</summary>
internal static class CsvReader
{
    public static IEnumerable<IReadOnlyList<string>> Read(TextReader reader)
    {
        var field = new StringBuilder();
        var row = new List<string>();
        var quoted = false;
        int c;
        while ((c = reader.Read()) != -1)
        {
            var ch = (char)c;
            if (quoted)
            {
                if (ch == '"' && reader.Peek() == '"')
                {
                    field.Append('"');
                    reader.Read();
                }
                else if (ch == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(ch);
                }

                continue;
            }

            switch (ch)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    yield return row;
                    row = [];
                    break;
                default:
                    field.Append(ch);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            yield return row;
        }
    }
}
