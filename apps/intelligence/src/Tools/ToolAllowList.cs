using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

using Cadence.Modules.Intelligence.Domain;

namespace Cadence.Modules.Intelligence.Tools;

/// <summary>
/// The tools one task configuration may use (ADR-2633 §4.4). Built against the catalogue, so an unknown or repeated
/// name refuses the configuration when it is loaded rather than when the model happens to call it. An empty list
/// means no tools, never all tools.
/// </summary>
public sealed class ToolAllowList
{
    private ToolAllowList(FrozenSet<string> names) => Names = names;

    /// <summary>The allowed tool names.</summary>
    public FrozenSet<string> Names { get; }

    /// <summary>An allow-list with no tools.</summary>
    public static ToolAllowList None { get; } = new(FrozenSet<string>.Empty);

    /// <summary>True when <paramref name="tool"/> is allowed.</summary>
    public bool Allows(string tool) => Names.Contains(tool);

    /// <summary>The allow-list for <paramref name="names"/>, refusing names <paramref name="catalog"/> does not know.</summary>
    /// <exception cref="ArgumentException">A name is unknown, blank or repeated.</exception>
    public static ToolAllowList Create(IEnumerable<string> names, AgentToolCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(catalog);
        var list = names.ToList();
        var repeated = list.GroupBy(n => n, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (repeated.Count > 0)
        {
            throw new ArgumentException($"The allow-list repeats {string.Join(", ", repeated)}.", nameof(names));
        }

        var unknown = list.Where(n => !catalog.Contains(n)).ToList();
        if (unknown.Count > 0)
        {
            throw new ArgumentException($"The allow-list names tools that do not exist: {string.Join(", ", unknown)}.", nameof(names));
        }

        return new ToolAllowList(list.ToFrozenSet(StringComparer.Ordinal));
    }

    /// <summary>The allow-list a task configuration names in <see cref="TaskConfiguration.Tools"/> (ADR-2631).</summary>
    /// <exception cref="ArgumentException">The configuration names a tool <paramref name="catalog"/> does not know.</exception>
    public static ToolAllowList For(TaskConfiguration configuration, AgentToolCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return Create(configuration.Tools, catalog);
    }
}
