using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Cadence.Kernel.Standards.Mapping;

/// <summary>
/// Reads and writes the JSON content of a mapping specification library item (ADR-2463). The format is documented in
/// <c>docs/kernel/MAPPING_SPECIFICATIONS.md</c>:
/// <code>
/// { "title": "…", "overrides": "base-key",
///   "controlledTerminology": [ { "codeSystem": "sdtmct", "version": "2024-09-27" } ],
///   "units": [ { "ucum": "mm[Hg]", "submissionValue": "mmHg" } ],
///   "mappings": [ { "id": "vs-sysbp", "concept": { "id": "…", "version": 1 }, "dataset": "VS",
///                   "assign": { "VSTESTCD": "SYSBP" },
///                   "result": { "original": "VSORRES", "originalUnit": "VSORRESU", "standardCharacter": "VSSTRESC",
///                               "standardNumeric": "VSSTRESN", "standardUnit": "VSSTRESU" },
///                   "standardUnit": "mm[Hg]", "conversions": [ { "from": "kPa", "to": "mm[Hg]", "factor": 7.50062 } ], <!-- deid-ignore: a unit conversion factor -->
///                   "decimals": 0, "codelist": "C66770" } ] }
/// </code>
/// Reading is strict and reports every problem it finds, so a specification is refused when it is drafted. Whether the
/// datasets and variables exist in the pinned standard version, and whether the Concepts and CT versions exist in the
/// tenant, is checked by the kernel when the item is drafted.
/// </summary>
public static class MappingSpecificationJson
{
    /// <summary>The most mappings one specification may hold.</summary>
    public const int MaxMappings = 5000;

    /// <summary>The longest text property (title, assigned value, unit), in characters.</summary>
    public const int MaxTextLength = 2000;

    /// <summary>The most decimal places a standardised result may be rounded to.</summary>
    public const int MaxDecimals = 10;

    private const int MaxProblems = 50;

    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Reads a specification, or returns why it is not valid.</summary>
    public static (MappingSpecification? Specification, IReadOnlyList<string> Problems) Read(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            return (null, ["A mapping specification is a JSON object."]);
        }

        var problems = new List<string>();
        MappingSpecificationJsonReader.CheckProperties(json, "$", problems, "title", "overrides", "controlledTerminology", "units", "mappings");
        var title = MappingSpecificationJsonReader.Text(json, "title", "$", problems, required: true);
        var overrides = MappingSpecificationJsonReader.Text(json, "overrides", "$", problems, required: false);
        var terminology = MappingSpecificationJsonReader.ReadTerminology(json, problems);
        var units = MappingSpecificationJsonReader.ReadUnits(json, problems);
        var mappings = new List<VariableMapping>();
        if (!json.TryGetProperty("mappings", out var mappingsJson) || mappingsJson.ValueKind != JsonValueKind.Array)
        {
            problems.Add("$.mappings: required, and must be an array.");
        }
        else if (mappingsJson.GetArrayLength() is 0 or > MaxMappings)
        {
            problems.Add($"$.mappings: a specification has between 1 and {MaxMappings} mappings.");
        }
        else
        {
            var index = 0;
            foreach (var mappingJson in mappingsJson.EnumerateArray())
            {
                if (MappingSpecificationJsonReader.ReadMapping(mappingJson, $"$.mappings[{index++}]", problems) is { } mapping)
                {
                    mappings.Add(mapping);
                }

                if (problems.Count >= MaxProblems)
                {
                    break;
                }
            }

            foreach (var duplicate in mappings.GroupBy(m => m.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                problems.Add($"$.mappings: mapping \"{duplicate.Key}\" appears {duplicate.Count()} times.");
            }

            foreach (var duplicate in mappings.GroupBy(m => m.Concept.ConceptId).Where(g => g.Count() > 1))
            {
                problems.Add($"$.mappings: concept {duplicate.Key} is mapped by {string.Join(", ", duplicate.Select(m => $"\"{m.Id}\""))}; a specification maps each concept once.");
            }
        }

        foreach (var mapping in mappings)
        {
            MappingSpecificationJsonReader.CheckUnits(mapping, units, problems);
        }

        return problems.Count == 0
            ? (new MappingSpecification(title!, overrides, terminology, units, mappings), [])
            : (null, problems.Take(MaxProblems).ToList());
    }

    /// <summary>Reads a specification.</summary>
    /// <exception cref="FormatException">The specification is not valid; the message lists the problems.</exception>
    public static MappingSpecification Parse(JsonElement json)
    {
        var (specification, problems) = Read(json);
        return specification ?? throw new FormatException(string.Join(" ", problems));
    }

    /// <summary>Writes a specification as JSON content for a <c>DraftLibraryItem</c> command.</summary>
    public static JsonElement ToJson(MappingSpecification specification)
    {
        ArgumentNullException.ThrowIfNull(specification);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("title", specification.Title);
            if (specification.Overrides is not null)
            {
                writer.WriteString("overrides", specification.Overrides);
            }

            writer.WriteStartArray("controlledTerminology");
            foreach (var reference in specification.ControlledTerminology)
            {
                writer.WriteStartObject();
                writer.WriteString("codeSystem", reference.CodeSystem);
                writer.WriteString("version", reference.Version);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("units");
            foreach (var unit in specification.Units)
            {
                writer.WriteStartObject();
                writer.WriteString("ucum", unit.Ucum);
                writer.WriteString("submissionValue", unit.SubmissionValue);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("mappings");
            foreach (var mapping in specification.Mappings)
            {
                WriteMapping(writer, mapping);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static void WriteMapping(Utf8JsonWriter writer, VariableMapping mapping)
    {
        writer.WriteStartObject();
        writer.WriteString("id", mapping.Id);
        writer.WriteStartObject("concept");
        writer.WriteString("id", mapping.Concept.ConceptId.Value.ToString("D", CultureInfo.InvariantCulture));
        writer.WriteNumber("version", mapping.Concept.Version);
        writer.WriteEndObject();
        writer.WriteString("dataset", mapping.Dataset);
        writer.WriteStartObject("assign");
        foreach (var (variable, value) in mapping.Assign)
        {
            writer.WriteString(variable, value);
        }

        writer.WriteEndObject();
        writer.WriteStartObject("result");
        writer.WriteString("original", mapping.Result.Original);
        WriteOptional(writer, "originalUnit", mapping.Result.OriginalUnit);
        WriteOptional(writer, "standardCharacter", mapping.Result.StandardCharacter);
        WriteOptional(writer, "standardNumeric", mapping.Result.StandardNumeric);
        WriteOptional(writer, "standardUnit", mapping.Result.StandardUnit);
        WriteOptional(writer, "status", mapping.Result.Status);
        WriteOptional(writer, "reasonNotDone", mapping.Result.ReasonNotDone);
        writer.WriteEndObject();
        WriteOptional(writer, "standardUnit", mapping.StandardUnit);
        if (mapping.Conversions.Count > 0)
        {
            writer.WriteStartArray("conversions");
            foreach (var conversion in mapping.Conversions)
            {
                writer.WriteStartObject();
                writer.WriteString("from", conversion.From);
                writer.WriteString("to", conversion.To);
                writer.WriteNumber("factor", conversion.Factor);
                if (conversion.Offset != 0m)
                {
                    writer.WriteNumber("offset", conversion.Offset);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        if (mapping.Decimals is { } decimals)
        {
            writer.WriteNumber("decimals", decimals);
        }

        WriteOptional(writer, "codelist", mapping.Codelist);
        writer.WriteEndObject();
    }

    private static void WriteOptional(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }
}
