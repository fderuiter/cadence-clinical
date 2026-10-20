using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.Modules.Intelligence.Domain;

/// <summary>
/// Fills a task configuration's instructions (ADR-2630 §4.3). Placeholders are <c>{{name}}</c>, with names in lower
/// snake case. Rendering is strict: a placeholder without a value, or a value no placeholder uses, is an error, so a
/// template and its caller can't drift apart silently.
/// </summary>
public static partial class PromptTemplate
{
    /// <summary>The placeholder names <paramref name="template"/> uses, in order of first use.</summary>
    public static IReadOnlyList<string> Placeholders(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return [.. Placeholder().Matches(template).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Replaces every placeholder in <paramref name="template"/> with its value.</summary>
    /// <exception cref="ArgumentException">A placeholder has no value, or a value has no placeholder.</exception>
    public static string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);
        var used = Placeholders(template);
        var missing = used.Where(name => !values.ContainsKey(name)).ToList();
        var unused = values.Keys.Where(name => !used.Contains(name, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToList();
        if (missing.Count > 0 || unused.Count > 0)
        {
            throw new ArgumentException(
                $"The values do not match the template: missing {Describe(missing)}; unused {Describe(unused)}.", nameof(values));
        }

        return Placeholder().Replace(template, m => values[m.Groups[1].Value]);
    }

    private static string Describe(IReadOnlyCollection<string> names) => names.Count == 0 ? "none" : string.Join(", ", names);

    [GeneratedRegex(@"\{\{([a-z][a-z0-9_]{0,63})\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}
