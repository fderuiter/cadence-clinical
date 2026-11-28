using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

using Cadence.Kernel.Protocol.Import;
using Cadence.Rules.Packs;

namespace Cadence.Ddf;

internal static class ProtocolRulePackMerger
{
    internal static (ImportedRulePack? RulePack, IReadOnlyList<string> Problems) CompileImportedRulePack(
        IReadOnlyList<Rule>? incomingRules,
        IReadOnlyDictionary<string, string> idRenameMap,
        string title,
        string? studyOid)
    {
        if (incomingRules is not { Count: > 0 })
        {
            return (null, []);
        }

        var usedRuleIds = new HashSet<string>(StringComparer.Ordinal);
        List<Rule> remappedRules;
        try
        {
            remappedRules = incomingRules.Select(rule =>
            {
                var mappedDataset = rule.Applicability.Dataset is not null && idRenameMap.TryGetValue(rule.Applicability.Dataset, out var ds)
                    ? ds
                    : rule.Applicability.Dataset;
                var mappedCondition = OdmFormalExpressionParser.RewriteVariables(
                    rule.Condition,
                    varName => idRenameMap.TryGetValue(varName, out var mappedVar) ? mappedVar : varName);
                var uniqueRuleId = OdmDesignImporter.AllocateRuleId(rule.Id, usedRuleIds);
                return rule with
                {
                    Id = uniqueRuleId,
                    Applicability = rule.Applicability with { Dataset = mappedDataset },
                    Condition = mappedCondition,
                };
            }).ToList();
        }
        catch (FormatException ex)
        {
            return (null, [ex.Message]);
        }

        var packTitle = $"{title} Edit Checks";
        if (packTitle.Length > RulePackJson.MaxTextLength)
        {
            packTitle = packTitle[..RulePackJson.MaxTextLength];
        }

        var pack = new RulePack(RuleAuthority.Study, packTitle, remappedRules);
        JsonElement packJson;
        try
        {
            packJson = RulePackJson.ToJson(pack);
        }
        catch (JsonException ex)
        {
            return (null, [$"Compiled rule pack exceeds JSON limits: {ex.Message}"]);
        }

        var (_, packProblems) = RulePackJson.Read(packJson);
        if (packProblems.Count > 0)
        {
            return (null, packProblems);
        }

        var keySlug = BuildLibraryKeySlug(ProtocolDesignMerger.FirstNonBlank(studyOid, title, "odm")!);
        return (new ImportedRulePack(keySlug, packTitle, remappedRules.Count, packJson), []);
    }

    internal static ImportedRulePack? MergeRulePacks(
        ImportedRulePack? first,
        ImportedRulePack? second,
        string designTitle,
        out IReadOnlyList<string> problems)
    {
        problems = [];
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        var (firstPack, firstProblems) = RulePackJson.Read(first.Content);
        if (firstProblems.Count > 0 || firstPack is null)
        {
            problems = firstProblems;
            return null;
        }

        var (secondPack, secondProblems) = RulePackJson.Read(second.Content);
        if (secondProblems.Count > 0 || secondPack is null)
        {
            problems = secondProblems;
            return null;
        }

        var usedRuleIds = new HashSet<string>(StringComparer.Ordinal);
        var combinedRules = new List<Rule>(firstPack.Rules.Count + secondPack.Rules.Count);
        foreach (var rule in firstPack.Rules.Concat(secondPack.Rules))
        {
            var uniqueId = OdmDesignImporter.AllocateRuleId(rule.Id, usedRuleIds);
            combinedRules.Add(uniqueId == rule.Id ? rule : rule with { Id = uniqueId });
        }

        var packTitle = $"{designTitle} Edit Checks";
        if (packTitle.Length > RulePackJson.MaxTextLength)
        {
            packTitle = packTitle[..RulePackJson.MaxTextLength];
        }

        var merged = new RulePack(RuleAuthority.Study, packTitle, combinedRules);
        var mergedJson = RulePackJson.ToJson(merged);
        var (_, mergedProblems) = RulePackJson.Read(mergedJson);
        if (mergedProblems.Count > 0)
        {
            problems = mergedProblems;
            return null;
        }

        return new ImportedRulePack(first.Key, packTitle, combinedRules.Count, mergedJson);
    }

    private static string BuildLibraryKeySlug(string source)
    {
        var sb = new StringBuilder("odm-");
        bool lastDash = true;
        foreach (var ch in source)
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastDash = false;
            }
            else if (!lastDash)
            {
                sb.Append('-');
                lastDash = true;
            }
        }

        var slug = sb.ToString().Trim('-');
        if (slug.Length > 52)
        {
            slug = slug[..52].TrimEnd('-');
        }

        return $"{slug}-edit-checks";
    }
}
