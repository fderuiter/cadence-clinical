using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

using Cadence.Kernel.Explanations;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;

namespace Cadence.Rules.Indicators;

/// <summary>
/// Evaluates a released indicator set over the records of its populations (ADR-2488, spec §10.4). Each indicator's
/// counting rule runs through <see cref="IRuleEvaluator"/>, the one evaluation port (K-RULE-3), so an indicator and an
/// edit check share their semantics and a replacement engine changes both. Evaluation is pure: the same set version,
/// scope, as-of and records always give the same results and hashes, in whatever order the records arrive (K-RULE-4).
/// </summary>
public sealed class IndicatorEvaluator(IRuleEvaluator rules)
{
    /// <summary>The most contributing objects a result lists; <see cref="IndicatorResult.Counted"/> has the full count.</summary>
    public const int MaxContributors = 100;

    /// <summary>The explanation condition code of a threshold.</summary>
    public const string ThresholdCondition = "indicator.threshold";

    // The port filters rules by stage. An indicator is not a validation stage, so each counting rule is given this
    // stage and evaluated with a context of the same stage: the filter always passes and plays no part in the result.
    private const ValidationStage CountingStage = ValidationStage.Collection;

    private readonly ConditionalWeakTable<IndicatorSet, RulePack> _packs = [];

    /// <summary>
    /// The value of every indicator in <paramref name="set"/> effective at the context's as-of, in the set's order.
    /// <paramref name="populations"/> holds the records of each population those indicators name, read within the
    /// context's scope at its as-of.
    /// </summary>
    /// <exception cref="ArgumentException">A population is missing, or holds the same object twice.</exception>
    public IReadOnlyList<IndicatorResult> Evaluate(
        LoadedIndicatorSet set, IndicatorContext context, IReadOnlyDictionary<string, IReadOnlyList<IndicatorRecord>> populations)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(populations);

        var indicators = set.Set.Indicators.Where(i => i.Effective.Contains(context.AsOf)).ToList();
        var missing = indicators.Select(i => i.Population).Distinct(StringComparer.Ordinal).Where(p => !populations.ContainsKey(p)).ToList();
        if (missing.Count > 0)
        {
            throw new ArgumentException($"No records were given for the population(s) {string.Join(", ", missing)}.", nameof(populations));
        }

        var pack = _packs.GetValue(set.Set, CountingRules);
        var evaluation = new RuleEvaluationContext(CountingStage, context.AsOf);
        var read = new Dictionary<string, Population>(StringComparer.Ordinal);
        foreach (var name in indicators.Select(i => i.Population).Distinct(StringComparer.Ordinal))
        {
            var records = Sorted(name, populations[name]);
            var sink = new IndicatorCountSink();
            rules.Evaluate(pack, evaluation, new IndicatorPopulationCursor(name, records), sink);
            read.Add(name, new Population(records, sink.Counts));
        }

        return [.. indicators.Select(indicator => Result(set, indicator, context, read[indicator.Population]))];
    }

    private static IndicatorResult Result(LoadedIndicatorSet set, Indicator indicator, IndicatorContext context, Population population)
    {
        var reference = new IndicatorVersionRef(set.Id, set.Key, set.Version, set.ContentHash, indicator.Id, indicator.Version);
        var hits = population.Counted.GetValueOrDefault(indicator.Id);
        var total = population.Records.Count;
        var counted = hits?.Count ?? 0;
        decimal? value = indicator.Measure switch
        {
            IndicatorMeasure.Count => counted,
            _ when total == 0 => null,
            _ => Math.Round(counted * 100m / total, 2, MidpointRounding.ToEven),
        };
        var breached = indicator.Threshold?.IsBreachedBy(value) ?? false;
        IReadOnlyList<NodeRef> contributors = hits is null || hits.ContributorKeys.Count == 0
            ? []
            : ResolveContributors(population.Records, hits.ContributorKeys);

        var inputHash = ContentHash.Compute(InputDocument(reference, indicator, context, population).ToJsonString());
        var resultHash = ContentHash.Compute(new JsonObject
        {
            ["input_hash"] = inputHash,
            ["population"] = total,
            ["counted"] = counted,
            ["measure"] = indicator.Measure.ToString(),
            ["value"] = value,
            ["breached"] = breached,
            ["contributors"] = new JsonArray([.. contributors.Select(c => (JsonNode)c.ToString())]),
        }.ToJsonString());

        return new IndicatorResult(
            reference,
            indicator.Title,
            indicator.Area,
            indicator.Kind,
            context.Scope,
            context.AsOf,
            total,
            counted,
            indicator.Measure,
            value,
            indicator.Threshold,
            breached,
            contributors,
            inputHash,
            resultHash,
            Explain(set, reference, indicator, context, total, counted, value, breached, contributors));
    }

    private static Explanation Explain(
        LoadedIndicatorSet set,
        IndicatorVersionRef reference,
        Indicator indicator,
        IndicatorContext context,
        int total,
        int counted,
        decimal? value,
        bool breached,
        IReadOnlyList<NodeRef> contributors)
    {
        var claim = value switch
        {
            null => $"{indicator.Title} has no value, because there are no {indicator.Population} records.",
            _ when indicator.Measure == IndicatorMeasure.Percent => $"{indicator.Title} is {Format(value.Value)}% ({counted} of {total}).",
            _ => $"{indicator.Title} is {counted}.",
        };
        var steps = new List<ExplanationStep>
        {
            new($"{total} {indicator.Population} record(s) were read within {context.Scope} as of {context.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.", []),
            new($"{counted} of them are counted by indicator {indicator.Id} version {indicator.Version} of {set.Key} version {set.Version}: {indicator.Explanation}", contributors),
        };
        var conditions = new List<EvaluatedCondition>();
        if (indicator.Threshold is { } threshold)
        {
            var side = threshold.Direction == ThresholdDirection.Above ? "above" : "below";
            var description = $"The value is not {side} the limit of {Format(threshold.Limit)}.";
            conditions.Add(breached
                ? EvaluatedCondition.Unsatisfied(ThresholdCondition, description, [.. contributors])
                : EvaluatedCondition.Satisfied(ThresholdCondition, description));
        }

        return new Explanation(NodeRef.For(set.Id), claim, reference.Producer, steps, conditions, []);
    }

    private static JsonObject InputDocument(IndicatorVersionRef reference, Indicator indicator, IndicatorContext context, Population population) => new()
    {
        ["set"] = reference.SetKey,
        ["set_version"] = reference.SetVersion,
        ["set_content_hash"] = reference.SetContentHash,
        ["indicator"] = indicator.Id,
        ["indicator_version"] = indicator.Version,
        ["scope"] = context.Scope.ToString(),
        ["as_of"] = context.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["population"] = indicator.Population,
        ["records"] = new JsonArray([.. population.Records.Select(r => (JsonNode)new JsonObject
        {
            ["node"] = r.Key,
            ["values"] = new JsonObject(r.Record.Values
                .OrderBy(v => v.Key, StringComparer.Ordinal)
                .Select(v => KeyValuePair.Create(v.Key, ValueNode(v.Value)))),
        })]),
    };

    private static JsonNode? ValueNode(RuleValue value)
    {
        if (value.TryGetNumber(out var number))
        {
            return JsonValue.Create(number);
        }

        if (value.TryGetBoolean(out var boolean))
        {
            return JsonValue.Create(boolean);
        }

        return value.TryGetText(out var text) ? JsonValue.Create(text) : null;
    }

    private static List<(string Key, IndicatorRecord Record)> Sorted(string population, IReadOnlyList<IndicatorRecord> records)
    {
        var sorted = records.Select(r => (Key: r.Node.ToString(), Record: r)).OrderBy(r => r.Key, StringComparer.Ordinal).ToList();
        for (var i = 1; i < sorted.Count; i++)
        {
            if (string.Equals(sorted[i].Key, sorted[i - 1].Key, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Population {population} holds {sorted[i].Key} more than once.", nameof(records));
            }
        }

        return sorted;
    }

    private static RulePack CountingRules(IndicatorSet set) => new(
        set.Authority,
        set.Title,
        [.. set.Indicators.Select(i => new Rule(
            i.Id,
            i.Version,
            set.Authority,
            new SourceCitation(set.Title, i.Id, null),
            new RuleApplicability(CountingStage, null, null, null, i.Population),
            i.Threshold?.Severity ?? ValidationSeverity.Notice,
            i.Counts,
            i.Explanation,
            null,
            i.Effective))]);

    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static IReadOnlyList<NodeRef> ResolveContributors(
        List<(string Key, IndicatorRecord Record)> sortedRecords,
        List<string> contributorKeys)
    {
        var count = Math.Min(contributorKeys.Count, MaxContributors);
        var result = new NodeRef[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = FindNodeByKey(sortedRecords, contributorKeys[i]);
        }

        return result;
    }

    private static NodeRef FindNodeByKey(List<(string Key, IndicatorRecord Record)> sortedRecords, string key)
    {
        var lo = 0;
        var hi = sortedRecords.Count - 1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            var comparison = string.CompareOrdinal(sortedRecords[mid].Key, key);
            if (comparison == 0)
            {
                return sortedRecords[mid].Record.Node;
            }

            if (comparison < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        throw new KeyNotFoundException($"Record key '{key}' was not found in the evaluated population.");
    }

    private sealed record Population(List<(string Key, IndicatorRecord Record)> Records, Dictionary<string, IndicatorHits> Counted);

    private sealed class IndicatorHits
    {
        public int Count { get; set; }

        public List<string> ContributorKeys { get; } = [];
    }

    private sealed class IndicatorPopulationCursor(
        string dataset,
        List<(string Key, IndicatorRecord Record)> records) : IRuleRecordCursor
    {
        private int _index = -1;
        private (string Key, IndicatorRecord Record) _current;

        public string Dataset => dataset;

        public string Key => _current.Key;

        public RuleValue this[string name] =>
            _current.Record.Values.TryGetValue(name, out var value) ? value : RuleValue.Null;

        public bool MoveNext()
        {
            if (++_index < records.Count)
            {
                _current = records[_index];
                return true;
            }

            _current = default;
            return false;
        }

        public RuleRecord MaterializeRecord(ReadOnlySpan<string> referencedVariables) =>
            new(dataset, _current.Key, _current.Record.Values);
    }

    private sealed class IndicatorCountSink : IRuleViolationSink
    {
        public Dictionary<string, IndicatorHits> Counts { get; } = new(StringComparer.Ordinal);

        public void OnViolation(RuleViolationHit violation)
        {
            if (!Counts.TryGetValue(violation.RuleId, out var hits))
            {
                hits = new IndicatorHits();
                Counts.Add(violation.RuleId, hits);
            }

            hits.Count++;
            if (hits.ContributorKeys.Count < MaxContributors)
            {
                hits.ContributorKeys.Add(violation.RecordKey);
            }
        }
    }
}
