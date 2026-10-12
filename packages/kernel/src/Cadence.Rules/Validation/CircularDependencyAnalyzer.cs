using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;

namespace Cadence.Rules.Validation;

/// <summary>
/// Analyzes rule packs for circular logic dependencies across cross-form and field-level rules.
/// </summary>
public static class CircularDependencyAnalyzer
{
    /// <summary>
    /// Analyzes the specified rule packs and returns a list of detected circular logic loops.
    /// </summary>
    /// <param name="packs">The rule packs to analyze.</param>
    /// <returns>A list of circular dependency error descriptions.</returns>
    public static IReadOnlyList<string> Analyze(IEnumerable<RulePack> packs)
    {
        ArgumentNullException.ThrowIfNull(packs);

        var cycles = new List<string>();
        var rules = packs.SelectMany(p => p.Rules).ToList();
        if (rules.Count == 0)
        {
            return cycles;
        }

        var graph = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var rule in rules)
        {
            if (!graph.ContainsKey(rule.Id))
            {
                graph[rule.Id] = new HashSet<string>(StringComparer.Ordinal);
            }

            var vars = RuleExpressionJson.Variables(rule.Condition);

            var targetDataset = rule.Applicability.Dataset;
            if (!string.IsNullOrEmpty(targetDataset) && vars.Contains(targetDataset, StringComparer.Ordinal))
            {
                cycles.Add($"Circular dependency detected: rule {rule.Id} reads variable '{targetDataset}' which matches its target dataset.");
            }

            foreach (var other in rules)
            {
                if (string.Equals(rule.Id, other.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                var otherTarget = other.Applicability.Dataset;
                if (!string.IsNullOrEmpty(otherTarget) && vars.Contains(otherTarget, StringComparer.Ordinal))
                {
                    graph[rule.Id].Add(other.Id);
                }
            }
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var recStack = new HashSet<string>(StringComparer.Ordinal);
        var currentPath = new List<string>();

        foreach (var ruleId in graph.Keys)
        {
            if (!visited.Contains(ruleId))
            {
                Dfs(ruleId, graph, visited, recStack, currentPath, cycles);
            }
        }

        return cycles.Distinct(StringComparer.Ordinal).ToList();
    }

    private static void Dfs(
        string node,
        Dictionary<string, HashSet<string>> graph,
        HashSet<string> visited,
        HashSet<string> recStack,
        List<string> currentPath,
        List<string> cycles)
    {
        visited.Add(node);
        recStack.Add(node);
        currentPath.Add(node);

        if (graph.TryGetValue(node, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (!visited.Contains(neighbor))
                {
                    Dfs(neighbor, graph, visited, recStack, currentPath, cycles);
                }
                else if (recStack.Contains(neighbor))
                {
                    var cycleStartIdx = currentPath.IndexOf(neighbor);
                    if (cycleStartIdx >= 0)
                    {
                        var cyclePath = currentPath.Skip(cycleStartIdx).ToList();
                        cyclePath.Add(neighbor);
                        var cycleStr = string.Join(" -> ", cyclePath);
                        cycles.Add($"Multi-rule circular logic loop detected: {cycleStr}");
                    }
                }
            }
        }

        recStack.Remove(node);
        currentPath.RemoveAt(currentPath.Count - 1);
    }
}
