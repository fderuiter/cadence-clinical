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

namespace Cadence.Rules.Packs;

/// <summary>
/// Reads and writes the JSON content of a rule pack library item. The format is documented in
/// <c>docs/kernel/RULE_PACKS.md</c>:
/// <code>
/// { "authority": "CDISC", "title": "…",
///   "rules": [ { "id": "…", "version": 1, "authority": "CDISC",
///                "citation": { "document": "…", "section": "…", "uri": "https://…" },
///                "applicability": { "stage": "Standardization", "standard": "…", "standardVersion": "…",
///                                   "agency": "…", "dataset": "…", "variable": "…" },
///                "severity": "Error", "condition": { "op": "…", "args": [ … ] },
///                "explanation": "…", "remediation": "…",
///                "effective": { "from": "2024-01-01", "until": null } } ] }
/// </code>
/// Reading is strict and reports every problem it finds, so a pack is rejected when it is drafted, not when it runs.
/// </summary>
public static partial class RulePackJson
{
    // Canonical output escapes only what JSON requires, so operators such as < and + stay readable.
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The most rules one pack may hold.</summary>
    public const int MaxRules = 5000;
    /// <summary>The longest text property (title, explanation, citation, …), in characters.</summary>
    public const int MaxTextLength = 2000;

    private const int MaxProblems = 50;

    // K-RULE-2: packs implement public documents. A citation of a vendor's validator is a copy of its rules.
    private static readonly string[] ProprietarySources = ["pinnacle 21", "pinnacle21", "opencdisc", "p21 validator"];

    private static readonly Dictionary<string, RuleAuthority> Authorities = new(StringComparer.Ordinal)
    {
        ["CDISC"] = RuleAuthority.Cdisc,
        ["FDA"] = RuleAuthority.Fda,
        ["PMDA"] = RuleAuthority.Pmda,
        ["NMPA"] = RuleAuthority.Nmpa,
        ["EMA"] = RuleAuthority.Ema,
        ["Sponsor"] = RuleAuthority.Sponsor,
        ["Study"] = RuleAuthority.Study,
    };

    /// <summary>Reads a pack, or returns why it is not valid.</summary>
    public static (RulePack? Pack, IReadOnlyList<string> Problems) Read(JsonElement json)
    {
        var problems = new List<string>();
        if (json.ValueKind != JsonValueKind.Object)
        {
            return (null, ["A rule pack is a JSON object."]);
        }

        CheckProperties(json, "$", problems, "authority", "title", "rules");
        var authority = Enum(json, "authority", "$", Authorities, problems, required: true);
        var title = Text(json, "title", "$", problems, required: true);
        var rules = new List<Rule>();
        if (!json.TryGetProperty("rules", out var rulesJson) || rulesJson.ValueKind != JsonValueKind.Array)
        {
            problems.Add("$.rules: required, and must be an array.");
        }
        else if (rulesJson.GetArrayLength() is 0 or > MaxRules)
        {
            problems.Add($"$.rules: a pack has between 1 and {MaxRules} rules.");
        }
        else
        {
            var index = 0;
            foreach (var ruleJson in rulesJson.EnumerateArray())
            {
                if (ReadRule(ruleJson, $"$.rules[{index++}]", authority ?? RuleAuthority.Sponsor, problems) is { } rule)
                {
                    rules.Add(rule);
                }

                if (problems.Count >= MaxProblems)
                {
                    break;
                }
            }

            foreach (var duplicate in rules.GroupBy(r => r.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                problems.Add($"$.rules: rule \"{duplicate.Key}\" appears {duplicate.Count()} times; a pack has one version of each rule.");
            }
        }

        return problems.Count == 0 ? (new RulePack(authority!.Value, title!, rules), []) : (null, problems.Take(MaxProblems).ToList());
    }

    /// <summary>Reads a pack.</summary>
    /// <exception cref="FormatException">The pack is not valid; the message lists the problems.</exception>
    public static RulePack Parse(JsonElement json)
    {
        var (pack, problems) = Read(json);
        return pack ?? throw new FormatException(string.Join(" ", problems));
    }

    /// <summary>Writes a pack as JSON content for a <c>DraftLibraryItem</c> command.</summary>
    public static JsonElement ToJson(RulePack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("authority", Name(pack.Authority));
            writer.WriteString("title", pack.Title);
            writer.WriteStartArray("rules");
            foreach (var rule in pack.Rules)
            {
                WriteRule(writer, rule);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static Rule? ReadRule(JsonElement json, string path, RuleAuthority packAuthority, List<string> problems)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{path}: a rule is a JSON object.");
            return null;
        }

        var before = problems.Count;
        CheckProperties(json, path, problems, "id", "version", "authority", "citation", "applicability", "severity", "condition", "explanation", "remediation", "effective");

        var id = Text(json, "id", path, problems, required: true);
        if (id is not null && !RuleId().IsMatch(id))
        {
            problems.Add($"{path}.id: \"{id}\" must be 1 to 64 letters, digits, '.', '_' or '-', starting with a letter or digit.");
        }

        int version = 0;
        if (!json.TryGetProperty("version", out var versionJson) || !versionJson.TryGetInt32(out version) || version < 1)
        {
            problems.Add($"{path}.version: required, a whole number from 1.");
        }

        var authority = Enum(json, "authority", path, Authorities, problems, required: false) ?? packAuthority;
        var citation = ReadCitation(json, path, problems);
        var applicability = ReadApplicability(json, path, problems);
        var severity = Enum(json, "severity", path, SeverityNames, problems, required: true);
        var explanation = Text(json, "explanation", path, problems, required: true);
        var remediation = Text(json, "remediation", path, problems, required: false);
        var effective = ReadEffective(json, path, problems);

        RuleExpression? condition = null;
        if (!json.TryGetProperty("condition", out var conditionJson))
        {
            problems.Add($"{path}.condition: required.");
        }
        else if (!RuleExpressionJson.TryParse(conditionJson, out condition, out var error))
        {
            problems.Add($"{path}.condition{error![1..]}");
        }

        return problems.Count > before
            ? null
            : new Rule(id!, version, authority, citation!, applicability!, severity!.Value, condition!, explanation!, remediation, effective!);
    }

    private static SourceCitation? ReadCitation(JsonElement rule, string path, List<string> problems)
    {
        path += ".citation";
        if (!rule.TryGetProperty("citation", out var json) || json.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{path}: required. Every rule cites the public document it implements (K-RULE-2).");
            return null;
        }

        CheckProperties(json, path, problems, "document", "section", "uri");
        var document = Text(json, "document", path, problems, required: true);
        var section = Text(json, "section", path, problems, required: false);
        var uri = Text(json, "uri", path, problems, required: false);
        if (uri is not null && (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)))
        {
            problems.Add($"{path}.uri: \"{uri}\" is not an http(s) URI.");
        }

        var cited = $"{document} {section} {uri}";
        if (ProprietarySources.Any(s => cited.Contains(s, StringComparison.OrdinalIgnoreCase)))
        {
            problems.Add($"{path}: cites a vendor's validator. Rule packs implement public standards and regulatory documents only (K-RULE-2).");
        }

        return document is null ? null : new SourceCitation(document, section, uri);
    }

    private static RuleApplicability? ReadApplicability(JsonElement rule, string path, List<string> problems)
    {
        path += ".applicability";
        if (!rule.TryGetProperty("applicability", out var json) || json.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{path}: required, with at least a stage.");
            return null;
        }

        CheckProperties(json, path, problems, "stage", "standard", "standardVersion", "agency", "dataset", "variable");
        var stage = Enum(json, "stage", path, StageNames, problems, required: true);
        var standard = Text(json, "standard", path, problems, required: false);
        var standardVersion = Text(json, "standardVersion", path, problems, required: false);
        var agency = Text(json, "agency", path, problems, required: false);
        var dataset = Text(json, "dataset", path, problems, required: false);
        var variable = Text(json, "variable", path, problems, required: false);
        if (standardVersion is not null && standard is null)
        {
            problems.Add($"{path}.standardVersion: needs a standard.");
        }

        return stage is null ? null : new RuleApplicability(stage.Value, standard, standardVersion, agency, dataset, variable);
    }

    internal static EffectivePeriod? ReadEffective(JsonElement rule, string path, List<string> problems)
    {
        path += ".effective";
        if (!rule.TryGetProperty("effective", out var json) || json.ValueKind == JsonValueKind.Null)
        {
            return EffectivePeriod.Always;
        }

        if (json.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{path}: must be an object with \"from\" and \"until\" dates.");
            return null;
        }

        CheckProperties(json, path, problems, "from", "until");
        var from = Date(json, "from", path, problems);
        var until = Date(json, "until", path, problems);
        if (from is { } f && until is { } u && u <= f)
        {
            problems.Add($"{path}: \"until\" must be after \"from\".");
        }

        return new EffectivePeriod(from, until);
    }

    internal static DateOnly? Date(JsonElement json, string name, string path, List<string> problems)
    {
        var text = Text(json, name, path, problems, required: false);
        if (text is null)
        {
            return null;
        }

        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        problems.Add($"{path}.{name}: \"{text}\" is not a yyyy-MM-dd date.");
        return null;
    }

    internal static string? Text(JsonElement json, string name, string path, List<string> problems, bool required)
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
            problems.Add($"{path}.{name}: must be non-blank text of at most {MaxTextLength} characters without surrounding spaces.");
            return null;
        }

        return text;
    }

    internal static TEnum? Enum<TEnum>(JsonElement json, string name, string path, Dictionary<string, TEnum> names, List<string> problems, bool required)
        where TEnum : struct
    {
        var text = Text(json, name, path, problems, required);
        if (text is null)
        {
            return null;
        }

        if (names.TryGetValue(text, out var value))
        {
            return value;
        }

        problems.Add($"{path}.{name}: \"{text}\" is not one of {string.Join(", ", names.Keys)}.");
        return null;
    }

    internal static void CheckProperties(JsonElement json, string path, List<string> problems, params string[] known)
    {
        foreach (var property in json.EnumerateObject().Where(p => !known.Contains(p.Name, StringComparer.Ordinal)))
        {
            problems.Add($"{path}: unknown property \"{property.Name}\".");
        }
    }

    private static void WriteRule(Utf8JsonWriter writer, Rule rule)
    {
        writer.WriteStartObject();
        writer.WriteString("id", rule.Id);
        writer.WriteNumber("version", rule.Version);
        writer.WriteString("authority", Name(rule.Authority));
        writer.WriteStartObject("citation");
        writer.WriteString("document", rule.Citation.Document);
        WriteOptional(writer, "section", rule.Citation.Section);
        WriteOptional(writer, "uri", rule.Citation.Uri);
        writer.WriteEndObject();
        writer.WriteStartObject("applicability");
        writer.WriteString("stage", rule.Applicability.Stage.ToString());
        WriteOptional(writer, "standard", rule.Applicability.Standard);
        WriteOptional(writer, "standardVersion", rule.Applicability.StandardVersion);
        WriteOptional(writer, "agency", rule.Applicability.Agency);
        WriteOptional(writer, "dataset", rule.Applicability.Dataset);
        WriteOptional(writer, "variable", rule.Applicability.Variable);
        writer.WriteEndObject();
        writer.WriteString("severity", rule.Severity.ToString());
        writer.WritePropertyName("condition");
        RuleExpressionJson.Write(writer, rule.Condition);
        writer.WriteString("explanation", rule.Explanation);
        WriteOptional(writer, "remediation", rule.RemediationGuidance);
        if (rule.Effective.From is not null || rule.Effective.Until is not null)
        {
            writer.WriteStartObject("effective");
            WriteOptional(writer, "from", rule.Effective.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            WriteOptional(writer, "until", rule.Effective.Until?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteOptional(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    private static string Name(RuleAuthority authority) => Authorities.First(pair => pair.Value == authority).Key;

    private static readonly Dictionary<string, ValidationSeverity> SeverityNames =
        System.Enum.GetValues<ValidationSeverity>().ToDictionary(v => v.ToString(), StringComparer.Ordinal);

    private static readonly Dictionary<string, ValidationStage> StageNames =
        System.Enum.GetValues<ValidationStage>().ToDictionary(v => v.ToString(), StringComparer.Ordinal);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")]
    private static partial Regex RuleId();
}
