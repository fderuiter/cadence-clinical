using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

using Cadence.Kernel.Simulation;
using Cadence.Kernel.Standards.Mapping;
using Cadence.Rules.Packs;

namespace Cadence.Kernel.Application.Simulation;

/// <summary>
/// Deterministic structural diff over <see cref="MappingSpecification"/> and <see cref="RulePack"/> candidates
/// (ADR-2480 §4.1, K-SIM-3).
/// </summary>
internal static class SimulationSpecificationDiff
{
    public const string RootId = "$root";
    public static readonly EngineVersion MappingDiffEngine = new("standards.mapping_diff", "1");
    public static readonly EngineVersion RulePackDiffEngine = new("rules.pack_diff", "1");

    public static IReadOnlyList<ElementChange> CompareMappingSpecifications(
        MappingSpecification? baseSpec,
        MappingSpecification candidateSpec)
    {
        ArgumentNullException.ThrowIfNull(candidateSpec);
        var candidateJson = JsonNode.Parse(MappingSpecificationJson.ToJson(candidateSpec).GetRawText())!.AsObject();
        var baseJson = baseSpec is null
            ? null
            : JsonNode.Parse(MappingSpecificationJson.ToJson(baseSpec).GetRawText())!.AsObject();

        return CompareChildCollection(baseJson, candidateJson, "mappings", "MappingSpecification", "VariableMapping");
    }

    public static IReadOnlyList<ElementChange> CompareRulePacks(
        RulePack? basePack,
        RulePack candidatePack)
    {
        ArgumentNullException.ThrowIfNull(candidatePack);
        var candidateJson = JsonNode.Parse(RulePackJson.ToJson(candidatePack).GetRawText())!.AsObject();
        var baseJson = basePack is null
            ? null
            : JsonNode.Parse(RulePackJson.ToJson(basePack).GetRawText())!.AsObject();

        return CompareChildCollection(baseJson, candidateJson, "rules", "RulePack", "Rule");
    }

    private static IReadOnlyList<ElementChange> CompareChildCollection(
        JsonObject? baseRoot,
        JsonObject candidateRoot,
        string arrayPropertyName,
        string rootInstanceType,
        string childInstanceType)
    {
        var changes = new List<ElementChange>();
        var candidateChildren = IndexById(candidateRoot[arrayPropertyName] as JsonArray);

        if (baseRoot is null)
        {
            changes.Add(new ElementChange(RootId, rootInstanceType, ElementChangeKind.Added, []));
            foreach (var id in candidateChildren.Keys.Order(StringComparer.Ordinal))
            {
                changes.Add(new ElementChange(id, childInstanceType, ElementChangeKind.Added, []));
            }

            return [.. changes.OrderBy(c => c.ElementId, StringComparer.Ordinal)];
        }

        var baseChildren = IndexById(baseRoot[arrayPropertyName] as JsonArray);
        var rootPointers = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var prop in baseRoot.Select(kv => kv.Key).Union(candidateRoot.Select(kv => kv.Key), StringComparer.Ordinal))
        {
            if (string.Equals(prop, arrayPropertyName, StringComparison.Ordinal))
            {
                var baseIds = baseChildren.Keys.ToList();
                var candIds = candidateChildren.Keys.ToList();
                if (!baseIds.SequenceEqual(candIds, StringComparer.Ordinal))
                {
                    rootPointers.Add($"/{EscapePointer(prop)}");
                }

                continue;
            }

            CollectDifferingPointers(baseRoot[prop], candidateRoot[prop], $"/{EscapePointer(prop)}", rootPointers);
        }

        if (rootPointers.Count > 0)
        {
            changes.Add(new ElementChange(RootId, rootInstanceType, ElementChangeKind.Changed, [.. rootPointers]));
        }

        foreach (var id in baseChildren.Keys.Union(candidateChildren.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var before = baseChildren.GetValueOrDefault(id);
            var after = candidateChildren.GetValueOrDefault(id);
            if (before is null && after is not null)
            {
                changes.Add(new ElementChange(id, childInstanceType, ElementChangeKind.Added, []));
            }
            else if (before is not null && after is null)
            {
                changes.Add(new ElementChange(id, childInstanceType, ElementChangeKind.Removed, []));
            }
            else if (before is not null && after is not null)
            {
                var pointers = new SortedSet<string>(StringComparer.Ordinal);
                CollectDifferingPointers(before, after, string.Empty, pointers);
                if (pointers.Count > 0)
                {
                    changes.Add(new ElementChange(id, childInstanceType, ElementChangeKind.Changed, [.. pointers]));
                }
            }
        }

        return [.. changes.OrderBy(c => c.ElementId, StringComparer.Ordinal)];
    }

    private static Dictionary<string, JsonObject> IndexById(JsonArray? array)
    {
        var map = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        if (array is null)
        {
            return map;
        }

        foreach (var node in array)
        {
            if (node is JsonObject obj && obj["id"]?.GetValue<string>() is { Length: > 0 } id)
            {
                map[id] = obj;
            }
        }

        return map;
    }

    private static void CollectDifferingPointers(
        JsonNode? left,
        JsonNode? right,
        string path,
        SortedSet<string> pointers)
    {
        if (JsonNode.DeepEquals(left, right))
        {
            return;
        }

        if (left is JsonObject leftObj && right is JsonObject rightObj)
        {
            foreach (var key in leftObj.Select(kv => kv.Key).Union(rightObj.Select(kv => kv.Key), StringComparer.Ordinal))
            {
                var childPath = $"{path}/{EscapePointer(key)}";
                CollectDifferingPointers(leftObj[key], rightObj[key], childPath, pointers);
            }

            return;
        }

        pointers.Add(path.Length == 0 ? "/" : path);
    }

    private static string EscapePointer(string token) =>
        token.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
