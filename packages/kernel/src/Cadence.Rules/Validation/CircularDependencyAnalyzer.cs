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

        var rules = packs
            .SelectMany(p => p.Rules)
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .ThenBy(r => r.Version)
            .ToList();
        if (rules.Count == 0)
        {
            return [];
        }

        var selfCycles = new SortedSet<string>(StringComparer.Ordinal);
        var graph = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (var rule in rules)
        {
            if (!graph.TryGetValue(rule.Id, out var edges))
            {
                edges = new SortedSet<string>(StringComparer.Ordinal);
                graph[rule.Id] = edges;
            }

            var vars = RuleExpressionJson.Variables(rule.Condition);

            if (TryMatchTargetVariable(rule, vars, out var matchedSelfVariable))
            {
                selfCycles.Add($"Circular dependency detected: rule {rule.Id} reads variable '{matchedSelfVariable}' which matches its target variable.");
            }

            foreach (var other in rules)
            {
                if (string.Equals(rule.Id, other.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                if (TryMatchTargetVariable(other, vars, out _))
                {
                    edges.Add(other.Id);
                }
            }
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var recStack = new HashSet<string>(StringComparer.Ordinal);
        var currentPath = new List<string>();
        var loopCycles = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var ruleId in graph.Keys)
        {
            if (!visited.Contains(ruleId))
            {
                Dfs(ruleId, graph, visited, recStack, currentPath, loopCycles);
            }
        }

        return [.. selfCycles, .. loopCycles];
    }

    private static bool TryMatchTargetVariable(Rule targetRule, IReadOnlyList<string> referencedVars, out string matched)
    {
        var rawTarget = targetRule.Applicability.Variable;
        if (string.IsNullOrWhiteSpace(rawTarget))
        {
            matched = string.Empty;
            return false;
        }

        var dataset = targetRule.Applicability.Dataset;
        if (!string.IsNullOrWhiteSpace(dataset))
        {
            var prefix = dataset + ".";
            var bare = rawTarget.StartsWith(prefix, StringComparison.Ordinal)
                ? rawTarget[prefix.Length..]
                : rawTarget;
            var qualified = prefix + bare;

            foreach (var v in referencedVars)
            {
                if (string.Equals(v, rawTarget, StringComparison.Ordinal)
                    || string.Equals(v, qualified, StringComparison.Ordinal)
                    || string.Equals(v, bare, StringComparison.Ordinal))
                {
                    matched = v;
                    return true;
                }
            }

            matched = string.Empty;
            return false;
        }

        var suffix = "." + rawTarget;
        var targetHasDot = rawTarget.Contains('.', StringComparison.Ordinal);
        foreach (var v in referencedVars)
        {
            if (string.Equals(v, rawTarget, StringComparison.Ordinal)
                || (!targetHasDot && v.EndsWith(suffix, StringComparison.Ordinal)))
            {
                matched = v;
                return true;
            }
        }

        matched = string.Empty;
        return false;
    }

    private static void Dfs(
        string node,
        SortedDictionary<string, SortedSet<string>> graph,
        HashSet<string> visited,
        HashSet<string> recStack,
        List<string> currentPath,
        SortedSet<string> cycles)
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
                        var cycleNodes = currentPath.Skip(cycleStartIdx).ToList();
                        var minNode = cycleNodes.Min(StringComparer.Ordinal)!;
                        var minIdx = cycleNodes.IndexOf(minNode);
                        var rotated = cycleNodes.Skip(minIdx).Concat(cycleNodes.Take(minIdx)).ToList();
                        rotated.Add(rotated[0]);
                        var cycleStr = string.Join(" -> ", rotated);
                        cycles.Add($"Multi-rule circular logic loop detected: {cycleStr}");
                    }
                }
            }
        }

        recStack.Remove(node);
        currentPath.RemoveAt(currentPath.Count - 1);
    }
}
