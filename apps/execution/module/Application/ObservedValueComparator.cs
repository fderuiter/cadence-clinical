using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Cadence.Kernel.Evidence;
using Cadence.Kernel.Validation;

namespace Cadence.Modules.Execution.Application;

internal static class ObservedValueComparator
{
    public static bool AreObservedValuesUnchanged(
        ValidationFindingView finding,
        CollectionRuleInput target,
        IReadOnlyList<CollectionRuleInput> enrollmentForms)
    {
        if (finding.Observed.Count == 0)
        {
            return true;
        }

        var siblings = enrollmentForms.Count > 0 ? enrollmentForms : [target];
        var currentValues = BuildComparableValues(target, siblings);
        foreach (var expected in finding.Observed)
        {
            currentValues.TryGetValue(expected.Variable, out var actual);
            if (!string.Equals(expected.Value, actual, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static Dictionary<string, string?> BuildComparableValues(
        CollectionRuleInput target,
        IReadOnlyList<CollectionRuleInput> enrollmentForms)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        var recordedByItem = target.Values.ToDictionary(v => v.ItemId, v => (ObservationValue?)v.Value, StringComparer.Ordinal);
        foreach (var itemId in target.Items.Select(i => i.Id).Concat(recordedByItem.Keys).Distinct(StringComparer.Ordinal))
        {
            recordedByItem.TryGetValue(itemId, out var raw);
            var formatted = FormatObservedValue(raw);
            values[itemId] = formatted;
            values[$"{target.CollectionSpecId}.{itemId}"] = formatted;
        }

        var latestBySpec = enrollmentForms
            .Where(f => f.CollectionInstanceId != target.CollectionInstanceId
                && !string.Equals(f.CollectionSpecId, target.CollectionSpecId, StringComparison.Ordinal))
            .GroupBy(f => f.CollectionSpecId, StringComparer.Ordinal)
            .Select(g => g.OrderBy(f => f.OpenedAt).ThenBy(f => f.CollectionInstanceId).Last());
        foreach (var sibling in latestBySpec)
        {
            var siblingRecorded = sibling.Values.ToDictionary(v => v.ItemId, v => (ObservationValue?)v.Value, StringComparer.Ordinal);
            foreach (var siblingItemId in sibling.Items.Select(i => i.Id).Concat(siblingRecorded.Keys).Distinct(StringComparer.Ordinal))
            {
                siblingRecorded.TryGetValue(siblingItemId, out var raw);
                var formatted = FormatObservedValue(raw);
                values[$"{sibling.CollectionSpecId}.{siblingItemId}"] = formatted;
                values.TryAdd(siblingItemId, formatted);
            }
        }

        return values;
    }

    private static string? FormatObservedValue(ObservationValue? value)
    {
        var raw = value switch
        {
            null or MissingValue => null,
            QuantityValue q => q.Value.ToString(CultureInfo.InvariantCulture),
            BooleanValue b => b.Value ? "true" : "false",
            CodedValue c => c.Code,
            TextValue t => t.Value,
            DateTimeValue d => d.Value,
            _ => value.ToDisplayString(),
        };
        if (raw is null)
        {
            return null;
        }

        return raw.Length <= ValidationDetection.MaxTextLength
            ? raw
            : string.Concat(raw.AsSpan(0, ValidationDetection.MaxTextLength - 1), "…");
    }
}
