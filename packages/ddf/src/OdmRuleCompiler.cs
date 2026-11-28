using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

using Cadence.Kernel.Evidence;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Validation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;

using static Cadence.Ddf.OdmXmlNavigationAndVendorNotes;

namespace Cadence.Ddf;

internal static class OdmRuleCompiler
{
    public static List<Rule> CompileOdmRules(
        string citationDocumentLabel,
        IReadOnlyList<OdmImportedItemMeta> importedItems,
        IReadOnlyDictionary<string, string> formIdByOid,
        IReadOnlyDictionary<string, XElement> conditionDefs,
        IReadOnlyList<(string ConditionOid, OdmImportedItemMeta TargetItem)> conditionTargets,
        List<ProtocolImportNote> notes,
        List<string> problems)
    {
        var rules = new List<Rule>();
        var usedRuleIds = new HashSet<string>(StringComparer.Ordinal);
        var itemsByLookup = BuildItemLookup(importedItems, formIdByOid);
        var docTitle = TruncateText($"CDISC ODM Import ({citationDocumentLabel})", RulePackJson.MaxTextLength);

        foreach (var meta in importedItems)
        {
            int rcIndex = 0;
            foreach (var rangeCheck in Children(meta.ItemDef, "RangeCheck"))
            {
                rcIndex++;
                var comparator = Attr(rangeCheck, "Comparator")?.ToUpperInvariant();
                var softHard = Attr(rangeCheck, "SoftHard");
                var severity = string.Equals(softHard, "Hard", StringComparison.OrdinalIgnoreCase)
                    ? ValidationSeverity.Error
                    : ValidationSeverity.Warning;
                var errorMessage = TranslatedText(Child(rangeCheck, "ErrorMessage"));
                var checkValues = Children(rangeCheck, "CheckValue")
                    .Select(v => v.Value?.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .Select(v => v!)
                    .ToList();
                var formalEl = Children(rangeCheck, "FormalExpression").FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Value));

                RuleExpression? condition = null;
                if (!string.IsNullOrWhiteSpace(comparator) && checkValues.Count > 0)
                {
                    condition = BuildRangeCheckExpression(meta, comparator, checkValues, problems);
                }
                else if (formalEl is not null)
                {
                    var rawExpr = formalEl.Value.Trim();
                    if (OdmFormalExpressionParser.TryParse(
                        rawExpr,
                        token => ResolveVariableForForm(token, meta.FormId, itemsByLookup),
                        meta.Item.Id,
                        out var parsedFormal,
                        out var parseError,
                        out var limitExceeded) && parsedFormal is not null)
                    {
                        condition = rawExpr.StartsWith('{')
                            ? parsedFormal
                            : new LogicalExpression(
                                LogicalOperator.And,
                                [
                                    new FunctionExpression(RuleFunction.IsNotEmpty, [new VariableExpression(meta.Item.Id)]),
                                    new NotExpression(parsedFormal),
                                ]);
                    }
                    else if (limitExceeded)
                    {
                        problems.Add($"Item '{meta.ItemOid}' RangeCheck #{rcIndex} FormalExpression exceeds expression complexity limits ({parseError}).");
                    }
                    else
                    {
                        notes.Add(new ProtocolImportNote(
                            $"{meta.RawFormOid}/{meta.ItemOid}",
                            $"RangeCheck #{rcIndex} FormalExpression on item '{meta.ItemName}' could not be compiled ({parseError}); skipped."));
                    }
                }
                else
                {
                    problems.Add($"Item '{meta.ItemOid}' RangeCheck #{rcIndex} must define either Comparator with <CheckValue> or a <FormalExpression>.");
                }

                if (condition is null)
                {
                    continue;
                }

                var ruleId = OdmDesignImporter.AllocateRuleId($"RC.{meta.Item.Id}.{rcIndex}", usedRuleIds);
                var explanation = TruncateText(
                    FirstNonBlank(
                        errorMessage,
                        !string.IsNullOrWhiteSpace(comparator)
                            ? $"Item '{meta.Item.Prompt ?? meta.ItemName}' must satisfy {comparator} {string.Join(", ", checkValues)}."
                            : $"Item '{meta.Item.Prompt ?? meta.ItemName}' failed range check #{rcIndex}.")!,
                    RulePackJson.MaxTextLength);

                rules.Add(new Rule(
                    ruleId,
                    1,
                    RuleAuthority.Study,
                    new SourceCitation(docTitle, $"{meta.RawFormOid}/{meta.ItemOid}", null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, meta.FormId),
                    severity,
                    condition,
                    explanation,
                    RemediationGuidance: "Verify the source value and correct or explain the discrepancy.",
                    EffectivePeriod.Always));
            }
        }

        var targetsByCondition = conditionTargets
            .GroupBy(t => t.ConditionOid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(x => x.TargetItem).ToList(), StringComparer.Ordinal);

        foreach (var (condOid, condDef) in conditionDefs)
        {
            var condName = FirstNonBlank(Attr(condDef, "Name"), condOid)!;
            var condDesc = TranslatedText(Child(condDef, "Description"));
            var formalElements = Children(condDef, "FormalExpression")
                .Where(e => !string.IsNullOrWhiteSpace(e.Value))
                .ToList();

            if (formalElements.Count == 0)
            {
                notes.Add(new ProtocolImportNote(
                    condOid,
                    $"ODM <ConditionDef> '{condName}' contains no <FormalExpression>; recorded description only."));
                continue;
            }

            RuleExpression? rawParsed = null;
            string? lastParseError = null;
            var conditionLimitExceeded = false;
            foreach (var fe in formalElements)
            {
                if (OdmFormalExpressionParser.TryParse(
                    fe.Value,
                    null,
                    null,
                    out var candidateExpr,
                    out lastParseError,
                    out var feLimitExceeded)
                    && candidateExpr is not null)
                {
                    rawParsed = candidateExpr;
                    break;
                }

                if (feLimitExceeded)
                {
                    conditionLimitExceeded = true;
                    break;
                }
            }

            if (conditionLimitExceeded)
            {
                problems.Add($"ODM <ConditionDef> '{condOid}' FormalExpression exceeds expression complexity limits ({lastParseError}).");
                continue;
            }

            if (rawParsed is null)
            {
                notes.Add(new ProtocolImportNote(
                    condOid,
                    $"ODM <ConditionDef> '{condName}' FormalExpression could not be compiled ({lastParseError}); skipped."));
                continue;
            }

            try
            {
                if (targetsByCondition.TryGetValue(condOid, out var targets) && targets.Count > 0)
                {
                    foreach (var target in targets)
                    {
                        var resolvedCond = OdmFormalExpressionParser.RewriteVariables(
                            rawParsed,
                            token => ResolveVariableForForm(token, target.FormId, itemsByLookup),
                            target.Item.Id);
                        var exceptionViolation = new LogicalExpression(
                            LogicalOperator.And,
                            [
                                resolvedCond,
                                new FunctionExpression(RuleFunction.IsNotEmpty, [new VariableExpression(target.Item.Id)]),
                            ]);
                        var ruleId = OdmDesignImporter.AllocateRuleId($"CD.{condOid}.{target.Item.Id}", usedRuleIds);
                        var explanation = TruncateText(
                            FirstNonBlank(
                                condDesc,
                                $"Item '{target.Item.Prompt ?? target.ItemName}' must not be collected when condition '{condName}' holds.")!,
                            RulePackJson.MaxTextLength);

                        rules.Add(new Rule(
                            ruleId,
                            1,
                            RuleAuthority.Study,
                            new SourceCitation(docTitle, condOid, null),
                            new RuleApplicability(ValidationStage.Collection, null, null, null, target.FormId),
                            ValidationSeverity.Warning,
                            exceptionViolation,
                            explanation,
                            RemediationGuidance: "Confirm whether the conditional collection exception applies or clear the unneeded item.",
                            EffectivePeriod.Always));
                    }
                }
                else
                {
                    var referencedVars = RuleExpressionJson.Variables(rawParsed);
                    var primaryFormId = ResolvePrimaryFormForStandaloneCondition(referencedVars, importedItems, itemsByLookup);
                    if (primaryFormId is null)
                    {
                        continue;
                    }

                    var resolvedCond = OdmFormalExpressionParser.RewriteVariables(
                        rawParsed,
                        token => ResolveVariableForForm(token, primaryFormId, itemsByLookup));
                    var ruleId = OdmDesignImporter.AllocateRuleId($"CD.{condOid}", usedRuleIds);
                    var explanation = TruncateText(
                        FirstNonBlank(condDesc, condName, $"Condition '{condOid}' reported a discrepancy.")!,
                        RulePackJson.MaxTextLength);

                    rules.Add(new Rule(
                        ruleId,
                        1,
                        RuleAuthority.Study,
                        new SourceCitation(docTitle, condOid, null),
                        new RuleApplicability(ValidationStage.Collection, null, null, null, primaryFormId),
                        ValidationSeverity.Warning,
                        resolvedCond,
                        explanation,
                        RemediationGuidance: "Verify the related clinical items and resolve the conditional discrepancy.",
                        EffectivePeriod.Always));
                }
            }
            catch (FormatException ex)
            {
                problems.Add($"ODM <ConditionDef> '{condOid}' FormalExpression exceeds expression complexity limits ({ex.Message}).");
            }
        }

        return rules;
    }

    private static RuleExpression? BuildRangeCheckExpression(
        OdmImportedItemMeta meta,
        string comparator,
        IReadOnlyList<string> checkValues,
        List<string> problems)
    {
        var variable = new VariableExpression(meta.Item.Id);
        var literals = new List<RuleExpression>(checkValues.Count);
        foreach (var rawVal in checkValues)
        {
            var effectiveVal = meta.Item.Terminology is not null
                ? OdmDesignImporter.BoundTerminologyToken(rawVal) ?? rawVal
                : rawVal;
            literals.Add(new LiteralExpression(ToTypedRuleValue(effectiveVal, meta.Item.ExpectedKind)));
        }

        switch (comparator)
        {
            case "LT":
                return new ComparisonExpression(ComparisonOperator.GreaterOrEqual, variable, literals[0]);
            case "LE":
                return new ComparisonExpression(ComparisonOperator.Greater, variable, literals[0]);
            case "GT":
                return new ComparisonExpression(ComparisonOperator.LessOrEqual, variable, literals[0]);
            case "GE":
                return new ComparisonExpression(ComparisonOperator.Less, variable, literals[0]);
            case "EQ":
                return new LogicalExpression(
                    LogicalOperator.And,
                    [
                        new FunctionExpression(RuleFunction.IsNotEmpty, [variable]),
                        new ComparisonExpression(ComparisonOperator.NotEqual, variable, literals[0]),
                    ]);
            case "NE":
                return new ComparisonExpression(ComparisonOperator.Equal, variable, literals[0]);
            case "IN":
                return new LogicalExpression(
                    LogicalOperator.And,
                    [
                        new FunctionExpression(RuleFunction.IsNotEmpty, [variable]),
                        new FunctionExpression(RuleFunction.NotIn, [variable, .. literals]),
                    ]);
            case "NOTIN":
                return new FunctionExpression(RuleFunction.In, [variable, .. literals]);
            default:
                problems.Add($"Item '{meta.ItemOid}' RangeCheck uses unsupported ODM Comparator '{comparator}'.");
                return null;
        }
    }

    private static RuleValue ToTypedRuleValue(string rawValue, ObservationValueKind? expectedKind)
    {
        if (expectedKind == ObservationValueKind.Quantity
            && decimal.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
        {
            return RuleValue.FromNumber(numeric);
        }

        if (expectedKind == ObservationValueKind.Boolean)
        {
            if (string.Equals(rawValue, "true", StringComparison.OrdinalIgnoreCase) || rawValue == "1")
            {
                return RuleValue.True;
            }

            if (string.Equals(rawValue, "false", StringComparison.OrdinalIgnoreCase) || rawValue == "0")
            {
                return RuleValue.False;
            }
        }

        if (expectedKind is null
            && decimal.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var inferredNumeric))
        {
            return RuleValue.FromNumber(inferredNumeric);
        }

        return RuleValue.FromText(rawValue);
    }

    private static Dictionary<string, List<(string FormId, string ItemId)>> BuildItemLookup(
        IReadOnlyList<OdmImportedItemMeta> items,
        IReadOnlyDictionary<string, string> formIdByOid)
    {
        var map = new Dictionary<string, List<(string FormId, string ItemId)>>(StringComparer.Ordinal);
        void AddKey(string key, string formId, string itemId)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (!map.TryGetValue(key, out var list))
            {
                list = [];
                map[key] = list;
            }

            if (!list.Contains((formId, itemId)))
            {
                list.Add((formId, itemId));
            }
        }

        foreach (var meta in items)
        {
            AddKey(meta.Item.Id, meta.FormId, meta.Item.Id);
            AddKey(meta.ItemOid, meta.FormId, meta.Item.Id);
            AddKey(meta.ItemName, meta.FormId, meta.Item.Id);
            AddKey($"{meta.FormId}.{meta.Item.Id}", meta.FormId, meta.Item.Id);
            AddKey($"{meta.RawFormOid}.{meta.ItemOid}", meta.FormId, meta.Item.Id);
            AddKey($"{meta.RawFormOid}.{meta.ItemName}", meta.FormId, meta.Item.Id);
            AddKey($"{meta.RawFormOid}/{meta.ItemOid}", meta.FormId, meta.Item.Id);
            AddKey($"{meta.RawGroupOid}/{meta.ItemOid}", meta.FormId, meta.Item.Id);
            AddKey($"{meta.RawFormOid}/{meta.RawGroupOid}/{meta.ItemOid}", meta.FormId, meta.Item.Id);
            if (formIdByOid.TryGetValue(meta.RawFormOid, out var mappedFormId))
            {
                AddKey($"{mappedFormId}.{meta.ItemOid}", meta.FormId, meta.Item.Id);
            }
        }

        return map;
    }

    private static string ResolveVariableForForm(
        string rawToken,
        string targetFormId,
        IReadOnlyDictionary<string, List<(string FormId, string ItemId)>> lookup)
    {
        var cleaned = rawToken.Trim().TrimStart('/');
        if (lookup.TryGetValue(cleaned, out var candidates) && candidates.Count > 0)
        {
            var sameForm = candidates.FirstOrDefault(c => string.Equals(c.FormId, targetFormId, StringComparison.Ordinal));
            if (sameForm.ItemId is not null)
            {
                return sameForm.ItemId;
            }

            var sibling = candidates[0];
            var qualified = $"{sibling.FormId}.{sibling.ItemId}";
            return qualified.Length <= FindingTarget.MaxNameLength ? qualified : sibling.ItemId;
        }

        var slashIdx = cleaned.LastIndexOf('/');
        if (slashIdx >= 0 && slashIdx + 1 < cleaned.Length)
        {
            return ResolveVariableForForm(cleaned[(slashIdx + 1)..], targetFormId, lookup);
        }

        return cleaned.Length <= FindingTarget.MaxNameLength ? cleaned : cleaned[..FindingTarget.MaxNameLength];
    }

    private static string? ResolvePrimaryFormForStandaloneCondition(
        IReadOnlyList<string> referencedVariables,
        IReadOnlyList<OdmImportedItemMeta> importedItems,
        IReadOnlyDictionary<string, List<(string FormId, string ItemId)>> lookup)
    {
        foreach (var rawVar in referencedVariables)
        {
            var cleaned = rawVar.Trim().TrimStart('/');
            if (!cleaned.Contains('/') && lookup.TryGetValue(cleaned, out var unqualified) && unqualified.Count > 0)
            {
                return unqualified[0].FormId;
            }
        }

        foreach (var rawVar in referencedVariables)
        {
            var cleaned = rawVar.Trim().TrimStart('/');
            if (lookup.TryGetValue(cleaned, out var candidates) && candidates.Count > 0)
            {
                return candidates[0].FormId;
            }
        }

        return importedItems.FirstOrDefault()?.FormId;
    }
}
