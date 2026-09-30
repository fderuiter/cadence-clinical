using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

using Cadence.Kernel.Validation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;

namespace Cadence.Rules.Indicators;

/// <summary>
/// Reads and writes the JSON content of an indicator set library item. The format is documented in
/// <c>docs/kernel/INDICATORS.md</c>:
/// <code>
/// { "authority": "Sponsor", "title": "…",
///   "indicators": [ { "id": "…", "version": 1, "title": "…", "area": "Enrollment", "kind": "Pulse",
///                     "population": "enrollments", "counts": { "op": "…", "args": [ … ] },
///                     "measure": "Count", "threshold": { "direction": "Below", "limit": 10, "severity": "Warning" },
///                     "explanation": "…", "effective": { "from": null, "until": null } } ] }
/// </code>
/// Reading is strict and reports every problem, so a set is refused when it is drafted, not when it runs.
/// </summary>
public static partial class IndicatorSetJson
{
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The most indicators one set may hold.</summary>
    public const int MaxIndicators = 200;

    /// <summary>The longest text property, in characters.</summary>
    public const int MaxTextLength = RulePackJson.MaxTextLength;

    private const int MaxProblems = 50;

    // Indicators are the sponsor's or a study's own definitions; public authorities publish rules, not Pulse metrics.
    private static readonly Dictionary<string, RuleAuthority> Authorities = new(StringComparer.Ordinal)
    {
        ["Sponsor"] = RuleAuthority.Sponsor,
        ["Study"] = RuleAuthority.Study,
    };

    private static readonly Dictionary<string, IndicatorArea> Areas = Names<IndicatorArea>();
    private static readonly Dictionary<string, IndicatorKind> Kinds = Names<IndicatorKind>();
    private static readonly Dictionary<string, IndicatorMeasure> Measures = Names<IndicatorMeasure>();
    private static readonly Dictionary<string, ThresholdDirection> Directions = Names<ThresholdDirection>();
    private static readonly Dictionary<string, ValidationSeverity> Severities = Names<ValidationSeverity>();

    /// <summary>Reads a set, or returns why it is not valid.</summary>
    public static (IndicatorSet? Set, IReadOnlyList<string> Problems) Read(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            return (null, ["An indicator set is a JSON object."]);
        }

        var problems = new List<string>();
        RulePackJson.CheckProperties(json, "$", problems, "authority", "title", "indicators");
        var authority = RulePackJson.Enum(json, "authority", "$", Authorities, problems, required: true);
        var title = RulePackJson.Text(json, "title", "$", problems, required: true);
        var indicators = new List<Indicator>();
        if (!json.TryGetProperty("indicators", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            problems.Add("$.indicators: required, and must be an array.");
        }
        else if (list.GetArrayLength() is 0 or > MaxIndicators)
        {
            problems.Add($"$.indicators: a set has between 1 and {MaxIndicators} indicators.");
        }
        else
        {
            var index = 0;
            foreach (var item in list.EnumerateArray())
            {
                if (ReadIndicator(item, $"$.indicators[{index++}]", problems) is { } indicator)
                {
                    indicators.Add(indicator);
                }

                if (problems.Count >= MaxProblems)
                {
                    break;
                }
            }

            foreach (var duplicate in indicators.GroupBy(i => i.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                problems.Add($"$.indicators: indicator \"{duplicate.Key}\" appears {duplicate.Count()} times; a set has one version of each indicator.");
            }
        }

        return problems.Count == 0
            ? (new IndicatorSet(authority!.Value, title!, indicators), [])
            : (null, problems.Take(MaxProblems).ToList());
    }

    /// <summary>Reads a set.</summary>
    /// <exception cref="FormatException">The set is not valid; the message lists the problems.</exception>
    public static IndicatorSet Parse(JsonElement json)
    {
        var (set, problems) = Read(json);
        return set ?? throw new FormatException(string.Join(" ", problems));
    }

    /// <summary>Writes a set as JSON content for a <c>DraftLibraryItem</c> command.</summary>
    public static JsonElement ToJson(IndicatorSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("authority", Authorities.First(pair => pair.Value == set.Authority).Key);
            writer.WriteString("title", set.Title);
            writer.WriteStartArray("indicators");
            foreach (var indicator in set.Indicators)
            {
                WriteIndicator(writer, indicator);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static Indicator? ReadIndicator(JsonElement json, string path, List<string> problems)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{path}: an indicator is a JSON object.");
            return null;
        }

        var before = problems.Count;
        RulePackJson.CheckProperties(json, path, problems, "id", "version", "title", "area", "kind", "population", "counts", "measure", "threshold", "explanation", "effective");
        var id = Identifier(json, "id", path, problems);
        var version = 0;
        if (!json.TryGetProperty("version", out var versionJson) || !versionJson.TryGetInt32(out version) || version < 1)
        {
            problems.Add($"{path}.version: required, a whole number from 1.");
        }

        var title = RulePackJson.Text(json, "title", path, problems, required: true);
        var area = RulePackJson.Enum(json, "area", path, Areas, problems, required: true);
        var kind = RulePackJson.Enum(json, "kind", path, Kinds, problems, required: true);
        var population = Identifier(json, "population", path, problems);
        var measure = RulePackJson.Enum(json, "measure", path, Measures, problems, required: true);
        var threshold = ReadThreshold(json, path, problems);
        var explanation = RulePackJson.Text(json, "explanation", path, problems, required: true);
        var effective = RulePackJson.ReadEffective(json, path, problems);
        RuleExpression? counts = null;
        if (!json.TryGetProperty("counts", out var countsJson))
        {
            problems.Add($"{path}.counts: required.");
        }
        else if (!RuleExpressionJson.TryParse(countsJson, out counts, out var error))
        {
            problems.Add($"{path}.counts{error![1..]}");
        }

        if (kind is IndicatorKind.Kri or IndicatorKind.Qtl
            && (!json.TryGetProperty("threshold", out var thresholdJson) || thresholdJson.ValueKind == JsonValueKind.Null))
        {
            problems.Add($"{path}.threshold: required for a {kind}; a risk indicator or tolerance limit has a limit (spec §10.4).");
        }

        return problems.Count > before
            ? null
            : new Indicator(id!, version, title!, area!.Value, kind!.Value, population!, counts!, measure!.Value, threshold, explanation!, effective!);
    }

    private static IndicatorThreshold? ReadThreshold(JsonElement indicator, string path, List<string> problems)
    {
        path += ".threshold";
        if (!indicator.TryGetProperty("threshold", out var json) || json.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (json.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{path}: must be an object with a direction, a limit and a severity.");
            return null;
        }

        RulePackJson.CheckProperties(json, path, problems, "direction", "limit", "severity");
        var direction = RulePackJson.Enum(json, "direction", path, Directions, problems, required: true);
        var severity = RulePackJson.Enum(json, "severity", path, Severities, problems, required: true);
        decimal limit = 0;
        if (!json.TryGetProperty("limit", out var limitJson) || limitJson.ValueKind != JsonValueKind.Number || !limitJson.TryGetDecimal(out limit))
        {
            problems.Add($"{path}.limit: required, a number.");
            return null;
        }

        return direction is null || severity is null ? null : new IndicatorThreshold(direction.Value, limit, severity.Value);
    }

    private static string? Identifier(JsonElement json, string name, string path, List<string> problems)
    {
        var text = RulePackJson.Text(json, name, path, problems, required: true);
        if (text is not null && !IdPattern().IsMatch(text))
        {
            problems.Add($"{path}.{name}: \"{text}\" must be 1 to 64 letters, digits, '.', '_' or '-', starting with a letter or digit.");
            return null;
        }

        return text;
    }

    private static void WriteIndicator(Utf8JsonWriter writer, Indicator indicator)
    {
        writer.WriteStartObject();
        writer.WriteString("id", indicator.Id);
        writer.WriteNumber("version", indicator.Version);
        writer.WriteString("title", indicator.Title);
        writer.WriteString("area", indicator.Area.ToString());
        writer.WriteString("kind", indicator.Kind.ToString());
        writer.WriteString("population", indicator.Population);
        writer.WritePropertyName("counts");
        RuleExpressionJson.Write(writer, indicator.Counts);
        writer.WriteString("measure", indicator.Measure.ToString());
        if (indicator.Threshold is { } threshold)
        {
            writer.WriteStartObject("threshold");
            writer.WriteString("direction", threshold.Direction.ToString());
            writer.WriteNumber("limit", threshold.Limit);
            writer.WriteString("severity", threshold.Severity.ToString());
            writer.WriteEndObject();
        }

        writer.WriteString("explanation", indicator.Explanation);
        if (indicator.Effective.From is not null || indicator.Effective.Until is not null)
        {
            writer.WriteStartObject("effective");
            if (indicator.Effective.From is { } from)
            {
                writer.WriteString("from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }

            if (indicator.Effective.Until is { } until)
            {
                writer.WriteString("until", until.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static Dictionary<string, TEnum> Names<TEnum>()
        where TEnum : struct, System.Enum =>
        System.Enum.GetValues<TEnum>().ToDictionary(v => v.ToString(), StringComparer.Ordinal);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")]
    private static partial Regex IdPattern();
}
