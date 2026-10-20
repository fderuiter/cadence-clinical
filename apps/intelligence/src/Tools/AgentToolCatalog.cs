using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Time;

namespace Cadence.Modules.Intelligence.Tools;

/// <summary>
/// Every agent tool the host registered (ADR-2633 §4.3), by name. A run opens an <see cref="AgentToolSession"/> on it
/// with its principal and allow-list; the catalogue itself is shared and holds no run state.
/// </summary>
public sealed class AgentToolCatalog
{
    private readonly FrozenDictionary<string, IAgentTool> tools;
    private readonly IClock clock;

    /// <summary>Creates the catalogue from the registered tools.</summary>
    /// <param name="tools">Every registered tool.</param>
    /// <param name="clock">Times each tool call.</param>
    /// <exception cref="ArgumentException">Two tools share a name.</exception>
    public AgentToolCatalog(IEnumerable<IAgentTool> tools, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var list = tools.ToList();
        var repeated = list.GroupBy(t => t.Name, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (repeated.Count > 0)
        {
            throw new InvalidOperationException($"Two agent tools are registered as {string.Join(", ", repeated)}.");
        }

        this.tools = list.ToFrozenDictionary(t => t.Name, StringComparer.Ordinal);
        this.clock = clock;
    }

    /// <summary>The registered tool names.</summary>
    public IEnumerable<string> Names => tools.Keys;

    /// <summary>True when a tool named <paramref name="name"/> is registered.</summary>
    public bool Contains(string name) => tools.ContainsKey(name);

    /// <summary>The tools on <paramref name="allowList"/>, by name, for describing them (an MCP tool list, a review screen).</summary>
    public IReadOnlyList<IAgentTool> ToolsFor(ToolAllowList allowList)
    {
        ArgumentNullException.ThrowIfNull(allowList);
        return [.. allowList.Names.Select(n => tools[n]).OrderBy(t => t.Name, StringComparer.Ordinal)];
    }

    /// <summary>Opens a run's session: only the tools on <paramref name="allowList"/>, reading as <paramref name="principal"/>.</summary>
    public AgentToolSession OpenSession(AgentPrincipal principal, ToolAllowList allowList)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(allowList);
        return new AgentToolSession(principal, ToolsFor(allowList), clock);
    }
}
