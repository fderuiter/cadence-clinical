using System;
using System.Collections.Generic;

using Cadence.Kernel;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Validation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;

namespace Cadence.Rules.Evaluation;

internal static class CollectionRuleRecordBuilder
{
    internal static RuleRecord BuildRuleRecordCore<TForm>(
        TForm target,
        IReadOnlyList<TForm> enrollmentForms,
        Func<TForm, CollectionInstanceId> getId,
        Func<TForm, string> getSpecId,
        Func<TForm, DateTimeOffset> getOpenedAt,
        Dictionary<string, RuleValue> targetOwnValues,
        Func<TForm, Dictionary<string, RuleValue>> buildSiblingOwnValues)
    {
        var targetId = getId(target);
        var targetSpecId = getSpecId(target);
        var latestBySpec = SelectLatestFormsBySpec(enrollmentForms, getId, getSpecId, getOpenedAt, targetId, targetSpecId);
        var values = new Dictionary<string, RuleValue>(targetOwnValues.Count * 2 + latestBySpec.Count * 8, StringComparer.Ordinal);

        foreach (var (itemId, converted) in targetOwnValues)
        {
            values[itemId] = converted;
            var qualified = $"{targetSpecId}.{itemId}";
            if (qualified.Length <= FindingTarget.MaxNameLength)
            {
                values[qualified] = converted;
            }
        }

        for (var i = 0; i < latestBySpec.Count; i++)
        {
            var sibling = latestBySpec[i];
            var siblingSpecId = getSpecId(sibling);
            var siblingOwnValues = buildSiblingOwnValues(sibling);
            foreach (var (siblingItemId, converted) in siblingOwnValues)
            {
                var qualified = $"{siblingSpecId}.{siblingItemId}";
                if (qualified.Length <= FindingTarget.MaxNameLength)
                {
                    values[qualified] = converted;
                }

                if (!values.ContainsKey(siblingItemId) && siblingItemId.Length <= FindingTarget.MaxNameLength)
                {
                    values[siblingItemId] = converted;
                }
            }
        }

        return new RuleRecord(targetSpecId, targetId.Value.ToString("D"), values);
    }

    internal static Dictionary<string, RuleValue> BuildStaticOwnItemValues(
        CollectionInstanceRecord form,
        IReadOnlyDictionary<string, ObservationValue?>? draftOverlay,
        Func<CollectionInstanceRecord, string, ObservationValue?, bool>? isBlindedVariable)
    {
        var recordedByItem = new Dictionary<string, ObservationValue?>(
            form.Values.Count + (draftOverlay?.Count ?? 0),
            StringComparer.Ordinal);
        for (var i = 0; i < form.Values.Count; i++)
        {
            var recorded = form.Values[i];
            recordedByItem[recorded.ItemId] = recorded.Value;
        }

        if (draftOverlay is not null)
        {
            foreach (var (itemId, draftValue) in draftOverlay)
            {
                recordedByItem[itemId] = draftValue;
            }
        }

        var ownValues = new Dictionary<string, RuleValue>(form.Items.Count + recordedByItem.Count, StringComparer.Ordinal);
        for (var i = 0; i < form.Items.Count; i++)
        {
            var itemId = form.Items[i].Id;
            if (ownValues.ContainsKey(itemId))
            {
                continue;
            }

            recordedByItem.TryGetValue(itemId, out var raw);
            ownValues.Add(
                itemId,
                isBlindedVariable is not null && isBlindedVariable(form, itemId, raw) ? RuleValue.Null : CollectionValidationService.ToRuleValue(raw));
        }

        foreach (var (itemId, raw) in recordedByItem)
        {
            if (ownValues.ContainsKey(itemId))
            {
                continue;
            }

            ownValues.Add(
                itemId,
                isBlindedVariable is not null && isBlindedVariable(form, itemId, raw) ? RuleValue.Null : CollectionValidationService.ToRuleValue(raw));
        }

        return ownValues;
    }

    internal static Dictionary<string, RuleValue> BuildStaticOwnItemValues(
        CollectionRuleInput form,
        IReadOnlyDictionary<string, ObservationValue?>? draftOverlay,
        Func<CollectionRuleInput, string, ObservationValue?, bool>? isBlindedVariable)
    {
        var recordedByItem = new Dictionary<string, ObservationValue?>(
            form.Values.Count + (draftOverlay?.Count ?? 0),
            StringComparer.Ordinal);
        for (var i = 0; i < form.Values.Count; i++)
        {
            var recorded = form.Values[i];
            recordedByItem[recorded.ItemId] = recorded.Value;
        }

        if (draftOverlay is not null)
        {
            foreach (var (itemId, draftValue) in draftOverlay)
            {
                recordedByItem[itemId] = draftValue;
            }
        }

        var ownValues = new Dictionary<string, RuleValue>(form.Items.Count + recordedByItem.Count, StringComparer.Ordinal);
        for (var i = 0; i < form.Items.Count; i++)
        {
            var itemId = form.Items[i].Id;
            if (ownValues.ContainsKey(itemId))
            {
                continue;
            }

            recordedByItem.TryGetValue(itemId, out var raw);
            ownValues.Add(
                itemId,
                isBlindedVariable is not null && isBlindedVariable(form, itemId, raw) ? RuleValue.Null : CollectionValidationService.ToRuleValue(raw));
        }

        foreach (var (itemId, raw) in recordedByItem)
        {
            if (ownValues.ContainsKey(itemId))
            {
                continue;
            }

            ownValues.Add(
                itemId,
                isBlindedVariable is not null && isBlindedVariable(form, itemId, raw) ? RuleValue.Null : CollectionValidationService.ToRuleValue(raw));
        }

        return ownValues;
    }

    internal static List<TForm> SelectLatestFormsBySpec<TForm>(
        IReadOnlyList<TForm> enrollmentForms,
        Func<TForm, CollectionInstanceId> getId,
        Func<TForm, string> getSpecId,
        Func<TForm, DateTimeOffset> getOpenedAt,
        CollectionInstanceId? excludeFormId = null,
        string? excludeSpecId = null)
    {
        var latest = new List<TForm>();
        if (enrollmentForms.Count == 0)
        {
            return latest;
        }

        var indexBySpec = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < enrollmentForms.Count; i++)
        {
            var candidate = enrollmentForms[i];
            var candidateId = getId(candidate);
            if (excludeFormId is { } excludedId && candidateId == excludedId)
            {
                continue;
            }

            var candidateSpecId = getSpecId(candidate);
            if (excludeSpecId is not null && string.Equals(candidateSpecId, excludeSpecId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!indexBySpec.TryGetValue(candidateSpecId, out var existingIndex))
            {
                indexBySpec.Add(candidateSpecId, latest.Count);
                latest.Add(candidate);
            }
            else if (IsNewerOrSameSibling(getOpenedAt(candidate), candidateId, getOpenedAt(latest[existingIndex]), getId(latest[existingIndex])))
            {
                latest[existingIndex] = candidate;
            }
        }

        return latest;
    }

    internal static EnrollmentSiblingIndex BuildSiblingIndex<TForm>(
        List<TForm> latestBySpec,
        Func<TForm, string> getSpecId,
        Func<TForm, Dictionary<string, RuleValue>> resolveOwnValues)
    {
        if (latestBySpec.Count == 0)
        {
            return EnrollmentSiblingIndex.Empty;
        }

        var qualifiedValues = new Dictionary<string, QualifiedSiblingEntry>(latestBySpec.Count * 8, StringComparer.Ordinal);
        var unqualifiedValues = new Dictionary<string, UnqualifiedSiblingEntry>(latestBySpec.Count * 8, StringComparer.Ordinal);

        for (var i = 0; i < latestBySpec.Count; i++)
        {
            var sibling = latestBySpec[i];
            var siblingSpec = getSpecId(sibling);
            var ownValues = resolveOwnValues(sibling);
            foreach (var (itemId, converted) in ownValues)
            {
                var qualified = $"{siblingSpec}.{itemId}";
                if (qualified.Length <= FindingTarget.MaxNameLength)
                {
                    if (qualifiedValues.TryGetValue(qualified, out var existingQualified))
                    {
                        qualifiedValues[qualified] = new QualifiedSiblingEntry(siblingSpec, converted, HasPrior: true, existingQualified.Value);
                    }
                    else
                    {
                        qualifiedValues.Add(qualified, new QualifiedSiblingEntry(siblingSpec, converted, HasPrior: false, default));
                    }
                }

                if (itemId.Length <= FindingTarget.MaxNameLength)
                {
                    if (!unqualifiedValues.TryGetValue(itemId, out var existingUnqualified))
                    {
                        unqualifiedValues.Add(itemId, new UnqualifiedSiblingEntry(siblingSpec, converted, HasSecond: false, default));
                    }
                    else if (!existingUnqualified.HasSecond)
                    {
                        unqualifiedValues[itemId] = existingUnqualified with { HasSecond = true, SecondValue = converted };
                    }
                }
            }
        }

        return new EnrollmentSiblingIndex(qualifiedValues, unqualifiedValues);
    }

    internal static Dictionary<string, Rule> BuildRulesById(RulePack pack)
    {
        var map = new Dictionary<string, Rule>(pack.Rules.Count, StringComparer.Ordinal);
        for (var i = 0; i < pack.Rules.Count; i++)
        {
            var rule = pack.Rules[i];
            map[rule.Id] = rule;
        }

        return map;
    }

    internal static string? ResolvePrimaryItemId(
        CollectionInstanceRecord target,
        Dictionary<string, RuleValue> targetOwnValues,
        ReadOnlySpan<RuleObservedValue> observed)
    {
        var specId = target.CollectionSpecId;
        for (var i = 0; i < observed.Length; i++)
        {
            var variable = observed[i].Variable;
            if (targetOwnValues.ContainsKey(variable))
            {
                return variable;
            }

            if (TryStripSpecPrefix(variable, specId, out var stripped) && targetOwnValues.ContainsKey(stripped))
            {
                return stripped;
            }
        }

        if (target.Items.Count > 0)
        {
            return target.Items[0].Id;
        }

        var fallback = observed.Length > 0 ? observed[0].Variable : null;
        return fallback is not null && fallback.Length <= FindingTarget.MaxNameLength ? fallback : null;
    }

    internal static bool TryStripSpecPrefix(string variable, string specId, out string stripped)
    {
        if (variable.Length > specId.Length + 1
            && variable.StartsWith(specId, StringComparison.Ordinal)
            && variable[specId.Length] == '.')
        {
            stripped = variable[(specId.Length + 1)..];
            return true;
        }

        stripped = string.Empty;
        return false;
    }

    private static bool IsNewerOrSameSibling(
        DateTimeOffset candidateOpenedAt,
        CollectionInstanceId candidateId,
        DateTimeOffset currentOpenedAt,
        CollectionInstanceId currentId)
    {
        var openedComparison = candidateOpenedAt.CompareTo(currentOpenedAt);
        if (openedComparison != 0)
        {
            return openedComparison > 0;
        }

        return Comparer<CollectionInstanceId>.Default.Compare(candidateId, currentId) >= 0;
    }
}
