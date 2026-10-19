using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

using Cadence.Kernel.Knowledge;

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
public static partial class MappingSpecificationJson
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
        CheckProperties(json, "$", problems, "title", "overrides", "controlledTerminology", "units", "mappings");
        var title = Text(json, "title", "$", problems, required: true);
        var overrides = Text(json, "overrides", "$", problems, required: false);
        var terminology = ReadTerminology(json, problems);
        var units = ReadUnits(json, problems);
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
                if (ReadMapping(mappingJson, $"$.mappings[{index++}]", problems) is { } mapping)
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
            CheckUnits(mapping, units, problems);
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

    private static List<CodeSystemVersionRef> ReadTerminology(JsonElement json, List<string> problems)
    {
        var references = new List<CodeSystemVersionRef>();
        if (!json.TryGetProperty("controlledTerminology", out var array))
        {
            return references;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            problems.Add("$.controlledTerminology: must be an array.");
            return references;
        }

        var index = 0;
        foreach (var entry in array.EnumerateArray())
        {
            var path = $"$.controlledTerminology[{index++}]";
            if (entry.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{path}: must be an object.");
                continue;
            }

            CheckProperties(entry, path, problems, "codeSystem", "version");
            var codeSystem = Text(entry, "codeSystem", path, problems, required: true);
            var version = Text(entry, "version", path, problems, required: true);
            if (codeSystem is null || version is null)
            {
                continue;
            }

            var reference = new CodeSystemVersionRef(codeSystem, version);
            if (reference.ToCodeSystemUri() is null)
            {
                problems.Add($"{path}.codeSystem: \"{codeSystem}\" names no code system; use a CDISC CT package type such as \"sdtmct\" or a code-system URI.");
            }

            references.Add(reference);
        }

        foreach (var duplicate in references.GroupBy(r => r.CodeSystem, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            problems.Add($"$.controlledTerminology: {duplicate.Key} is listed {duplicate.Count()} times; a specification uses one version of each code system.");
        }

        return references;
    }

    private static List<UnitTerm> ReadUnits(JsonElement json, List<string> problems)
    {
        var units = new List<UnitTerm>();
        if (!json.TryGetProperty("units", out var array))
        {
            return units;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            problems.Add("$.units: must be an array.");
            return units;
        }

        var index = 0;
        foreach (var entry in array.EnumerateArray())
        {
            var path = $"$.units[{index++}]";
            if (entry.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{path}: must be an object.");
                continue;
            }

            CheckProperties(entry, path, problems, "ucum", "submissionValue");
            var ucum = Unit(entry, "ucum", path, problems, required: true);
            var submissionValue = Text(entry, "submissionValue", path, problems, required: true);
            if (ucum is not null && submissionValue is not null)
            {
                units.Add(new UnitTerm(ucum, submissionValue));
            }
        }

        foreach (var duplicate in units.GroupBy(u => u.Ucum, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            problems.Add($"$.units: {duplicate.Key} is listed {duplicate.Count()} times.");
        }

        return units;
    }

    private static VariableMapping? ReadMapping(JsonElement json, string path, List<string> problems)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{path}: must be an object.");
            return null;
        }

        var before = problems.Count;
        CheckProperties(json, path, problems, "id", "concept", "dataset", "assign", "result", "standardUnit", "conversions", "decimals", "codelist");
        var id = Text(json, "id", path, problems, required: true);
        if (id is not null && !IdPattern().IsMatch(id))
        {
            problems.Add($"{path}.id: \"{id}\" must start with a letter and use only letters, digits and _ . : - (at most 128).");
        }

        var concept = ReadConcept(json, path, problems);
        var dataset = Name(json, "dataset", path, problems, required: true);
        var assign = ReadAssign(json, path, problems);
        var result = ReadResult(json, path, problems);
        var standardUnit = Unit(json, "standardUnit", path, problems, required: false);
        var conversions = ReadConversions(json, path, problems);
        int? decimals = null;
        if (json.TryGetProperty("decimals", out var decimalsJson))
        {
            if (decimalsJson.ValueKind == JsonValueKind.Number && decimalsJson.TryGetInt32(out var value) && value is >= 0 and <= MaxDecimals)
            {
                decimals = value;
            }
            else
            {
                problems.Add($"{path}.decimals: must be a whole number from 0 to {MaxDecimals}.");
            }
        }

        var codelist = Text(json, "codelist", path, problems, required: false);
        if (codelist is not null && !CodelistPattern().IsMatch(codelist))
        {
            problems.Add($"{path}.codelist: \"{codelist}\" must be an NCI C-code such as C66770.");
        }

        if (result is not null)
        {
            if ((result.StandardNumeric is not null || result.StandardUnit is not null || conversions.Count > 0) && standardUnit is null)
            {
                problems.Add($"{path}.standardUnit: required when the result is standardised to a numeric value or unit, or when conversions are given.");
            }

            if (result.StandardUnit is not null && result.StandardNumeric is null)
            {
                problems.Add($"{path}.result.standardNumeric: required with result.standardUnit.");
            }

            if (codelist is not null && (standardUnit is not null || result.StandardNumeric is not null || result.OriginalUnit is not null))
            {
                problems.Add($"{path}: a coded result (codelist) is not a quantity; drop standardUnit, result.standardNumeric and result.originalUnit.");
            }

            var written = assign.Keys.Concat(new[] { result.Original, result.OriginalUnit, result.StandardCharacter, result.StandardNumeric, result.StandardUnit, result.Status, result.ReasonNotDone }.OfType<string>());
            foreach (var duplicate in written.GroupBy(v => v, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                problems.Add($"{path}: variable {duplicate.Key} is written {duplicate.Count()} times.");
            }
        }

        foreach (var conversion in conversions)
        {
            if (standardUnit is not null && !string.Equals(conversion.To, standardUnit, StringComparison.Ordinal))
            {
                problems.Add($"{path}.conversions: {conversion.From} → {conversion.To} does not convert into the standard unit {standardUnit}.");
            }

            if (string.Equals(conversion.From, conversion.To, StringComparison.Ordinal))
            {
                problems.Add($"{path}.conversions: {conversion.From} → {conversion.To} converts a unit into itself.");
            }
        }

        foreach (var duplicate in conversions.GroupBy(c => c.From, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            problems.Add($"{path}.conversions: {duplicate.Key} is converted {duplicate.Count()} times.");
        }

        return problems.Count == before
            ? new VariableMapping(id!, concept!.Value, dataset!, assign, result!, standardUnit, conversions, decimals, codelist)
            : null;
    }

    private static void CheckUnits(VariableMapping mapping, List<UnitTerm> units, List<string> problems)
    {
        // Every unit the mapping writes needs its standard term: the standard unit when a unit variable is written, and
        // each accepted unit when the original unit is written.
        var listed = units.Select(u => u.Ucum).ToHashSet(StringComparer.Ordinal);
        var needed = new SortedSet<string>(StringComparer.Ordinal);
        if (mapping.StandardUnit is { } standardUnit && (mapping.Result.StandardUnit is not null || mapping.Result.OriginalUnit is not null))
        {
            needed.Add(standardUnit);
        }

        if (mapping.Result.OriginalUnit is not null)
        {
            needed.UnionWith(mapping.Conversions.Select(c => c.From));
        }

        foreach (var unit in needed.Where(u => !listed.Contains(u)))
        {
            problems.Add($"$.units: mapping \"{mapping.Id}\" writes results in {unit}, which $.units does not list.");
        }
    }

    private static ConceptRef? ReadConcept(JsonElement json, string path, List<string> problems)
    {
        if (!json.TryGetProperty("concept", out var concept) || concept.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{path}.concept: required, and must be an object with id and version.");
            return null;
        }

        CheckProperties(concept, $"{path}.concept", problems, "id", "version");
        Guid? id = concept.TryGetProperty("id", out var idJson) && idJson.ValueKind == JsonValueKind.String
            && Guid.TryParseExact(idJson.GetString(), "D", out var guid) && guid != Guid.Empty ? guid : null;
        int? version = concept.TryGetProperty("version", out var versionJson) && versionJson.ValueKind == JsonValueKind.Number
            && versionJson.TryGetInt32(out var number) && number >= 1 ? number : null;
        if (id is null)
        {
            problems.Add($"{path}.concept.id: required, and must be a concept id (a UUID).");
        }

        if (version is null)
        {
            problems.Add($"{path}.concept.version: required, and must be a whole number from 1; a mapping names the concept version it was authored against.");
        }

        return id is null || version is null ? null : new ConceptRef(ConceptId.FromGuid(id.Value), version.Value);
    }

    private static SortedDictionary<string, string> ReadAssign(JsonElement json, string path, List<string> problems)
    {
        var assign = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (!json.TryGetProperty("assign", out var assignJson))
        {
            return assign;
        }

        if (assignJson.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{path}.assign: must be an object of variable names and values.");
            return assign;
        }

        foreach (var property in assignJson.EnumerateObject())
        {
            if (!NamePattern().IsMatch(property.Name))
            {
                problems.Add($"{path}.assign: \"{property.Name}\" is not a variable name (upper-case letters, digits and _, starting with a letter, at most 32).");
                continue;
            }

            var value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaxTextLength || value.Trim().Length != value.Length)
            {
                problems.Add($"{path}.assign.{property.Name}: must be text of 1 to {MaxTextLength} characters with no surrounding spaces.");
                continue;
            }

            assign[property.Name] = value;
        }

        return assign;
    }

    private static ResultVariables? ReadResult(JsonElement json, string path, List<string> problems)
    {
        if (!json.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{path}.result: required, and must be an object naming at least the original result variable.");
            return null;
        }

        var resultPath = $"{path}.result";
        var before = problems.Count;
        CheckProperties(result, resultPath, problems, "original", "originalUnit", "standardCharacter", "standardNumeric", "standardUnit", "status", "reasonNotDone");
        var original = Name(result, "original", resultPath, problems, required: true);
        var originalUnit = Name(result, "originalUnit", resultPath, problems, required: false);
        var standardCharacter = Name(result, "standardCharacter", resultPath, problems, required: false);
        var standardNumeric = Name(result, "standardNumeric", resultPath, problems, required: false);
        var standardUnit = Name(result, "standardUnit", resultPath, problems, required: false);
        var status = Name(result, "status", resultPath, problems, required: false);
        var reasonNotDone = Name(result, "reasonNotDone", resultPath, problems, required: false);
        if (reasonNotDone is not null && status is null)
        {
            problems.Add($"{resultPath}.reasonNotDone: requires result.status, which says the value was not done.");
        }

        return problems.Count == before ? new ResultVariables(original!, originalUnit, standardCharacter, standardNumeric, standardUnit, status, reasonNotDone) : null;
    }

    private static List<UnitConversion> ReadConversions(JsonElement json, string path, List<string> problems)
    {
        var conversions = new List<UnitConversion>();
        if (!json.TryGetProperty("conversions", out var array))
        {
            return conversions;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"{path}.conversions: must be an array.");
            return conversions;
        }

        var index = 0;
        foreach (var entry in array.EnumerateArray())
        {
            var entryPath = $"{path}.conversions[{index++}]";
            if (entry.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{entryPath}: must be an object.");
                continue;
            }

            CheckProperties(entry, entryPath, problems, "from", "to", "factor", "offset");
            var from = Unit(entry, "from", entryPath, problems, required: true);
            var to = Unit(entry, "to", entryPath, problems, required: true);
            decimal? factor = entry.TryGetProperty("factor", out var factorJson) && factorJson.ValueKind == JsonValueKind.Number
                && factorJson.TryGetDecimal(out var f) && f != 0m ? f : null;
            if (factor is null)
            {
                problems.Add($"{entryPath}.factor: required, and must be a non-zero number.");
            }

            var offset = 0m;
            if (entry.TryGetProperty("offset", out var offsetJson) && !(offsetJson.ValueKind == JsonValueKind.Number && offsetJson.TryGetDecimal(out offset)))
            {
                problems.Add($"{entryPath}.offset: must be a number.");
            }

            if (from is not null && to is not null && factor is not null)
            {
                conversions.Add(new UnitConversion(from, to, factor.Value, offset));
            }
        }

        return conversions;
    }

    private static void CheckProperties(JsonElement json, string path, List<string> problems, params string[] allowed)
    {
        foreach (var name in json.EnumerateObject().Select(p => p.Name).Where(n => Array.IndexOf(allowed, n) < 0))
        {
            problems.Add($"{path}: unknown property \"{name}\".");
        }
    }

    private static string? Text(JsonElement json, string name, string path, List<string> problems, bool required)
    {
        if (!json.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            if (required)
            {
                problems.Add($"{path}.{name}: required.");
            }

            return null;
        }

        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength || text.Trim().Length != text.Length)
        {
            problems.Add($"{path}.{name}: must be text of 1 to {MaxTextLength} characters with no surrounding spaces.");
            return null;
        }

        return text;
    }

    private static string? Name(JsonElement json, string name, string path, List<string> problems, bool required)
    {
        var text = Text(json, name, path, problems, required);
        if (text is not null && !NamePattern().IsMatch(text))
        {
            problems.Add($"{path}.{name}: \"{text}\" is not a dataset or variable name (upper-case letters, digits and _, starting with a letter, at most 32).");
            return null;
        }

        return text;
    }

    private static string? Unit(JsonElement json, string name, string path, List<string> problems, bool required)
    {
        var text = Text(json, name, path, problems, required);
        if (text is not null && text.Any(char.IsWhiteSpace))
        {
            problems.Add($"{path}.{name}: \"{text}\" is not a UCUM code (UCUM codes have no spaces).");
            return null;
        }

        return text;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.:-]{0,127}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[A-Z][A-Z0-9_]{0,31}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex NamePattern();

    [GeneratedRegex("^C[0-9]{1,10}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex CodelistPattern();
}
