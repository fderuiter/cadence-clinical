using System;
using System.Collections.Generic;

using Cadence.Kernel.Validation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;

namespace Cadence.Rules.Evaluation;

/// <summary>
/// Evaluates rule packs over records (spec §10.1, K-RULE-3). This is the one evaluation port: EDC edit checks,
/// standards conformance, ADaM checks and quality indicators all call it, so a replacement engine (the Rust runtime
/// seam) changes one registration and no caller. An implementation must reproduce the semantics documented on
/// <see cref="RuleExpressionCompiler"/> and must be deterministic: the same rule versions over the same records in the
/// same context give the same violations in the same order (K-RULE-4).
/// </summary>
public interface IRuleEvaluator
{
    /// <summary>
    /// Checks every record against every rule of <paramref name="pack"/> that applies in <paramref name="context"/>.
    /// Violations come out in record order, then in the pack's rule order.
    /// </summary>
    IReadOnlyList<RuleViolation> Evaluate(RulePack pack, RuleEvaluationContext context, IEnumerable<RuleRecord> records);

    /// <summary>
    /// Streams records from <paramref name="records"/> against every rule of <paramref name="pack"/> that applies in
    /// <paramref name="context"/>, emitting violations in record order, then in the pack's rule order, to
    /// <paramref name="sink"/> without buffering a full violation list.
    /// </summary>
    void Evaluate(RulePack pack, RuleEvaluationContext context, IRuleRecordCursor records, IRuleViolationSink sink)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(sink);

        var referencedVariables = CollectPackVariables(pack);
        var materialized = new List<RuleRecord>();
        while (records.MoveNext())
        {
            materialized.Add(records.MaterializeRecord(referencedVariables));
        }

        var violations = Evaluate(pack, context, materialized);
        RuleObservedValue[]? scratch = null;
        for (var i = 0; i < violations.Count; i++)
        {
            var violation = violations[i];
            var observedCount = violation.Observed.Count;
            if (observedCount == 0)
            {
                sink.OnViolation(new RuleViolationHit(
                    violation.RuleId,
                    violation.RuleVersion,
                    violation.Severity,
                    violation.Dataset,
                    violation.RecordKey,
                    violation.Explanation,
                    ReadOnlySpan<RuleObservedValue>.Empty));
                continue;
            }

            if (scratch is null || scratch.Length < observedCount)
            {
                scratch = new RuleObservedValue[observedCount];
            }

            var slice = scratch.AsSpan(0, observedCount);
            for (var j = 0; j < observedCount; j++)
            {
                var item = violation.Observed[j];
                slice[j] = new RuleObservedValue(item.Variable, item.Value);
            }

            try
            {
                sink.OnViolation(new RuleViolationHit(
                    violation.RuleId,
                    violation.RuleVersion,
                    violation.Severity,
                    violation.Dataset,
                    violation.RecordKey,
                    violation.Explanation,
                    slice));
            }
            finally
            {
                slice.Clear();
            }
        }
    }

    private static string[] CollectPackVariables(RulePack pack)
    {
        if (pack.Rules.Count == 0)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < pack.Rules.Count; i++)
        {
            var variables = RuleExpressionJson.Variables(pack.Rules[i].Condition);
            for (var j = 0; j < variables.Count; j++)
            {
                seen.Add(variables[j]);
            }
        }

        if (seen.Count == 0)
        {
            return [];
        }

        var result = new string[seen.Count];
        seen.CopyTo(result);
        return result;
    }
}

/// <summary>
/// Forward-only cursor over records for streaming rule evaluation. Implementations may reuse internal state across
/// <see cref="MoveNext"/> calls; callers must not retain the cursor reference past each step.
/// </summary>
public interface IRuleRecordCursor : IRuleVariables
{
    /// <summary>Advances to the next record and returns <see langword="true"/>, or <see langword="false"/> at the end.</summary>
    bool MoveNext();

    /// <summary>The current record's dataset, domain, form or record type; compared with a rule's dataset.</summary>
    string Dataset { get; }

    /// <summary>Identifies the current record in the caller's terms; copied to each violation.</summary>
    string Key { get; }

    /// <summary>
    /// Materializes an immutable <see cref="RuleRecord"/> snapshot for the current cursor row when evaluating through a
    /// custom <see cref="IRuleEvaluator"/> that implements only the enumerable overload.
    /// </summary>
    RuleRecord MaterializeRecord(ReadOnlySpan<string> referencedVariables)
    {
        var values = new Dictionary<string, RuleValue>(referencedVariables.Length, StringComparer.Ordinal);
        for (var i = 0; i < referencedVariables.Length; i++)
        {
            var variable = referencedVariables[i];
            var value = this[variable];
            if (!value.IsNull)
            {
                values[variable] = value;
            }
        }

        return new RuleRecord(Dataset, Key, values);
    }
}

/// <summary>Receives streaming rule violations in record order, then in rule order (K-RULE-4).</summary>
public interface IRuleViolationSink
{
    /// <summary>
    /// Handles one violation hit. <paramref name="violation"/> and its <see cref="RuleViolationHit.Observed"/> span are
    /// valid only for the duration of this call.
    /// </summary>
    void OnViolation(RuleViolationHit violation);
}

/// <summary>A variable and value read by a violated rule during streaming evaluation.</summary>
/// <param name="Variable">The variable name read by the rule.</param>
/// <param name="Value">The value observed on the record.</param>
public readonly record struct RuleObservedValue(string Variable, RuleValue Value);

/// <summary>
/// Stack-only view of a rule violation emitted to an <see cref="IRuleViolationSink"/>. <see cref="Observed"/> points to
/// a transient buffer scoped to the callback so mutable scratch state cannot escape across records.
/// </summary>
public readonly ref struct RuleViolationHit
{
    /// <summary>Creates a transient violation hit.</summary>
    public RuleViolationHit(
        string ruleId,
        int ruleVersion,
        ValidationSeverity severity,
        string dataset,
        string recordKey,
        string explanation,
        ReadOnlySpan<RuleObservedValue> observed)
    {
        RuleId = ruleId ?? throw new ArgumentNullException(nameof(ruleId));
        RuleVersion = ruleVersion;
        Severity = severity;
        Dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        RecordKey = recordKey ?? throw new ArgumentNullException(nameof(recordKey));
        Explanation = explanation ?? throw new ArgumentNullException(nameof(explanation));
        Observed = observed;
    }

    /// <summary>The violated rule identifier.</summary>
    public string RuleId { get; }

    /// <summary>The violated rule version.</summary>
    public int RuleVersion { get; }

    /// <summary>The rule's validation severity.</summary>
    public ValidationSeverity Severity { get; }

    /// <summary>The dataset, domain, form or record type of the violating record.</summary>
    public string Dataset { get; }

    /// <summary>The record key in the caller's terms.</summary>
    public string RecordKey { get; }

    /// <summary>The rule's explanation text.</summary>
    public string Explanation { get; }

    /// <summary>The values read by the rule on this record, valid only during the sink callback.</summary>
    public ReadOnlySpan<RuleObservedValue> Observed { get; }

    /// <summary>Materializes an immutable <see cref="RuleViolation"/> copy of this hit.</summary>
    public RuleViolation ToViolation()
    {
        ObservedValue[] observed;
        if (Observed.Length == 0)
        {
            observed = [];
        }
        else
        {
            observed = new ObservedValue[Observed.Length];
            for (var i = 0; i < Observed.Length; i++)
            {
                var item = Observed[i];
                observed[i] = new ObservedValue(item.Variable, item.Value);
            }
        }

        return new RuleViolation(RuleId, RuleVersion, Severity, Dataset, RecordKey, Explanation, observed);
    }
}

internal sealed class RuleRecordListCursor : IRuleRecordCursor
{
    private readonly IReadOnlyList<RuleRecord> _records;
    private readonly HashSet<string>? _datasetFilter;
    private readonly int _end;
    private int _index;
    private RuleRecord? _current;

    public RuleRecordListCursor(IReadOnlyList<RuleRecord> records, HashSet<string>? datasetFilter = null)
    {
        _records = records;
        _datasetFilter = datasetFilter;
        _index = -1;
        _end = records.Count;
    }

    public RuleRecordListCursor(IReadOnlyList<RuleRecord> records, int offset, int count)
    {
        _records = records;
        _datasetFilter = null;
        _index = offset - 1;
        _end = offset + count;
    }

    public string Dataset => _current!.Dataset;

    public string Key => _current!.Key;

    public RuleValue this[string name] => _current![name];

    public bool MoveNext()
    {
        while (++_index < _end)
        {
            var candidate = _records[_index];
            if (_datasetFilter is null || _datasetFilter.Contains(candidate.Dataset))
            {
                _current = candidate;
                return true;
            }
        }

        _current = null;
        return false;
    }

    public RuleRecord MaterializeRecord(ReadOnlySpan<string> referencedVariables) => _current!;
}

/// <summary>
/// What an evaluation is for. A rule applies when its stage equals <see cref="Stage"/>, its effective period contains
/// <see cref="AsOf"/>, and each of its standard, standard version and agency is null or equal to the context's. The
/// caller supplies <see cref="AsOf"/>; evaluation never reads the clock.
/// </summary>
public sealed record RuleEvaluationContext(ValidationStage Stage, DateOnly AsOf)
{
    /// <summary>The standard being checked (for example <c>SDTMIG</c>); rules naming another standard do not apply.</summary>
    public string? Standard { get; init; }

    /// <summary>The standard version being checked; rules naming another version do not apply.</summary>
    public string? StandardVersion { get; init; }

    /// <summary>The agency the data is prepared for (for example <c>FDA</c>); rules naming another agency do not apply.</summary>
    public string? Agency { get; init; }
}

/// <summary>
/// One record to check: the dataset (or domain, form or record type) it belongs to, a key that identifies it in the
/// caller's terms (a row number, an observation id, a design element reference), and its variables.
/// </summary>
public sealed class RuleRecord(string dataset, string key, IReadOnlyDictionary<string, RuleValue> values) : IRuleVariables
{
    /// <summary>The dataset, domain, form or record type; compared with a rule's dataset.</summary>
    public string Dataset { get; } = dataset ?? throw new ArgumentNullException(nameof(dataset));

    /// <summary>Identifies the record in the caller's terms; copied to each violation.</summary>
    public string Key { get; } = key ?? throw new ArgumentNullException(nameof(key));

    /// <summary>The record's variables by name.</summary>
    public IReadOnlyDictionary<string, RuleValue> Values { get; } = values ?? throw new ArgumentNullException(nameof(values));

    /// <summary>The value of <paramref name="name"/>, or <see cref="RuleValue.Null"/> when the record has no such variable.</summary>
    public RuleValue this[string name] => Values.TryGetValue(name, out var value) ? value : RuleValue.Null;
}

/// <summary>A record's value of a variable the violated rule reads.</summary>
public sealed record ObservedValue(string Variable, RuleValue Value);

/// <summary>
/// A record that violates a rule: which rule and version, the record, and the values the rule read, so a finding can
/// show what was observed. <see cref="Explanation"/> is the rule's own text.
/// </summary>
public sealed record RuleViolation(
    string RuleId,
    int RuleVersion,
    ValidationSeverity Severity,
    string Dataset,
    string RecordKey,
    string Explanation,
    IReadOnlyList<ObservedValue> Observed);
