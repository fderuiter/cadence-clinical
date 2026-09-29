using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Cadence.Ddf.Rules;

public static class CircularDependencyDetector
{
    public static List<string> DetectCircularDependencies(IEnumerable<IReadOnlyDictionary<string, object?>> rules)
    {
        var skipRules = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var r in rules)
        {
            if (r.TryGetValue("type", out var t) && t?.ToString() == "skip_logic")
            {
                if (r.TryGetValue("is_deleted", out var d) && (d is true || d?.ToString() == "True" || d?.ToString() == "true"))
                {
                    continue;
                }
                skipRules.Add(r);
            }
        }

        var adj = new Dictionary<string, List<string>>();
        foreach (var rule in skipRules)
        {
            if (!rule.TryGetValue("target_field", out var targetObj) || targetObj is null) continue;
            string target = targetObj.ToString()!;

            if (!rule.TryGetValue("condition", out var condObj) || condObj is null) continue;

            ExpressionNode? node = null;
            if (condObj is ExpressionNode en)
            {
                node = en;
            }
            else if (condObj is JsonElement je)
            {
                try
                {
                    node = JsonSerializer.Deserialize<ExpressionNode>(je.GetRawText());
                }
                catch
                {
                    node = null;
                }
            }

            if (node != null)
            {
                var refs = AstExtensions.ExtractFieldReferences(node);
                var set = new HashSet<string>();
                foreach (var rf in refs)
                {
                    set.Add(rf.FieldId);
                }
                adj[target] = new List<string>(set);
            }
        }

        var state = new Dictionary<string, int>(); // 0: unvisited, 1: visiting, 2: visited
        var cycles = new List<string>();
        var parent = new Dictionary<string, string?>();

        [SuppressMessage("SonarAnalyzer.CSharp", "S4143:Collection elements should not be overwritten unconditionally", Justification = "DFS state transition 1 -> 2")]
        bool Dfs(string u)
        {
            state[u] = 1;
            if (adj.TryGetValue(u, out var neighbors))
            {
                foreach (var v in neighbors)
                {
                    if (!state.TryGetValue(v, out int s) || s == 0)
                    {
                        parent[v] = u;
                        if (Dfs(v)) return true;
                    }
                    else if (s == 1)
                    {
                        var path = new List<string> { v };
                        string? curr = u;
                        while (curr != null && curr != v)
                        {
                            path.Add(curr);
                            parent.TryGetValue(curr, out curr);
                        }
                        path.Reverse();
                        cycles.Add(string.Join(" -> ", path));
                        return true;
                    }
                }
            }
            state[u] = 2;
            return false;
        }

        foreach (var node in adj.Keys)
        {
            if (!state.TryGetValue(node, out int s) || s == 0)
            {
                Dfs(node);
            }
        }

        return cycles;
    }
}
