using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Cadence.Rules.Expressions;

namespace Cadence.Ddf;

/// <summary>
/// Thrown when a USDM study cannot be compiled into a form: a rule that does not parse, names an item the study does
/// not have or makes a visibility cycle, a duplicate item, or an unsupported item property. Every problem is listed.
/// </summary>
public sealed class DdfCompilationException : Exception
{
    /// <summary>Creates the exception from the problems found.</summary>
    public DdfCompilationException(IReadOnlyList<string> problems)
        : base("The study cannot be compiled: " + string.Join("; ", problems))
    {
        Problems = problems;
    }

    /// <summary>What is wrong, one entry per problem.</summary>
    public IReadOnlyList<string> Problems { get; }
}

/// <summary>
/// Thrown when a USDM document cannot be read at all: the text is not JSON, or a value has another JSON type than a
/// USDM study gives it (an array for the study, a number for a name). The caller sent a malformed document (#5040).
/// </summary>
public sealed class UsdmFormatException : Exception
{
    /// <summary>Creates the exception with a default message.</summary>
    public UsdmFormatException()
        : base("The USDM document cannot be read.")
    {
    }

    /// <summary>Creates the exception with <paramref name="message"/>.</summary>
    public UsdmFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with <paramref name="message"/> and the exception that caused it.</summary>
    public UsdmFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Turns item names into XML element names, the same way on every compile.</summary>
public static partial class DdfIdentifier
{
    /// <summary>
    /// A valid XML element name for <paramref name="raw"/>. A name of ASCII letters, digits and underscores that starts
    /// with a letter is kept; otherwise a space becomes <c>_</c>, any other character becomes <c>_</c> and its hex
    /// code, and a name that does not start with a letter or underscore, or starts with the reserved <c>xml</c>, gets
    /// the prefix <c>item_</c>. Returns null for a missing or blank name.
    /// </summary>
    public static string? Sanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string trimmed = raw.Trim();
        if (SimpleNameRegex().IsMatch(trimmed) && !trimmed.StartsWith("xml", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var builder = new StringBuilder(trimmed.Length + 8);
        foreach (char c in trimmed)
        {
            if (char.IsAsciiLetterOrDigit(c) || c == '_')
            {
                builder.Append(c);
            }
            else if (c == ' ')
            {
                builder.Append('_');
            }
            else
            {
                builder.Append('_').Append(((int)c).ToString(c <= 0xFF ? "x2" : "x4", CultureInfo.InvariantCulture));
            }
        }

        string name = builder.ToString();
        bool needsPrefix = !(char.IsAsciiLetter(name[0]) || name[0] == '_') || name.StartsWith("xml", StringComparison.OrdinalIgnoreCase);
        return needsPrefix ? "item_" + name : name;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex SimpleNameRegex();
}

/// <summary>Finds cycles in the visibility dependencies of form items (PRD-DDF-001 static circular dependency checks).</summary>
public static class DdfDependencyGraph
{
    /// <summary>
    /// The cycles in <paramref name="dependsOn"/> (item id to the item ids its skip logic reads): at least one through
    /// every group of items that depend on each other in a loop, each written <c>a -&gt; b -&gt; a</c> from its
    /// smallest id and sorted, so the output is the same on every run. An item whose skip logic reads itself is a cycle
    /// of one. Empty when there is no cycle.
    /// </summary>
    public static IReadOnlyList<string> FindCycles(IReadOnlyDictionary<string, IReadOnlyList<string>> dependsOn)
    {
        ArgumentNullException.ThrowIfNull(dependsOn);
        var cycles = new SortedSet<string>(StringComparer.Ordinal);
        var state = new Dictionary<string, bool>(StringComparer.Ordinal); // false: on the path, true: done
        var path = new List<string>();

        foreach (string start in dependsOn.Keys.Order(StringComparer.Ordinal))
        {
            Visit(start);
        }

        return [.. cycles];

        void Visit(string node)
        {
            if (state.TryGetValue(node, out bool done))
            {
                if (!done)
                {
                    cycles.Add(Describe(path.Skip(path.IndexOf(node)).ToList()));
                }

                return;
            }

            state[node] = false;
            path.Add(node);
            foreach (string next in dependsOn.GetValueOrDefault(node) ?? [])
            {
                Visit(next);
            }

            path.RemoveAt(path.Count - 1);
            state[node] = true;
        }
    }

    private static string Describe(List<string> cycle)
    {
        int first = cycle.IndexOf(cycle.Min(StringComparer.Ordinal)!);
        var rotated = cycle.Skip(first).Concat(cycle.Take(first)).ToList();
        rotated.Add(rotated[0]);
        return string.Join(" -> ", rotated);
    }
}

/// <summary>
/// The skip logic and constraints of one form item, read from its USDM <c>rules</c> array and compiled to XPath. Each
/// rule is <c>{"type": "skip_logic", "action": "show" | "hide", "condition": …}</c> or
/// <c>{"type": "constraint", "condition": …, "query_message": "…"}</c>, where the condition is a rule expression
/// (<see cref="RuleExpressionJson"/>) whose variables are item ids. A skip-logic condition
/// says when the item is shown (<c>show</c>, the default) or hidden (<c>hide</c>); a constraint condition says what a
/// valid value satisfies, as an OpenRosa constraint does, and the query message is shown when it does not.
/// </summary>
public sealed record DdfItemRules(string? Relevant, string? Constraint, string? ConstraintMessage, IReadOnlyList<string> SkipLogicReads, string? ConstraintSeverity = null, string? ConstraintCode = null)
{
    private static readonly HashSet<string> SkipLogicProperties = new(StringComparer.Ordinal) { "type", "action", "condition" };
    private static readonly HashSet<string> ConstraintProperties = new(StringComparer.Ordinal) { "type", "condition", "query_message", "severity", "code", "rule_id" };

    /// <summary>
    /// Reads the rules of item <paramref name="itemId"/>. Problems (a malformed rule, an unknown item) are added to
    /// <paramref name="problems"/>; the result is only usable when none were added.
    /// </summary>
    public static DdfItemRules Read(string itemId, JsonElement? rules, Func<string, string?> resolve, List<string> problems)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(problems);
        if (rules is not { } array || array.ValueKind == JsonValueKind.Null)
        {
            return new DdfItemRules(null, null, null, []);
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"Item '{itemId}': 'rules' must be an array.");
            return new DdfItemRules(null, null, null, []);
        }

        var relevant = new List<string>();
        var constraints = new List<string>();
        var messages = new List<string>();
        var reads = new List<string>();
        var severities = new List<string>();
        var codes = new List<string>();
        int index = 0;
        foreach (var rule in array.EnumerateArray())
        {
            string where = $"Item '{itemId}' rule {index++}";
            if (rule.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{where}: a rule must be an object.");
                continue;
            }

            string? type = rule.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            var allowed = type switch
            {
                "skip_logic" => SkipLogicProperties,
                "constraint" => ConstraintProperties,
                _ => null,
            };
            if (allowed is null)
            {
                problems.Add($"{where}: 'type' must be 'skip_logic' or 'constraint'.");
                continue;
            }

            foreach (var property in rule.EnumerateObject().Where(p => !allowed.Contains(p.Name)))
            {
                problems.Add($"{where}: unknown property '{property.Name}'.");
            }

            if (!rule.TryGetProperty("condition", out var condition))
            {
                problems.Add($"{where}: 'condition' is required.");
                continue;
            }

            if (!RuleExpressionJson.TryParse(condition, out var expression, out string? error))
            {
                problems.Add($"{where}: the condition does not parse: {error}");
                continue;
            }

            var (xpath, unresolved) = RuleXPathCompiler.Compile(expression!, resolve);
            problems.AddRange(unresolved.Select(name => $"{where}: the condition reads '{name}', which is not an item of this study."));

            if (type == "skip_logic")
            {
                string action = rule.TryGetProperty("action", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString()! : "show";
                if (action is not ("show" or "hide"))
                {
                    problems.Add($"{where}: 'action' must be 'show' or 'hide'.");
                    continue;
                }

                relevant.Add(action == "hide" ? $"not({xpath})" : xpath);
                reads.AddRange(RuleExpressionJson.Variables(expression!).Where(v => !reads.Contains(v, StringComparer.Ordinal)));
            }
            else
            {
                string? message = rule.TryGetProperty("query_message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                if (string.IsNullOrWhiteSpace(message))
                {
                    problems.Add($"{where}: a constraint needs a 'query_message'.");
                    continue;
                }

                string severity = "Error";
                if (rule.TryGetProperty("severity", out var s) && s.ValueKind == JsonValueKind.String)
                {
                    string rawSeverity = s.GetString() ?? "";
                    if (string.Equals(rawSeverity, "Error", StringComparison.OrdinalIgnoreCase))
                    {
                        severity = "Error";
                    }
                    else if (string.Equals(rawSeverity, "Warning", StringComparison.OrdinalIgnoreCase))
                    {
                        severity = "Warning";
                    }
                    else if (string.Equals(rawSeverity, "Notice", StringComparison.OrdinalIgnoreCase))
                    {
                        severity = "Notice";
                    }
                    else
                    {
                        problems.Add($"{where}: 'severity' must be 'Error', 'Warning', or 'Notice'.");
                        continue;
                    }
                }
                else if (rule.TryGetProperty("severity", out var sInvalid) && sInvalid.ValueKind != JsonValueKind.Null)
                {
                    problems.Add($"{where}: 'severity' must be 'Error', 'Warning', or 'Notice'.");
                    continue;
                }

                string? code = null;
                if (rule.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(c.GetString()))
                {
                    code = c.GetString();
                }
                else if (rule.TryGetProperty("rule_id", out var rId) && rId.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(rId.GetString()))
                {
                    code = rId.GetString();
                }

                constraints.Add(xpath);
                messages.Add(message);
                severities.Add(severity);
                if (code is not null)
                {
                    codes.Add(code);
                }
            }
        }

        string? constraintSeverity = null;
        string? constraintCode = null;

        if (constraints.Count > 0)
        {
            constraintSeverity = severities.Count == 0 ? "Error" : string.Join("; ", severities);
            if (codes.Count > 0)
            {
                constraintCode = string.Join("; ", codes);
            }
        }

        return new DdfItemRules(
            relevant.Count == 0 ? null : string.Join(" and ", relevant),
            constraints.Count == 0 ? null : string.Join(" and ", constraints),
            messages.Count == 0 ? null : string.Join("; ", messages),
            reads,
            constraintSeverity,
            constraintCode);
    }
}

/// <summary>
/// The text a form item shows: its label, description and hint. An item's <c>label</c>, <c>description</c> and
/// <c>hint</c> are in the compile's default language (the label defaults to the item's own name, never the element
/// name); its <c>localizations</c> object adds or replaces languages:
/// <c>{"fr": {"label": "…", "description": "…", "hint": "…"}}</c>.
/// </summary>
public sealed partial record ItemText(string Label, string? Description, string? Hint)
{
    private static readonly HashSet<string> Keys = new(StringComparer.Ordinal) { "label", "description", "hint" };

    /// <summary>
    /// The item's text in each language, ordered by language tag. Problems (a language tag that is not BCP 47, a language
    /// without a label, an unknown property) are added to <paramref name="problems"/>.
    /// </summary>
    public static IReadOnlyList<ItemLocalization> Localize(
        string itemId,
        ItemText text,
        JsonElement? localizations,
        string defaultLanguage,
        List<string> problems)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(problems);
        var result = new SortedDictionary<string, ItemLocalization>(StringComparer.Ordinal)
        {
            [defaultLanguage] = new ItemLocalization(defaultLanguage, text.Label, text.Description, text.Hint),
        };

        if (localizations is not { } languages || languages.ValueKind == JsonValueKind.Null)
        {
            return [.. result.Values];
        }

        if (languages.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"Item '{itemId}': 'localizations' must be an object keyed by language.");
            return [.. result.Values];
        }

        foreach (var language in languages.EnumerateObject())
        {
            string where = $"Item '{itemId}' localization '{language.Name}'";
            if (!LanguageTagRegex().IsMatch(language.Name))
            {
                problems.Add($"{where}: not a BCP 47 language tag.");
                continue;
            }

            if (language.Value.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{where}: must be an object with a 'label'.");
                continue;
            }

            foreach (var property in language.Value.EnumerateObject().Where(p => !Keys.Contains(p.Name)))
            {
                problems.Add($"{where}: unknown property '{property.Name}'.");
            }

            string? label = Value(language.Value, "label");
            if (label is null)
            {
                problems.Add($"{where}: a 'label' is required.");
                continue;
            }

            result[language.Name] = new ItemLocalization(language.Name, label, Value(language.Value, "description"), Value(language.Value, "hint"));
        }

        return [.. result.Values];
    }

    private static string? Value(JsonElement entry, string key) =>
        entry.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    [GeneratedRegex("^[A-Za-z]{2,3}(-[A-Za-z0-9]{1,8})*$")]
    private static partial Regex LanguageTagRegex();
}
